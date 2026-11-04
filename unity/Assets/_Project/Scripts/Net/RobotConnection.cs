using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ARaki.Net
{
    /// <summary>
    /// Uygulamanın robottan istediği her şey, tek pakette. 50 ms'de bir (ve bir şey değişince hemen) bütünüyle
    /// gönderilir; bir paket kaybolursa sonraki aynı bilgiyi taşır. Tek seferlik işler (selektör, melodi)
    /// sayaçla gider: köprü sayaç değişince bir kez yapar.
    /// </summary>
    public struct ControlState
    {
        public int Left, Right;             // -255..255
        public bool Led, Hazard, EngineSound, ReverseBeep;
        public bool Horn;                   // basılı tutuldukça
        public int HornStyle;               // 0 düz, 1 kalın, 2 tiz, 3 titrek iki ton, 4 dü-düt, 5 La Cucaracha
        public int FlashCount;              // selektör: her basışta bir artar
        public int MelodyCount, Melody;     // melodi: her istekte sayaç artar; 0 = sustur
        public int EffectCount, Effect;     // kısa ses efekti (vites, foto, kayıt): her istekte sayaç artar
        public bool AutoEffects;            // robotun kendiliğinden çaldığı açılış / bağlandı / koptu sesleri

        public bool SameAs(ControlState o) =>
            Left == o.Left && Right == o.Right && Led == o.Led && Hazard == o.Hazard && EngineSound == o.EngineSound &&
            ReverseBeep == o.ReverseBeep && Horn == o.Horn && HornStyle == o.HornStyle && FlashCount == o.FlashCount &&
            MelodyCount == o.MelodyCount && Melody == o.Melody && EffectCount == o.EffectCount && Effect == o.Effect &&
            AutoEffects == o.AutoEffects;
    }

    /// <summary>Köprünün durum cevabı. Alan adları köprüdeki JSON ile aynı.</summary>
    [Serializable]
    public class RobotStatus
    {
        [Serializable]
        public class Motor
        {
            public int sol, sag, sol_ma, sag_ma, melodi;
            public bool ariza, led, dortlu, motor_sesi, geri_bip;
            public int sikisma;   // akım koruması devrede: 1 sol, 2 sağ, 3 ikisi
        }

        public string t;
        public int n;
        public double z;
        public bool arduino;
        public Motor motor;
        public string kamera;     // kapali | yok | baslatiliyor | aktif
        public float fps;
        public float sicaklik;   // Jetson'un en sıcak noktası, °C (0 = bilinmiyor)
        public bool h264;
        public int sinyal;        // dBm (0 = bilinmiyor)
        public int veri_kbps;
        public int atilan;
    }

    /// <summary>Gerçek robot (UDP) ya da robotsuz demo; sürüş kodu farkı bilmez.</summary>
    public interface IRobotConnection : IDisposable
    {
        string Host { get; }
        bool Connected { get; }
        double RoundTripMs { get; }
        /// <summary>Son 2 sn'de cevapsız kalan paket oranı, 0..1.</summary>
        float Loss { get; }
        /// <summary>Son cevaptan beri geçen süre (ms).</summary>
        double SinceReplyMs { get; }
        RobotStatus Status { get; }
        void Send(ControlState state);
    }

    /// <summary>
    /// Köprünün UDP sürüş kanalı (port 8081). Gönderen ve alan ayrı iş parçacıklarıdır; ana iş parçacığı yalnızca
    /// istenen durumu yazar. Bekleme kuyruğu yoktur: her gönderimde en son durum gider.
    /// </summary>
    public sealed class UdpRobotConnection : IRobotConnection
    {
        public const int UdpPort = 8081;
        public const int HttpPort = 8080;   // görüntü ve kamera ayarı (köprünün web sunucusu)
        const int Interval = 50;                 // ms
        const double ConnectedWithinMs = 1500;

        static readonly Stopwatch Clock = Stopwatch.StartNew();
        public static double Now() => Clock.Elapsed.TotalMilliseconds;

        public string Host { get; }
        public bool Connected => SinceReplyMs < ConnectedWithinMs;
        public double RoundTripMs { get; private set; } = -1;
        public float Loss
        {
            get
            {
                // 300 ms - 2 sn önce gönderilenlerden cevapsız kalanların oranı
                double now = Now();
                int total = 0, lost = 0;
                lock (ring)
                    for (int i = 0; i < RingSize; i++)
                    {
                        double age = now - ring[i].t;
                        if (ring[i].n == 0 || age < 300 || age > 2000) continue;
                        total++;
                        if (!ring[i].answered) lost++;
                    }
                return total > 0 ? lost / (float)total : 0f;
            }
        }
        public double SinceReplyMs => Now() - lastReply;
        public RobotStatus Status { get; private set; }

        readonly UdpClient udp;
        readonly IPEndPoint target;
        readonly CancellationTokenSource cts = new CancellationTokenSource();
        readonly AutoResetEvent changed = new AutoResetEvent(false);
        readonly object stateLock = new object();
        const int RingSize = 64;
        readonly (int n, double t, bool answered)[] ring = new (int, double, bool)[RingSize];
        ControlState state;
        int seq;
        double lastReply = double.NegativeInfinity;

        public UdpRobotConnection(string host)
        {
            Host = host;
            var address = Dns.GetHostAddresses(host)[0];
            foreach (var a in Dns.GetHostAddresses(host))
                if (a.AddressFamily == AddressFamily.InterNetwork) { address = a; break; }
            target = new IPEndPoint(address, UdpPort);
            udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.ReceiveTimeout = 500;
            // Wi-Fi önceliği: DSCP EF → WMM "ses" kuyruğu; sürüş paketleri görüntünün önüne geçer (Android uygular,
            // Windows yalnızca QoS ilkesiyle uygular)
            try { udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService, 0xB8); }
            catch (SocketException) { }
            new Thread(SendLoop) { IsBackground = true, Name = "Robot UDP gönder" }.Start();
            new Thread(ReceiveLoop) { IsBackground = true, Name = "Robot UDP al" }.Start();
        }

        public void Send(ControlState s)
        {
            bool different;
            lock (stateLock) { different = !s.SameAs(state); state = s; }
            if (different) changed.Set();        // değişiklik beklemeden gitsin (ör. korna, dur)
        }

        void SendLoop()
        {
            while (!cts.IsCancellationRequested)
            {
                changed.WaitOne(Interval);
                ControlState s;
                lock (stateLock) s = state;
                int n = ++seq;
                double z = Now();
                var inv = CultureInfo.InvariantCulture;
                string json = "{\"t\":\"d\",\"n\":" + n + ",\"z\":" + z.ToString("0.0", inv) +
                              ",\"sol\":" + s.Left + ",\"sag\":" + s.Right +
                              ",\"led\":" + B(s.Led) + ",\"dortlu\":" + B(s.Hazard) + ",\"motor_sesi\":" + B(s.EngineSound) +
                              ",\"geri_bip\":" + B(s.ReverseBeep) + ",\"k\":" + B(s.Horn) + ",\"ks\":" + s.HornStyle +
                              ",\"fs\":" + s.FlashCount + ",\"mi\":" + s.MelodyCount + ",\"mn\":" + s.Melody +
                              ",\"ef\":" + s.EffectCount + ",\"en\":" + s.Effect + ",\"efekt\":" + B(s.AutoEffects) + "}";
                var bytes = Encoding.ASCII.GetBytes(json);
                try { udp.Send(bytes, bytes.Length, target); }
                catch (Exception) { /* ağ yok: sonraki turda tekrar */ }
                lock (ring) ring[n % RingSize] = (n, z, false);
            }
        }

        void ReceiveLoop()
        {
            var from = new IPEndPoint(IPAddress.Any, 0);
            while (!cts.IsCancellationRequested)
            {
                byte[] data;
                try { data = udp.Receive(ref from); }
                catch (SocketException) { continue; }
                catch (ObjectDisposedException) { return; }
                RobotStatus s;
                try { s = JsonUtility.FromJson<RobotStatus>(Encoding.UTF8.GetString(data)); }
                catch (Exception) { continue; }
                if (s == null || s.t != "s") continue;
                lastReply = Now();
                if (s.z > 0) RoundTripMs = lastReply - s.z;
                Status = s;
                lock (ring)
                    if (ring[s.n % RingSize].n == s.n) ring[s.n % RingSize].answered = true;
            }
        }

        static string B(bool b) => b ? "1" : "0";

        public void Dispose()
        {
            cts.Cancel();
            changed.Set();
            try
            {
                // son söz: dur
                var stop = Encoding.ASCII.GetBytes("{\"t\":\"d\",\"n\":" + (seq + 1) + ",\"sol\":0,\"sag\":0}");
                udp.Send(stop, stop.Length, target);
            }
            catch (Exception) { }
            udp.Close();
        }
    }

    /// <summary>
    /// Robotun adresini bulur. Sırayla: son çalışan adres, jetson.local, UDP yayını ("robot nerede?"),
    /// en son bu bilgisayarın/telefonun alt ağını tarama (8080 portu açık ve köprü cevap veriyor mu).
    /// </summary>
    public static class RobotDiscovery
    {
        public const string AddressKey = "robot_adresi";

        public static async Task<string> FindAsync(string last, Action<string> progress, CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(last))
            {
                progress?.Invoke("son adres deneniyor: " + last);
                if (await Answers(last, ct)) return last;
            }
            progress?.Invoke("jetson.local deneniyor");
            try
            {
                var addrs = await Dns.GetHostAddressesAsync("jetson.local");
                foreach (var a in addrs)
                    if (a.AddressFamily == AddressFamily.InterNetwork && await Answers(a.ToString(), ct)) return a.ToString();
            }
            catch (Exception) { }

            progress?.Invoke("ağa soruluyor");
            string viaBroadcast = await Broadcast(ct);
            if (viaBroadcast != null) return viaBroadcast;

            foreach (var local in LocalAddresses())
            {
                ct.ThrowIfCancellationRequested();
                var b = local.GetAddressBytes();
                progress?.Invoke($"ağ taranıyor: {b[0]}.{b[1]}.{b[2]}.x");
                var tasks = new List<Task<string>>();
                for (int i = 1; i < 255; i++)
                {
                    if (i == b[3]) continue;
                    string ip = $"{b[0]}.{b[1]}.{b[2]}.{i}";
                    tasks.Add(PortOpen(ip, 8080, 700).ContinueWith(t => t.Result ? ip : null));
                }
                foreach (var ip in await Task.WhenAll(tasks))
                    if (ip != null && await Answers(ip, ct)) return ip;
            }
            return null;
        }

        /// <summary>Köprü UDP'den "robot" diye cevap veriyor mu?</summary>
        static async Task<bool> Answers(string host, CancellationToken ct)
        {
            try
            {
                using (var udp = new UdpClient(AddressFamily.InterNetwork))
                {
                    var ask = Encoding.ASCII.GetBytes("{\"t\":\"bul\"}");
                    for (int i = 0; i < 3; i++)
                    {
                        await udp.SendAsync(ask, ask.Length, host, UdpRobotConnection.UdpPort);
                        var r = udp.ReceiveAsync();
                        if (await Task.WhenAny(r, Task.Delay(400, ct)) == r && Encoding.UTF8.GetString(r.Result.Buffer).Contains("\"robot\""))
                            return true;
                    }
                }
            }
            catch (Exception) { }
            return false;
        }

        static async Task<string> Broadcast(CancellationToken ct)
        {
            try
            {
                using (var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true })
                {
                    var ask = Encoding.ASCII.GetBytes("{\"t\":\"bul\"}");
                    for (int i = 0; i < 3; i++)
                    {
                        await udp.SendAsync(ask, ask.Length, new IPEndPoint(IPAddress.Broadcast, UdpRobotConnection.UdpPort));
                        var r = udp.ReceiveAsync();
                        if (await Task.WhenAny(r, Task.Delay(500, ct)) == r && Encoding.UTF8.GetString(r.Result.Buffer).Contains("\"robot\""))
                            return r.Result.RemoteEndPoint.Address.ToString();
                    }
                }
            }
            catch (Exception e) { Debug.Log("[Robot] yayın sorusu olmadı: " + e.Message); }
            return null;
        }

        static async Task<bool> PortOpen(string ip, int port, int timeoutMs)
        {
            using (var c = new TcpClient())
            {
                try
                {
                    var connect = c.ConnectAsync(ip, port);
                    return await Task.WhenAny(connect, Task.Delay(timeoutMs)) == connect && c.Connected;
                }
                catch (Exception) { return false; }
            }
        }

        static IEnumerable<IPAddress> LocalAddresses()
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (ni.Description.Contains("Virtual") || ni.Description.Contains("vEthernet") || ni.Name.Contains("vEthernet")) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    var a = ua.Address;
                    if (a.AddressFamily != AddressFamily.InterNetwork) continue;
                    var b = a.GetAddressBytes();
                    if (b[0] == 169 && b[1] == 254) continue;
                    yield return a;
                }
            }
        }
    }
}
