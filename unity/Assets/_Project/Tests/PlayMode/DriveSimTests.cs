using System.Collections;
using System.Linq;
using ARaki.Dev;
using ARaki.Game;
using ARaki.Robot;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ARaki.Tests
{
    /// <summary>Oyun akışı ve oyun mantığı testleri (PlayMode, gerçek sahne üzerinde).</summary>
    public class DriveSimTests
    {
        MockRobotLink robot;
        MissionController mission;
        GameSession session;
        MissionGenerator generator;
        Threat[] threats;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("DriveSim");
            yield return null;

            robot = Object.FindFirstObjectByType<MockRobotLink>();
            mission = Object.FindFirstObjectByType<MissionController>();
            session = Object.FindFirstObjectByType<GameSession>();
            generator = Object.FindFirstObjectByType<MissionGenerator>();
            threats = Object.FindObjectsByType<Threat>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            session.StartGame();
            yield return null;
            // Testler robotu kendileri yönetir: klavye girişini ve düşmanları kapat.
            robot.GetComponent<KeyboardDriveInput>().enabled = false;
            foreach (var t in threats) t.enabled = false;
        }

        [TearDown]
        public void Restore() => Time.timeScale = 1f;

        IEnumerable ActivePoints(PointKind kind) =>
            mission.Points.Where(p => p.gameObject.activeSelf && p.Kind == kind);

        MissionPoint FirstTower() => mission.Points.First(p => p.gameObject.activeSelf && p.Kind == PointKind.Tower);
        MissionPoint Base() => mission.Points.First(p => p.Kind == PointKind.Base);

        void Place(Vector3 position, float yaw) => robot.Teleport(position, yaw);

        // ---------------------------------------------------------------- görev mantığı

        [UnityTest]
        public IEnumerator ScanningATowerCompletesIt()
        {
            var tower = FirstTower();
            Place(tower.transform.position + new Vector3(-0.35f, 0f, 0f), 90f);

            yield return new WaitForSeconds(tower.ScanSeconds * mission.ScanTimeMultiplier + 0.8f);

            Assert.IsTrue(tower.Done, "Kule, yarıçapı içinde durulunca taranmalıydı");
        }

        [UnityTest]
        public IEnumerator FarAwayTowersAreNotScanned()
        {
            Place(Base().transform.position, 0f);

            yield return new WaitForSeconds(1.0f);

            foreach (MissionPoint p in ActivePoints(PointKind.Tower))
            {
                Assert.IsFalse(p.Done);
                Assert.AreEqual(0f, p.Progress, 0.001f);
            }
        }

        [UnityTest]
        public IEnumerator BaseStaysLockedUntilOtherPointsAreDone()
        {
            Place(Base().transform.position, 0f);

            yield return new WaitForSeconds(2.8f);

            Assert.IsTrue(Base().Locked);
            Assert.IsFalse(Base().Done, "Ana üs diğer hedefler bitmeden tamamlanmamalı");
        }

        // ---------------------------------------------------------------- LED ve düşman

        [UnityTest]
        public IEnumerator LedScaresAThreatInFrontOfRobot()
        {
            var threat = threats[0];
            threat.gameObject.SetActive(true);
            Place(Vector3.zero, 0f);
            threat.transform.position = new Vector3(0f, 0.28f, 0.8f);
            threat.enabled = true;
            robot.SetLed(true);

            yield return new WaitForSeconds(0.5f);

            Assert.AreEqual(ThreatState.Scared, threat.State, "LED yüzüne tutulan tehdit kaçmalı");
        }

        [UnityTest]
        public IEnumerator LedBehindThreatDoesNotScareIt()
        {
            var threat = threats[0];
            threat.gameObject.SetActive(true);
            Place(Vector3.zero, 0f);
            threat.transform.position = new Vector3(0f, 0.28f, -0.8f); // robotun arkasında
            threat.enabled = true;
            robot.SetLed(true);

            yield return new WaitForSeconds(0.4f);

            Assert.AreNotEqual(ThreatState.Scared, threat.State);
        }

        [UnityTest]
        public IEnumerator LedDrainsBattery()
        {
            float before = robot.Telemetry.Battery01;
            robot.SetLed(true);

            yield return new WaitForSeconds(2f);
            robot.SetLed(false);

            Assert.Less(robot.Telemetry.Battery01, before - 0.02f, "LED yakıt harcamalı");
        }

        // ---------------------------------------------------------------- robot

        [UnityTest]
        public IEnumerator FailsafeStopsRobotWhenCommandsStop()
        {
            Place(Base().transform.position, 0f);
            for (int i = 0; i < 20; i++)
            {
                robot.SendDrive(new DriveCommand(1f, 1f));
                yield return null;
            }
            Assert.Greater(Mathf.Abs(robot.Telemetry.ForwardSpeed), 0.01f, "Robot hareket etmeliydi");

            yield return new WaitForSeconds(1.5f);

            Assert.IsTrue(robot.FailsafeActive);
            Assert.AreEqual(0f, robot.Telemetry.ForwardSpeed, 0.01f, "Komut kesilince robot durmalı");
        }

        [UnityTest]
        public IEnumerator OppositeWheelCommandsTurnRobotInPlace()
        {
            Place(Base().transform.position, 0f);
            var start = robot.transform.position;
            for (int i = 0; i < 60; i++)
            {
                robot.SendDrive(new DriveCommand(1f, -1f)); // sol ileri, sağ geri: yerinde sağa döner
                yield return null;
            }

            float moved = Vector3.Distance(start, robot.transform.position);
            Assert.Less(moved, 0.03f, "Zıt komutlarda robot olduğu yerde dönmeli");
            Assert.Greater(Mathf.DeltaAngle(0f, robot.transform.eulerAngles.y), 3f, "Sağa dönmeli (yaw artar)");
        }

        // ---------------------------------------------------------------- görev üreticisi

        [Test]
        public void DifficultyGrowsWithLevel()
        {
            var easy = MissionGenerator.Plan(1);
            var hard = MissionGenerator.Plan(6);
            Assert.Less(easy.ThreatCount, hard.ThreatCount);
            Assert.LessOrEqual(easy.TowerCount, hard.TowerCount);
            Assert.LessOrEqual(hard.TowerCount, 4, "Havuzda en çok 4 kule var");
            Assert.LessOrEqual(hard.ThreatCount, 4, "Havuzda en çok 4 düşman var");
            Assert.Greater(hard.ThreatSpeed, easy.ThreatSpeed);
        }

        [Test]
        public void GeneratedMissionsKeepObjectivesApartAndOnFreeFloor()
        {
            for (int level = 1; level <= 6; level++)
                for (int seed = 1; seed <= 8; seed++)
                {
                    var spec = generator.Generate(level, seed * 31 + level);
                    var all = spec.TowerPositions.Append(spec.FuelPosition).Append(spec.BasePosition).ToArray();
                    for (int i = 0; i < all.Length; i++)
                        for (int j = i + 1; j < all.Length; j++)
                            Assert.GreaterOrEqual(Vector3.Distance(all[i], all[j]), 0.9f,
                                $"Seviye {level}, tohum {seed}: hedefler çok yakın");
                }
        }

        [Test]
        public void SameSeedGivesSameMission()
        {
            var a = generator.Generate(3, 777);
            var b = generator.Generate(3, 777);
            Assert.AreEqual(a.BasePosition, b.BasePosition);
            Assert.AreEqual(a.TowerPositions[0], b.TowerPositions[0]);
        }

        // ---------------------------------------------------------------- yükseltmeler

        [UnityTest]
        public IEnumerator UpgradesChangeRobotBehaviour()
        {
            session.Profile.Add(UpgradeId.TurboMotors);
            session.Profile.Add(UpgradeId.EfficientLed);
            session.Profile.Add(UpgradeId.FastScan);
            session.StartMission(5);
            yield return null;

            Assert.AreEqual(0.75f, robot.MaxPwmLimit, 0.001f, "Turbo motorlar PWM üst sınırını açmalı");
            robot.PwmLimit = 1f;
            Assert.AreEqual(0.75f, robot.PwmLimit, 0.001f, "PWM limiti üst sınırı aşamaz");
            Assert.AreEqual(0.6f, robot.LedDrainMultiplier, 0.001f);
            Assert.AreEqual(0.7f, mission.ScanTimeMultiplier, 0.001f);
        }

        // ---------------------------------------------------------------- oyun akışı

        [UnityTest]
        public IEnumerator PlayingAllObjectivesWinsTheMission()
        {
            mission.ScanTimeMultiplier = 0.15f;   // testi hızlandır
            var order = mission.Points.Where(p => p.gameObject.activeSelf)
                .OrderBy(p => p.Kind == PointKind.Base ? 1 : 0).ToList();

            foreach (var p in order)
            {
                Place(p.transform.position + new Vector3(0.3f, 0f, 0f), -90f);
                yield return new WaitForSeconds(0.7f);
            }
            yield return null;

            Assert.AreEqual(GameState.Won, session.State);
            Assert.Greater(session.Result.Total, 0);
            Assert.GreaterOrEqual(session.Result.Stars, 1);
        }

        [UnityTest]
        public IEnumerator EmptyBatteryLosesTheMission()
        {
            robot.SetBattery(0f);

            yield return new WaitForSeconds(0.3f);

            Assert.AreEqual(GameState.Lost, session.State);
        }

        [UnityTest]
        public IEnumerator PauseFreezesTimeAndResumeRestoresIt()
        {
            session.Pause();
            Assert.AreEqual(GameState.Paused, session.State);
            Assert.AreEqual(0f, Time.timeScale);

            session.Resume();
            Assert.AreEqual(GameState.Playing, session.State);
            Assert.AreEqual(1f, Time.timeScale);
            yield return null;
        }

        [UnityTest]
        public IEnumerator WinThenUpgradeStartsNextHarderMission()
        {
            session.ForceWin();
            Assert.AreEqual(GameState.Won, session.State);

            session.ShowUpgrades();
            Assert.AreEqual(GameState.Upgrade, session.State);
            Assert.AreEqual(3, session.Offers.Count, "Üç yükseltme önerilmeli");

            var chosen = session.Offers[0];
            session.ChooseUpgrade(0);
            yield return null;

            Assert.AreEqual(GameState.Playing, session.State);
            Assert.AreEqual(2, session.Level);
            Assert.AreEqual(1, session.Profile.Level(chosen));
        }

        [UnityTest]
        public IEnumerator RetryAfterLossKeepsTheSameLevel()
        {
            robot.SetBattery(0f);
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(GameState.Lost, session.State);

            session.Retry();
            yield return null;

            Assert.AreEqual(GameState.Playing, session.State);
            Assert.AreEqual(1, session.Level);
            Assert.Greater(robot.Telemetry.Battery01, 0.5f, "Yeni görev dolu yakıtla başlamalı");
        }
    }
}
