using UnityEngine;

namespace ARaki.Game
{
    public enum UpgradeId { EfficientLed, TurboMotors, FastScan, BigBattery, NightVision }

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

    /// <summary>Yükseltmeler robotun gerçek davranışını değiştirir (ör. motor PWM limiti). Metinler ekranda gösterilir.</summary>
    public static class UpgradeCatalog
    {
        public static readonly UpgradeDef[] All =
        {
            new UpgradeDef(UpgradeId.EfficientLed, "VERİMLİ LED", "LED %40 daha az yakıt harcar. Düşmanları korkutmak ucuzlar.", 3),
            new UpgradeDef(UpgradeId.TurboMotors, "TURBO MOTORLAR", "Motor güç limiti +%15. Robot hızlanır, yakıtı da hızlı yer.", 3),
            new UpgradeDef(UpgradeId.FastScan, "HIZLI TARAYICI", "Tarama süresi %30 kısalır. Tehlikeli yerlerde daha az beklersin.", 3),
            new UpgradeDef(UpgradeId.BigBattery, "GENİŞ BATARYA", "Göreve %10 daha fazla yakıtla başlarsın.", 3),
            new UpgradeDef(UpgradeId.NightVision, "GECE GÖZÜ", "Radar menzili +%50. Tehditleri daha erken görürsün.", 2),
        };

        public static UpgradeDef Get(UpgradeId id) => All[(int)id];
    }

    /// <summary>Oyuncunun kalıcı ilerlemesi: seçtiği yükseltmeler ve bunlardan türeyen değerler.</summary>
    public class PlayerProfile
    {
        readonly int[] levels = new int[UpgradeCatalog.All.Length];

        public int Level(UpgradeId id) => levels[(int)id];
        public bool CanUpgrade(UpgradeId id) => Level(id) < UpgradeCatalog.Get(id).MaxLevel;
        public void Add(UpgradeId id) { if (CanUpgrade(id)) levels[(int)id]++; }
        public void Reset() => System.Array.Clear(levels, 0, levels.Length);

        public float LedDrainMultiplier => Mathf.Pow(0.6f, Level(UpgradeId.EfficientLed));
        public float MaxPwm => Mathf.Min(1f, 0.6f + 0.15f * Level(UpgradeId.TurboMotors));
        public float ScanTimeMultiplier => Mathf.Pow(0.7f, Level(UpgradeId.FastScan));
        public float StartBattery => Mathf.Min(1f, 0.7f + 0.1f * Level(UpgradeId.BigBattery));
        public float RadarRange => 4f * (1f + 0.5f * Level(UpgradeId.NightVision));
    }
}
