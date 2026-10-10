using UnityEngine;
using UnityEngine.InputSystem;

namespace ARaki.Video
{
    /// <summary>
    /// 360° görüntüyü bir kürenin içine kaplar ve kürenin merkezindeki kamerayla "bakılan yön"ü seçtirir.
    /// Robot yönünden bağımsız etrafa bakılır (Q/E, fare sağ tuş + sürükle, Boşluk = öne dön).
    /// Aynı yapı gerçek Theta S videosuyla da çalışır; tek fark dokunun kaynağıdır.
    /// </summary>
    public class PanoramaViewer : MonoBehaviour
    {
        [SerializeField] Sim360Camera source;
        [SerializeField] Camera viewCamera;
        [SerializeField] MeshRenderer sphere;
        [Tooltip("Simülasyon çekimi dünyaya göre sabit çıkar; bu doluysa küre robotun yönüne göre döndürülür. " +
                 "Gerçek kamera görüntüsü zaten robota göre olduğundan orada boş bırakılır.")]
        [SerializeField] Transform worldAlignedRobot;
        [SerializeField] float keyLookSpeed = 80f;  // derece/sn
        [SerializeField] float mouseSensitivity = 0.12f;
        [SerializeField] float defaultPitch = 20f; // pozitif = aşağı bak

        public float Yaw { get; private set; }
        public float Pitch { get; private set; }
        public Camera ViewCamera => viewCamera;
        public float DefaultPitch => defaultPitch;

        void Start()
        {
            Pitch = defaultPitch;
            Bind();
            Apply();
        }

        public void Bind()
        {
            source.EnsureTargets();
            var filter = sphere.GetComponent<MeshFilter>();
            if (filter.sharedMesh == null || filter.sharedMesh.name != "EquirectSphere")
                filter.sharedMesh = EquirectSphere.Create();
            var mat = sphere.sharedMaterial;
            mat.SetTexture("_BaseMap", source.Equirect);
            mat.SetTextureScale("_BaseMap", Vector2.one);   // aynalama yok: UV'ler zaten doğru yönde
            mat.SetTextureOffset("_BaseMap", Vector2.zero);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            if (kb != null)
            {
                if (kb.eKey.isPressed) Yaw += keyLookSpeed * dt;
                if (kb.qKey.isPressed) Yaw -= keyLookSpeed * dt;
                if (kb.spaceKey.isPressed)
                {
                    Yaw = Mathf.MoveTowardsAngle(Yaw, 0f, 240f * dt);
                    Pitch = Mathf.MoveTowards(Pitch, defaultPitch, 120f * dt);
                }
            }

            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue();
                Yaw += d.x * mouseSensitivity;
                Pitch -= d.y * mouseSensitivity;
            }

            Yaw = Mathf.Repeat(Yaw + 180f, 360f) - 180f;
            Pitch = Mathf.Clamp(Pitch, -70f, 70f);
            Apply();
        }

        public void SetLook(float yaw, float pitch)
        {
            Yaw = yaw;
            Pitch = pitch;
            Apply();
        }

        public void Apply()
        {
            viewCamera.transform.localRotation = Quaternion.Euler(Pitch, Yaw, 0f);
            if (worldAlignedRobot != null)
                sphere.transform.localRotation = Quaternion.Euler(0f, -worldAlignedRobot.eulerAngles.y, 0f);
        }
    }
}
