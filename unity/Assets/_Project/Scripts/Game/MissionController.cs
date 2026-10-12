using System;
using System.Collections;
using System.Collections.Generic;
using ARaki.Robot;
using ARaki.Sensing;
using UnityEngine;

namespace ARaki.Game
{
    /// <summary>
    /// Görev mantığı (ASTRA-7 yüzey keşfi): robotun sensörü çevreyi haritalar; haritalanan alan, sinyal kaynağı (ORBİT-9),
    /// mineral numuneleri ve enerji hücresi ancak sensör menziline girince keşfedilir. Görev: yeterli alanı haritala,
    /// ORBİT-9'u sektör sektör tara, sonra ana üsse dön. Numuneler isteğe bağlıdır; üs hem görev bitişi hem de
    /// şarj, numune teslimi ve konum kalibrasyonu noktasıdır.
    /// Olaylar iki kanala gider: Notice (ekranın üstündeki kısa başlık) ve Comms (merkezden gelen telsiz mesajı).
    /// </summary>
    public class MissionController : MonoBehaviour
    {
        [SerializeField] Transform robot;
        [SerializeField] MockRobotLink link;
        [SerializeField] ExplorationMap map;
        [SerializeField] MissionPoint[] points;
        [SerializeField] Threat[] threats;
        [SerializeField] float refuelAmount = 0.4f;
        [SerializeField] float revealRadius = 1.9f;       // bir hedefin sinyalini alma mesafesi (görüş hattıyla)
        [SerializeField] float touchRevealRadius = 1.0f;  // bu kadar yakında görüş hattı aranmaz
        [SerializeField] float depotRadius = 0.6f;
        [SerializeField] float depotChargePerSecond = 0.05f;

        public event Action<string> Notice;
        public event Action<string> Comms;
        public event Action<MissionPoint> PointCompleted;
        public event Action<MissionPoint> PointRevealed;
        public event Action<MissionPoint, int> SectorCompleted;
        public event Action SurveyCompleted;
        public event Action<int> SamplesDelivered;
        public event Action Calibrated;
        public event Action MissionStarted;

        public IReadOnlyList<MissionPoint> Points => points;
        public IReadOnlyList<Threat> Threats => threats;
        public ExplorationMap Map => map;
        public bool Complete { get; private set; }
        public int Level { get; private set; } = 1;

        /// <summary>Kapalıyken tarama ilerlemez (menü, duraklatma, sonuç ekranı).</summary>
        public bool Running { get; set; } = true;

        /// <summary>Yükseltmelerle kısalan tarama süresi çarpanı.</summary>
        public float ScanTimeMultiplier { get; set; } = 1f;

        /// <summary>Açıkken hedefler sensör bulana kadar görünmez; kapalıyken hepsi baştan bilinir.</summary>
        public bool FogOfWar { get; set; } = true;

        /// <summary>Görevin gerektirdiği haritalama alanı (m²). 0 = haritalama gerekmez.</summary>
        public float SurveyTargetArea { get; set; }
        public bool SurveyDone { get; private set; }
        public float SurveyProgress01 => SurveyTargetArea <= 0f ? 1f : Mathf.Clamp01((map != null ? map.ExploredArea : 0f) / SurveyTargetArea);

        public int SamplesCollected { get; private set; }
        public int SamplesDeliveredCount { get; private set; }
        public int SamplesCarried => SamplesCollected - SamplesDeliveredCount;
        public int SamplesTotal
        {
            get { int n = 0; foreach (var p in points) if (p.Kind == PointKind.Sample && p.gameObject.activeSelf) n++; return n; }
        }

        bool toldAboutLed, toldAboutScare, toldReturn;
        bool wasInBase;
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
            SurveyDone = SurveyTargetArea <= 0f;
            SamplesCollected = SamplesDeliveredCount = 0;
            toldAboutLed = toldAboutScare = toldReturn = false;
            foreach (var p in points)
            {
                if (!p.gameObject.activeSelf) continue;
                p.SetRevealed(!FogOfWar || p.Kind == PointKind.Base);
                p.ResetState();
            }
            var basePoint = Find(PointKind.Base);
            wasInBase = basePoint != null && FlatDistance(robot.position, basePoint.transform.position) < depotRadius;
            Running = true;
            if (intro != null) StopCoroutine(intro);
            intro = StartCoroutine(Intro(Story.For(level), Story.Hint(basePoint != null ? basePoint.transform.position : Vector3.zero,
                Find(PointKind.Station) != null ? Find(PointKind.Station).transform.position : Vector3.zero)));
            MissionStarted?.Invoke();
        }

        /// <summary>Menü arka planı ve testler için: her hedefi keşfedilmiş sayar.</summary>
        public void RevealAll()
        {
            foreach (var p in points)
                if (p.gameObject.activeSelf) p.SetRevealed(true);
        }

        MissionPoint Find(PointKind kind)
        {
            foreach (var p in points) if (p.Kind == kind && p.gameObject.activeSelf) return p;
            return null;
        }

        IEnumerator Intro(Chapter chapter, string hint)
        {
            yield return new WaitForSeconds(0.8f);
            Notice?.Invoke(chapter.Title);
            for (int i = 0; i < chapter.Lines.Length; i++)
            {
                if (i > 0) yield return new WaitForSeconds(6.5f);
                Comms?.Invoke(chapter.Lines[i]);
            }
            if (!string.IsNullOrEmpty(hint))
            {
                yield return new WaitForSeconds(6.5f);
                Comms?.Invoke(hint);
            }
        }

