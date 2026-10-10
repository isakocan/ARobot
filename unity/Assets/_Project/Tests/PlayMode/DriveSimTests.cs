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
    /// <summary>Sürüş simülasyonu sahnesinin oyun mantığı testleri (PlayMode, gerçek sahne üzerinde).</summary>
    public class DriveSimTests
    {
        MockRobotLink robot;
        MissionController mission;
        Threat[] threats;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync("DriveSim");
            yield return null;

            robot = Object.FindFirstObjectByType<MockRobotLink>();
            mission = Object.FindFirstObjectByType<MissionController>();
            threats = Object.FindObjectsByType<Threat>(FindObjectsSortMode.None);

            // Testler robotu kendileri yönetir: klavye girişini ve düşmanları kapat.
            robot.GetComponent<KeyboardDriveInput>().enabled = false;
            foreach (var t in threats) t.enabled = false;
        }

        void Place(Transform t, Vector3 position, float yaw)
        {
            if (t == robot.transform) robot.Teleport(position, yaw);
            else t.position = position;
        }

        [UnityTest]
        public IEnumerator ScanningATowerCompletesIt()
        {
            var tower = mission.Points.First(p => p.Label == "KULE ALFA");
            Place(robot.transform, tower.transform.position + new Vector3(-0.35f, 0f, 0f), 90f);

            yield return new WaitForSeconds(tower.ScanSeconds + 0.8f);

            Assert.IsTrue(tower.Done, "Kule, yarıçapı içinde durulunca taranmalıydı");
        }

        [UnityTest]
        public IEnumerator FarAwayTowerIsNotScanned()
        {
            var tower = mission.Points.First(p => p.Label == "KULE BETA");
            Place(robot.transform, new Vector3(-1.6f, 0f, -1.4f), 0f);

            yield return new WaitForSeconds(1.0f);

            Assert.IsFalse(tower.Done);
            Assert.AreEqual(0f, tower.Progress, 0.001f);
        }

        [UnityTest]
        public IEnumerator BaseStaysLockedUntilOtherPointsAreDone()
        {
            var bas = mission.Points.First(p => p.Kind == PointKind.Base);
            Place(robot.transform, bas.transform.position, 0f);

            yield return new WaitForSeconds(2.8f);

            Assert.IsTrue(bas.Locked);
            Assert.IsFalse(bas.Done, "Ana üs diğer hedefler bitmeden tamamlanmamalı");
        }

        [UnityTest]
        public IEnumerator LedScaresAThreatInFrontOfRobot()
        {
            var threat = threats[0];
            Place(robot.transform, Vector3.zero, 0f);
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
            Place(robot.transform, Vector3.zero, 0f);
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

        [UnityTest]
        public IEnumerator FailsafeStopsRobotWhenCommandsStop()
        {
            Place(robot.transform, new Vector3(-1.2f, 0f, -1.5f), 0f);
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
            Place(robot.transform, new Vector3(-1.2f, 0f, -1.5f), 0f);
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
    }
}
