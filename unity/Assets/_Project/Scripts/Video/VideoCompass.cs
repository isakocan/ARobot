using UnityEngine;
using UnityEngine.Rendering;

namespace ARaki.Video
{
    /// <summary>
    /// Telefonda görsel pusula: ekranda gösterilen 360 karenin ufuk bandı (enlem -5..+31 derece, 0,5 derece/piksel)
    /// her karede GPU'da çizilir (DualFisheye "Band" geçişi), eşzamansız okunur ve bir referans şeritle döngüsel
    /// korelasyonla karşılaştırılır: şeridin kayması = robotun dönüşü. Robottaki teta.Pusula'nın aynısı, ama
    /// ekrandaki görüntüyle aynı anda: AR nesneleri dönüşte görüntüyle birlikte döner (robotun konumu ~0,3 sn geç gelir,
    /// motor komutundan tahmin ise görüntünün önüne geçiyordu, 22 Kasım denemesi).
    /// Robotun kendi sabit parçaları (anten) Resources/PusulaMaske ile dışarıda (robottaki pusula_maske.png'den).
    /// </summary>
    public class VideoCompass : MonoBehaviour
    {
        const int W = 720, H = 24;
        const float PxPerDeg = W / 360f;
        const int Search = 18;            // px: referansa göre beklenen kaymanın çevresinde arama
        const int SearchLost = 60;        // px: referans tutmazsa bir önceki kareyle geniş arama
        const float RefRenew = 25f;       // derece
        const float GoodConf = 0.3f;

        PanoramaViewer viewer;
        RenderTexture rt;
        bool pending;
        float[] mask, win;
        float[] cur = new float[W * H], prev = new float[W * H], refS = new float[W * H];
        float refYaw, relYaw; bool haveRef, havePrev;
        float lastStepDeg;
        float rateDegS; float lastT;

        /// <summary>Başlangıçtan beri toplam dönüş (derece, sağa +). Yalnız göreli kullanılır.</summary>
        public float Yaw { get; private set; }
        /// <summary>Son eşleşmenin güveni 0..1 (normalleştirilmiş korelasyon).</summary>
        public float Confidence { get; private set; }
        /// <summary>Yakın zamanda güvenilir ölçüm var mı.</summary>
        public bool Healthy => Time.unscaledTime - lastGood < 0.5f;
        /// <summary>Yumuşatılmış dönüş hızı (derece/sn, mutlak).</summary>
        public float Rate => rateDegS;
        float lastGood = -10f;

        public void Init(PanoramaViewer v)
        {
            viewer = v;
            var ta = Resources.Load<TextAsset>("PusulaMaske");
            mask = new float[W * H];
            var b = ta != null ? ta.bytes : null;
            for (int i = 0; i < W * H; i++) mask[i] = b != null && b.Length == W * H ? (b[i] > 127 ? 1f : 0f) : 1f;
            win = new float[H];
            for (int y = 0; y < H; y++) win[y] = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * (y + 0.5f) / H);   // dikey Hann
        }

        void OnDestroy() { if (rt != null) rt.Release(); }

        void Update()
        {
            if (viewer == null || viewer.Sphere == null || pending || !SystemInfo.supportsAsyncGPUReadback) return;
            var mat = viewer.Sphere.sharedMaterial;
            if (mat == null || !mat.HasProperty("_MainTex")) return;
            int pass = mat.FindPass("Band");
            var tex = mat.GetTexture("_MainTex");
            if (tex == null || pass < 0) return;
            if (rt == null) rt = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            Graphics.Blit(tex, rt, mat, pass);
            pending = true;
            AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, OnRead);
        }

        void OnRead(AsyncGPUReadbackRequest r)
        {
            pending = false;
            if (r.hasError || this == null) return;
            var d = r.GetData<Color32>();
            if (d.Length < W * H) return;
            // yatay kenar (parlaklıktan bağımsız; desensiz duvarda da çalışır), maske ve dikey pencereyle
            float e = 0f;
            for (int y = 0; y < H; y++)
            {
                int o = y * W;
                for (int x = 0; x < W; x++)
                {
                    float g = d[o + (x + 1) % W].r - d[o + (x + W - 1) % W].r;
                    float v = g * mask[o + x] * win[y];
                    cur[o + x] = v; e += v * v;
                }
            }
            if (e < 1f) return;                                        // kare yok / siyah
            float now = Time.unscaledTime, dt = Mathf.Clamp(now - lastT, 0.001f, 0.5f); lastT = now;
            float before = Yaw;
            if (!haveRef)
            {
                Copy(cur, refS); Copy(cur, prev); haveRef = havePrev = true; refYaw = Yaw; relYaw = 0f;
                return;
            }
            float s, c;
            Match(refS, cur, relYaw * PxPerDeg, Search, out s, out c);
            if (c >= GoodConf)
            {
                relYaw = s / PxPerDeg; Yaw = refYaw + relYaw; Confidence = c; lastGood = now;
                if (Mathf.Abs(relYaw) > RefRenew || c < GoodConf * 1.5f) { Copy(cur, refS); refYaw = Yaw; relYaw = 0f; }
            }
            else
            {
                // referans tuttu: hızlı dönüş ya da sahne değişti; bir önceki kareyle, son adım çevresinde geniş arama
                Match(prev, cur, lastStepDeg * PxPerDeg, SearchLost, out s, out c);
                Confidence = c;
                if (c >= GoodConf * 0.8f) { Yaw += s / PxPerDeg; lastGood = now; }
                Copy(cur, refS); refYaw = Yaw; relYaw = 0f;
            }
            lastStepDeg = Mathf.DeltaAngle(0f, Yaw - before);
            rateDegS = Mathf.Lerp(rateDegS, Mathf.Abs(lastStepDeg) / dt, 1f - Mathf.Exp(-dt / 0.3f));
            Copy(cur, prev);
        }

        static void Copy(float[] a, float[] b) => System.Array.Copy(a, b, a.Length);

        /// <summary>a'dan b'ye kayma (px, sağa dönüş +): b[x] = a[x + s]. center çevresinde ±range tam piksel, sonra
        /// parabolle alt piksel. c = normalleştirilmiş tepe.</summary>
        static void Match(float[] a, float[] b, float center, int range, out float s, out float c)
        {
            int c0 = Mathf.RoundToInt(center);
            int n = 2 * range + 1;
            var corr = new float[n + 2];
            float ea = 0f, eb = 0f;
            for (int i = 0; i < a.Length; i++) { ea += a[i] * a[i]; eb += b[i] * b[i]; }
            int best = 0; float bv = float.MinValue;
            for (int k = -1; k <= n; k++)
            {
                int sh = c0 - range + k;
                int m = ((sh % W) + W) % W;
                float sum = 0f;
                for (int y = 0; y < H; y++)
                {
                    int o = y * W;
                    for (int x = 0; x < W; x++)
                    {
                        int xs = x + m; if (xs >= W) xs -= W;
                        sum += a[o + xs] * b[o + x];
                    }
                }
                corr[k + 1] = sum;
                if (k >= 0 && k < n && sum > bv) { bv = sum; best = k; }
            }
            float y0 = corr[best], y1 = corr[best + 1], y2 = corr[best + 2];
            float den = y0 - 2f * y1 + y2;
            float fine = Mathf.Abs(den) > 1e-6f ? Mathf.Clamp(0.5f * (y0 - y2) / den, -0.5f, 0.5f) : 0f;
            s = c0 - range + best + fine;
            c = bv / (Mathf.Sqrt(ea * eb) + 1e-6f);
        }
    }
}
