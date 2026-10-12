using System.Collections;
using System.Linq;
using ARaki.Dev;
using ARaki.Game;
using ARaki.Robot;
using ARaki.Sensing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ARaki.Tests
{
    /// <summary>Yüzey keşfi testleri: mesafe sensörü haritası, keşif sisi, numuneler, üs işlevleri ve haritalama hedefi.</summary>
    public class ExplorationTests
    {
        MockRobotLink robot;
        GameSession session;
        MissionController mission;
        MissionGenerator generator;
        ExplorationMap map;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("DriveSim");
            yield return null;

            robot = Object.FindFirstObjectByType<MockRobotLink>();
            session = Object.FindFirstObjectByType<GameSession>();
            mission = Object.FindFirstObjectByType<MissionController>();
            generator = Object.FindFirstObjectByType<MissionGenerator>();
            map = Object.FindFirstObjectByType<ExplorationMap>();

            session.StartGame();
            yield return null;
            robot.GetComponent<KeyboardDriveInput>().enabled = false;
            foreach (var t in Object.FindObjectsByType<Threat>(FindObjectsInactive.Include, FindObjectsSortMode.None)) t.enabled = false;
            Physics.SyncTransforms();
        }

        [TearDown]
        public void Restore() => Time.timeScale = 1f;

        MissionPoint Point(PointKind kind) => mission.Points.First(p => p.Kind == kind && p.gameObject.activeSelf);

        void Stand(Vector3 position) { robot.Teleport(position, 0f); Physics.SyncTransforms(); }

        // ---------------------------------------------------------------- sensör ve harita

        [UnityTest]
        public IEnumerator SensorMapsFloorAndWallsAroundTheRobot()
        {
            map.Clear();
            Stand(Point(PointKind.Base).transform.position);
            for (int i = 0; i < 4; i++) map.Scan();
            yield return null;

            Assert.Greater(map.ExploredArea, 2f, "açık zemin boş olarak işaretlenmeli");
            Assert.AreEqual(CellState.Free, map.At(robot.transform.position));
            int walls = 0;
            for (int z = 0; z < map.Height; z++) for (int x = 0; x < map.Width; x++) if (map.At(x, z) == CellState.Wall) walls++;
            Assert.Greater(walls, 5, "sensör duvar/mobilyaya çarpıp engel hücre bırakmalı");
        }

        [UnityTest]
        public IEnumerator ExploredAreaGrowsAsRobotMoves()
        {
            map.Clear();
            Stand(Point(PointKind.Base).transform.position);
            for (int i = 0; i < 3; i++) map.Scan();
            float first = map.ExploredArea;

            Stand(Point(PointKind.Station).transform.position + new Vector3(0.9f, 0f, 0f));
            for (int i = 0; i < 3; i++) map.Scan();
            yield return null;

            Assert.Greater(map.ExploredArea, first + 0.5f, "yeni yerden bakınca haritalanan alan büyümeli");
        }

        [UnityTest]
        public IEnumerator AnObstacleHidesWhatIsBehindIt()
        {
            map.Clear();
            Vector3 from = Point(PointKind.Base).transform.position;
            Vector3 dir = (-from).normalized;                 // odanın ortasına doğru
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = from + dir * 1.0f + Vector3.up * 0.3f;
            wall.transform.rotation = Quaternion.LookRotation(dir);
            wall.transform.localScale = new Vector3(1.4f, 0.6f, 0.15f);
            Stand(from);
            for (int i = 0; i < 4; i++) map.Scan();
            yield return null;

            Assert.AreEqual(CellState.Wall, map.At(from + dir * 0.93f), "engelin yüzü duvar olarak görünmeli");
            Assert.AreEqual(CellState.Unknown, map.At(from + dir * 1.9f), "engelin arkası bilinmiyor kalmalı");
            Object.Destroy(wall);
        }

        // ---------------------------------------------------------------- keşif sisi

        [UnityTest]
        public IEnumerator PointsStayHiddenUntilTheSensorFindsThem()
        {
            var station = Point(PointKind.Station);
            Assume.That(MissionController.FlatDistance(Point(PointKind.Base).transform.position, station.transform.position), Is.GreaterThan(2.1f));

            Assert.IsTrue(Point(PointKind.Base).Revealed, "üs baştan bilinir");
            Assert.IsFalse(station.Revealed, "istasyon keşfedilmemiş başlamalı");
            Assert.IsFalse(station.GetComponent<ARaki.AR.ARMirror>().Visual.gameObject.activeSelf, "keşfedilmemiş hedefin görseli gizli olmalı");

            var found = new System.Collections.Generic.List<MissionPoint>();
            mission.PointRevealed += p => found.Add(p);
            Stand(station.transform.position + new Vector3(0.8f, 0f, 0f));
            mission.Tick(0.1f);
            yield return null;

            Assert.IsTrue(station.Revealed);
            Assert.Contains(station, found);
            Assert.IsTrue(station.GetComponent<ARaki.AR.ARMirror>().Visual.gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator HiddenPointCannotBeScanned()
        {
            var station = Point(PointKind.Station);
            Assume.That(MissionController.FlatDistance(Point(PointKind.Base).transform.position, station.transform.position), Is.GreaterThan(2.1f));
            // robot sinyal mesafesinin dışında, tarama halkasında değil
            Stand(Point(PointKind.Base).transform.position);
            for (int i = 0; i < 30; i++) mission.Tick(0.1f);
            yield return null;
            Assert.IsFalse(station.Revealed);
            Assert.AreEqual(0f, station.Progress, 1e-4f);
        }

        // ---------------------------------------------------------------- numuneler ve üs

        [UnityTest]
        public IEnumerator SamplesAreCollectedAndDeliveredAtBase()
        {
            mission.RevealAll();
            var sample = mission.Points.First(p => p.Kind == PointKind.Sample && p.gameObject.activeSelf);
            Vector3 basePos = Point(PointKind.Base).transform.position;

            Stand(sample.transform.position + new Vector3(0.2f, 0f, 0f));
            for (int i = 0; i < 25; i++) mission.Tick(0.1f);
            Assert.IsTrue(sample.Done, "numune taranınca alınmalı");
            Assert.AreEqual(1, mission.SamplesCollected);
            Assert.AreEqual(1, mission.SamplesCarried);

            int delivered = 0;
            mission.SamplesDelivered += n => delivered += n;
            Stand(basePos);
            mission.Tick(0.1f);
            yield return null;

            Assert.AreEqual(1, delivered, "üse girince numune teslim edilmeli");
            Assert.AreEqual(0, mission.SamplesCarried);
        }

        [UnityTest]
        public IEnumerator BaseRechargesTheRobot()
        {
            robot.SetBattery(0.3f);
            Stand(Point(PointKind.Base).transform.position);
            for (int i = 0; i < 40; i++) mission.Tick(0.1f);
            yield return null;
            Assert.Greater(robot.Telemetry.Battery01, 0.45f, "üssün içinde robot şarj olmalı");
        }

        // ---------------------------------------------------------------- haritalama hedefi

        [UnityTest]
        public IEnumerator SurveyTargetIsRequiredToFinishTheMission()
        {
            mission.RevealAll();
            mission.SurveyTargetArea = 10000f;       // ulaşılamaz
            Point(PointKind.Station).Done = true;
            Point(PointKind.Base).Done = true;
            mission.Tick(0.1f);
            yield return null;
            Assert.IsFalse(mission.Complete, "haritalama bitmeden görev tamamlanmamalı");
            Assert.IsTrue(Point(PointKind.Base).Locked);

            mission.SurveyTargetArea = 0f;
            mission.Tick(0.1f);
            Assert.IsTrue(mission.Complete);
        }

        [UnityTest]
        public IEnumerator SurveyCompletesWhenEnoughAreaIsMapped()
        {
            bool done = false;
            mission.SurveyCompleted += () => done = true;
            map.Clear();
            mission.SurveyTargetArea = 3f;
            var spots = new[] { Point(PointKind.Base).transform.position, Point(PointKind.Station).transform.position + new Vector3(0.9f, 0f, 0f) };
            foreach (var s in spots)
            {
                Stand(s);
                for (int i = 0; i < 4; i++) map.Scan();
            }
            mission.Tick(0.1f);
            yield return null;
            Assert.IsTrue(done);
            Assert.IsTrue(mission.SurveyDone);
        }

        // ---------------------------------------------------------------- görev üretimi

        [Test]
        public void GeneratedMissionsPlaceSamplesOnFreeFloorAwayFromBase()
        {
            Physics.SyncTransforms();
            for (int level = 1; level <= 3; level++)
                for (int seed = 1; seed <= 20; seed++)
                {
                    var spec = generator.Generate(level, seed * 17 + level);
                    Assert.IsTrue(generator.IsValid(spec, out var reason), $"seviye {level} tohum {seed}: {reason}");
                    Assert.AreEqual(3, spec.SamplePositions.Length);
                    for (int i = 0; i < spec.SamplePositions.Length; i++)
                    {
                        Assert.GreaterOrEqual(Vector3.Distance(spec.SamplePositions[i], spec.BasePosition), 1.3f, "numune üsse çok yakın");
                        for (int j = i + 1; j < spec.SamplePositions.Length; j++)
                            Assert.GreaterOrEqual(Vector3.Distance(spec.SamplePositions[i], spec.SamplePositions[j]), 0.6f, "numuneler üst üste");
                    }
                }
        }

        [Test]
        public void SurveyAreaGrowsWithLevel()
        {
            Assert.Less(MissionGenerator.Plan(1).SurveyArea, MissionGenerator.Plan(4).SurveyArea);
        }
    }
}
