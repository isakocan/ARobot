using System;
using System.Collections.Generic;
using ARaki.Robot;
using ARaki.Video;
using UnityEngine;
using UnityEngine.Rendering;

namespace ARaki.Game
{
    public enum HazardPhase { Calm, Warning, Storm }

    /// <summary>
    /// Zorlu koşullar: ara sıra pencereden gelen güneş parlamaları. Önce kısa bir uyarı gelir ve robotun
    /// yakınındaki gölgeli yerler (mobilyaların güneşe karşı kapattığı yerler) AR'da sığınak olarak işaretlenir.
    /// Parlama başlayınca açıkta kalan robot yakıt kaybeder, sinyali zayıflar, radarı bozulur ve kamera görüntüsü
    /// parazitlenir. Gölgede duran robot büyük ölçüde korunur.
    /// </summary>
    public class HazardController : MonoBehaviour
    {
        [SerializeField] Transform robot;
        [SerializeField] MockRobotLink link;
        [SerializeField] PanoramaViewer viewer;
        [SerializeField] Volume stormVolume;
        [SerializeField] Transform[] shelterProxies;        // AR sığınak işaretleri (havuz)
        [SerializeField] Vector3 flareSource = new Vector3(2.85f, 1.5f, 0.2f);   // pencere
        [SerializeField] float warningSeconds = 5f;
        [SerializeField] float drainPerSecond = 0.014f;
        [SerializeField] Vector2 areaMin = new Vector2(-2.5f, -2.0f);
        [SerializeField] Vector2 areaMax = new Vector2(2.5f, 2.0f);

        public event Action<string> Notice;
        public event Action<string> Comms;
        public event Action<HazardPhase> PhaseChanged;

        public bool Active { get; set; }
        public HazardPhase Phase { get; private set; } = HazardPhase.Calm;
        public float Intensity { get; private set; }            // 0..1, yumuşatılmış
        public bool Exposed { get; private set; }
        public float SecondsToStorm { get; private set; }
        public float StormSecondsLeft { get; private set; }
        public IReadOnlyList<Vector3> ShelterSpots => shelterSpots;
        public IReadOnlyList<Transform> ShelterProxies => shelterProxies;

        /// <summary>Sinyal kalitesi 0..1: parlama sırasında açıkta kalınca çöker.</summary>
        public float Signal => 1f - Intensity * (Exposed ? 0.9f : 0.3f);

        readonly List<Vector3> shelterSpots = new List<Vector3>();
        float interval = 40f, duration = 7f, countdown;
        bool toldFirst;

        public void Begin(MissionSpec spec)
        {
            interval = spec.FlareInterval;
            duration = spec.FlareDuration;
            countdown = interval * 0.85f;
            toldFirst = false;
            SetPhase(HazardPhase.Calm);
            StormSecondsLeft = 0f;
            Intensity = 0f;
            HideShelters();
            Active = true;
        }

        /// <summary>Parlamaları tamamen kapatır (yarış modu, menü).</summary>
        public void Stop()
        {
            Active = false;
            if (Phase != HazardPhase.Calm) SetPhase(HazardPhase.Calm);
            StormSecondsLeft = 0f;
            HideShelters();
        }

        void Update() => Tick(Time.deltaTime);

        public void Tick(float dt)
        {
            Exposed = ComputeExposed(robot.position);

            if (Active)
            {
                switch (Phase)
                {
                    case HazardPhase.Calm:
                        countdown -= dt;
                        SecondsToStorm = countdown;
                        if (countdown <= warningSeconds) StartWarning();
                        break;

                    case HazardPhase.Warning:
                        countdown -= dt;
                        SecondsToStorm = Mathf.Max(0f, countdown);
                        if (countdown <= 0f) StartStorm();
                        break;

                    case HazardPhase.Storm:
                        StormSecondsLeft -= dt;
                        if (Exposed) link.AddBattery(-drainPerSecond * dt);
                        if (StormSecondsLeft <= 0f) EndStorm();
                        break;
                }
            }

            float target = Phase == HazardPhase.Storm ? 1f : Phase == HazardPhase.Warning ? 0.2f : 0f;
            Intensity = Mathf.MoveTowards(Intensity, target, dt * 1.4f);

            if (stormVolume != null) stormVolume.weight = Intensity * (Exposed ? 1f : 0.45f);
            if (viewer != null) viewer.ExtraShake = Intensity * (Exposed ? 0.9f : 0.25f);
        }

