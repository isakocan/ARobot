using System;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace ARaki.Video
{
    public enum VideoMode { Auto, H264, Jpeg, Viewport }

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
    ///
    /// Görünen kısım (Viewport): kare iki balık gözü lensten oluşur (sol yarı ön, sağ yarı arka). İki akış alınır:
    /// tüm karenin küçüğü (taban, 640x360, ~0,4 Mbit/s) ve bakılan lensin keskin karesi (640x640, ~2,2 Mbit/s).
    /// Bakış tek lensin içinde kalıyorsa yalnızca o lens istenir; iki lensin arasına (yanlara) bakılınca tam kare
    /// istenir. İkisi LensComposite ile 1280x720 tek kareye birleşir; geçiş robotta sonraki tam karede olur
    /// (en geç ~1 sn), o arada taban görünür. Dikiz aynası arka lensi tabandan gösterir.
    /// </summary>
    public class RobotVideo : MonoBehaviour
    {
        [SerializeField] ARaki.Robot.RealRobotLink robot;
        [SerializeField] Renderer target;
        [SerializeField, Range(1, 30)] int fps = 15;
        [Tooltip("ARaki/YuvToRgb; sahnede bağlı olmalı ki derlemede ayıklanmasın")]
        [SerializeField] Shader yuvShader;

        public VideoMode Mode { get; set; } = VideoMode.Auto;
        /// <summary>Bakışı veren kamera (görünen kısım modunda hangi lensin isteneceği buna göre seçilir).</summary>
        public Camera ViewCamera { get; set; }
        /// <summary>Görünen kısım modunda istenen akış: "on", "arka" ya da "yuksek" (tam kare).</summary>
        public string Lens { get; private set; } = "on";
        /// <summary>Şu an kullanılan yol: "H.264", "JPEG", "demo" ya da "".</summary>
        public string ActivePath { get; private set; } = "";
        public int FramesDecoded { get; private set; }
        public float Fps { get; private set; }
        /// <summary>Son karenin çözünürlüğü (0: kare yok).</summary>
        public int Width => ActivePath == "H.264" ? main.W : ActivePath == ViewportPath ? win.W : jpegTexture != null && HasFrame ? jpegTexture.width : 0;
        public int Height => ActivePath == "H.264" ? main.H : ActivePath == ViewportPath ? win.H : jpegTexture != null && HasFrame ? jpegTexture.height : 0;
        public const string ViewportPath = "görünen";
        public string LastError { get; private set; } = "";
        /// <summary>Son 3 sn içinde kare geldi mi (kamera kapanınca ya da akış durunca false olur).</summary>
        public bool HasFrame => lastFrame >= 0f && Time.unscaledTime - lastFrame < 3f;

        Texture2D jpegTexture;
        Material yuvMaterial, compositeMaterial;
        RenderTexture composite;

        /// <summary>Bir H.264 akışı: çözücü iş parçacığı son kareyi buraya bırakır, ana iş parçacığı dokuya çevirir.</summary>
        class Slot
        {
            public byte[] Y, U, V;
            public int W, H, StrideY, StrideUV, Id;     // Id: köprünün akış sırası (ilk baytın üst bitleri)
            public bool Ready;
            public Texture2D TexY, TexU, TexV;
            public RenderTexture Rgb;
            public int RgbId = -1;                      // Rgb'deki karenin akışı
            public volatile string Request;             // iş parçacığı köprüye bu akışı istesin
            public float LastFrame = -1f;
        }
        readonly Slot main = new Slot(), baseSlot = new Slot(), win = new Slot();
        bool viewportBroken;
        float slowSince;
        /// <summary>Bekçinin akışı kaç kez yeniden kurduğu (tanı için).</summary>
        public int Restarts { get; private set; }
        float viewportBrokenAt;

        volatile int generation;          // her yeni okuyucu bir artırır; eskisi kendini bitirir
        readonly object latestLock = new object();
        byte[] latestJpeg;
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
            var comp = Resources.Load<Shader>("LensComposite");
            if (comp != null) compositeMaterial = new Material(comp);
            Bind(jpegTexture);
        }

        void OnDisable() => StopReader();

        void OnDestroy()
        {
            foreach (var sl in new[] { main, baseSlot, win }) if (sl.Rgb != null) sl.Rgb.Release();
            if (composite != null) composite.Release();
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
            if (viewportBroken && Time.unscaledTime - viewportBrokenAt > 30f) viewportBroken = false;
            bool viewport = useH264 && Mode == VideoMode.Viewport && compositeMaterial != null && !viewportBroken;
            string key = host == null ? null : host + (viewport ? "/gorunen" : useH264 ? "/h264" : "/jpeg");
            if (key != streamKey)
            {
                StopReader();
                lastFrame = -1f;
                streamKey = key;
                pathStarted = Time.unscaledTime;
                if (host != null)
                {
                    int gen = ++generation;
                    if (viewport)
                    {
                        baseSlot.LastFrame = win.LastFrame = -1f;
                        win.Request = Lens;
                        new Thread(() => H264Loop(host, gen, "/goruntu-h264?akis=taban", baseSlot)) { IsBackground = true, Name = "H.264 taban" }.Start();
                        new Thread(() => H264Loop(host, gen, "/goruntu-h264?akis=" + Lens, win)) { IsBackground = true, Name = "H.264 lens" }.Start();
                    }
                    else if (useH264) new Thread(() => H264Loop(host, gen, "/goruntu-h264", main)) { IsBackground = true, Name = "H.264" }.Start();
                    else new Thread(() => JpegLoop(host, gen)) { IsBackground = true, Name = "MJPEG" }.Start();
                    ActivePath = viewport ? ViewportPath : useH264 ? "H.264" : "JPEG";
                }
                else ActivePath = demo ? "demo" : "";
            }
            if (host == null) jpegFallback = false;

            // Otomatik modda H.264 10 sn kare vermezse (hiç gelmedi ya da kesildi) JPEG'e geç; 15 sn sonra H.264'ü yeniden dene.
            // JPEG ~40 Mbit/s ister (H.264'ün 10 katı): Wi-Fi'ı boğduğu için yalnızca kısa bir yedek olmalı.
            // JPEG'e kendiliğinden geçilmez: ~40 Mbit/s ister, zayıf sinyalde Wi-Fi'ı tümden boğar, her kareyi ana
            // iş parçacığında çözdüğü için uygulama da kasar (gaz tepkisi gecikir). H.264 kesilirse yeniden bağlanılır.
            if (Mode == VideoMode.Auto && ActivePath == "H.264" && Time.unscaledTime - Mathf.Max(pathStarted, lastFrame) > 6f)
            {
                Debug.LogWarning("[Görüntü] H.264'ten kare gelmiyor, yeniden bağlanılıyor. " + LastError);
                StopReader();
            }
            else if (jpegFallback && !h264Broken && Mode == VideoMode.Auto && Time.unscaledTime - fallbackAt > 15f)
                jpegFallback = false;
            // görünen kısım 10 sn kare vermezse (eski köprü, kodlayıcı açılamadı) normal H.264'e dön, 30 sn sonra yeniden dene
            if (ActivePath == ViewportPath && Time.unscaledTime - Mathf.Max(pathStarted, baseSlot.LastFrame) > 10f)
            {
                Debug.LogWarning("[Görüntü] görünen kısım akışı gelmiyor, tam kareye dönülüyor. " + LastError);
                viewportBroken = true;
                viewportBrokenAt = Time.unscaledTime;
            }

            // bekçi: bağlantı iyiyken (UDP cevap veriyor) görüntü 6 sn takılıysa (fps < 6) akışı baştan kur. Zayıf
            // sinyalden sonra bağlantı düzeldiği hâlde görüntü kötü kalıyordu; elle yeniden bağlamak gerekmesin.
            bool linkGood = robot != null && robot.IsConnected && !robot.IsWeak && robot.RoundTripMs >= 0 && robot.RoundTripMs < 200;
            if (!demo && host != null && linkGood && Time.unscaledTime - pathStarted > 8f && Fps < 6f) slowSince += Time.unscaledDeltaTime;
            else slowSince = 0f;
            if (slowSince > 6f)
            {
                Debug.LogWarning($"[Görüntü] bağlantı iyi ama {Fps:0} fps: akış yeniden kuruluyor ({ActivePath}). {LastError}");
                slowSince = 0f;
                StopReader();
                Restarts++;
            }

            if (demo) DemoFrame();
            else if (ActivePath == "JPEG") JpegFrame();
            else if (ActivePath == "H.264") YuvFrame();
            else if (ActivePath == ViewportPath) ViewportFrame();

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
            if (!Convert(main)) return;
            Bind(main.Rgb);
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

        /// <summary>Slotun son karesini (varsa) RGB dokusuna çevirir.</summary>
        bool Convert(Slot sl)
        {
            int w, h, sy, suv, id;
            lock (latestLock)
            {
                if (!sl.Ready) return false;
                sl.Ready = false;
                w = sl.W; h = sl.H; sy = sl.StrideY; suv = sl.StrideUV; id = sl.Id;
                Upload(ref sl.TexY, sy, h, sl.Y);
                Upload(ref sl.TexU, suv, h / 2, sl.U);
                Upload(ref sl.TexV, suv, h / 2, sl.V);
            }
            if (sl.Rgb == null || sl.Rgb.width != w || sl.Rgb.height != h)
            {
                if (sl.Rgb != null) sl.Rgb.Release();
                sl.Rgb = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                    { name = "Robot360 H.264", wrapMode = TextureWrapMode.Clamp };
            }
            yuvMaterial.SetTexture("_Y", sl.TexY);
            yuvMaterial.SetTexture("_U", sl.TexU);
            yuvMaterial.SetTexture("_V", sl.TexV);
            // dokuların genişliği satır adımı (stride); görüntünün kendi genişliği kadarı kullanılır
            yuvMaterial.SetVector("_Scale", new Vector4(w / (float)sy, 1f, w / 2f / suv, 1f));
            Graphics.Blit(null, sl.Rgb, yuvMaterial);
            sl.RgbId = id;
            sl.LastFrame = Time.unscaledTime;
            return true;
        }

        // akış sıraları köprüdeki H264_AKISLAR ile aynı: 0-2 tam kare, 3 ön lens, 4 arka lens, 5 taban
        const int StreamFront = 3, StreamBack = 4;
        const float LensReach = 93f;   // derece: lensin keskin gördüğü en geniş açı (kenardaki karışım bandından içeride)

        /// <summary>Bakış hangi lensin içinde kalıyor: kameranın köşegen yarı açısıyla birlikte.</summary>
        void ChooseLens()
        {
            if (ViewCamera == null || target == null) return;
            var mount = target.sharedMaterial.GetMatrix("_Mount");
            Vector3 d = mount.MultiplyVector(target.transform.InverseTransformDirection(ViewCamera.transform.forward)).normalized;
            float front = Vector3.Angle(d, Vector3.forward);
            float half = Mathf.Atan(Mathf.Tan(ViewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) *
                                    Mathf.Sqrt(1f + ViewCamera.aspect * ViewCamera.aspect)) * Mathf.Rad2Deg;
            float margin = Lens == "yuksek" ? 4f : 0f;   // tam kareden lense dönerken biraz daha içeri girilsin (gidip gelmesin)
            string want = front + half < LensReach - margin ? "on" : 180f - front + half < LensReach - margin ? "arka" : "yuksek";
            if (want != Lens) { Lens = want; win.Request = want; }
        }

        void ViewportFrame()
        {
            ChooseLens();
            bool fresh = Convert(baseSlot) | Convert(win);
            if (!fresh || baseSlot.Rgb == null) return;
            if (composite == null)
                composite = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                    { name = "Robot360 görünen", wrapMode = TextureWrapMode.Clamp };
            // lens karesi yeni sayılır: 1,5 sn'den eskiyse (akış değişiyor) yalnızca taban
            bool winOk = win.Rgb != null && Time.unscaledTime - win.LastFrame < 1.5f;
            Vector4 rect = Vector4.zero;
            if (winOk && win.RgbId == StreamFront) rect = new Vector4(0f, 80f / 720f, 0.5f, 1f);      // sol yarı, üstteki 640 satır
            else if (winOk && win.RgbId == StreamBack) rect = new Vector4(0.5f, 80f / 720f, 1f, 1f);
            else if (winOk && win.RgbId < StreamFront) rect = new Vector4(0f, 0f, 1f, 1f);          // tam kare
            compositeMaterial.SetTexture("_Base", baseSlot.Rgb);
            compositeMaterial.SetTexture("_Win", winOk ? (Texture)win.Rgb : Texture2D.blackTexture);
            compositeMaterial.SetVector("_Rect", rect);
            Graphics.Blit(null, composite, compositeMaterial);
            Bind(composite);
            Counted();
        }

        void StopReader()
        {
            generation++;
            streamKey = null;
        }

        // ---------------------------------------------------------------- H.264 (arka plan)

        void H264Loop(string host, int gen, string path, Slot slot)
        {
            while (gen == generation)
            {
                try
                {
                    using (var ws = new ClientWebSocket())
                    using (var decoder = new H264Decoder())
                    {
                        ws.ConnectAsync(new Uri("ws://" + host + ":" + ARaki.Net.UdpRobotConnection.HttpPort + path),
                                        CancellationToken.None).Wait(5000);
                        if (ws.State != WebSocketState.Open) throw new IOException("bağlanılamadı");
                        string sent = slot.Request;   // bağlanırken adreste istendi
                        var buffer = new byte[512 * 1024];
                        byte[] y = null, u = null, v = null;
                        while (gen == generation && ws.State == WebSocketState.Open)
                        {
                            // bakış değiştiyse köprüye başka akış iste (cevap beklenmez; geçiş sonraki tam karede)
                            string want = slot.Request;
                            if (want != null && want != sent)
                            {
                                var msg = Encoding.UTF8.GetBytes("{\"akis\":\"" + want + "\"}");
                                ws.SendAsync(new ArraySegment<byte>(msg), WebSocketMessageType.Text, true, CancellationToken.None).Wait(1000);
                                sent = want;
                            }
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
                                Swap(ref y, ref slot.Y); Swap(ref u, ref slot.U); Swap(ref v, ref slot.V);
                                slot.W = decoder.Width; slot.H = decoder.Height;
                                slot.StrideY = decoder.StrideY; slot.StrideUV = decoder.StrideUV;
                                slot.Id = buffer[0] >> 1;
                                slot.Ready = true;
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
