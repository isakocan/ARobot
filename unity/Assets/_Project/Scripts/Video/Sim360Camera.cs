using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ARaki.Video
{
    /// <summary>Simüle edilen 360° video kalitesi. Telefonda gerçek Theta S akışına (≈2K, 30 kare) yakın ayar kullanılır.</summary>
    public enum VideoQuality { High, Medium, Low }

    /// <summary>
    /// Simülasyonda Ricoh Theta S'in yerini tutar: bulunduğu noktadan her yöne bakıp tek bir
    /// equirectangular (360°) görüntü üretir. Gerçek robotta bu doku Jetson'dan gelen videodan okunacak;
    /// geri kalan her şey (PanoramaViewer, AR) aynı kalır.
    ///
    /// Maliyet: her yakalama 6 yüzlü bir küp çizer ve onu equirect'e çevirir; sahnenin en pahalı işi budur.
    /// Gerçek kamera zaten saniyede ~30 kare verdiği için yakalama oranı sınırlanır (bakış her karede akıcı kalır,
    /// yalnızca dokunun kendisi 30 Hz yenilenir). Kalite telefonda otomatik olarak orta seviyeye iner.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class Sim360Camera : MonoBehaviour
    {
        [SerializeField] float captureHz = 30f;

        public RenderTexture Equirect { get; private set; }
        public VideoQuality Quality { get; private set; } = VideoQuality.High;
        public int CaptureCount { get; private set; }
        public event Action TargetsRecreated;

        Camera cam;
        RenderTexture cube;
        float nextCapture;
        bool qualityChosen;

        static void Sizes(VideoQuality q, out int equirectWidth, out int face)
        {
            switch (q)
            {
                case VideoQuality.Low: equirectWidth = 1536; face = 768; break;
                case VideoQuality.Medium: equirectWidth = 2048; face = 1024; break;
                default: equirectWidth = 3072; face = 1536; break;
            }
        }

        void Awake()
        {
            if (!qualityChosen) Quality = Application.isMobilePlatform ? VideoQuality.Medium : VideoQuality.High;
            qualityChosen = true;
            EnsureTargets();
        }

        void LateUpdate()
        {
            float now = Time.unscaledTime;
            if (now < nextCapture) return;
            float period = 1f / Mathf.Max(1f, captureHz);
            nextCapture = Mathf.Max(nextCapture + period, now - period);   // geride kalırsak birikmesin
            Capture();
        }

        void OnDestroy() => Release();

        void Release()
        {
            if (cube != null) { cube.Release(); cube = null; }
            if (Equirect != null) { Equirect.Release(); Equirect = null; }
        }

        public void SetQuality(VideoQuality quality)
        {
            qualityChosen = true;
            if (Quality == quality && Equirect != null) return;
            Quality = quality;
            if (Equirect != null)
            {
                Release();
                EnsureTargets();
                TargetsRecreated?.Invoke();
            }
        }

        public void EnsureTargets()
        {
            if (Equirect != null) return;
            cam = GetComponent<Camera>();
            cam.enabled = false; // kendi başına çizmez, Capture() çağırır
            Sizes(Quality, out int width, out int face);
            cube = new RenderTexture(face, face, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                dimension = TextureDimension.Cube,
                antiAliasing = 1,
            };
            Equirect = new RenderTexture(width, width / 2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat,
                useMipMap = false,
            };
            cube.Create();
            Equirect.Create();
        }

        public void Capture()
        {
            EnsureTargets();
            cam.RenderToCubemap(cube, 63);
            cube.ConvertToEquirect(Equirect, Camera.MonoOrStereoscopicEye.Mono);
            CaptureCount++;
        }
    }
}
