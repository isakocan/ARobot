using System.Collections.Generic;
using ARaki.Sensing;
using UnityEngine;

namespace ARaki.AR
{
    /// <summary>
    /// Mesafe sensörünün AR karşılığı (zemine oturan, hologram değil): sensörün engele çarptığı noktalar zeminde
    /// parlayan nokta dizisi olarak görünür, robotun etrafından da periyodik bir tarama halkası yayılır.
    /// Gerçek robotta bu noktalar gerçek mesafe ölçümlerinden gelecek.
    /// </summary>
    public class LidarOverlay : MonoBehaviour
    {
        [SerializeField] ExplorationMap map;
        [SerializeField] ARRig rig;
        [SerializeField] Material dotMaterial;
        [SerializeField] LineRenderer pulseRing;     // yarıçapı 1 olan halka; ölçeklenerek yayılır
        [SerializeField] float dotSize = 0.05f;
        [SerializeField] float pulsePeriod = 2.6f;
        [SerializeField] int maxDots = 700;

        Transform dots;
        MeshFilter dotFilter;
        Mesh mesh;
        float timer;
        Material ringMaterial;
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> triangles = new List<int>();
        bool built;

        void Build()
        {
            if (built) return;
            built = true;
            var go = new GameObject("AR_LidarDots");
            go.transform.SetParent(transform, false);
            dotFilter = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = dotMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            dots = go.transform;
            mesh = new Mesh { name = "LidarDots" };
            mesh.MarkDynamic();
            dotFilter.sharedMesh = mesh;
            if (pulseRing != null) ringMaterial = pulseRing.material;
        }

        void LateUpdate() => Tick(Time.deltaTime);

        public void Tick(float dt)
        {
            Build();
            bool on = map != null && map.Running;
            if (dots.gameObject.activeSelf != on) dots.gameObject.SetActive(on);
            if (pulseRing != null && pulseRing.gameObject.activeSelf != on) pulseRing.gameObject.SetActive(on);
            if (!on) return;

            dots.position = rig.ToRig(Vector3.zero);
            dots.rotation = rig.ToRig(Quaternion.identity);

            timer -= dt;
            if (timer <= 0f)
            {
                timer = 0.2f;
                Rebuild();
            }
            UpdatePulse();
        }

        /// <summary>Robota yakın engel hücrelerini nokta olarak çizer; uzaktakiler sönükleşir.</summary>
        public void Rebuild()
        {
            Build();
            vertices.Clear();
            colors.Clear();
            triangles.Clear();

            Vector3 me = rig.Robot.position;
            float reach = map.Range + 0.3f;
            var c0 = map.ToCell(me);
            int r = Mathf.CeilToInt(reach / map.CellSize);
            int count = 0;
            float half = dotSize * 0.5f;
            for (int dz = -r; dz <= r && count < maxDots; dz++)
                for (int dx = -r; dx <= r && count < maxDots; dx++)
                {
                    int x = c0.x + dx, z = c0.y + dz;
                    if (map.At(x, z) != CellState.Wall) continue;
                    Vector3 p = map.CellCenter(x, z);
                    float d = Vector3.Distance(new Vector3(me.x, 0f, me.z), p);
                    if (d > reach) continue;
                    float a = Mathf.Clamp01(1f - d / reach) * 0.85f + 0.1f;
                    // taramanın o an geçtiği noktalar biraz daha parlak
                    var col = new Color(0.45f, 0.95f, 1f, a);
                    int k = vertices.Count;
                    p.y = 0.004f;
                    vertices.Add(p + new Vector3(-half, 0f, -half));
                    vertices.Add(p + new Vector3(half, 0f, -half));
                    vertices.Add(p + new Vector3(half, 0f, half));
                    vertices.Add(p + new Vector3(-half, 0f, half));
                    for (int i = 0; i < 4; i++) colors.Add(col);
                    triangles.Add(k); triangles.Add(k + 2); triangles.Add(k + 1);
                    triangles.Add(k); triangles.Add(k + 3); triangles.Add(k + 2);
                    count++;
                }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            var uv = new Vector2[vertices.Count];
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        float burstStart = -100f, burstRadius;

        /// <summary>Sonar: robotun etrafında hızlı, parlak tek bir halka yayar.</summary>
        public void TriggerPulse(float radius)
        {
            burstStart = Time.time;
            burstRadius = radius;
        }

        void UpdatePulse()
        {
            if (pulseRing == null) return;
            float t = (Time.time % pulsePeriod) / pulsePeriod;
            float eased = 1f - (1f - t) * (1f - t);
            float radius = Mathf.Lerp(0.2f, map.Range, eased);
            float boost = 1f;
            float burstT = (Time.time - burstStart) / 0.9f;
            if (burstT >= 0f && burstT < 1f)
            {
                t = burstT;
                eased = 1f - (1f - t) * (1f - t);
                radius = Mathf.Lerp(0.2f, burstRadius, eased);
                boost = 2.4f;
            }
            var tr = pulseRing.transform;
            tr.position = rig.ToRig(rig.Robot.position) + Vector3.up * 0.004f;
            tr.rotation = Quaternion.identity;
            tr.localScale = new Vector3(radius, 1f, radius);
            if (ringMaterial != null)
            {
                float fade = (1f - t) * (1f - t);
                var c = new Color(0.37f, 0.89f, 1f) * (0.9f * fade * boost);
                c.a = 1f;
                ringMaterial.SetColor("_BaseColor", c);
            }
        }
    }
}
