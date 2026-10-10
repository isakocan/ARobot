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

        MissionPoint Station() => mission.Points.First(p => p.Kind == PointKind.Station);
        MissionPoint Base() => mission.Points.First(p => p.Kind == PointKind.Base);
        MissionPoint Fuel() => mission.Points.First(p => p.Kind == PointKind.Fuel);

        /// <summary>İstasyonun bir sektörünün ortasında, tarama halkası içinde bir konum.</summary>
        Vector3 SectorSpot(MissionPoint station, int sector)
        {
            float a = (sector + 0.5f) * 360f / station.SectorCount * Mathf.Deg2Rad;
            return station.transform.position + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.85f;
        }

        void Place(Vector3 position, float yaw) => robot.Teleport(position, yaw);

        // ---------------------------------------------------------------- görev mantığı

        [UnityTest]
        public IEnumerator ScanningOneSectorFillsOnlyThatSector()
        {
            var st = Station();
            Place(SectorSpot(st, 0), 0f);

            yield return new WaitForSeconds(st.ScanSeconds * mission.ScanTimeMultiplier + 0.6f);

            Assert.IsTrue(st.SectorDone[0], "Robotun durduğu sektör taranmalıydı");
            Assert.IsFalse(st.SectorDone[1]);
            Assert.IsFalse(st.Done, "Tek sektör istasyonu tamamlamaz");
        }

        [UnityTest]
        public IEnumerator ScanningEverySectorCompletesTheStation()
        {
            var st = Station();
            mission.ScanTimeMultiplier = 0.2f;
            for (int i = 0; i < st.SectorCount; i++)
            {
                Place(SectorSpot(st, i), 0f);
                yield return new WaitForSeconds(st.ScanSeconds * 0.2f + 0.4f);
            }
            Assert.IsTrue(st.Done, "Tüm sektörler taranınca istasyon çevrimiçi olmalı");
        }

        [UnityTest]
        public IEnumerator StandingTooCloseToStationDoesNotScan()
        {
            var st = Station();
            Place(st.transform.position + new Vector3(0.3f, 0f, 0f), 0f);   // gövdenin içi: MinRadius'tan yakın

            yield return new WaitForSeconds(1.2f);

            Assert.AreEqual(0f, st.Progress, 0.001f);
        }

        [UnityTest]
        public IEnumerator FarAwayStationIsNotScanned()
        {
            Place(Base().transform.position, 0f);

            yield return new WaitForSeconds(1.0f);

            Assert.IsFalse(Station().Done);
            Assert.AreEqual(0f, Station().Progress, 0.001f);
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
            for (float t0 = Time.time; Time.time - t0 < 0.4f;)      // süre tabanlı: kare hızından bağımsız
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
            for (float t0 = Time.time; Time.time - t0 < 0.8f;)
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
            Assert.LessOrEqual(easy.SectorCount, hard.SectorCount);
            Assert.LessOrEqual(hard.ThreatCount, 4, "Havuzda en çok 4 düşman var");
            Assert.Greater(hard.ThreatSpeed, easy.ThreatSpeed);
            Assert.Less(hard.FlareInterval, easy.FlareInterval, "Parlamalar sıklaşmalı");
        }

        [Test]
        public void GeneratedMissionsKeepObjectivesApartAndOnFreeFloor()
        {
            for (int level = 1; level <= 6; level++)
                for (int seed = 1; seed <= 10; seed++)
                {
                    var spec = generator.Generate(level, seed * 31 + level);
                    string tag = $"Seviye {level}, tohum {seed}";
                    Assert.GreaterOrEqual(Vector3.Distance(spec.StationPosition, spec.BasePosition), 1.3f, tag + ": üs istasyona çok yakın");
                    Assert.GreaterOrEqual(Vector3.Distance(spec.StationPosition, spec.FuelPosition), 1.1f, tag + ": yakıt istasyona çok yakın");
                    Assert.IsTrue(generator.IsValid(spec, out string reason), tag + ": yerleşim geçersiz: " + reason);
                }
        }

        [Test]
        public void HunterThreatsSeeFartherButNarrower()
        {
            var t = threats[0];
            t.SetKind(ThreatKind.Sentinel);
            float distance = t.ViewDistance, angle = t.ViewHalfAngle;
            t.SetKind(ThreatKind.Hunter);
            Assert.Greater(t.ViewDistance, distance);
            Assert.Less(t.ViewHalfAngle, angle);
            t.SetKind(ThreatKind.Sentinel);
            Assert.AreEqual(distance, t.ViewDistance, 0.0001f, "Tip değişince eski değerler geri gelmeli");
        }

        [Test]
        public void SameSeedGivesSameMission()
        {
            var a = generator.Generate(3, 777);
            var b = generator.Generate(3, 777);
            Assert.AreEqual(a.BasePosition, b.BasePosition);
            Assert.AreEqual(a.StationPosition, b.StationPosition);
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
        public IEnumerator ScanningStationAndReturningToBaseWinsWithoutFuel()
        {
            mission.ScanTimeMultiplier = 0.15f;   // testi hızlandır
            var st = Station();
            for (int i = 0; i < st.SectorCount; i++)
            {
                Place(SectorSpot(st, i), 0f);
                yield return new WaitForSeconds(0.7f);
            }
            Place(Base().transform.position + new Vector3(0.3f, 0f, 0f), -90f);
            yield return new WaitForSeconds(0.7f);
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

        // ---------------------------------------------------------------- güneş parlaması

        Vector3? FindSpot(bool exposed)
        {
            var hazard = Object.FindFirstObjectByType<HazardController>();
            for (float x = -2.4f; x <= 2.4f; x += 0.3f)
                for (float z = -1.9f; z <= 1.9f; z += 0.3f)
                {
                    var p = new Vector3(x, 0f, z);
                    if (Physics.CheckSphere(new Vector3(x, 0.15f, z), 0.3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (hazard.ComputeExposed(p) == exposed) return p;
                }
            return null;
        }

        [UnityTest]
        public IEnumerator FlareDrainsFuelOnlyWhenExposed()
        {
            var hazard = Object.FindFirstObjectByType<HazardController>();
            var open = FindSpot(true);
            var shade = FindSpot(false);
            Assert.IsTrue(open.HasValue, "Odada açıkta kalınan bir yer olmalı");
            Assert.IsTrue(shade.HasValue, "Odada mobilyanın gölgesinde kalan bir yer olmalı");

            hazard.ForcePhase(HazardPhase.Storm);
            Place(open.Value, 0f);
            yield return null;
            robot.SetBattery(0.8f);
            yield return new WaitForSeconds(1.5f);
            float exposedLoss = 0.8f - robot.Telemetry.Battery01;

            Place(shade.Value, 0f);
            yield return null;
            robot.SetBattery(0.8f);
            yield return new WaitForSeconds(1.5f);
            float shadedLoss = 0.8f - robot.Telemetry.Battery01;

            Assert.Greater(exposedLoss, shadedLoss + 0.005f, "Açıkta kalan daha çok yakıt kaybetmeli");
        }

        [UnityTest]
        public IEnumerator FlareWarningMarksShelterSpots()
        {
            var hazard = Object.FindFirstObjectByType<HazardController>();
            hazard.ForcePhase(HazardPhase.Warning);
            yield return null;

            Assert.AreEqual(HazardPhase.Warning, hazard.Phase);
            Assert.Greater(hazard.ShelterSpots.Count, 0, "Uyarıda en az bir sığınak işaretlenmeli");
            foreach (var spot in hazard.ShelterSpots)
                Assert.IsFalse(hazard.ComputeExposed(spot), "İşaretlenen sığınak gerçekten gölgede olmalı");
        }

        [UnityTest]
        public IEnumerator FlareEndsAfterItsDuration()
        {
            var hazard = Object.FindFirstObjectByType<HazardController>();
            hazard.ForcePhase(HazardPhase.Storm);
            yield return null;
            Assert.AreEqual(HazardPhase.Storm, hazard.Phase);

            yield return new WaitForSeconds(session.Spec.FlareDuration + 1.0f);

            Assert.AreEqual(HazardPhase.Calm, hazard.Phase);
        }

        // ---------------------------------------------------------------- performans

        [UnityTest]
        public IEnumerator FrameTimeStaysReasonableWithFullScene()
        {
            session.StartMission(3);
            yield return null;
            for (int i = 0; i < 30; i++) yield return null;   // ısınma

            float start = Time.realtimeSinceStartup;
            const int frames = 90;
            for (int i = 0; i < frames; i++) yield return null;
            float ms = (Time.realtimeSinceStartup - start) / frames * 1000f;

            Debug.Log($"[Perf] ortalama kare süresi: {ms:0.0} ms ({1000f / ms:0} fps)");
            Assert.Less(ms, 120f, "Kare süresi çok yüksek: sahne bu bilgisayarda akıcı çalışmıyor olabilir");
        }

        [Test]
        public void LogFreeSpaceMap()
        {
            Physics.SyncTransforms();
            var sb = new System.Text.StringBuilder("[FreeMap] z yukarıdan aşağı, x soldan sağa; sayı = boş yarıçap (#=dolu, 9=1m+)");
            sb.AppendLine();
            for (float z = 2.2f; z >= -2.2f; z -= 0.2f)
            {
                for (float x = -2.8f; x <= 2.8f; x += 0.2f)
                {
                    int level = 0;
                    foreach (float r in new[] { 1.0f, 0.85f, 0.7f, 0.55f, 0.4f, 0.3f, 0.2f })
                        if (!Physics.CheckSphere(new Vector3(x, 0.15f, z), r, ~0, QueryTriggerInteraction.Ignore)) { level = Mathf.RoundToInt(r * 9f); break; }
                    sb.Append(level == 0 ? '#' : (char)('0' + Mathf.Min(9, level)));
                }
                sb.AppendLine();
            }
            sb.AppendLine("[Colliders]");
            foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
                sb.AppendLine($"{c.name} konum=({c.transform.position.x:0.00},{c.transform.position.y:0.00},{c.transform.position.z:0.00})  merkez=({c.bounds.center.x:0.00},{c.bounds.center.y:0.00},{c.bounds.center.z:0.00})  boyut=({c.bounds.size.x:0.00},{c.bounds.size.y:0.00},{c.bounds.size.z:0.00})");
            Debug.Log(sb.ToString());
            Assert.Pass();
        }
    }
}
