using System;
using System.Collections;
using System.Collections.Generic;
using ARaki.Robot;
using UnityEngine;

namespace ARaki.Game
{
    /// <summary>
    /// Demo görevi: kuleleri tara, yakıt istasyonunda şarj ol, ana üsse dön.
    /// Robot bir hedefin yarıçapında durdukça tarama dolar. Ana üs, diğerleri bitmeden kilitlidir.
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

        public IReadOnlyList<MissionPoint> Points => points;
        public IReadOnlyList<Threat> Threats => threats;
        public bool Complete { get; private set; }

        bool toldAboutLed, toldAboutScare, toldReturn;

        void Start()
        {
            foreach (var p in points) p.RefreshVisual();
            foreach (var t in threats) t.StateChanged += OnThreatChanged;
            StartCoroutine(Intro());
        }

        void OnDestroy()
        {
            foreach (var t in threats) if (t != null) t.StateChanged -= OnThreatChanged;
        }

        IEnumerator Intro()
        {
            yield return new WaitForSeconds(1.2f);
            Notice?.Invoke("KEŞİF BAŞLADI");
            Comms?.Invoke("Bağlantı kuruldu. Hane sektörü taramaya hazır.");
            yield return new WaitForSeconds(6f);
            Comms?.Invoke("İki kule ve bir yakıt noktası işaretlendi. Yaklaş, dur ve tara.");
        }

        void Update()
        {
            if (Complete) return;
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            bool othersDone = true;
            foreach (var p in points)
                if (p.Kind != PointKind.Base && !p.Done) othersDone = false;

            foreach (var p in points)
            {
                bool wasLocked = p.Locked;
                p.Locked = p.Kind == PointKind.Base && !othersDone;
                if (p.Done) continue;

                float dist = FlatDistance(robot.position, p.transform.position);
                bool inside = !p.Locked && dist < p.Radius;
                float before = p.Progress;
                p.Progress = inside
                    ? Mathf.Min(1f, p.Progress + dt / p.ScanSeconds)
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
            foreach (var p in points) if (p.Kind != PointKind.Base && !p.Done) return false;
            return true;
        }

        bool AllDone()
        {
            foreach (var p in points) if (!p.Done) return false;
            return true;
        }

        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
