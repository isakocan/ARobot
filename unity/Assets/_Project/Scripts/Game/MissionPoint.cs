using ARaki.AR;
using UnityEngine;

namespace ARaki.Game
{
    public enum PointKind { Tower, Fuel, Base }

    /// <summary>
    /// Görevdeki bir hedef: yerleştirilen bir obje ya da ana üs. Gerçek oyunda kameranın tanıdığı objeler
    /// bu rolleri alacak; şimdilik görev üreticisinin yerleştirdiği yer tutucular.
    /// </summary>
    public class MissionPoint : MonoBehaviour
    {
        public string Label = "HEDEF";
        public PointKind Kind;
        public float Radius = 0.6f;       // taramanın başlaması için yaklaşılacak mesafe, m
        public float ScanSeconds = 2f;

        [SerializeField] HoloTint holo;
        [SerializeField] HoloBurst burst;
        [SerializeField] Transform physical;   // odadaki gerçek (3D baskı) nesne

        public float Progress { get; set; }
        public bool Done { get; set; }
        public bool Locked { get; set; }
        public Transform Physical => physical;

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

        public void ResetState()
        {
            Progress = 0f;
            Done = false;
            Locked = Kind == PointKind.Base;
            wasDone = false;
            RefreshVisual();
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
