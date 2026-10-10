using UnityEngine;

namespace ARaki.Game
{
    /// <summary>Bir görevin yerleşimi ve zorluğu.</summary>
    public struct MissionSpec
    {
        public int Level;
        public string Title;
        public int SectorCount;
        public float SectorSeconds;
        public bool HasFuel;
        public int ThreatCount;
        public float ThreatSpeed;
        public float FlareInterval;      // iki güneş parlaması arası (sn)
        public float FlareDuration;      // parlamanın sürdüğü süre (sn)
        public Vector3 StationPosition;
        public Vector3 FuelPosition;
        public Vector3 BasePosition;
        public float BaseYaw;
        public Vector3[] ThreatPositions;
        public ThreatKind[] ThreatKinds;
    }

    /// <summary>
    /// Görevi her seferinde farklı kurar: istasyonu, enerji hücresini ve ana üssü odanın boş yerlerine dağıtır;
    /// zorluğu (sonda sayısı ve hızı, parlama sıklığı, istasyon sektör sayısı) seviyeye göre ayarlar.
    /// Gerçek oyunda yerleşimi oyuncu yapacak; üretici o zaman "bu yerleşim görev gereksinimine uyuyor mu"
    /// diye doğrulayıcı olarak çalışacak (boş yer, aralarında asgari mesafe).
    /// </summary>
    public class MissionGenerator : MonoBehaviour
    {
        [SerializeField] Vector2 areaMin = new Vector2(-2.4f, -1.9f);
        [SerializeField] Vector2 areaMax = new Vector2(2.4f, 1.9f);
        [SerializeField] float clearance = 0.36f;           // robotun sığacağı boşluk
        [SerializeField] float stationClearance = 0.85f;    // istasyonun etrafında dolaşılabilecek boşluk
        [SerializeField] float stationToBase = 2.0f;
        [SerializeField] float stationToFuel = 1.7f;
        [SerializeField] float fuelToBase = 1.5f;
        [SerializeField] Transform[] ignoreRoots;           // görev nesneleri: yerleştirilirken engel sayılmaz

        public float StationClearance => stationClearance;

        /// <summary>Seviyeye göre görev içeriği ve zorluk.</summary>
        public static MissionSpec Plan(int level)
        {
            level = Mathf.Max(1, level);
            return new MissionSpec
            {
                Level = level,
                Title = "SEKTÖR " + (level + 6).ToString("00"),
                SectorCount = level <= 2 ? 3 : level <= 4 ? 4 : 5,
                SectorSeconds = 2.4f,
                HasFuel = true,
                ThreatCount = Mathf.Clamp(1 + (level - 1) / 2 + (level >= 5 ? 1 : 0), 1, 4),
                ThreatSpeed = Mathf.Min(1.35f, 0.95f + 0.07f * (level - 1)),
                FlareInterval = Mathf.Max(18f, 42f - 4f * (level - 1)),
                FlareDuration = Mathf.Min(10f, 6f + 0.6f * level),
            };
        }

        public MissionSpec Generate(int level, int seed)
        {
            var spec = Plan(level);
            var rng = new System.Random(seed);

            // İstasyon odanın açık ortasında olsun; etrafında dolaşılabilsin.
            spec.StationPosition = Pick(rng, stationClearance, new Vector2(-1.4f, -1.0f), new Vector2(1.4f, 1.0f), null, 0f);
            spec.BasePosition = Pick(rng, clearance, areaMin, areaMax, spec.StationPosition, stationToBase);
            spec.FuelPosition = PickTwo(rng, spec.StationPosition, stationToFuel, spec.BasePosition, fuelToBase);

            spec.ThreatPositions = new Vector3[spec.ThreatCount];
            spec.ThreatKinds = new ThreatKind[spec.ThreatCount];
            for (int i = 0; i < spec.ThreatCount; i++)
            {
                var p = Pick(rng, 0.2f, areaMin, areaMax, spec.BasePosition, 2.2f);
                spec.ThreatPositions[i] = new Vector3(p.x, 0.28f, p.z);
                // 3. seviyeden itibaren son sonda "avcı" tipinde: daha hızlı, daha dar bakıyor
                spec.ThreatKinds[i] = level >= 3 && i == spec.ThreatCount - 1 ? ThreatKind.Hunter : ThreatKind.Sentinel;
            }

            Vector3 toCenter = -spec.BasePosition;
            spec.BaseYaw = Mathf.Atan2(toCenter.x, toCenter.z) * Mathf.Rad2Deg;
            return spec;
        }

