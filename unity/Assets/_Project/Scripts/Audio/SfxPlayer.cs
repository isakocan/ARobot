using ARaki.Game;
using ARaki.Robot;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ARaki.Audio
{
    /// <summary>
    /// Oyun olaylarına ses bağlar. Bütün sesler kodla üretilen geçici efektlerdir (ses dosyası yok):
    /// hafif ortam, motor, LED, bildirimler, keşif/yakalama sesleri, sonar. K tuşu sesi kapatıp açar.
    /// </summary>
    public class SfxPlayer : MonoBehaviour
    {
        [SerializeField] MockRobotLink link;
        [SerializeField] Expedition expedition;
        [SerializeField, Range(0f, 1f)] float masterVolume = 0.6f;

        public bool Muted { get; private set; }

        AudioSource ambient, motor, ledHum, effects, beeps;
        AudioClip chime, alarm, blip, ledOn, zap;
        bool ledWasOn;
        float ledVolume;

        void Awake()
        {
            chime = AudioSynth.Chime();
            alarm = AudioSynth.Alarm();
            blip = AudioSynth.Blip();
            ledOn = AudioSynth.LedOn();
            zap = AudioSynth.Zap();

            ambient = Loop("Ambient", AudioSynth.AmbientSoft(), 0.25f);
            motor = Loop("Motor", AudioSynth.Motor(), 0f);
            ledHum = Loop("LedHum", AudioSynth.LedHum(), 0f);
            effects = Source("Effects");
            beeps = Source("Beeps");
        }

        void Start()
        {
            expedition.Notice += OnNotice;
            expedition.Comms += OnComms;
            expedition.FindRevealed += OnRevealed;
            expedition.FindCaptured += OnCaptured;
            expedition.FindFled += OnFled;
            expedition.Delivered += OnDelivered;
            expedition.ChapterCompleted += OnChapter;
            expedition.SonarFired += OnSonar;
            expedition.Rescued += OnRescued;
        }

        void OnDestroy()
        {
            if (expedition == null) return;
            expedition.Notice -= OnNotice;
            expedition.Comms -= OnComms;
            expedition.FindRevealed -= OnRevealed;
            expedition.FindCaptured -= OnCaptured;
            expedition.FindFled -= OnFled;
            expedition.Delivered -= OnDelivered;
            expedition.ChapterCompleted -= OnChapter;
            expedition.SonarFired -= OnSonar;
            expedition.Rescued -= OnRescued;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame) SetMuted(!Muted);

            var tel = link.Telemetry;
            float load = (Mathf.Abs(tel.LeftSpeed) + Mathf.Abs(tel.RightSpeed)) * 0.5f / link.MaxWheelSpeed;
            motor.volume = Mathf.MoveTowards(motor.volume, Mathf.Clamp01(load * 1.6f) * 0.2f * masterVolume, dt * 2f);
            motor.pitch = Mathf.Lerp(motor.pitch, 0.6f + load * 1.3f, dt * 6f);

            if (link.LedOn && !ledWasOn) effects.PlayOneShot(ledOn, masterVolume);
            ledWasOn = link.LedOn;
            ledVolume = Mathf.MoveTowards(ledVolume, link.LedOn ? 0.1f * masterVolume : 0f, dt * 1.5f);
            ledHum.volume = ledVolume;
        }

        public void SetMuted(bool muted)
        {
            Muted = muted;
            AudioListener.volume = muted ? 0f : 1f;
        }

        void OnNotice(string message) => effects.PlayOneShot(chime, 0.6f * masterVolume);
        void OnComms(string message) => effects.PlayOneShot(blip, 0.55f * masterVolume);

        void OnRevealed(Find f)
        {
            beeps.pitch = 1.4f;
            beeps.PlayOneShot(blip, 0.8f * masterVolume);
        }

        void OnCaptured(Find f)
        {
            beeps.pitch = 1.8f;
            beeps.PlayOneShot(blip, 0.9f * masterVolume);
            effects.PlayOneShot(chime, 0.7f * masterVolume);
        }

        void OnFled(Find f) => effects.PlayOneShot(alarm, 0.25f * masterVolume);
        void OnDelivered(int count, int essence) => effects.PlayOneShot(chime, 0.9f * masterVolume);
        void OnChapter(int index) => effects.PlayOneShot(chime, 1f * masterVolume);
        void OnSonar() => effects.PlayOneShot(zap, 0.5f * masterVolume);
        void OnRescued() => effects.PlayOneShot(alarm, 0.7f * masterVolume);

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
