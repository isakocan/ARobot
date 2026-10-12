using System.Collections;
using System.Linq;
using ARaki.Dev;
using ARaki.Game;
using ARaki.Robot;
using ARaki.Sensing;
using ARaki.Theming;
using ARaki.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ARaki.Tests
{
    /// <summary>Serbest gezi mantığı: bulgu çıkışı, davranışlar, yakalama, teslim, pil, bölümler, laboratuvar ve kayıt.</summary>
    public class ExpeditionTests
    {
        MockRobotLink robot;
        GameSession session;
        Expedition ex;
        ExplorationMap map;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            Time.timeScale = 1f;
            SaveData.Delete(ThemeId.Home);
            ThemeCatalog.Set(ThemeId.Home, false);
            yield return SceneManager.LoadSceneAsync("DriveSim");
            yield return null;

            robot = Object.FindFirstObjectByType<MockRobotLink>();
            session = Object.FindFirstObjectByType<GameSession>();
            ex = Object.FindFirstObjectByType<Expedition>();
            map = Object.FindFirstObjectByType<ExplorationMap>();

            session.ControlDriveInput = false;
            session.StartGame(true);
            robot.GetComponent<KeyboardDriveInput>().enabled = false;
            ex.enabled = false;               // testler Tick'i kendileri çağırır
            ex.AutoSpawn = false;             // bulguları da kendileri koyar (spawn testleri açar)
            ex.Seed(7);
            Physics.SyncTransforms();
            yield return null;
        }

        [TearDown]
        public void Restore()
        {
            Time.timeScale = 1f;
            ex.NoiseOverride = -1f;
        }

        Vector3 Ahead(float meters) { var p = robot.transform.position + robot.transform.forward * meters; p.y = 0f; return p; }

        Find SpawnAhead(FindKind kind, float meters, int tier = 1) => ex.Spawn(kind, HouseLayout.RoomAt(Ahead(meters)), Ahead(meters), tier);

        void Run(float seconds, float dt = 0.1f) { for (float t = 0f; t < seconds; t += dt) ex.Tick(dt); }

        void Away() => robot.Teleport(new Vector3(-3.4f, 0f, -3.4f), 0f);

        // ---------------------------------------------------------------- çıkış

        [UnityTest]
        public IEnumerator FindsAppearOnlyOnMappedFreeFloor()
        {
            ex.AutoSpawn = true;
            map.Clear();
            for (int i = 0; i < 3; i++) map.Scan();
            Run(40f, 0.5f);
            yield return null;

            Assert.Greater(ex.Finds.Count, 0, "harita dolunca bulgular çıkmalı");
            foreach (var f in ex.Finds)
            {
                Assert.AreEqual(CellState.Free, map.At(f.Position), "bulgu haritada bilinen boş zeminde olmalı: " + f.Kind);
                Assert.AreSame(f.Room, HouseLayout.RoomAt(f.Position));
                Assert.GreaterOrEqual(Vector3.Distance(f.Position, robot.transform.position), 1.0f, "robotun yüzüne çıkmamalı");
            }
        }

        [UnityTest]
        public IEnumerator NothingSpawnsWhereTheMapIsEmpty()
        {
            ex.AutoSpawn = true;
            map.Clear();            // hiç taranmamış
            Run(30f, 0.5f);
            yield return null;
            Assert.AreEqual(0, ex.Finds.Count, "haritalanmamış yerde bulgu çıkmamalı");
        }

        [UnityTest]
        public IEnumerator RoomsNeverExceedTheirTarget()
        {
            ex.AutoSpawn = true;
            for (int i = 0; i < 3; i++) map.Scan();
            Run(120f, 0.5f);
            yield return null;
            foreach (var room in HouseLayout.Rooms)
                Assert.LessOrEqual(ex.Finds.Count(f => f.Alive && f.Room == room), room.TargetFinds, room.Name);
        }

        [UnityTest]
        public IEnumerator HiddenFindsAreNotSpawnedBeforeTheyAreTaught()
        {
            ex.AutoSpawn = true;
            for (int i = 0; i < 3; i++) map.Scan();
            Run(80f, 0.5f);
            yield return null;
            Assert.AreEqual(0, ex.Finds.Count(f => f.Kind == FindKind.Hidden), "sonar öğretilmeden gizli bulgu çıkmamalı");
        }

        // ---------------------------------------------------------------- görünürlük ve yakalama

        [UnityTest]
        public IEnumerator FindsBecomeVisibleWhenNearAndInSight()
        {
            map.Clear();
            var f = SpawnAhead(FindKind.Relic, 1.8f);
            Physics.SyncTransforms();
            bool seen = false;
            ex.FindRevealed += x => seen = x == f;
            ex.Tick(0.1f);
            yield return null;
            Assert.IsTrue(f.Revealed);
            Assert.IsTrue(seen);
        }

        [UnityTest]
        public IEnumerator FarFindStaysHidden()
        {
            var f = SpawnAhead(FindKind.Relic, 1.8f);
            f.transform.position = new Vector3(3.4f, 0f, 2.5f);     // başka odada, çok uzak
            ex.Tick(0.1f);
            yield return null;
            Assert.IsFalse(f.Revealed);
        }

        [UnityTest]
        public IEnumerator RelicIsCapturedByWaitingNearIt()
        {
            var f = SpawnAhead(FindKind.Relic, 0.35f);
            Run(3f);
            yield return null;
            Assert.AreEqual(FindState.Captured, f.State);
            Assert.AreEqual(1, ex.Carried.Count);
        }

        [UnityTest]
        public IEnumerator RelicIsNotCapturedWhileDrivingFast()
        {
            var f = SpawnAhead(FindKind.Relic, 0.35f);
            ex.NoiseOverride = 0.9f;
            Run(4f);
            yield return null;
            Assert.AreNotEqual(FindState.Captured, f.State, "hızla geçerken kaydedilmemeli");
        }

        [UnityTest]
        public IEnumerator ShyFindFleesFromLoudRobot()
        {
            var f = SpawnAhead(FindKind.Shy, 1.0f);
            ex.NoiseOverride = 0.9f;
            Run(1.2f);
            yield return null;
            Assert.AreEqual(FindState.Fleeing, f.State, "gürültülü robottan kaçmalı");
            Assert.AreEqual(1, f.Flees);
            float before = Vector3.Distance(f.Position, robot.transform.position);
            Run(1.5f);
            Assert.Greater(Vector3.Distance(f.Position, robot.transform.position), before, "kaçarken uzaklaşmalı");
        }

        [UnityTest]
        public IEnumerator ShyFindIsCapturedWhenRobotIsQuiet()
        {
            var f = SpawnAhead(FindKind.Shy, 0.4f);
            ex.NoiseOverride = 0.05f;
            Run(3f);
            yield return null;
            Assert.AreEqual(FindState.Captured, f.State);
            Assert.AreEqual(0, f.Flees);
        }

        [UnityTest]
        public IEnumerator ShyFindIgnoresQuietRobotButFleesFromLed()
        {
            var f = SpawnAhead(FindKind.Shy, 1.2f);
            ex.NoiseOverride = 0.05f;
            Run(1f);
            Assert.AreEqual(FindState.Idle, f.State, "sessiz robottan ürkmemeli");
            robot.SetLed(true);
            Run(1.2f);
            yield return null;
            Assert.AreEqual(FindState.Fleeing, f.State, "ürkek bulgu ışıktan kaçmalı");
        }

        [UnityTest]
        public IEnumerator ShyFindEscapesAfterFiveScares()
        {
            var f = SpawnAhead(FindKind.Shy, 1.0f);
            bool fled = false;
            ex.FindFled += x => fled = true;
            ex.NoiseOverride = 0.9f;
            for (int i = 0; i < 12 && f != null && f.State != FindState.Gone; i++)
            {
                // robot her seferinde yeniden yaklaşır
                Vector3 target = f.Position - (f.Position - robot.transform.position).normalized * 0.9f;
                robot.Teleport(new Vector3(target.x, 0f, target.z), 0f);
                Run(2f);
            }
            yield return null;
            Assert.IsTrue(fled, "beş ürkütmeden sonra kaçmalı");
        }

        [UnityTest]
        public IEnumerator LureFindComesToTheLedAndIsCaptured()
        {
            robot.Teleport(new Vector3(-3.4f, 0f, -2.4f), 0f);      // salonun açık yeri: bulgu da aynı odada kalsın
            Physics.SyncTransforms();
            var f = SpawnAhead(FindKind.Lure, 1.4f);
            ex.Tick(0.1f);
            Assert.IsTrue(f.Revealed);
            Vector3 start = f.Position;
            Run(2f);
            Assert.AreEqual(0f, Vector3.Distance(start, f.Position), 0.01f, "LED kapalıyken yerinden kıpırdamamalı");

            robot.SetLed(true);
            Run(6f);
            yield return null;
            Assert.AreEqual(FindState.Captured, f.State, "LED ile çağrılan bulgu yakalanmalı");
        }

        [UnityTest]
        public IEnumerator HiddenFindNeedsSonar()
        {
            var f = SpawnAhead(FindKind.Hidden, 0.4f);
            Run(3f);
            Assert.IsFalse(f.Revealed, "sonarsız görünmemeli");
            Assert.AreNotEqual(FindState.Captured, f.State);

            Assert.IsTrue(ex.TrySonar());
            Assert.IsTrue(f.Revealed, "sonar gizli bulguyu göstermeli");
            Run(3f);
            yield return null;
            Assert.AreEqual(FindState.Captured, f.State);
        }

        [UnityTest]
        public IEnumerator SonarHasCooldownAndCostsBattery()
        {
            robot.SetBattery(0.5f);
            Assert.IsTrue(ex.TrySonar());
            Assert.IsFalse(ex.TrySonar(), "soğuma süresinde tekrar atılmamalı");
            yield return null;
            Assert.Less(robot.Telemetry.Battery01, 0.5f, "sonar pil harcamalı");
            Run(6f);
            Assert.IsTrue(ex.TrySonar(), "soğuma bitince yeniden hazır olmalı");
        }

        [UnityTest]
        public IEnumerator SonarOnlyReachesNearbyFinds()
        {
            var near = SpawnAhead(FindKind.Hidden, 1.5f);
            var far = ex.Spawn(FindKind.Hidden, HouseLayout.Kitchen, new Vector3(3.4f, 0f, 2.5f));
            ex.TrySonar();
            yield return null;
            Assert.IsTrue(near.Revealed);
            Assert.IsFalse(far.Revealed);
        }

        // ---------------------------------------------------------------- teslim, pil, üs

        Find CaptureOne(FindKind kind = FindKind.Relic)
        {
            var f = SpawnAhead(kind, 0.35f);
            if (kind == FindKind.Lure) robot.SetLed(true);
            if (kind == FindKind.Hidden) ex.TrySonar();
            Run(4f);
            robot.SetLed(false);
            return f;
        }

        [UnityTest]
        public IEnumerator CarriedFindsAreDeliveredAtBase()
        {
            var f = CaptureOne();
            Assert.AreEqual(1, ex.Carried.Count);
            int essenceBefore = ex.Essence;

            Away();
            ex.Tick(0.1f);
            Assert.IsFalse(ex.InBase);
            int delivered = 0;
            ex.Delivered += (n, e) => delivered += n;
            robot.Teleport(HouseLayout.BasePosition, 0f);
            ex.Tick(0.1f);
            yield return null;

            Assert.AreEqual(1, delivered, "üse girince teslim edilmeli");
            Assert.AreEqual(0, ex.Carried.Count);
            Assert.Greater(ex.Essence, essenceBefore);
            Assert.AreEqual(1, ex.TotalDelivered);
            Assert.IsTrue(ex.Discovered(FindKind.Relic), "günlüğe işlenmeli");
        }

        [UnityTest]
        public IEnumerator EmptyBatteryLosesCarriedFindsAndReturnsToBase()
        {
            CaptureOne();
            Assert.AreEqual(1, ex.Carried.Count);
            Away();
            bool rescued = false;
            ex.Rescued += () => rescued = true;
            robot.SetBattery(0f);
            ex.Tick(0.1f);
            yield return null;

            Assert.IsTrue(rescued);
            Assert.AreEqual(0, ex.Carried.Count, "pil bitince taşınanlar kaybolmalı");
            Assert.Less(Vector3.Distance(robot.transform.position, HouseLayout.BasePosition), 0.2f);
            Assert.AreEqual(0.4f, robot.Telemetry.Battery01, 0.05f);
        }

        [UnityTest]
        public IEnumerator BaseRechargesTheRobot()
        {
            robot.SetBattery(0.3f);
            robot.Teleport(HouseLayout.BasePosition, 0f);
            Run(5f);
            yield return null;
            Assert.Greater(robot.Telemetry.Battery01, 0.5f);
        }

        // ---------------------------------------------------------------- hikaye

        [UnityTest]
        public IEnumerator DeliveringEnoughFindsCompletesTheChapter()
        {
            int completed = -1;
            ex.ChapterCompleted += i => completed = i;
            int essenceBefore = ex.Essence;
            for (int i = 0; i < 3; i++) CaptureOne();
            Away();
            ex.Tick(0.1f);
            robot.Teleport(HouseLayout.BasePosition, 0f);
            ex.Tick(0.1f);
            yield return null;

            Assert.AreEqual(0, completed);
            Assert.AreEqual(1, ex.ChapterIndex, "bölüm ilerlemeli");
            Assert.AreEqual(0, ex.ChapterProgress);
            Assert.GreaterOrEqual(ex.Essence, essenceBefore + 3 + 3, "bulgu özü + bölüm ödülü");
        }

        [UnityTest]
        public IEnumerator OnlyMatchingFindsCountForKindChapters()
        {
            ex.DebugSetChapter(1);                          // 2 ürkek bulgu teslim et
            CaptureOne(FindKind.Relic);
            Away(); ex.Tick(0.1f);
            robot.Teleport(HouseLayout.BasePosition, 0f); ex.Tick(0.1f);
            Assert.AreEqual(0, ex.ChapterProgress, "farklı türdeki bulgu sayılmamalı");
            Assert.AreEqual(1, ex.ChapterIndex);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LegendAppearsInChapterSixAndFinishesTheStory()
        {
            ex.DebugSetChapter(5);
            ex.AutoSpawn = true;
            ex.Tick(0.1f);
            Assert.IsTrue(ex.IsLegendPending);
            var room = HouseLayout.Rooms.Where(r => r.Kind != RoomKind.Hall)
                .OrderByDescending(r => Vector3.Distance(r.Center, HouseLayout.BasePosition)).First();

            // robot o odayı haritalar
            var spots = new[] { room.Center, room.Center + Vector3.right * 0.8f, room.Center + Vector3.left * 0.8f };
            map.SurveyFrom(spots, 3);
            bool finished = false;
            ex.Finished += () => finished = true;
            Run(8f, 0.5f);
            yield return null;
            var legend = ex.Finds.FirstOrDefault(f => f.Kind == FindKind.Legend);
            Assert.IsNotNull(legend, "efsane bulgu uzak odada çıkmalı");
            Assert.AreSame(room, legend.Room);

            // robot ışıkla ve sessizce yanında bekler
            robot.Teleport(new Vector3(legend.Position.x, 0f, legend.Position.z) - Vector3.forward * 0.5f, 0f);
            Physics.SyncTransforms();
            robot.SetLed(true);
            ex.NoiseOverride = 0.05f;
            Run(10f);
            Assert.AreEqual(FindState.Captured, legend.State);
            robot.SetLed(false);
            Away(); ex.Tick(0.1f);
            robot.Teleport(HouseLayout.BasePosition, 0f); ex.Tick(0.1f);
            yield return null;
            Assert.IsTrue(finished, "efsane teslim edilince hikaye bitmeli");
            Assert.IsTrue(ex.IsFinished);
        }

        // ---------------------------------------------------------------- laboratuvar ve kayıt

        [UnityTest]
        public IEnumerator LabUpgradeCostsEssenceAndChangesTheRobot()
        {
            Assert.IsFalse(ex.Buy(UpgradeId.TurboMotors), "özü yokken alınamaz");
            ex.DebugSetEssence(10);
            float maxBefore = robot.MaxPwmLimit;
            Assert.IsTrue(ex.Buy(UpgradeId.TurboMotors));
            Assert.AreEqual(10 - 3, ex.Essence);
            Assert.Greater(robot.MaxPwmLimit, maxBefore, "motor güç limiti artmalı");
            Assert.AreEqual(1, ex.Profile.Level(UpgradeId.TurboMotors));

            Assert.IsTrue(ex.Buy(UpgradeId.WideSensor));
            Assert.Greater(map.Range, 3f, "sensör menzili artmalı");
            yield return null;
        }

        [UnityTest]
        public IEnumerator LabOpensOnlyAtBase()
        {
            Away();
            ex.Tick(0.1f);
            yield return null;
            Assert.IsFalse(session.OpenLab(), "üs dışında laboratuvar açılmamalı");
            robot.Teleport(HouseLayout.BasePosition, 0f);
            ex.Tick(0.1f);
            Assert.IsTrue(session.OpenLab());
            Assert.AreEqual(GameState.Lab, session.State);
            Assert.AreEqual(0f, Time.timeScale);
            session.CloseLab();
            Assert.AreEqual(GameState.Playing, session.State);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator ProgressIsSavedPerTheme()
        {
            CaptureOne();
            Away(); ex.Tick(0.1f);
            robot.Teleport(HouseLayout.BasePosition, 0f); ex.Tick(0.1f);
            int essence = ex.Essence;
            int delivered = ex.TotalDelivered;
            Assert.Greater(delivered, 0);

            ex.Begin(false);
            yield return null;
            Assert.AreEqual(essence, ex.Essence, "kayıt yüklenmeli");
            Assert.AreEqual(delivered, ex.TotalDelivered);

            ex.Begin(true);
            Assert.AreEqual(0, ex.Essence, "yeni oyun ilerlemeyi silmeli");

            // başka temanın kaydı ayrı tutulur
            Assert.IsNull(SaveData.Load(ThemeId.Home), "yeni oyun kaydı silmeli");
        }

        [UnityTest]
        public IEnumerator RealTimeLoopRunsWithoutErrors()
        {
            // Gerçek güncelleme döngüsüyle: robot sürer, LED yanıp söner, sonar atılır; arayüz, harita ve çıkış sistemi birlikte çalışır.
            ex.enabled = true;
            ex.AutoSpawn = true;
            Time.timeScale = 4f;
            float end = Time.realtimeSinceStartup + 14f;
            int frame = 0;
            while (Time.realtimeSinceStartup < end)
            {
                frame++;
                float turn = Mathf.Sin(frame * 0.02f);
                robot.SendDrive(new DriveCommand(0.8f + turn * 0.5f, 0.8f - turn * 0.5f));
                robot.SetLed((frame / 60) % 2 == 0);
                if (frame % 120 == 0) ex.TrySonar();
                yield return null;
            }
            Time.timeScale = 1f;
            Assert.Greater(map.ExploredArea, 6f, "gezerken harita dolmalı");
            Assert.Greater(ex.Finds.Count, 0, "bulgular çıkmış olmalı");
            Assert.Less(robot.Telemetry.Battery01, 1f, "pil harcanmalı");
        }

        // ---------------------------------------------------------------- gölgeler

        void PlaceRobotFar() { robot.Teleport(new Vector3(-3.4f, 0f, -3.4f), 0f); Physics.SyncTransforms(); }

        [UnityTest]
        public IEnumerator GhostChasesNoisyRobotAndTakesBatteryAndOneCarriedFind()
        {
            PlaceRobotFar();
            var carried = SpawnAhead(FindKind.Relic, 0.3f);
            carried.Revealed = true;
            carried.Progress = 0.99f;
            ex.NoiseOverride = 0f;
            Run(0.3f);
            ex.NoiseOverride = 0.6f;
            Assert.AreEqual(1, ex.Carried.Count, "kayıt taşınmalı");

            float before = robot.Telemetry.Battery01;
            robot.SetBattery(0.8f);
            var g = ex.SpawnGhost(robot.transform.position + Vector3.forward * 2.0f);
            bool hit = false;
            ex.GhostHit += _ => hit = true;
            Run(14f);
            yield return null;
            Assert.IsTrue(hit, "gölge yetişip dokunmalı");
            Assert.AreEqual(0, ex.Carried.Count, "dokunuş bir kaydı almalı");
            Assert.Less(robot.Telemetry.Battery01, 0.75f, "pil düşmeli");
            _ = before; _ = g;
        }

        [UnityTest]
        public IEnumerator GhostDoesNotNoticeQuietRobotFarAway()
        {
            PlaceRobotFar();
            ex.NoiseOverride = 0f;
            robot.SetLed(false);
            var g = ex.SpawnGhost(robot.transform.position + Vector3.forward * 2.4f);
            Run(4f);
            yield return null;
            Assert.AreEqual(GhostState.Drift, g.State, "sessiz ve ışıksız robotu 2,4 m'den fark etmemeli");
        }

        [UnityTest]
        public IEnumerator GhostGivesUpWhenRobotReachesBase()
        {
            robot.Teleport(HouseLayout.BasePosition + new Vector3(2.2f, 0f, 0f), 0f);
            Physics.SyncTransforms();
            ex.NoiseOverride = 0.6f;
            var g = ex.SpawnGhost(robot.transform.position + new Vector3(0.9f, 0f, 0f));
            Run(0.5f);
            Assert.AreEqual(GhostState.Chase, g.State);
            robot.Teleport(HouseLayout.BasePosition, 0f);
            Physics.SyncTransforms();
            Run(3f);
            yield return null;
            Assert.AreEqual(GhostState.Drift, g.State, "robot üste girince gölge bırakmalı");
            Assert.GreaterOrEqual(Vector3.Distance(g.Position, HouseLayout.BasePosition), 1.2f, "gölge güvenli alana girmemeli");
        }

        [UnityTest]
        public IEnumerator LightFreezesAndDissolvesAGhost()
        {
            PlaceRobotFar();
            robot.SetLed(true);
            ex.NoiseOverride = 0.3f;
            int before = ex.Essence;
            var g = ex.SpawnGhost(robot.transform.position + robot.transform.forward * 2.0f);
            Run(0.3f);
            Assert.AreEqual(GhostState.Chase, g.State, "ışık ve gürültü gölgeyi çeker");
            Vector3 start = g.Position;
            Run(1.0f);
            Assert.Less(Vector3.Distance(start, g.Position), 0.1f, "fenerin altında donmalı");
            Run(2.5f);
            yield return null;
            Assert.IsFalse(g != null && g.Alive, "yeterince ışık tutunca erimeli");
            Assert.Greater(ex.Essence, before, "eriyen gölge öz bırakmalı");
        }

        [UnityTest]
        public IEnumerator ShyFindGetsUneasyBeforeFleeing()
        {
            PlaceRobotFar();
            var f = SpawnAhead(FindKind.Shy, 1.2f);
            f.Revealed = true;
            ex.NoiseOverride = 0.7f;
            Run(0.5f);
            Assert.AreEqual(FindState.Idle, f.State, "ilk yarım saniyede kaçmamalı (tedirgin olur)");
            Assert.Greater(f.Alarm, 0.2f);
            Run(1.5f);
            yield return null;
            Assert.AreEqual(FindState.Fleeing, f.State, "gürültü sürerse kaçar");
        }

        [UnityTest]
        public IEnumerator FindsStaySparse()
        {
            ex.AutoSpawn = true;
            map.Clear();
            for (int i = 0; i < 3; i++) map.Scan();
            Run(90f, 0.5f);
            yield return null;
            Assert.LessOrEqual(ex.Finds.Count, Expedition.MaxAliveFinds, "aynı anda en fazla " + Expedition.MaxAliveFinds + " bulgu");
        }

        [UnityTest]
        public IEnumerator SessionStatesFollowTheGameFlow()
        {
            Assert.AreEqual(GameState.Playing, session.State);
            session.Pause();
            Assert.AreEqual(GameState.Paused, session.State);
            Assert.AreEqual(0f, Time.timeScale);
            session.Resume();
            Assert.AreEqual(GameState.Playing, session.State);
            session.ToMenu();
            Assert.AreEqual(GameState.Menu, session.State);
            Assert.IsFalse(ex.Running);
            yield return null;
        }
    }
}
