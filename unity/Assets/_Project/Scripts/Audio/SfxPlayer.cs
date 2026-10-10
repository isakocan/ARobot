using ARaki.Game;
using ARaki.Robot;
using UnityEngine;

namespace ARaki.Audio
{
    /// <summary>Oyun olaylarına ses bağlar: motor, LED, tarama, tehdit, bildirimler ve ortam uğultusu.</summary>
    public class SfxPlayer : MonoBehaviour
    {
        [SerializeField] MockRobotLink link;
        [SerializeField] MissionController mission;
        [SerializeField, Range(0f, 1f)] float masterVolume = 0.8f;

        AudioSource ambient, motor, ledHum, effects, scan;
        AudioClip chime, alarm, blip, ledOn, scare, tick;
        float scanTimer, alarmTimer, ledVolume;
        bool ledWasOn;

        void Awake()
        {
            chime = AudioSynth.Chime();
            alarm = AudioSynth.Alarm();
            blip = AudioSynth.Blip();
            ledOn = AudioSynth.LedOn();
            scare = AudioSynth.Scare();
            tick = AudioSynth.ScanTick();

            ambient = Loop("Ambient", AudioSynth.Ambient(), 0.5f);
            motor = Loop("Motor", AudioSynth.Motor(), 0f);
            ledHum = Loop("LedHum", AudioSynth.LedHum(), 0f);
            effects = Source("Effects");
            scan = Source("Scan");
        }

        void Start()
        {
            mission.Notice += OnNotice;
            mission.Comms += OnComms;
            foreach (var t in mission.Threats) t.StateChanged += OnThreatChanged;
        }

        void OnDestroy()
        {
            if (mission == null) return;
            mission.Notice -= OnNotice;
            mission.Comms -= OnComms;
            foreach (var t in mission.Threats) if (t != null) t.StateChanged -= OnThreatChanged;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            var tel = link.Telemetry;

            // Motor: tekerlek hızına göre perde ve ses
            float load = (Mathf.Abs(tel.LeftSpeed) + Mathf.Abs(tel.RightSpeed)) * 0.5f / link.MaxWheelSpeed;
            motor.volume = Mathf.MoveTowards(motor.volume, Mathf.Clamp01(load * 1.6f) * 0.35f * masterVolume, dt * 2f);
            motor.pitch = Mathf.Lerp(motor.pitch, 0.6f + load * 1.5f, dt * 6f);

            // LED
            if (link.LedOn && !ledWasOn) effects.PlayOneShot(ledOn, masterVolume);
            ledWasOn = link.LedOn;
            ledVolume = Mathf.MoveTowards(ledVolume, link.LedOn ? 0.22f * masterVolume : 0f, dt * 1.5f);
            ledHum.volume = ledVolume;

            // Tarama tıkırtısı: ilerledikçe sıklaşır, tizleşir
            MissionPoint best = null;
            foreach (var p in mission.Points)
                if (!p.Done && !p.Locked && p.Progress > 0.01f && (best == null || p.Progress > best.Progress)) best = p;
            if (best != null)
            {
                scanTimer -= dt;
                if (scanTimer <= 0f)
                {
                    scan.pitch = 0.9f + best.Progress * 0.9f;
                    scan.PlayOneShot(tick, 0.7f * masterVolume);
                    scanTimer = Mathf.Lerp(0.35f, 0.08f, best.Progress);
                }
            }
            else scanTimer = 0f;

            // Tehdit teması
            bool engaged = false;
            foreach (var t in mission.Threats) engaged |= t.Engaged;
            if (engaged)
            {
                alarmTimer -= dt;
                if (alarmTimer <= 0f)
                {
                    effects.PlayOneShot(alarm, 0.8f * masterVolume);
                    alarmTimer = 0.7f;
                }
            }
            else alarmTimer = 0f;
        }

        void OnNotice(string message) => effects.PlayOneShot(chime, 0.9f * masterVolume);
        void OnComms(string message) => effects.PlayOneShot(blip, 0.8f * masterVolume);

        void OnThreatChanged(Threat t, ThreatState state)
        {
            if (state == ThreatState.Alert) effects.PlayOneShot(alarm, 0.5f * masterVolume);
            else if (state == ThreatState.Scared) effects.PlayOneShot(scare, masterVolume);
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
