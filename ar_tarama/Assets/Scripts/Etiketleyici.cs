using System;
using System.Collections;
using System.Diagnostics;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Debug = UnityEngine.Debug;

namespace ArTarama
{
    /// <summary>
    /// Telefonda SegFormer-B0 (ADE20K) bolutleme: kamera goruntusu dik (portre) cevrilip 192x320'ye (dik en-boy orani)
    /// olceklenir, model GPU'da calisir, sinif = argmax (grafa eklenmis; yalniz 48x80 tamsayi geri okunur).
    /// GPU ayni zamanda kamera goruntusunu ciziyor: modeli tek seferde calistirmak ekrani ~0,9 sn donduruyordu (telefonda
    /// olculdu, 512x512). Bu yuzden katmanlar karelere yayilir (ScheduleIterable), etiket ~1 sn'de bir gelir, ekran akici kalir.
    /// 192x320 bilgisayarda 512x512 ile ayni isabeti verdi (zemin etiketinin derinlikte zemine oturma orani 0,634 / 0,638).
    /// </summary>
    public class Etiketleyici : MonoBehaviour
    {
        public const int DikG = 192, DikY = 320;                    // dik goruntu: genislik (sutun) x yukseklik (satir)
        public const int CikG = DikG / 4, CikY = DikY / 4;          // model cikisi 1/4
        public const int HedefKare = 30;                            // bir etiketleme kac kareye yayilir

        // ADE20K sinif -> OdaEtiket grubu (tools/tarama/etiketle.py GRUP tablosundan uretildi)
        static readonly byte[] Grup =
        {
            1,0,0,2,0,3,0,13,4,0,14,0,5,0,4,11,0,16,4,10,0,0,0,12,14,0,0,0,2,0,12,10,0,11,0,14,16,16,0,12,0,16,0,0,14,11,0,16,0,0,
            16,0,0,0,0,14,11,0,4,0,0,0,14,4,11,16,0,0,0,10,0,16,0,11,15,10,0,0,0,0,0,0,0,0,0,0,0,0,0,15,0,0,0,0,0,0,0,12,0,14,
            0,0,0,0,0,0,0,16,0,0,10,0,16,0,0,16,0,13,16,0,0,0,0,0,16,0,0,0,0,16,15,0,0,0,0,0,0,0,0,16,0,15,0,15,0,0,16,0,0,0
        };

        public bool Hazir { get; private set; }
        public bool Mesgul { get; private set; }
        public float SonMs { get; private set; }       // bir etiketleme (goruntu alma -> sonuc) suresi
        public float ModelMs { get; private set; }     // yalniz model + geri okuma
        public int Sayi { get; private set; }
        public string Arka { get; private set; } = "";

        Worker worker;
        float[] giris = new float[3 * DikG * DikY];
        int katmanSay = 200;                                        // isinmada sayilir
        byte[] rgba = new byte[0];

        void Awake()
        {
            try
            {
                var ma = Resources.Load<ModelAsset>("segformer_b0_ade");
                if (ma == null) { Debug.LogWarning("[ArTarama] SegFormer modeli bulunamadi (Resources/segformer_b0_ade)"); return; }
                var m = ModelLoader.Load(ma);
                var g = new FunctionalGraph();
                var gir = g.AddInputs(m);
                var cik = Functional.Forward(m, gir);
                var am = Functional.ArgMax(cik[0], 1);
                var m2 = g.Compile(am);
                var arka = SystemInfo.supportsComputeShaders ? BackendType.GPUCompute : BackendType.CPU;
                worker = new Worker(m2, arka); Arka = arka.ToString();
                Debug.Log("[ArTarama] SegFormer yuklendi, arka uc: " + Arka + "; isinma basliyor");
                StartCoroutine(Isin());
            }
            catch (Exception e) { Debug.LogWarning("[ArTarama] SegFormer yuklenemedi: " + e.Message); Hazir = false; }
        }

