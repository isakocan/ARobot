using ARaki.AR;
using UnityEngine;

namespace ARaki.Game
{
    public enum PointKind { Tower, Fuel, Base }

    /// <summary>
    /// Görevdeki bir hedef: yerleştirilen bir obje ya da ana üs. Gerçek oyunda kameranın tanıdığı objeler
    /// bu rolleri alacak; şimdilik elle yerleştirilmiş yer tutucular.
    /// </summary>
    public class MissionPoint : MonoBehaviour
    {
        public string Label = "HEDEF";
        public PointKind Kind;
        public float Radius = 0.6f;       // taramanın başlaması için yaklaşılacak mesafe, m
        public float ScanSeconds = 2f;

        [SerializeField] HoloTint holo;

        public float Progress { get; set; }
        public bool Done { get; set; }
        public bool Locked { get; set; }

        static readonly Color Active = new Color(0.37f, 0.89f, 1f) * 1.3f;
        static readonly Color Scanning = new Color(1f, 0.71f, 0.33f) * 1.5f;
        static readonly Color Complete = new Color(0.45f, 1f, 0.6f) * 1.3f;
        static readonly Color LockedColor = new Color(0.37f, 0.55f, 0.65f) * 0.8f;

        public void RefreshVisual()
        {
            if (holo == null) return;
            holo.SetColor(Done ? Complete : Locked ? LockedColor : Progress > 0.01f ? Scanning : Active);
        }
    }
}
