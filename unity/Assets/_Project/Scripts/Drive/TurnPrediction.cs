using System.Collections.Generic;
using ARaki.Video;
using UnityEngine;

namespace ARaki.Drive
{
    /// <summary>
    /// Dönüş gecikmesini gizler (25 Kasım). Robotun kamerası görüntüyü ~0,2 sn geç verir (Theta S; telefon jiroskobuyla
    /// ölçüldü), telefona gelene kadar kodlama + Wi-Fi + çözme eklenir. Motor ise komuttan ~0,07 sn sonra döner. Sürücü
    /// sağa dönünce görüntü bir süre hâlâ eski yöne bakar: "gecikmeli" his en çok dönüşte. 360 görüntüde dönüş yalnız bakış
    /// yönüdür: son L saniyede verilen dönüş komutlarının (henüz görüntüye yansımamış) toplamı kadar bakış önden çevrilir;
    /// görüntü yetiştikçe bu toplam kendiliğinden azalır. L ve dönüş hızı katsayısı ekrandaki görüntünün kendi dönüşünden
    /// (VideoCompass) sürekli öğrenilir; görüntü pusulası yoksa varsayılanlar.
    /// </summary>
    public class TurnPrediction
    {
        public float Latency = 0.32f;          // sn: komut -> ekranda görünme (öğrenilir)
        public float TurnK = 140f;             // derece/sn, (sol - sag) = 510'da (öğrenilir; robottaki konum.py ile aynı başlangıç)
        public float MaxDeg = 45f;
        public bool Learned { get; private set; }
        public float PredictDeg { get; private set; }

        struct Cmd { public float t, w; }          // w: komuttan beklenen dönüş hızı birimi (sol - sag) / 510
        readonly List<Cmd> cmds = new List<Cmd>();
        struct Obs { public float t, yaw; }
        readonly List<Obs> video = new List<Obs>();
        float lastFit;

        /// <summary>w: (sol - sag) / 510 (PWM birimi, robotun gördüğü). DriveController her gönderişte çağırır.</summary>
        public void Command(float t, float w)
        {
            cmds.Add(new Cmd { t = t, w = w });
            int drop = 0; while (drop < cmds.Count && t - cmds[drop].t > 15f) drop++;
            if (drop > 0) cmds.RemoveRange(0, drop);
        }

        /// <summary>Ekrandaki görüntünün ölçülen toplam dönüşü (VideoCompass.Yaw, derece). Sağlıklıysa çağrılır.</summary>
        public void VideoYaw(float t, float yaw)
        {
            video.Add(new Obs { t = t, yaw = yaw });
            int drop = 0; while (drop < video.Count && t - video[drop].t > 15f) drop++;
            if (drop > 0) video.RemoveRange(0, drop);
            if (t - lastFit > 2f) { lastFit = t; Fit(); }
        }

        /// <summary>[a, b] aralığında verilen komutların integrali (birim: (sol-sag)/510 x sn).</summary>
        float Integral(float a, float b)
        {
            float s = 0f;
            for (int i = 0; i < cmds.Count; i++)
            {
                float t0 = cmds[i].t, t1 = i + 1 < cmds.Count ? cmds[i + 1].t : float.MaxValue;
                float lo = Mathf.Max(a, t0), hi = Mathf.Min(b, t1);
                if (hi > lo) s += cmds[i].w * (hi - lo);
            }
            return s;
        }

        /// <summary>Gecikme ve katsayı: görüntünün art arda iki ölçümü arasındaki dönüşü, komutların L kaydırılmış
        /// integraliyle en iyi açıklayan L (0,05-0,8 sn), her L için katsayı en küçük karelerle.</summary>
        void Fit()
        {
            if (video.Count < 20) return;
            float best = float.MaxValue, bestL = Latency, bestK = TurnK; float turning = 0f;
            for (float L = 0.05f; L <= 0.8f; L += 0.01f)
            {
                double sxy = 0, sxx = 0;
                for (int i = 1; i < video.Count; i++)
                {
                    float x = Integral(video[i - 1].t - L, video[i].t - L), y = Mathf.DeltaAngle(video[i - 1].yaw, video[i].yaw);
                    sxy += x * y; sxx += x * x;
                }
                if (sxx < 1e-4) return;                       // yeterince dönülmedi
                float k = (float)(sxy / sxx), e = 0f;
                for (int i = 1; i < video.Count; i++)
                {
                    float x = Integral(video[i - 1].t - L, video[i].t - L), y = Mathf.DeltaAngle(video[i - 1].yaw, video[i].yaw);
                    e += Mathf.Abs(k * x - y);
                }
                if (e < best) { best = e; bestL = L; bestK = k; }
                turning = (float)sxx;
            }
            if (bestK < 40f || bestK > 400f) return;          // anlamsız (görüntü pusulası tutmamış olabilir)
            // yumuşak güncelle: tek bir kötü pencere öngörüyü bozmasın
            Latency = Learned ? Mathf.Lerp(Latency, bestL, 0.3f) : bestL;
            TurnK = Learned ? Mathf.Lerp(TurnK, bestK, 0.3f) : bestK;
            Learned = true;
        }

        /// <summary>Şimdi bakışın önden çevrileceği açı (derece, sağa +): son Latency saniyedeki komutların dönüşü.</summary>
        public float Update(float now)
        {
            PredictDeg = Mathf.Clamp(TurnK * Integral(now - Latency, now), -MaxDeg, MaxDeg);
            return PredictDeg;
        }
    }
}