        void Update()
        {
            if (Complete || !Running) return;
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            UpdateReveals();
            UpdateSurvey();
            UpdateDepot(dt);

            bool required = RequiredDone();

            foreach (var p in points)
            {
                if (!p.gameObject.activeSelf) continue;
                bool wasLocked = p.Locked;
                p.Locked = p.Kind == PointKind.Base && !required;
                if (p.Done || !p.Revealed) continue;

                TickPoint(p, dt);
                if (wasLocked != p.Locked) p.RefreshVisual();
            }

            if (!Complete && AllDone())
            {
                Complete = true;
                DeliverSamples();
                Notice?.Invoke("GÖREV TAMAMLANDI");
                Comms?.Invoke("Harika iş. Keşif raporu aktarılıyor.");
            }
        }

        // ------------------------------------------------------------------ keşif

        void UpdateReveals()
        {
            foreach (var p in points)
            {
                if (!p.gameObject.activeSelf || p.Revealed) continue;
                float dist = FlatDistance(robot.position, p.transform.position);
                bool seen = dist < touchRevealRadius ||
                            (dist < revealRadius && (map == null || map.LineOfSight(robot.position, p.transform.position)));
                if (!seen) continue;
                p.SetRevealed(true);
                p.RefreshVisual();
                OnRevealed(p);
            }
        }

        void OnRevealed(MissionPoint p)
        {
            PointRevealed?.Invoke(p);
            switch (p.Kind)
            {
                case PointKind.Station:
                    Notice?.Invoke("SİNYAL KAYNAĞI BULUNDU");
                    Comms?.Invoke(Story.StationName + " bu! Etrafında dolaşıp sektör sektör tara.");
                    break;
                case PointKind.Sample:
                    Notice?.Invoke("MİNERAL NUMUNESİ TESPİT EDİLDİ");
                    break;
                case PointKind.Fuel:
                    Notice?.Invoke("ENERJİ HÜCRESİ BULUNDU");
                    break;
            }
        }

        void UpdateSurvey()
        {
            if (SurveyDone || map == null) return;
            if (map.ExploredArea >= SurveyTargetArea)
            {
                SurveyDone = true;
                SurveyCompleted?.Invoke();
                Notice?.Invoke("HARİTALAMA TAMAM");
                Comms?.Invoke("Yüzey haritası yeterli. Şimdi sinyal kaynağına odaklan.");
            }
        }

        // ------------------------------------------------------------------ üs: şarj, teslim, kalibrasyon

        void UpdateDepot(float dt)
        {
            var b = Find(PointKind.Base);
            if (b == null) return;
            bool inBase = FlatDistance(robot.position, b.transform.position) < depotRadius;
            if (inBase)
            {
                if (link.Telemetry.Battery01 < 1f) link.AddBattery(depotChargePerSecond * dt);
                if (!wasInBase)
                {
                    Calibrated?.Invoke();
                    if (SamplesCarried > 0) DeliverSamples();
                    else if (link.Telemetry.Battery01 < 0.95f) Notice?.Invoke("ÜS  ·  ŞARJ VE KALİBRASYON");
                }
            }
            wasInBase = inBase;
        }

        void DeliverSamples()
        {
            int carried = SamplesCarried;
            if (carried <= 0) return;
            SamplesDeliveredCount += carried;
            SamplesDelivered?.Invoke(carried);
            Notice?.Invoke("NUMUNE TESLİM EDİLDİ  ×" + carried);
            Comms?.Invoke("Numuneler laboratuvara ulaştı. Üssün sensörleri yeniden kalibre edildi.");
        }

        // ------------------------------------------------------------------ tarama

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
                    Comms?.Invoke(RequiredDone() ? "İstasyon enerjisi bağlandı. Ana üsse dön." : "İstasyon enerjisi bağlandı. Haritalamayı tamamla, sonra ana üsse dön.");
                    break;
                case PointKind.Fuel:
                    link.AddBattery(refuelAmount);
                    Notice?.Invoke("YAKIT ALINDI  +" + Mathf.RoundToInt(refuelAmount * 100f) + "%");
                    Comms?.Invoke("Şarj tamam.");
                    break;
                case PointKind.Sample:
                    SamplesCollected++;
                    Notice?.Invoke("NUMUNE ALINDI  " + SamplesCollected + "/" + SamplesTotal);
                    Comms?.Invoke(SamplesCollected == 1 ? "İlk numune bende. Üsse teslim edince puan ve şarj kazanırız." : "Numune alındı.");
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
                Comms?.Invoke("Bir bekçi seni fark etti. LED'i tut: ışıktan kaçar, ama yakıt harcar.");
            }
            else if (state == ThreatState.Scared && !toldAboutScare)
            {
                toldAboutScare = true;
                Comms?.Invoke("İşe yaradı. Işığı kesmeden önce yakıt seviyene bak.");
            }
        }

        /// <summary>Zorunlu hedefler (istasyon taraması ve haritalama) bitti mi? Üs ancak bundan sonra açılır.</summary>
        bool RequiredDone()
        {
            if (!SurveyDone) return false;
            foreach (var p in points)
                if (p.gameObject.activeSelf && p.Kind != PointKind.Base && !p.Optional && !p.Done) return false;
            return true;
        }

        bool AllDone()
        {
            if (!SurveyDone) return false;
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