        /// <summary>Testler ve ekran görüntüleri için: aşamayı doğrudan başlatır.</summary>
        public void ForcePhase(HazardPhase phase)
        {
            Active = true;
            if (phase == HazardPhase.Warning) { StartWarning(); countdown = 3.2f; }
            else if (phase == HazardPhase.Storm) { StartWarning(); StartStorm(); }
            else EndStorm();
        }

        void StartWarning()
        {
            SetPhase(HazardPhase.Warning);
            ComputeShelters();
            Notice?.Invoke("GÜNEŞ PARLAMASI YAKLAŞIYOR");
            Comms?.Invoke(toldFirst
                ? "Parlama geliyor! En yakın sığınağa geç."
                : "Parlama uyarısı! Pencereden güçlü radyasyon geliyor. Mobilyanın gölgesindeki işaretli sığınağa gir.");
            toldFirst = true;
        }

        void StartStorm()
        {
            SetPhase(HazardPhase.Storm);
            StormSecondsLeft = duration;
            Notice?.Invoke("PARLAMA BAŞLADI");
        }

        void EndStorm()
        {
            SetPhase(HazardPhase.Calm);
            countdown = interval;
            HideShelters();
            Notice?.Invoke("PARLAMA GEÇTİ");
            Comms?.Invoke("Parlama geçti. Sistemler toparlanıyor.");
        }

        void SetPhase(HazardPhase phase)
        {
            if (Phase == phase) return;
            Phase = phase;
            PhaseChanged?.Invoke(phase);
        }

        /// <summary>Robot, pencere ile arasına giren bir şey yoksa parlamaya açıktadır.</summary>
        public bool ComputeExposed(Vector3 position)
        {
            var from = new Vector3(position.x, 0.15f, position.z);
            return !Physics.Linecast(from, flareSource, out _, ~0, QueryTriggerInteraction.Ignore);
        }

        void ComputeShelters()
        {
            shelterSpots.Clear();
            var candidates = new List<Vector3>();
            for (float x = areaMin.x; x <= areaMax.x; x += 0.25f)
                for (float z = areaMin.y; z <= areaMax.y; z += 0.25f)
                {
                    var p = new Vector3(x, 0f, z);
                    if (Physics.CheckSphere(new Vector3(x, 0.15f, z), 0.28f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (ComputeExposed(p)) continue;
                    candidates.Add(p);
                }

            candidates.Sort((a, b) => MissionController.FlatDistance(a, robot.position).CompareTo(MissionController.FlatDistance(b, robot.position)));
            foreach (var c in candidates)
            {
                bool far = true;
                foreach (var s in shelterSpots) if (Vector3.Distance(s, c) < 0.9f) far = false;
                if (far) shelterSpots.Add(c);
                if (shelterSpots.Count >= (shelterProxies != null ? shelterProxies.Length : 2)) break;
            }

            if (shelterProxies == null) return;
            for (int i = 0; i < shelterProxies.Length; i++)
            {
                bool on = i < shelterSpots.Count;
                shelterProxies[i].gameObject.SetActive(on);
                if (on) shelterProxies[i].position = shelterSpots[i];
            }
        }

        void HideShelters()
        {
            shelterSpots.Clear();
            if (shelterProxies == null) return;
            foreach (var t in shelterProxies) if (t != null) t.gameObject.SetActive(false);
        }
    }
}
