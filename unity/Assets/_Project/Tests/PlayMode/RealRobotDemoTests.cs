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

        int savedDemo;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            // Testler kullanıcının ayarını (demo mu gerçek robot mu) bozmasın
            savedDemo = PlayerPrefs.GetInt(RealRobotLink.DemoKey, 0);
            PlayerPrefs.SetInt(RealRobotLink.DemoKey, 1);
            yield return SceneManager.LoadSceneAsync("RealRobot");
            yield return null;
            drive = Object.FindFirstObjectByType<DriveController>();
            link = Object.FindFirstObjectByType<RealRobotLink>();
            buzzer = Object.FindFirstObjectByType<BuzzerSynth>();
            link.UseDemo();
            yield return new WaitForSecondsRealtime(0.5f);
        }

        [TearDown]
        public void RestorePrefs()
        {
            PlayerPrefs.SetInt(RealRobotLink.DemoKey, savedDemo);
            PlayerPrefs.Save();
        }

        [UnityTest]
        public IEnumerator DrivesThroughGearsAndRamps()
        {
            Assert.IsTrue(link.Demo && link.IsConnected);
            drive.Settings.Gearbox = GearMode.Automatic;
            drive.TouchForward = 1;
            yield return new WaitForSecondsRealtime(0.9f);
            Assert.AreEqual(1, drive.Model.Gear, "vites hemen büyümemeli: o viteste basılı tutmak gerekir");
            yield return new WaitForSecondsRealtime(1.1f);
            Assert.GreaterOrEqual(drive.Model.Gear, 2, "2 sn'de 2. vitese geçmeli");
            Assert.Less(drive.Model.Speed, 0.7f, "tam güce bu kadar çabuk varmamalı");
            Assert.Greater(link.Status.motor.sol, 0, "demo Arduino motoru döndürmeli");
            yield return new WaitForSecondsRealtime(5.4f);
            Assert.AreEqual(5, drive.Model.Gear, "~6,6 sn'de 5. vites");
            drive.TouchForward = 0;
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.AreEqual(0, link.Status.motor.sol, "bırakınca durmalı");
            drive.TouchForward = -1;   // geri vites (devir göstergesi geri viteste de hesaplanmalı)
            yield return new WaitForSecondsRealtime(1f);
            Assert.AreEqual(-1, drive.Model.Gear);
            Assert.Less(link.Status.motor.sol, 0, "geri gitmeli");
            drive.TouchForward = 0;
            yield return new WaitForSecondsRealtime(1f);
        }

        [UnityTest]
        public IEnumerator ManualGearboxShiftsOnRequest()
        {
            drive.Settings.Gearbox = GearMode.Manual;
            drive.TouchForward = 1;
            yield return new WaitForSecondsRealtime(2.5f);
            Assert.AreEqual(1, drive.Model.Gear, "manuelde vites kendiliğinden büyümez");
            Assert.LessOrEqual(drive.Model.Speed, DriveModel.GearTop[1] + 0.001f, "1. vitesin sınırında kalır");
            Assert.IsTrue(drive.Model.ShiftHint, "sınırda vites atma işareti yanar");
            drive.ShiftUp();
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.AreEqual(2, drive.Model.Gear);
            Assert.Greater(drive.Model.Speed, DriveModel.GearTop[1] + 0.05f, "vites atınca hızlanır");
            drive.ShiftDown();
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.AreEqual(1, drive.Model.Gear);
            drive.TouchForward = 0;
            drive.Settings.Gearbox = GearMode.Automatic;
            yield return new WaitForSecondsRealtime(1.5f);
        }

        [UnityTest]
        public IEnumerator HornAndMelodyPlayOnBuzzer()
        {
            drive.PlayMelody(1);
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.AreEqual(1, buzzer.Melody, "Megalovania çalmalı");
            drive.StopMelody();
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.AreEqual(0, buzzer.Melody);
            Assert.AreEqual(13, drive.Firmware.Songs.Count);
        }

        [UnityTest]
        public IEnumerator WheelSteeringFollowsFingerAndReturns()
        {
            drive.TouchSteer = 0.5f;
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.AreEqual(0.5f, drive.Model.Steer, 0.05f, "direksiyon parmağın açısını izlemeli");
            drive.TouchSteer = -1f;
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.AreEqual(-1f, drive.Model.Steer, 0.05f);
            drive.TouchSteer = null;
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.AreEqual(0f, drive.Model.Steer, 0.02f, "bırakınca ortaya dönmeli");
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
            if (Application.platform == RuntimePlatform.WindowsEditor)
                StringAssert.EndsWith(".mp4", saved, "Windows'ta MP4 (Media Foundation) yazılmalı");
            if (!string.IsNullOrEmpty(shots)) File.Copy(saved, Path.Combine(shots, "deneme_video" + Path.GetExtension(saved)), true);
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
                drive.Settings.RearMirror = true;
                drive.Settings.Cockpit = true;
                drive.TouchForward = 1;
                drive.TouchTurn = side == WheelSide.Left ? 1 : 0;
                yield return new WaitForSecondsRealtime(2.2f);
                yield return Shot(Path.Combine(dir, "kokpit_" + side.ToString().ToLowerInvariant() + ".png"));
                drive.TouchForward = drive.TouchTurn = 0;
            }
            // dokunmatik: yön tuşu (direksiyonun karşısında), sonra pedal + manuel vites kulakçıkları
            drive.Settings.Wheel = WheelSide.Left;
            drive.Settings.Touch = TouchMode.Pad;
            drive.TouchForward = 1;
            drive.TouchTurn = 1;
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot(Path.Combine(dir, "dokunmatik_yon.png"));
            drive.Settings.Touch = TouchMode.Pedals;
            drive.Settings.Gearbox = GearMode.Manual;
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot(Path.Combine(dir, "dokunmatik_pedal.png"));
            // dokunmatik direksiyon: simit çevrilmiş, teypte şarkı çalıyor, şaside teker güçleri
            drive.Settings.Touch = TouchMode.Wheel;
            drive.Settings.Gearbox = GearMode.Automatic;
            drive.TouchTurn = 0;
            yield return null;
            yield return null;
            drive.TouchSteer = 0.7f;
            drive.PlayMelody(9);
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Shot(Path.Combine(dir, "dokunmatik_direksiyon.png"));
            drive.TouchSteer = null;
            drive.TouchForward = 0;
            drive.TouchTurn = -1;   // yerinde dönüş: bir teker ileri, öbürü geri
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Shot(Path.Combine(dir, "yerinde_donus.png"));
            drive.StopMelody();
            drive.TouchForward = drive.TouchTurn = 0;
            drive.Settings.Touch = TouchMode.Off;
            drive.Settings.Gearbox = GearMode.Automatic;
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
            drive.MusicMenuOpen = false;
            drive.HornMenuOpen = true;
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot(Path.Combine(dir, "korna.png"));
            drive.HornMenuOpen = false;
            drive.RobotPanelOpen = true;
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot(Path.Combine(dir, "robot_paneli.png"));
            drive.RobotPanelOpen = false;
            string photo = null;
            drive.Recorder.Saved += p => photo = p;
            drive.Photo();
            yield return new WaitForSecondsRealtime(0.6f);
            drive.RecordsOpen = true;
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot(Path.Combine(dir, "kayitlar.png"));
            drive.RecordsOpen = false;
            if (photo != null) File.Delete(photo);
            // telefon düzeni (büyük dokunma alanları, iki sütunlu ayarlar)
            DriveCockpit.ForceTouch = true;
            drive.SettingsOpen = true;
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot(Path.Combine(dir, "ayarlar_telefon.png"));
            drive.SettingsOpen = false;
            drive.RobotPanelOpen = true;
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot(Path.Combine(dir, "robot_paneli_telefon.png"));
            drive.RobotPanelOpen = false;
            DriveCockpit.ForceTouch = null;
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
