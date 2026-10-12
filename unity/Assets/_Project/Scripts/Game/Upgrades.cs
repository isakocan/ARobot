using UnityEngine;

namespace ARaki.Game
{
    public enum UpgradeId { EfficientLed, TurboMotors, QuickCapture, BigBattery, WideSensor, SonarPlus }

    public readonly struct UpgradeDef
    {
        public readonly UpgradeId Id;
        public readonly string Title;
        public readonly string Description;
        public readonly int MaxLevel;

        public UpgradeDef(UpgradeId id, string title, string description, int maxLevel)
        {
            Id = id;
            Title = title;
            Description = description;
            MaxLevel = maxLevel;
        }
    }

    /// <summary>Yükseltmeler robotun gerçek davranışını değiştirir (ör. motor PWM limiti). Bedeli bulgulardan gelen "öz"dür.</summary>
    public static class UpgradeCatalog
    {
        public static readonly UpgradeDef[] All =
        {
            new UpgradeDef(UpgradeId.EfficientLed, "VERİMLİ LED", "LED %40 daha az pil harcar. Işıkla çağırmak ucuzlar.", 3),
            new UpgradeDef(UpgradeId.TurboMotors, "TURBO MOTORLAR", "Motor güç limiti +%15. Robot hızlanır, pili de hızlı yer.", 3),
            new UpgradeDef(UpgradeId.QuickCapture, "HIZLI KAYIT", "Bir bulguyu kaydetme süresi %25 kısalır.", 3),
            new UpgradeDef(UpgradeId.BigBattery, "GENİŞ BATARYA", "Pil %20 daha yavaş tükenir.", 3),
            new UpgradeDef(UpgradeId.WideSensor, "GENİŞ SENSÖR", "Mesafe sensörü menzili +0,5 m. Daha uzaktan görür, haritayı hızlı çıkarır.", 3),
            new UpgradeDef(UpgradeId.SonarPlus, "GÜÇLÜ SONAR", "Sonar menzili +0,8 m ve daha çabuk dolar.", 3),
        };

        public static UpgradeDef Get(UpgradeId id) => All[(int)id];
    }

    /// <summary>Oyuncunun kalıcı ilerlemesi: seçtiği yükseltmeler ve bunlardan türeyen değerler.</summary>
    public class PlayerProfile
    {
        readonly int[] levels = new int[UpgradeCatalog.All.Length];

        public int[] Levels => levels;
        public int Level(UpgradeId id) => levels[(int)id];
        public bool CanUpgrade(UpgradeId id) => Level(id) < UpgradeCatalog.Get(id).MaxLevel;
        public void Add(UpgradeId id) { if (CanUpgrade(id)) levels[(int)id]++; }
        public void Reset() => System.Array.Clear(levels, 0, levels.Length);
        public int Cost(UpgradeId id) => 3 + 2 * Level(id);

        public float LedDrainMultiplier => Mathf.Pow(0.6f, Level(UpgradeId.EfficientLed));
        public float MaxPwm => Mathf.Min(1f, 0.6f + 0.15f * Level(UpgradeId.TurboMotors));
        public float CaptureTimeMultiplier => Mathf.Pow(0.75f, Level(UpgradeId.QuickCapture));
        public float DrainMultiplier => Mathf.Pow(0.8f, Level(UpgradeId.BigBattery));
        public float SensorRange => 3f + 0.5f * Level(UpgradeId.WideSensor);
        public float SonarRadius => 3f + 0.8f * Level(UpgradeId.SonarPlus);
        public float SonarCooldown => 5f * Mathf.Pow(0.85f, Level(UpgradeId.SonarPlus));
    }
}
