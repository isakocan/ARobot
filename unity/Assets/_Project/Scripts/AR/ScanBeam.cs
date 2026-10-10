using ARaki.Game;
using UnityEngine;

namespace ARaki.AR
{
    /// <summary>Robot bir hedefi tararken robottan hedefe uzanan, dalgalanan enerji ışını.</summary>
    [RequireComponent(typeof(LineRenderer))]
    public class ScanBeam : MonoBehaviour
    {
        [SerializeField] ARRig rig;
        [SerializeField] MissionController mission;
        [SerializeField] Transform robot;
        [SerializeField] Vector3 sensorLocal = new Vector3(0f, 0.1f, 0.14f);
        [SerializeField] int segments = 28;
        [SerializeField] float amplitude = 0.012f;

        LineRenderer line;
        Vector3[] points;

        void Awake() => Ensure();

        void Ensure()
        {
            if (line != null) return;
            line = GetComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = segments;
            points = new Vector3[segments];
        }

        void LateUpdate() => Tick();

        public void Tick()
        {
            Ensure();
            MissionPoint best = null;
            foreach (var p in mission.Points)
                if (!p.Done && !p.Locked && p.Progress > 0.01f && (best == null || p.Progress > best.Progress)) best = p;

            line.enabled = best != null;
            if (best == null) return;

            Vector3 a = rig.ToRig(robot.TransformPoint(sensorLocal));
            Vector3 b = rig.ToRig(best.transform.position + Vector3.up * 0.34f);
            Vector3 dir = (b - a).normalized;
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 0.001f) side = Vector3.right;
            side.Normalize();
            Vector3 up = Vector3.Cross(side, dir);

            float time = Time.time;
            for (int i = 0; i < segments; i++)
            {
                float t = i / (float)(segments - 1);
                float envelope = Mathf.Sin(t * Mathf.PI);
                float wave = Mathf.Sin(t * 34f - time * 28f);
                float wave2 = Mathf.Cos(t * 21f - time * 19f);
                points[i] = Vector3.Lerp(a, b, t) + (side * wave + up * wave2) * (amplitude * envelope);
            }
            line.SetPositions(points);
            line.widthMultiplier = 0.008f * (0.8f + 0.3f * Mathf.Sin(time * 22f) + best.Progress * 0.6f);
        }
    }
}
