using UnityEngine;

namespace ARaki.Video
{
    /// <summary>
    /// Dikiz aynası: 360° görüntünün robotun arkasını gösteren küçük bir ikinci görünümü.
    /// Tek kamera yeter, çünkü her yön zaten panoramada var; ikinci bir görünüm kamerası aynı küreyi ters yöne bakar.
    /// </summary>
    public class RearMirror : MonoBehaviour
    {
        [SerializeField] PanoramaViewer viewer;

        Camera cam;
        RenderTexture target;

        public Texture Texture => target;

        public void Init(PanoramaViewer v, int width = 384, int height = 128)
        {
            viewer = v;
            if (!Application.isPlaying) return;
            var main = viewer.ViewCamera;
            var go = new GameObject("RearMirrorCamera");
            go.transform.SetParent(main.transform.parent, false);
            cam = go.AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.fieldOfView = 55f;
            cam.depth = main.depth - 1;
            target = new RenderTexture(width, height, 16) { name = "RearMirror" };
            cam.targetTexture = target;
            cam.enabled = false;
        }

        public bool Active
        {
            get => cam != null && cam.enabled;
            set { if (cam != null && cam.enabled != value) cam.enabled = value; }
        }

        void LateUpdate()
        {
            if (cam == null || viewer == null) return;
            var main = viewer.ViewCamera.transform;
            cam.transform.localPosition = main.localPosition;
            cam.transform.localRotation = Quaternion.Euler(12f, 180f, 0f);   // robotun tam arkası (görüş yönünden bağımsız)
        }

        void OnDestroy()
        {
            if (target != null) { target.Release(); Destroy(target); }
            if (cam != null) Destroy(cam.gameObject);
        }
    }
}
