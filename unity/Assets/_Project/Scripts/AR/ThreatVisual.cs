using ARaki.Game;
using UnityEngine;

namespace ARaki.AR
{
    /// <summary>
    /// Düşman sondanın görünümü: davranış durumuna göre renk, hareket yönüne eğilen gövde, yerdeki görüş yelpazesi
    /// ve temas halinde robota uzanan elektrik arkı.
    /// </summary>
    public class ThreatVisual : MonoBehaviour
    {
        [SerializeField] Threat threat;
        [SerializeField] HoloTint holo;
        [SerializeField] Transform sectorRoot;      // görüş yelpazesi buraya kurulur
        [SerializeField] Material fanMaterial;
        [SerializeField] Transform model;           // eğilen gövde
        [SerializeField] Transform core;
        [SerializeField] LineRenderer arc;
        [SerializeField] ARRig rig;
        [SerializeField] Transform robot;

        static readonly Color SentinelPatrol = new Color(1f, 0.7f, 0.3f) * 1.1f;
        static readonly Color HunterPatrol = new Color(1f, 0.38f, 0.2f) * 1.25f;
        static readonly Color Alert = new Color(1f, 0.85f, 0.2f) * 1.8f;
        static readonly Color Chase = new Color(1f, 0.15f, 0.15f) * 2.1f;
        static readonly Color Search = new Color(1f, 0.55f, 0.15f) * 1.5f;
        static readonly Color Scared = new Color(0.4f, 0.85f, 1f) * 1.1f;

        ThreatState? shown;
        ThreatKind? shownKind;
        MeshFilter fanFilter;
        float fanDistance = -1f, fanAngle = -1f;
        float pitch, roll;
        Vector3[] arcPoints;

        void Update() => Refresh();

        public void Refresh()
        {
            EnsureFan();
            var s = threat.State;
            if (shown != s || shownKind != threat.Kind)
            {
                shown = s;
                shownKind = threat.Kind;
                holo.SetColor(ColorFor(s, threat.Kind));
                if (sectorRoot != null) sectorRoot.gameObject.SetActive(s != ThreatState.Scared);
            }

            if (core != null)
            {
                float beat = s == ThreatState.Chase ? 14f : s == ThreatState.Alert ? 8f : s == ThreatState.Search ? 5f : 3f;
                core.localScale = Vector3.one * (0.06f * (1f + 0.14f * Mathf.Sin(Time.time * beat)));
            }

            // Hareket yönüne doğru eğil; korkunca sendele
            if (model != null)
            {
                Vector3 vel = threat.Velocity;
                float forward = Vector3.Dot(vel, threat.Heading);
                float side = Vector3.Dot(vel, Vector3.Cross(Vector3.up, threat.Heading));
                float targetPitch = Mathf.Clamp(forward * 70f, -12f, 24f);
                float targetRoll = Mathf.Clamp(-side * 90f, -18f, 18f);
                if (s == ThreatState.Scared) { targetRoll += Mathf.Sin(Time.time * 17f) * 14f; targetPitch += Mathf.Cos(Time.time * 13f) * 10f; }
                pitch = Mathf.Lerp(pitch, targetPitch, Time.deltaTime * 6f);
                roll = Mathf.Lerp(roll, targetRoll, Time.deltaTime * 6f);
                model.localRotation = Quaternion.Euler(pitch, 0f, roll);
            }

            UpdateArc();
        }

        static Color ColorFor(ThreatState s, ThreatKind kind)
        {
            switch (s)
            {
                case ThreatState.Alert: return Alert;
                case ThreatState.Chase: return Chase;
                case ThreatState.Search: return Search;
                case ThreatState.Scared: return Scared;
                default: return kind == ThreatKind.Hunter ? HunterPatrol : SentinelPatrol;
            }
        }

        void EnsureFan()
        {
            if (sectorRoot == null || fanMaterial == null) return;
            if (Mathf.Approximately(fanDistance, threat.ViewDistance) && Mathf.Approximately(fanAngle, threat.ViewHalfAngle) && fanFilter != null) return;
            fanDistance = threat.ViewDistance;
            fanAngle = threat.ViewHalfAngle;
            if (fanFilter == null)
            {
                var go = new GameObject("Fan");
                go.transform.SetParent(sectorRoot, false);
                fanFilter = go.AddComponent<MeshFilter>();
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = fanMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            fanFilter.sharedMesh = ProceduralMeshes.Fan(fanDistance, fanAngle, 26);
        }

        void UpdateArc()
        {
            if (arc == null || rig == null || robot == null) return;
            bool on = threat.Engaged;
            arc.enabled = on;
            if (!on) return;

            const int n = 14;
            if (arcPoints == null || arcPoints.Length != n) { arcPoints = new Vector3[n]; arc.positionCount = n; arc.useWorldSpace = true; }
            Vector3 a = core != null ? core.position : transform.position;
            Vector3 b = rig.ToRig(robot.position + Vector3.up * 0.14f);
            Vector3 dir = (b - a);
            Vector3 side = Vector3.Cross(dir.normalized, Vector3.up);
            if (side.sqrMagnitude < 0.001f) side = Vector3.right;
            side.Normalize();
            Vector3 up = Vector3.Cross(side, dir.normalized);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float env = Mathf.Sin(t * Mathf.PI);
                Vector3 jitter = (side * (Random.value - 0.5f) + up * (Random.value - 0.5f)) * (0.06f * env);
                arcPoints[i] = Vector3.Lerp(a, b, t) + jitter;
            }
            arc.SetPositions(arcPoints);
            arc.widthMultiplier = 0.007f + Random.value * 0.005f;
        }
    }
}
