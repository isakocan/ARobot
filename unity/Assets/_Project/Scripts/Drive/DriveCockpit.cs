using System;
using System.Collections.Generic;
using ARaki.Video;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ARaki.Drive
{
    /// <summary>
    /// Sürüş ekranı: simülasyon oyunlarındaki gibi araç içi görünüm. Altta göbeği görünen direksiyon (göbeğe
    /// basınca korna, manuel viteste arkasında vites kulakçıkları), yanında siperlikli gösterge kutusu (devir,
    /// vites, hız), onun yanında panel düğmeleri (ışık, selektör, dörtlü, korna); hepsi tek parça bir ön panelin
    /// üstünde. Üstte dikiz aynası (360° görüntüden ayrı kamerayla), sağ üstte menü, şarkı paneli ve ayarlar. Dokunmatikte
    /// direksiyonun karşı yanında yön tuşu ya da pedallar. Her şey koddan çizilir (VectorShape); ayar değişince
    /// yeniden kurulur.
    ///
    /// Dokunmatik ve fare girişi burada çözülür (birden çok parmak: gaz basılıyken dönülebilir);
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
        static readonly Color ButtonOff = new Color(0.03f, 0.035f, 0.04f, 0.72f);
        static readonly Color ButtonOn = new Color(Accent.r * 0.42f, Accent.g * 0.42f, Accent.b * 0.42f, 0.86f);

        enum ButtonStyle { Normal, Switch, Pedal, Paddle, Invisible }

        class Btn
        {
            public RectTransform Rt;
            public Text Label;
            public Action Press;
            public Action<bool> Hold;
            public Func<bool> Lit, Held;
            public Graphic Back;
            public VectorShape Icon, Glow, Led;
            public Color OffColor = ButtonOff, OnColor = ButtonOn, LedColor = Green;
            public Color? IconTint;
            public Func<string> Dynamic;
            public ButtonStyle Style;
            public int Pointer = int.MinValue;
        }

        /// <summary>Dokunmatik yön tuşu: 8 yön (çapraz = gaz/fren + dönüş), başparmak noktası parmağı izler.</summary>
        class DPad
        {
            public Vector2 Center;
            public float Radius;
            public VectorShape[] Wedges;
            public RectTransform Knob;
            public int Pointer = int.MinValue;
        }

        Canvas canvas;
        RectTransform root, content;
        float W;
        readonly List<Btn> buttons = new List<Btn>();
        readonly Dictionary<int, Vector2> looking = new Dictionary<int, Vector2>();
        bool dirty = true;
        string layoutKey = "";
        DPad dpad;

        RectTransform wheelRt, hubRt, tachNeedle, speedNeedle, menuRoot, settingsRoot, musicRoot;
        VectorShape hubPress;
        float speedMax;
        bool menuOpen;
        public bool MenuOpen { get => menuOpen; set => menuOpen = value; }
        Text gearText, gearModeText, speedText, statusText, infoText, toastText, centerText, songText, recText, warnText;
        Text settingsInfo;
        VectorShape lampLight, lampHazard, lampLink;
        float wheelAngle;

        Camera rearCam;
        RenderTexture rearRt;
        RawImage rearImg;

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
            dpad = null;
            Canvas.ForceUpdateCanvases();
            W = root.rect.width;
            content = Stretch("İçerik", root);
            content.pivot = Vector2.zero;
            var s = drive.Settings;

            // Düzen (kullanıcının seçimi): altta göbeği görünen direksiyon, yanında arabalardaki gibi siperlikli
            // gösterge kutusu, onun yanında panel anahtarları; üstte dikiz aynası ve iki yanında bilgi satırları,
            // dikey yan aynalar kenarlarda orta yükseklikte; diğer düğmeler sağ üstte MENÜ'nün arkasında.
            float wheelX = s.Wheel == WheelSide.Left ? W / 2f - 380f : s.Wheel == WheelSide.Right ? W / 2f + 380f : W / 2f - 150f;
            float clusterX = !s.Cockpit ? W / 2f : s.Wheel == WheelSide.Right ? wheelX - 360f : wheelX + 360f;
            bool switchesLeft = s.Cockpit && s.Wheel == WheelSide.Right;   // anahtarlar kutunun direksiyona uzak yanında
            const float wheelY = 64f, clusterY = 68f, dialR = 50f, dialDx = 84f;

            // panel düğmeleri gösterge kutusunun direksiyona uzak yanında; ön panel direksiyondan düğmelerin sonuna uzanır
            const float sw = 58f, sh = 66f, sg = 8f, swCount = 4;
            float swWidth = swCount * sw + (swCount - 1) * sg;
            float sx = switchesLeft ? clusterX - 172f - 16f - swWidth : clusterX + 172f + 16f;
            float wheelSide = s.Cockpit ? (s.Wheel == WheelSide.Right ? wheelX + 172f : wheelX - 172f) : (switchesLeft ? clusterX + 230f : clusterX - 230f);
            float dashFrom = switchesLeft ? sx - 22f : wheelSide, dashTo = switchesLeft ? wheelSide : sx + swWidth + 22f;
            var body = Shape("Ön panel", content);
            Dashboard(body, clusterX, dashFrom, dashTo, switchesLeft, sx - 10f, sx + swWidth + 10f);

            if (s.Cockpit)
            {
                var wheel = Shape("Direksiyon", content);
                wheelRt = (RectTransform)wheel.transform;
                wheelRt.anchorMin = wheelRt.anchorMax = Vector2.zero;
                wheelRt.anchoredPosition = V(wheelX, wheelY);
                wheelRt.sizeDelta = Vector2.zero;
                SteeringWheel(wheel);
                var hub = Shape("Göbek", wheelRt);
                hubRt = (RectTransform)hub.transform;
                HubCover(hub);
                hubPress = Shape("Göbek basılı", hubRt);
                hubPress.RoundRect(new Rect(-45f, -34f, 90f, 64f), 22f, Color.white);
                hubPress.color = new Color(1f, 1f, 1f, 0f);
                // korna: gerçek araçtaki gibi göbeğe basılır
                Button("", new Rect(wheelX - 52f, wheelY - 40f, 104f, 80f), null, held => drive.TouchHorn = held, null,
                       style: ButtonStyle.Invisible);
                if (s.Gearbox == GearMode.Manual)
                {
                    Button("−", new Rect(wheelX - 154f - 32f, wheelY - 40f, 26f, 84f), drive.ShiftDown, null, null, 22, style: ButtonStyle.Paddle);
                    Button("+", new Rect(wheelX + 154f + 6f, wheelY - 40f, 26f, 84f), drive.ShiftUp, null, null, 22, style: ButtonStyle.Paddle);
                }
            }
            else
            {
                wheelRt = hubRt = null;
                hubPress = null;
                if (s.Gearbox == GearMode.Manual)
                {
                    float px = switchesLeft ? clusterX + 172f + 12f : clusterX - 172f - 44f;
                    Button("+", new Rect(px, 70f, 32f, 50f), drive.ShiftUp, null, null, 22, style: ButtonStyle.Paddle);
                    Button("−", new Rect(px, 14f, 32f, 50f), drive.ShiftDown, null, null, 22, style: ButtonStyle.Paddle);
                }
            }

            // ibreli saatler, ortada vites; hız göstergesinin sonu en yüksek güçteki hız
            Dial(body, V(clusterX - dialDx, clusterY), dialR, 4f, 1f, 3.4f, out tachNeedle);
            float top = Mathf.Max(0.05f, s.TopSpeedShown);
            float step = NiceStep(top * 1.04f);
            speedMax = Mathf.Ceil(top * 1.04f / step - 0.001f) * step;
            Dial(body, V(clusterX + dialDx, clusterY), dialR, speedMax, step, -1f, out speedNeedle);
            body.RoundRect(new Rect(clusterX - 23f, clusterY - 30f, 46f, 62f), 7f, new Color(1f, 1f, 1f, 0.1f));
            body.RoundRect(new Rect(clusterX - 22f, clusterY - 29f, 44f, 60f), 6f, Hex("#0B100F"));
            gearText = Label(content, "N", mono, 32, Text, TextAnchor.MiddleCenter, V(clusterX, clusterY + 4f), V(44, 40));
            gearModeText = Label(content, "", font, 9, Dim, TextAnchor.MiddleCenter, V(clusterX, clusterY - 21f), V(44, 10));
            // dijital hız ibre göbeğinin üstünde, birim altında: kadranın alt uçlarındaki rakamlarla çakışmaz
            speedText = Label(content, "0,0", mono, 14, Text, TextAnchor.MiddleCenter, V(clusterX + dialDx, clusterY + 15f), V(48, 16));
            Label(content, s.Unit == SpeedUnit.KilometersPerHour ? "km/sa" : "m/sn", font, 9, Dim, TextAnchor.MiddleCenter,
                  V(clusterX + dialDx, clusterY - 15f), V(30, 10));
            Label(content, "x1000", font, 9, Dim, TextAnchor.MiddleCenter, V(clusterX - dialDx, clusterY - 15f), V(30, 10));
            // uyarı lambaları siperliğin altında, saatlerin arasında: ışık, dörtlü, bağlantı
            lampLight = Lamp("isik", V(clusterX - 24f, clusterY + 64f));
            lampHazard = Lamp("dortlu", V(clusterX, clusterY + 64f));
            lampLink = Lamp("ag", V(clusterX + 24f, clusterY + 64f));

            // panel düğmeleri (dörtlü, arabalardaki gibi kırmızı üçgen; korna basılı tutulur)
            {
                float y0 = 20f;
                Button("IŞIK", new Rect(sx, y0, sw, sh), drive.ToggleLight, null, () => drive.CurrentLed, 10, icon: "isik", style: ButtonStyle.Switch);
                Button("SELEKTÖR", new Rect(sx + sw + sg, y0, sw, sh), drive.Flash, null, null, 10, icon: "selektor", style: ButtonStyle.Switch);
                var hz = Button("DÖRTLÜ", new Rect(sx + 2 * (sw + sg), y0, sw, sh), drive.ToggleHazard, null, () => drive.CurrentHazard, 10,
                                icon: "dortlu", style: ButtonStyle.Switch);
                hz.IconTint = Red;
                hz.LedColor = Red;
                var horn = Button("KORNA", new Rect(sx + 3 * (sw + sg), y0, sw, sh), null, held => drive.TouchHorn = held, null, 10,
                                  icon: "korna", style: ButtonStyle.Switch);
                horn.Held = () => drive.Link.State.Horn;
                horn.LedColor = Amber;
                horn.Lit = () => drive.Link.State.Horn;
            }

            // MENÜ sağ üstte; açılınca altında 3 + 2 simgeli düğme
            const float bw = 100f, bh = 78f, gap = 8f, mb = 64f;
            float mw = 3 * bw + 4 * gap, mh = 2 * bh + 3 * gap;
            float mbX = W - 24f - mb, mbY = H - 24f - mb;
            Button("MENÜ", new Rect(mbX, mbY, mb, mb), () => { menuOpen = !menuOpen; drive.MusicMenuOpen = drive.SettingsOpen = false; },
                   null, () => menuOpen, 11, icon: "menu");
            float menuX = W - 24f - mw, menuY = mbY - gap - mh;
            songText = Label(content, "", font, 14, Text, TextAnchor.MiddleRight, V(mbX - 12f - 170f, mbY + mb / 2f - 22f), V(340, 18));
            UiShadow(songText);
            menuRoot = Rt("Menü", content, V(menuX, menuY), V(mw, mh));
            var menuBg = Shape("Zemin", menuRoot);
            menuBg.RoundRect(new Rect(0, 0, mw, mh), 16f, new Color(0.03f, 0.035f, 0.04f, 0.82f));
            string[] names = { "MÜZİK", "FOTO", "VİDEO", "DİKİZ", "AYARLAR" };
            string[] icons = { "muzik", "foto", "video", "dikiz", "ayar" };
            Action[] acts = { () => { drive.MusicMenuOpen = !drive.MusicMenuOpen; drive.SettingsOpen = false; menuOpen = false; },
                              drive.Photo, drive.Video, drive.ToggleRearMirror,
                              () => { drive.SettingsOpen = !drive.SettingsOpen; drive.MusicMenuOpen = false; menuOpen = false; } };
            Func<bool>[] lits = { () => drive.MusicMenuOpen, null, () => drive.Recorder.Recording,
                                  () => drive.Settings.RearMirror, () => drive.SettingsOpen };
            for (int i = 0; i < names.Length; i++)
            {
                int col = i % 3, row = i / 3;
                Button(names[i], new Rect(menuX + gap + col * (bw + gap), menuY + mh - gap - bh - row * (bh + gap), bw, bh), acts[i], null, lits[i], 12,
                       TextAnchor.MiddleCenter, menuRoot, icon: icons[i]);
            }
            menuRoot.gameObject.SetActive(false);

            // dikiz aynası üstte, tavana sapla bağlı
            const float rearW = 344f, rearH = 81f, rearTop = 30f;
            rearImg = s.Cockpit && s.RearMirror ? RearMirror(new Rect(W / 2f - rearW / 2f, H - rearTop - rearH, rearW, rearH)) : null;

            // durum ve bildirimler
            // üst bilgi: dikiz aynasının solunda bağlantı, sağında görüntü / anlık güç / sıcaklık / saat, altında kayıt
            float side = rearW / 2f + 14f;
            statusText = Label(content, "", mono, 14, Text, TextAnchor.MiddleRight, V(W / 2f - side - 170f, H - 44f), V(340, 18));
            UiShadow(statusText);
            infoText = Label(content, "", mono, 14, Text, TextAnchor.MiddleLeft, V(W / 2f + side + 170f, H - 44f), V(340, 18));
            UiShadow(infoText);
            recText = Label(content, "", font, 16, Red, TextAnchor.MiddleLeft, V(W / 2f + side + 170f, H - 66f), V(340, 18));
            UiShadow(recText);
            toastText = Label(content, "", font, 20, Text, TextAnchor.MiddleCenter, V(W / 2f, H - 140f), V(700, 30));
            UiShadow(toastText);
            warnText = Label(content, "", font, 22, Red, TextAnchor.MiddleCenter, V(W / 2f, H / 2f + 120f), V(800, 30));
            UiShadow(warnText);
            centerText = Label(content, "", font, 24, Text, TextAnchor.MiddleCenter, V(W / 2f, H / 2f + 40f), V(900, 34));
            UiShadow(centerText);

            // dokunmatik sürüş: direksiyonun karşı yanında yön tuşu; direksiyon ortadaysa (ya da pedal seçiliyse)
            // bir yanda oklar, öbür yanda gaz ve fren pedalı
            if (s.Touch != TouchMode.Off)
            {
                bool wheelOnRight = s.Cockpit && s.Wheel == WheelSide.Right;
                if (s.Touch == TouchMode.Pad && !(s.Cockpit && s.Wheel == WheelSide.Center))
                {
                    const float r = 108f;
                    BuildPad(V(wheelOnRight ? 24f + r + 6f : W - 24f - r - 6f, 20f + r + 6f), r);
                }
                else
                {
                    bool pedalsRight = !wheelOnRight;
                    var gas = pedalsRight ? new Rect(W - 24f - 76f, 16f, 76f, 150f) : new Rect(24f, 16f, 76f, 150f);
                    var brake = pedalsRight ? new Rect(gas.x - 14f - 100f, 16f, 100f, 112f) : new Rect(gas.xMax + 14f, 16f, 100f, 112f);
                    var g = Button("GAZ", gas, null, h => drive.TouchForward = h ? 1 : (drive.TouchForward == 1 ? 0 : drive.TouchForward), null, 11,
                                   style: ButtonStyle.Pedal);
                    g.Held = () => drive.InputForward > 0;
                    var b = Button("FREN", brake, null, h => drive.TouchForward = h ? -1 : (drive.TouchForward == -1 ? 0 : drive.TouchForward), null, 11,
                                   style: ButtonStyle.Pedal);
                    b.Held = () => drive.InputForward < 0;
                    float ax = pedalsRight ? 24f : W - 24f - 2 * 84f - 10f;
                    float ay = s.Cockpit && s.Wheel != WheelSide.Center ? 196f : 24f;   // direksiyon o yandaysa onun üstünde
                    var l = Button("", new Rect(ax, ay, 84f, 84f), null, h => drive.TouchTurn = h ? -1 : (drive.TouchTurn == -1 ? 0 : drive.TouchTurn), null,
                                   icon: "sol");
                    l.Held = () => drive.InputTurn < 0;
                    var rr = Button("", new Rect(ax + 94f, ay, 84f, 84f), null, h => drive.TouchTurn = h ? 1 : (drive.TouchTurn == 1 ? 0 : drive.TouchTurn), null,
                                    icon: "sag");
                    rr.Held = () => drive.InputTurn > 0;
                }
            }

            menuRoot.SetAsLastSibling();   // menü açılınca aynaların ve düğmelerin üstünde kalsın
            BuildMusicMenu();
            BuildSettings();
            layoutKey = Key();
            dirty = false;
        }

        string Key()
        {
            var s = drive.Settings;
            return W + "|" + s.Wheel + s.Cockpit + s.RearMirror + s.Touch + s.Unit + s.Gearbox + s.TopSpeedShown.ToString("0.000");
        }

        /// <summary>Gösterge rakamları en çok 5 tane olsun (küçük kadranda sıkışmasın) diye adım.</summary>
        static float NiceStep(float max)
        {
            foreach (float c in new[] { 0.1f, 0.2f, 0.5f, 1f, 2f })
                if (max / c <= 4f) return c;
            return 5f;
        }

        void Dial(VectorShape body, Vector2 c, float r, float max, float step, float redFrom, out RectTransform needle)
        {
            // ince metal çerçeve, içe doğru koyulaşan kadran, üstte hafif cam yansıması
            body.Arc(c, r + 1.5f, r + 4f, 0f, 360f, new Color(0.62f, 0.65f, 0.69f, 0.55f), 96);
            body.Arc(c, r, r + 1.5f, 0f, 360f, new Color(0f, 0f, 0f, 0.9f), 96);
            body.Disc(c, r, Hex("#191C20"), Hex("#08090B"), 64);
            if (redFrom > 0f) body.Arc(c, r - 5f, r - 1.5f, Angle(redFrom, max), Angle(max, max), Red);
            string format = step < 0.1f || Mathf.Abs(step - 0.25f) < 0.001f ? "0.00" : step < 1f ? "0.0" : "0";
            for (float v = 0f; v <= max + 0.001f; v += step / 4f)
            {
                float q = v / step;
                bool major = Mathf.Abs(q - Mathf.Round(q)) < 0.01f, half = !major && Mathf.Abs(q * 2f - Mathf.Round(q * 2f)) < 0.01f;
                float a = Angle(v, max) * Mathf.Deg2Rad;
                var d = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                float len = major ? 8f : half ? 5f : 3f;
                body.Line(c + d * (r - 1.5f - len), c + d * (r - 1.5f), major ? 2f : 1f, major ? Text : Dim);
                if (major)
                    Label(content, v.ToString(format).Replace('.', ','), mono, format == "0.00" ? 9 : 11, Text,
                          TextAnchor.MiddleCenter, c + d * (r - 17f), V(28, 14));
            }
            body.Arc(c, r - 3f, r - 1.5f, -55f, 55f, new Color(1f, 1f, 1f, 0.07f));
            var n = Shape("İbre", content);
            needle = (RectTransform)n.transform;
            needle.anchorMin = needle.anchorMax = Vector2.zero;
            needle.anchoredPosition = c;
            needle.sizeDelta = Vector2.zero;
            n.Polygon(new[] { V(-3.2f, -11f), V(-1f, r - 7f), V(1f, r - 7f), V(3.2f, -11f) }, Accent);
            n.Disc(Vector2.zero, 7f, Hex("#3A3F45"), Hex("#16181B"), 24);
            n.Arc(Vector2.zero, 7f, 8f, 0f, 360f, new Color(1f, 1f, 1f, 0.18f), 24);
        }

        static float Angle(float v, float max) => -130f + 260f * Mathf.Clamp01(v / max);

        /// <summary>
        /// Tek parça ön panel: direksiyonun arkasından düğmelerin sonuna uzanan koyu torpido. Gösterge kutusunun
        /// üstü kemerli siperlik gibi yükselir, düğmelerin olduğu yerde raf, direksiyon tarafında alçak kenar;
        /// geçişler yumuşak. Üst kenarında ince ışık, saatlerin ve düğmelerin oturduğu çukur yüzler.
        /// </summary>
        void Dashboard(VectorShape b, float cx, float x0, float x1, bool switchesLeft, float swFrom, float swTo)
        {
            const float half = 172f, edge = 112f, crown = 156f, shelf = 100f, low = 44f, fade = 34f;
            float Hood(float x)
            {
                float u = (x - cx) / half;
                return Mathf.Abs(u) >= 1f ? 0f : edge + (crown - edge) * Mathf.Sqrt(1f - u * u) + 6f * (1f - Mathf.Abs(u));
            }
            float Shelf(float x)
            {
                bool switchSide = switchesLeft ? x < cx : x > cx;
                float h = switchSide ? shelf : low;
                float t = Mathf.Min(Mathf.Clamp01((x - x0) / fade), Mathf.Clamp01((x1 - x) / fade));
                return h * Mathf.SmoothStep(0f, 1f, t);
            }
            // yumuşak en büyük: siperlik ile raf arasındaki köşe yuvarlanır
            float Top(float x)
            {
                float a = Hood(x), c = Shelf(x);
                const float k = 7f;
                float m = Mathf.Max(a, c);
                return m + k * Mathf.Log(Mathf.Exp((a - m) / k) + Mathf.Exp((c - m) / k));
            }
            var top = new List<Vector2>();
            for (float x = x0; x < x1; x += 6f) top.Add(V(x, Top(x)));
            top.Add(V(x1, Top(x1)));
            b.Band(top, 0f, new Color(0.11f, 0.115f, 0.125f, 0.96f), new Color(0.02f, 0.022f, 0.025f, 0.98f));
            for (int i = 0; i < top.Count - 1; i++)
                if (top[i].y > 2f) b.Line(top[i], top[i + 1], 1.6f, new Color(1f, 1f, 1f, 0.15f));
            // saatlerin çukuru ve düğme yuvası
            b.RoundRect(new Rect(cx - 158f, 6f, 316f, 118f), 22f, new Color(0f, 0f, 0f, 0.55f));
            b.RoundRect(new Rect(cx - 156f, 8f, 312f, 114f), 20f, new Color(0.012f, 0.014f, 0.016f, 0.92f));
            b.RoundRect(new Rect(swFrom, 10f, swTo - swFrom, 84f), 14f, new Color(0f, 0f, 0f, 0.5f));
            b.RoundRect(new Rect(swFrom + 2f, 12f, swTo - swFrom - 4f, 80f), 12f, new Color(0.015f, 0.017f, 0.02f, 0.9f));
        }

        /// <summary>Spor direksiyon: deri simit (yanlarda dikişli tutamaklar), üç kollu gövde. Göbek ayrı çizilir.</summary>
        void SteeringWheel(VectorShape w)
        {
            const float rIn = 134f, rOut = 154f, hub = 38f;
            Color rim = Hex("#16181B"), grip = Hex("#1E2125"), spoke = Hex("#202328");
            w.Arc(Vector2.zero, rIn, rOut, 0f, 360f, rim, 160);
            foreach (float g in new[] { 90f, 270f })   // tutamaklar: biraz kalın ve açık
            {
                w.Arc(Vector2.zero, rIn - 2.5f, rOut + 2.5f, g - 32f, g + 32f, grip, 40);
                for (float a = g - 28f; a <= g + 28.1f; a += 4f)   // dikiş
                {
                    float ra = a * Mathf.Deg2Rad;
                    var d = V(Mathf.Sin(ra), Mathf.Cos(ra));
                    var t = V(d.y, -d.x);
                    var p = d * (rIn + rOut) * 0.5f;
                    w.Line(p - t * 1.4f, p + t * 1.4f, 1f, new Color(1f, 1f, 1f, 0.22f));
                }
            }
            w.Arc(Vector2.zero, rOut - 3f, rOut + 0.5f, -70f, 70f, new Color(1f, 1f, 1f, 0.12f), 60);   // üstten ışık
            w.Arc(Vector2.zero, rIn, rIn + 2f, 0f, 360f, new Color(0f, 0f, 0f, 0.55f), 160);
            w.Arc(Vector2.zero, rIn, rOut, -3f, 3f, Accent);                                              // tepe işareti

            void Spoke(float deg, float w0, float w1)
            {
                float ra = deg * Mathf.Deg2Rad;
                var d = V(Mathf.Sin(ra), Mathf.Cos(ra));
                var n = V(d.y, -d.x);
                Vector2 a = d * (hub - 4f), b = d * (rIn + 2f);
                w.Polygon(new[] { a + n * w0, b + n * w1, b - n * w1, a - n * w0 }, spoke);
                w.Line(a + n * w0, b + n * w1, 1.2f, new Color(1f, 1f, 1f, 0.08f));
            }
            Spoke(90f, 22f, 12f);
            Spoke(270f, 22f, 12f);
            Spoke(166f, 9f, 6f);
            Spoke(194f, 9f, 6f);
            foreach (float x in new[] { -66f, 66f })   // kollardaki düğmeler
            {
                w.Disc(V(x, 0f), 6.5f, Hex("#30353B"), Hex("#1A1D21"), 20);
                w.Arc(V(x, 0f), 6.5f, 7.5f, 0f, 360f, new Color(1f, 1f, 1f, 0.15f), 20);
            }
        }

        /// <summary>Hava yastığı kapağı (korna): basınca hafifçe içeri girer ve aydınlanır.</summary>
        static void HubCover(VectorShape h)
        {
            const float hub = 38f;
            h.RoundRect(new Rect(-hub - 9f, -hub + 2f, 2f * hub + 18f, 2f * hub - 8f), 24f, new Color(0f, 0f, 0f, 0.6f));
            h.RoundRect(new Rect(-hub - 7f, -hub + 4f, 2f * hub + 14f, 2f * hub - 12f), 22f, Hex("#1D2024"));
            h.RoundRect(new Rect(-hub - 3f, 0f, 2f * hub + 6f, hub - 12f), 18f, new Color(1f, 1f, 1f, 0.04f));
            h.Arc(V(0f, -1f), 8f, 10f, 0f, 360f, new Color(Accent.r, Accent.g, Accent.b, 0.8f), 32);
        }

        VectorShape Lamp(string icon, Vector2 c)
        {
            var l = Shape("Lamba " + icon, content);
            Icon(l, icon, c, 8f);
            return l;
        }

        void BuildPad(Vector2 c, float r)
        {
            var bg = Shape("Yön tuşu", content);
            bg.Disc(c, r + 3f, new Color(1f, 1f, 1f, 0.22f), new Color(1f, 1f, 1f, 0.22f), 72);
            bg.Disc(c, r + 1.5f, new Color(0.07f, 0.075f, 0.085f, 0.8f), new Color(0.02f, 0.022f, 0.026f, 0.86f), 72);
            bg.Arc(c, r - 2f, r, -60f, 60f, new Color(1f, 1f, 1f, 0.08f), 48);   // üstten ışık
            var wedges = new VectorShape[4];
            float[] at = { 0f, 90f, 180f, 270f };   // yukarı = gaz, sağ, aşağı = fren, sol
            for (int i = 0; i < 4; i++)
            {
                var w = Shape("Yön " + i, content);
                w.Arc(c, r * 0.34f, r - 5f, at[i] - 41f, at[i] + 41f, Color.white, 48);
                w.color = new Color(1f, 1f, 1f, 0.05f);
                wedges[i] = w;
                float ra = at[i] * Mathf.Deg2Rad;
                var d = V(Mathf.Sin(ra), Mathf.Cos(ra));
                var n = V(d.y, -d.x);
                var arrow = Shape("Ok " + i, content);
                Vector2 tip = c + d * (r * 0.88f), b0 = c + d * (r * 0.7f);
                arrow.Polygon(new[] { tip, b0 + n * (r * 0.12f), b0 - n * (r * 0.12f) }, new Color(1f, 1f, 1f, 0.85f));
            }
            Label(content, "GAZ", font, 11, Dim, TextAnchor.MiddleCenter, c + V(0, r * 0.52f), V(50, 14));
            Label(content, "FREN", font, 11, Dim, TextAnchor.MiddleCenter, c - V(0, r * 0.52f), V(50, 14));
            var knob = Shape("Başparmak", content);
            var krt = (RectTransform)knob.transform;
            krt.anchorMin = krt.anchorMax = Vector2.zero;
            krt.anchoredPosition = c;
            krt.sizeDelta = Vector2.zero;
            knob.Disc(Vector2.zero, r * 0.26f, Hex("#3A3F46"), Hex("#1B1E22"), 40);
            knob.Arc(Vector2.zero, r * 0.26f, r * 0.26f + 1.5f, 0f, 360f, new Color(1f, 1f, 1f, 0.25f), 40);
            dpad = new DPad { Center = c, Radius = r, Wedges = wedges, Knob = krt };
        }

        void PadMove(Vector2 p)
        {
            var v = p - dpad.Center;
            float m = v.magnitude;
            int f = 0, t = 0;
            if (m > dpad.Radius * 0.16f)   // sin 22,5° ≈ 0,38: 8 eşit yön
            {
                f = v.y > m * 0.38f ? 1 : v.y < -m * 0.38f ? -1 : 0;
                t = v.x > m * 0.38f ? 1 : v.x < -m * 0.38f ? -1 : 0;
            }
            drive.TouchForward = f;
            drive.TouchTurn = t;
            dpad.Knob.anchoredPosition = dpad.Center + Vector2.ClampMagnitude(v, dpad.Radius * 0.5f);
        }

        void PadRelease()
        {
            dpad.Pointer = int.MinValue;
            drive.TouchForward = drive.TouchTurn = 0;
        }

        /// <summary>
        /// Dikiz aynası: tavana giden sap, kalın plastik çerçeve, yuvarlak köşeli cam (görüntü maskelenir),
        /// camda hafif çapraz yansıma.
        /// </summary>
        RawImage RearMirror(Rect r)
        {
            var frame = Shape("Dikiz aynası", content);
            frame.RoundRect(new Rect(W / 2f - 10f, r.yMax - 6f, 20f, H - r.yMax + 12f), 6f, Hex("#121417"));
            frame.RoundRect(new Rect(W / 2f - 16f, r.yMax + 2f, 32f, 14f), 5f, Hex("#1A1D21"));
            frame.RoundRect(new Rect(r.x - 8f, r.y - 8f, r.width + 16f, r.height + 16f), 28f, new Color(0f, 0f, 0f, 0.55f));
            frame.RoundRect(new Rect(r.x - 6f, r.y - 6f, r.width + 12f, r.height + 12f), 26f, Hex("#16181B"));
            frame.RoundRect(new Rect(r.x - 5f, r.y + r.height * 0.5f, r.width + 10f, r.height * 0.5f + 5f), 25f, new Color(1f, 1f, 1f, 0.04f));
            frame.RoundRect(new Rect(r.x - 1.5f, r.y - 1.5f, r.width + 3f, r.height + 3f), 21.5f, new Color(0f, 0f, 0f, 0.95f));

            var glass = Shape("Ayna camı", content);
            glass.Feather = 0f;
            glass.RoundRect(r, 20f, Color.white);
            glass.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var img = new GameObject("Dikiz görüntü", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            var irt = (RectTransform)img.transform;
            irt.SetParent(glass.transform, false);
            irt.anchorMin = irt.anchorMax = Vector2.zero;
            irt.pivot = Vector2.zero;
            irt.anchoredPosition = r.position;
            irt.sizeDelta = r.size;
            img.texture = rearRt;
            img.uvRect = new Rect(1f, 0f, -1f, 1f);   // ayna: soldan sağa ters
            img.raycastTarget = false;
            var glare = Shape("Yansıma", glass.transform);
            glare.Feather = 6f;
            glare.Polygon(new[] { V(r.x + r.width * 0.16f, r.yMax), V(r.x + r.width * 0.3f, r.yMax),
                                  V(r.x + r.width * 0.22f, r.y), V(r.x + r.width * 0.08f, r.y) }, new Color(1f, 1f, 1f, 0.06f));
            glare.RoundRect(new Rect(r.x, r.yMax - 10f, r.width, 10f), 6f, new Color(1f, 1f, 1f, 0.04f));
            return img;
        }

        void BuildMusicMenu()
        {
            var songs = drive.Firmware.Songs;
            const int perColumn = 14;
            int lines = songs.Count;
            string g0 = null;
            foreach (var so in songs) if (so.Group != g0) { g0 = so.Group; if (!string.IsNullOrEmpty(g0)) lines++; }
            int columns = Mathf.Max(2, Mathf.CeilToInt(lines / (float)perColumn));
            float pw = 24f + columns * 260f, ph = 430f;
            var pr = new Rect(W / 2f - pw / 2f, H / 2f - 200f, pw, ph);
            musicRoot = Panel("Müzik", pr);
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
                              V(24f + column * 260f + 110f, ph - 60f - line * 25f), V(220, 18));
                        Next();
                    }
                }
                int n = i + 1;
                Button(songs[i].Title, new Rect(pr.x + 16f + column * 260f, pr.y + ph - 72f - line * 25f, 250f, 23f),
                       () => drive.PlayMelody(n), null, () => drive.PlayingMelody == n, 15, TextAnchor.MiddleLeft, musicRoot);
                Next();
            }
            Label(musicRoot, "ŞARKILAR  ·  buzzer'dan çalar", font, 16, Text, TextAnchor.MiddleLeft, V(180f, ph - 24f), V(330, 22));
            Button("DURDUR", new Rect(pr.xMax - 196f, pr.y + ph - 40f, 92f, 28f), drive.StopMelody, null, null, 14, TextAnchor.MiddleCenter, musicRoot);
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

            Choice("Vites", new[] { "Otomatik", "Manuel", "Vitessiz" }, () => (int)s.Gearbox, v => s.Gearbox = (GearMode)v);
            Toggle("Geri vites bipi", () => s.ReverseBeep, v => s.ReverseBeep = v);
            Toggle("Kamera hareketi", () => s.CameraMotion, v => s.CameraMotion = v);
            Toggle("Kokpit", () => s.Cockpit, v => s.Cockpit = v);
            Toggle("Dikiz aynası", () => s.RearMirror, v => s.RearMirror = v);
            Choice("Dokunmatik sürüş", new[] { "Kapalı", "Yön tuşu", "Pedal" }, () => (int)s.Touch, v => s.Touch = (TouchMode)v);
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
            Label(settingsRoot, "W A S D sür · boşluk korna · E / Q vites · L ışık · F selektör · H dörtlü · N müzik · M dikiz · P foto · V video · O ayarlar",
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

        static string ModeName(GearMode m) => m == GearMode.Automatic ? "OTOMATİK" : m == GearMode.Manual ? "MANUEL" : "VİTESSİZ";

        // ---------------------------------------------------------------- her kare

        void Update()
        {
            if (root == null) return;
            if (dirty || Mathf.Abs(root.rect.width - W) > 0.5f || Key() != layoutKey) Rebuild();
            HandlePointers();

            var s = drive.Settings;
            var link = drive.Link;
            var model = drive.Model;
            float dt = Time.unscaledDeltaTime;

            video.Mode = (VideoMode)s.Video;
            if (wheelRt != null)
            {
                wheelAngle = Mathf.Lerp(wheelAngle, model.Steer * 100f, Mathf.Min(1f, dt * 8f));
                wheelRt.localRotation = Quaternion.Euler(0f, 0f, -wheelAngle);
                bool horn = link.State.Horn;
                float hk = Mathf.Min(1f, dt * 20f);
                hubRt.localScale = Vector3.one * Mathf.Lerp(hubRt.localScale.x, horn ? 0.93f : 1f, hk);
                hubPress.color = new Color(1f, 1f, 1f, Mathf.Lerp(hubPress.color.a, horn ? 0.16f : 0f, hk));
            }
            tachNeedle.localRotation = Quaternion.Euler(0f, 0f, -Angle(drive.Rpm / 1000f, 4f));
            float shown = s.Unit == SpeedUnit.KilometersPerHour ? drive.SpeedMs * 3.6f : drive.SpeedMs;
            speedNeedle.localRotation = Quaternion.Euler(0f, 0f, -Angle(shown, speedMax));
            menuRoot.gameObject.SetActive(menuOpen);
            speedText.text = s.FormatSpeed(drive.SpeedMs, out _);
            gearText.text = model.Gear < 0 ? "R" : model.Gear == 0 ? "N" : s.Gearbox == GearMode.Fixed ? "D" : model.Gear.ToString();
            bool hint = s.Gearbox == GearMode.Manual && model.ShiftHint && (int)(Time.unscaledTime / 0.25f) % 2 == 0;
            gearText.color = model.Gear < 0 ? Red : hint ? Accent : Text;
            gearModeText.text = hint ? "VİTES ▲" : ModeName(s.Gearbox);
            gearModeText.color = hint ? Accent : Dim;

            bool hazardBlink = drive.CurrentHazard && (int)(Time.unscaledTime / 0.5f) % 2 == 0;
            var off = new Color(1f, 1f, 1f, 0.14f);
            lampLight.color = drive.CurrentLed ? Green : off;
            lampHazard.color = hazardBlink ? Red : off;
            lampLink.color = !link.Demo && (link.IsWeak || !link.IsConnected) ? Amber : off;

            int playing = drive.PlayingMelody;
            songText.text = playing > 0 && playing <= drive.Firmware.Songs.Count ? "Çalıyor:  " + drive.Firmware.Songs[playing - 1].Title : "";

            var st = link.Status;
            var m = st?.motor;
            if (link.Demo) statusText.text = "DEMO  ·  robotsuz";
            else if (!link.IsConnected) statusText.text = link.Searching ? "robot aranıyor…" : "bağlantı yok";
            else statusText.text = $"ROBOT  ·  {link.RoundTripMs:0} ms" + (st != null && st.sinyal != 0 ? $"  ·  {st.sinyal} dBm {Bars(st.sinyal)}" : "") +
                                   (link.Loss > 0.05f ? $"  ·  kayıp %{link.Loss * 100f:0}" : "");
            // güç: motora o an giden pay (Arduino'nun bildirdiği, rampa dahil); vites atınca artışı görünür
            infoText.text = $"{Mathf.RoundToInt(video.Fps)} fps" + (st != null && st.veri_kbps > 0 ? $"  ·  {st.veri_kbps / 1000f:0.0} Mbit/s" : "") +
                            $"  ·  güç %{Mathf.RoundToInt(Mathf.Abs(drive.MotorPower) * 100f)}" +
                            (st != null && st.sicaklik > 0f ? $"  ·  {st.sicaklik:0}°C" : "") + $"  ·  {DateTime.Now:HH:mm}";
            statusText.color = !link.Demo && (link.IsWeak || !link.IsConnected) ? Red : Text;
            infoText.color = st != null && st.sicaklik >= 75f ? Amber : Text;

            recText.text = drive.Recorder.Recording ? "KAYIT  " + TimeSpan.FromSeconds(drive.Recorder.RecordedSeconds).ToString(@"m\:ss") : "";
            toastText.text = Time.unscaledTime < drive.ToastUntil ? drive.Toast : "";
            warnText.text = link.Demo ? "" :
                            link.IsConnected && link.IsWeak ? "Bağlantı zayıf: robot durduruldu" :
                            m != null && m.sikisma != 0 ? "Teker sıkıştı: motorlar bir an durduruldu" :
                            m != null && m.ariza ? "Motor sürücüsü arıza bildiriyor" :
                            st != null && st.sicaklik >= 80f ? $"Jetson çok ısındı: {st.sicaklik:0}°C" : "";
            centerText.text = video.HasFrame ? "" : link.Demo ? "Kayıtlı görüntü yükleniyor" :
                              !link.IsConnected ? (string.IsNullOrEmpty(link.SearchText) ? "Robota bağlanılıyor" : Capital(link.SearchText)) : "Görüntü bekleniyor";

            musicRoot.gameObject.SetActive(drive.MusicMenuOpen);
            settingsRoot.gameObject.SetActive(drive.SettingsOpen);
            if (drive.SettingsOpen)
                settingsInfo.text = link.Demo ? "Robotsuz demo: kayıtlı 360° görüntü, sesler bu cihazdan çalar." :
                    $"adres {link.Host ?? "-"}   gidip gelme {link.RoundTripMs:0} ms   kayıp %{link.Loss * 100f:0}\n" +
                    $"Wi-Fi {(st != null ? st.sinyal + " dBm" : "-")}   görüntü {(st != null ? st.veri_kbps / 1000f : 0f):0.0} Mbit/s   " +
                    $"Jetson {(st != null && st.sicaklik > 0f ? st.sicaklik.ToString("0") + "°C" : "-")}\n" +
                    $"kayıtlar: {ScreenRecorder.Folder}";

            float k = Mathf.Min(1f, dt * 18f);
            foreach (var b in buttons)
            {
                bool pressed = b.Pointer != int.MinValue || b.Held != null && b.Held();
                bool on = b.Lit != null && b.Lit();
                if (b.Style != ButtonStyle.Invisible)
                {
                    // basınca hafifçe içeri girer (pedal: ayakla basılmış gibi alttan aşağı)
                    float target = pressed ? (b.Style == ButtonStyle.Pedal ? 0.9f : 0.93f) : 1f;
                    var sc = b.Rt.localScale;
                    b.Rt.localScale = new Vector3(b.Style == ButtonStyle.Pedal ? 1f : Mathf.Lerp(sc.x, target, k), Mathf.Lerp(sc.y, target, k), 1f);
                }
                if (b.Glow != null)
                {
                    float ga = pressed ? 0.6f : on && b.Style == ButtonStyle.Normal ? 0.2f : 0f;
                    b.Glow.color = new Color(Accent.r, Accent.g, Accent.b, Mathf.Lerp(b.Glow.color.a, ga, k));
                }
                if (b.Dynamic != null && b.Label != null) b.Label.text = b.Dynamic();
                if (b.Back == null) continue;
                bool hot = pressed || on && b.Style != ButtonStyle.Switch;
                b.Back.color = hot ? b.OnColor : b.OffColor;
                var fg = hot ? Accent : b.Style == ButtonStyle.Pedal || b.Style == ButtonStyle.Switch ? Dim : Text;
                if (b.Label != null) b.Label.color = fg;
                if (b.Icon != null) b.Icon.color = b.IconTint ?? (b.Style == ButtonStyle.Switch && !pressed ? (on ? Text : Dim) : fg);
                if (b.Led != null) b.Led.color = on ? b.LedColor : new Color(1f, 1f, 1f, 0.1f);
            }

            if (dpad != null)
            {
                int f = drive.InputForward, t = drive.InputTurn;
                bool[] active = { f > 0, t > 0, f < 0, t < 0 };
                for (int i = 0; i < 4; i++)
                {
                    var c = active[i] ? new Color(Accent.r, Accent.g, Accent.b, 0.55f) : new Color(1f, 1f, 1f, 0.05f);
                    dpad.Wedges[i].color = Color.Lerp(dpad.Wedges[i].color, c, k);
                }
                if (dpad.Pointer == int.MinValue)
                    dpad.Knob.anchoredPosition = Vector2.Lerp(dpad.Knob.anchoredPosition, dpad.Center, k);
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
            rearCam = Make("İç ayna kamerası", 960, 226, 22f, out rearRt);
        }

        void UpdateMirrors()
        {
            rearCam.enabled = drive.Settings.Cockpit && drive.Settings.RearMirror;
            rearCam.transform.localRotation = Quaternion.Euler(3f, drive.Viewer.Yaw + 180f, 0f);
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
            if (dpad != null && dpad.Pointer != int.MinValue && !down.ContainsKey(dpad.Pointer)) PadRelease();
            foreach (var id in new List<int>(looking.Keys))
                if (!down.ContainsKey(id)) looking.Remove(id);

            foreach (var kv in down)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(root, kv.Value, null, out var p);
                if (dpad != null && dpad.Pointer == kv.Key) { PadMove(p); continue; }
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
                // Açık menü / panel düğmesi, altında kalan düğmeden (ör. pedal) önce gelir
                Btn hit = null;
                bool hitOverlay = false;
                foreach (var b in buttons)
                {
                    if (!b.Rt.gameObject.activeInHierarchy || !Contains(b.Rt, p)) continue;
                    bool overlay = b.Rt.IsChildOf(menuRoot) || b.Rt.IsChildOf(settingsRoot) || b.Rt.IsChildOf(musicRoot);
                    if (overlay || !hitOverlay) { hit = b; hitOverlay = overlay; }
                }
                bool onPad = dpad != null && (p - dpad.Center).magnitude < dpad.Radius + 12f;
                if (onPad && !hitOverlay && !(drive.SettingsOpen && Contains(settingsRoot, p)) && !(menuOpen && Contains(menuRoot, p)))
                {
                    dpad.Pointer = kv.Key;
                    PadMove(p);
                }
                else if (hit != null)
                {
                    hit.Pointer = kv.Key;
                    hit.Press?.Invoke();
                    hit.Hold?.Invoke(true);
                }
                else if (drive.MusicMenuOpen && Contains(musicRoot, p)) { }   // panelin boş yeri: bir şey yapma
                else if (drive.SettingsOpen) { if (!Contains(settingsRoot, p)) drive.SettingsOpen = false; }   // dışına dokununca kapan
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

        Btn Button(string text, Rect r, Action press, Action<bool> hold, Func<bool> lit, int size = 13,
                   TextAnchor align = TextAnchor.MiddleCenter, RectTransform parent = null, Func<string> dynamic = null, string icon = null,
                   ButtonStyle style = ButtonStyle.Normal)
        {
            parent = parent != null ? parent : content;
            Vector2 origin = parent == content ? Vector2.zero : parent.anchoredPosition;
            var rt = Rt("Düğme " + text, parent, r.position - origin, r.size);
            // basınca küçülme ortaya doğru (pedal: alta doğru)
            var pivot = style == ButtonStyle.Pedal ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 0.5f);
            rt.pivot = pivot;
            rt.anchoredPosition += Vector2.Scale(r.size, pivot);
            var b = new Btn { Rt = rt, Press = press, Hold = hold, Lit = lit, Dynamic = dynamic, Style = style };
            buttons.Add(b);
            if (style == ButtonStyle.Invisible) return b;

            float radius = style == ButtonStyle.Pedal ? 10f : style == ButtonStyle.Paddle ? 9f : Mathf.Min(12f, r.height / 2f);
            if (icon == "sol" || icon == "sag") radius = r.height / 2f;
            // basılıyken çevresinde turuncu ışıltı
            b.Glow = Shape("Işıltı", rt);
            b.Glow.Feather = 7f;
            b.Glow.RoundRect(new Rect(-2f, -2f, r.width + 4f, r.height + 4f), radius + 2f, Color.white);
            b.Glow.color = new Color(Accent.r, Accent.g, Accent.b, 0f);

            switch (style)
            {
                case ButtonStyle.Pedal:
                {
                    // metal çerçeve, kauçuk yüz, yatay tırtıklar
                    var frame = Shape("Çerçeve", rt);
                    frame.RoundRect(new Rect(0, 0, r.width, r.height), radius, Color.white);
                    b.Back = frame;
                    b.OffColor = Hex("#4A4F56");
                    b.OnColor = Accent;
                    var rubber = Shape("Kauçuk", rt);
                    rubber.RoundRect(new Rect(4f, 4f, r.width - 8f, r.height - 8f), radius - 3f, Hex("#111315"));
                    for (float yy = 26f; yy < r.height - 12f; yy += 10f)
                        rubber.Line(V(12f, yy), V(r.width - 12f, yy), 3.5f, Hex("#24272B"));
                    rubber.RoundRect(new Rect(6f, r.height * 0.55f, r.width - 12f, r.height * 0.45f - 6f), radius - 4f, new Color(1f, 1f, 1f, 0.035f));
                    b.Label = Label(rt, text, font, size, Dim, TextAnchor.MiddleCenter, V(r.width / 2f, 13f), V(r.width, 14));
                    break;
                }
                case ButtonStyle.Paddle:
                {
                    var back = Shape("Zemin", rt);
                    back.RoundRect(new Rect(0, 0, r.width, r.height), radius, Color.white);
                    b.Back = back;
                    b.OffColor = Hex("#2B2F35");
                    b.OnColor = Hex("#6B3F22");
                    var sheen = Shape("Parlaklık", rt);
                    sheen.RoundRect(new Rect(2f, r.height * 0.5f, r.width - 4f, r.height * 0.5f - 2f), radius - 2f, new Color(1f, 1f, 1f, 0.08f));
                    sheen.Line(V(r.width * 0.5f, 8f), V(r.width * 0.5f, 18f), 1f, new Color(1f, 1f, 1f, 0.15f));
                    b.Label = Label(rt, text, font, size, Text, TextAnchor.MiddleCenter, r.size / 2f + V(0, 4f), r.size);
                    break;
                }
                case ButtonStyle.Switch:
                {
                    // gösterge paneli düğmesi: koyu plastik, üstte durum ışığı şeridi
                    var border = Shape("Çerçeve", rt);
                    border.RoundRect(new Rect(0, 0, r.width, r.height), radius, new Color(1f, 1f, 1f, 0.2f));
                    var back = Shape("Zemin", rt);
                    back.RoundRect(new Rect(1.5f, 1.5f, r.width - 3f, r.height - 3f), radius - 1.5f, Color.white);
                    b.Back = back;
                    b.OffColor = new Color(0.055f, 0.06f, 0.07f, 0.94f);
                    b.OnColor = new Color(0.12f, 0.12f, 0.13f, 0.96f);
                    var sheen = Shape("Parlaklık", rt);
                    sheen.RoundRect(new Rect(3f, r.height * 0.5f, r.width - 6f, r.height * 0.5f - 3f), radius - 3f, new Color(1f, 1f, 1f, 0.05f));
                    b.Led = Shape("Durum ışığı", rt);
                    b.Led.RoundRect(new Rect(12f, r.height - 9f, r.width - 24f, 3.5f), 1.7f, Color.white);
                    b.Icon = Shape("Simge", rt);
                    Icon(b.Icon, icon, V(r.width / 2f, r.height / 2f + 4f), Mathf.Min(r.width, r.height) * 0.22f);
                    b.Label = Label(rt, text, font, size, Dim, TextAnchor.MiddleCenter, V(r.width / 2f, 11f), V(r.width, 12));
                    break;
                }
                default:
                {
                    // ince açık çerçeve + koyu cam: açık renkli görüntünün üstünde de belli olur
                    var border = Shape("Çerçeve", rt);
                    border.RoundRect(new Rect(0, 0, r.width, r.height), radius, new Color(1f, 1f, 1f, 0.26f));
                    var back = Shape("Zemin", rt);
                    back.RoundRect(new Rect(1.5f, 1.5f, r.width - 3f, r.height - 3f), radius - 1.5f, Color.white);
                    b.Back = back;
                    var sheen = Shape("Parlaklık", rt);   // üst yarıda çok hafif açıklık: düğme kabarık görünür
                    sheen.RoundRect(new Rect(3f, r.height * 0.5f, r.width - 6f, r.height * 0.5f - 3f), Mathf.Max(0f, radius - 3f), new Color(1f, 1f, 1f, 0.05f));
                    if (icon != null)
                    {
                        b.Icon = Shape("Simge", rt);
                        bool alone = string.IsNullOrEmpty(text);
                        Icon(b.Icon, icon, V(r.width / 2f, r.height / 2f + (alone ? 0f : 10f)), Mathf.Min(r.width, r.height) * (alone ? 0.3f : 0.24f));
                        if (!alone) b.Label = Label(rt, text, font, size, Text, TextAnchor.MiddleCenter, V(r.width / 2f, 15f), V(r.width, 16));
                    }
                    else
                        b.Label = Label(rt, text, font, size, Text, align, align == TextAnchor.MiddleLeft ? V(r.width / 2f + 8f, r.height / 2f) : r.size / 2f,
                                        align == TextAnchor.MiddleLeft ? r.size - V(16, 0) : r.size);
                    break;
                }
            }
            return b;
        }

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
                case "ag":   // Wi-Fi
                    for (int i = 1; i <= 3; i++) s.Arc(c + V(0, -r * 0.7f), r * 0.55f * i - t, r * 0.55f * i, -45f, 45f, w, 24);
                    s.Circle(c + V(0, -r * 0.7f), t, w, 12);
                    break;
                case "sol":
                case "sag":
                    float sx = kind == "sol" ? -1f : 1f;
                    s.Polygon(new[] { c + V(sx * r * 0.75f, 0f), c + V(-sx * r * 0.45f, r * 0.7f), c + V(-sx * r * 0.45f, -r * 0.7f) }, w);
                    break;
                case "dikiz":   // geniş ayna + sap
                    s.Line(c + V(-r, -r * 0.15f), c + V(r, -r * 0.15f), t, w); s.Line(c + V(r, -r * 0.15f), c + V(r, r * 0.5f), t, w);
                    s.Line(c + V(r, r * 0.5f), c + V(-r, r * 0.5f), t, w); s.Line(c + V(-r, r * 0.5f), c + V(-r, -r * 0.15f), t, w);
                    s.Line(c + V(0, r * 0.5f), c + V(0, r * 0.95f), t, w);
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
            if (rearRt != null) rearRt.Release();
        }
    }
}
