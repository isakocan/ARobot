using UnityEngine;

namespace ARaki.Drive
{
    /// <summary>
    /// Sürüş hissi: vites, dönüş karışımı, motor devri ve kamera hareketi. Ağ ve Unity sahnesinden bağımsız;
    /// her 30 ms'de Step çağrılır.
    ///
    /// Vites (5 ileri): ileri basılı tutuldukça güç o vitesin üst sınırına çıkar ve orada kalır. Devir hem hızla hem
    /// o viteste gazda geçen süreyle (GearHold) yavaş yavaş kırmızıya yükselir. Otomatikte kırmızıya varınca vites
    /// kendisi büyür; geçerken 0,22 sn güç düşer (kavrama), sonra yeni vitesin sınırına çıkar. Böylece her vites
    /// atışı belirgin bir hızlanmadır; 5. vitese ~6,6 sn'de varılır. Gerçek tekerin hızı ayrıca Arduino'daki
    /// rampayla yumuşar.
    ///
    /// Manuel (sıralı): aynı hız ve devir, ama vitesi sürücü atar (ShiftUp / ShiftDown); kırmızıda "vites at"
    /// işareti yanar. Büyük viteste düşük hızdan kalkış güçsüzdür, küçük vitese inince araç motorla yavaşlar.
    /// Duruştan gaza basınca 1. vites, fren basılı tutunca geri vites kendiliğinden takılır.
    ///
    /// Direksiyon: tuş dijital (-1/0/1) ama direksiyon yavaşça hızlanarak döner, sona yaklaşınca yavaşlar
    /// (yumuşak başlangıç ve bitiş); tam kırmaya ~1,2 sn, bırakınca ~0,8 sn'de ortalanır. Dönüşün keskinliği de
    /// bununla birlikte artar.
    ///
    /// Geri vites: 1. vitesin gücüyle başlar, 3,5 sn basılı tutunca 2. vitesin gücüne çıkar.
    /// </summary>
    public class DriveModel
    {
        public static readonly float[] GearTop = { 0f, 0.24f, 0.4f, 0.58f, 0.78f, 1f };     // güç sınırının oranı
        public static readonly float[] GearRate = { 0f, 0.5f, 0.44f, 0.38f, 0.34f, 0.3f };  // saniyede
        public static readonly float[] GearHold = { 0f, 1.2f, 1.5f, 1.8f, 2.1f, 2.4f };     // kırmızıya varmak için o viteste gazda geçen sn
        public static int TopGear => GearTop.Length - 1;
        public const float ReverseRate = 0.4f;
        public const float Coast = 1.2f;    // tuş bırakınca saniyede yavaşlama
        public const float Brake = 2.5f;    // ters yöne basınca
        public const float ClutchTime = 0.22f, ClutchDip = 0.6f;
        public const float SteerRate = 1.1f, SteerReturn = 1.6f, SteerAccel = 4f;   // en yüksek hız (1/sn), ivme (1/sn²)
        public const float ReverseSecondAfter = 3.5f;
        // Dönüş: kızaklı şasi ancak tekerler arasında büyük fark olunca döner. Hareket halinde dış teker en az
        // bu kadar güçle, iç teker ters yönde dışın çeyreği kadar döner. Geri giderken de aynı (araba gibi).
        public const float TurnMinimum = 0.6f, InnerWheel = -0.25f, InPlace = 0.8f;
        public const float TurnStart = 0.35f;   // dönüşün başladığı fark (sürtünme eşiği), direksiyonun 0..1'ine eklenir

        public const float IdleRpm = 900f, TopRpm = 3700f, ScaleRpm = 4000f;

        public float Speed { get; private set; }       // -1..1, vitesin ürettiği güç (kavrama hariç)
        public float Output { get; private set; }      // kavrama dahil, motora giden ileri/geri güç
        public int Gear { get; private set; }          // -1 geri, 0 boş, 1..5
        public float Acceleration { get; private set; } // Output'un süzülmüş değişim hızı (1/sn)
        public float Steer { get; private set; }       // -1..1, yavaşça dönen direksiyon