        /// <summary>Yerleşim görev gereksinimine uyuyor mu? (boş yer ve asgari mesafe)</summary>
        public bool IsValid(MissionSpec spec) => IsValid(spec, out _);

        public bool IsValid(MissionSpec spec, out string reason)
        {
            // Asgari koşul: robot sığabilmeli ve istasyonun etrafında dolaşılabilmeli (ideal boşluk gevşetilmiş olabilir)
            reason = null;
            if (!IsFree(spec.StationPosition, stationClearance * 0.7f)) reason = "istasyon dolu yerde: " + spec.StationPosition;
            else if (!IsFree(spec.BasePosition, clearance * 0.6f)) reason = "üs dolu yerde: " + spec.BasePosition;
            else if (spec.HasFuel && !IsFree(spec.FuelPosition, clearance * 0.6f)) reason = "yakıt dolu yerde: " + spec.FuelPosition;
            else if (Vector3.Distance(spec.StationPosition, spec.BasePosition) < stationToBase * 0.7f) reason = "üs istasyona yakın";
            else if (spec.HasFuel && Vector3.Distance(spec.StationPosition, spec.FuelPosition) < stationToFuel * 0.7f) reason = "yakıt istasyona yakın";
            return reason == null;
        }

        Vector3 Pick(System.Random rng, float clear, Vector2 min, Vector2 max, Vector3? from, float minDistance)
        {
            float need = minDistance;
            float c = clear;
            for (int round = 0; round < 6; round++)
            {
                for (int attempt = 0; attempt < 250; attempt++)
                {
                    var p = new Vector3(Mathf.Lerp(min.x, max.x, (float)rng.NextDouble()), 0f, Mathf.Lerp(min.y, max.y, (float)rng.NextDouble()));
                    if (from.HasValue && Vector3.Distance(p, from.Value) < need) continue;
                    if (IsFree(p, c)) return p;
                }
                need *= 0.9f;      // yer bulunamazsa kuralı biraz gevşet
                c *= 0.92f;
            }
            return Fallback(min, max, from, minDistance, null, 0f);
        }

        Vector3 PickTwo(System.Random rng, Vector3 a, float da, Vector3 b, float db)
        {
            float na = da, nb = db;
            for (int round = 0; round < 6; round++)
            {
                for (int attempt = 0; attempt < 250; attempt++)
                {
                    var p = new Vector3(Mathf.Lerp(areaMin.x, areaMax.x, (float)rng.NextDouble()), 0f, Mathf.Lerp(areaMin.y, areaMax.y, (float)rng.NextDouble()));
                    if (Vector3.Distance(p, a) < na || Vector3.Distance(p, b) < nb) continue;
                    if (IsFree(p, clearance)) return p;
                }
                na *= 0.9f;
                nb *= 0.9f;
            }
            return Fallback(areaMin, areaMax, a, da, b, db);
        }

        /// <summary>Rastgele arama sonuç vermezse: alandaki en boş noktayı ızgara taramasıyla bulur.</summary>
        Vector3 Fallback(Vector2 min, Vector2 max, Vector3? a, float da, Vector3? b, float db)
        {
            Vector3 best = new Vector3((min.x + max.x) * 0.5f, 0f, (min.y + max.y) * 0.5f);
            float bestScore = -1f;
            float[] radii = { 1.0f, 0.85f, 0.7f, 0.55f, 0.4f, 0.3f, 0.2f };
            for (float x = min.x; x <= max.x; x += 0.12f)
                for (float z = min.y; z <= max.y; z += 0.12f)
                {
                    var p = new Vector3(x, 0f, z);
                    if (a.HasValue && Vector3.Distance(p, a.Value) < da * 0.6f) continue;
                    if (b.HasValue && Vector3.Distance(p, b.Value) < db * 0.6f) continue;
                    float score = 0f;
                    foreach (var r in radii)
                        if (IsFree(p, r)) { score = r; break; }
                    if (score > bestScore) { bestScore = score; best = p; }
                }
            return best;
        }

        bool IsFree(Vector3 p, float radius)
        {
            var hits = Physics.OverlapSphere(new Vector3(p.x, 0.15f, p.z), radius, ~0, QueryTriggerInteraction.Ignore);
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
