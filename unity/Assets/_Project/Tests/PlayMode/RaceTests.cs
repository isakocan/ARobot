using System.Collections;
using System.Linq;
using ARaki.Dev;
using ARaki.Game;
using ARaki.Robot;
using ARaki.Video;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ARaki.Tests
{
    /// <summary>Yarış modu testleri: pist üretimi, kapı geçişi, tur sayımı, mod geçişleri ve video yakalama oranı.</summary>
    public class RaceTests
    {
        MockRobotLink robot;
        GameSession session;
        RaceController race;
        MissionController mission;
        HazardController hazard;
        KeyboardDriveInput input;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("DriveSim");
            yield return null;

            robot = Object.FindFirstObjectByType<MockRobotLink>();
            session = Object.FindFirstObjectByType<GameSession>();
            race = Object.FindFirstObjectByType<RaceController>();
            mission = Object.FindFirstObjectByType<MissionController>();
            hazard = Object.FindFirstObjectByType<HazardController>();
            input = robot.GetComponent<KeyboardDriveInput>();
            session.ControlDriveInput = false;      // testler robotu kendileri sürer
        }

        [TearDown]
        public void Restore() => Time.timeScale = 1f;

        // ---------------------------------------------------------------- pist üretimi

        [Test]
        public void GeneratedTracksAreFreeOfObstaclesForManySeeds()
        {
            Physics.SyncTransforms();
            int checkedCount = 0;
            for (int level = 1; level <= 4; level++)
                for (int seed = 1; seed <= 25; seed++)
                {
                    var spec = race.Generate(level, seed * 31 + level);
                    Assert.IsTrue(race.IsValid(spec, out var reason), $"seviye {level} tohum {seed}: {reason}");
                    Assert.GreaterOrEqual(spec.Gates.Length, 4);
                    Assert.LessOrEqual(spec.Gates.Length, RaceController.MaxGates);
                    Assert.Greater(spec.Length, 6f);
                    Assert.Less(spec.Length, 18f);
                    checkedCount++;
                }
            Assert.AreEqual(100, checkedCount);
        }

        [Test]
        public void SameSeedGivesSameTrack()
        {
            var a = race.Generate(2, 77);
            var b = race.Generate(2, 77);
            Assert.AreEqual(a.Gates.Length, b.Gates.Length);
            for (int i = 0; i < a.Gates.Length; i++) Assert.AreEqual(0f, Vector3.Distance(a.Gates[i], b.Gates[i]), 1e-4f);
        }

        [Test]
        public void DifferentSeedsGiveDifferentTracks()
        {
            var a = race.Generate(1, 1);
            var b = race.Generate(1, 2);
            float diff = 0f;
            for (int i = 0; i < Mathf.Min(a.Gates.Length, b.Gates.Length); i++) diff += Vector3.Distance(a.Gates[i], b.Gates[i]);
            Assert.Greater(diff, 0.5f);
        }

        // ---------------------------------------------------------------- mod geçişi

        [UnityTest]
        public IEnumerator RaceModeSwitchesOffExploreFeatures()
        {
            session.StartRaceGame(5);
            yield return null;

            Assert.AreEqual(GameMode.Race, session.Mode);
            Assert.AreEqual(GameState.Playing, session.State);
            Assert.AreEqual(RacePhase.Countdown, race.Phase);
            foreach (var p in mission.Points) Assert.IsFalse(p.gameObject.activeSelf, p.name + " yarışta kapalı olmalı");
            foreach (var t in mission.Threats) Assert.IsFalse(t.gameObject.activeSelf);
            Assert.IsFalse(hazard.Active);
            Assert.IsFalse(input.enabled, "geri sayım sürerken sürüş kapalı olmalı");
            Assert.AreEqual(RaceController.CountdownSeconds, race.CountdownLeft, 0.5f);
        }

        [UnityTest]
        public IEnumerator CountdownEndsAndRaceStarts()
        {
            session.StartRaceGame(5);
            Time.timeScale = 4f;
            float limit = Time.realtimeSinceStartup + 6f;
            while (race.Phase == RacePhase.Countdown && Time.realtimeSinceStartup < limit) yield return null;
            Assert.AreEqual(RacePhase.Running, race.Phase);
        }

        [UnityTest]
        public IEnumerator MenuAndModeSwitchesKeepStateClean()
        {
            session.StartRaceGame(5);
            yield return null;
            session.ToMenu();
            yield return null;
            Assert.AreEqual(GameMode.Explore, session.Mode);
            Assert.AreEqual(RacePhase.Idle, race.Phase);
            Assert.IsFalse(race.Visible);

            session.StartGame();
            yield return null;
            Assert.AreEqual(GameMode.Explore, session.Mode);
            Assert.IsTrue(mission.Points.Any(p => p.gameObject.activeSelf), "keşif hedefleri geri gelmeli");
            Assert.IsTrue(hazard.Active);
        }

        // ---------------------------------------------------------------- kapılar ve turlar

        [UnityTest]
        public IEnumerator SkippingGatesDoesNotCountALap()
        {
            session.StartRaceGame(9);
            yield return null;
            race.SkipCountdown();
            var spec = race.Spec;

            // başlangıç çizgisini diğer kapıları geçmeden geç
            Vector3 g = spec.Gates[0], t = spec.Tangents[0];
            robot.Teleport(g - t * 0.4f, 0f); yield return null; race.Tick(0.05f);
            robot.Teleport(g + t * 0.4f, 0f); yield return null; race.Tick(0.05f);

            Assert.AreEqual(1, race.Lap);
            Assert.AreEqual(1, race.NextGate, "sırası gelmeyen kapı sayılmamalı");
            Assert.AreEqual(0, race.LapTimes.Count);
        }

        [UnityTest]
        public IEnumerator CrossingGateBackwardsDoesNotCount()
        {
            session.StartRaceGame(9);
            yield return null;
            race.SkipCountdown();
            var spec = race.Spec;
            bool warned = false;
            race.WrongWay += () => warned = true;

            Vector3 g = spec.Gates[1], t = spec.Tangents[1], right = TrackMesh_Right(t);
            robot.Teleport(g + t * 0.4f + right * 1.2f, 0f); yield return null; race.Tick(0.05f);   // kapının ilerisi, yan tarafta
            robot.Teleport(g + t * 0.3f, 0f); yield return null; race.Tick(0.05f);                    // kapının ilerisi, ortada
            robot.Teleport(g - t * 0.4f, 0f); yield return null; race.Tick(0.05f);                    // geriye doğru geçiş

            Assert.AreEqual(1, race.NextGate, "ters yönden geçiş sayılmamalı");
            Assert.IsTrue(warned, "ters yön uyarısı verilmeli");
        }

        [UnityTest]
        public IEnumerator PassingGateInOrderAdvancesToTheNextOne()
        {
            session.StartRaceGame(9);
            yield return null;
            race.SkipCountdown();
            var spec = race.Spec;
            int crossed = -1;
            race.GateCrossed += (gate, lapEnd) => crossed = gate;

            Vector3 g = spec.Gates[1], t = spec.Tangents[1];
            robot.Teleport(g - t * 0.4f, 0f); yield return null; race.Tick(0.05f);
            robot.Teleport(g + t * 0.4f, 0f); yield return null; race.Tick(0.05f);

            Assert.AreEqual(1, crossed);
            Assert.AreEqual(2 % spec.Gates.Length, race.NextGate);
            Assert.IsTrue(race.IsGatePassed(1));
        }

        [UnityTest]
        public IEnumerator DrivingThreeLapsFinishesTheRaceAndWins()
        {
            session.StartRaceGame(11);
            yield return null;
            race.SkipCountdown();
            input.enabled = false;
            Time.timeScale = 5f;

            var path = race.Spec.Path;
            int idx = 0;
            float timeout = Time.realtimeSinceStartup + 90f;
            float lastLog = 0f;
            while (race.Phase == RacePhase.Running && Time.realtimeSinceStartup < timeout)
            {
                Vector3 p = robot.transform.position; p.y = 0f;
                for (int i = 0; i < 24; i++)
                {
                    int next = (idx + 1) % path.Length;
                    if (Vector3.Distance(p, path[next]) < Vector3.Distance(p, path[idx])) idx = next; else break;
                }
                int look = idx;
                float acc = 0f;
                while (acc < 0.4f)
                {
                    int n = (look + 1) % path.Length;
                    acc += Vector3.Distance(path[look], path[n]);
                    look = n;
                }
                Vector3 dir = path[look] - p; dir.y = 0f;
                Vector3 fwd = robot.transform.forward; fwd.y = 0f;
                float err = Vector3.SignedAngle(fwd, dir, Vector3.up);
                float steer = Mathf.Clamp(err / 55f, -1f, 1f);
                float throttle = Mathf.Clamp(1f - Mathf.Abs(err) / 75f, 0.1f, 1f);
                float l = throttle + steer, r = throttle - steer;
                float max = Mathf.Max(1f, Mathf.Abs(l), Mathf.Abs(r));
                robot.SendDrive(new DriveCommand(l / max, r / max));

                if (Time.realtimeSinceStartup - lastLog > 8f)
                {
                    lastLog = Time.realtimeSinceStartup;
                    Debug.Log($"[RaceTest] tur {race.Lap}, sıradaki kapı {race.NextGate}, süre {race.RaceTime:0.0}, yakıt {robot.Telemetry.Battery01:0.00}");
                }
                yield return null;
            }

            Assert.AreEqual(RacePhase.Finished, race.Phase, $"yarış bitmedi: tur {race.Lap}, kapı {race.NextGate}, süre {race.RaceTime:0.0}");
            yield return null;
            yield return null;
            Assert.AreEqual(GameState.Won, session.State);
            Assert.AreEqual(3, race.LapTimes.Count);
            Assert.IsTrue(race.LapTimes.All(x => x > 3f), "tur süreleri gerçekçi olmalı");
            Assert.AreNotEqual(Medal.None, race.Result.Medal);
            Assert.Greater(robot.Telemetry.Battery01, 0f, "yakıt yarışı bitirmeye yetmeli");
            Debug.Log($"[RaceTest] toplam {race.Result.TotalSeconds:0.0} sn, madalya {race.Result.Medal}, turlar {string.Join(", ", race.Result.LapTimes.Select(x => x.ToString("0.0")))}, pist {race.Spec.Length:0.0} m, kalan yakıt {robot.Telemetry.Battery01:0.00}");
        }

        [UnityTest]
        public IEnumerator FinishedRaceContinuesToNextTrack()
        {
            session.StartRaceGame(5);
            yield return null;
            race.SkipCountdown();
            session.ForceRaceFinish(40f);
            yield return null;
            Assert.AreEqual(GameState.Won, session.State);
            Assert.AreNotEqual(Medal.None, race.Result.Medal);

            session.Continue();
            yield return null;
            Assert.AreEqual(GameState.Playing, session.State);
            Assert.AreEqual(2, session.Level);
            Assert.AreEqual(GameMode.Race, session.Mode);
            Assert.AreEqual(RacePhase.Countdown, race.Phase);
        }

        [UnityTest]
        public IEnumerator EmptyBatteryLosesTheRace()
        {
            session.StartRaceGame(5);
            yield return null;
            race.SkipCountdown();
            robot.SetBattery(0f);
            yield return null;
            yield return null;
            Assert.AreEqual(GameState.Lost, session.State);
        }

        static Vector3 TrackMesh_Right(Vector3 tangent) => ARaki.AR.TrackMesh.Right(tangent);

        // ---------------------------------------------------------------- performans

        [UnityTest]
        public IEnumerator VideoCaptureIsRateLimited()
        {
            var video = Object.FindFirstObjectByType<Sim360Camera>();
            for (int i = 0; i < 20; i++) yield return null;
            int before = video.CaptureCount;
            float start = Time.realtimeSinceStartup;
            int frames = 0;
            while (Time.realtimeSinceStartup - start < 1f) { frames++; yield return null; }
            int captures = video.CaptureCount - before;
            Debug.Log($"[Perf] 1 sn: {frames} kare, {captures} 360° yakalama");
            Assert.LessOrEqual(captures, 36, "yakalama ~30 Hz ile sınırlı olmalı");
            Assert.GreaterOrEqual(captures, 15);
        }

        [UnityTest]
        public IEnumerator LowerVideoQualityRendersFasterOrEqual()
        {
            var video = Object.FindFirstObjectByType<Sim360Camera>();
            // her karede yakalayarak (sınırı aşarak) asıl çizim maliyetini ölç
            IEnumerator Measure(System.Action<float> done)
            {
                for (int i = 0; i < 20; i++) { video.Capture(); yield return null; }
                int frames = 0;
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 1.5f) { video.Capture(); frames++; yield return null; }
                done((Time.realtimeSinceStartup - t0) / frames * 1000f);
            }

            float high = 0f, medium = 0f, low = 0f;
            video.SetQuality(VideoQuality.High);
            yield return Measure(v => high = v);
            video.SetQuality(VideoQuality.Medium);
            yield return Measure(v => medium = v);
            video.SetQuality(VideoQuality.Low);
            yield return Measure(v => low = v);
            video.SetQuality(VideoQuality.High);
            Debug.Log($"[Perf] 360° yakalama süresi: yüksek {high:0.00} ms, orta {medium:0.00} ms, düşük {low:0.00} ms");
            Assert.LessOrEqual(medium, high * 1.1f);
            Assert.LessOrEqual(low, medium * 1.1f);
        }
    }
}