        float clutchUntil, time, inGear;   // inGear: bu viteste gaza basılı geçen süre
        float steerVelocity, reverseHeld;
        int shiftRequest;                  // manuel: +1 büyüt, -1 küçült (bir sonraki adımda uygulanır)

        public void ShiftUp() => shiftRequest = 1;
        public void ShiftDown() => shiftRequest = -1;
        /// <summary>Manuelde devir kırmızıya vardı: vites atılabilir.</summary>
        public bool ShiftHint { get; private set; }
        /// <summary>Vites değişti (ses efekti için; her geçişte artar).</summary>
        public int ShiftCount { get; private set; }

        /// <summary>forward: +1 ileri, -1 geri, 0 bırakıldı; turn: +1 sağ, -1 sol.</summary>
        public (float left, float right) Step(float dt, int forward, int turn, GearMode mode)
        {
            time += dt;
            // yumuşak direksiyon: hedefe yaklaştıkça yavaşlayan hız, o hıza da ivmeyle varılır
            bool returning = turn == 0 || (Steer != 0f && Mathf.Sign(turn) != Mathf.Sign(Steer));
            float maxRate = returning ? SteerReturn : SteerRate;
            float wanted = Mathf.Clamp((turn - Steer) * 3f, -maxRate, maxRate);
            steerVelocity = Mathf.MoveTowards(steerVelocity, wanted, SteerAccel * dt);
            Steer = Mathf.Clamp(Steer + steerVelocity * dt, -1f, 1f);
            if (Mathf.Abs(turn - Steer) < 0.002f && Mathf.Abs(steerVelocity) < 0.05f) { Steer = turn; steerVelocity = 0f; }
            float before = Output;
            bool gears = mode != GearMode.Fixed;
            if (!gears)
            {
                Speed = forward;
                Gear = forward;
            }
            else
            {
                if (mode == GearMode.Manual) ApplyShiftRequest();
                if (forward > 0)
                {
                    if (Speed < 0) Speed = Mathf.Min(0f, Speed + Brake * dt);          // önce dur
                    else
                    {
                        if (Gear < 1) { Gear = 1; inGear = 0f; }
                        float top = GearTop[Gear];
                        // manuel: büyük viteste alttan kalkış güçsüz (motor bayılır gibi)
                        float lug = mode == GearMode.Manual && Gear > 1 && Speed < GearTop[Gear - 1] * 0.6f ? 0.35f : 1f;
                        if (Speed < top) Speed = Mathf.Min(top, Speed + GearRate[Gear] * lug * dt);
                        inGear = Mathf.Min(inGear + dt, GearHold[Gear]);
                        if (mode == GearMode.Automatic && AtRedline && Gear < TopGear) Shift(+1);
                    }
                }
                else if (forward < 0)
                {
                    if (Speed > 0) Speed = Mathf.Max(0f, Speed - Brake * dt);
                    else
                    {
                        Gear = -1;
                        reverseHeld += dt;
                        float top = ReverseTop;
                        Speed = Speed > -top ? Mathf.Max(-top, Speed - ReverseRate * dt) : Mathf.Min(-top, Speed + Coast * dt);
                    }
                }
                else Speed = Speed > 0 ? Mathf.Max(0f, Speed - Coast * dt) : Mathf.Min(0f, Speed + Coast * dt);

                if (mode == GearMode.Automatic)
                {
                    // yavaşlayınca vites küçülür
                    if (Speed > 0) while (Gear > 1 && Speed < GearTop[Gear - 1] * 0.8f) { Gear--; inGear = GearHold[Gear]; ShiftCount++; }
                    if (Speed == 0 && forward == 0) Gear = 0;
                }
                else
                {
                    if (Gear >= 1 && Speed > GearTop[Gear]) Speed = Mathf.Max(GearTop[Gear], Speed - Coast * dt);   // motor freni
                    if (Gear < 0 && Speed == 0f && forward == 0) Gear = 0;
                }
            }
            if (forward >= 0 || Gear != -1) reverseHeld = 0f;
            ShiftHint = mode == GearMode.Manual && AtRedline && Gear < TopGear;
            Output = Speed * (gears && time < clutchUntil ? ClutchDip : 1f);
            Acceleration += ((Output - before) / Mathf.Max(dt, 0.004f) - Acceleration) * Mathf.Min(1f, dt * 4f);
            return Mix(Output, Steer);
        }

