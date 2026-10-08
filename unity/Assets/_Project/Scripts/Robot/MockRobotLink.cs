using UnityEngine;

namespace ARaki.Robot
{
    /// <summary>
    /// Gerçek robot gelene kadar kullanılan simülasyon: diferansiyel sürüş, motor gecikmesi,
    /// batarya, PWM limiti ve Arduino'daki gibi "komut kesilirse dur" güvenliği.
    /// Birimler metre / saniye. Obje, robotun dünyadaki karşılığını hareket ettirir (XZ düzlemi).
    /// </summary>
    public class MockRobotLink : MonoBehaviour, IRobotLink
    {
        [Header("Fiziksel model")]
        [SerializeField] float maxWheelSpeed = 0.5f;      // m/s (yaklaşık 1.8 km/h)
        [SerializeField] float trackWidth = 0.20f;        // tekerlekler arası mesafe, m
        [SerializeField] float motorAcceleration = 1.2f;  // m/s², motor tepkisi

        [Header("Batarya")]
        [SerializeField] float batteryDrainPerSecond = 0.004f; // tam güçte
        [SerializeField] float batteryIdleDrain = 0.0003f;

        [Header("Güvenlik")]
        [SerializeField] float commandTimeout = 0.5f; // bu süre komut gelmezse motorlar durur

        public bool IsConnected => true;
        public RobotTelemetry Telemetry { get; private set; }
        public float PwmLimit { get; set; } = 0.6f;

        public float TrackWidth => trackWidth;
        public float MaxWheelSpeed => maxWheelSpeed;
        public DriveCommand LastCommand { get; private set; }
        public bool FailsafeActive { get; private set; }

        DriveCommand command;
        float lastCommandTime = -999f;
        float left, right;      // gerçekleşen tekerlek hızları
        float yawDegrees;
        float battery = 1f;

        void Awake()
        {
            yawDegrees = transform.eulerAngles.y;
            Publish(0f);
        }

        public void SendDrive(DriveCommand c)
        {
            command = new DriveCommand(Mathf.Clamp(c.Left, -1f, 1f), Mathf.Clamp(c.Right, -1f, 1f));
            LastCommand = command;
            lastCommandTime = Time.time;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            FailsafeActive = Time.time - lastCommandTime > commandTimeout;
            DriveCommand active = FailsafeActive || battery <= 0f ? DriveCommand.Stop : command;

            float step = motorAcceleration * dt;
            left = Mathf.MoveTowards(left, active.Left * PwmLimit * maxWheelSpeed, step);
            right = Mathf.MoveTowards(right, active.Right * PwmLimit * maxWheelSpeed, step);

            float forward = (left + right) * 0.5f;
            float omega = (right - left) / trackWidth; // rad/s, sağ hızlıysa sola döner

            // Unity'de Y ekseni etrafındaki pozitif dönüş saat yönüdür, o yüzden eksi.
            yawDegrees -= omega * Mathf.Rad2Deg * dt;
            transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            transform.position += transform.forward * (forward * dt);

            float load = (Mathf.Abs(left) + Mathf.Abs(right)) / (2f * maxWheelSpeed);
            battery = Mathf.Max(0f, battery - (batteryIdleDrain + batteryDrainPerSecond * load) * dt);

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