        /// <summary>Ilk calistirmada GPU golgelendiricileri derlenir (telefonda ~14 sn surdu): taramadan once, karelere yayarak.</summary>
        IEnumerator Isin()
        {
            var sw = Stopwatch.StartNew(); int n = 0;
            using (var t = new Tensor<float>(new TensorShape(1, 3, DikY, DikG), giris))
            {
                var it = worker.ScheduleIterable(t);
                while (true)
                {
                    bool devam;
                    try { devam = it.MoveNext(); } catch (Exception e) { Debug.LogWarning("[ArTarama] SegFormer isinma hatasi: " + e.Message); yield break; }
                    if (!devam) break;
                    n++; if (n % 4 == 0) yield return null;
                }
            }
            katmanSay = Math.Max(1, n);
            Hazir = true;
            Debug.Log("[ArTarama] SegFormer hazir: " + n + " katman, isinma " + sw.ElapsedMilliseconds + " ms");
        }

        void OnDestroy() { worker?.Dispose(); worker = null; }

        /// <summary>Gosterim yonune gore: dik goruntu (satir yn, sutun xn; 0..1) -> sensor goruntusu (un, vn).</summary>
        static void DiktenSensore(ScreenOrientation o, float yn, float xn, out float un, out float vn)
        {
            switch (o)
            {
                case ScreenOrientation.LandscapeLeft: un = xn; vn = yn; break;
                case ScreenOrientation.LandscapeRight: un = 1f - xn; vn = 1f - yn; break;
                case ScreenOrientation.PortraitUpsideDown: un = 1f - yn; vn = xn; break;
                default: un = yn; vn = 1f - xn; break;   // Portrait: sensore gore saat yonunde 90 derece
            }
        }

        static void SensordenDike(ScreenOrientation o, float un, float vn, out float yn, out float xn)
        {
            switch (o)
            {
                case ScreenOrientation.LandscapeLeft: xn = un; yn = vn; break;
                case ScreenOrientation.LandscapeRight: xn = 1f - un; yn = 1f - vn; break;
                case ScreenOrientation.PortraitUpsideDown: yn = 1f - un; xn = vn; break;
                default: yn = un; xn = 1f - vn; break;
            }
        }

        /// <summary>Sonuc: sensor yonundeki (w x h) izgaraya orneklenmis grup etiketleri.</summary>
        public class Sonuc
        {
            public byte[] dik;               // CikY satir x CikG sutun, dik goruntu yonunde
            public ScreenOrientation yon;
            public byte[] Izgaraya(int w, int h)
            {
                var e = new byte[w * h];
                for (int v = 0; v < h; v++)
                    for (int u = 0; u < w; u++)
                    {
                        SensordenDike(yon, (u + 0.5f) / w, (v + 0.5f) / h, out float yn, out float xn);
                        int r = Mathf.Clamp((int)(yn * CikY), 0, CikY - 1), c = Mathf.Clamp((int)(xn * CikG), 0, CikG - 1);
                        e[v * w + u] = dik[r * CikG + c];
                    }
                return e;
            }
        }

        /// <summary>Kamera goruntusunu alir ve etiketlemeyi baslatir. Bitince 'bitti' ana is parcaciginda cagrilir.</summary>
        public bool Baslat(ARCameraManager kamera, Action<Sonuc> bitti)
        {
            if (!Hazir || Mesgul || kamera == null) return false;
            if (!kamera.TryAcquireLatestCpuImage(out XRCpuImage img)) return false;
            int w, h;
            try
            {
                w = img.width / 4; h = img.height / 4;   // 1920x1080 -> 480x270 (model 512'ye zaten kucultuyor)
                var prm = new XRCpuImage.ConversionParams
                {
                    inputRect = new RectInt(0, 0, img.width, img.height),
                    outputDimensions = new Vector2Int(w, h),
                    outputFormat = TextureFormat.RGBA32,
                    transformation = XRCpuImage.Transformation.None
                };
                int n = img.GetConvertedDataSize(prm);
                if (rgba.Length != n) rgba = new byte[n];
                var na = new NativeArray<byte>(n, Allocator.Temp);
                img.Convert(prm, na); na.CopyTo(rgba); na.Dispose();
            }
            finally { img.Dispose(); }
            Mesgul = true;
            StartCoroutine(Calistir(w, h, Screen.orientation, bitti));
            return true;
        }