        /// <summary>Geri viteste o anki güç sınırı: önce 1. vites gücü, uzun basınca 2. vites gücü.</summary>
        public float ReverseTop => reverseHeld >= ReverseSecondAfter ? GearTop[2] : GearTop[1];

        bool AtRedline => Gear >= 1 && Speed >= GearTop[Gear] - 0.002f && inGear >= GearHold[Gear] - 0.001f;

        void Shift(int d)
        {
            Gear += d;
            inGear = d > 0 ? 0f : GearHold[Gear] * 0.6f;
            clutchUntil = time + ClutchTime;
            ShiftCount++;
        }

        void ApplyShiftRequest()
        {
            int req = shiftRequest;
            shiftRequest = 0;
            if (req > 0 && Gear >= 1 && Gear < TopGear) Shift(+1);
            else if (req > 0 && Gear == 0) { Gear = 1; inGear = 0f; ShiftCount++; }
            else if (req < 0 && Gear > 1) Shift(-1);
            else if (req < 0 && Gear == 1 && Speed == 0f) { Gear = 0; ShiftCount++; }
        }

        /// <summary>
        /// Teker güçleri. t direksiyonun ne kadar kırıldığı (-1..1). Kızaklı şasi sürtünmeyi yenmeden dönmez: küçük
        /// farkta hiç dönmeyip eşik aşılınca birden döner. Bu yüzden direksiyon kırılmaya başlar başlamaz eşiğe
        /// (TurnStart) atlanır, sonrası direksiyonla doğrusal artar; dönüş hızı direksiyonla kademeli büyür.
        /// </summary>
        public static (float, float) Mix(float f, float t)
        {
            float a = Mathf.Abs(t);
            if (a < 0.02f) return (f, f);
            float k = TurnStart + (1f - TurnStart) * a;
            if (f != 0f)
            {
                float outer = Mathf.Sign(f) * Mathf.Lerp(Mathf.Abs(f), Mathf.Max(Mathf.Abs(f), TurnMinimum), k);
                float inner = Mathf.Lerp(f, InnerWheel * outer, k);
                return t > 0f ? (outer, inner) : (inner, outer);   // dönülen taraf iç tekerdir
            }
            float spin = Mathf.Sign(t) * InPlace * k;   // yerinde dönüş de eşikten başlar
            return (spin, -spin);
        }

        /// <summary>Gösterge için motor devri: hızla ve viteste geçen süreyle yavaş yavaş kırmızıya çıkar.</summary>
        public float Rpm(GearMode mode)
        {
            float a = Mathf.Abs(Speed);
            if (mode == GearMode.Fixed) return IdleRpm + a * (TopRpm - IdleRpm);
            if (Gear == 0) return IdleRpm;
            float top = Gear < 0 ? ReverseTop : GearTop[Gear];
            float bottom = Gear <= 1 ? 0f : GearTop[Gear - 1] * 0.8f;
            float speedK = Mathf.Clamp01((a - bottom) / (top - bottom));
            float hold = Gear > 0 ? Mathf.Clamp01(inGear / GearHold[Gear]) : 1f;   // geri viteste (-1) dizi yok
            float k = Gear < 0 ? speedK : 0.3f * speedK + 0.7f * hold;
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
        public const float PitchPerAccel = 3f, MaxPitch = 4f;   // derece / (güç/sn), en çok derece (ilk sürümdeki şiddet)
        public const float MaxRoll = 2.5f;
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
