using System;
using System.Collections.Generic;
using ARaki.AR;
using ARaki.Robot;
using UnityEngine;

namespace ARaki.Game
{
    public enum RacePhase { Idle, Countdown, Running, Finished }
    public enum Medal { None, Bronze, Silver, Gold }

    /// <summary>Bir yarış pistinin yerleşimi: kapılar, pist eğrisi, tur sayısı ve süre hedefleri.</summary>
    public struct RaceSpec
    {
        public int Level;
        public string Title;
        public int Laps;
        public Vector3[] Gates;          // 0 = başlangıç/bitiş
        public Vector3[] Tangents;       // kapıdan geçiş yönü
        public Vector3[] Path;           // kapalı pist eğrisi
        public int[] GateIndex;          // kapıların Path içindeki sırası
        public float Length;             // bir tur, m
        public float GoldSeconds, SilverSeconds;   // tüm yarış için
        public float StartBattery;
        public Vector3 StartPosition;
        public float StartYaw;
    }

    public struct RaceResult
    {
        public float TotalSeconds, BestLap;
        public float[] LapTimes;
        public Medal Medal;
        public bool NewBestTotal, NewBestLap;
        public float BestTotalRecord, BestLapRecord;
    }

    /// <summary>
    /// Yarış modu: odadaki boş yerlere kapılar dizer, kapılardan geçen kapalı bir pist çizer ve zemine AR olarak serer.
    /// Oyuncu kapıları sırayla geçerek tur atar. Gerçek robotta kapılar kameranın tanıdığı işaretçilerle (ArUco) kurulur;
    /// pist onların arasına "oturtulur". Simülasyonda yerleşimi bu sınıf üretir ve doğrular.
    /// </summary>
    public class RaceController : MonoBehaviour
    {
        [SerializeField] Transform robot;
        [SerializeField] MockRobotLink link;
        [SerializeField] ARRig rig;
        [SerializeField] Transform arRoot;
        [SerializeField] GameObject gateTemplate;
        [SerializeField] Material trackMaterial;
        [SerializeField] Vector2 areaMin = new Vector2(-2.45f, -1.9f);
        [SerializeField] Vector2 areaMax = new Vector2(2.35f, 1.75f);
        [SerializeField] float pathClearance = 0.2f;     // pist boyunca robotun sığacağı boşluk
        [SerializeField] Transform[] ignoreRoots;

        public const float GateHalfWidth = 0.4f;
        public const int MaxGates = 8;
        public const float CountdownSeconds = 3f;

        public event Action<string> Notice;
        public event Action<int> CountdownTick;                 // 3, 2, 1, 0 (= başla)
        public event Action<int, bool> GateCrossed;             // kapı numarası, tur bitti mi
        public event Action<int, float, bool> LapCompleted;     // tur, süre, en iyi tur mu
        public event Action Finished;
        public event Action WrongWay;

        public RaceSpec Spec { get; private set; }
        public RacePhase Phase { get; private set; }
        public int Lap { get; private set; } = 1;               // 1 tabanlı
        public int NextGate { get; private set; } = 1;
        public float RaceTime { get; private set; }
        public float LapTime { get; private set; }
        public float BestLapThisRace { get; private set; }
        public float LastLapTime { get; private set; }
        public float CountdownLeft { get; private set; }
        public RaceResult Result { get; private set; }
        public IReadOnlyList<float> LapTimes => lapTimes;
        public int GateCount => Spec.Gates != null ? Spec.Gates.Length : 0;
        public bool Visible { get; private set; }

        public static float BestTotalRecord => PlayerPrefs.GetFloat("ARaki.RaceBestTotal", 0f);
        public static float BestLapRecord => PlayerPrefs.GetFloat("ARaki.RaceBestLap", 0f);

        readonly List<float> lapTimes = new List<float>();
        readonly List<GateView> gateViews = new List<GateView>();
        Transform trackVisual;
        MeshFilter trackFilter;
        Mesh trackMesh;
        readonly bool[] passed = new bool[MaxGates];
        float lastAlong;
        float wrongWayCooldown;
        int lastCountdown = -1;

