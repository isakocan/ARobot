using ARaki.AR;
using UnityEngine;

namespace ARaki.Game
{
    public enum PointKind { Station, Fuel, Base }

    /// <summary>
    /// Görevdeki bir hedef: ORBİT-9 istasyonu, enerji hücresi ya da ana üs. Gerçek oyunda kameranın tanıdığı
    /// objeler bu rolleri alacak; şimdilik görev üreticisinin yerleştirdiği yer tutucular.
    /// İstasyon birkaç sektöre bölünür: her sektör, robot o yönden istasyonu tararken dolar.
    /// </summary>
    public class MissionPoint : MonoBehaviour
    {
        public string Label = "HEDEF";
        public PointKind Kind;
        public float Radius = 0.6f;       // taramanın geçerli olduğu en uzak mesafe, m
        public float MinRadius = 0f;      // bundan yakında tarama olmaz (istasyonun gövdesi)
        public float ScanSeconds = 2f;    // bir sektörü doldurmak için gereken süre
        public int SectorCount = 1;
        public bool Optional;             // zorunlu olmayan hedef (ör. yakıt)

        [SerializeField] HoloTint holo;
        [SerializeField] HoloBurst burst;
        [SerializeField] Transform physical;   // odadaki gerçek (3D baskı) nesne
        [SerializeField] StationVisual station;

        public float Progress { get; set; }     // tüm sektörlerin ortalaması
        public bool Done { get; set; }
        public bool Locked { get; set; }
        public int ActiveSector { get; set; } = -1;
        public float[] SectorProgress { get; private set; } = new float[1];
        public bool[] SectorDone { get; private set; } = new bool[1];
        public Transform Physical => physical;

        public int CompletedSectors
        {
            get { int n = 0; foreach (var d in SectorDone) if (d) n++; return n; }
        }

        static readonly Color Active = new Color(0.37f, 0.89f, 1f) * 1.3f;
        static readonly Color Scanning = new Color(1f, 0.71f, 0.33f) * 1.5f;
        static readonly Color Complete = new Color(0.45f, 1f, 0.6f) * 1.3f;
        static readonly Color LockedColor = new Color(0.37f, 0.55f, 0.65f) * 0.8f;

        bool wasDone;

        /// <summary>Hedefi (ve karşılığı olan gerçek nesneyi) yeni bir yere koyar.</summary>
        public void MoveTo(Vector3 position)
        {
            position.y = 0f;
            transform.position = position;
            if (physical != null) physical.position = position;
        }

        /// <summary>Görevde kullanılıp kullanılmayacağı: pasifse hologramı ve gerçek nesnesi de gizlenir.</summary>
        public void SetInMission(bool inMission)
        {
            gameObject.SetActive(inMission);
            if (physical != null) physical.gameObject.SetActive(inMission);
        }

        public void ConfigureSectors(int count)
        {
            SectorCount = Mathf.Max(1, count);
            SectorProgress = new float[SectorCount];
            SectorDone = new bool[SectorCount];
            if (station != null) station.Configure(this);
        }

        public void ResetState()
        {
            ConfigureSectors(SectorCount);
            Progress = 0f;
            Done = false;
            Locked = Kind == PointKind.Base;
            ActiveSector = -1;
            wasDone = false;
            RefreshVisual();
        }

        /// <summary>Robotun hedefe göre bulunduğu sektör (0..SectorCount-1). Kuzeyden (+Z) saat yönünde sayılır.</summary>
        public int SectorOf(Vector3 worldPosition)
        {
            if (SectorCount <= 1) return 0;
            var d = worldPosition - transform.position;
            float angle = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;
            return Mathf.Clamp(Mathf.FloorToInt(angle / (360f / SectorCount)), 0, SectorCount - 1);
        }

        public void RefreshVisual()
        {
            if (holo == null) return;
            holo.SetColor(Done ? Complete : Locked ? LockedColor : Progress > 0.01f ? Scanning : Active);
            RefreshIntensity();
            if (Done && !wasDone && burst != null && isActiveAndEnabled) burst.Play();
            wasDone = Done;
        }

        /// <summary>Tarama ilerledikçe hologram parlar.</summary>
        public void RefreshIntensity()
        {
            if (holo != null) holo.Intensity = Locked ? 0.7f : 1f + Progress * 0.9f;
        }
    }
}
