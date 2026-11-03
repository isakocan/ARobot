using UnityEngine;

namespace ARaki.Robot
{
    /// <summary>
    /// Robotsuz demoda robotun buzzer'ı: Arduino'daki ses mantığının (robot_kontrol.ino, sesGuncelle ve melodi
    /// çalar) birebir kopyası, kare dalgayla. Mantık ses iş parçacığında her 1 ms'lik örnek zamanında yürür;
    /// böylece notalar ve kornanın 4 ms'lik ton değişimi Arduino'daki gibi tam zamanında olur.
    /// Öncelik: korna > efekt > melodi > geri vites bipi > motor sesi.
    /// Not: gerçek piezo kalın seslerde (motor sesi 38-140 Hz) kulaklıktan çok daha cılız ve tıkırtılı duyulur.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class BuzzerSynth : MonoBehaviour
    {
        [SerializeField, Range(0f, 0.3f)] float volume = 0.08f;

        FirmwareData fw;
        readonly object gate = new object();
        double sampleRate;
        double clockMs;            // ses saatine göre "millis()"
        double phase;

        // Arduino'nun değişkenleri (gate kilidiyle)
        double hornEnd, hornStart;
        int hornStyle;
        int[] effect;              // çalan kısa efekt (hz, ms çiftleri), null = yok
        int effectIndex, effectHz;
        double effectEnd;
        int melody, noteIndex;
        double noteEnd, soundEnd;
        int melodyHz;
        bool reverseBeep = true, engineSound, reversing;
        double reverseStart;
        int motorLevel;            // max(|sol|, |sag|), rampalı
        int playingHz;
        bool playingWobble;

        public int Melody { get { lock (gate) return melody; } }

        void Awake()
        {
            fw = FirmwareData.Load();
            sampleRate = AudioSettings.outputSampleRate;
            var src = GetComponent<AudioSource>();
            src.playOnAwake = true;
            src.loop = true;
            src.spatialBlend = 0f;
            if (!src.isPlaying) src.Play();
        }

        // ---------------------------------------------------------------- komutlar (ana iş parçacığı)

        public void Horn(int ms, int style)
        {
            lock (gate)
            {
                bool fresh = clockMs >= hornEnd;   // basılı tutarken gelen yenileme değil
                hornEnd = clockMs + Mathf.Clamp(ms, 1, 2000);
                hornStyle = Mathf.Clamp(style, 0, 5);
                if (fresh)
                {
                    hornStart = clockMs;
                    if (hornStyle == 5) StartEffect(fw["EF_CUCARACHA"]);
                }
            }
        }
        public void PlayEffect(int n) { lock (gate) StartEffect(n); }
        public void PlayMelody(int n) { lock (gate) StartMelody(n); }
        public void SetEngineSound(bool on) { lock (gate) engineSound = on; }
        public void SetReverseBeep(bool on) { lock (gate) reverseBeep = on; }
        public void SetMotors(int left, int right, int targetLeft, int targetRight)
        {
            lock (gate)
            {
                motorLevel = Mathf.Max(Mathf.Abs(left), Mathf.Abs(right));
                reversing = targetLeft < 0 && targetRight < 0;
            }
        }

        // ---------------------------------------------------------------- Arduino mantığı

        void StartMelody(int n)
        {
            melody = 0;
            melodyHz = 0;
            if (n < 1 || n > fw.Songs.Count) return;
            melody = n;
            noteIndex = 0;
            StartNote(clockMs);
        }

        void StartEffect(int n)
        {
            effect = n >= 1 && n <= fw.Effects.Count ? fw.Effects[n - 1] : null;
            effectIndex = 0;
            effectHz = 0;
            if (effect != null) EffectStep(clockMs);
        }

        void EffectStep(double now)
        {
            if (effectIndex * 2 + 1 >= effect.Length || effect[effectIndex * 2 + 1] == 0) { effect = null; effectHz = 0; return; }
            effectHz = effect[effectIndex * 2];
            effectEnd = now + effect[effectIndex * 2 + 1];
            effectIndex++;
        }

        void StartNote(double now)
        {
            var notes = fw.Songs[melody - 1].Notes;
            if (noteIndex * 2 + 1 >= notes.Length || notes[noteIndex * 2 + 1] == 0) { melody = 0; melodyHz = 0; return; }
            int hz = notes[noteIndex * 2], ms = notes[noteIndex * 2 + 1];
            noteIndex++;
            noteEnd = now + ms;
            soundEnd = now + (ms > 60 ? ms - 20 : ms);   // aynı nota art arda ayrı duyulsun
            melodyHz = hz;
        }

        void Step(double now)
        {
            if (melody != 0)
            {
                if (soundEnd > 0 && now >= soundEnd) { melodyHz = 0; soundEnd = 0; }
                if (now >= noteEnd) StartNote(now);
            }
            if (effect != null && now >= effectEnd) EffectStep(now);
            if (!reversing) reverseStart = 0;
            else if (reverseStart == 0) reverseStart = now;

            int hz = 0;
            playingWobble = false;
            if (now < hornEnd && hornStyle != 5)
            {
                if (hornStyle == 4)
                {
                    double t = (now - hornStart) % 520;
                    hz = t < 170 || (t >= 230 && t < 400) ? fw["DUDUT_HZ"] : 0;
                }
                else if (hornStyle < 3) hz = hornStyle == 1 ? 330 : hornStyle == 2 ? 660 : 440;
                else hz = ((long)(now / fw["KORNA_ADIMI"]) % 2) != 0 ? fw["KORNA_HZ_1"] : fw["KORNA_HZ_2"];
            }
            else if (effect != null) hz = effectHz;
            else if (melody != 0) hz = melodyHz;
            else if (reverseBeep && reversing)
            {
                int half = fw["GERI_BIP_YARIM"];
                hz = (now - reverseStart) % (2 * half) < half ? fw["GERI_BIP_HZ"] : 0;
            }
            else if (engineSound)
            {
                int idle = fw["MOTOR_ROLANTI_HZ"], top = fw["MOTOR_EN_YUKSEK_HZ"];
                hz = idle + motorLevel * (top - idle) / 255;
                if (((long)(now / fw["MOTOR_TITRESIM"]) % 2) != 0) hz += hz / 16;
            }
            playingHz = hz;
        }

        // ---------------------------------------------------------------- ses

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (fw == null) return;
            double msPerSample = 1000.0 / sampleRate;
            lock (gate)
            {
                for (int i = 0; i < data.Length; i += channels)
                {
                    double before = clockMs;
                    clockMs += msPerSample;
                    if ((long)clockMs != (long)before) Step(clockMs);   // her 1 ms'de bir loop()
                    float v = 0f;
                    if (playingHz > 0)
                    {
                        phase += playingHz / sampleRate;
                        if (phase >= 1.0) phase -= 1.0;
                        v = phase < 0.5 ? volume : -volume;
                    }
                    for (int c = 0; c < channels; c++) data[i + c] = v;
                }
            }
        }
    }
}
