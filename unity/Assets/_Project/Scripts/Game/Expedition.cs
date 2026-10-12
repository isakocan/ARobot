using System;
using System.Collections;
using System.Collections.Generic;
using ARaki.AR;
using ARaki.Robot;
using ARaki.Sensing;
using ARaki.Theming;
using ARaki.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ARaki.Game
{
    /// <summary>
    /// Serbest gezi oyununun beyni. Robot evde dolaşırken sensör haritası dolar; harita dolduğu yerlerde odaya göre
    /// bulgular çıkar (Pokémon GO gibi, ama evin içinde ve robotun bedeniyle). Her bulgu farklı bir fiil ister:
    /// sessiz yaklaş, ışıkla çağır, sonarla ortaya çıkar, yanında bekle. Yakalananlar robotun hafızasında taşınır;
    /// üsse getirilince teslim edilir (öz, günlük, hikaye ilerlemesi), pil şarj olur, konum kalibre edilir.
    /// Pil biterse taşınanlar kaybolur.
    /// </summary>
    public class Expedition : MonoBehaviour
    {
        [SerializeField] Transform robot;
        [SerializeField] MockRobotLink link;
        [SerializeField] ExplorationMap map;
        [SerializeField] ARRig rig;
        [SerializeField] Transform arRoot;
        [SerializeField] LidarOverlay lidar;
        [SerializeField] GameObject[] templates;      // [tema * 5 + bulgu türü]
        [SerializeField] GameObject ghostTemplate;
        [SerializeField] Transform baseMarker;

        [Header("Ayarlar")]
        [SerializeField] float detectRange = 2.4f;     // bulguyu görme mesafesi (görüş hattıyla)
        [SerializeField] float captureRadius = 0.55f;
        [SerializeField] float depotRadius = 0.7f;
        [SerializeField] float depotChargePerSecond = 0.06f;
        [SerializeField] float sonarBatteryCost = 0.012f;

        public event Action<string> Notice;
        public event Action<string> Comms;
        public event Action<RoomInfo> RoomEntered;
        public event Action<Find> FindRevealed;
        public event Action<Find> FindCaptured;
        public event Action<Find> FindFled;
        public event Action<int, int> Delivered;       // adet, öz
        public event Action Calibrated;
        public event Action<int> ChapterCompleted;
        public event Action Finished;
        public event Action SonarFired;
        public event Action Rescued;
        public event Action EnergyChanged;
        public event Action<Ghost> GhostSpotted;        // gölge seni fark etti
        public event Action<bool> GhostHit;              // gölge dokundu (true = bir kayıt kaybedildi)
        public event Action GhostLost;                   // gölgeden kurtuldun

        public bool Running { get; set; }

        /// <summary>Kapalıysa kendiliğinden bulgu çıkmaz (testler bulguları kendileri koyar).</summary>
        public bool AutoSpawn { get; set; } = true;

        public void DebugSetEssence(int value) => Essence = value;

        /// <summary>Testler için: sıfırdan büyükse motor gürültüsü bu değer sayılır.</summary>
        public float NoiseOverride { get; set; } = -1f;

        /// <summary>Testler için: bölümü doğrudan ayarlar.</summary>
        public void DebugSetChapter(int index, int progress = 0)
        {
            ChapterIndex = Mathf.Clamp(index, 0, Theme.Chapters.Length - 1);
            ChapterProgress = progress;
            IsFinished = false;
            LegendRoom = null;
        }
        public PlayerProfile Profile { get; } = new PlayerProfile();
        public int Essence { get; private set; }
        public int ChapterIndex { get; private set; }
        public int ChapterProgress { get; private set; }
        public bool IsFinished { get; private set; }
        public int TotalDelivered { get; private set; }
        public int[] DeliveredByKind { get; private set; } = new int[5];
        public RoomInfo CurrentRoom { get; private set; }
        public RoomInfo LegendRoom { get; private set; }
        public bool InBase { get; private set; }
        public float SonarCooldownLeft { get; private set; }
        public float SonarRadius => Profile.SonarRadius;
        public IReadOnlyList<Find> Finds => finds;
        public IReadOnlyList<Find> Carried => carried;
        public ExplorationMap Map => map;
        public Vector3 BasePosition => HouseLayout.BasePosition;
        public ThemeDef Theme => ThemeCatalog.Def;
        public ChapterDef Chapter => Theme.Chapters[Mathf.Min(ChapterIndex, Theme.Chapters.Length - 1)];
        public IReadOnlyList<Ghost> Ghosts => ghosts;

        /// <summary>0..1: en yakın gölgenin yakınlığı (arayüzdeki kırmızı uyarı için).</summary>
        public float GhostThreat { get; private set; }

        public bool IsLegendPending => !IsFinished && Chapter.Goal == GoalKind.Legend;

        /// <summary>Motorların gürültüsü 0..1 (tekerlek hızı). Ürkek bulgular bundan kaçar.</summary>
        public float Noise
        {
            get
            {
                if (NoiseOverride >= 0f) return NoiseOverride;
                var t = link.Telemetry;
                return Mathf.Clamp01((Mathf.Abs(t.LeftSpeed) + Mathf.Abs(t.RightSpeed)) * 0.5f / link.MaxWheelSpeed);
            }
        }

        readonly List<Find> finds = new List<Find>();
        readonly List<Find> carried = new List<Find>();
        readonly List<Ghost> ghosts = new List<Ghost>();
        float nextFindTime, nextGhostTime, clock;      // clock: oyun saati (Tick ile ilerler)
        readonly HashSet<string> visited = new HashSet<string>();
        readonly bool[] tutorialShown = new bool[5];
        System.Random rng = new System.Random();
        float spawnTimer, coverageTimer, coverageCache;
        bool wasInBase;
        Coroutine introRoutine;
        int codexMask;

        static readonly float[][] KindWeights =
        {
            /* Hall    */ new[] { 2f, 2f, 1f, 1f },
            /* Living  */ new[] { 2f, 2f, 1f, 3f },
            /* Kitchen */ new[] { 3f, 3f, 1f, 2f },
            /* Bedroom */ new[] { 2f, 1f, 3f, 2f },
            /* Bath    */ new[] { 2f, 3f, 1f, 1f },
            /* Study   */ new[] { 1f, 1f, 2f, 3f },
        };

        void Awake()
        {
            if (map != null) map.Init();
        }

        public void Seed(int seed) => rng = new System.Random(seed);

        // ------------------------------------------------------------------ başlangıç / kayıt

        /// <summary>Oyunu başlatır. fresh = kayıtlı ilerlemeyi silip baştan başla.</summary>
        public void Begin(bool fresh)
        {
            ClearWorld();
            Essence = 0; ChapterIndex = 0; ChapterProgress = 0; IsFinished = false; TotalDelivered = 0; codexMask = 0;
            Array.Clear(DeliveredByKind, 0, DeliveredByKind.Length);
            Profile.Reset();
            if (fresh) SaveData.Delete(ThemeCatalog.Current); else LoadSave();

            ApplyProfile();
            map.Clear();
            link.Teleport(HouseLayout.BasePosition, HouseLayout.BaseYaw);
            link.SetBattery(1f);
            link.SetLed(false);
            Physics.SyncTransforms();

            foreach (var r in HouseLayout.Rooms) r.NextRefillTime = 0f;
            clock = 0f;
            visited.Clear();
            Array.Clear(tutorialShown, 0, tutorialShown.Length);
            CurrentRoom = null;
            wasInBase = true;
            InBase = true;
            spawnTimer = 0.5f;
            nextFindTime = clock + 3f;
            nextGhostTime = clock + 35f;
            SonarCooldownLeft = 0f;
            Running = true;
            StartChapterIntro();
        }

        void ClearWorld()
        {
            foreach (var f in finds) if (f != null) DestroyFind(f);
            finds.Clear();
            carried.Clear();
            ClearGhosts();
            LegendRoom = null;
        }

        void ClearGhosts()
        {
            foreach (var g in ghosts) if (g != null) DestroyGhost(g);
            ghosts.Clear();
            GhostThreat = 0f;
        }

        /// <summary>Yükseltme düzeylerini robota ve sensöre uygular.</summary>
        public void ApplyProfile()
        {
            link.MaxPwmLimit = Profile.MaxPwm;
            link.PwmLimit = Mathf.Min(0.6f, Profile.MaxPwm);
            link.LedDrainMultiplier = Profile.LedDrainMultiplier;
            link.DrainMultiplier = Profile.DrainMultiplier;
            map.Range = Profile.SensorRange;
        }

        void LoadSave()
        {
            var s = SaveData.Load(ThemeCatalog.Current);
            if (s == null) return;
            ChapterIndex = Mathf.Clamp(s.chapter, 0, Theme.Chapters.Length);
            IsFinished = s.finished;
            ChapterProgress = s.progress;
            Essence = s.essence;
            TotalDelivered = s.total;
            codexMask = s.codex;
            if (s.byKind != null) Array.Copy(s.byKind, DeliveredByKind, Mathf.Min(s.byKind.Length, DeliveredByKind.Length));
            if (s.upgrades != null) Array.Copy(s.upgrades, Profile.Levels, Mathf.Min(s.upgrades.Length, Profile.Levels.Length));
        }

        public void Save()
        {
            SaveData.Store(ThemeCatalog.Current, new SaveData
            {
                chapter = ChapterIndex, finished = IsFinished, progress = ChapterProgress, essence = Essence, total = TotalDelivered,
                codex = codexMask, byKind = (int[])DeliveredByKind.Clone(), upgrades = (int[])Profile.Levels.Clone(),
            });
        }

        public bool HasSave => SaveData.Load(ThemeCatalog.Current) != null;

        public bool IsVisited(RoomInfo room) => visited.Contains(room.Id);

        public bool Discovered(FindKind kind) => (codexMask & (1 << (int)kind)) != 0;

        // ------------------------------------------------------------------ her kare

        void Update()
        {
            if (!Running) return;
            var kb = Keyboard.current;
            if (kb != null && kb.xKey.wasPressedThisFrame) TrySonar();
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            clock += dt;
            Vector3 pos = Flat(robot.position);
            UpdateRoom(pos);
            SonarCooldownLeft = Mathf.Max(0f, SonarCooldownLeft - dt);

            spawnTimer -= dt;
            if (spawnTimer <= 0f && AutoSpawn)
            {
                spawnTimer = 1.4f;
                TrySpawnFor(CurrentRoom);
                if (IsLegendPending) TrySpawnLegend();
                TrySpawnGhost(pos);
            }

            for (int i = finds.Count - 1; i >= 0; i--)
            {
                var f = finds[i];
                if (f == null || f.State == FindState.Gone || f.State == FindState.Captured) { finds.RemoveAt(i); continue; }
                UpdateFind(f, pos, dt);
            }

            UpdateGhosts(pos, dt);
            UpdateDepot(pos, dt);

            if (link.Telemetry.Battery01 <= 0.0005f) Rescue();
        }

        void UpdateRoom(Vector3 pos)
        {
            var room = HouseLayout.RoomAt(pos);
            if (room == CurrentRoom) return;
            CurrentRoom = room;
            coverageTimer = 0f;
            bool first = visited.Add(room.Id);
            RoomEntered?.Invoke(room);
            Notice?.Invoke(room.Name);
            if (first && visited.Count == 2) Comms?.Invoke("Yeni bir oda. Sensörün haritayı doldurdukça burada da şeyler çıkacak.");
        }

        // ------------------------------------------------------------------ çıkış (spawn)

        float RoomCoverage(RoomInfo room)
        {
            if (map == null) return 1f;
            int total = 0, free = 0;
            float c = map.CellSize;
            for (float z = room.Rect.yMin + c * 0.5f; z < room.Rect.yMax; z += c)
                for (float x = room.Rect.xMin + c * 0.5f; x < room.Rect.xMax; x += c)
                {
                    total++;
                    if (map.At(new Vector3(x, 0f, z)) == CellState.Free) free++;
                }
            return total == 0 ? 0f : free / (float)total;
        }

        int AliveIn(RoomInfo room)
        {
            int n = 0;
            foreach (var f in finds) if (f != null && f.Alive && f.Room == room && f.Kind != FindKind.Legend) n++;
            return n;
        }

        void TrySpawnFor(RoomInfo room)
        {
            if (room == null || clock < room.NextRefillTime || clock < nextFindTime) return;
            if (AliveIn(room) >= room.TargetFinds || AliveCount() >= MaxAliveFinds) return;
            coverageCache = RoomCoverage(room);
            if (coverageCache < 0.10f) return;

            var kind = PickKind(room);
            if (!TryPickSpot(room, kind, out var spot)) return;
            Spawn(kind, room, spot, RollTier());
            nextFindTime = clock + Mathf.Lerp(9f, 20f, (float)rng.NextDouble());
        }

        /// <summary>Evde aynı anda en çok bu kadar bulgu bulunur: çıkışlar seyrek ve değerli.</summary>
        public const int MaxAliveFinds = 4;

        int AliveCount()
        {
            int n = 0;
            foreach (var f in finds) if (f != null && f.Alive && f.Kind != FindKind.Legend) n++;
            return n;
        }

        void TrySpawnLegend()
        {
            foreach (var f in finds) if (f != null && f.Alive && f.Kind == FindKind.Legend) return;
            foreach (var f in carried) if (f.Kind == FindKind.Legend) return;
            LegendRoom ??= FarthestRoom();
            if (RoomCoverage(LegendRoom) < 0.10f) return;
            if (!TryPickSpot(LegendRoom, FindKind.Legend, out var spot)) return;
            Spawn(FindKind.Legend, LegendRoom, spot, 3);
        }

        RoomInfo FarthestRoom()
        {
            RoomInfo best = null;
            float bestDistance = -1f;
            foreach (var r in HouseLayout.Rooms)
            {
                if (r.Kind == RoomKind.Hall) continue;
                float d = Vector3.Distance(r.Center, HouseLayout.BasePosition);
                if (d > bestDistance) { bestDistance = d; best = r; }
            }
            return best;
        }

        FindKind PickKind(RoomInfo room)
        {
            var w = (float[])KindWeights[(int)room.Kind].Clone();      // Shy, Lure, Hidden, Relic
            if (ChapterIndex < 3 && TotalDelivered < 6) w[2] = 0f;       // sonar öğretilmeden gizli bulgu çıkmaz
            if (!IsFinished)
            {
                var c = Chapter;
                if (c.Goal == GoalKind.DeliverKind && (int)c.Kind < 4) w[(int)c.Kind] *= 3f;
            }
            float sum = 0f;
            foreach (var x in w) sum += x;
            float pick = (float)rng.NextDouble() * sum;
            for (int i = 0; i < w.Length; i++)
            {
                pick -= w[i];
                if (pick <= 0f) return (FindKind)i;
            }
            return FindKind.Relic;
        }

        int RollTier()
        {
            double r = rng.NextDouble();
            return r < 0.03 ? 3 : r < 0.15 ? 2 : 1;
        }

        bool TryPickSpot(RoomInfo room, FindKind kind, out Vector3 spot)
        {
            spot = default;
            Vector3 me = Flat(robot.position);
            for (int attempt = 0; attempt < 70; attempt++)
            {
                float x = Mathf.Lerp(room.Rect.xMin + 0.4f, room.Rect.xMax - 0.4f, (float)rng.NextDouble());
                float z = Mathf.Lerp(room.Rect.yMin + 0.4f, room.Rect.yMax - 0.4f, (float)rng.NextDouble());
                var p = new Vector3(x, 0f, z);
                if (map != null && !map.IsKnownFree(p, 1)) continue;
                float distance = Vector3.Distance(p, me);
                if (distance < 1.1f) continue;
                if (attempt < 45 && distance > 5.0f) continue;     // önce yakında dene: çok gezdirmesin
                if (HouseLayout.NearDoor(p, 0.9f)) continue;
                if (Vector3.Distance(p, HouseLayout.BasePosition) < 1.4f) continue;
                if (NearOtherFind(p, 0.9f)) continue;
                if (kind == FindKind.Hidden && (map == null || !map.HasWallNear(p, 4))) continue;
                // robotun gözü önünde belirmesin
                if (map != null && Vector3.Distance(p, me) < detectRange + 0.1f && map.LineOfSight(me, p)) continue;
                spot = p;
                return true;
            }
            return false;
        }

        bool NearOtherFind(Vector3 p, float radius)
        {
            foreach (var f in finds) if (f != null && f.Alive && Vector3.Distance(f.Position, p) < radius) return true;
            return false;
        }

        /// <summary>Bir bulguyu verilen yerde oluşturur (testler de kullanır).</summary>
        public Find Spawn(FindKind kind, RoomInfo room, Vector3 position, int tier = 1)
        {
            var go = new GameObject("Find_" + kind);
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(position.x, 0f, position.z);
            var f = go.AddComponent<Find>();
            f.Kind = kind;
            f.Tier = tier;
            f.Room = room ?? HouseLayout.RoomAt(position);
            f.Home = go.transform.position;
            f.Essence = Find.EssenceFor(kind, tier);
            f.Revealed = false;

            int index = (int)ThemeCatalog.Current * 5 + (int)kind;
            if (templates != null && index < templates.Length && templates[index] != null)
            {
                var inst = Instantiate(templates[index], arRoot);
                inst.name = "AR_" + kind;
                inst.SetActive(false);
                f.Visual = inst.GetComponent<FindVisual>();
                inst.transform.localScale = Vector3.one * (tier == 3 ? 1.35f : tier == 2 ? 1.15f : 1f);
                if (f.Visual != null) f.Visual.SetTier(tier);
            }
            finds.Add(f);
            return f;
        }

        void DestroyFind(Find f)
        {
            if (f.Visual != null) { if (Application.isPlaying) Destroy(f.Visual.gameObject); else DestroyImmediate(f.Visual.gameObject); }
            if (Application.isPlaying) Destroy(f.gameObject); else DestroyImmediate(f.gameObject);
        }

        // ------------------------------------------------------------------ bulgu davranışları

        void UpdateFind(Find f, Vector3 me, float dt)
        {
            float d = Vector3.Distance(me, f.Position);
            bool led = link.LedOn;
            float noise = Noise;
            float captureSeconds = Find.CaptureSeconds(f.Kind) * Profile.CaptureTimeMultiplier;

            // görünürlük
            if (f.Kind == FindKind.Hidden)
            {
                bool wasRevealed = f.Revealed;
                f.Revealed = Time.time < f.PingUntil;
                if (wasRevealed && !f.Revealed && f.Progress < 0.01f) { /* sonar etkisi bitti, yine saklandı */ }
            }
            else if (!f.Revealed && d < detectRange && (map == null || map.LineOfSight(me, f.Position, 0.2f)))
            {
                f.Revealed = true;
                OnRevealed(f);
            }

            if (f.ExpireAt > 0f && clock > f.ExpireAt && f.Progress < 0.05f && f.Revealed)
            {
                f.State = FindState.Gone;
                Notice?.Invoke(Theme.NamesOf(f.Kind).Name + " KAYBOLDU");
                FindFled?.Invoke(f);
                if (f.Visual != null) f.Visual.gameObject.SetActive(false);
                return;
            }

            bool satisfied = false;

            switch (f.Kind)
            {
                case FindKind.Shy:
                    if (f.State == FindState.Fleeing) { StepFlee(f, dt); break; }
                    // önce tedirgin olur; gürültü sürerse kaçar
                    bool spooked = f.Revealed && d < 1.7f && (noise > 0.5f || (led && d < 2.0f));
                    f.Alarm = spooked ? f.Alarm + dt : Mathf.Max(0f, f.Alarm - dt * 1.5f);
                    if (f.Alarm > 0.9f) { f.Alarm = 0f; StartFlee(f, me); break; }
                    satisfied = f.Revealed && d < captureRadius && noise < 0.2f && !led;
                    break;

                case FindKind.Lure:
                    if (led && d < 3.2f && f.Revealed)
                    {
                        if (d > 0.4f) MoveFind(f, f.Position + (me - f.Position).normalized * 0.4f, 0.5f * dt);
                    }
                    else if (Vector3.Distance(f.Position, f.Home) > 0.05f) MoveFind(f, f.Home, 0.25f * dt);
                    satisfied = f.Revealed && led && d < captureRadius;
                    break;

                case FindKind.Hidden:
                case FindKind.Relic:
                    satisfied = f.Revealed && d < 0.5f && noise < 0.35f;
                    break;

                case FindKind.Legend:
                    if (f.State == FindState.Fleeing) { StepFlee(f, dt); break; }
                    if (f.Revealed && d < 2.0f && noise > 0.4f) { StartFlee(f, me); break; }
                    if (led && f.Revealed && d < 3.4f && d > 0.7f) MoveFind(f, f.Position + (me - f.Position).normalized * 0.7f, 0.45f * dt);
                    satisfied = f.Revealed && led && d < 0.8f && noise < 0.15f;
                    break;
            }

            f.Progress = Mathf.Clamp01(f.Progress + (satisfied ? dt / captureSeconds : -dt * 0.6f));
            if (f.Progress >= 1f) Capture(f);
        }

        void OnRevealed(Find f)
        {
            FindRevealed?.Invoke(f);
            var names = Theme.NamesOf(f.Kind);
            if (!tutorialShown[(int)f.Kind])
            {
                tutorialShown[(int)f.Kind] = true;
                Comms?.Invoke(names.Name + " görüldü. " + Tutorial(f.Kind));
            }
            if (f.Tier > 1 && f.ExpireAt <= 0f) f.ExpireAt = clock + (f.Tier == 3 ? 40f : 75f);
            Notice?.Invoke(names.Name + (f.Tier == 3 ? "  ·  ÇOK NADİR" : f.Tier == 2 ? "  ·  NADİR" : "") + "  GÖRÜLDÜ");
        }

        static string Tutorial(FindKind kind)
        {
            switch (kind)
            {
                case FindKind.Shy: return "Ürkek: hızlı gidersen kaçar. SHIFT'e basılı tutarak sessizce yaklaş, yanında bekle.";
                case FindKind.Lure: return "Işığa gelir: L ile LED'i yak, yanına çağır ve bekle.";
                case FindKind.Hidden: return "Saklanıyor: sonar (X) ile ortaya çıkarıp yanına git.";
                case FindKind.Legend: return "Efsane! Işıkla çağır, ama gürültü yaparsan kaçar. Çok yavaş yaklaş.";
                default: return "Hareketsiz: yanına git, kısa süre bekle.";
            }
        }

        void StartFlee(Find f, Vector3 me)
        {
            Vector3 away = (f.Position - me);
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
            away.Normalize();

            float bestDistance = 0f;
            Vector3 bestTarget = f.Position;
            foreach (float angle in new[] { 0f, 40f, -40f, 80f, -80f, 120f, -120f })
            {
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * away;
                float reach = FreeDistance(f, dir, 1.3f);
                if (reach > bestDistance) { bestDistance = reach; bestTarget = f.Position + dir * reach; }
            }

            f.Flees++;
            f.Progress = 0f;
            if (bestDistance < 0.35f) return;           // köşeye sıkıştı: kaçamaz (yakalanabilir)
            f.State = FindState.Fleeing;
            f.FleeTarget = ClampToRoom(f, bestTarget);
            f.FleeTimer = 1.4f;
        }

        void StepFlee(Find f, float dt)
        {
            MoveFind(f, f.FleeTarget, 1.0f * dt);
            f.FleeTimer -= dt;
            if (f.FleeTimer <= 0f || Vector3.Distance(f.Position, f.FleeTarget) < 0.05f)
            {
                f.State = FindState.Idle;
                f.Home = f.transform.position;
                if (f.Kind == FindKind.Shy && f.Flees >= 5)
                {
                    // beş kez ürküttün: tamamen uzaklaşır
                    f.State = FindState.Gone;
                    Notice?.Invoke(Theme.NamesOf(f.Kind).Name + " KAÇTI");
                    FindFled?.Invoke(f);
                    f.Room.NextRefillTime = Mathf.Max(f.Room.NextRefillTime, clock + 15f);
                    if (f.Visual != null) f.Visual.gameObject.SetActive(false);
                }
            }
        }

        float FreeDistance(Find f, Vector3 dir, float max)
        {
            Vector3 origin = f.transform.position + Vector3.up * 0.15f;
            if (Physics.SphereCast(origin, 0.14f, dir, out var hit, max, ~0, QueryTriggerInteraction.Ignore))
                return Mathf.Max(0f, hit.distance - 0.05f);
            return max;
        }

        Vector3 ClampToRoom(Find f, Vector3 p)
        {
            var r = f.Room.Rect;
            p.x = Mathf.Clamp(p.x, r.xMin + 0.3f, r.xMax - 0.3f);
            p.z = Mathf.Clamp(p.z, r.yMin + 0.3f, r.yMax - 0.3f);
            p.y = 0f;
            return p;
        }

        void MoveFind(Find f, Vector3 target, float maxStep)
        {
            Vector3 delta = target - f.Position;
            float dist = delta.magnitude;
            if (dist < 0.001f) return;
            Vector3 dir = delta / dist;
            float step = Mathf.Min(maxStep, dist);
            if (FreeDistance(f, dir, step + 0.1f) < step) return;     // duvara/eşyaya girmesin
            Vector3 next = ClampToRoom(f, f.transform.position + dir * step);
            f.transform.position = next;
        }

        void Capture(Find f)
        {
            f.State = FindState.Captured;
            f.Progress = 1f;
            carried.Add(f);
            FindCaptured?.Invoke(f);
            var names = Theme.NamesOf(f.Kind);
            Notice?.Invoke(names.Name + "  KAYDEDİLDİ");
            if (carried.Count == 1 && TotalDelivered == 0) Comms?.Invoke("Kayıt robotun hafızasında. Üsse götürünce teslim edilir; pil biterse kaybolur.");
            f.Room.NextRefillTime = Mathf.Max(f.Room.NextRefillTime, clock + 20f);
            if (f.Visual != null) f.Visual.PlayCapture();
        }

        // ------------------------------------------------------------------ gölgeler (kaçılacak tehdit)

        public bool GhostsActive => true;
        int MaxGhosts => ChapterIndex >= 4 ? 2 : 1;
        const float GhostBaseSpeed = 0.24f;       // robotun varsayılan hızından (0,3 m/s) biraz yavaş
        const float SafeRadius = 1.25f;           // üssün çevresinde gölge giremez
        bool tutorialGhost, tutorialLight;

        void TrySpawnGhost(Vector3 me)
        {
            if (!GhostsActive || clock < nextGhostTime || ghosts.Count >= MaxGhosts) return;
            if (InBase || Vector3.Distance(me, HouseLayout.BasePosition) < 3.5f) return;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                var room = HouseLayout.Rooms[rng.Next(HouseLayout.Rooms.Count)];
                var p = new Vector3(Mathf.Lerp(room.Rect.xMin + 0.5f, room.Rect.xMax - 0.5f, (float)rng.NextDouble()), 0f,
                                    Mathf.Lerp(room.Rect.yMin + 0.5f, room.Rect.yMax - 0.5f, (float)rng.NextDouble()));
                float d = Vector3.Distance(p, me);
                if (d < 3.4f || d > 6.5f) continue;
                if (Vector3.Distance(p, HouseLayout.BasePosition) < SafeRadius + 1.5f) continue;
                SpawnGhost(p);
                nextGhostTime = clock + Mathf.Lerp(40f, 70f, (float)rng.NextDouble());
                return;
            }
        }

        /// <summary>Bir gölge oluşturur (testler de kullanır).</summary>
        public Ghost SpawnGhost(Vector3 position)
        {
            var go = new GameObject("Ghost");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(position.x, 0f, position.z);
            var g = go.AddComponent<Ghost>();
            g.WanderTarget = g.Position;
            if (ghostTemplate != null)
            {
                var inst = Instantiate(ghostTemplate, arRoot);
                inst.name = "AR_Ghost";
                inst.SetActive(false);
                g.Visual = inst.GetComponent<GhostVisual>();
            }
            ghosts.Add(g);
            return g;
        }

        void DestroyGhost(Ghost g)
        {
            if (g.Visual != null) { if (Application.isPlaying) Destroy(g.Visual.gameObject); else DestroyImmediate(g.Visual.gameObject); }
            if (Application.isPlaying) Destroy(g.gameObject); else DestroyImmediate(g.gameObject);
        }

        void UpdateGhosts(Vector3 me, float dt)
        {
            float threat = 0f;
            float noise = Noise;
            bool led = link.LedOn;
            Vector3 basePos = HouseLayout.BasePosition;

            for (int i = ghosts.Count - 1; i >= 0; i--)
            {
                var g = ghosts[i];
                if (g == null) { ghosts.RemoveAt(i); continue; }
                if (g.State == GhostState.Gone) { ghosts.RemoveAt(i); DestroyGhost(g); continue; }
                g.Age += dt;
                float d = Vector3.Distance(me, g.Position);

                // fark etme: yakınsa her zaman; gürültü ve ışık menzili büyütür; hareketsiz + ışıksız olunca biraz saklanır
                float notice = 1.7f + noise * 2.6f + (led ? 2.6f : 0f);
                if (noise < 0.15f && !led) notice = 1.0f;
                bool detected = d < notice && !InBase;

                if (g.State == GhostState.Drift)
                {
                    g.WanderTimer -= dt;
                    if (g.WanderTimer <= 0f || Vector3.Distance(g.Position, g.WanderTarget) < 0.1f)
                    {
                        g.WanderTimer = 3f + (float)rng.NextDouble() * 3f;
                        var room = HouseLayout.RoomAt(g.Position);
                        g.WanderTarget = new Vector3(Mathf.Lerp(room.Rect.xMin + 0.4f, room.Rect.xMax - 0.4f, (float)rng.NextDouble()), 0f,
                                                     Mathf.Lerp(room.Rect.yMin + 0.4f, room.Rect.yMax - 0.4f, (float)rng.NextDouble()));
                    }
                    GhostStep(g, g.WanderTarget, 0.14f * dt, basePos);
                    if (detected)
                    {
                        g.State = GhostState.Chase;
                        g.LostTimer = 0f;
                        Notice?.Invoke("GÖLGE SENİ FARK ETTİ  ·  KAÇ");
                        if (!tutorialGhost)
                        {
                            tutorialGhost = true;
                            Comms?.Invoke("Bu bir GÖLGE. Toplanmaz: kaç, ya da LED'i üzerine tut. Üssün çevresi güvenli.");
                        }
                        GhostSpotted?.Invoke(g);
                    }
                    else if (g.Age > 110f && d > 5f) g.State = GhostState.Gone;
                }
                else
                {
                    g.LostTimer = detected ? 0f : g.LostTimer + dt;
                    float speed = Mathf.Min(0.3f, GhostBaseSpeed + ChapterIndex * 0.012f);

                    // fener: ışığın altındaki gölge donar; yeterince tutarsan erir
                    Vector3 toGhost = g.Position - me;
                    Vector3 fwd = Flat(robot.forward);
                    bool lit = led && d < 3.4f && d > 0.3f && Vector3.Angle(fwd, toGhost) < 40f;
                    if (lit)
                    {
                        g.Burn += dt;
                        if (!tutorialLight) { tutorialLight = true; Comms?.Invoke("Fener gölgeyi dondurdu. Işığı üzerinde tut, erir."); }
                        if (g.Burn >= 2.5f) { DissolveGhost(g); continue; }
                    }
                    else
                    {
                        g.Burn = Mathf.Max(0f, g.Burn - dt * 0.5f);
                        GhostStep(g, me, speed * dt, basePos);
                    }
                    if (InBase || d > 6f || g.LostTimer > 5f)
                    {
                        g.State = GhostState.Drift;
                        g.WanderTimer = 0f;
                        Notice?.Invoke("GÖLGEDEN KURTULDUN");
                        GhostLost?.Invoke();
                    }
                    else if (d < 0.42f)
                    {
                        HitByGhost(g);
                        continue;
                    }
                }

                g.Alert = Mathf.MoveTowards(g.Alert, g.State == GhostState.Chase ? 1f : 0f, dt * 2f);
                float near = Mathf.Clamp01(1f - (d - 0.4f) / 3.4f);
                threat = Mathf.Max(threat, g.State == GhostState.Chase ? Mathf.Max(near, 0.25f) : near * 0.5f);
            }
            GhostThreat = Mathf.MoveTowards(GhostThreat, threat, dt * 2.5f);
        }

        /// <summary>Gölge duvarlardan geçer; yalnızca üssün güvenli alanına giremez.</summary>
        static void GhostStep(Ghost g, Vector3 target, float maxStep, Vector3 basePos)
        {
            Vector3 delta = target - g.Position;
            float dist = delta.magnitude;
            if (dist < 0.001f) return;
            Vector3 next = g.Position + delta / dist * Mathf.Min(maxStep, dist);
            Vector3 fromBase = next - basePos;
            fromBase.y = 0f;
            if (fromBase.magnitude < SafeRadius)
                next = basePos + (fromBase.sqrMagnitude < 0.0001f ? Vector3.forward : fromBase.normalized) * SafeRadius;
            g.transform.position = new Vector3(next.x, 0f, next.z);
        }

        void DissolveGhost(Ghost g)
        {
            g.State = GhostState.Gone;
            Essence += 2;
            nextGhostTime = Mathf.Max(nextGhostTime, clock + 30f);
            Notice?.Invoke("GÖLGE ERİDİ  ·  +2 ÖZ");
            GhostLost?.Invoke();
            EnergyChanged?.Invoke();
        }

        void HitByGhost(Ghost g)
        {
            link.AddBattery(-0.12f);
            bool lostOne = false;
            if (carried.Count > 0)
            {
                var victim = carried[rng.Next(carried.Count)];
                carried.Remove(victim);
                if (victim.Visual != null) { if (Application.isPlaying) Destroy(victim.Visual.gameObject); else DestroyImmediate(victim.Visual.gameObject); }
                if (Application.isPlaying) Destroy(victim.gameObject); else DestroyImmediate(victim.gameObject);
                lostOne = true;
            }
            g.State = GhostState.Gone;
            nextGhostTime = Mathf.Max(nextGhostTime, clock + 45f);
            Notice?.Invoke(lostOne ? "GÖLGE DOKUNDU  ·  BİR KAYIT KAYBOLDU  ·  PİL -12%" : "GÖLGE DOKUNDU  ·  PİL -12%");
            GhostHit?.Invoke(lostOne);
            EnergyChanged?.Invoke();
        }

        // ------------------------------------------------------------------ sonar

        public bool TrySonar()
        {
            if (!Running || SonarCooldownLeft > 0f || link.Telemetry.Battery01 < 0.02f) return false;
            SonarCooldownLeft = Profile.SonarCooldown;
            link.AddBattery(-sonarBatteryCost);
            lidar?.TriggerPulse(Profile.SonarRadius);
            SonarFired?.Invoke();

            int found = 0;
            Vector3 me = Flat(robot.position);
            foreach (var f in finds)
            {
                if (f == null || !f.Alive || f.Kind != FindKind.Hidden) continue;
                if (Vector3.Distance(me, f.Position) > Profile.SonarRadius) continue;
                bool newly = !f.Revealed;
                f.PingUntil = Time.time + 12f;
                f.Revealed = true;
                found++;
                if (newly) OnRevealed(f);
            }
            if (found == 0) Notice?.Invoke("SONAR  ·  YAKINDA BİR ŞEY YOK");
            else Notice?.Invoke("SONAR  ·  " + found + " GİZLİ SİNYAL");
            EnergyChanged?.Invoke();
            return true;
        }

        // ------------------------------------------------------------------ üs

        void UpdateDepot(Vector3 me, float dt)
        {
            InBase = Vector3.Distance(me, HouseLayout.BasePosition) < depotRadius;
            if (InBase)
            {
                if (link.Telemetry.Battery01 < 1f) link.AddBattery(depotChargePerSecond * dt);
                if (!wasInBase)
                {
                    Calibrated?.Invoke();
                    if (carried.Count > 0) Deliver();
                    else if (link.Telemetry.Battery01 < 0.95f) Notice?.Invoke("ÜS  ·  ŞARJ VE KALİBRASYON");
                }
            }
            wasInBase = InBase;
        }

        /// <summary>Taşınan bulguları teslim eder: öz, günlük, hikaye ilerlemesi.</summary>
        public void Deliver()
        {
            if (carried.Count == 0) return;
            int count = carried.Count, essence = 0;
            bool chapterDone = false;
            foreach (var f in carried)
            {
                essence += f.Essence;
                TotalDelivered++;
                DeliveredByKind[(int)f.Kind]++;
                codexMask |= 1 << (int)f.Kind;
                if (!IsFinished && Matches(Chapter, f))
                {
                    ChapterProgress++;
                    if (ChapterProgress >= Chapter.Count) chapterDone = true;
                }
                if (f.Visual != null) { if (Application.isPlaying) Destroy(f.Visual.gameObject); else DestroyImmediate(f.Visual.gameObject); }
                if (Application.isPlaying) Destroy(f.gameObject); else DestroyImmediate(f.gameObject);
            }
            carried.Clear();
            Essence += essence;
            Delivered?.Invoke(count, essence);
            Notice?.Invoke(count + " BULGU TESLİM EDİLDİ  ·  +" + essence + " ÖZ");
            EnergyChanged?.Invoke();

            if (chapterDone) CompleteChapter();
            Save();
        }

        static bool Matches(ChapterDef c, Find f)
        {
            switch (c.Goal)
            {
                case GoalKind.DeliverAny: return f.Kind != FindKind.Legend;
                case GoalKind.DeliverKind: return f.Kind == c.Kind;
                case GoalKind.DeliverRoom: return f.Room != null && f.Room.Kind == c.Room;
                case GoalKind.Legend: return f.Kind == FindKind.Legend;
            }
            return false;
        }

        void CompleteChapter()
        {
            var done = Chapter;
            int index = ChapterIndex;
            ChapterIndex++;
            ChapterProgress = 0;
            Essence += 3;
            ChapterCompleted?.Invoke(index);
            Notice?.Invoke("BÖLÜM TAMAMLANDI  ·  +3 ÖZ");
            Comms?.Invoke(done.Outro);
            if (ChapterIndex >= Theme.Chapters.Length)
            {
                IsFinished = true;
                ChapterIndex = Theme.Chapters.Length - 1;
                Finished?.Invoke();
                return;
            }
            if (introRoutine != null) StopCoroutine(introRoutine);
            introRoutine = StartCoroutine(Intro(Chapter, 7f));
        }

        void StartChapterIntro()
        {
            if (introRoutine != null) StopCoroutine(introRoutine);
            if (IsFinished) return;
            introRoutine = StartCoroutine(Intro(Chapter, 1.0f));
        }

        IEnumerator Intro(ChapterDef chapter, float delay)
        {
            yield return new WaitForSeconds(delay);
            Notice?.Invoke(chapter.Title);
            for (int i = 0; i < chapter.Intro.Length; i++)
            {
                if (i > 0) yield return new WaitForSeconds(7f);
                Comms?.Invoke(chapter.Intro[i]);
            }
            if (chapter.Goal == GoalKind.Legend)
            {
                LegendRoom ??= FarthestRoom();
                yield return new WaitForSeconds(7f);
                Comms?.Invoke("Sinyal " + LegendRoom.Name + " tarafından geliyor.");
            }
        }

        // ------------------------------------------------------------------ yükseltme

        public bool CanBuy(UpgradeId id) => Profile.CanUpgrade(id) && Essence >= Profile.Cost(id);

        public bool Buy(UpgradeId id)
        {
            if (!CanBuy(id)) return false;
            Essence -= Profile.Cost(id);
            Profile.Add(id);
            ApplyProfile();
            EnergyChanged?.Invoke();
            Save();
            return true;
        }

        // ------------------------------------------------------------------ kurtarma

        void Rescue()
        {
            int lost = carried.Count;
            foreach (var f in carried)
            {
                if (f.Visual != null) { if (Application.isPlaying) Destroy(f.Visual.gameObject); else DestroyImmediate(f.Visual.gameObject); }
                if (Application.isPlaying) Destroy(f.gameObject); else DestroyImmediate(f.gameObject);
            }
            carried.Clear();
            ClearGhosts();
            link.Teleport(HouseLayout.BasePosition, HouseLayout.BaseYaw);
            link.SetBattery(0.4f);
            link.SetLed(false);
            Physics.SyncTransforms();
            wasInBase = true;
            Rescued?.Invoke();
            Notice?.Invoke("PİL BİTTİ  ·  ÜSSE ÇEKİLDİN");
            Comms?.Invoke(lost > 0 ? "Robot durdu ve üsse çekildi. Taşıdığın " + lost + " kayıt kayboldu. Pili izle, kayıtları erken teslim et." : "Robot durdu ve üsse çekildi. Pili izle.");
        }

        // ------------------------------------------------------------------ AR görseli

        void LateUpdate() => SyncVisuals();

        public void SyncVisuals()
        {
            if (rig == null) return;
            Vector3 me = Flat(robot.position);
            if (baseMarker != null)
            {
                baseMarker.position = rig.ToRig(HouseLayout.BasePosition);
                baseMarker.rotation = rig.ToRig(Quaternion.identity);
            }
            foreach (var f in finds)
            {
                if (f == null || f.Visual == null || f.State == FindState.Captured) continue;
                // duvarın/eşyanın arkasındaki şey çizilmez (gerçekte mesafe sensörü haritası bunu söyler)
                bool show = Running && f.Alive && f.Revealed && CanSee(me, f.Position);
                var go = f.Visual.gameObject;
                if (go.activeSelf != show)
                {
                    go.SetActive(show);
                    if (show && !f.RevealPlayed) { f.RevealPlayed = true; f.Visual.PlayReveal(); }
                }
                if (!show) continue;
                Vector3 toRobot = me - f.Position;
                toRobot.y = 0f;
                if (toRobot.sqrMagnitude < 0.0001f) toRobot = Vector3.forward;
                go.transform.position = rig.ToRig(f.Position);
                go.transform.rotation = rig.ToRig(Quaternion.LookRotation(toRobot.normalized, Vector3.up));
                f.Visual.Refresh(Time.deltaTime, f);
            }
            SyncGhostVisuals(me);
        }

        /// <summary>Robotun kamerası bu noktayı görebilir mi? Aradaki duvar ya da eşya varsa hayır.</summary>
        bool CanSee(Vector3 me, Vector3 target)
        {
            if (map == null) return true;
            return map.LineOfSight(me, target, 0.12f);
        }

        void SyncGhostVisuals(Vector3 me)
        {
            foreach (var g in ghosts)
            {
                if (g == null || g.Visual == null) continue;
                bool show = Running && g.Alive && Vector3.Distance(me, g.Position) < 4.2f && CanSee(me, g.Position);
                var go = g.Visual.gameObject;
                if (go.activeSelf != show) go.SetActive(show);
                if (!show) continue;
                Vector3 toRobot = me - g.Position;
                toRobot.y = 0f;
                if (toRobot.sqrMagnitude < 0.0001f) toRobot = Vector3.forward;
                go.transform.position = rig.ToRig(g.Position);
                go.transform.rotation = rig.ToRig(Quaternion.LookRotation(toRobot.normalized, Vector3.up));
                g.Visual.Refresh(Time.deltaTime, g);
            }
        }

        public Find NearestRevealed(float maxDistance)
        {
            Find best = null;
            float bestD = maxDistance;
            Vector3 me = Flat(robot.position);
            foreach (var f in finds)
            {
                if (f == null || !f.Alive || !f.Revealed) continue;
                float d = Vector3.Distance(me, f.Position);
                if (d < bestD) { bestD = d; best = f; }
            }
            return best;
        }

        /// <summary>Henüz görülmemiş ama yakındaki bulgular: haritada belirsiz "?" olarak gösterilir.</summary>
        /// <summary>Görünmeyen ama yakındaki en yakın bulgunun uzaklığı (yoksa büyük bir değer).</summary>
        public float NearestUnseenDistance()
        {
            float best = float.MaxValue;
            Vector3 me = Flat(robot.position);
            foreach (var f in finds)
            {
                if (f == null || !f.Alive || f.Revealed) continue;
                best = Mathf.Min(best, Vector3.Distance(me, f.Position));
            }
            return best;
        }

        public bool IsNearbyUnseen(Find f, float within)
        {
            if (f == null || !f.Alive || f.Revealed || f.Kind == FindKind.Hidden) return false;
            return Vector3.Distance(Flat(robot.position), f.Position) < within;
        }

        public string GoalText()
        {
            var c = Chapter;
            var names = Theme.NamesOf(c.Kind);
            switch (c.Goal)
            {
                case GoalKind.DeliverAny: return c.Count + " bulgu teslim et";
                case GoalKind.DeliverKind: return c.Count + " " + names.Name + " teslim et";
                case GoalKind.DeliverRoom:
                    foreach (var r in HouseLayout.Rooms) if (r.Kind == c.Room) return r.Name + ": " + c.Count + " bulgu teslim et";
                    return c.Count + " bulgu teslim et";
                case GoalKind.Legend: return names.Name + " kaydet ve teslim et";
            }
            return "";
        }

        static Vector3 Flat(Vector3 p) { p.y = 0f; return p; }
    }
}
