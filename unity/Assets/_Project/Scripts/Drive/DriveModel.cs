using UnityEngine;

namespace ARaki.Drive
{
    /// <summary>
    /// Sürüş hissi: vites, dönüş karışımı, motor devri ve kamera hareketi. Ağ ve Unity sahnesinden bağımsız;
    /// her 30 ms'de Step çağrılır.
    ///
    /// Vites: ileri basılı tutuldukça güç, o vitesin üst sınırına kadar vitesin hızıyla artar; sınıra varınca
    /// vites büyür ve 0,22 sn güç düşer (kavrama). Büyük viteslerde artış yavaştır, bu yüzden tam güce
    /// ~5 sn'de varılır. Gerçek tekerin hızı ayrıca Arduino'daki rampayla yumuşar.
    /// </summary>
    public class DriveModel
    {
        public static readonly float[] GearTop = { 0f, 0.35f, 0.55f, 0.75f, 1f };      // güç sınırının oranı
        public static readonly float[] GearRate = { 0f, 0.45f, 0.25f, 0.16f, 0.11f };   // saniyede
        public const float ReverseTop = 0.5f, ReverseRate = 0.4f;
        public const float Coast = 1.2f;    // tuş bırakınca saniyede yavaşlama
        public const float Brake = 2.5f;    // ters yöne basınca
        public const float ClutchTime = 0.22f, ClutchDip = 0.6f;
        // Dönüş: kızaklı şasi ancak tekerler arasında büyük fark olunca döner. Hareket halinde dış teker en az
        // bu kadar güçle, iç teker ters yönde dışın çeyreği kadar döner. Geri giderken de aynı (araba gibi).
        public const float TurnMinimum = 0.6f, InnerWheel = -0.25f, InPlace = 0.8f;

        public const float IdleRpm = 900f, TopRpm = 3700f, ScaleRpm = 4000f;

        public float Speed { get; private set; }       // -1..1, vitesin ürettiği güç (kavrama hariç)
        public float Output { get; private set; }      // kavrama dahil, motora giden ileri/geri güç
        public int Gear { get; private set; }          // -1 geri, 0 boş, 1..4
        public float Acceleration { get; private set; } // Output'un süzülmüş değişim hızı (1/sn)
        public float Steer { get; private set; }       // -1, 0, 1

        float clutchUntil, time;

        /// <summary>forward: +1 ileri, -1 geri, 0 bırakıldı; turn: +1 sağ, -1 sol.</summary>
        public (float left, float right) Step(float dt, int forward, int turn, bool gears)
        {
            time += dt;
            Steer = turn;
            float before = Output;
            if (!gears)
            {
                Speed = forward;
                Gear = forward;
            }
            else if (forward > 0)
            {
                if (Speed < 0) Speed = Mathf.Min(0f, Speed + Brake * dt);          // önce dur
                else
                {
                    if (Gear < 1) Gear = 1;
                    float top = GearTop[Gear];
                    Speed = Mathf.Min(top, Speed + GearRate[Gear] * dt);
                    if (Speed >= top - 0.002f && Gear < 4) { Gear++; clutchUntil = time + ClutchTime; }
                }
            }
            else if (forward < 0)
            {
                if (Speed > 0) Speed = Mathf.Max(0f, Speed - Brake * dt);
                else { Gear = -1; Speed = Mathf.Max(-ReverseTop, Speed - ReverseRate * dt); }
            }
            else Speed = Speed > 0 ? Mathf.Max(0f, Speed - Coast * dt) : Mathf.Min(0f, Speed + Coast * dt);

            if (gears)
            {
                if (Speed > 0) while (Gear > 1 && Speed < GearTop[Gear - 1] * 0.8f) Gear--;   // yavaşlayınca vites küçülür
                if (Speed == 0 && forward == 0) Gear = 0;
            }
            Output = Speed * (gears && time < clutchUntil ? ClutchDip : 1f);
            Acceleration += ((Output - before) / Mathf.Max(dt, 0.004f) - Acceleration) * Mathf.Min(1f, dt * 4f);
            return Mix(Output, turn);
        }

        public static (float, float) Mix(float f, float t)
        {
            if (f != 0f && t != 0f)
            {
                float outer = Mathf.Sign(f) * Mathf.Max(Mathf.Abs(f), TurnMinimum);
                float inner = InnerWheel * outer;
                return t > 0f ? (outer, inner) : (inner, outer);   // dönülen taraf iç tekerdir
            }
            if (t != 0f) return (t * InPlace, -t * InPlace);
            return (f, f);
        }

        /// <summary>Gösterge için motor devri: vitesin alt sınırında rölanti, üstünde kırmızıya yakın.</summary>
        public float Rpm(bool gears)
        {
            float a = Mathf.Abs(Speed);
            if (!gears) return IdleRpm + a * (TopRpm - IdleRpm);
            if (Gear == 0) return IdleRpm;
            float top = Gear < 0 ? ReverseTop : GearTop[Gear];
            float bottom = Gear <= 1 ? 0f : GearTop[Gear - 1] * 0.8f;
            float k = Mathf.Clamp01((a - bottom) / (top - bottom));
            return (IdleRpm + (TopRpm - IdleRpm) * k) * (time < clutchUntil ? 0.85f : 1f);
        }
    }

    /// <summary>
    /// Kamera hareketi: gerçek kamera robota sabit; bu, sürücünün başı gibi davranan küçük bir sahte hareket.
    /// Eğim ivmenin gücüyle orantılı (hızlanınca hafif yukarı, yavaşlayınca hafif aşağı), virajda dışa hafif yatma.
    /// Her eksen yumuşak bir yay: hedefe yavaşça gider, neredeyse hiç geçmeden oturur.
    /// </summary>
    public class CameraSway
    {
        public const float PitchPerAccel = 1.1f, MaxPitch = 1.6f;   // derece / (güç/sn), en çok derece
        public const float MaxRoll = 1.2f;
        const float Stiffness = 16f, Damping = 7f;

        float pitch, pitchV, roll, rollV;

        public (float pitch, float roll) Step(float dt, DriveModel m, bool enabled)
        {
            float targetPitch = enabled ? Mathf.Clamp(m.Acceleration * PitchPerAccel, -MaxPitch, MaxPitch) : 0f;
            float targetRoll = enabled ? m.Steer * Mathf.Min(1f, Mathf.Abs(m.Output) * 2f) * MaxRoll : 0f;
            pitchV += ((targetPitch - pitch) * Stiffness - pitchV * Damping) * dt;
            pitch += pitchV * dt;
            rollV += ((targetRoll - roll) * Stiffness - rollV * Damping) * dt;
            roll += rollV * dt;
            return (pitch, roll);
        }
    }
}
