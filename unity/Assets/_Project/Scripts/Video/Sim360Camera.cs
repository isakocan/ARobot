using UnityEngine;
using UnityEngine.Rendering;

namespace ARaki.Video
{
    /// <summary>
    /// Simülasyonda Ricoh Theta S'in yerini tutar: bulunduğu noktadan her yöne bakıp tek bir
    /// equirectangular (360°) görüntü üretir. Gerçek robotta bu doku Jetson'dan gelen videodan okunacak;
    /// geri kalan her şey (PanoramaViewer, AR) aynı kalır.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class Sim360Camera : MonoBehaviour
    {
        [SerializeField] int width = 3072;
        [SerializeField] int cubeFaceSize = 1536;

        public RenderTexture Equirect { get; private set; }

        Camera cam;
        RenderTexture cube;

        void Awake() => EnsureTargets();

        void LateUpdate() => Capture();

        void OnDestroy()
        {
            if (cube != null) cube.Release();
            if (Equirect != null) Equirect.Release();
        }

        public void EnsureTargets()
        {
            if (Equirect != null) return;
            cam = GetComponent<Camera>();
            cam.enabled = false; // kendi başına çizmez, Capture() çağırır
            cube = new RenderTexture(cubeFaceSize, cubeFaceSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
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
        }
    }
}
