using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace ARaki.Drive
{
    /// <summary>
    /// Ekranda görüneni (kokpit dahil) kaydeder: fotoğraf (PNG) ve video (Motion-JPEG .avi, VLC ve Windows
    /// oynatıcısı açar). Video kareleri ekran kartından eşzamansız okunur, JPEG'e arka planda çevrilip diske
    /// yazılır; sürüşü yavaşlatmaz. Ses: cihazın mikrofonu (telefon / PC), 22 kHz mono, videoyla aynı dosyada;
    /// telefonda ilk kayıtta mikrofon izni istenir. Kayıt yeri: PC'de Resimler/Robot, telefonda uygulamanın klasörü.
    /// </summary>
    public class ScreenRecorder : MonoBehaviour
    {
        [SerializeField] int videoFps = 15;
        [SerializeField] int videoHeight = 720;
        [SerializeField, Range(30, 95)] int jpegQuality = 80;

        public bool Recording => writer != null;
        public float RecordedSeconds => writer != null ? writer.Frames / (float)videoFps : 0f;
        public event Action<string> Saved;
        /// <summary>Son kayıtta ses var mı (mikrofon yoksa ya da izin verilmediyse yok).</summary>
        public bool HasAudio { get; private set; }

        const int MicRate = 22050;

        AviWriter writer;
        AudioClip mic;
        int micPos;
        float[] micBuffer = new float[MicRate];
        RenderTexture screen, scaled;
        float nextFrame;
        int pendingReadbacks;

        public static string Folder
        {
            get
            {
                string dir = Application.isMobilePlatform
                    ? Path.Combine(Application.persistentDataPath, "Kayitlar")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Robot");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        static string Stamp() => DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        public void TakePhoto() => StartCoroutine(Photo());

        IEnumerator Photo()
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            string path = Path.Combine(Folder, "foto_" + Stamp() + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            Saved?.Invoke(path);
        }

        public void ToggleVideo()
        {
            if (Recording) StopVideo();
            else StartVideo();
        }

        public void StartVideo()
        {
            int h = Mathf.Min(videoHeight, Screen.height);
            int w = Mathf.RoundToInt(h * Screen.width / (float)Screen.height) & ~1;
            mic = StartMic();
            HasAudio = mic != null;
            writer = new AviWriter(Path.Combine(Folder, "video_" + Stamp() + ".avi"), w, h, videoFps, jpegQuality, HasAudio ? MicRate : 0);
            nextFrame = 0f;
            StartCoroutine(Capture());
        }

        public void StopVideo()
        {
            if (writer == null) return;
            var w = writer;
            PumpMic(w);
            writer = null;
            if (mic != null) { Microphone.End(null); mic = null; }
            StartCoroutine(Finish(w));
        }

        IEnumerator Finish(AviWriter w)
        {
            while (pendingReadbacks > 0) yield return null;   // yoldaki kareler de yazılsın
            string path = w.Close();
            Saved?.Invoke(path);
        }

        IEnumerator Capture()
        {
            var end = new WaitForEndOfFrame();
            var current = writer;
            while (writer == current && current != null)
            {
                yield return end;
                PumpMic(current);
                if (Time.unscaledTime < nextFrame) continue;
                nextFrame = Mathf.Max(nextFrame + 1f / videoFps, Time.unscaledTime);
                if (screen == null || screen.width != Screen.width || screen.height != Screen.height)
                {
                    if (screen != null) screen.Release();
                    screen = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
                }
                if (scaled == null || scaled.width != current.Width || scaled.height != current.Height)
                {
                    if (scaled != null) scaled.Release();
                    scaled = new RenderTexture(current.Width, current.Height, 0, RenderTextureFormat.ARGB32);
                }
                ScreenCapture.CaptureScreenshotIntoRenderTexture(screen);
                // Bazı grafik arayüzlerinde (D3D, Metal) yakalanan görüntü baş aşağıdır
                if (SystemInfo.graphicsUVStartsAtTop) Graphics.Blit(screen, scaled, new Vector2(1f, -1f), new Vector2(0f, 1f));
                else Graphics.Blit(screen, scaled);
                pendingReadbacks++;
                int w = current.Width, h = current.Height;
                AsyncGPUReadback.Request(scaled, 0, TextureFormat.RGBA32, r =>
                {
                    pendingReadbacks--;
                    if (r.hasError) return;
                    current.AddFrame(r.GetData<byte>().ToArray(), w, h);
                });
            }
        }

        AudioClip StartMic()
        {
            if (Microphone.devices.Length == 0) return null;
#if UNITY_ANDROID
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
                return null;   // bu kayıt sessiz; izin verilirse sonrakinde ses olur
            }
#endif
            micPos = 0;
            return Microphone.Start(null, true, 1, MicRate);   // 1 sn'lik halka; her karede okunur
        }

        /// <summary>Mikrofonda biriken örnekleri videoya ekler (halka arabelleğin sonunda başa döner).</summary>
        void PumpMic(AviWriter w)
        {
            if (mic == null || w == null) return;
            int pos = Microphone.GetPosition(null);
            int n = (pos - micPos + mic.samples) % mic.samples;
            if (n == 0) return;
            var samples = new short[n];
            int first = Mathf.Min(n, mic.samples - micPos);
            CopyMic(micPos, first, samples, 0);
            if (n > first) CopyMic(0, n - first, samples, first);
            micPos = pos;
            w.AddAudio(samples);
        }

        void CopyMic(int from, int count, short[] to, int at)
        {
            if (micBuffer.Length < count) micBuffer = new float[count];
            var part = count == micBuffer.Length ? micBuffer : new float[count];
            mic.GetData(part, from);
            for (int i = 0; i < count; i++) to[at + i] = (short)(Mathf.Clamp(part[i], -1f, 1f) * 32767f);
        }

        void OnDestroy()
        {
            if (mic != null) Microphone.End(null);
            writer?.Close();
            if (screen != null) screen.Release();
            if (scaled != null) scaled.Release();
        }

        /// <summary>
        /// En basit video dosyası: AVI kabı içinde art arda JPEG kareler (MJPG). Kareler arka planda JPEG'e çevrilir.
        /// Dosya kapanırken başlıktaki sayılar ve kare dizini (idx1) yazılır.
        /// </summary>
        class AviWriter
        {
            public int Width { get; }
            public int Height { get; }
            public int Frames { get; private set; }

            readonly string path;
            readonly int fps, quality;
            readonly FileStream file;
            readonly BinaryWriter bw;
            readonly MemoryStream index = new MemoryStream();
            readonly BlockingCollection<byte[]> queue = new BlockingCollection<byte[]>(6);
            readonly ConcurrentQueue<short[]> audio = new ConcurrentQueue<short[]>();
            readonly Thread worker;
            readonly int audioRate;   // 0 = sessiz
            long moviStart, riffSizePos, framesPos1, framesPos2, moviSizePos, audioLenPos;
            int maxFrame, audioSamples;

            public AviWriter(string path, int width, int height, int fps, int quality, int audioRate)
            {
                this.path = path;
                Width = width; Height = height; this.fps = fps; this.quality = quality; this.audioRate = audioRate;
                file = new FileStream(path, FileMode.Create, FileAccess.Write);
                bw = new BinaryWriter(file);
                WriteHeader();
                worker = new Thread(Work) { IsBackground = true, Name = "Video kaydı" };
                worker.Start();
            }

            /// <summary>Ham RGBA kare (ana iş parçacığı). Kodlama yetişmezse kare atlanır: kayıt sürüşü yavaşlatmasın.</summary>
            public void AddFrame(byte[] rgba, int w, int h)
            {
                // Kayıt durdurulduktan sonra gelen son ekran okuması atılır
                try { if (!queue.IsAddingCompleted) queue.TryAdd(rgba); }
                catch (System.InvalidOperationException) { }
            }

            /// <summary>Mikrofon örnekleri (16 bit mono); kayıt kareleri sırasında araya yazılır, hiç atılmaz.</summary>
            public void AddAudio(short[] samples)
            {
                if (audioRate > 0) audio.Enqueue(samples);
            }

            void WriteAudio()
            {
                while (audio.TryDequeue(out var a))
                {
                    long offset = file.Position - moviStart;
                    Fourcc("01wb");
                    bw.Write(a.Length * 2);
                    var bytes = new byte[a.Length * 2];
                    Buffer.BlockCopy(a, 0, bytes, 0, bytes.Length);
                    bw.Write(bytes);
                    var ib = new BinaryWriter(index);
                    ib.Write(Encoding.ASCII.GetBytes("01wb"));
                    ib.Write(0x10);
                    ib.Write((int)offset);
                    ib.Write(bytes.Length);
                    audioSamples += a.Length;
                }
            }

            void Work()
            {
                foreach (var rgba in queue.GetConsumingEnumerable())
                {
                    WriteAudio();
                    byte[] jpg = ImageConversion.EncodeArrayToJPG(rgba, GraphicsFormat.R8G8B8A8_UNorm, (uint)Width, (uint)Height, 0, quality);
                    long offset = file.Position - moviStart;
                    Fourcc("00dc");
                    bw.Write(jpg.Length);
                    bw.Write(jpg);
                    if ((jpg.Length & 1) == 1) bw.Write((byte)0);
                    var ib = new BinaryWriter(index);
                    ib.Write(Encoding.ASCII.GetBytes("00dc"));
                    ib.Write(0x10);              // anahtar kare
                    ib.Write((int)offset);
                    ib.Write(jpg.Length);
                    maxFrame = Math.Max(maxFrame, jpg.Length);
                    Frames++;
                }
            }

            void Fourcc(string s) => bw.Write(Encoding.ASCII.GetBytes(s));

            void WriteHeader()
            {
                Fourcc("RIFF"); riffSizePos = file.Position; bw.Write(0); Fourcc("AVI ");
                Fourcc("LIST"); bw.Write(4 + 8 + 56 + 8 + 4 + 8 + 56 + 8 + 40 + (audioRate > 0 ? 8 + 4 + 8 + 56 + 8 + 16 : 0)); Fourcc("hdrl");
                Fourcc("avih"); bw.Write(56);
                bw.Write(1000000 / fps); bw.Write(0); bw.Write(0); bw.Write(0x10);   // AVIF_HASINDEX
                framesPos1 = file.Position; bw.Write(0); bw.Write(0); bw.Write(audioRate > 0 ? 2 : 1); bw.Write(0);
                bw.Write(Width); bw.Write(Height); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
                Fourcc("LIST"); bw.Write(4 + 8 + 56 + 8 + 40); Fourcc("strl");
                Fourcc("strh"); bw.Write(56);
                Fourcc("vids"); Fourcc("MJPG"); bw.Write(0); bw.Write((short)0); bw.Write((short)0); bw.Write(0);
                bw.Write(1); bw.Write(fps); bw.Write(0); framesPos2 = file.Position; bw.Write(0);
                bw.Write(0); bw.Write(-1); bw.Write(0);
                bw.Write((short)0); bw.Write((short)0); bw.Write((short)Width); bw.Write((short)Height);
                Fourcc("strf"); bw.Write(40);
                bw.Write(40); bw.Write(Width); bw.Write(Height); bw.Write((short)1); bw.Write((short)24);
                Fourcc("MJPG"); bw.Write(Width * Height * 3); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
                if (audioRate > 0)
                {
                    // ses akışı: PCM 16 bit mono
                    Fourcc("LIST"); bw.Write(4 + 8 + 56 + 8 + 16); Fourcc("strl");
                    Fourcc("strh"); bw.Write(56);
                    Fourcc("auds"); bw.Write(0); bw.Write(0); bw.Write((short)0); bw.Write((short)0); bw.Write(0);
                    bw.Write(2); bw.Write(audioRate * 2); bw.Write(0); audioLenPos = file.Position; bw.Write(0);
                    bw.Write(audioRate); bw.Write(-1); bw.Write(2);
                    bw.Write((short)0); bw.Write((short)0); bw.Write((short)0); bw.Write((short)0);
                    Fourcc("strf"); bw.Write(16);
                    bw.Write((short)1); bw.Write((short)1); bw.Write(audioRate); bw.Write(audioRate * 2); bw.Write((short)2); bw.Write((short)16);
                }
                Fourcc("LIST"); moviSizePos = file.Position; bw.Write(0); moviStart = file.Position; Fourcc("movi");
            }

            public string Close()
            {
                queue.CompleteAdding();
                worker.Join();
                WriteAudio();
                long moviEnd = file.Position;
                Fourcc("idx1"); bw.Write((int)index.Length); bw.Write(index.ToArray());
                long end = file.Position;
                file.Position = moviSizePos; bw.Write((int)(moviEnd - moviStart));
                file.Position = riffSizePos; bw.Write((int)(end - 8));
                file.Position = framesPos1; bw.Write(Frames);
                file.Position = framesPos2; bw.Write(Frames);
                if (audioRate > 0) { file.Position = audioLenPos; bw.Write(audioSamples); }
                bw.Flush();
                file.Dispose();
                return path;
            }
        }
    }
}
