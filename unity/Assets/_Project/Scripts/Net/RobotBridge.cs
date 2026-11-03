using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ARaki.Net
{
    /// <summary>
    /// Jetson'daki sürüş köprüsüyle konuşur (ws://robot:8080/kontrol). Tarayıcıdaki sürüş sayfasıyla
    /// aynı JSON mesajlarını kullanır. Bağlantı koparsa kendiliğinden yeniden bağlanır.
    ///
    /// Sürüş komutları kuyruğa girmez: tek bir "son komut" yuvası vardır, yeni komut eskisinin üstüne
    /// yazılır. Ağ takılırsa birikmiş eski komutlar sinyal dönünce robotu oynatmaz. LED, korna gibi
    /// tek seferlik komutlar ise kaybolmasın diye ayrı, küçük bir kuyruktan gider.
    /// Unity'ye bağlı değildir; ana iş parçacığında olmayan işler (alma, gönderme) arka planda döner.
    /// </summary>
    public sealed class RobotBridge : IDisposable
    {
        public const int Port = 8080;

        public string Host { get; }
        public bool Connected => socket != null && socket.State == WebSocketState.Open;
        /// <summary>Son ping-pong gidiş dönüş süresi (ms).</summary>
        public double RoundTripMs { get; private set; } = -1;
        /// <summary>Son pong'dan beri geçen süre (ms); büyükse bağlantı zayıftır.</summary>
        public double SincePongMs => Connected ? Now() - lastPong : double.PositiveInfinity;
        public string LastError { get; private set; } = "";

        /// <summary>Köprüden gelen ham JSON mesajları (durum, pong). Ana iş parçacığında boşaltılır.</summary>
        public readonly ConcurrentQueue<string> Incoming = new ConcurrentQueue<string>();

        static readonly Stopwatch Clock = Stopwatch.StartNew();
        public static double Now() => Clock.Elapsed.TotalMilliseconds;

        readonly CancellationTokenSource cts = new CancellationTokenSource();
        readonly ConcurrentQueue<string> oneShot = new ConcurrentQueue<string>();
        readonly SemaphoreSlim wake = new SemaphoreSlim(0);
        string driveSlot;               // gönderilmeyi bekleyen en son sürüş komutu
        readonly object slotLock = new object();
        ClientWebSocket socket;
        double lastPong;

        public RobotBridge(string host)
        {
            Host = host;
            Task.Run(RunAsync);
        }

        /// <summary>Sürüş komutu (-255..255). Bekleyen eski komutun yerine geçer.</summary>
        public void Drive(int left, int right)
        {
            string json = "{\"t\":\"surus\",\"sol\":" + left + ",\"sag\":" + right +
                          ",\"z\":" + Now().ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "}";
            lock (slotLock) driveSlot = json;
            wake.Release();
        }

        /// <summary>Tek seferlik komut: {"t":"led","acik":true} gibi.</summary>
        public void Send(string json)
        {
            oneShot.Enqueue(json);
            wake.Release();
        }

        public void Dispose()
        {
            cts.Cancel();
            try { socket?.Abort(); } catch (Exception) { }
        }

        async Task RunAsync()
        {
            while (!cts.IsCancellationRequested)
            {
                using (var ws = new ClientWebSocket())
                {
                    try
                    {
                        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cts.Token))
                        {
                            timeout.CancelAfter(4000);
                            await ws.ConnectAsync(new Uri("ws://" + Host + ":" + Port + "/kontrol"), timeout.Token);
                        }
                        socket = ws;
                        lastPong = Now();
                        LastError = "";
                        var receiving = ReceiveAsync(ws);
                        var sending = SendLoopAsync(ws);
                        var pinging = PingLoopAsync();
                        await Task.WhenAny(receiving, sending, pinging);
                    }
                    catch (Exception e) when (!(e is OperationCanceledException && cts.IsCancellationRequested))
                    {
                        LastError = e.Message;
                    }
                    catch (OperationCanceledException) { }
                    finally
                    {
                        socket = null;
                        try { ws.Abort(); } catch (Exception) { }
                    }
                }
                if (!cts.IsCancellationRequested)
                    await Task.Delay(1000).ConfigureAwait(false);
            }
        }

        async Task ReceiveAsync(ClientWebSocket ws)
        {
            var buffer = new byte[8192];
            var text = new StringBuilder();
            while (ws.State == WebSocketState.Open && !cts.IsCancellationRequested)
            {
                var r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                if (r.MessageType == WebSocketMessageType.Close) return;
                text.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
                if (!r.EndOfMessage) continue;
                string msg = text.ToString();
                text.Clear();
                if (msg.StartsWith("{\"t\": \"pong\"") || msg.StartsWith("{\"t\":\"pong\""))
                {
                    lastPong = Now();
                    int z = msg.IndexOf("\"z\":", StringComparison.Ordinal);
                    if (z >= 0 && double.TryParse(msg.Substring(z + 4).Trim(' ', '}'),
                            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sent))
                        RoundTripMs = Now() - sent;
                    continue;
                }
                Incoming.Enqueue(msg);
                while (Incoming.Count > 20) Incoming.TryDequeue(out _);
            }
        }

        async Task SendLoopAsync(ClientWebSocket ws)
        {
            while (ws.State == WebSocketState.Open && !cts.IsCancellationRequested)
            {
                await wake.WaitAsync(cts.Token);
                while (oneShot.TryDequeue(out var msg)) await SendText(ws, msg);
                string drive;
                lock (slotLock) { drive = driveSlot; driveSlot = null; }
                if (drive != null) await SendText(ws, drive);
            }
        }

        async Task PingLoopAsync()
        {
            while (!cts.IsCancellationRequested && Connected)
            {
                Send("{\"t\":\"ping\",\"z\":" + Now().ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "}");
                await Task.Delay(500, cts.Token);
                // 5 sn hiç cevap yoksa bağlantı ölmüştür; yeniden kur
                if (Now() - lastPong > 5000) return;
            }
        }

        Task SendText(ClientWebSocket ws, string msg) =>
            ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(msg)), WebSocketMessageType.Text, true, cts.Token);
    }
}
