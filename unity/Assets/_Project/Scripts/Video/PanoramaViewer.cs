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
        [SerializeField] ARaki.Robot.MockRobotLink motion;   // doluysa robotun hızına göre hafif titreşim eklenir
        [SerializeField] float shakeDegrees = 0.35f;

        /// <summary>Menüde kullanıcı girişi yerine yavaş dönen "tanıtım" bakışı.</summary>
        public bool AutoRotate { get; set; }

        /// <summary>Dışarıdan eklenen sarsıntı (derece): güneş parlaması, elektrik şoku...</summary>
        public float ExtraShake { get; set; }

        /// <summary>Kamera hareketi (derece): eğim + = yukarı bak, yatma + = sağa dönerken dışa (sola) yatma.</summary>
        public float SwayPitch { get; set; }
        public float SwayRoll { get; set; }

        /// <summary>Kapalıysa klavye tuşlarını (Q/E, B, Z, boşluk) başka bileşen yönetir (gerçek robot sürüşü).</summary>
        public bool UseKeyboard { get; set; } = true;

        /// <summary>Yumuşakça öne (0) ya da arkaya (180) dön.</summary>
        public void LookFront() => SnapTo(0f);
        public void LookBack() => SnapTo(lookedBack ? 0f : 180f);

        float? snapYaw;
        float baseFov;
        bool lookedBack;

        /// <summary>Bakışı verilen yöne (robota göre derece) hızla çevirir; elle bakmaya başlayınca iptal olur.</summary>
        public void SnapTo(float yaw) { snapYaw = yaw; lookedBack = Mathf.Abs(Mathf.DeltaAngle(yaw, 180f)) < 1f; }

        public bool Zooming { get; private set; }

        public float Yaw { get; private set; }
        public float Pitch { get; private set; }
        public Camera ViewCamera => viewCamera;
        public float DefaultPitch => defaultPitch;

        void Start()
        {
            Pitch = defaultPitch;
            baseFov = viewCamera.fieldOfView;
            Bind();
            Apply();
            if (source != null) source.TargetsRecreated += Bind;     // kalite değişince doku yeniden bağlanır
        }

        void OnDestroy()
        {
            if (source != null) source.TargetsRecreated -= Bind;
        }

        public void Bind()
        {
            var filter = sphere.GetComponent<MeshFilter>();
            if (filter.sharedMesh == null || filter.sharedMesh.name != "EquirectSphere")
                filter.sharedMesh = EquirectSphere.Create();
            if (source == null) return;    // gerçek robot: dokuyu ve malzemeyi RobotVideo / RobotCameraMount verir
            source.EnsureTargets();
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

            if (AutoRotate)
            {
                Yaw += 7f * dt;
                Pitch = Mathf.Lerp(Pitch, defaultPitch * 0.6f, dt * 2f);
                kb = null;
                mouse = null;
            }

            if (!UseKeyboard) kb = null;
            if (kb != null)
            {
                if (kb.eKey.isPressed || kb.qKey.isPressed) snapYaw = null;
                if (kb.eKey.isPressed) Yaw += keyLookSpeed * dt;
                if (kb.qKey.isPressed) Yaw -= keyLookSpeed * dt;
                if (kb.spaceKey.isPressed)
                {
                    snapYaw = null;
                    Yaw = Mathf.MoveTowardsAngle(Yaw, 0f, 240f * dt);
                    Pitch = Mathf.MoveTowards(Pitch, defaultPitch, 120f * dt);
                }
                // B: tek dokunuşla arkaya bak, tekrar basınca öne dön
                if (kb.bKey.wasPressedThisFrame) SnapTo(lookedBack ? 0f : 180f);
                Zooming = kb.zKey.isPressed;
            }

            if (snapYaw.HasValue)
            {
                Yaw = Mathf.MoveTowardsAngle(Yaw, snapYaw.Value, 900f * dt);
                Pitch = Mathf.MoveTowards(Pitch, defaultPitch, 200f * dt);
                if (Mathf.Abs(Mathf.DeltaAngle(Yaw, snapYaw.Value)) < 0.5f) snapYaw = null;
            }

            // Z (basılı): dürbün; sinsice yaklaşırken uzaktaki yaratığı arabayı oynatmadan inceler
            float targetFov = Zooming ? baseFov * 0.45f : baseFov;
            if (baseFov > 0f) viewCamera.fieldOfView = Mathf.MoveTowards(viewCamera.fieldOfView, targetFov, 120f * dt);

            if (mouse != null && mouse.rightButton.isPressed)
            {
                snapYaw = null;
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
            float amplitude = ExtraShake;
            if (motion != null)
            {
                var t = motion.Telemetry;
                float speed = Mathf.Clamp01(Mathf.Abs(t.ForwardSpeed) / 0.3f + Mathf.Abs(t.LeftSpeed - t.RightSpeed) / 0.4f);
                amplitude += shakeDegrees * speed;
            }
            float n = Time.time * 17f;
            Vector3 shake = new Vector3(Mathf.PerlinNoise(n, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, n) - 0.5f, Mathf.PerlinNoise(n, n) - 0.5f) * amplitude;
            viewCamera.transform.localRotation = Quaternion.Euler(Pitch + shake.x - SwayPitch, Yaw + shake.y, shake.z - SwayRoll);
            if (worldAlignedRobot != null)
                sphere.transform.localRotation = Quaternion.Euler(0f, -worldAlignedRobot.eulerAngles.y, 0f);
        }
    }
}
