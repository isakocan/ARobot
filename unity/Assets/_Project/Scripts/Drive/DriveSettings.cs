using UnityEngine;

namespace ARaki.Drive
{
    public enum WheelSide { Left, Center, Right }
    public enum SpeedUnit { KilometersPerHour, MetersPerSecond }

    /// <summary>Sürüş ayarları; cihazda (PlayerPrefs) saklanır.</summary>
    public class DriveSettings
    {
        public bool Gears = true;
        public bool ReverseBeep = true;
        public bool CameraMotion = true;
        public bool Mirrors = true;
        public bool Cockpit = true;
        public bool TouchControls = Application.isMobilePlatform;
        public WheelSide Wheel = WheelSide.Left;
        public SpeedUnit Unit = SpeedUnit.KilometersPerHour;
        public int HornStyle;               // 0 düz, 1 kalın, 2 tiz, 3 titrek iki ton
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
            s.Gears = Get(Names[0], s.Gears);
            s.ReverseBeep = Get(Names[1], s.ReverseBeep);
            s.CameraMotion = Get(Names[2], s.CameraMotion);
            s.Mirrors = Get(Names[3], s.Mirrors);
            s.Cockpit = Get(Names[4], s.Cockpit);
            s.TouchControls = Get(Names[5], s.TouchControls);
            s.Wheel = (WheelSide)PlayerPrefs.GetInt("wheel", (int)s.Wheel);
            s.Unit = (SpeedUnit)PlayerPrefs.GetInt("unit", (int)s.Unit);
            s.HornStyle = PlayerPrefs.GetInt("hornStyle", s.HornStyle);
            s.PowerLimit = PlayerPrefs.GetFloat("power", s.PowerLimit);
            s.WheelDiameterCm = PlayerPrefs.GetFloat("wheelCm", s.WheelDiameterCm);
            return s;
        }

        public void Save()
        {
            Set(Names[0], Gears); Set(Names[1], ReverseBeep); Set(Names[2], CameraMotion);
            Set(Names[3], Mirrors); Set(Names[4], Cockpit); Set(Names[5], TouchControls);
            PlayerPrefs.SetInt("wheel", (int)Wheel);
            PlayerPrefs.SetInt("unit", (int)Unit);
            PlayerPrefs.SetInt("hornStyle", HornStyle);
            PlayerPrefs.SetFloat("power", PowerLimit);
            PlayerPrefs.SetFloat("wheelCm", WheelDiameterCm);
            PlayerPrefs.Save();
        }

        /// <summary>Tam güçte tekerlek devri (dk⁻¹): motor devri gerilimle orantılıdır.</summary>
        public float FullRpm => MotorRpmAt12V * BatteryVolts / 12f * LoadFactor;

        /// <summary>Motor gücü (-1..1, Arduino'nun bildirdiği) → m/s.</summary>
        public float SpeedMs(float power) => Mathf.Abs(power) * FullRpm / 60f * Mathf.PI * WheelDiameterCm / 100f;

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