        class GateView
        {
            public Transform Root;
            public HoloTint Tint;
            public HoloBurst Burst;
            public GameObject Banner;
        }

        static readonly Color PendingColor = new Color(0.78f, 0.9f, 1f) * 0.7f;
        static readonly Color NextColor = new Color(1f, 0.72f, 0.15f) * 1.7f;
        static readonly Color PassedColor = new Color(0.3f, 1f, 0.5f) * 0.9f;
        static readonly Color StartColor = new Color(1f, 0.3f, 0.22f) * 1.1f;

        // ------------------------------------------------------------------ üretim

        public static RaceSpec Plan(int level)
        {
            level = Mathf.Max(1, level);
            return new RaceSpec
            {
                Level = level,
                Title = "PİST " + level.ToString("00"),
                Laps = 3,
                StartBattery = 0.6f,
            };
        }

        /// <summary>Verilen tohumla odanın boş yerlerinden geçen bir pist üretir. Geçerli pist bulunamazsa en iyi adayı döndürür.</summary>
        public RaceSpec Generate(int level, int seed)
        {
            var plan = Plan(level);
            int gateCount = Mathf.Clamp(5 + (level - 1) / 2, 5, 7);
            var rng = new System.Random(seed);

            RaceSpec best = default;
            float bestScore = float.MinValue;
            float clearance = pathClearance;
            for (int round = 0; round < 5; round++)
            {
                for (int attempt = 0; attempt < 700; attempt++)
                {
                    var gates = Candidate(rng, gateCount);
                    var spec = Assemble(plan, gates);
                    float score = Evaluate(spec, clearance);
                    if (score > bestScore) { bestScore = score; best = spec; }
                    if (score >= 0f) return best;
                }
                clearance = Mathf.Max(0.17f, clearance * 0.95f);   // yer bulunamazsa boşluk koşulunu gevşet (robot yarıçapı 0.17)
                if (round >= 1) gateCount = Mathf.Max(4, gateCount - 1);
            }
            return best;
        }

        Vector3[] Candidate(System.Random rng, int count)
        {
            float cx = Mathf.Lerp(areaMin.x, areaMax.x, 0.5f) + ((float)rng.NextDouble() - 0.5f) * 0.8f;
            float cz = Mathf.Lerp(areaMin.y, areaMax.y, 0.5f) + ((float)rng.NextDouble() - 0.5f) * 0.6f;
            float rx = (areaMax.x - areaMin.x) * 0.5f, rz = (areaMax.y - areaMin.y) * 0.5f;
            float offset = (float)rng.NextDouble() * Mathf.PI * 2f;
            var gates = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float a = offset + (i + ((float)rng.NextDouble() - 0.5f) * 0.45f) * Mathf.PI * 2f / count;
                float k = Mathf.Lerp(0.45f, 1f, (float)rng.NextDouble());
                gates[i] = new Vector3(Mathf.Clamp(cx + Mathf.Cos(a) * rx * k, areaMin.x, areaMax.x), 0f, Mathf.Clamp(cz + Mathf.Sin(a) * rz * k, areaMin.y, areaMax.y));
            }
            return gates;
        }

        RaceSpec Assemble(RaceSpec plan, Vector3[] gates)
        {
            var spec = plan;
            spec.Gates = gates;
            spec.Path = TrackMesh.SamplePath(gates, out var gateIndex);
            spec.GateIndex = gateIndex;
            spec.Length = TrackMesh.Length(spec.Path);
            spec.Tangents = new Vector3[gates.Length];
            for (int i = 0; i < gates.Length; i++) spec.Tangents[i] = TrackMesh.Tangent(spec.Path, gateIndex[i]);
            spec.GoldSeconds = spec.Laps * spec.Length / 0.42f + 4f;
            spec.SilverSeconds = spec.Laps * spec.Length / 0.31f + 8f;
            spec.StartPosition = gates[0] - spec.Tangents[0] * 0.7f;
            spec.StartYaw = Mathf.Atan2(spec.Tangents[0].x, spec.Tangents[0].z) * Mathf.Rad2Deg;
            return spec;
        }

