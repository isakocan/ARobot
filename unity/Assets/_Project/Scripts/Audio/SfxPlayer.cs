using ARaki.Game;
using ARaki.Robot;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ARaki.Audio
{
    /// <summary>
    /// Oyun olaylarına ses bağlar. Bütün sesler kodla üretilen geçici efektlerdir (ses dosyası yok):
    /// hafif ortam hava akımı, motor, LED, tarama tıkırtısı, bildirim sesleri, alarm, siren, gümbürtü ve elektrik şoku.
    /// K tuşu sesi kapatıp açar.
    /// </summary>
    public class SfxPlayer : MonoBehaviour
    {
        [SerializeField] MockRobotLink link;
        [SerializeField] MissionController mission;
        [SerializeField] HazardController hazard;
        [SerializeField] RaceController race;
        [SerializeField, Range(0f, 1f)] float masterVolume = 0.6f;

        public bool Muted { get; private set; }

        AudioSource ambient, motor, ledHum, rumble, effects, scan, beeps;
        AudioClip chime, alarm, blip, ledOn, scare, tick, siren, zap;
        float scanTimer, alarmTimer, ledVolume, zapTimer, ambientLevel = 0.35f;
        bool ledWasOn;

        void Awake()
        {
            chime = AudioSynth.Chime();
            alarm = AudioSynth.Alarm();
            blip = AudioSynth.Blip();
            ledOn = AudioSynth.LedOn();
            scare = AudioSynth.Scare();
            tick = AudioSynth.ScanTick();
            siren = AudioSynth.Siren();
            zap = AudioSynth.Zap();

            ambient = Loop("Ambient", AudioSynth.AmbientSoft(), 0.35f);
            motor = Loop("Motor", AudioSynth.Motor(), 0f);
            ledHum = Loop("LedHum", AudioSynth.LedHum(), 0f);
            rumble = Loop("Rumble", AudioSynth.Rumble(), 0f);
            effects = Source("Effects");
            scan = Source("Scan");
            beeps = Source("Beeps");
        }

        void Start()
        {
            mission.Notice += OnNotice;
            mission.Comms += OnComms;
            foreach (var t in mission.Threats) t.StateChanged += OnThreatChanged;
            if (hazard != null) hazard.PhaseChanged += OnHazardPhase;
            if (race != null)
            {
                race.CountdownTick += OnCountdown;
                race.GateCrossed += OnGate;
                race.LapCompleted += OnLap;
                race.Finished += OnRaceFinished;
                race.WrongWay += OnWrongWay;
            }
        }

        void OnDestroy()
        {
            if (mission == null) return;
            mission.Notice -= OnNotice;
            mission.Comms -= OnComms;
            foreach (var t in mission.Threats) if (t != null) t.StateChanged -= OnThreatChanged;
            if (hazard != null) hazard.PhaseChanged -= OnHazardPhase;
            if (race != null)
            {
                race.CountdownTick -= OnCountdown;
                race.GateCrossed -= OnGate;
                race.LapCompleted -= OnLap;
                race.Finished -= OnRaceFinished;
                race.WrongWay -= OnWrongWay;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame) SetMuted(!Muted);

            var tel = link.Telemetry;

            // Yarışta uzay uğultusu kapanır; sadece motor ve olay sesleri kalır
            bool racing = race != null && race.Phase != RacePhase.Idle;
            ambientLevel = Mathf.MoveTowards(ambientLevel, racing ? 0.04f : 0.35f, dt * 0.5f);
            ambient.volume = ambientLevel * masterVolume;

            // Motor: tekerlek hızına göre perde ve ses
            float load = (Mathf.Abs(tel.LeftSpeed) + Mathf.Abs(tel.RightSpeed)) * 0.5f / link.MaxWheelSpeed;
            motor.volume = Mathf.MoveTowards(motor.volume, Mathf.Clamp01(load * 1.6f) * 0.22f * masterVolume, dt * 2f);
            motor.pitch = Mathf.Lerp(motor.pitch, 0.6f + load * 1.3f, dt * 6f);

            // LED
            if (link.LedOn && !ledWasOn) effects.PlayOneShot(ledOn, masterVolume);
            ledWasOn = link.LedOn;
            ledVolume = Mathf.MoveTowards(ledVolume, link.LedOn ? 0.1f * masterVolume : 0f, dt * 1.5f);
            ledHum.volume = ledVolume;

            // Güneş parlaması gümbürtüsü
            float storm = hazard != null ? hazard.Intensity * (hazard.Exposed ? 1f : 0.4f) : 0f;
            rumble.volume = Mathf.MoveTowards(rumble.volume, storm * 0.5f * masterVolume, dt * 1.5f);

            // Tarama tıkırtısı: ilerledikçe sıklaşır, tizleşir
            MissionPoint best = null;
            foreach (var p in mission.Points)
                if (p.gameObject.activeSelf && !p.Done && !p.Locked && p.ActiveSector >= 0 && (best == null || p.Progress > best.Progress)) best = p;
            if (best != null)
            {
                float sp = best.SectorProgress[best.ActiveSector];
                scanTimer -= dt;
                if (scanTimer <= 0f)
                {
                    scan.pitch = 0.9f + sp * 0.9f;
                    scan.PlayOneShot(tick, 0.5f * masterVolume);
                    scanTimer = Mathf.Lerp(0.35f, 0.08f, sp);
                }
            }
            else scanTimer = 0f;

            // Tehdit teması: şok sesi
            bool engaged = false;
            foreach (var t in mission.Threats) engaged |= t.gameObject.activeSelf && t.Engaged;
            if (engaged)
            {
                alarmTimer -= dt;
                zapTimer -= dt;
                if (zapTimer <= 0f) { effects.PlayOneShot(zap, 0.7f * masterVolume); zapTimer = 0.3f; }
                if (alarmTimer <= 0f) { effects.PlayOneShot(alarm, 0.45f * masterVolume); alarmTimer = 0.9f; }
            }
            else alarmTimer = zapTimer = 0f;
        }

        public void SetMuted(bool muted)
        {
            Muted = muted;
            AudioListener.volume = muted ? 0f : 1f;
        }

        void OnNotice(string message) => effects.PlayOneShot(chime, 0.7f * masterVolume);
        void OnComms(string message) => effects.PlayOneShot(blip, 0.6f * masterVolume);

        void OnThreatChanged(Threat t, ThreatState state)
        {
            if (state == ThreatState.Alert) effects.PlayOneShot(alarm, 0.4f * masterVolume);
            else if (state == ThreatState.Scared) effects.PlayOneShot(scare, 0.8f * masterVolume);
        }

        void OnCountdown(int n)
        {
            beeps.pitch = n > 0 ? 0.9f : 1.7f;
            beeps.PlayOneShot(blip, (n > 0 ? 0.7f : 1f) * masterVolume);
        }

        void OnGate(int gate, bool lapEnd)
        {
            beeps.pitch = lapEnd ? 1.5f : 1.25f;
            beeps.PlayOneShot(blip, 0.8f * masterVolume);
        }

        void OnLap(int lap, float seconds, bool best) => effects.PlayOneShot(chime, 0.8f * masterVolume);
        void OnRaceFinished() => effects.PlayOneShot(chime, 1f * masterVolume);
        void OnWrongWay() => effects.PlayOneShot(alarm, 0.5f * masterVolume);

        void OnHazardPhase(HazardPhase phase)
        {
            if (phase == HazardPhase.Warning) effects.PlayOneShot(siren, 0.8f * masterVolume);
        }

        AudioSource Source(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            return s;
        }

        AudioSource Loop(string name, AudioClip clip, float volume)
        {
            var s = Source(name);
            s.clip = clip;
            s.loop = true;
            s.volume = volume * masterVolume;
            s.Play();
            return s;
        }
    }
}
