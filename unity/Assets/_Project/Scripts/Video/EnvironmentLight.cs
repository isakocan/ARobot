using UnityEngine;
using UnityEngine.Rendering;

namespace ARaki.Video
{
    /// <summary>
    /// AR nesneleri için ortamın kendi ışığı, 360° görüntüden. ~6 Hz'de görüntü 256x128 equirect olarak çizilip 64x32'ye
    /// küçültülür (DualFisheye "EnvBake" geçişi; simde Sim360Camera dokusu), GPU'dan eşzamansız okunur ve 2. derece küresel
    /// harmoniklere (9 katsayı, renkli) indirgenir. Katsayılar Lambert yüzeyinin yansıttığı ışığa çevrilir (Ramamoorthi):
    /// beyaz bir yüzey odadaki gerçek beyaz bir yüzey kadar parlak, aynı renk tonunda görünür. Baskın ışık yönü 1. derece
    /// katsayılardan, yönlülük (0 = her yerden eşit, 1 = tek kaynak) onların 0. dereceye oranından. Sonuçlar zamanla
    /// yumuşatılır (~0,4 sn): lamba yanınca / robot pencereye dönünce nesneler de değişir ama titremez.
    ///
    /// Gölgelendiriciye (ARaki/Toon) küresel değişkenlerle verilir: _EnvSH[9], _EnvDir (xyz yön, w yönlülük), _EnvRot
    /// (dünya -> küre/robot), _EnvOn. Görüntü robota göre olduğundan hesap küre uzayında yapılır.
    /// </summary>
    public class EnvironmentLight : MonoBehaviour
    {
        const int W = 64, H = 32;
        const float Interval = 0.16f, Smooth = 0.4f;

        PanoramaViewer viewer;
        RenderTexture big, mid, small;
        bool pending; float last = -10f;
        readonly Vector3[] sh = new Vector3[9];        // yumuşatılmış, Lambert'e çevrilmiş
        readonly Vector4[] shOut = new Vector4[9];
        readonly Vector3[] raw = new Vector3[9];
        bool have;

        /// <summary>Baskın ışık yönü (küre uzayı, birim) ve yönlülük 0..1.</summary>
        public Vector3 DirSphere { get; private set; } = new Vector3(-0.3f, 0.85f, -0.3f).normalized;
        public float Directionality { get; private set; }
        /// <summary>Ortalama ortam ışığı (lineer RGB).</summary>
        public Color Ambient { get; private set; } = Color.gray;
        public bool Ready => have;
        public Transform Frame => viewer != null && viewer.Sphere != null ? viewer.Sphere.transform : transform;
        /// <summary>Baskın ışık yönü dünya uzayında.</summary>
        public Vector3 DirWorld => Frame.TransformDirection(DirSphere).normalized;

        static readonly int IdSH = Shader.PropertyToID("_EnvSH"), IdDir = Shader.PropertyToID("_EnvDir"),
            IdOn = Shader.PropertyToID("_EnvOn"), IdRot = Shader.PropertyToID("_EnvRot");

        public void Init(PanoramaViewer v) { viewer = v; }

        void OnDisable() { Shader.SetGlobalFloat(IdOn, 0f); }

        void OnDestroy()
        {
            Shader.SetGlobalFloat(IdOn, 0f);
            if (big != null) big.Release(); if (mid != null) mid.Release(); if (small != null) small.Release();
        }

        static RenderTexture Rt(int w, int h) =>
            new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };

        void Update()
        {
            if (viewer == null || viewer.Sphere == null) return;
            // dünya -> küre dönmesi her karede (robot / bakış dönünce de doğru kalsın)
            var q = Quaternion.Inverse(Frame.rotation);
            Shader.SetGlobalMatrix(IdRot, Matrix4x4.Rotate(q));
            if (have) Push();

            if (pending || Time.unscaledTime - last < Interval || !SystemInfo.supportsAsyncGPUReadback) return;
            if (!Bake()) return;
            last = Time.unscaledTime; pending = true;
            AsyncGPUReadback.Request(small, 0, TextureFormat.RGBAFloat, OnRead);
        }

