using ARaki.AR;
using ARaki.World;
using UnityEngine;

namespace ARaki.Game
{
    /// <summary>
    /// Bulgu türleri; her biri farklı bir "fiil" ister:
    /// Shy = sessiz yaklaş, Lure = LED ile çağır, Hidden = sonarla ortaya çıkar, Relic = yanında bekle, Legend = ışıkla çağır ve sessiz kal.
    /// </summary>
    public enum FindKind { Shy = 0, Lure = 1, Hidden = 2, Relic = 3, Legend = 4 }

    public enum FindState { Idle, Fleeing, Gone, Captured }

    /// <summary>
    /// Evde bulunabilecek bir şey (yaratık, parça, ipucu). Görünmez bir dünya nesnesidir; çizimi AR katmanındaki
    /// <see cref="FindVisual"/> yapar. Davranışı <see cref="Expedition"/> yürütür.
    /// </summary>
    public class Find : MonoBehaviour
    {
        public FindKind Kind;
        public int Tier = 1;                 // 1 yaygın, 2 nadir, 3 çok nadir
        public RoomInfo Room;
        public Vector3 Home;
        public FindState State;
        public float Progress;               // yakalama ilerlemesi 0..1
        public bool Revealed;                // oyuncu gördü mü
        public float PingUntil;              // sonarla görünür kalma süresi sonu
        public int Flees;
        public float FleeTimer;
        public bool RevealPlayed;             // belirme animasyonu oynatıldı mı
        public float Alarm;                  // ürkek bulgu: tedirginlik süresi (dolunca kaçar)
        public Vector3 FleeTarget;
        public int Essence = 1;
        public float ExpireAt;               // nadir bulgular görüldükten sonra bu süre dolunca kaybolur (0 = kaybolmaz)
        public FindVisual Visual;

        public Vector3 Position => new Vector3(transform.position.x, 0f, transform.position.z);
        public bool Alive => State == FindState.Idle || State == FindState.Fleeing;

        public static int EssenceFor(FindKind kind, int tier)
        {
            int baseValue = kind == FindKind.Shy ? 2 : kind == FindKind.Lure ? 2 : kind == FindKind.Hidden ? 3 : kind == FindKind.Legend ? 10 : 1;
            return baseValue + (tier - 1) * 2;
        }

        /// <summary>Yakalama süresi (sn): kind'a göre; hızlı yakalama yükseltmesi çarpanla kısaltır.</summary>
        public static float CaptureSeconds(FindKind kind) =>
            kind == FindKind.Shy ? 1.4f : kind == FindKind.Lure ? 1.6f : kind == FindKind.Legend ? 3f : 1.5f;
    }
}
