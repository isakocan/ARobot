using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace ARaki.Video
{
    /// <summary>
    /// Robotun 360° kamerasını köprünün JPEG akışından (http://robot:8080/goruntu.mjpg) okur ve bir dokuya yazar.
    /// Okuma arka planda; yalnızca en yeni kare tutulur, ana iş parçacığı onu çözer (eski kareler atlanır,
    /// gecikme birikmez). Doku ham çift balık gözü karesidir (1280x720); küreye DualFisheye gölgelendiricisi kaplar.
    ///
    /// Robotsuz demoda robottan kaydedilmiş kareler (Resources/RobotDemo) 15 fps döngüyle oynar.
    ///
    /// Not: JPEG akışı H.264'ten 5-12 kat fazla veri ister. Telefonda H.264 için Android'in donanımsal
    /// çözücüsüne (MediaCodec) bir eklenti gerekir; o gelene kadar bu yol kullanılır.
    /// </summary>
    public class MjpegStream : MonoBehaviour
    {
        [SerializeField] ARaki.Robot.RealRobotLink robot;
        [SerializeField] Renderer target;
        [SerializeField, Range(1, 30)] int fps = 15;

        public Texture2D Texture { get; private set; }
        public int FramesDecoded { get; private set; }
        public float Fps { get; private set; }
        public string LastError { get; private set; } = "";

        volatile int generation;     // her yeni okuyucu bir artırır; eskisi kendini bitirir
        byte[] latest;
        readonly object latestLock = new object();
        string streamHost;
        int frameCounter;
        float fpsTimer;
        TextAsset[] demoFrames;
        int demoIndex;
        float demoTimer;
        bool hasFrame;

        /// <summary>En az bir kare geldi mi (gelmediyse ekranda "görüntü bekleniyor").</summary>
        public bool HasFrame => hasFrame;

        public void Bind(Renderer r) { target = r; if (Texture != null) Apply(); }

        void OnEnable()
        {
            Texture = new Texture2D(2, 2, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, name = "Robot360" };
            Apply();
        }

        void OnDisable() => StopReader();

        void Update()
        {
            string host = robot != null && !robot.Demo ? robot.Host : null;
            if (host != streamHost)
            {
                StopReader();
                hasFrame = false;
                if (!string.IsNullOrEmpty(host)) StartReader(host);
            }

            byte[] jpeg = null;
            if (robot != null && robot.Demo)
            {
                if (demoFrames == null)
                {
                    demoFrames = Resources.LoadAll<TextAsset>("RobotDemo");
                    Array.Sort(demoFrames, (a, b) => string.CompareOrdinal(a.name, b.name));
                }
                demoTimer += Time.unscaledDeltaTime;
                if (demoFrames.Length > 0 && (demoTimer >= 1f / fps || !hasFrame))
                {
                    demoTimer = 0f;
                    jpeg = demoFrames[demoIndex++ % demoFrames.Length].bytes;
                }
            }
            else lock (latestLock) { jpeg = latest; latest = null; }
            if (jpeg != null && Texture.LoadImage(jpeg, false))
            {
                hasFrame = true;
                FramesDecoded++;
                frameCounter++;
            }
            fpsTimer += Time.unscaledDeltaTime;
            if (fpsTimer >= 1f) { Fps = frameCounter / fpsTimer; frameCounter = 0; fpsTimer = 0f; }
        }

        void Apply()
        {
            if (target != null) target.sharedMaterial.SetTexture("_MainTex", Texture);
        }

        void StartReader(string host)
        {
            streamHost = host;
            int gen = ++generation;
            new Thread(() => ReadLoop(host, gen)) { IsBackground = true, Name = "MJPEG" }.Start();
        }

        void StopReader()
        {
            generation++;
            streamHost = null;
        }

        void ReadLoop(string host, int gen)
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
                            lock (latestLock) latest = jpeg;
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