        /// <summary>≥ 0: geçerli. Negatif: ihlal sayısı (daha büyük = daha iyi).</summary>
        float Evaluate(RaceSpec spec, float clearance)
        {
            int bad = 0;
            // asgari uzunluk ve kapılar arası mesafe
            if (spec.Length < 6.5f) bad += 20;
            if (spec.Length > 16f) bad += 10;
            for (int i = 0; i < spec.Gates.Length; i++)
            {
                float d = Vector3.Distance(spec.Gates[i], spec.Gates[(i + 1) % spec.Gates.Length]);
                if (d < 1.0f) bad += 5;
            }
            // eğri boyunca her 0.12 m'de engel kontrolü
            for (int i = 0; i < spec.Path.Length; i += 1)
            {
                if (!IsFree(spec.Path[i], clearance)) bad++;
            }
            if (!IsFree(spec.StartPosition, 0.16f)) bad += 10;
            return bad == 0 ? 0f : -bad;
        }

        /// <summary>Testler ve ekran görüntüleri için: bu pist geçerli mi?</summary>
        public bool IsValid(RaceSpec spec, out string reason)
        {
            reason = null;
            if (spec.Path == null || spec.Path.Length < 8) { reason = "pist yok"; return false; }
            int blocked = 0;
            foreach (var p in spec.Path) if (!IsFree(p, pathClearance * 0.85f)) blocked++;
            if (blocked > 0) reason = blocked + " örnek noktası engele değiyor";
            else if (spec.Length < 6f) reason = "pist çok kısa: " + spec.Length.ToString("0.0");
            return reason == null;
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

        // ------------------------------------------------------------------ yarış akışı

        /// <summary>Pisti kurar, kapıları AR olarak çizer ve geri sayımı başlatır.</summary>
        public void Begin(RaceSpec spec)
        {
            Spec = spec;
            Lap = 1;
            NextGate = 1;
            RaceTime = LapTime = LastLapTime = BestLapThisRace = 0f;
            lapTimes.Clear();
            System.Array.Clear(passed, 0, passed.Length);
            Phase = RacePhase.Countdown;
            CountdownLeft = CountdownSeconds;
            lastCountdown = -1;
            wrongWayCooldown = 0f;
            BuildVisuals();
            SetVisible(true);
            RefreshGates();
            ResetCrossing();
        }

        /// <summary>Yarışı bitirir ve AR görsellerini gizler (menü, keşif moduna dönüş).</summary>
        public void Clear()
        {
            Phase = RacePhase.Idle;
            SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            Visible = visible;
            if (trackVisual != null) trackVisual.gameObject.SetActive(visible);
            foreach (var g in gateViews) if (g.Root != null) g.Root.gameObject.SetActive(false);
            if (visible)
                for (int i = 0; i < GateCount && i < gateViews.Count; i++) gateViews[i].Root.gameObject.SetActive(true);
        }

        void BuildVisuals()
        {
            if (trackVisual == null)
            {
                var go = new GameObject("AR_RaceTrack");
                go.transform.SetParent(arRoot, false);
                trackFilter = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = trackMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                trackVisual = go.transform;
            }
            if (trackMesh != null) { if (Application.isPlaying) Destroy(trackMesh); else DestroyImmediate(trackMesh); }
            trackMesh = TrackMesh.Build(Spec.Path, Spec.GateIndex[0]);
            trackFilter.sharedMesh = trackMesh;

            while (gateViews.Count < GateCount)
            {
                var inst = Instantiate(gateTemplate, arRoot);
                inst.name = "AR_Gate" + gateViews.Count;
                inst.SetActive(true);
                gateViews.Add(new GateView
                {
                    Root = inst.transform,
                    Tint = inst.GetComponent<HoloTint>(),
                    Burst = inst.GetComponentInChildren<HoloBurst>(true),
                    Banner = inst.transform.Find("StartBanner")?.gameObject,
                });
            }
            for (int i = 0; i < gateViews.Count; i++)
            {
                bool used = i < GateCount;
                gateViews[i].Root.gameObject.SetActive(used);
                if (gateViews[i].Banner != null) gateViews[i].Banner.SetActive(i == 0);
            }
            SyncVisuals();
        }

        void LateUpdate() => SyncVisuals();

        public void SyncVisuals()
        {
            if (!Visible || rig == null) return;
            if (trackVisual != null)
            {
                trackVisual.position = rig.ToRig(Vector3.zero);
                trackVisual.rotation = rig.ToRig(Quaternion.identity);
            }
            for (int i = 0; i < GateCount && i < gateViews.Count; i++)
            {
                var t = gateViews[i].Root;
                t.position = rig.ToRig(Spec.Gates[i]);
                t.rotation = rig.ToRig(Quaternion.LookRotation(Spec.Tangents[i], Vector3.up));
            }
        }

        void Update()
        {
            if (Phase == RacePhase.Idle || Phase == RacePhase.Finished) return;
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            if (Phase == RacePhase.Countdown)
            {
                CountdownLeft -= dt;
                int whole = Mathf.CeilToInt(Mathf.Max(0f, CountdownLeft));
                if (whole != lastCountdown)
                {
                    lastCountdown = whole;
                    CountdownTick?.Invoke(whole);
                }
                if (CountdownLeft <= 0f)
                {
                    Phase = RacePhase.Running;
                    CountdownTick?.Invoke(0);
                    ResetCrossing();
                }
                return;
            }
            if (Phase != RacePhase.Running) return;

            RaceTime += dt;
            LapTime += dt;
            wrongWayCooldown -= dt;

            Vector3 rel = Flat(robot.position) - Spec.Gates[NextGate];
            Vector3 tangent = Spec.Tangents[NextGate];
            float along = Vector3.Dot(rel, tangent);
            float lateral = Vector3.Dot(rel, TrackMesh.Right(tangent));
            if (Mathf.Abs(lateral) < GateHalfWidth + 0.05f)
            {
                if (lastAlong < 0f && along >= 0f) PassGate();
                else if (lastAlong > 0.05f && along <= 0.05f && wrongWayCooldown <= 0f)
                {
                    wrongWayCooldown = 3f;
                    WrongWay?.Invoke();
                    Notice?.Invoke("TERS YÖN");
                }
            }
            lastAlong = along;
        }

        void PassGate()
        {
            int gate = NextGate;
            bool lapEnd = gate == 0;
            GateCrossed?.Invoke(gate, lapEnd);
            var view = gateViews[gate];
            view.Burst?.Play();

            passed[gate] = true;
            if (!lapEnd)
            {
                NextGate = gate + 1 >= GateCount ? 0 : gate + 1;
                RefreshGates();
                ResetCrossing();
                return;
            }

            // tur tamam
            LastLapTime = LapTime;
            lapTimes.Add(LapTime);
            bool bestLap = BestLapThisRace <= 0f || LapTime < BestLapThisRace;
            if (bestLap) BestLapThisRace = LapTime;
            LapCompleted?.Invoke(Lap, LapTime, bestLap);
            Notice?.Invoke("TUR " + Lap + "  " + FormatLap(LapTime) + (bestLap && lapTimes.Count > 1 ? "  EN İYİ" : ""));
            LapTime = 0f;

            if (Lap >= Spec.Laps)
            {
                Finish();
                return;
            }
            Lap++;
            NextGate = 1;
            System.Array.Clear(passed, 0, passed.Length);
            RefreshGates();
            ResetCrossing();
        }

        void Finish()
        {
            Phase = RacePhase.Finished;
            float total = RaceTime;
            Medal medal = total <= Spec.GoldSeconds ? Medal.Gold : total <= Spec.SilverSeconds ? Medal.Silver : Medal.Bronze;
            float bestLap = BestLapThisRace;

            float recTotal = BestTotalRecord, recLap = BestLapRecord;
            bool newTotal = recTotal <= 0f || total < recTotal;
            bool newLap = recLap <= 0f || bestLap < recLap;
            if (newTotal) { PlayerPrefs.SetFloat("ARaki.RaceBestTotal", total); recTotal = total; }
            if (newLap) { PlayerPrefs.SetFloat("ARaki.RaceBestLap", bestLap); recLap = bestLap; }
            if (newTotal || newLap) PlayerPrefs.Save();

            Result = new RaceResult
            {
                TotalSeconds = total,
                BestLap = bestLap,
                LapTimes = lapTimes.ToArray(),
                Medal = medal,
                NewBestTotal = newTotal,
                NewBestLap = newLap,
                BestTotalRecord = recTotal,
                BestLapRecord = recLap,
            };
            RefreshGates(true);
            Finished?.Invoke();
        }

        void ResetCrossing()
        {
            if (Spec.Gates == null || NextGate >= GateCount) return;
            Vector3 rel = Flat(robot.position) - Spec.Gates[NextGate];
            lastAlong = Vector3.Dot(rel, Spec.Tangents[NextGate]);
        }

        /// <summary>Kapıların rengini yarış durumuna göre boyar: geçilen yeşil, sıradaki sarı, diğerleri soluk.</summary>
        void RefreshGates(bool allPassed = false)
        {
            for (int i = 0; i < GateCount && i < gateViews.Count; i++)
            {
                Color color;
                float intensity = 1f;
                if (allPassed || passed[i]) color = PassedColor;
                else if (i == NextGate) { color = NextColor; intensity = 1.15f; }
                else color = i == 0 ? StartColor : PendingColor;
                gateViews[i].Tint.SetColor(color);
                gateViews[i].Tint.Intensity = intensity;
            }
        }

        public bool IsGatePassed(int i) => i >= 0 && i < MaxGates && passed[i];

        static Vector3 Flat(Vector3 p) { p.y = 0f; return p; }

        public static string FormatLap(float seconds)
        {
            int m = Mathf.FloorToInt(seconds / 60f);
            float s = seconds - m * 60f;
            return m + ":" + s.ToString("00.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Sıradaki kapıya doğru yön (dünya düzleminde), HUD oku için.</summary>
        public Vector3 DirectionToNextGate()
        {
            if (Spec.Gates == null) return Vector3.forward;
            var d = Spec.Gates[NextGate] - Flat(robot.position);
            return d.sqrMagnitude < 1e-4f ? Vector3.forward : d.normalized;
        }

        public float DistanceToNextGate() => Spec.Gates == null ? 0f : Vector3.Distance(Spec.Gates[NextGate], Flat(robot.position));

        /// <summary>Testler ve ekran görüntüleri için: yarışı verilen toplam süreyle bitmiş say.</summary>
        public void ForceFinish(float totalSeconds = 0f)
        {
            if (Phase == RacePhase.Idle || Phase == RacePhase.Finished) return;
            if (totalSeconds <= 0f) totalSeconds = Spec.GoldSeconds * 1.05f;
            RaceTime = totalSeconds;
            lapTimes.Clear();
            float per = totalSeconds / Spec.Laps;
            for (int i = 0; i < Spec.Laps; i++) lapTimes.Add(per * (1f + 0.04f * (i % 2 == 0 ? -1f : 1f)));
            BestLapThisRace = Mathf.Min(lapTimes.ToArray());
            Finish();
        }

        /// <summary>Testler için: yarışı doğrudan başlat (geri sayımı atla).</summary>
        public void SkipCountdown()
        {
            if (Phase != RacePhase.Countdown) return;
            CountdownLeft = 0.0001f;
            Tick(0.01f);
        }
    }
}
