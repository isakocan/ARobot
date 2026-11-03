using System;
using System.Collections.Generic;
using ARaki.Video;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ARaki.Drive
{
    /// <summary>
    /// Sürüş ekranı: simülasyon oyunlarındaki gibi araç içi görünüm. Ön cam çerçevesi (tavan, A sütunları),
    /// gösterge paneli, direksiyon (solda / ortada / sağda), direksiyonun içinden görünen devir ve hız
    /// saatleri, iç dikiz aynası ve iki yan ayna (360° görüntüden ayrı kameralarla), orta konsolda dokunmatik
    /// düğmeler, şarkı menüsü ve ayarlar. Her şey koddan çizilir (VectorShape); ayar değişince yeniden kurulur.
    ///
    /// Dokunmatik ve fare girişi burada çözülür (birden çok parmak: gaz basılıyken direksiyon çevrilebilir);
    /// eylemler DriveController'a gider.
    /// </summary>
    public class DriveCockpit : MonoBehaviour
    {
        [SerializeField] DriveController drive;
        [SerializeField] RobotVideo video;
        [SerializeField] Font font;
        [SerializeField] Font mono;

        const float H = 720f;
        static readonly Color Text = Hex("#ECEAE6"), Dim = Hex("#8E949B"), Accent = Hex("#F08A45"),
            Red = Hex("#E5533D"), Green = Hex("#5CC28B"), Amber = Hex("#F2B33D"), Glass = new Color(0.05f, 0.055f, 0.065f, 0.95f);

        class Btn
        {
            public RectTransform Rt;
            public Text Label;
            public Action Press;
            public Action<bool> Hold;
            public Func<bool> Lit;
            public Graphic Back;
            public VectorShape Icon;
            public Func<string> Dynamic;
            public int Pointer = int.MinValue;
        }

        Canvas canvas;
        RectTransform root, content;
        float W;
        readonly List<Btn> buttons = new List<Btn>();
        readonly Dictionary<int, Vector2> looking = new Dictionary<int, Vector2>();
        bool dirty = true;
        string layoutKey = "";

        RectTransform wheelRt, tachNeedle, speedNeedle, menuRoot, settingsRoot, musicRoot;
        float speedMax;
        bool menuOpen;
        public bool MenuOpen { get => menuOpen; set => menuOpen = value; }
        Text gearText, speedText, statusText, infoText, toastText, centerText, songText, recText, warnText;
        Text lampLight, lampHazard, lampEngine, settingsInfo;
        float wheelAngle;

        Camera rearCam, leftCam, rightCam;
        RenderTexture rearRt, leftRt, rightRt;
        RawImage rearImg, leftImg, rightImg;

        void Start()
        {
            var go = new GameObject("Kokpit", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, H);
            scaler.matchWidthOrHeight = 1f;
            root = Stretch("Kök", go.transform);
            root.pivot = Vector2.zero;
            MakeMirrorCameras();
        }

        // ---------------------------------------------------------------- kurulum

        void Rebuild()
        {
            if (content != null) Destroy(content.gameObject);
            buttons.Clear();
            Canvas.ForceUpdateCanvases();
            W = root.rect.width;
            content = Stretch("İçerik", root);
            content.pivot = Vector2.zero;
            var s = drive.Settings;

            // Düzen (kullanıcının seçimi): altta üst yarısı görünen büyük direksiyon, ibreli devir ve hız saatleri
            // direksiyonun yanında (sol direksiyonda sağında), üstte iç ayna ve iki yanında bilgi satırları,
            // dikey yan aynalar kenarlarda orta yükseklikte; düğmeler sağ altta büyük MENÜ'nün arkasında,
            // korna her zaman görünür. Torpido ve sütun yok, görüntü açık kalır.
            float wheelX = s.Wheel == WheelSide.Left ? W / 2f - 360f : s.Wheel == WheelSide.Right ? W / 2f + 360f : W / 2f - 120f;
            float clusterX = !s.Cockpit ? W / 2f : s.Wheel == WheelSide.Right ? wheelX - 300f : wheelX + 300f;
            const float wheelY = -64f, clusterY = 64f, dialR = 40f, dialDx = 52f;   // direksiyonun merkezi ekranın altında

            var body = Shape("Gövde", content);
            body.RoundRect(new Rect(clusterX - 110f, clusterY - 50f, 220f, 118f), 20f, new Color(0.03f, 0.035f, 0.04f, 0.6f));

            // direksiyon
            if (s.Cockpit)
            {
                var wheel = Shape("Direksiyon", content);
                wheelRt = (RectTransform)wheel.transform;
                wheelRt.anchorMin = wheelRt.anchorMax = Vector2.zero;
                wheelRt.anchoredPosition = V(wheelX, wheelY);
                wheelRt.sizeDelta = Vector2.zero;
                var rim = new Color(0.07f, 0.075f, 0.085f, 0.94f);
                wheel.Arc(Vector2.zero, 168f, 192f, 0f, 360f, rim, 160);
                wheel.Arc(Vector2.zero, 188f, 192f, 0f, 360f, new Color(1f, 1f, 1f, 0.14f), 160);
                wheel.Arc(Vector2.zero, 168f, 171f, 0f, 360f, new Color(0f, 0f, 0f, 0.5f), 160);
                foreach (float a in new[] { 90f, 270f, 180f })
                {
                    float r = a * Mathf.Deg2Rad;
                    wheel.Line(Vector2.zero, new Vector2(Mathf.Sin(r), Mathf.Cos(r)) * 172f, 22f, rim);
                }
                wheel.Disc(Vector2.zero, 44f, Hex("#22262B"), Hex("#121417"));
                wheel.Arc(Vector2.zero, 44f, 47f, 0f, 360f, new Color(1f, 1f, 1f, 0.12f));
                wheel.Arc(Vector2.zero, 168f, 192f, -2.5f, 2.5f, Accent);   // tepe işareti
            }
            else wheelRt = null;

            // ibreli saatler
            Dial(body, V(clusterX - dialDx, clusterY), dialR, 4f, 1f, 3.4f, out tachNeedle);
            speedMax = s.Unit == SpeedUnit.KilometersPerHour ? 3f : 0.8f;
            Dial(body, V(clusterX + dialDx, clusterY), dialR, speedMax, s.Unit == SpeedUnit.KilometersPerHour ? 1f : 0.2f, -1f, out speedNeedle);
            gearText = Label(content, "N", mono, 19, Text, TextAnchor.MiddleCenter, V(clusterX - dialDx, clusterY - 19f), V(28, 22));
            speedText = Label(content, "0,0", mono, 14, Text, TextAnchor.MiddleCenter, V(clusterX + dialDx, clusterY - 17f), V(44, 16));
            Label(content, s.Unit == SpeedUnit.KilometersPerHour ? "km/sa" : "m/sn", font, 10, Dim, TextAnchor.MiddleCenter,
                  V(clusterX + dialDx, clusterY - 31f), V(44, 12));
            Label(content, "x1000", font, 10, Dim, TextAnchor.MiddleCenter, V(clusterX - dialDx, clusterY - 33f), V(44, 12));
            // lambalar: saatlerin üstünde
            lampLight = Label(content, "IŞIK", font, 12, Dim, TextAnchor.MiddleCenter, V(clusterX - 60f, clusterY + 54f), V(44, 14));
            lampHazard = Label(content, "DÖRTLÜ", font, 12, Dim, TextAnchor.MiddleCenter, V(clusterX, clusterY + 54f), V(52, 14));
            lampEngine = Label(content, "MOTOR", font, 12, Dim, TextAnchor.MiddleCenter, V(clusterX + 60f, clusterY + 54f), V(48, 14));
            foreach (var l in new[] { lampLight, lampHazard, lampEngine }) UiShadow(l);

            // büyük KORNA ve MENÜ direksiyonun karşı köşesinde; menü açılınca üstünde 5 + 4 simgeli düğme
            // (iki sıra: yan aynanın altında kalır, onu örtmez)
            bool onLeft = s.Cockpit && s.Wheel == WheelSide.Right;
            const float bw = 100f, bh = 78f, gap = 8f;
            float mw = 5 * bw + 6 * gap, mh = 2 * bh + 3 * gap;
            float menuX = onLeft ? 24f : W - 24f - mw;
            float menuBtnX = onLeft ? 24f : W - 24f - 78f, hornX = onLeft ? 24f + 88f : W - 24f - 78f - 88f;
            Button("MENÜ", new Rect(menuBtnX, 16f, 78f, 78f), () => { menuOpen = !menuOpen; drive.MusicMenuOpen = drive.SettingsOpen = false; },
                   null, () => menuOpen, 13, icon: "menu");
            Button("KORNA", new Rect(hornX, 16f, 78f, 78f), null, held => drive.TouchHorn = held, null, 13, icon: "korna");
            songText = Label(content, "", font, 14, Text, onLeft ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight,
                             V(onLeft ? 24f + 170f : W - 24f - 170f, 108f), V(340, 18));
            UiShadow(songText);
            menuRoot = Rt("Menü", content, V(menuX, 104f), V(mw, mh));
            var menuBg = Shape("Zemin", menuRoot);
            menuBg.RoundRect(new Rect(0, 0, mw, mh), 16f, new Color(0.03f, 0.035f, 0.04f, 0.82f));
            string[] names = { "IŞIK", "SELEKTÖR", "DÖRTLÜ", "MOTOR SESİ", "MÜZİK", "FOTO", "VİDEO", "GÜÇ", "AYARLAR" };
            string[] icons = { "isik", "selektor", "dortlu", "motor", "muzik", "foto", "video", "guc", "ayar" };
            Action[] acts = { drive.ToggleLight, drive.Flash, drive.ToggleHazard, drive.ToggleEngineSound,
                              () => { drive.MusicMenuOpen = !drive.MusicMenuOpen; drive.SettingsOpen = false; menuOpen = false; },
                              drive.Photo, drive.Video, () => drive.ChangePower(drive.Settings.PowerLimit >= 0.99f ? -0.8f : 0.1f),
                              () => { drive.SettingsOpen = !drive.SettingsOpen; drive.MusicMenuOpen = false; menuOpen = false; } };
            Func<bool>[] lits = { () => drive.CurrentLed, null, () => drive.CurrentHazard, () => drive.CurrentEngine, () => drive.MusicMenuOpen,
                                  null, () => drive.Recorder.Recording, null, () => drive.SettingsOpen };
            for (int i = 0; i < names.Length; i++)
            {
                int col = i % 5, row = i / 5;
                Button(names[i], new Rect(menuX + gap + col * (bw + gap), 104f + mh - gap - bh - row * (bh + gap), bw, bh), acts[i], null, lits[i], 12,
                       TextAnchor.MiddleCenter, menuRoot, icon: icons[i]);
            }
            menuRoot.gameObject.SetActive(false);

            // aynalar: iç ayna üstte, yan aynalar kenarlarda orta yükseklikte
            if (s.Cockpit && s.Mirrors)
            {
                rearImg = Mirror("İç ayna", new Rect(W / 2f - 115f, H - 12f - 54f, 230f, 54f), rearRt, true);
                leftImg = Mirror("Sol ayna", new Rect(16f, H / 2f - 70f, 100f, 150f), leftRt, true);
                rightImg = Mirror("Sağ ayna", new Rect(W - 116f, H / 2f - 70f, 100f, 150f), rightRt, true);
            }
            else rearImg = leftImg = rightImg = null;

            // durum ve bildirimler
            // üst bilgi: iç aynanın solunda bağlantı, sağında görüntü / güç / saat, altında kayıt
            statusText = Label(content, "", mono, 14, Text, TextAnchor.MiddleRight, V(W / 2f - 135f - 170f, H - 30f), V(340, 18));
            UiShadow(statusText);
            infoText = Label(content, "", mono, 14, Text, TextAnchor.MiddleLeft, V(W / 2f + 135f + 170f, H - 30f), V(340, 18));
            UiShadow(infoText);
            recText = Label(content, "", font, 16, Red, TextAnchor.MiddleLeft, V(W / 2f + 135f + 170f, H - 52f), V(340, 18));
            UiShadow(recText);
            toastText = Label(content, "", font, 20, Text, TextAnchor.MiddleCenter, V(W / 2f, H - 100f), V(700, 30));
            UiShadow(toastText);
            warnText = Label(content, "", font, 22, Red, TextAnchor.MiddleCenter, V(W / 2f, H / 2f + 120f), V(800, 30));
            UiShadow(warnText);
            centerText = Label(content, "", font, 24, Text, TextAnchor.MiddleCenter, V(W / 2f, H / 2f + 40f), V(900, 34));
            UiShadow(centerText);

            // dokunmatik sürüş: solda yön, sağda pedallar
            if (s.TouchControls)
            {
                Button("SOL", new Rect(24f, 220f, 80f, 80f), null, h => drive.TouchTurn = h ? -1 : (drive.TouchTurn == -1 ? 0 : drive.TouchTurn), null, 18);
                Button("SAĞ", new Rect(112f, 220f, 80f, 80f), null, h => drive.TouchTurn = h ? 1 : (drive.TouchTurn == 1 ? 0 : drive.TouchTurn), null, 18);
                Button("GAZ", new Rect(W - 104f, 220f, 80f, 120f), null, h => drive.TouchForward = h ? 1 : (drive.TouchForward == 1 ? 0 : drive.TouchForward), null, 16);
                Button("FREN", new Rect(W - 192f, 220f, 80f, 90f), null, h => drive.TouchForward = h ? -1 : (drive.TouchForward == -1 ? 0 : drive.TouchForward), null, 16);
            }

            menuRoot.SetAsLastSibling();   // menü açılınca aynaların üstünde kalsın
            BuildMusicMenu();
            BuildSettings();
            layoutKey = Key();
            dirty = false;
        }

        string Key() => W + "|" + drive.Settings.Wheel + drive.Settings.Cockpit + drive.Settings.Mirrors + drive.Settings.TouchControls + drive.Settings.Unit;

        void Dial(VectorShape body, Vector2 c, float r, float max, float step, float redFrom, out RectTransform needle)
        {
            body.Disc(c, r + 3f, new Color(1f, 1f, 1f, 0.14f), new Color(1f, 1f, 1f, 0.06f));
            body.Disc(c, r, new Color(0.05f, 0.055f, 0.065f, 0.92f), new Color(0.02f, 0.022f, 0.026f, 0.95f));
            if (redFrom > 0f) body.Arc(c, r - 4f, r - 1f, Angle(redFrom, max), Angle(max, max), Red);
            for (float v = 0f; v <= max + 0.001f; v += step / 2f)
            {
                bool major = Mathf.Abs(v / step - Mathf.Round(v / step)) < 0.01f;
                float a = Angle(v, max) * Mathf.Deg2Rad;
                var d = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                body.Line(c + d * (r - (major ? 6f : 3.5f)), c + d * (r - 1f), major ? 1.6f : 1f, major ? Text : Dim);
                if (major)
                    Label(content, step < 1f ? v.ToString("0.0").Replace('.', ',') : v.ToString("0"), mono, 10, Dim,
                          TextAnchor.MiddleCenter, c + d * (r - 13f), V(24, 12));
            }
            var n = Shape("İbre", content);
            needle = (RectTransform)n.transform;
            needle.anchorMin = needle.anchorMax = Vector2.zero;
            needle.anchoredPosition = c;
            needle.sizeDelta = Vector2.zero;
            n.Line(V(0, -3f), V(0, r - 5f), 2f, Accent);
            n.Disc(Vector2.zero, 3.5f, Hex("#2E3237"), Hex("#2E3237"), 16);
        }

        static float Angle(float v, float max) => -130f + 260f * Mathf.Clamp01(v / max);

        RawImage Mirror(string name, Rect r, RenderTexture rt, bool flip)
        {
            var frame = Shape(name, content);
            frame.RoundRect(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), 8f, new Color(0.03f, 0.035f, 0.04f, 0.9f));
            var img = new GameObject(name + " görüntü", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            var irt = (RectTransform)img.transform;
            irt.SetParent(content, false);
            irt.anchorMin = irt.anchorMax = Vector2.zero;
            irt.pivot = Vector2.zero;
            irt.anchoredPosition = r.position;
            irt.sizeDelta = r.size;
            img.texture = rt;
            img.uvRect = flip ? new Rect(1f, 0f, -1f, 1f) : new Rect(0f, 0f, 1f, 1f);
            img.raycastTarget = false;
            return img;
        }

        void BuildMusicMenu()
        {
            var songs = drive.Firmware.Songs;
            musicRoot = Panel("Müzik", new Rect(W / 2f - 400f, H / 2f - 200f, 800f, 430f));
            const int perColumn = 14;
            int line = 0, column = 0;
            string group = null;
            void Next() { if (++line >= perColumn) { line = 0; column++; } }
            for (int i = 0; i < songs.Count; i++)
            {
                if (songs[i].Group != group)
                {
                    group = songs[i].Group;
                    if (!string.IsNullOrEmpty(group))
                    {
                        if (line >= perColumn - 1) { line = 0; column++; }
                        Label(musicRoot, group.ToUpperInvariant(), font, 12, Dim, TextAnchor.MiddleLeft,
                              V(24f + column * 260f + 110f, 430f - 60f - line * 25f), V(220, 18));
                        Next();
                    }
                }
                int n = i + 1;
                Button(songs[i].Title, new Rect(W / 2f - 400f + 16f + column * 260f, H / 2f - 200f + 430f - 72f - line * 25f, 250f, 23f),
                       () => drive.PlayMelody(n), null, () => drive.PlayingMelody == n, 15, TextAnchor.MiddleLeft, musicRoot);
                Next();
            }
            Label(musicRoot, "ŞARKILAR  ·  buzzer'dan çalar", font, 16, Text, TextAnchor.MiddleLeft, V(180f, 430f - 24f), V(330, 22));
            Button("DURDUR", new Rect(W / 2f + 400f - 196f, H / 2f - 200f + 430f - 40f, 92f, 28f), drive.StopMelody, null, null, 14, TextAnchor.MiddleCenter, musicRoot);
        }

        void BuildSettings()
        {
            var s = drive.Settings;
            var r = new Rect(W / 2f - 290f, H / 2f - 320f, 580f, 640f);
            settingsRoot = Panel("Ayarlar", r);
            float y = r.yMax - 46f;
            Label(settingsRoot, "AYARLAR", font, 18, Text, TextAnchor.MiddleLeft, V(r.x + 20f + 150f - r.x, y + 14f - r.y), V(300, 24));
            y -= 40f;
            void Toggle(string label, Func<bool> get, Action<bool> set)
            {
                Label(settingsRoot, label, font, 16, Text, TextAnchor.MiddleLeft, V(170f + 20f, y - r.y + 14f), V(340, 22));
                Button("", new Rect(r.xMax - 110f, y, 90f, 28f), () => { set(!get()); drive.ApplySettings(); dirty = true; }, null, get, 14,
                       TextAnchor.MiddleCenter, settingsRoot, () => get() ? "AÇIK" : "KAPALI");
                y -= 34f;
            }
            void Choice(string label, string[] options, Func<int> get, Action<int> set)
            {
                Label(settingsRoot, label, font, 16, Text, TextAnchor.MiddleLeft, V(170f + 20f, y - r.y + 14f), V(340, 22));
                float w = 78f, x = r.xMax - 20f - options.Length * w;
                for (int i = 0; i < options.Length; i++)
                {
                    int k = i;
                    Button(options[i], new Rect(x + i * w, y, w - 4f, 28f), () => { set(k); drive.ApplySettings(); dirty = true; }, null,
                           () => get() == k, 14, TextAnchor.MiddleCenter, settingsRoot);
                }
                y -= 34f;
            }
            void Stepper(string label, Func<string> value, Action minus, Action plus)
            {
                Label(settingsRoot, label, font, 16, Text, TextAnchor.MiddleLeft, V(170f + 20f, y - r.y + 14f), V(340, 22));
                Button("-", new Rect(r.xMax - 176f, y, 40f, 28f), () => { minus(); drive.ApplySettings(); }, null, null, 18, TextAnchor.MiddleCenter, settingsRoot);
                Button("", new Rect(r.xMax - 134f, y, 72f, 28f), null, null, null, 15, TextAnchor.MiddleCenter, settingsRoot, value);
                Button("+", new Rect(r.xMax - 60f, y, 40f, 28f), () => { plus(); drive.ApplySettings(); }, null, null, 18, TextAnchor.MiddleCenter, settingsRoot);
                y -= 34f;
            }

            Choice("Güç", new[] { "Vitesli", "Sabit" }, () => s.Gears ? 0 : 1, v => s.Gears = v == 0);
            Toggle("Geri vites bipi", () => s.ReverseBeep, v => s.ReverseBeep = v);
            Toggle("Kamera hareketi", () => s.CameraMotion, v => s.CameraMotion = v);
            Toggle("Kokpit", () => s.Cockpit, v => s.Cockpit = v);
            Toggle("Aynalar", () => s.Mirrors, v => s.Mirrors = v);
            Toggle("Dokunmatik sürüş düğmeleri", () => s.TouchControls, v => s.TouchControls = v);
            Choice("Direksiyon", new[] { "Solda", "Ortada", "Sağda" }, () => (int)s.Wheel, v => s.Wheel = (WheelSide)v);
            Choice("Hız birimi", new[] { "km/sa", "m/sn" }, () => (int)s.Unit, v => s.Unit = (SpeedUnit)v);
            Choice("Görüntü", new[] { "Otomatik", "H.264", "JPEG" }, () => s.Video, v => s.Video = v);
            Choice("Korna sesi", new[] { "Düz", "Kalın", "Tiz", "Titrek" }, () => s.HornStyle, v => { s.HornStyle = v; drive.ApplySettings(); drive.TestHorn(); });
            Stepper("Tekerlek çapı", () => s.WheelDiameterCm.ToString("0.0").Replace('.', ',') + " cm",
                    () => s.WheelDiameterCm = Mathf.Max(2f, s.WheelDiameterCm - 0.5f), () => s.WheelDiameterCm = Mathf.Min(20f, s.WheelDiameterCm + 0.5f));
            Stepper("En yüksek güç", () => "%" + Mathf.RoundToInt(s.PowerLimit * 100f), () => drive.ChangePower(-0.05f), () => drive.ChangePower(0.05f));

            y -= 6f;
            Label(settingsRoot, "Bağlantı", font, 16, Text, TextAnchor.MiddleLeft, V(170f + 20f, y - r.y + 14f), V(340, 22));
            Button("ROBOTU BUL", new Rect(r.xMax - 236f, y, 120f, 28f), () => drive.Link.FindRobot(), null, () => !drive.Link.Demo, 14, TextAnchor.MiddleCenter, settingsRoot);
            Button("DEMO", new Rect(r.xMax - 110f, y, 90f, 28f), () => drive.Link.UseDemo(), null, () => drive.Link.Demo, 14, TextAnchor.MiddleCenter, settingsRoot);
            y -= 30f;
            settingsInfo = Label(settingsRoot, "", mono, 13, Dim, TextAnchor.UpperLeft, V(260f + 20f, y - r.y - 20f), V(520, 70));
            Label(settingsRoot, "W A S D sür · boşluk korna · L ışık · F selektör · H dörtlü · E motor · N müzik · R öne · B arkaya · P foto · V video · + − güç · O ayarlar",
                  font, 12, Dim, TextAnchor.MiddleCenter, V(280f, 22f), V(540, 16));
        }

        RectTransform Panel(string name, Rect r)
        {
            var rt = Rt(name, content, r.position, r.size);
            var bg = Shape("Zemin", rt);
            bg.RoundRect(new Rect(0, 0, r.width, r.height), 16f, Glass);
            Button("KAPAT", new Rect(r.xMax - 96f, r.yMax - 40f, 84f, 28f), () => drive.SettingsOpen = drive.MusicMenuOpen = false,
                   null, null, 14, TextAnchor.MiddleCenter, rt);
            rt.gameObject.SetActive(false);
            return rt;
        }

        // ---------------------------------------------------------------- her kare

        void Update()
        {
            if (root == null) return;
            if (dirty || Mathf.Abs(root.rect.width - W) > 0.5f || Key() != layoutKey) Rebuild();
            HandlePointers();

            var s = drive.Settings;
            var link = drive.Link;
            var model = drive.Model;

            video.Mode = (VideoMode)s.Video;
            if (wheelRt != null)
            {
                wheelAngle = Mathf.Lerp(wheelAngle, model.Steer * 100f, Mathf.Min(1f, Time.unscaledDeltaTime * 8f));
                wheelRt.localRotation = Quaternion.Euler(0f, 0f, -wheelAngle);
            }
            tachNeedle.localRotation = Quaternion.Euler(0f, 0f, -Angle(drive.Rpm / 1000f, 4f));
            float shown = s.Unit == SpeedUnit.KilometersPerHour ? drive.SpeedMs * 3.6f : drive.SpeedMs;
            speedNeedle.localRotation = Quaternion.Euler(0f, 0f, -Angle(shown, speedMax));
            menuRoot.gameObject.SetActive(menuOpen);
            speedText.text = s.FormatSpeed(drive.SpeedMs, out _);
            gearText.text = model.Gear < 0 ? "R" : model.Gear == 0 ? "N" : s.Gears ? model.Gear.ToString() : "D";
            gearText.color = model.Gear < 0 ? Red : Text;

            bool hazardBlink = drive.CurrentHazard && (int)(Time.unscaledTime / 0.5f) % 2 == 0;
            lampLight.color = drive.CurrentLed ? Amber : Dim * new Color(1, 1, 1, 0.5f);
            lampHazard.color = hazardBlink ? Accent : Dim * new Color(1, 1, 1, drive.CurrentHazard ? 0.9f : 0.5f);
            lampEngine.color = drive.CurrentEngine ? Green : Dim * new Color(1, 1, 1, 0.5f);

            int playing = drive.PlayingMelody;
            songText.text = playing > 0 && playing <= drive.Firmware.Songs.Count ? "Çalıyor:  " + drive.Firmware.Songs[playing - 1].Title : "";

            var st = link.Status;
            if (link.Demo) statusText.text = "DEMO  ·  robotsuz";
            else if (!link.IsConnected) statusText.text = link.Searching ? "robot aranıyor…" : "bağlantı yok";
            else statusText.text = $"ROBOT  ·  {link.RoundTripMs:0} ms" + (st != null && st.sinyal != 0 ? $"  ·  {st.sinyal} dBm {Bars(st.sinyal)}" : "") +
                                   (link.Loss > 0.05f ? $"  ·  kayıp %{link.Loss * 100f:0}" : "");
            infoText.text = $"{Mathf.RoundToInt(video.Fps)} fps" + (st != null && st.veri_kbps > 0 ? $"  ·  {st.veri_kbps / 1000f:0.0} Mbit/s" : "") +
                            $"  ·  güç %{Mathf.RoundToInt(s.PowerLimit * 100f)}  ·  {DateTime.Now:HH:mm}";
            statusText.color = !link.Demo && (link.IsWeak || !link.IsConnected) ? Red : Text;

            recText.text = drive.Recorder.Recording ? "KAYIT  " + TimeSpan.FromSeconds(drive.Recorder.RecordedSeconds).ToString(@"m\:ss") : "";
            toastText.text = Time.unscaledTime < drive.ToastUntil ? drive.Toast : "";
            warnText.text = !link.Demo && link.IsConnected && link.IsWeak ? "Bağlantı zayıf: robot durduruldu" : "";
            centerText.text = video.HasFrame ? "" : link.Demo ? "Kayıtlı görüntü yükleniyor" :
                              !link.IsConnected ? (string.IsNullOrEmpty(link.SearchText) ? "Robota bağlanılıyor" : Capital(link.SearchText)) : "Görüntü bekleniyor";

            musicRoot.gameObject.SetActive(drive.MusicMenuOpen);
            settingsRoot.gameObject.SetActive(drive.SettingsOpen);
            if (drive.SettingsOpen)
                settingsInfo.text = link.Demo ? "Robotsuz demo: kayıtlı 360° görüntü, sesler bu cihazdan çalar." :
                    $"adres {link.Host ?? "-"}   gidip gelme {link.RoundTripMs:0} ms   kayıp %{link.Loss * 100f:0}\n" +
                    $"Wi-Fi {(st != null ? st.sinyal + " dBm" : "-")}   görüntü {(st != null ? st.veri_kbps / 1000f : 0f):0.0} Mbit/s   atılan eski komut {(st != null ? st.atilan : 0)}\n" +
                    $"kayıtlar: {ScreenRecorder.Folder}";

            foreach (var b in buttons)
            {
                if (b.Back == null) continue;
                bool on = b.Lit != null && b.Lit() || b.Pointer != int.MinValue;
                // koyu cam: açık renkli görüntünün üstünde de okunur
                b.Back.color = on ? new Color(Accent.r * 0.42f, Accent.g * 0.42f, Accent.b * 0.42f, 0.86f) : ButtonOff;
                b.Label.color = on ? Accent : Text;
                if (b.Icon != null) b.Icon.color = b.Label.color;
                if (b.Dynamic != null) b.Label.text = b.Dynamic();
            }
            UpdateMirrors();
        }

        /// <summary>Wi-Fi sinyali için 4 çubuk: −55 dBm ve üstü dolu, −82 ve altı boş.</summary>
        static string Bars(int dbm)
        {
            int n = Mathf.Clamp(Mathf.CeilToInt((dbm + 82) / 7f), 0, 4);
            return new string('|', n) + new string('.', 4 - n);
        }

        static string Capital(string s) => s.Length > 0 ? char.ToUpper(s[0]) + s.Substring(1) : s;

        // ---------------------------------------------------------------- aynalar

        void MakeMirrorCameras()
        {
            Camera Make(string name, int w, int h, float fov, out RenderTexture rt)
            {
                rt = new RenderTexture(w, h, 16) { name = name };
                var cam = new GameObject(name, typeof(Camera)).GetComponent<Camera>();
                cam.transform.SetParent(drive.Viewer.ViewCamera.transform.parent, false);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.fieldOfView = fov;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 200f;
                cam.targetTexture = rt;
                cam.depth = -5;
                return cam;
            }
            rearCam = Make("İç ayna kamerası", 640, 164, 22f, out rearRt);
            leftCam = Make("Sol ayna kamerası", 200, 300, 52f, out leftRt);
            rightCam = Make("Sağ ayna kamerası", 200, 300, 52f, out rightRt);
        }

        void UpdateMirrors()
        {
            bool on = drive.Settings.Cockpit && drive.Settings.Mirrors;
            float yaw = drive.Viewer.Yaw;
            rearCam.enabled = on;
            leftCam.enabled = on;
            rightCam.enabled = on;
            if (!on) return;
            rearCam.transform.localRotation = Quaternion.Euler(3f, yaw + 180f, 0f);
            leftCam.transform.localRotation = Quaternion.Euler(6f, yaw - 152f, 0f);
            rightCam.transform.localRotation = Quaternion.Euler(6f, yaw + 152f, 0f);
        }

        // ---------------------------------------------------------------- dokunma / fare

        void HandlePointers()
        {
            var down = new Dictionary<int, Vector2>();
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed) down[-1] = mouse.position.ReadValue();
            var touch = Touchscreen.current;
            if (touch != null)
                foreach (var t in touch.touches)
                    if (t.press.isPressed) down[t.touchId.ReadValue()] = t.position.ReadValue();

            foreach (var b in buttons)
                if (b.Pointer != int.MinValue && !down.ContainsKey(b.Pointer))
                {
                    b.Pointer = int.MinValue;
                    b.Hold?.Invoke(false);
                }
            foreach (var id in new List<int>(looking.Keys))
                if (!down.ContainsKey(id)) looking.Remove(id);

            foreach (var kv in down)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(root, kv.Value, null, out var p);
                if (looking.TryGetValue(kv.Key, out var last))
                {
                    var d = p - last;
                    float k = drive.Viewer.ViewCamera.fieldOfView / H;
                    drive.Viewer.SetLook(drive.Viewer.Yaw - d.x * k, Mathf.Clamp(drive.Viewer.Pitch + d.y * k, -70f, 70f));
                    looking[kv.Key] = p;
                    continue;
                }
                bool owned = false;
                foreach (var b in buttons) if (b.Pointer == kv.Key) owned = true;
                if (owned) continue;
                bool pressedNow = kv.Key == -1 ? mouse.leftButton.wasPressedThisFrame : IsNewTouch(touch, kv.Key);
                if (!pressedNow) continue;
                Btn hit = null;
                foreach (var b in buttons)
                    if (b.Rt.gameObject.activeInHierarchy && Contains(b.Rt, p)) hit = b;
                if (hit != null)
                {
                    hit.Pointer = kv.Key;
                    hit.Press?.Invoke();
                    hit.Hold?.Invoke(true);
                }
                else if (drive.SettingsOpen || drive.MusicMenuOpen)
                {
                    var panel = drive.SettingsOpen ? settingsRoot : musicRoot;
                    if (!Contains(panel, p)) drive.SettingsOpen = drive.MusicMenuOpen = false;   // dışına dokununca kapan
                }
                else if (menuOpen && !Contains(menuRoot, p)) menuOpen = false;
                else if (p.y > 130f) looking[kv.Key] = p;
            }
        }

        static bool IsNewTouch(Touchscreen ts, int id)
        {
            if (ts == null) return false;
            foreach (var t in ts.touches) if (t.touchId.ReadValue() == id) return t.press.wasPressedThisFrame;
            return false;
        }

        bool Contains(RectTransform rt, Vector2 p)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            Vector2 a = root.InverseTransformPoint(corners[0]), b = root.InverseTransformPoint(corners[2]);
            return p.x >= a.x && p.x <= b.x && p.y >= a.y && p.y <= b.y;
        }

        // ---------------------------------------------------------------- yardımcılar

        void Button(string text, Rect r, Action press, Action<bool> hold, Func<bool> lit, int size = 13,
                    TextAnchor align = TextAnchor.MiddleCenter, RectTransform parent = null, Func<string> dynamic = null, string icon = null)
        {
            parent = parent != null ? parent : content;
            Vector2 origin = parent == content ? Vector2.zero : ((RectTransform)parent).anchoredPosition;
            var rt = Rt("Düğme " + text, parent, r.position - origin, r.size);
            float radius = Mathf.Min(12f, r.height / 2f);
            // ince açık çerçeve + koyu cam: açık renkli görüntünün üstünde de belli olur
            var border = Shape("Çerçeve", rt);
            border.RoundRect(new Rect(0, 0, r.width, r.height), radius, new Color(1f, 1f, 1f, 0.26f));
            var back = Shape("Zemin", rt);
            back.RoundRect(new Rect(1.5f, 1.5f, r.width - 3f, r.height - 3f), radius - 1.5f, Color.white);
            back.color = ButtonOff;
            var sheen = Shape("Parlaklık", rt);   // üst yarıda çok hafif açıklık: düğme kabarık görünür
            sheen.RoundRect(new Rect(3f, r.height * 0.5f, r.width - 6f, r.height * 0.5f - 3f), radius - 3f, new Color(1f, 1f, 1f, 0.05f));
            VectorShape ic = null;
            Text label;
            if (icon != null)
            {
                ic = Shape("Simge", rt);
                Icon(ic, icon, V(r.width / 2f, r.height / 2f + 10f), Mathf.Min(r.width, r.height) * 0.24f);
                label = Label(rt, text, font, size, Text, TextAnchor.MiddleCenter, V(r.width / 2f, 15f), V(r.width, 16));
            }
            else
                label = Label(rt, text, font, size, Text, align, align == TextAnchor.MiddleLeft ? V(r.width / 2f + 8f, r.height / 2f) : r.size / 2f,
                              align == TextAnchor.MiddleLeft ? r.size - V(16, 0) : r.size);
            buttons.Add(new Btn { Rt = rt, Label = label, Press = press, Hold = hold, Lit = lit, Back = back, Icon = ic, Dynamic = dynamic });
        }

        static readonly Color ButtonOff = new Color(0.03f, 0.035f, 0.04f, 0.72f);

        /// <summary>Basit çizgi simgeler (beyaz; düğme rengine boyanır). c merkez, r yarı boy.</summary>
        static void Icon(VectorShape s, string kind, Vector2 c, float r)
        {
            Color w = Color.white;
            float t = Mathf.Max(1.8f, r * 0.16f);
            switch (kind)
            {
                case "isik":
                case "selektor":
                    s.Arc(c + V(-r * 0.25f, 0), r * 0.55f - t, r * 0.55f, 180f, 360f, w);
                    s.Line(c + V(-r * 0.25f, -r * 0.55f), c + V(-r * 0.25f, r * 0.55f), t, w);
                    for (int i = -1; i <= 1; i++)
                    {
                        float tilt = kind == "selektor" ? i * 0.35f : 0f;
                        s.Line(c + V(r * 0.15f, i * r * 0.4f), c + V(r * 0.95f, i * r * 0.4f + tilt * r), t, w);
                    }
                    break;
                case "dortlu":
                    var a = c + V(0, r * 0.85f); var b = c + V(r * 0.95f, -r * 0.7f); var d = c + V(-r * 0.95f, -r * 0.7f);
                    s.Line(a, b, t, w); s.Line(b, d, t, w); s.Line(d, a, t, w);
                    break;
                case "motor":
                    s.Arc(c, r * 0.85f - t, r * 0.85f, -125f, 125f, w);
                    s.Line(c, c + V(r * 0.45f, r * 0.45f), t, w);
                    s.Circle(c, t * 0.9f, w, 12);
                    break;
                case "muzik":
                    s.Circle(c + V(-r * 0.35f, -r * 0.55f), r * 0.3f, w, 16);
                    s.Line(c + V(-r * 0.08f, -r * 0.55f), c + V(-r * 0.08f, r * 0.85f), t, w);
                    s.Line(c + V(-r * 0.08f, r * 0.85f), c + V(r * 0.65f, r * 0.45f), t, w);
                    break;
                case "foto":
                    s.Line(c + V(-r, -r * 0.6f), c + V(r, -r * 0.6f), t, w); s.Line(c + V(r, -r * 0.6f), c + V(r, r * 0.55f), t, w);
                    s.Line(c + V(r, r * 0.55f), c + V(-r, r * 0.55f), t, w); s.Line(c + V(-r, r * 0.55f), c + V(-r, -r * 0.6f), t, w);
                    s.Arc(c + V(0, -r * 0.02f), r * 0.32f - t, r * 0.32f, 0f, 360f, w);
                    break;
                case "video":
                    s.Arc(c, r * 0.8f - t, r * 0.8f, 0f, 360f, w);
                    s.Circle(c, r * 0.38f, w, 20);
                    break;
                case "guc":
                    s.Polygon(new[] { c + V(r * 0.2f, r * 0.95f), c + V(-r * 0.55f, -r * 0.05f), c + V(0, -r * 0.05f) }, w);
                    s.Polygon(new[] { c + V(0, r * 0.1f), c + V(r * 0.55f, r * 0.1f), c + V(-r * 0.2f, -r * 0.95f) }, w);
                    break;
                case "ayar":
                    s.Arc(c, r * 0.5f - t, r * 0.5f, 0f, 360f, w);
                    for (int i = 0; i < 8; i++)
                    {
                        float ang = i * Mathf.PI / 4f;
                        var dir = V(Mathf.Sin(ang), Mathf.Cos(ang));
                        s.Line(c + dir * r * 0.5f, c + dir * r * 0.85f, t * 1.4f, w);
                    }
                    break;
                case "korna":
                    s.Polygon(new[] { c + V(-r * 0.9f, -r * 0.25f), c + V(-r * 0.9f, r * 0.25f), c + V(r * 0.15f, r * 0.7f), c + V(r * 0.15f, -r * 0.7f) }, w);
                    s.Arc(c + V(r * 0.2f, 0), r * 0.45f - t, r * 0.45f, 45f, 135f, w);
                    s.Arc(c + V(r * 0.2f, 0), r * 0.85f - t, r * 0.85f, 50f, 130f, w);
                    break;
                case "menu":
                    for (int i = -1; i <= 1; i++) s.Line(c + V(-r * 0.8f, i * r * 0.5f), c + V(r * 0.8f, i * r * 0.5f), t, w);
                    break;
            }
        }

        VectorShape Shape(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(VectorShape));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = Vector2.zero;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var shape = go.GetComponent<VectorShape>();
            shape.raycastTarget = false;
            return shape;
        }

        static RectTransform Rt(string name, Transform parent, Vector2 pos, Vector2 size)
        {
            var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static RectTransform Stretch(string name, Transform parent)
        {
            var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>Metin; center = kutunun ortası (ebeveynin sol altına göre).</summary>
        Text Label(RectTransform parent, string text, Font f, int size, Color color, TextAnchor align, Vector2 center, Vector2 box)
        {
            var rt = Rt("Yazı", parent, center - box / 2f, box);
            var t = rt.gameObject.AddComponent<UnityEngine.UI.Text>();
            t.font = f != null ? f : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }

        static void UiShadow(Text t)
        {
            var s = t.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, 0.9f);
            s.effectDistance = new Vector2(1.5f, -1.5f);
        }

        static Vector2 V(float x, float y) => new Vector2(x, y);
        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        void OnDestroy()
        {
            foreach (var rt in new[] { rearRt, leftRt, rightRt }) if (rt != null) rt.Release();
        }
    }
}
