using UnityEngine;

namespace ARaki.Game
{
    /// <summary>Bir görevin yerleşimi ve zorluğu.</summary>
    public struct MissionSpec
    {
        public int Level;
        public string Title;
        public int TowerCount;
        public bool HasFuel;
        public int ThreatCount;
        public float ThreatSpeed;
        public Vector3 BasePosition;
        public float BaseYaw;
        public Vector3[] TowerPositions;
        public Vector3 FuelPosition;
        public Vector3[] ThreatPositions;
    }

    /// <summary>
    /// Görevi her seferinde farklı kurar: hedefleri odanın boş yerlerine, birbirinden uzak olacak şekilde dağıtır.
    /// Gerçek oyunda yerleşimi oyuncu yapacak; üretici o zaman "bu yerleşim görev gereksinimine uyuyor mu" diye
    /// doğrulayıcı olarak çalışacak. Aynı mantık: boş yer, aralarında asgari mesafe, üsse uzaklık.
    /// </summary>
    public class MissionGenerator : MonoBehaviour
    {
        [SerializeField] Vector2 areaMin = new Vector2(-2.45f, -1.95f);
        [SerializeField] Vector2 areaMax = new Vector2(2.45f, 1.95f);
        [SerializeField] float clearance = 0.36f;
        [SerializeField] float spacing = 1.3f;
        [SerializeField] float distanceFromBase = 1.6f;
        [SerializeField] Transform[] ignoreRoots;   // görev nesneleri: yerleştirilirken engel sayılmaz

        public float Spacing => spacing;
        public float DistanceFromBase => distanceFromBase;

        /// <summary>Seviyeye göre görev içeriği ve zorluk.</summary>
        public static MissionSpec Plan(int level)
        {
            level = Mathf.Max(1, level);
            var spec = new MissionSpec { Level = level, Title = "SEKTÖR " + (level + 6).ToString("00") };
            spec.TowerCount = level <= 2 ? 2 : level <= 4 ? 3 : 4;
            spec.HasFuel = true;
            spec.ThreatCount = Mathf.Clamp(level == 1 ? 1 : 1 + level / 2 + (level >= 3 ? 1 : 0), 1, 4);
            spec.ThreatSpeed = Mathf.Min(1.4f, 0.9f + 0.1f * (level - 1));
            return spec;
        }

        public MissionSpec Generate(int level, int seed)
        {
            var spec = Plan(level);
            var rng = new System.Random(seed);
            var placed = new System.Collections.Generic.List<Vector3>();

            spec.BasePosition = Pick(rng, placed, 0f, 0f);
            placed.Add(spec.BasePosition);

            spec.TowerPositions = new Vector3[spec.TowerCount];
            for (int i = 0; i < spec.TowerCount; i++)
            {
                spec.TowerPositions[i] = Pick(rng, placed, spacing, distanceFromBase, spec.BasePosition);
                placed.Add(spec.TowerPositions[i]);
            }
            spec.FuelPosition = Pick(rng, placed, spacing, distanceFromBase * 0.8f, spec.BasePosition);
            placed.Add(spec.FuelPosition);

            spec.ThreatPositions = new Vector3[spec.ThreatCount];
            for (int i = 0; i < spec.ThreatCount; i++)
            {
                var p = Pick(rng, null, 0f, 2.2f, spec.BasePosition);
                spec.ThreatPositions[i] = new Vector3(p.x, 0.28f, p.z);
            }

            // Robot oda merkezine bakarak başlar
            Vector3 toCenter = -spec.BasePosition;
            spec.BaseYaw = Mathf.Atan2(toCenter.x, toCenter.z) * Mathf.Rad2Deg;
            return spec;
        }

        /// <summary>Hedefin yerleşimi geçerli mi? (boş yer ve asgari mesafe)</summary>
        public bool IsValid(MissionSpec spec)
        {
            var all = new System.Collections.Generic.List<Vector3> { spec.BasePosition };
            all.AddRange(spec.TowerPositions);
            if (spec.HasFuel) all.Add(spec.FuelPosition);
            for (int i = 0; i < all.Count; i++)
            {
                if (!IsFree(all[i])) return false;
                for (int j = i + 1; j < all.Count; j++)
                    if (Vector3.Distance(all[i], all[j]) < spacing * 0.99f && i != 0 && j != 0) return false;
            }
            return true;
        }

        Vector3 Pick(System.Random rng, System.Collections.Generic.List<Vector3> placed, float minSpacing, float minFromBase, Vector3? basePos = null)
        {
            float spacingNow = minSpacing, baseNow = minFromBase;
            for (int round = 0; round < 6; round++)
            {
                for (int attempt = 0; attempt < 250; attempt++)
                {
                    var p = new Vector3(
                        Mathf.Lerp(areaMin.x, areaMax.x, (float)rng.NextDouble()), 0f,
                        Mathf.Lerp(areaMin.y, areaMax.y, (float)rng.NextDouble()));
                    if (!IsFree(p)) continue;
                    if (basePos.HasValue && Vector3.Distance(p, basePos.Value) < baseNow) continue;
                    bool ok = true;
                    if (placed != null)
                        foreach (var q in placed)
                            if (q != basePos && Vector3.Distance(p, q) < spacingNow) { ok = false; break; }
                    if (ok) return p;
                }
                spacingNow *= 0.9f;   // yer bulunamazsa kuralı biraz gevşet
                baseNow *= 0.9f;
            }
            return Vector3.zero;
        }

        bool IsFree(Vector3 p)
        {
            var hits = Physics.OverlapSphere(new Vector3(p.x, 0.15f, p.z), clearance, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
                if (!IsIgnored(h.transform)) return false;
            return true;
        }

        bool IsIgnored(Transform t)
        {
            if (ignoreRoots == null) return false;
            foreach (var r in ignoreRoots)
                if (r != null && (t == r || t.IsChildOf(r))) return true;
            return false;
        }
    }
}
