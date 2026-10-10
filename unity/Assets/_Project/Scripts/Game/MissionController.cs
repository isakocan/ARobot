using System;
using System.Collections;
using System.Collections.Generic;
using ARaki.Robot;
using UnityEngine;

namespace ARaki.Game
{
    /// <summary>
    /// Görev mantığı: hedefleri tara, yakıt istasyonunda şarj ol, ana üsse dön.
    /// Robot bir hedefin yarıçapında durdukça tarama dolar. Ana üs, diğerleri bitmeden kilitlidir.
    /// Havuzdaki hedeflerden yalnızca aktif olanlar görevde sayılır (görev üreticisi hangilerinin kullanılacağını seçer).
    /// Olaylar iki kanala gider: Notice (ekranın üstündeki kısa başlık) ve Comms (merkezden gelen telsiz mesajı).
    /// </summary>
    public class MissionController : MonoBehaviour
    {
        [SerializeField] Transform robot;
        [SerializeField] MockRobotLink link;
        [SerializeField] MissionPoint[] points;
        [SerializeField] Threat[] threats;
        [SerializeField] float refuelAmount = 0.4f;

        public event Action<string> Notice;
        public event Action<string> Comms;
        public event Action<MissionPoint> PointCompleted;
        public event Action MissionStarted;

        public IReadOnlyList<MissionPoint> Points => points;
        public IReadOnlyList<Threat> Threats => threats;
        public bool Complete { get; private set; }

        /// <summary>Kapalıyken tarama ilerlemez (menü, duraklatma, sonuç ekranı).</summary>
        public bool Running { get; set; } = true;

        /// <summary>Yükseltmelerle kısalan tarama süresi çarpanı.</summary>
        public float ScanTimeMultiplier { get; set; } = 1f;

        bool toldAboutLed, toldAboutScare, toldReturn;
        Coroutine intro;

        void Start()
        {
            foreach (var t in threats) t.StateChanged += OnThreatChanged;
            foreach (var p in points) { p.RefreshVisual(); }
        }

        void OnDestroy()
        {
            foreach (var t in threats) if (t != null) t.StateChanged -= OnThreatChanged;
        }

        /// <summary>Yeni görev: hedefleri sıfırlar ve açılış telsiz mesajlarını başlatır.</summary>
        public void Begin(string sectorTitle = null)
        {
            Complete = false;
            toldAboutLed = toldAboutScare = toldReturn = false;
            foreach (var p in points) if (p.gameObject.activeSelf) p.ResetState();
            Running = true;
            if (intro != null) StopCoroutine(intro);
            intro = StartCoroutine(Intro(sectorTitle));
            MissionStarted?.Invoke();
        }

        IEnumerator Intro(string sectorTitle)
        {
            yield return new WaitForSeconds(0.8f);
            Notice?.Invoke(string.IsNullOrEmpty(sectorTitle) ? "KEŞİF BAŞLADI" : sectorTitle + "  ·  KEŞİF BAŞLADI");
            Comms?.Invoke("Bağlantı kuruldu. Hedefler işaretlendi: yaklaş, dur ve tara.");
            yield return new WaitForSeconds(6f);
            if (!toldAboutLed) Comms?.Invoke("Devriye sondalarına dikkat. Yakıtı izle.");
        }

        void Update()
        {
            if (Complete || !Running) return;
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            bool othersDone = true;
            foreach (var p in points)
                if (p.gameObject.activeSelf && p.Kind != PointKind.Base && !p.Done) othersDone = false;

            foreach (var p in points)
            {
                if (!p.gameObject.activeSelf) continue;
                bool wasLocked = p.Locked;
                p.Locked = p.Kind == PointKind.Base && !othersDone;
                if (p.Done) continue;

                float dist = FlatDistance(robot.position, p.transform.position);
                bool inside = !p.Locked && dist < p.Radius;
                float before = p.Progress;
                float scanSeconds = Mathf.Max(0.2f, p.ScanSeconds * ScanTimeMultiplier);
                p.Progress = inside
                    ? Mathf.Min(1f, p.Progress + dt / scanSeconds)
                    : Mathf.Max(0f, p.Progress - dt * 0.6f);

                if (p.Progress >= 1f)
                {
                    p.Done = true;
                    OnPointDone(p);
                }
                if (wasLocked != p.Locked || p.Done || (before > 0.01f) != (p.Progress > 0.01f)) p.RefreshVisual();
                else p.RefreshIntensity();
            }

            if (!Complete && AllDone())
            {
                Complete = true;
                Notice?.Invoke("GÖREV TAMAMLANDI");
                Comms?.Invoke("Harika iş. Keşif raporu aktarılıyor.");
            }
        }

        void OnPointDone(MissionPoint p)
        {
            PointCompleted?.Invoke(p);
            switch (p.Kind)
            {
                case PointKind.Tower:
                    Notice?.Invoke(p.Label + "  TARANDI");
                    Comms?.Invoke("Veri alındı: " + p.Label.ToLowerInvariant() + ".");
                    break;
                case PointKind.Fuel:
                    link.AddBattery(refuelAmount);
                    Notice?.Invoke("YAKIT ALINDI  +" + Mathf.RoundToInt(refuelAmount * 100f) + "%");
                    Comms?.Invoke("Şarj tamam.");
                    break;
                case PointKind.Base:
                    Notice?.Invoke("ÜSSE DÖNÜLDÜ");
                    break;
            }

            if (!toldReturn && p.Kind != PointKind.Base && OthersDone())
            {
                toldReturn = true;
                Comms?.Invoke("Tüm noktalar tarandı. Ana üsse dön.");
            }
        }

        void OnThreatChanged(Threat t, ThreatState state)
        {
            if (!Running) return;
            if (state == ThreatState.Alert && !toldAboutLed)
            {
                toldAboutLed = true;
                Comms?.Invoke("Devriye sondası seni fark etti. LED'i tut: ışıktan kaçar, ama yakıt harcar.");
            }
            else if (state == ThreatState.Scared && !toldAboutScare)
            {
                toldAboutScare = true;
                Comms?.Invoke("İşe yaradı. Işığı kesmeden önce yakıt seviyene bak.");
            }
        }

        bool OthersDone()
        {
            foreach (var p in points) if (p.gameObject.activeSelf && p.Kind != PointKind.Base && !p.Done) return false;
            return true;
        }

        bool AllDone()
        {
            bool any = false;
            foreach (var p in points)
            {
                if (!p.gameObject.activeSelf) continue;
                any = true;
                if (!p.Done) return false;
            }
            return any;
        }

        /// <summary>Görevdeki (aktif) hedef sayısı.</summary>
        public int ActivePointCount
        {
            get { int n = 0; foreach (var p in points) if (p.gameObject.activeSelf) n++; return n; }
        }

        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
