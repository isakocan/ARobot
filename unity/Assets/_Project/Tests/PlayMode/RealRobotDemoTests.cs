using System.Collections;
using System.IO;
using ARaki.Drive;
using ARaki.Robot;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ARaki.Tests
{
    /// <summary>
    /// Gerçek robot sahnesi robotsuz demoda: sürüş, vites, korna, melodi, kayıt. ARAKI_SHOT klasörü verilirse
    /// kokpitin ekran görüntülerini oraya kaydeder (direksiyon solda / sağda).
    /// </summary>
    public class RealRobotDemoTests
    {
        DriveController drive;
        RealRobotLink link;
        BuzzerSynth buzzer;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            PlayerPrefs.SetInt(RealRobotLink.DemoKey, 1);
            PlayerPrefs.DeleteKey("wheel");
            yield return SceneManager.LoadSceneAsync("RealRobot");
            yield return null;
            drive = Object.FindFirstObjectByType<DriveController>();
            link = Object.FindFirstObjectByType<RealRobotLink>();
            buzzer = Object.FindFirstObjectByType<BuzzerSynth>();
            link.UseDemo();
            yield return new WaitForSecondsRealtime(0.5f);
        }

        [UnityTest]
        public IEnumerator DrivesThroughGearsAndRamps()
        {
            Assert.IsTrue(link.Demo && link.IsConnected);
            drive.Settings.Gears = true;
            drive.TouchForward = 1;
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.GreaterOrEqual(drive.Model.Gear, 2, "1,5 sn'de 2. vitese geçmeli");
            Assert.Less(drive.Model.Speed, 0.7f, "tam güce bu kadar çabuk varmamalı");
            Assert.Greater(link.Status.motor.sol, 0, "demo Arduino motoru döndürmeli");
            yield return new WaitForSecondsRealtime(4f);
            Assert.AreEqual(4, drive.Model.Gear, "~5 sn'de 4. vites");
            drive.TouchForward = 0;
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.AreEqual(0, link.Status.motor.sol, "bırakınca durmalı");
        }

        [UnityTest]
        public IEnumerator HornAndMelodyPlayOnBuzzer()
        {
            drive.PlayMelody(2);
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.AreEqual(2, buzzer.Melody, "Megalovania çalmalı");
            drive.StopMelody();
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.AreEqual(0, buzzer.Melody);
            Assert.AreEqual(32, drive.Firmware.Songs.Count);
        }

        [UnityTest]
        public IEnumerator RecordsPhotoAndVideo()
        {
            string saved = null;
            drive.Recorder.Saved += p => saved = p;
            drive.Photo();
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.IsNotNull(saved, "fotoğraf kaydedilmeli");
            Assert.Greater(new FileInfo(saved).Length, 10000);
            File.Delete(saved);
            saved = null;
            drive.Recorder.StartVideo();
            drive.TouchForward = 1;
            yield return new WaitForSecondsRealtime(2f);
            drive.TouchForward = 0;
            drive.Recorder.StopVideo();
            yield return new WaitForSecondsRealtime(1f);
            Assert.IsNotNull(saved, "video kaydedilmeli");
            var info = new FileInfo(saved);
            Debug.Log($"[Test] video {info.Length / 1024} KB: {saved}");
            Assert.Greater(info.Length, 50000);
            string shots = System.Environment.GetEnvironmentVariable("ARAKI_SHOT");
            if (!string.IsNullOrEmpty(shots)) File.Copy(saved, Path.Combine(shots, "deneme_video.avi"), true);
            File.Delete(saved);
        }

        [UnityTest]
        public IEnumerator CockpitScreenshots()
        {
            string dir = System.Environment.GetEnvironmentVariable("ARAKI_SHOT");
            if (string.IsNullOrEmpty(dir)) yield break;
            foreach (var side in new[] { WheelSide.Left, WheelSide.Right })
            {
                drive.Settings.Wheel = side;
                drive.Settings.Mirrors = true;
                drive.Settings.Cockpit = true;
                drive.TouchForward = 1;
                drive.TouchTurn = side == WheelSide.Left ? 1 : 0;
                yield return new WaitForSecondsRealtime(2.2f);
                yield return Shot(Path.Combine(dir, "kokpit_" + side.ToString().ToLowerInvariant() + ".png"));
                drive.TouchForward = drive.TouchTurn = 0;
            }
            var cockpit = Object.FindFirstObjectByType<DriveCockpit>();
            cockpit.MenuOpen = true;
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot(Path.Combine(dir, "menu_acik.png"));
            cockpit.MenuOpen = false;
            drive.SettingsOpen = true;
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot(Path.Combine(dir, "ayarlar.png"));
            drive.SettingsOpen = false;
            drive.MusicMenuOpen = true;
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot(Path.Combine(dir, "muzik.png"));
        }

        static IEnumerator Shot(string path)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
        }
    }
}
