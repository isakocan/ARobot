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

        /// <summary>Çok hafif hava akımı (alçak geçişli gürültü). 6 sn, kesintisiz döngü.</summary>
        public static AudioClip AmbientSoft()
        {
            int n = Rate * 6;
            var data = new float[n];
            uint seed = 12345u;
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                seed = seed * 1664525u + 1013904223u;
                float white = ((seed >> 9) / (float)(1 << 23)) * 2f - 1f;
                lp += (white - lp) * 0.03f;
                float breathe = 0.6f + 0.4f * Mathf.Sin(Tau * (i / (float)n));
                data[i] = lp * 0.55f * breathe;
            }
            // son 0.4 sn'yi başa doğru karıştır: döngü sesi atlamasın
            int fade = (int)(Rate * 0.4f);
            for (int i = 0; i < fade; i++)
            {
                float t = i / (float)fade;
                data[n - fade + i] = data[n - fade + i] * (1f - t) + data[i] * t;
            }
            var clip = AudioClip.Create("ambientSoft", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Güneş parlaması: derin, yavaş gümbürtü. 3 sn, kesintisiz döngü.</summary>
        public static AudioClip Rumble()
        {
            int n = Rate * 3;
            var data = new float[n];
            uint seed = 777u;
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                seed = seed * 1664525u + 1013904223u;
                float white = ((seed >> 9) / (float)(1 << 23)) * 2f - 1f;
                lp += (white - lp) * 0.012f;
                lp2 += (lp - lp2) * 0.05f;
                float t = i / (float)Rate;
                data[i] = (lp2 * 5f + 0.25f * Mathf.Sin(Tau * 40f * t)) * (0.7f + 0.3f * Mathf.Sin(Tau * (i / (float)n) * 2f));
            }
            int fade = (int)(Rate * 0.3f);
            for (int i = 0; i < fade; i++)
            {
                float t = i / (float)fade;
                data[n - fade + i] = data[n - fade + i] * (1f - t) + data[i] * t;
            }
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(data[i], -1f, 1f);
            var clip = AudioClip.Create("rumble", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Parlama uyarısı: iki kez yükselip alçalan siren.</summary>
        public static AudioClip Siren() => Make("siren", 2.4f, t =>
        {
            float cycle = (t % 1.2f) / 1.2f;
            float hz = 520f + 380f * Mathf.Sin(cycle * Mathf.PI);
            float phase = hz * t;
            float env = Mathf.Clamp01(t * 8f) * Mathf.Clamp01((2.4f - t) * 8f);
            return 0.22f * Mathf.Sin(Tau * phase) * env + 0.07f * Mathf.Sin(Tau * phase * 2f) * env;
        });

        /// <summary>Elektrik şoku: kısa, çıtırtılı.</summary>
        public static AudioClip Zap() => Make("zap", 0.35f, t =>
        {
            float crackle = Mathf.Sign(Mathf.Sin(Tau * 190f * t + 9f * Mathf.Sin(Tau * 53f * t)));
            return 0.28f * crackle * Mathf.Exp(-t * 9f) + 0.1f * Mathf.Sin(Tau * 1400f * t) * Mathf.Exp(-t * 14f);
        });

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
            for (int k = 1; k <= 3; k++) v += Sin(110f * k, t) / (k * k);
            return 0.13f * v;
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