        IEnumerator Calistir(int w, int h, ScreenOrientation yon, Action<Sonuc> bitti)
        {
            var sw = Stopwatch.StartNew();
            var kaynak = rgba; var g = giris;
            var on = Task.Run(() => OnIsle(kaynak, w, h, yon, g));
            while (!on.IsCompleted) yield return null;
            if (on.IsFaulted) { Debug.LogWarning("[ArTarama] SegFormer on isleme hatasi: " + on.Exception?.Message); Mesgul = false; yield break; }
            var t0 = sw.Elapsed.TotalMilliseconds;
            var t = new Tensor<float>(new TensorShape(1, 3, DikY, DikG), g);
            IEnumerator it;
            try { it = worker.ScheduleIterable(t); } catch (Exception e) { Debug.LogWarning("[ArTarama] SegFormer hatasi: " + e.Message); t.Dispose(); Mesgul = false; yield break; }
            int adim = Math.Max(1, (katmanSay + HedefKare - 1) / HedefKare), k = 0;
            while (true)
            {
                bool devam;
                try { devam = it.MoveNext(); } catch (Exception e) { Debug.LogWarning("[ArTarama] SegFormer hatasi: " + e.Message); t.Dispose(); Mesgul = false; yield break; }
                if (!devam) break;
                if (++k % adim == 0) yield return null;
            }
            t.Dispose();
            var cik = worker.PeekOutput() as Tensor<int>;
            if (cik == null) { Mesgul = false; yield break; }
            cik.ReadbackRequest();
            while (!cik.IsReadbackRequestDone()) yield return null;
            int[] ar;
            try { ar = cik.DownloadToArray(); } catch (Exception e) { Debug.LogWarning("[ArTarama] SegFormer okuma hatasi: " + e.Message); Mesgul = false; yield break; }
            ModelMs = (float)(sw.Elapsed.TotalMilliseconds - t0);
            var dik = new byte[CikG * CikY];
            int m = Math.Min(dik.Length, ar.Length);
            for (int i = 0; i < m; i++) { int c = ar[i]; dik[i] = c >= 0 && c < Grup.Length ? Grup[c] : (byte)0; }
            SonMs = (float)sw.Elapsed.TotalMilliseconds; Sayi++;
            if (Sayi == 1 || Sayi % 20 == 0) Debug.Log("[ArTarama] SegFormer " + Arka + ": toplam " + SonMs.ToString("F0") + " ms (karelere yayili), model " + ModelMs.ToString("F0") + " ms, fps " + (1f / Mathf.Max(1e-3f, Time.smoothDeltaTime)).ToString("F0"));
            Mesgul = false;
            try { bitti?.Invoke(new Sonuc { dik = dik, yon = yon }); } catch (Exception e) { Debug.LogWarning("[ArTarama] etiket isleme hatasi: " + e.Message); }
        }

        static void OnIsle(byte[] rgba, int w, int h, ScreenOrientation yon, float[] g)
        {
            const float m0 = 0.485f, m1 = 0.456f, m2 = 0.406f, s0 = 1f / 0.229f, s1 = 1f / 0.224f, s2 = 1f / 0.225f, k = 1f / 255f;
            int pl = DikG * DikY;
            for (int i = 0; i < DikY; i++)
            {
                float yn = (i + 0.5f) / DikY;
                for (int j = 0; j < DikG; j++)
                {
                    DiktenSensore(yon, yn, (j + 0.5f) / DikG, out float un, out float vn);
                    int x = Math.Min(w - 1, (int)(un * w)), y = Math.Min(h - 1, (int)(vn * h));
                    int o = (y * w + x) * 4, d = i * DikG + j;
                    g[d] = (rgba[o] * k - m0) * s0;
                    g[pl + d] = (rgba[o + 1] * k - m1) * s1;
                    g[2 * pl + d] = (rgba[o + 2] * k - m2) * s2;
                }
            }
        }
    }
}
