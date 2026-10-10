using System;
using System.Collections;
using System.Collections.Generic;
using ARaki.Robot;
using UnityEngine;

namespace ARaki.Game
{
    /// <summary>
    /// Görev mantığı: ORBİT-9 istasyonunu sektör sektör tara, isteğe bağlı olarak enerji hücresinden şarj ol,
    /// sonra ana üsse dön. Robot bir sektörde, istasyonun halkası içinde durdukça o sektörün taraması dolar.
    /// Ana üs, zorunlu hedefler bitmeden kilitlidir.
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
        public event Action<MissionPoint, int> SectorCompleted;
        public event Action MissionStarted;

        public IReadOnlyList<MissionPoint> Points => points;
        public IReadOnlyList<Threat> Threats => threats;
        public bool Complete { get; private set; }
        public int Level { get; private set; } = 1;

        /// <summary>Kapalıyken tarama ilerlemez (menü, duraklatma, sonuç ekranı).</summary>
        public bool Running { get; set; } = true;

        /// <summary>Yükseltmelerle kısalan tarama süresi çarpanı.</summary>
        public float ScanTimeMultiplier { get; set; } = 1f;

        bool toldAboutLed, toldAboutScare, toldReturn;
        Coroutine intro;

        void Start()
        {
            foreach (var t in threats) t.StateChanged += OnThreatChanged;
            foreach (var p in points) p.RefreshVisual();
        }

        void OnDestroy()
        {
            foreach (var t in threats) if (t != null) t.StateChanged -= OnThreatChanged;
        }

        /// <summary>Yeni görev: hedefleri sıfırlar ve bölümün açılış hikayesini başlatır.</summary>
        public void Begin(int level)
        {
            Level = level;
            Complete = false;
            toldAboutLed = toldAboutScare = toldReturn = false;
            foreach (var p in points) if (p.gameObject.activeSelf) p.ResetState();
            Running = true;
            if (intro != null) StopCoroutine(intro);
            intro = StartCoroutine(Intro(Story.For(level)));
            MissionStarted?.Invoke();
        }

        IEnumerator Intro(Chapter chapter)
        {
            yield return new WaitForSeconds(0.8f);
            Notice?.Invoke(chapter.Title);
            for (int i = 0; i < chapter.Lines.Length; i++)
            {
                if (i > 0) yield return new WaitForSeconds(6.5f);
                Comms?.Invoke(chapter.Lines[i]);
            }
        }

        void Update()
        {
            if (Complete || !Running) return;
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            bool required = true;
            foreach (var p in points)
                if (p.gameObject.activeSelf && p.Kind != PointKind.Base && !p.Optional && !p.Done) required = false;

            foreach (var p in points)
            {
                if (!p.gameObject.activeSelf) continue;
                bool wasLocked = p.Locked;
                p.Locked = p.Kind == PointKind.Base && !required;
                if (p.Done) continue;

                TickPoint(p, dt);
                if (wasLocked != p.Locked) p.RefreshVisual();
            }

            if (!Complete && AllDone())
            {
                Complete = true;
                Notice?.Invoke("GÖREV TAMAMLANDI");
                Comms?.Invoke("Harika iş. Keşif raporu aktarılıyor.");
            }
        }

        void TickPoint(MissionPoint p, float dt)
        {
            float dist = FlatDistance(robot.position, p.transform.position);
            bool inRing = !p.Locked && dist < p.Radius && dist >= p.MinRadius;
            int sector = p.SectorOf(robot.position);
            p.ActiveSector = inRing ? sector : -1;

            float scanSeconds = Mathf.Max(0.2f, p.ScanSeconds * ScanTimeMultiplier);
            float before = p.Progress;
            float sum = 0f;
            bool anyNewSector = false;

            for (int s = 0; s < p.SectorCount; s++)
            {
                if (!p.SectorDone[s])
                {
                    if (inRing && s == sector) p.SectorProgress[s] = Mathf.Min(1f, p.SectorProgress[s] + dt / scanSeconds);
                    else p.SectorProgress[s] = Mathf.Max(0f, p.SectorProgress[s] - dt * 0.45f);

                    if (p.SectorProgress[s] >= 1f)
                    {
                        p.SectorDone[s] = true;
                        anyNewSector = true;
                        OnSectorDone(p, s);
                    }
                }
                sum += p.SectorDone[s] ? 1f : p.SectorProgress[s];
            }
            p.Progress = sum / p.SectorCount;

            if (p.CompletedSectors >= p.SectorCount)
            {
                p.Done = true;
                p.Progress = 1f;
                OnPointDone(p);
            }

            if (p.Done || anyNewSector || (before > 0.01f) != (p.Progress > 0.01f)) p.RefreshVisual();
            else p.RefreshIntensity();
        }

        void OnSectorDone(MissionPoint p, int sector)
        {
            SectorCompleted?.Invoke(p, sector);
            if (p.Kind != PointKind.Station || p.SectorCount <= 1) return;
            int done = p.CompletedSectors;
            if (done < p.SectorCount)
            {
                Notice?.Invoke("SEKTÖR " + done + "/" + p.SectorCount + "  TARANDI");
                Comms?.Invoke("Veri parçası alındı. İstasyonun etrafında sıradaki yöne geç.");
            }
        }

        void OnPointDone(MissionPoint p)
        {
            PointCompleted?.Invoke(p);
            switch (p.Kind)
            {
                case PointKind.Station:
                    Notice?.Invoke(Story.StationName + "  ÇEVRİMİÇİ");
                    Comms?.Invoke("İstasyon enerjisi bağlandı. Şimdi ana üsse dön.");
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

            if (!toldReturn && p.Kind != PointKind.Base && RequiredDone())
            {
                toldReturn = true;
                Comms?.Invoke("Tüm hedefler tamam. Ana üsse dön.");
            }
        }

        void OnThreatChanged(Threat t, ThreatState state)
        {
            if (!Running) return;
            if (state == ThreatState.Alert && !toldAboutLed)
            {
                toldAboutLed = true;
                Comms?.Invoke("Bir sonda seni fark etti. LED'i tut: ışıktan kaçar, ama yakıt harcar.");
            }
            else if (state == ThreatState.Scared && !toldAboutScare)
            {
                toldAboutScare = true;
                Comms?.Invoke("İşe yaradı. Işığı kesmeden önce yakıt seviyene bak.");
            }
        }

        bool RequiredDone()
        {
            foreach (var p in points)
                if (p.gameObject.activeSelf && p.Kind != PointKind.Base && !p.Optional && !p.Done) return false;
            return true;
        }

        bool AllDone()
        {
            bool any = false;
            foreach (var p in points)
            {
                if (!p.gameObject.activeSelf || p.Optional) continue;
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
