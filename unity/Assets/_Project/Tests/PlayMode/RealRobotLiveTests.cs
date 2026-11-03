using System.Collections;
using System.IO;
using ARaki.Drive;
using ARaki.Robot;
using ARaki.Video;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ARaki.Tests
{
    /// <summary>
    /// Gerçek robotla (ARAKI_CANLI=1 verilirse): robotu ağda bulur, UDP'den durum alır, 360° görüntü gelir.
    /// Motorlara hiç komut vermez (sürüş sıfır). ARAKI_SHOT verilirse ekran görüntüsü kaydeder.
    /// </summary>
    public class RealRobotLiveTests
    {
        [UnityTest]
        public IEnumerator FindsRobotAndReceivesVideo()
        {
            if (System.Environment.GetEnvironmentVariable("ARAKI_CANLI") != "1") Assert.Ignore("canlı robot testi kapalı");
            PlayerPrefs.SetInt(RealRobotLink.DemoKey, 0);
            yield return SceneManager.LoadSceneAsync("RealRobot");
            var link = Object.FindFirstObjectByType<RealRobotLink>();
            var video = Object.FindFirstObjectByType<RobotVideo>();
            var drive = Object.FindFirstObjectByType<DriveController>();
            link.FindRobot();
            float t = 0f;
            while (!link.IsConnected && t < 40f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.IsTrue(link.IsConnected, "robot bulunamadı: " + link.SearchText);
            Debug.Log($"[Canlı] adres {link.Host}, {t:0.0} sn'de bulundu");
            t = 0f;
            while (!video.HasFrame && t < 20f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.IsTrue(video.HasFrame, "görüntü gelmedi");
            yield return new WaitForSecondsRealtime(4f);
            var s = link.Status;
            Debug.Log($"[Canlı] gidip gelme {link.RoundTripMs:0} ms, kayıp %{link.Loss * 100f:0}, sinyal {s.sinyal} dBm, " +
                      $"görüntü {video.Fps:0} fps, veri {s.veri_kbps / 1000f:0.0} Mbit/s, arduino {s.arduino}");
            Assert.IsTrue(s.arduino);
            Assert.AreEqual(0, s.motor.sol);
            string dir = System.Environment.GetEnvironmentVariable("ARAKI_SHOT");
            if (!string.IsNullOrEmpty(dir))
            {
                drive.Settings.Wheel = WheelSide.Left;
                yield return new WaitForSecondsRealtime(0.5f);
                yield return new WaitForEndOfFrame();
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(dir, "canli.png"), tex.EncodeToPNG());
            }
        }
    }
}
