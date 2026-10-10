using ARaki.Game;
using UnityEngine;

namespace ARaki.AR
{
    /// <summary>Düşman sondanın rengini ve görüş sektörünü davranış durumuna göre değiştirir.</summary>
    public class ThreatVisual : MonoBehaviour
    {
        [SerializeField] Threat threat;
        [SerializeField] HoloTint holo;
        [SerializeField] GameObject sector;
        [SerializeField] Transform core;

        static readonly Color Patrol = new Color(1f, 0.28f, 0.28f) * 1.1f;
        static readonly Color Alert = new Color(1f, 0.72f, 0.2f) * 1.6f;
        static readonly Color Chase = new Color(1f, 0.15f, 0.15f) * 2.0f;
        static readonly Color Scared = new Color(0.4f, 0.85f, 1f) * 1.1f;

        ThreatState? shown;

        void Update() => Refresh();

        public void Refresh()
        {
            var s = threat.State;
            if (shown != s)
            {
                shown = s;
                holo.SetColor(s == ThreatState.Patrol ? Patrol : s == ThreatState.Alert ? Alert : s == ThreatState.Chase ? Chase : Scared);
                if (sector != null) sector.SetActive(s != ThreatState.Scared);
            }
            if (core != null)
            {
                float beat = s == ThreatState.Chase ? 14f : s == ThreatState.Alert ? 8f : 3f;
                core.localScale = Vector3.one * (0.075f * (1f + 0.12f * Mathf.Sin(Time.time * beat)));
            }
        }
    }
}
