using ARaki.Net;
using ARaki.Robot;
using ARaki.Video;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ARaki.Drive
{
    /// <summary>
    /// Gerçek robotu sürmenin beyni: klavye / oyun kolu / dokunmatik girişi okur, vites modelini 30 ms'de bir
    /// yürütür, istenen durumu (motorlar, ışık, korna, melodi...) RealRobotLink'e yazar, kamera hareketini
    /// görüntüye uygular. Arayüz (DriveCockpit) yalnızca bunu gösterir ve dokunmatik düğmeleri buraya bağlar.
    ///
    /// Tuşlar: W A S D / oklar sür · boşluk korna · L ışık · F selektör · H dörtlü · E / Q vites büyüt / küçült (manuel)
    /// N müzik · R öne bak · B arkaya bak · M dikiz aynası · K kokpit · G vites türü · P fotoğraf · V video
    /// + / - güç · O ayarlar · F11 tam ekran
    /// Sağ tık + sürükle (telefonda parmakla sürükle): etrafa bak.
    /// </summary>
    public class DriveController : MonoBehaviour
    {
        const float Tick = 0.03f;

        [SerializeField] RealRobotLink link;
        [SerializeField] PanoramaViewer viewer;
        [SerializeField] ScreenRecorder recorder;

        public DriveSettings Settings { get; private set; }
        public DriveModel Model { get; } = new DriveModel();
        public RealRobotLink Link => link;
        public PanoramaViewer Viewer => viewer;
        public ScreenRecorder Recorder => recorder;
        public FirmwareData Firmware { get; private set; }

        /// <summary>Dokunmatik düğmelerden gelen giriş (DriveCockpit yazar).</summary>
        public int TouchForward, TouchTurn;
        public bool TouchHorn;
        /// <summary>Bu karede tüm girişlerden çıkan istek (pedal ve okların basılı görünmesi için).</summary>
        public int InputForward { get; private set; }
        public int InputTurn { get; private set; }

        public bool MusicMenuOpen, SettingsOpen, HornMenuOpen;
        public string Toast { get; private set; } = "";
        public float ToastUntil { get; private set; }
        /// <summary>Arduino'nun bildirdiği gerçek ileri güç (-1..1), rampa dahil.</summary>
        public float MotorPower { get; private set; }
        /// <summary>Göstergedeki güç: iki tekerden büyüğü (0..1); yerinde dönerken de görünür.</summary>
        public float MotorLoad { get; private set; }
        public float Rpm { get; private set; } = DriveModel.IdleRpm;
        public float SpeedMs { get; private set; }

        readonly CameraSway sway = new CameraSway();
        float accumulator;
        float lastLocalChange = -10f;
        float hornUntil;
        int lastShiftCount;
        int shiftDir;
        float shiftAt = -10f;

        void Awake()
        {
            Settings = DriveSettings.Load();
            Firmware = FirmwareData.Load();
            viewer.UseKeyboard = false;
            link.PwmLimit = Settings.PowerLimit;
            link.State.ReverseBeep = Settings.ReverseBeep;
            link.State.HornStyle = Settings.HornStyle;
            recorder.Saved += path => ShowToast("Kaydedildi: " + System.IO.Path.GetFileName(path));
        }

        public void ShowToast(string text, float seconds = 2.5f)
        {
            Toast = text;
            ToastUntil = Time.unscaledTime + seconds;
        }

        // ---------------------------------------------------------------- eylemler (tuşlar ve düğmeler)

        public void ToggleLight() { Changed(); link.State.Led = !CurrentLed; link.State.Hazard = false; }
        public void Flash() => link.State.FlashCount++;
        public void ToggleRearMirror() { Settings.RearMirror = !Settings.RearMirror; Settings.Save(); ShowToast(Settings.RearMirror ? "Dikiz aynası açık" : "Dikiz aynası kapalı"); }
        public void ToggleHazard() { Changed(); link.State.Hazard = !CurrentHazard; }
        public void PlayMelody(int n)
        {
            link.State.Melody = n;
            link.State.MelodyCount++;
            if (n > 0 && n <= Firmware.Songs.Count) ShowToast("Çalıyor: " + Firmware.Songs[n - 1].Title);
        }
        public void StopMelody() => PlayMelody(0);
        public void ShiftUp() { if (Settings.Gearbox == GearMode.Manual) { Model.ShiftUp(); shiftDir = 1; shiftAt = Time.unscaledTime; } }
        public void ShiftDown() { if (Settings.Gearbox == GearMode.Manual) { Model.ShiftDown(); shiftDir = -1; shiftAt = Time.unscaledTime; } }
        /// <summary>Vites az önce bu yöne atıldı mı (klavyeyle atınca da kol basılmış görünsün).</summary>
        public bool ShiftFlash(int dir) => shiftDir == dir && Time.unscaledTime - shiftAt < 0.15f;
        public void LookFront() => viewer.LookFront();
        public void CycleGearbox()
        {
            Settings.Gearbox = (GearMode)(((int)Settings.Gearbox + 1) % 3);
            Settings.Save();
            ShowToast(Settings.Gearbox == GearMode.Automatic ? "Otomatik vites" : Settings.Gearbox == GearMode.Manual ? "Manuel vites (E / Q)" : "Vitessiz");
        }
        public void ChangePower(float d)
        {
            Settings.PowerLimit = Mathf.Clamp(Mathf.Round((Settings.PowerLimit + d) * 20f) / 20f, 0.2f, 1f);
            link.PwmLimit = Settings.PowerLimit;
            Settings.Save();
            ShowToast("Güç %" + Mathf.RoundToInt(Settings.PowerLimit * 100f));
        }
        /// <summary>Ayarlarda korna sesini denemek için kısa korna.</summary>
        public void TestHorn() => hornUntil = Time.unscaledTime + 0.35f;
        public void ApplySettings()
        {
            link.State.ReverseBeep = Settings.ReverseBeep;
            link.State.HornStyle = Settings.HornStyle;
            Settings.Save();
        }
        public void Photo()
        {
            recorder.TakePhoto();
            PlayEffect("EF_FOTO");
        }

        /// <summary>Robotun buzzer'ında kısa efekt (Arduino'daki EF_ sabitinin adıyla).</summary>
        public void PlayEffect(string name)
        {
            int n = Firmware[name];
            if (n <= 0 || !Settings.Effects) return;
            link.State.Effect = n;
            link.State.EffectCount++;
        }

        /// <summary>F11: tam ekran aç/kapa (PC uygulamasında; editörde Game penceresinin "Play Maximized"ı kullanılır).</summary>
        public void ToggleFullscreen()
        {
            if (Screen.fullScreen) Screen.fullScreenMode = FullScreenMode.Windowed;
            else
            {
                var r = Screen.currentResolution;
                Screen.SetResolution(r.width, r.height, FullScreenMode.FullScreenWindow);
            }
        }
        public void Video()
        {
            recorder.ToggleVideo();
            PlayEffect(recorder.Recording ? "EF_KAYIT_BASLA" : "EF_KAYIT_BITTI");
            ShowToast(recorder.Recording ? (recorder.HasAudio ? "Video kaydı başladı (sesli)" : "Video kaydı başladı (ses yok: mikrofon izni)")
                                         : "Video kaydediliyor…");
        }

        public bool CurrentLed => Synced(link.State.Led, s => s.led);
        public bool CurrentHazard => Synced(link.State.Hazard, s => s.dortlu);
        public int PlayingMelody => link.Status?.motor?.melodi ?? 0;

        void Changed() => lastLocalChange = Time.unscaledTime;

        // Robotun bildirdiği durum (ör. tarayıcıdan değiştirildi) bizimkinden farklıysa ve biz az önce
        // değiştirmediysek robotunkini benimse
        bool Synced(bool mine, System.Func<RobotStatus.Motor, bool> theirs)
        {
            var m = link.Status?.motor;
            if (m == null || Time.unscaledTime - lastLocalChange < 1.5f) return mine;
            return theirs(m);
        }

        // ---------------------------------------------------------------- her kare

        void Update()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            int forward = TouchForward, turn = TouchTurn;
            bool horn = TouchHorn || Time.unscaledTime < hornUntil;

            if (kb != null)
            {
                forward += (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1 : 0) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1 : 0);
                turn += (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1 : 0) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1 : 0);
                horn |= kb.spaceKey.isPressed;
                if (kb.lKey.wasPressedThisFrame) ToggleLight();
                if (kb.fKey.wasPressedThisFrame) Flash();
                if (kb.hKey.wasPressedThisFrame) ToggleHazard();
                if (kb.nKey.wasPressedThisFrame) { MusicMenuOpen = !MusicMenuOpen; SettingsOpen = HornMenuOpen = false; }
                if (kb.oKey.wasPressedThisFrame) { SettingsOpen = !SettingsOpen; MusicMenuOpen = HornMenuOpen = false; }
                if (kb.escapeKey.wasPressedThisFrame) { SettingsOpen = MusicMenuOpen = HornMenuOpen = false; }
                if (kb.rKey.wasPressedThisFrame) viewer.LookFront();
                if (kb.bKey.wasPressedThisFrame) viewer.LookBack();
                if (kb.mKey.wasPressedThisFrame) ToggleRearMirror();
                if (kb.kKey.wasPressedThisFrame) { Settings.Cockpit = !Settings.Cockpit; Settings.Save(); }
                if (kb.gKey.wasPressedThisFrame) CycleGearbox();
                if (kb.eKey.wasPressedThisFrame) ShiftUp();
                if (kb.qKey.wasPressedThisFrame) ShiftDown();
                if (kb.pKey.wasPressedThisFrame) Photo();
                if (kb.f11Key.wasPressedThisFrame) ToggleFullscreen();
                if (kb.vKey.wasPressedThisFrame) Video();
                if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame) ChangePower(0.1f);
                if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame) ChangePower(-0.1f);
                for (int i = 1; i <= 9; i++)
                    if (kb[(Key)((int)Key.Digit1 + i - 1)].wasPressedThisFrame) PlayMelody(i);
                if (kb.digit0Key.wasPressedThisFrame) StopMelody();
            }
            if (pad != null)
            {
                Vector2 stick = pad.leftStick.ReadValue();
                if (Mathf.Abs(stick.x) > 0.4f) turn += stick.x > 0 ? 1 : -1;
                float gas = pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
                if (Mathf.Abs(stick.y) > 0.4f) gas += stick.y;
                if (Mathf.Abs(gas) > 0.3f) forward += gas > 0 ? 1 : -1;
                horn |= pad.buttonSouth.isPressed;
                if (pad.buttonWest.wasPressedThisFrame) ToggleLight();
                if (pad.buttonEast.wasPressedThisFrame) ToggleHazard();
                if (pad.buttonNorth.wasPressedThisFrame) viewer.LookBack();
                if (pad.rightShoulder.wasPressedThisFrame) ShiftUp();
                if (pad.leftShoulder.wasPressedThisFrame) ShiftDown();
            }
            // Robota ulaşamıyorken robot zaten duruyor: model de hız biriktirmesin, korna da kalmasın.
            // Bağlantı gelince araç kaldığı yerden değil, durduğu yerden kalkar.
            if (!link.Demo && (!link.IsConnected || link.IsWeak)) { forward = turn = 0; horn = false; }
            forward = Mathf.Clamp(forward, -1, 1);
            turn = Mathf.Clamp(turn, -1, 1);
            InputForward = forward;
            InputTurn = turn;
            link.State.Horn = horn;

            accumulator += Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            while (accumulator >= Tick)
            {
                accumulator -= Tick;
                var (l, r) = Model.Step(Tick, forward, turn, Settings.Gearbox);
                link.SendDrive(new DriveCommand(l, r));
            }

            if (Model.ShiftCount != lastShiftCount)   // vites değişti: kısa "tık"
            {
                lastShiftCount = Model.ShiftCount;
                if (Settings.Gearbox != GearMode.Fixed && Model.Gear != 0) PlayEffect("EF_VITES");
            }

            var m = link.Status?.motor;
            // Robotun durumu bizimkinden farklıysa (Arduino yeniden başladı ya da tarayıcıdan değişti) istediğimiz
            // durumu robotunkine eşitle; yoksa sonraki "aç" köprüye değişiklik gibi görünmez.
            if (m != null && Time.unscaledTime - lastLocalChange >= 1.5f)
            {
                link.State.Led = m.led;
                link.State.Hazard = m.dortlu;
            }
            link.State.EngineSound = false;   // motor sesi kaldırıldı (Arduino'da komutu duruyor)
            link.State.AutoEffects = Settings.Effects;
            MotorPower = m != null ? (m.sol + m.sag) / 2f / 255f : 0f;   // ileri hız için (yerinde dönerken sıfır)
            MotorLoad = m != null ? Mathf.Max(Mathf.Abs(m.sol), Mathf.Abs(m.sag)) / 255f : 0f;
            float k = Mathf.Min(1f, Time.unscaledDeltaTime * 5f);   // ibreler yumuşak
            SpeedMs += (Settings.SpeedMs(MotorPower) - SpeedMs) * k;
            Rpm += (Model.Rpm(Settings.Gearbox) + Mathf.Sin(Time.unscaledTime * 11f) * 12f - Rpm) * k;

            var (pitch, roll) = sway.Step(Mathf.Min(Time.unscaledDeltaTime, 0.05f), Model, Settings.CameraMotion);
            viewer.SwayPitch = pitch;
            viewer.SwayRoll = roll;
        }
    }
}
