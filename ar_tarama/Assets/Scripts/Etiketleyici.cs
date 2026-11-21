using System;
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
    /// Telefonda SegFormer-B0 (ADE20K) bolutleme: kamera goruntusu dik (portre) cevrilip 512x512'ye olceklenir, model
    /// GPU'da (yoksa CPU) calisir, sinif = argmax (grafa eklenmis; yalniz 128x128 tamsayi geri okunur). Sonuc OdaEtiket
    /// grup kodlarina cevrilir ve sensor yonundeki derinlik izgarasina ornekleyen Etiket(u,v) ile verilir.
    /// tools/tarama/etiketle.py ile ayni on isleme (bilgisayardaki kayit etiketlemesiyle ayni sonuc).
    /// </summary>
    public class Etiketleyici : MonoBehaviour
    {
        public const int Boy = 512, Cikis = 128;

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
        float[] giris = new float[3 * Boy * Boy];
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
                Hazir = true;
                Debug.Log("[ArTarama] SegFormer hazir, arka uc: " + Arka);
            }
            catch (Exception e) { Debug.LogWarning("[ArTarama] SegFormer yuklenemedi: " + e.Message); Hazir = false; }
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
            public byte[] dik;               // Cikis x Cikis, dik goruntu yonunde
            public ScreenOrientation yon;
            public byte[] Izgaraya(int w, int h)
            {
                var e = new byte[w * h];
                for (int v = 0; v < h; v++)
                    for (int u = 0; u < w; u++)
                    {
                        SensordenDike(yon, (u + 0.5f) / w, (v + 0.5f) / h, out float yn, out float xn);
                        int r = Mathf.Clamp((int)(yn * Cikis), 0, Cikis - 1), c = Mathf.Clamp((int)(xn * Cikis), 0, Cikis - 1);
                        e[v * w + u] = dik[r * Cikis + c];
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
            Calistir(w, h, Screen.orientation, bitti);
            return true;
        }

        async void Calistir(int w, int h, ScreenOrientation yon, Action<Sonuc> bitti)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var kaynak = rgba; var g = giris;
                await Task.Run(() => OnIsle(kaynak, w, h, yon, g));
                var t0 = sw.Elapsed.TotalMilliseconds;
                using (var t = new Tensor<float>(new TensorShape(1, 3, Boy, Boy), g))
                {
                    worker.Schedule(t);
                    var cik = worker.PeekOutput() as Tensor<int>;
                    using (var kopya = await cik.ReadbackAndCloneAsync())
                    {
                        var ar = kopya.DownloadToArray();
                        ModelMs = (float)(sw.Elapsed.TotalMilliseconds - t0);
                        var dik = new byte[Cikis * Cikis];
                        int m = Math.Min(dik.Length, ar.Length);
                        for (int i = 0; i < m; i++) { int c = ar[i]; dik[i] = c >= 0 && c < Grup.Length ? Grup[c] : (byte)0; }
                        SonMs = (float)sw.Elapsed.TotalMilliseconds; Sayi++;
                        if (Sayi == 1 || Sayi % 20 == 0) Debug.Log("[ArTarama] SegFormer " + Arka + ": toplam " + SonMs.ToString("F0") + " ms, model " + ModelMs.ToString("F0") + " ms");
                        bitti?.Invoke(new Sonuc { dik = dik, yon = yon });
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[ArTarama] SegFormer hatasi: " + e.Message); }
            finally { Mesgul = false; }
        }

        static void OnIsle(byte[] rgba, int w, int h, ScreenOrientation yon, float[] g)
        {
            const float m0 = 0.485f, m1 = 0.456f, m2 = 0.406f, s0 = 1f / 0.229f, s1 = 1f / 0.224f, s2 = 1f / 0.225f, k = 1f / 255f;
            int pl = Boy * Boy;
            for (int i = 0; i < Boy; i++)
            {
                float yn = (i + 0.5f) / Boy;
                for (int j = 0; j < Boy; j++)
                {
                    DiktenSensore(yon, yn, (j + 0.5f) / Boy, out float un, out float vn);
                    int x = Math.Min(w - 1, (int)(un * w)), y = Math.Min(h - 1, (int)(vn * h));
                    int o = (y * w + x) * 4, d = i * Boy + j;
                    g[d] = (rgba[o] * k - m0) * s0;
                    g[pl + d] = (rgba[o + 1] * k - m1) * s1;
                    g[2 * pl + d] = (rgba[o + 2] * k - m2) * s2;
                }
            }
        }
    }
}
