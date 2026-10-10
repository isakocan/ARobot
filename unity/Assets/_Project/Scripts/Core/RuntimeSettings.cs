using ARaki.Video;
using UnityEngine;

namespace ARaki.Core
{
    /// <summary>
    /// Çalışma zamanı performans ayarları ve basit bir "kalite bekçisi".
    /// - Telefonda 60 kareye sabitler, ekranı açık tutar, vsync'i kapatır (hedefi biz belirleriz).
    /// - Kare süresi uzun süre hedefin çok üstünde kalırsa 360° video kalitesini bir kademe düşürür
    ///   (yalnızca düşürür; sürekli iniş-çıkış titremesi olmasın diye geri çıkmaz).
    /// </summary>
    public class RuntimeSettings : MonoBehaviour
    {
        [SerializeField] Sim360Camera video;
        [SerializeField] PanoramaViewer viewer;
        [SerializeField] int targetFrameRate = 60;
        [SerializeField] float slowFactor = 1.35f;      // hedef kare süresinin bu katını aşarsa "yavaş" sayılır
        [SerializeField] float windowSeconds = 2f;
        [SerializeField] int slowWindowsToDowngrade = 3;
        [SerializeField] bool governOnDesktop;           // PC'de de kalite düşürsün (testler için)

        public float AverageFrameMs { get; private set; }
        public int Downgrades { get; private set; }

        float accumulated;
        int frames, slowWindows;

        void Awake()
        {
            if (Application.isMobilePlatform)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = targetFrameRate;
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
            }
            Time.fixedDeltaTime = 0.02f;
        }

        void Update()
        {
            accumulated += Time.unscaledDeltaTime;
            frames++;
            if (accumulated < windowSeconds) return;

            float avg = accumulated / frames;
            AverageFrameMs = avg * 1000f;
            accumulated = 0f;
            frames = 0;

            if (video == null || (!Application.isMobilePlatform && !governOnDesktop)) return;
            float budget = 1f / Mathf.Max(30, targetFrameRate);
            slowWindows = avg > budget * slowFactor ? slowWindows + 1 : 0;
            if (slowWindows >= slowWindowsToDowngrade && video.Quality != VideoQuality.Low)
            {
                slowWindows = 0;
                Downgrades++;
                video.SetQuality(video.Quality == VideoQuality.High ? VideoQuality.Medium : VideoQuality.Low);
                Debug.Log($"[Perf] kare süresi {avg * 1000f:0.0} ms: 360° video kalitesi {video.Quality} seviyesine indirildi");
            }
        }
    }
}
