using System;
using System.Threading;
using ARaki.Net;
using UnityEngine;

namespace ARaki.Robot
{
    /// <summary>
    /// Gerçek robot ya da robotsuz demo. Gerçek modda robotun adresini kendisi bulur (RobotDiscovery) ve köprünün
    /// UDP kanalına bağlanır; demoda aynı kurallarla çalışan DemoRobotConnection kullanılır.
    /// Sürüş kodu (DriveController) her karede istenen durumu State'e yazar; bağlantı zayıfken (900 ms cevap yok)
    /// hareket istenmez, sıfır gider. Rampa (yumuşak kalkış/duruş) Arduino'da.
    /// </summary>
    public class RealRobotLink : MonoBehaviour, IRobotLink
    {
        public const string DemoKey = "robot_demo";

        [SerializeField] BuzzerSynth buzzer;
        [SerializeField] float weakAfterMs = 900f;

        IRobotConnection connection;
        CancellationTokenSource search;
        float pwmLimit = 0.6f;

        public bool Demo { get; private set; }
        /// <summary>Bağlantı yokken ne yapıldığı: "robot aranıyor", "bulunamadı" ...</summary>
        public string SearchText { get; private set; } = "";
        public bool Searching => search != null;
        public string Host => connection?.Host;
        public bool IsConnected => connection != null && connection.Connected;
        public bool IsWeak => connection != null && connection.SinceReplyMs > weakAfterMs;
        public double RoundTripMs => connection?.RoundTripMs ?? -1;
        public float Loss => connection?.Loss ?? 0f;
        public RobotStatus Status => connection?.Status;
        public RobotTelemetry Telemetry => new RobotTelemetry { Battery01 = 1f };   // pil ölçümü henüz yok

        /// <summary>Uygulamanın istediği durum; DriveController yazar.</summary>
        public ControlState State;

        public event Action ConnectionChanged;

        public float PwmLimit { get => pwmLimit; set => pwmLimit = Mathf.Clamp(value, 0.1f, 1f); }
        public bool LedOn => State.Led;
        public void SetLed(bool on) => State.Led = on;
        public void SendDrive(DriveCommand c)
        {
            State.Left = Mathf.RoundToInt(Mathf.Clamp(c.Left, -1f, 1f) * pwmLimit * 255f);
            State.Right = Mathf.RoundToInt(Mathf.Clamp(c.Right, -1f, 1f) * pwmLimit * 255f);
        }

        void Start()
        {
            string env = Environment.GetEnvironmentVariable("ARAKI_ROBOT");
            if (env == "demo") UseDemo();
            else if (!string.IsNullOrEmpty(env)) UseRobot(env);
            else if (PlayerPrefs.GetInt(DemoKey, 1) == 1) UseDemo();
            else FindRobot();
        }

        public void UseDemo()
        {
            Stop();
            Demo = true;
            PlayerPrefs.SetInt(DemoKey, 1);
            connection = new DemoRobotConnection(buzzer);
            SearchText = "";
            ConnectionChanged?.Invoke();
        }

        /// <summary>Robotu ağda arar; bulunca bağlanır.</summary>
        public async void FindRobot()
        {
            Stop();
            Demo = false;
            PlayerPrefs.SetInt(DemoKey, 0);
            ConnectionChanged?.Invoke();
            var cts = search = new CancellationTokenSource();
            SearchText = "robot aranıyor";
            string last = PlayerPrefs.GetString(RobotDiscovery.AddressKey, "");
            string found = null;
            try
            {
                found = await System.Threading.Tasks.Task.Run(() =>
                    RobotDiscovery.FindAsync(last, s => SearchText = s, cts.Token));
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e) { Debug.LogWarning("[Robot] arama: " + e.Message); }
            if (cts != search) return;   // bu arada başka bir şey seçildi
            search = null;
            if (found == null) { SearchText = "robot bulunamadı: aynı Wi-Fi'da mı, açık mı?"; ConnectionChanged?.Invoke(); return; }
            UseRobot(found);
        }

        public void UseRobot(string host)
        {
            Stop();
            Demo = false;
            try
            {
                connection = new UdpRobotConnection(host);
                PlayerPrefs.SetString(RobotDiscovery.AddressKey, host);
                SearchText = "";
            }
            catch (Exception e) { SearchText = "bağlanılamadı: " + e.Message; }
            ConnectionChanged?.Invoke();
        }

        void Stop()
        {
            search?.Cancel();
            search = null;
            connection?.Dispose();
            connection = null;
        }

        void Update()
        {
            if (connection == null) return;
            if (connection is DemoRobotConnection demo) demo.Tick();
            var s = State;
            if (IsWeak) s.Left = s.Right = 0;   // ulaşamıyorsak hareket isteme
            connection.Send(s);
        }

        void OnDestroy() => Stop();
    }
}
