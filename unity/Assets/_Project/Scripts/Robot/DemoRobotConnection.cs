using ARaki.Net;
using UnityEngine;

namespace ARaki.Robot
{
    /// <summary>
    /// Robotsuz demo: köprü ve Arduino'nun bu cihazda çalışan kopyası. Köprünün UDP kurallarını (durum değişince
    /// komut, sayaçla tek seferlik işler, korna yenileme) ve Arduino'nun motor mantığını (rampa, 300 ms
    /// güvenlik) gerçek koddaki sayılarla uygular. Sesleri BuzzerSynth çalar, görüntü kayıtlı karelerden gelir.
    /// </summary>
    public sealed class DemoRobotConnection : IRobotConnection
    {
        readonly FirmwareData fw = FirmwareData.Load();
        readonly BuzzerSynth buzzer;
        ControlState previous;
        bool hasPrevious;
        float lastHorn = -10f;
        int targetLeft, targetRight, left, right;
        float lastCommand, rampClock;
        bool led, hazard, engine, reverseBeep = true;

        public DemoRobotConnection(BuzzerSynth buzzer) { this.buzzer = buzzer; }

        public string Host => "demo";
        public bool Connected => true;
        public double RoundTripMs => 4;
        public float Loss => 0f;
        public double SinceReplyMs => 0;
        public RobotStatus Status { get; private set; } = new RobotStatus { arduino = true, kamera = "aktif", fps = 15, h264 = true, sinyal = -58 };

        public void Send(ControlState s)
        {
            float now = Time.unscaledTime;
            targetLeft = Mathf.Clamp(s.Left, -255, 255);
            targetRight = Mathf.Clamp(s.Right, -255, 255);
            lastCommand = now;
            if (hasPrevious)
            {
                if (s.Led != previous.Led) { led = s.Led; hazard = false; }
                if (s.Hazard != previous.Hazard) hazard = s.Hazard;
                if (s.EngineSound != previous.EngineSound) buzzer.SetEngineSound(engine = s.EngineSound);
                if (s.ReverseBeep != previous.ReverseBeep) buzzer.SetReverseBeep(reverseBeep = s.ReverseBeep);
                if (s.MelodyCount != previous.MelodyCount) buzzer.PlayMelody(s.Melody);
                if (!s.Horn && previous.Horn) buzzer.Horn(1, 0);
            }
            else
            {
                buzzer.SetEngineSound(engine = s.EngineSound);
                buzzer.SetReverseBeep(reverseBeep = s.ReverseBeep);
            }
            if (s.Horn && now - lastHorn > 0.12f) { lastHorn = now; buzzer.Horn(250, s.HornStyle); }
            previous = s;
            hasPrevious = true;
        }

        /// <summary>Arduino'nun loop()'u: her 5 ms bir rampa adımı.</summary>
        public void Tick()
        {
            float now = Time.unscaledTime;
            if ((targetLeft != 0 || targetRight != 0) && now - lastCommand > fw["KOMUT_ZAMAN_ASIMI"] / 1000f)
                targetLeft = targetRight = 0;
            float step = fw["RAMPA_ADIMI_MS"] / 1000f;
            if (rampClock < now - 0.5f) rampClock = now;
            while (rampClock + step <= now)
            {
                rampClock += step;
                left = Approach(left, targetLeft);
                right = Approach(right, targetRight);
            }
            buzzer.SetMotors(left, right, targetLeft, targetRight);

            // Uydurma akım: düz giderken 0,1-0,2 A, hızlanırken kalkış tepesi
            int Current(int v, int t) => v == 0 ? 0 : 90 + Mathf.Abs(v) / 3 + (Mathf.Abs(t) > Mathf.Abs(v) ? 700 : 0);
            Status.motor = new RobotStatus.Motor
            {
                sol = left, sag = right, sol_ma = Current(left, targetLeft), sag_ma = Current(right, targetRight),
                led = led, dortlu = hazard, motor_sesi = engine, geri_bip = reverseBeep, melodi = buzzer.Melody,
            };
            Status.veri_kbps = 4000;
        }

        int Approach(int now, int target)
        {
            bool slowing = (now > 0 && target < now) || (now < 0 && target > now);
            int step = slowing ? fw["YAVASLAMA_ADIMI"] : fw["HIZLANMA_ADIMI"];
            if (slowing && ((now > 0 && target < 0) || (now < 0 && target > 0))) target = 0;   // önce sıfırda dur
            if (now < target) return Mathf.Min(now + step, target);
            if (now > target) return Mathf.Max(now - step, target);
            return now;
        }

        public void Dispose() { }
    }
}
