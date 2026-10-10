using UnityEngine;

namespace ARaki.Robot
{
    /// <summary>
    /// Gerçek robot gelene kadar kullanılan simülasyon: dört tekerlekli kızaklı (skid-steer) sürüş,
    /// motor gecikmesi, batarya, PWM limiti ve Arduino'daki gibi "komut kesilirse dur" güvenliği.
    /// Birimler metre / saniye. Obje, robotun dünyadaki karşılığını hareket ettirir (XZ düzlemi).
    /// Sağ ve sol taraftaki ikişer tekerlek aynı komutu alır; zıt yönde sürülürse robot olduğu yerde döner.
    /// </summary>
    public class MockRobotLink : MonoBehaviour, IRobotLink
    {
        [Header("Fiziksel model")]
        [SerializeField] float maxWheelSpeed = 0.5f;      // m/s (yaklaşık 1.8 km/h)
        [SerializeField] float trackWidth = 0.24f;        // sol ve sağ tekerlek hattı arası, m
        [SerializeField] float motorAcceleration = 1.2f;  // m/s², motor tepkisi
        [SerializeField, Range(0.3f, 1f)] float turnEfficiency = 0.75f; // kızaklı dönüşte tekerlek kayması
        [SerializeField] float collisionRadius = 0.17f;   // engellere çarpma yarıçapı, m

        [Header("Batarya")]
        [SerializeField, Range(0f, 1f)] float startBattery = 0.65f;
        [SerializeField] float batteryDrainPerSecond = 0.004f; // tam güçte
        [SerializeField] float batteryIdleDrain = 0.0003f;
        [SerializeField] float ledDrainPerSecond = 0.02f;

        [Header("Güvenlik")]
        [SerializeField] float commandTimeout = 0.5f; // bu süre komut gelmezse motorlar durur

        public bool IsConnected => true;
        public RobotTelemetry Telemetry { get; private set; }
        public float PwmLimit { get; set; } = 0.6f;

        public float TrackWidth => trackWidth;
        public float MaxWheelSpeed => maxWheelSpeed;
        public DriveCommand LastCommand { get; private set; }
        public bool FailsafeActive { get; private set; }
        public bool Blocked { get; private set; }
        public bool LedOn { get; private set; }

        DriveCommand command;
        float lastCommandTime = -999f;
        float left, right;      // gerçekleşen tekerlek hızları
        float yawDegrees;
        float battery;

        void Awake() => Initialize();

        /// <summary>Başlangıç durumunu kurar (Awake çalışmayan editör çekimleri için ayrıca açık).</summary>
        public void Initialize()
        {
            battery = startBattery;
            yawDegrees = transform.eulerAngles.y;
            Publish(0f);
        }

        public void SendDrive(DriveCommand c)
        {
            command = new DriveCommand(Mathf.Clamp(c.Left, -1f, 1f), Mathf.Clamp(c.Right, -1f, 1f));
            LastCommand = command;
            lastCommandTime = Time.time;
        }

        /// <summary>Simülasyon: robotu doğrudan bir konuma ve yöne yerleştirir (testler, ekran görüntüleri).</summary>
        public void Teleport(Vector3 position, float yawDeg)
        {
            transform.position = position;
            yawDegrees = yawDeg;
            transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
            left = right = 0f;
        }

        public void SetLed(bool on) => LedOn = on && battery > 0f;

        /// <summary>Simülasyon: oyun olayları (yakıt istasyonu, düşman hasarı) bataryayı değiştirir.</summary>
        public void AddBattery(float delta) => battery = Mathf.Clamp01(battery + delta);

        void Update()
        {
            float dt = Time.deltaTime;
            FailsafeActive = Time.time - lastCommandTime > commandTimeout;
            DriveCommand active = FailsafeActive || battery <= 0f ? DriveCommand.Stop : command;

            float step = motorAcceleration * dt;
            left = Mathf.MoveTowards(left, active.Left * PwmLimit * maxWheelSpeed, step);
            right = Mathf.MoveTowards(right, active.Right * PwmLimit * maxWheelSpeed, step);

            float forward = (left + right) * 0.5f;
            float omega = (right - left) / trackWidth * turnEfficiency; // rad/s, sağ hızlıysa sola döner

            // Unity'de Y ekseni etrafındaki pozitif dönüş saat yönüdür, o yüzden eksi.
            yawDegrees -= omega * Mathf.Rad2Deg * dt;
            transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);

            Vector3 next = transform.position + transform.forward * (forward * dt);
            Blocked = Physics.CheckSphere(next + Vector3.up * 0.07f, collisionRadius, ~0, QueryTriggerInteraction.Ignore);
            if (Blocked)
            {
                left = right = 0f; // çarptı: motorlar durur
                forward = 0f;
            }
            else
            {
                transform.position = next;
            }

            float load = (Mathf.Abs(left) + Mathf.Abs(right)) / (2f * maxWheelSpeed);
            float drain = batteryIdleDrain + batteryDrainPerSecond * load + (LedOn ? ledDrainPerSecond : 0f);
            battery = Mathf.Max(0f, battery - drain * dt);
            if (battery <= 0f) LedOn = false;

            Publish(forward);
        }

        void Publish(float forward)
        {
            Telemetry = new RobotTelemetry
            {
                Battery01 = battery,
                YawDegrees = Mathf.Repeat(yawDegrees, 360f),
                LeftSpeed = left,
                RightSpeed = right,
                ForwardSpeed = forward,
            };
        }
    }
}
