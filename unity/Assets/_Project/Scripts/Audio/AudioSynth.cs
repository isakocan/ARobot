using System;
using UnityEngine;

namespace ARaki.Audio
{
    /// <summary>
    /// Ses dosyası olmadan, kodla üretilen kısa efektler. Demo ve ilk his için yeterli; ileride
    /// gerçek ses tasarımıyla (kayıtlı sesler) değiştirilecek.
    /// </summary>
    public static class AudioSynth
    {
        const int Rate = 44100;
        const float Tau = Mathf.PI * 2f;

        static AudioClip Make(string name, float seconds, Func<float, float> sample)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(sample(i / (float)Rate), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Sin(float hz, float t) => Mathf.Sin(Tau * hz * t);

        /// <summary>Alçak, yavaş nefes alan uğultu: uzay aracının içi. 4 sn, kesintisiz döngü.</summary>
        public static AudioClip Ambient() => Make("ambient", 4f, t =>
        {
            float lfo = 0.65f + 0.35f * Sin(0.25f, t);
            return 0.10f * Sin(55f, t) + 0.07f * Sin(82.5f, t) * lfo + 0.045f * Sin(110f, t) + 0.02f * Sin(220.5f, t) * (1f - lfo);
        });

        /// <summary>Elektrik motoru: tekrarlayan tını, hıza göre perdesi (pitch) değişir. 1 sn, kesintisiz döngü.</summary>
        public static AudioClip Motor() => Make("motor", 1f, t =>
        {
            float v = 0f;
            for (int k = 1; k <= 6; k++) v += Sin(110f * k, t) / k;
            return 0.22f * v + 0.03f * Sin(330f, t);
        });

        /// <summary>Tarama sırasında tıkırtı.</summary>
        public static AudioClip ScanTick() => Make("scan", 0.07f, t => 0.5f * Sin(1800f, t) * Mathf.Exp(-t * 55f));

        /// <summary>Görev ilerlemesi: üç notalık yukarı arpej.</summary>
        public static AudioClip Chime() => Make("chime", 0.95f, t =>
        {
            float v = 0f;
            float[] notes = { 659.3f, 830.6f, 987.8f };
            for (int i = 0; i < notes.Length; i++)
            {
                float t0 = i * 0.11f;
                if (t < t0) continue;
                float e = Mathf.Exp(-(t - t0) * 5f);
                v += 0.30f * Sin(notes[i], t - t0) * e + 0.10f * Sin(notes[i] * 2f, t - t0) * e;
            }
            return v;
        });

        /// <summary>Tehdit teması alarmı.</summary>
        public static AudioClip Alarm() => Make("alarm", 0.6f, t =>
        {
            float hz = t < 0.3f ? 880f : 660f;
            float local = t < 0.3f ? t : t - 0.3f;
            float env = Mathf.Clamp01(local * 80f) * Mathf.Clamp01((0.3f - local) * 80f);
            return Mathf.Sign(Sin(hz, t)) * 0.18f * env;
        });

        /// <summary>Merkezden gelen telsiz mesajı: iki kısa bip.</summary>
        public static AudioClip Blip() => Make("blip", 0.16f, t =>
        {
            bool on = t < 0.05f || (t > 0.09f && t < 0.14f);
            float local = t < 0.07f ? t : t - 0.09f;
            float env = Mathf.Clamp01(local * 200f) * Mathf.Clamp01((0.05f - local) * 200f);
            return on ? 0.28f * Sin(1250f, t) * env : 0f;
        });

        /// <summary>LED açılırken yükselen ıslık.</summary>
        public static AudioClip LedOn() => Make("ledOn", 0.3f, t =>
        {
            float phase = 300f * t + 550f * t * t / 0.3f;
            return 0.28f * Mathf.Sin(Tau * phase) * (1f - t / 0.3f);
        });

        /// <summary>LED yanarken tiz uğultu. 1 sn, kesintisiz döngü.</summary>
        public static AudioClip LedHum() => Make("ledHum", 1f, t => 0.11f * Sin(660f, t) + 0.05f * Sin(1320f, t) + 0.03f * Sin(220f, t));

        /// <summary>Düşman ışıktan kaçarken alçalan ses.</summary>
        public static AudioClip Scare() => Make("scare", 0.5f, t =>
        {
            float phase = 900f * t - 700f * t * t / 1f;
            return 0.3f * Mathf.Sin(Tau * phase) * Mathf.Exp(-t * 4f);
        });
    }
}