        bool Bake()
        {
            if (big == null) { big = Rt(256, 128); mid = Rt(128, 64); small = Rt(W, H); }
            var src = viewer.Source;
            if (src != null && src.Equirect != null) Graphics.Blit(src.Equirect, big);
            else
            {
                var mat = viewer.Sphere.sharedMaterial;
                if (mat == null || !mat.HasProperty("_MainTex")) return false;
                int pass = mat.FindPass("EnvBake");
                var tex = mat.GetTexture("_MainTex");
                if (tex == null || pass < 0) return false;
                Graphics.Blit(tex, big, mat, pass);
            }
            Graphics.Blit(big, mid);
            Graphics.Blit(mid, small);
            return true;
        }

        void OnRead(AsyncGPUReadbackRequest r)
        {
            pending = false;
            if (r.hasError || this == null) return;
            var d = r.GetData<Vector4>();
            if (d.Length < W * H) return;
            for (int k = 0; k < 9; k++) raw[k] = Vector3.zero;
            float dA = (2f * Mathf.PI / W) * (Mathf.PI / H);
            for (int y = 0; y < H; y++)
            {
                float lat = ((y + 0.5f) / H - 0.5f) * Mathf.PI, cl = Mathf.Cos(lat), sl = Mathf.Sin(lat);
                float w = dA * cl;
                for (int x = 0; x < W; x++)
                {
                    float yaw = ((x + 0.5f) / W - 0.5f) * Mathf.PI * 2f;
                    float nx = Mathf.Sin(yaw) * cl, ny = sl, nz = Mathf.Cos(yaw) * cl;
                    var p = d[y * W + x]; var c = new Vector3(p.x, p.y, p.z) * w;
                    raw[0] += c * 0.282095f;
                    raw[1] += c * (0.488603f * ny); raw[2] += c * (0.488603f * nz); raw[3] += c * (0.488603f * nx);
                    raw[4] += c * (1.092548f * nx * ny); raw[5] += c * (1.092548f * ny * nz);
                    raw[6] += c * (0.315392f * (3f * nz * nz - 1f));
                    raw[7] += c * (1.092548f * nx * nz); raw[8] += c * (0.546274f * (nx * nx - ny * ny));
                }
            }
            // ışınım -> Lambert yansıması (/pi): 0. derece x1, 1. derece x2/3, 2. derece x1/4
            for (int k = 0; k < 9; k++) raw[k] *= k == 0 ? 1f : k < 4 ? 2f / 3f : 0.25f;
            float a = have ? 1f - Mathf.Exp(-Interval / Smooth) : 1f;
            for (int k = 0; k < 9; k++) sh[k] = Vector3.Lerp(sh[k], raw[k], a);
            have = true;

            // baskın yön: parlaklığın 1. derece katsayıları (Y1-1 ~ y, Y10 ~ z, Y11 ~ x)
            float L(Vector3 c) => c.x * 0.2126f + c.y * 0.7152f + c.z * 0.0722f;
            float l0 = L(sh[0]), ly = L(sh[1]), lz = L(sh[2]), lx = L(sh[3]);
            var dir = new Vector3(lx, ly, lz);
            float m = dir.magnitude;
            if (m > 1e-5f) DirSphere = dir / m;
            // tek kaynakta |L1|/L0 (2/3 ölçekli) ~1,15; düz ortamda 0
            Directionality = l0 > 1e-5f ? Mathf.Clamp01((m / l0) / 0.7f) : 0f;
            var amb = sh[0] * 0.282095f; Ambient = new Color(amb.x, amb.y, amb.z, 1f);
        }

        void Push()
        {
            for (int k = 0; k < 9; k++) shOut[k] = new Vector4(sh[k].x, sh[k].y, sh[k].z, 0f);
            Shader.SetGlobalVectorArray(IdSH, shOut);
            var ds = DirSphere;
            Shader.SetGlobalVector(IdDir, new Vector4(ds.x, ds.y, ds.z, Directionality));
            Shader.SetGlobalFloat(IdOn, 1f);
        }
    }
}
