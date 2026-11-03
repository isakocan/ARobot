using System;
using ARaki.Net;
using UnityEngine;

namespace ARaki.Robot
{
    /// <summary>Köprünün ~10 Hz gönderdiği durum mesajı. Alan adları köprüdeki JSON ile aynı.</summary>
    [Serializable]
    public class BridgeStatus
    {
        [Serializable]
        public class Motor
        {
            public int sol, sag, sol_ma, sag_ma, melodi;
            public bool ariza, led, dortlu, motor_sesi, geri_bip;
        }

        public string t;
        public bool arduino;
        public Motor motor;
        public string kamera;     // kapali | yok | baslatiliyor | aktif
        public float fps;
        public bool h264;
        public int sinyal;        // dBm (0 = bilinmiyor)
        public int veri_kbps;
        public int atilan;        // geç geldiği için atılan sürüş komutu sayısı
    }

    /// <summary>
    /// Gerçek robot: IRobotLink'i Wi-Fi üzerinden Jetson'daki sürüş köprüsüne bağlar.
    /// Sürüş, tarayıcıdaki sürüş sayfasıyla aynı kurallarla gider:
    /// hareket varken ~60 ms'de bir komut (Arduino 300 ms komut alamazsa durur), durunca bir kez sıfır,
    /// bağlantı zayıfken (900 ms pong yok) hareket komutu yerine sıfır.
    /// Rampa (yumuşak kalkış/duruş) Arduino'da olduğu için burada yapılmaz.
    /// </summary>
    public class RealRobotLink : MonoBehaviour, IRobotLink
    {
        public const string AddressKey = "robot_adresi";

        [Tooltip("Boşsa son kullanılan adres, o da yoksa jetson.local denenir.")]
        [SerializeField] string host = "";
        [SerializeField, Range(0.1f, 1f)] float pwmLimit = 0.6f;
        [SerializeField] float sendInterval = 0.055f;
        [SerializeField] float weakAfterMs = 900f;

        RobotBridge bridge;
        DriveCommand command;
        string lastSent = "0,0";
        float lastSendTime;
        bool ledOn;
        float ledChangedAt = -10f;

        public string Host => bridge?.Host ?? host;
        public bool IsConnected => bridge != null && bridge.Connected;
        /// <summary>Bağlı ama pong gecikiyor: komutlar robota ulaşmıyor olabilir.</summary>
        public bool IsWeak => IsConnected && bridge.SincePongMs > weakAfterMs;
        public double RoundTripMs => bridge?.RoundTripMs ?? -1;
        public string LastError => bridge?.LastError ?? "";
        public BridgeStatus Status { get; private set; }
        public RobotTelemetry Telemetry { get; private set; }
        public bool LedOn => ledOn;

        public float PwmLimit
        {
            get => pwmLimit;
            set => pwmLimit = Mathf.Clamp(value, 0.1f, 1f);
        }

        public event Action<BridgeStatus> StatusReceived;

        void Start()
        {
            string env = Environment.GetEnvironmentVariable("ARAKI_ROBOT");
            if (!string.IsNullOrEmpty(env)) host = env;
            if (string.IsNullOrEmpty(host)) host = PlayerPrefs.GetString(AddressKey, "jetson.local");
            Connect(host);
        }

        /// <summary>Başka bir adrese bağlanır ve adresi hatırlar.</summary>
        public void Connect(string address)
        {
            bridge?.Dispose();
            host = address.Trim();
            PlayerPrefs.SetString(AddressKey, host);
            bridge = new RobotBridge(host);
        }

        void OnDestroy()
        {
            if (bridge != null && bridge.Connected) bridge.Drive(0, 0);
            bridge?.Dispose();
        }

        public void SendDrive(DriveCommand c) => command = c;

        public void SetLed(bool on)
        {
            if (on == ledOn) return;
            ledOn = on;
            ledChangedAt = Time.unscaledTime;
            bridge?.Send("{\"t\":\"led\",\"acik\":" + (on ? "true" : "false") + "}");
        }

        public void Horn(int ms = 250) => bridge?.Send("{\"t\":\"korna\",\"ms\":" + Mathf.Clamp(ms, 50, 1000) + "}");
        public void Hazard(bool on) => bridge?.Send("{\"t\":\"dortlu\",\"acik\":" + (on ? "true" : "false") + "}");
        public void PlayMelody(int number) => bridge?.Send("{\"t\":\"melodi\",\"no\":" + number + "}");

        void Update()
        {
            if (bridge == null) return;
            while (bridge.Incoming.TryDequeue(out var json))
            {
                if (!json.Contains("\"durum\"")) continue;
                var s = JsonUtility.FromJson<BridgeStatus>(json);
                Status = s;
                // LED başka yerden (sürüş sayfası) değiştiyse benimse; az önce biz değiştirdiysek durum gecikmeli gelir
                if (s.motor != null && s.motor.led != ledOn && Time.unscaledTime - ledChangedAt > 0.5f) ledOn = s.motor.led;
                Telemetry = new RobotTelemetry { Battery01 = 1f };   // pil ölçümü henüz yok
                StatusReceived?.Invoke(s);
            }

            int left = Mathf.RoundToInt(Mathf.Clamp(command.Left, -1f, 1f) * pwmLimit * 255f);
            int right = Mathf.RoundToInt(Mathf.Clamp(command.Right, -1f, 1f) * pwmLimit * 255f);
            if (!IsConnected || IsWeak) left = right = 0;
            bool moving = left != 0 || right != 0;
            string key = left + "," + right;
            if (key != lastSent || (moving && Time.unscaledTime - lastSendTime > sendInterval))
            {
                if (IsConnected) bridge.Drive(left, right);
                lastSent = key;
                lastSendTime = Time.unscaledTime;
            }
        }
    }
}
