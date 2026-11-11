using UnityEngine;

namespace ARaki.Drive
{
    public enum WheelSide { Left, Center, Right }
    public enum SpeedUnit { KilometersPerHour, MetersPerSecond }
    /// <summary>Otomatik: vites kendisi büyür · Manuel: sıralı, sürücü atar (E / Q, kulakçıklar) · Sabit: vites yok.</summary>
    public enum GearMode { Automatic, Manual, Fixed }
    /// <summary>Dokunmatik sürüş: kapalı · yön tuşu (tek basamaklı yön tuşu, direksiyonun karşı yanında; çapraz basınca
    /// gaz + dönüş) · pedal (oklar bir yanda, gaz ve fren pedalı öbür yanda) · direksiyon (direksiyonun simidini
    /// parmakla çevirerek dönülür, karşı yanda gaz ve fren pedalı).</summary>
    public enum TouchMode { Off, Pad, Pedals, Wheel }

    /// <summary>Sürüş ayarları; cihazda (PlayerPrefs) saklanır.</summary>
    public class DriveSettings
    {
        public GearMode Gearbox = GearMode.Automatic;
        public bool ReverseBeep = true;
        public bool CameraMotion = true;
        public bool RearMirror = true;
        public bool Cockpit = true;
        public TouchMode Touch = Application.isMobilePlatform ? TouchMode.Pad : TouchMode.Off;
        public WheelSide Wheel = WheelSide.Left;
        public SpeedUnit Unit = SpeedUnit.KilometersPerHour;
        public bool Effects = true;         // kısa sesler (açılış, bağlandı, koptu, foto, kayıt); varsayılan açık
        public int HornStyle;               // HornNames sırası; 5 ve üstü basınca bir kez çalan ezgili kornalar
        public static readonly string[] HornNames = { "Düz", "Kalın", "Tiz", "Titrek", "Dü-düt", "Havalı",
                                                      "La Cucaracha", "Dixie", "Baba", "Ta-ta-ra-ta-ta" };
        public const int HeldHorns = 6;     // ilk 6 basılı tuttukça öter, sonrası ezgi
        public int Video;                   // 0 otomatik (H.264, olmazsa JPEG), 1 H.264, 2 JPEG, 3 görünen kısım (VideoMode)
        public float PowerLimit = 0.6f;     // en yüksek güç (0.2..1)
        public float WheelDiameterCm = 6.5f;
        public float BatteryVolts = 14.8f;  // pil ölçümü gelene kadar 4S LiPo'nun ortalaması

        // Motor: Hsiang Neng HN-GH12-2217Y, 12 V'ta yüksüz 120 devir/dk, 50:1 dişli
        public const float MotorRpmAt12V = 120f;
        public const float LoadFactor = 0.85f;   // hafif yükte yüksüz devrin ~%85'i

        static readonly string[] Names = { "gears", "reverseBeep", "cameraMotion", "mirrors", "cockpit", "touch" };

        public static DriveSettings Load()
        {
            var s = new DriveSettings();
            s.Gearbox = (GearMode)PlayerPrefs.GetInt("gearbox", Get(Names[0], true) ? 0 : 2);   // eski "gears" ayarından
            s.ReverseBeep = Get(Names[1], s.ReverseBeep);
            s.CameraMotion = Get(Names[2], s.CameraMotion);
            s.RearMirror = Get("rearMirror", s.RearMirror);
            s.Cockpit = Get(Names[4], s.Cockpit);
            s.Touch = (TouchMode)PlayerPrefs.GetInt("touchMode", PlayerPrefs.HasKey(Names[5]) ? (Get(Names[5], false) ? 1 : 0) : (int)s.Touch);
            s.Wheel = (WheelSide)PlayerPrefs.GetInt("wheel", (int)s.Wheel);
            s.Unit = (SpeedUnit)PlayerPrefs.GetInt("unit", (int)s.Unit);
            s.HornStyle = PlayerPrefs.GetInt("hornStyle", s.HornStyle);
            s.Effects = Get("effects2", s.Effects);   // eski "effects" kapalı başlıyordu; yeni varsayılan açık
            s.Video = PlayerPrefs.GetInt("video", s.Video);
            s.PowerLimit = PlayerPrefs.GetFloat("power", s.PowerLimit);
            s.WheelDiameterCm = PlayerPrefs.GetFloat("wheelCm", s.WheelDiameterCm);
            return s;
        }

        public void Save()
        {
            PlayerPrefs.SetInt("gearbox", (int)Gearbox); Set(Names[1], ReverseBeep); Set(Names[2], CameraMotion);
            Set("rearMirror", RearMirror); Set(Names[4], Cockpit); PlayerPrefs.SetInt("touchMode", (int)Touch);
            PlayerPrefs.SetInt("wheel", (int)Wheel);
            PlayerPrefs.SetInt("unit", (int)Unit);
            PlayerPrefs.SetInt("hornStyle", HornStyle);
            Set("effects2", Effects);
            PlayerPrefs.SetInt("video", Video);
            PlayerPrefs.SetFloat("power", PowerLimit);
            PlayerPrefs.SetFloat("wheelCm", WheelDiameterCm);
            PlayerPrefs.Save();
        }

        /// <summary>Tam güçte tekerlek devri (dk⁻¹): motor devri gerilimle orantılıdır.</summary>
        public float FullRpm => MotorRpmAt12V * BatteryVolts / 12f * LoadFactor;

        /// <summary>Motor gücü (-1..1, Arduino'nun bildirdiği) → m/s.</summary>
        public float SpeedMs(float power) => Mathf.Abs(power) * FullRpm / 60f * Mathf.PI * WheelDiameterCm / 100f;

        /// <summary>Seçili birimde, en yüksek güçte varılan hız (hız göstergesinin sonu buna göre).</summary>
        public float TopSpeedShown => SpeedMs(PowerLimit) * (Unit == SpeedUnit.KilometersPerHour ? 3.6f : 1f);

        public string FormatSpeed(float ms, out string unit)
        {
            unit = Unit == SpeedUnit.KilometersPerHour ? "km/sa" : "m/sn";
            float v = Unit == SpeedUnit.KilometersPerHour ? ms * 3.6f : ms;
            return v.ToString(Unit == SpeedUnit.KilometersPerHour ? "0.0" : "0.00").Replace('.', ',');
        }

        static bool Get(string k, bool d) => PlayerPrefs.GetInt(k, d ? 1 : 0) == 1;
        static void Set(string k, bool v) => PlayerPrefs.SetInt(k, v ? 1 : 0);
    }
}
