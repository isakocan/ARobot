using System;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace ARaki.Video
{
    public enum VideoMode { Auto, H264, Jpeg }

    /// <summary>
    /// Robotun 360° kamerası. Kare ham çift balık gözüdür (1280x720); küreye DualFisheye gölgelendiricisi kaplar.
    ///
    /// İki yol:
    /// - H.264 (ws://robot:8080/goruntu-h264): Jetson'un donanım kodlayıcısıyla sıkıştırılmış akış, ~4 Mbit/s.
    ///   Arka planda OpenH264 ile çözülür (H264Decoder); YUV düzlemleri dokuya yüklenir, RGB'ye ekran kartında
    ///   (ARaki/YuvToRgb) çevrilir.
    /// - JPEG (http://robot:8080/goruntu.mjpg): kameranın kendi kareleri, dokunulmadan; ~20-50 Mbit/s. Yedek yol.
    /// Otomatik modda H.264 denenir, 6 sn kare gelmezse ya da çözücü yüklenemezse JPEG'e geçilir.
    /// İki yolda da yalnızca en yeni kare tutulur, eski kareler atlanır: gecikme birikmez.
    /// Robotsuz demoda robottan kaydedilmiş kareler (Resources/RobotDemo) 15 fps döngüyle oynar.
    /// </summary>
    public class RobotVideo : MonoBehaviour
    {
        [SerializeField] ARaki.Robot.RealRobotLink robot;
        [SerializeField] Renderer target;
        [SerializeField, Range(1, 30)] int fps = 15;
        [Tooltip("ARaki/YuvToRgb; sahnede bağlı olmalı ki derlemede ayıklanmasın")]
        [SerializeField] Shader yuvShader;

        public VideoMode Mode { get; set; } = VideoMode.Auto;
        /// <summary>Şu an kullanılan yol: "H.264", "JPEG", "demo" ya da "".</summary>
        public string ActivePath { get; private set; } = "";
        public int FramesDecoded { get; private set; }
        public float Fps { get; private set; }
        /// <summary>Son karenin çözünürlüğü (0: kare yok).</summary>
        public int Width => ActivePath == "H.264" ? yuvW : jpegTexture != null && HasFrame ? jpegTexture.width : 0;
        public int Height => ActivePath == "H.264" ? yuvH : jpegTexture != null && HasFrame ? jpegTexture.height : 0;
        public string LastError { get; private set; } = "";
        /// <summary>Son 3 sn içinde kare geldi mi (kamera kapanınca ya da akış durunca false olur).</summary>
        public bool HasFrame => lastFrame >= 0f && Time.unscaledTime - lastFrame < 3f;

        Texture2D jpegTexture;
        Texture2D texY, texU, texV;
        RenderTexture rgb;
        Material yuvMaterial;

        volatile int generation;          // her yeni okuyucu bir artırır; eskisi kendini bitirir
        readonly object latestLock = new object();
        byte[] latestJpeg;
        // H.264: çözücü iş parçacığı son kareyi buraya bırakır
        byte[] yuvY, yuvU, yuvV;
        int yuvW, yuvH, yuvStrideY, yuvStrideUV;
        bool yuvReady;
        string streamKey;
        bool jpegFallback, h264Broken;   // h264Broken: kütüphane yok, bir daha deneme
        float lastFrame = -1f, fallbackAt;
        float pathStarted;
        int frameCounter;
        float fpsTimer;
        TextAsset[] demoFrames;
        int demoIndex;
        float demoTimer;

        void OnEnable()
        {
            jpegTexture = new Texture2D(2, 2, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, name = "Robot360 JPEG" };
            var shader = yuvShader != null ? yuvShader : Shader.Find("ARaki/YuvToRgb");
            if (shader != null) yuvMaterial = new Material(shader);
            Bind(jpegTexture);
        }

        void OnDisable() => StopReader();

        void OnDestroy()
        {
            if (rgb != null) rgb.Release();
        }

        void Bind(Texture t)
        {
            if (target != null) target.sharedMaterial.SetTexture("_MainTex", t);
        }

        void Update()
        {
            bool demo = robot != null && robot.Demo;
            string host = robot != null && !demo ? robot.Host : null;
            bool useH264 = !jpegFallback && !h264Broken && Mode != VideoMode.Jpeg && yuvMaterial != null;
            string key = host == null ? null : host + (useH264 ? "/h264" : "/jpeg");
            if (key != streamKey)
            {
                StopReader();
                lastFrame = -1f;
                streamKey = key;
                pathStarted = Time.unscaledTime;
                if (host != null)
                {
                    int gen = ++generation;
                    if (useH264) new Thread(() => H264Loop(host, gen)) { IsBackground = true, Name = "H.264" }.Start();
                    else new Thread(() => JpegLoop(host, gen)) { IsBackground = true, Name = "MJPEG" }.Start();
                    ActivePath = useH264 ? "H.264" : "JPEG";
                }
                else ActivePath = demo ? "demo" : "";
            }
            if (host == null) jpegFallback = false;

            // Otomatik modda H.264 10 sn kare vermezse (hiç gelmedi ya da kesildi) JPEG'e geç; 15 sn sonra H.264'ü yeniden dene.
            // JPEG ~40 Mbit/s ister (H.264'ün 10 katı): Wi-Fi'ı boğduğu için yalnızca kısa bir yedek olmalı.
            if (Mode == VideoMode.Auto && ActivePath == "H.264" && Time.unscaledTime - Mathf.Max(pathStarted, lastFrame) > 10f)
            {
                Debug.LogWarning("[Görüntü] H.264'ten kare gelmiyor, JPEG'e geçiliyor. " + LastError);
                jpegFallback = true;
                fallbackAt = Time.unscaledTime;
            }
            else if (jpegFallback && !h264Broken && Mode == VideoMode.Auto && Time.unscaledTime - fallbackAt > 15f)
                jpegFallback = false;

            if (demo) DemoFrame();
            else if (ActivePath == "JPEG") JpegFrame();
            else if (ActivePath == "H.264") YuvFrame();

            fpsTimer += Time.unscaledDeltaTime;
            if (fpsTimer >= 1f) { Fps = frameCounter / fpsTimer; frameCounter = 0; fpsTimer = 0f; }
        }

        void Counted()
        {
            lastFrame = Time.unscaledTime;
            FramesDecoded++;
            frameCounter++;
        }

        void DemoFrame()
        {
            if (demoFrames == null)
            {
                demoFrames = Resources.LoadAll<TextAsset>("RobotDemo");
                Array.Sort(demoFrames, (a, b) => string.CompareOrdinal(a.name, b.name));
            }
            demoTimer += Time.unscaledDeltaTime;
            if (demoFrames.Length == 0 || (demoTimer < 1f / fps && HasFrame)) return;
            demoTimer = 0f;
            if (jpegTexture.LoadImage(demoFrames[demoIndex++ % demoFrames.Length].bytes, false)) { Bind(jpegTexture); Counted(); }
        }

        void JpegFrame()
        {
            byte[] jpeg;
            lock (latestLock) { jpeg = latestJpeg; latestJpeg = null; }
            if (jpeg != null && jpegTexture.LoadImage(jpeg, false)) { Bind(jpegTexture); Counted(); }
        }

        void YuvFrame()
        {
            lock (latestLock)
            {
                if (!yuvReady) return;
                yuvReady = false;
                Upload(ref texY, yuvStrideY, yuvH, yuvY);
                Upload(ref texU, yuvStrideUV, yuvH / 2, yuvU);
                Upload(ref texV, yuvStrideUV, yuvH / 2, yuvV);
            }
            if (rgb == null || rgb.width != yuvW || rgb.height != yuvH)
            {
                if (rgb != null) rgb.Release();
                rgb = new RenderTexture(yuvW, yuvH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                    { name = "Robot360 H.264", wrapMode = TextureWrapMode.Clamp };
            }
            yuvMaterial.SetTexture("_Y", texY);
            yuvMaterial.SetTexture("_U", texU);
            yuvMaterial.SetTexture("_V", texV);
            // dokuların genişliği satır adımı (stride); görüntünün kendi genişliği kadarı kullanılır
            yuvMaterial.SetVector("_Scale", new Vector4(yuvW / (float)yuvStrideY, 1f, yuvW / 2f / yuvStrideUV, 1f));
            Graphics.Blit(null, rgb, yuvMaterial);
            Bind(rgb);
            Counted();
        }

        static void Upload(ref Texture2D tex, int w, int h, byte[] data)
        {
            if (tex == null || tex.width != w || tex.height != h)
            {
                if (tex != null) Destroy(tex);
                tex = new Texture2D(w, h, TextureFormat.R8, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            }
            tex.LoadRawTextureData(data);
            tex.Apply(false);
        }

        void StopReader()
        {
            generation++;
            streamKey = null;
        }

        // ---------------------------------------------------------------- H.264 (arka plan)

        void H264Loop(string host, int gen)
        {
            while (gen == generation)
            {
                try
                {
                    using (var ws = new ClientWebSocket())
                    using (var decoder = new H264Decoder())
                    {
                        ws.ConnectAsync(new Uri("ws://" + host + ":" + ARaki.Net.UdpRobotConnection.HttpPort + "/goruntu-h264"),
                                        CancellationToken.None).Wait(5000);
                        if (ws.State != WebSocketState.Open) throw new IOException("bağlanılamadı");
                        var buffer = new byte[512 * 1024];
                        byte[] y = null, u = null, v = null;
                        while (gen == generation && ws.State == WebSocketState.Open)
                        {
                            int len = 0;
                            WebSocketReceiveResult r;
                            do
                            {
                                if (len == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);
                                var t = ws.ReceiveAsync(new ArraySegment<byte>(buffer, len, buffer.Length - len), CancellationToken.None);
                                if (!t.Wait(5000)) throw new IOException("görüntü akışı durdu");
                                r = t.Result;
                                len += r.Count;
                            } while (!r.EndOfMessage);
                            if (r.MessageType == WebSocketMessageType.Close) break;
                            if (len < 2) continue;
                            // ilk bayt: 1 = tam kare (anahtar), 0 = ara kare; ardından H.264 erişim birimi
                            if (!decoder.Decode(buffer, 1, len - 1, ref y, ref u, ref v)) continue;
                            lock (latestLock)
                            {
                                // ana iş parçacığı yetişemediyse eski kare üstüne yazılır (birikmez)
                                Swap(ref y, ref yuvY); Swap(ref u, ref yuvU); Swap(ref v, ref yuvV);
                                yuvW = decoder.Width; yuvH = decoder.Height;
                                yuvStrideY = decoder.StrideY; yuvStrideUV = decoder.StrideUV;
                                yuvReady = true;
                            }
                            LastError = "";
                        }
                    }
                }
                catch (Exception e)
                {
                    LastError = e.InnerException?.Message ?? e.Message;
                    if (e is DllNotFoundException || e is EntryPointNotFoundException) { jpegFallback = h264Broken = true; return; }
                }
                if (gen == generation) Thread.Sleep(1000);
            }
        }

        static void Swap(ref byte[] a, ref byte[] b) { var t = a; a = b; b = t; }

        // ---------------------------------------------------------------- JPEG (arka plan)

        void JpegLoop(string host, int gen)
        {
            while (gen == generation)
            {
                try
                {
                    using (var client = new TcpClient())
                    {
                        client.ReceiveBufferSize = 256 * 1024;
                        if (!client.ConnectAsync(host, ARaki.Net.UdpRobotConnection.HttpPort).Wait(4000)) throw new IOException("bağlanılamadı");
                        var net = client.GetStream();
                        net.ReadTimeout = 5000;
                        var stream = new BufferedStream(net, 64 * 1024);
                        var request = Encoding.ASCII.GetBytes("GET /goruntu.mjpg?fps=" + fps + " HTTP/1.1\r\nHost: " + host + "\r\n\r\n");
                        net.Write(request, 0, request.Length);
                        ReadLine(stream);                                   // HTTP/1.1 200 OK
                        while (ReadLine(stream).Length > 0) { }              // başlıklar
                        while (gen == generation)
                        {
                            int length = -1;
                            string line;
                            while ((line = ReadLine(stream)) != null)
                            {
                                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                                    length = int.Parse(line.Substring(15).Trim());
                                else if (line.Length == 0 && length > 0) break;
                            }
                            if (line == null) break;
                            var jpeg = new byte[length];
                            ReadExactly(stream, jpeg);
                            lock (latestLock) latestJpeg = jpeg;
                            LastError = "";
                        }
                    }
                }
                catch (Exception e)
                {
                    LastError = e.Message;
                }
                if (gen == generation) Thread.Sleep(1000);
            }
        }

        static string ReadLine(Stream s)
        {
            var sb = new StringBuilder();
            while (true)
            {
                int b = s.ReadByte();
                if (b < 0) return sb.Length > 0 ? sb.ToString() : null;
                if (b == '\n') return sb.ToString().TrimEnd('\r');
                sb.Append((char)b);
                if (sb.Length > 512) throw new IOException("beklenmeyen akış");
            }
        }

        static void ReadExactly(Stream s, byte[] buffer)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n = s.Read(buffer, read, buffer.Length - read);
                if (n <= 0) throw new EndOfStreamException();
                read += n;
            }
        }
    }
}
