using System.Collections.Generic;
using ARaki.AR;
using ARaki.Game;
using ARaki.Robot;
using ARaki.Sensing;
using ARaki.Video;
using UnityEngine;
using UnityEngine.UI;

namespace ARaki.UI
{
    /// <summary>
    /// Uzay temalı oyun arayüzü (demo): görev kartı, yakıt, motor PWM, radar, yön, AR etiketleri ve bildirimler.
    /// Arayüz çalışma anında kodla kurulur; renkler SpaceTheme'den gelir.
    /// </summary>
    public class SpaceHud : MonoBehaviour
    {
        [Header("Kaynaklar")]
        [SerializeField] MockRobotLink robot;
        [SerializeField] MissionController mission;
        [SerializeField] PanoramaViewer viewer;
        [SerializeField] ARRig rig;
        [SerializeField] GameSession session;
        [SerializeField] HazardController hazard;
        [SerializeField] ExplorationMap map;

        [Header("Tasarım")]
        [SerializeField] Font titleFont;
        [SerializeField] Font bodyFont;
        [SerializeField] Font monoFont;
        [SerializeField] Sprite bracket;   // yalnızca köşeleri çizilen 9-dilimli çerçeve
        [SerializeField] Sprite ring;      // ince daire

        float RadarRange => session != null ? session.Profile.RadarRange : 4f;   // m, yükseltmeyle büyür
        const float RadarRadius = 78f;     // px

        class Row { public MissionPoint Point; public int Custom; public RectTransform Root; public Text Status; public RectTransform Fill; public Image FillImage; public float Width; public Text Label; }
        class Blip { public Transform Target; public Image Image; public bool Threat; public MissionPoint Point; public Threat Th; public bool Detected; }
        class Tag { public bool Shelter; public Transform Target; public MissionPoint Point; public Threat Threat; public RectTransform Rt; public Image Frame; public Text Name; public Text Info; public float Lift; }

        RectTransform root;
        Canvas canvas;
        bool built;
        readonly List<Row> rows = new List<Row>();
        readonly List<Blip> blips = new List<Blip>();
        readonly List<Tag> tags = new List<Tag>();

        RectTransform fuelFill, powerFill, motorLeft, motorRight, sweep, ledFill;
        Image fuelImage, ledFillImage;
        Text fuelValue, powerValue, heading, noticeText, alertText, commsText, ledLabel;
        CanvasGroup commsGroup, rootGroup;
        Text hazardText, shelterText;
        Image stormOverlay;
        readonly List<Image> signalBars = new List<Image>();
        RectTransform missionCard, commsCard;
        Text missionHeader, sectorLabel, radarLabel;
        float noticeTimer, ledLevel;
        int shownFuel = -1, shownPower = -1, shownHeading = int.MinValue;
        const float BarWidth = 202f;
        const float MotorTrack = 150f;
        const float LedBarWidth = 120f;

        // Telsiz mesajları: sırayla, daktilo gibi yazılır
        readonly Queue<string> commsQueue = new Queue<string>();
        string commsFull = "";
        float commsShown, commsHold;
        const float CommsCharsPerSecond = 42f;

        void Awake() => Build();
        void LateUpdate() => Tick(Time.deltaTime);

        void OnDestroy()
        {
            if (mission == null) return;
            mission.Notice -= OnNotice;
            mission.Comms -= OnComms;
            mission.MissionStarted -= OnMissionStarted;
            if (hazard != null)
            {
                hazard.Notice -= OnNotice;
                hazard.Comms -= OnComms;
            }
        }

        // ------------------------------------------------------------------ kurulum

        public void Build()
        {
            if (built) return;
            built = true;

            var go = new GameObject("HUD Canvas");
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>();
            // Oyunda arayüz görüntü sonrası işlemenin üstüne çizilir (net kalır); editörden alınan
            // ekran görüntülerinde kameraya bağlı çizilir.
            canvas.renderMode = Application.isPlaying ? RenderMode.ScreenSpaceOverlay : RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = viewer.ViewCamera;
            canvas.planeDistance = 1f;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            root = go.GetComponent<RectTransform>();
            rootGroup = go.AddComponent<CanvasGroup>();

            BuildStormOverlay();
            BuildCrosshair();
            BuildMissionCard();
            BuildStatusCard();
            BuildMotorCard();
            BuildRadar();
            BuildBottomBar();
            BuildLed();
            BuildBanners();
            BuildComms();
            BuildTags();

            mission.Notice += OnNotice;
            mission.Comms += OnComms;
            mission.MissionStarted += OnMissionStarted;
            if (hazard != null)
            {
                hazard.Notice += OnNotice;
                hazard.Comms += OnComms;
            }
        }

        /// <summary>Oyun menüde ya da sonuç ekranındayken arayüz gizlenir.</summary>
        public void SetVisible(bool visible)
        {
            if (rootGroup == null) return;
            rootGroup.alpha = visible ? 1f : 0f;
        }

        void OnMissionStarted()
        {
            commsQueue.Clear();
            commsFull = "";
            commsShown = commsHold = 0f;
            noticeTimer = 0f;
            if (commsGroup != null) commsGroup.alpha = 0f;
        }

        // Parlama sırasında ekranın kenarlarını turuncuya boyayan katman
        void BuildStormOverlay()
        {
            var rt = kitStretch("StormOverlay");
            stormOverlay = Img(rt, new Color(1f, 0.42f, 0.16f, 0f));
        }

        RectTransform kitStretch(string name)
        {
            var rt = Rt(name, root, Center, Center, Vector2.zero, Vector2.zero);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        void BuildComms()
        {
            var card = Card("Comms", TopLeft, TopLeft, new Vector2(24f, -400f), new Vector2(330f, 104f));
            commsCard = card;
            commsGroup = card.gameObject.AddComponent<CanvasGroup>();
            commsGroup.alpha = 0f;
            Label(card, "MERKEZ  //  KANAL 4", monoFont, 11, SpaceTheme.Primary, TextAnchor.UpperLeft, TopLeft, TopLeft, new Vector2(16f, -11f), new Vector2(240f, 16f));
            Img(Rt("Dot", card, TopRight, TopRight, new Vector2(-16f, -13f), new Vector2(6f, 6f)), SpaceTheme.Success);
            commsText = Label(card, "", bodyFont, 17, SpaceTheme.Text, TextAnchor.UpperLeft, TopLeft, TopLeft, new Vector2(16f, -32f), new Vector2(298f, 66f));
            commsText.horizontalOverflow = HorizontalWrapMode.Wrap;
            commsText.verticalOverflow = VerticalWrapMode.Truncate;
        }

        void BuildLed()
        {
            ledLabel = Label(root, "", monoFont, 12, SpaceTheme.TextDim, TextAnchor.MiddleCenter, BottomCenter, BottomCenter, new Vector2(0f, 84f), new Vector2(420f, 16f));
            AddShadow(ledLabel);
            Img(Rt("LedTrack", root, BottomCenter, BottomCenter, new Vector2(0f, 78f), new Vector2(LedBarWidth, 3f)), SpaceTheme.Track);
            ledFill = Rt("LedFill", root, BottomCenter, new Vector2(0.5f, 0f), new Vector2(0f, 78f), new Vector2(0f, 3f));
            ledFillImage = Img(ledFill, SpaceTheme.Warning);

            sectorLabel = Label(root, "SEKTÖR 07  ·  SALON", monoFont, 12, SpaceTheme.WithAlpha(SpaceTheme.Text, 0.65f), TextAnchor.MiddleCenter,
                TopCenter, TopCenter, new Vector2(0f, -24f), new Vector2(400f, 18f));
        }

        void BuildCrosshair()
        {
            var r = Rt("Crosshair", root, Center, Center, Vector2.zero, new Vector2(30f, 30f));
            Img(r, SpaceTheme.WithAlpha(SpaceTheme.Text, 0.35f), ring);
            Img(Rt("Dot", root, Center, Center, Vector2.zero, new Vector2(3f, 3f)), SpaceTheme.WithAlpha(SpaceTheme.Text, 0.7f));
        }

        void BuildMissionCard()
        {
            int n = 2;     // haritalama + numune satırı
            foreach (var p in mission.Points) if (p.Kind != PointKind.Sample) n++;
            var card = Card("Mission", TopLeft, TopLeft, new Vector2(24f, -24f), new Vector2(330f, 92f + n * 46f));
            missionCard = card;
            missionHeader = Label(card, "GÖREV 01  ·  AKTİF", monoFont, 11, SpaceTheme.Primary, TextAnchor.UpperLeft, TopLeft, TopLeft, new Vector2(16f, -12f), new Vector2(298f, 16f));
            Label(card, "YÜZEY KEŞFİ", titleFont, 26, SpaceTheme.Text, TextAnchor.UpperLeft, TopLeft, TopLeft, new Vector2(16f, -26f), new Vector2(298f, 32f));
            Img(Rt("Rule", card, TopLeft, TopLeft, new Vector2(16f, -64f), new Vector2(298f, 1f)), SpaceTheme.WithAlpha(SpaceTheme.Primary, 0.35f));

            AddRow(card, null, 1, "HARİTALAMA");
            AddRow(card, null, 2, "NUMUNE");
            foreach (var p in mission.Points)
                if (p.Kind != PointKind.Sample) AddRow(card, p, 0, p.Label);
        }

        void AddRow(RectTransform card, MissionPoint p, int custom, string label)
        {
            int i = rows.Count;
            var rowRoot = Rt("Row", card, TopLeft, TopLeft, new Vector2(16f, -78f - i * 46f), new Vector2(298f, 40f));
            var row = new Row { Point = p, Custom = custom, Root = rowRoot, Width = 298f };
            row.Label = Label(rowRoot, label, bodyFont, 18, SpaceTheme.Text, TextAnchor.UpperLeft, TopLeft, TopLeft, new Vector2(0f, 0f), new Vector2(200f, 22f));
            row.Status = Label(rowRoot, "", monoFont, 13, SpaceTheme.TextDim, TextAnchor.UpperRight, TopLeft, TopLeft, new Vector2(208f, -3f), new Vector2(90f, 18f));
            Img(Rt("Track", rowRoot, TopLeft, TopLeft, new Vector2(0f, -26f), new Vector2(298f, 3f)), SpaceTheme.Track);
            row.Fill = Rt("Fill", rowRoot, TopLeft, new Vector2(0f, 1f), new Vector2(0f, -26f), new Vector2(0f, 3f));
            row.FillImage = Img(row.Fill, SpaceTheme.Primary);
            rows.Add(row);
        }

        /// <summary>Görevdeki aktif hedeflere göre satırları ve kart yüksekliğini düzenler.</summary>
        void Relayout()
        {
            int index = 0;
            foreach (var r in rows)
            {
                bool active;
                if (r.Custom == 1) active = mission.SurveyTargetArea > 0f;
                else if (r.Custom == 2) active = mission.SamplesTotal > 0;
                else active = r.Point.gameObject.activeSelf && (r.Point.Revealed || r.Point.Kind == PointKind.Station);
                if (r.Root.gameObject.activeSelf != active) r.Root.gameObject.SetActive(active);
                if (!active) continue;
                r.Root.anchoredPosition = new Vector2(16f, -78f - index * 46f);
                index++;
            }
            missionCard.sizeDelta = new Vector2(330f, 92f + index * 46f);
            commsCard.anchoredPosition = new Vector2(24f, -(24f + 92f + index * 46f + 12f));
        }

        void BuildStatusCard()
        {
            var card = Card("Status", TopRight, TopRight, new Vector2(-24f, -24f), new Vector2(232f, 122f));
            Label(card, "YAKIT", bodyFont, 15, SpaceTheme.TextDim, TextAnchor.UpperLeft, TopLeft, TopLeft, new Vector2(15f, -10f), new Vector2(100f, 20f));
            fuelValue = Label(card, "", monoFont, 14, SpaceTheme.Text, TextAnchor.UpperRight, TopLeft, TopLeft, new Vector2(117f, -11f), new Vector2(100f, 20f));
            Img(Rt("Track", card, TopLeft, TopLeft, new Vector2(15f, -34f), new Vector2(BarWidth, 6f)), SpaceTheme.Track);
            fuelFill = Rt("Fill", card, TopLeft, TopLeft, new Vector2(15f, -34f), new Vector2(0f, 6f));
            fuelImage = Img(fuelFill, SpaceTheme.Primary);

            Label(card, "GÜÇ LİMİTİ", bodyFont, 15, SpaceTheme.TextDim, TextAnchor.UpperLeft, TopLeft, TopLeft, new Vector2(15f, -50f), new Vector2(120f, 20f));
            powerValue = Label(card, "", monoFont, 14, SpaceTheme.Text, TextAnchor.UpperRight, TopLeft, TopLeft, new Vector2(117f, -51f), new Vector2(100f, 20f));
            Img(Rt("Track", card, TopLeft, TopLeft, new Vector2(15f, -74f), new Vector2(BarWidth, 6f)), SpaceTheme.Track);
            powerFill = Rt("Fill", card, TopLeft, TopLeft, new Vector2(15f, -74f), new Vector2(0f, 6f));
            Img(powerFill, SpaceTheme.Primary);

            Label(card, "SİNYAL", bodyFont, 15, SpaceTheme.TextDim, TextAnchor.UpperLeft, TopLeft, TopLeft, new Vector2(15f, -92f), new Vector2(100f, 20f));
            for (int i = 0; i < 4; i++)
            {
                float h = 5f + i * 4f;
                signalBars.Add(Img(Rt("Bar", card, TopLeft, new Vector2(0f, 0f), new Vector2(170f + i * 11f, -111f), new Vector2(7f, h)),
                    SpaceTheme.Primary));
            }
        }

        void BuildMotorCard()
        {
            var card = Card("Motors", BottomLeft, BottomLeft, new Vector2(24f, 24f), new Vector2(236f, 88f));
            Label(card, "MOTOR PWM", monoFont, 11, SpaceTheme.Primary, TextAnchor.UpperLeft, TopLeft, TopLeft, new Vector2(15f, -11f), new Vector2(200f, 16f));
            motorLeft = MotorRow(card, "SOL", -34f);
            motorRight = MotorRow(card, "SAĞ", -60f);
        }

        RectTransform MotorRow(RectTransform card, string name, float y)
        {
            Label(card, name, bodyFont, 15, SpaceTheme.TextDim, TextAnchor.UpperLeft, TopLeft, TopLeft, new Vector2(15f, y), new Vector2(50f, 20f));
            Img(Rt("Track", card, TopLeft, new Vector2(0f, 0.5f), new Vector2(70f, y - 9f), new Vector2(MotorTrack, 4f)), SpaceTheme.Track);
            Img(Rt("Zero", card, TopLeft, new Vector2(0.5f, 0.5f), new Vector2(70f + MotorTrack * 0.5f, y - 9f), new Vector2(1f, 10f)), SpaceTheme.WithAlpha(SpaceTheme.Text, 0.45f));
            var fill = Rt("Fill", card, TopLeft, new Vector2(0f, 0.5f), new Vector2(70f + MotorTrack * 0.5f, y - 9f), new Vector2(0f, 4f));
            Img(fill, SpaceTheme.Primary);
            return fill;
        }

        const float MapW = 190f, MapH = 159f;
        RectTransform mapHolder, meRoot;
        float detectTimer;

        float PxPerMeter => MapW / map.Size.x;

        void BuildRadar()
        {
            map.Init();
            var card = Card("Map", BottomRight, BottomRight, new Vector2(-24f, 24f), new Vector2(214f, 200f));
            Label(card, "HARİTA", monoFont, 11, SpaceTheme.Primary, TextAnchor.UpperLeft, TopLeft, TopLeft, new Vector2(12f, -9f), new Vector2(80f, 14f));
            radarLabel = Label(card, "0 m²", monoFont, 11, SpaceTheme.TextDim, TextAnchor.UpperRight, TopLeft, TopLeft, new Vector2(100f, -9f), new Vector2(102f, 14f));

            mapHolder = Rt("Holder", card, TopCenter, TopCenter, new Vector2(0f, -28f), new Vector2(MapW, MapH));
            Img(mapHolder, new Color(0.01f, 0.05f, 0.08f, 0.9f));
            var tex = Rt("Tex", mapHolder, Center, Center, Vector2.zero, new Vector2(MapW, MapH));
            var raw = tex.gameObject.AddComponent<RawImage>();
            raw.texture = map.Texture;
            raw.raycastTarget = false;
            mapRaw = raw;
            Img(Rt("Edge", mapHolder, Center, Center, Vector2.zero, new Vector2(MapW, MapH)), SpaceTheme.WithAlpha(SpaceTheme.Primary, 0.25f), bracket, Image.Type.Sliced);

            // sensör süpürmesi (robotun etrafında dönen çizgi) ve robotun kendisi
            sweep = Rt("Sweep", mapHolder, BottomLeft, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(1.5f, map.Range * PxPerMeter));
            Img(sweep, SpaceTheme.WithAlpha(SpaceTheme.Primary, 0.5f));
            meRoot = Rt("Me", mapHolder, BottomLeft, Center, Vector2.zero, new Vector2(7f, 7f));
            Img(meRoot, SpaceTheme.Text);
            Img(Rt("Nose", meRoot, Center, new Vector2(0.5f, 0f), new Vector2(0f, 3f), new Vector2(2f, 8f)), SpaceTheme.Text);

            foreach (var p in mission.Points) blips.Add(MakeBlip(mapHolder, p.transform, false, p, null));
            foreach (var t in mission.Threats) blips.Add(MakeBlip(mapHolder, t.transform, true, null, t));
            if (hazard != null)
                foreach (var sp in hazard.ShelterProxies)
                {
                    var b = MakeBlip(mapHolder, sp, false, null, null);
                    b.Image.color = SpaceTheme.Success;
                    blips.Add(b);
                }
        }

        RawImage mapRaw;

        Blip MakeBlip(RectTransform parent, Transform target, bool threat, MissionPoint point, Threat th)
        {
            var r = Rt("Blip", parent, BottomLeft, Center, Vector2.zero, new Vector2(threat ? 8f : 7f, threat ? 8f : 7f));
            if (threat) r.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Color c = threat ? SpaceTheme.Danger : point != null && point.Kind == PointKind.Sample ? SpaceTheme.Warning : SpaceTheme.Primary;
            return new Blip { Target = target, Threat = threat, Point = point, Th = th, Image = Img(r, c) };
        }

        Vector2 ToMap(Vector3 world) => new Vector2((world.x - map.Origin.x) / map.Size.x * MapW, (world.z - map.Origin.y) / map.Size.y * MapH);

        void BuildBottomBar()
        {
            heading = Label(root, "", monoFont, 15, SpaceTheme.Text, TextAnchor.MiddleCenter, BottomCenter, BottomCenter, new Vector2(0f, 50f), new Vector2(520f, 24f));
            AddShadow(heading);
            var hint = Label(root, "W A S D  SÜR     ·     Q E  ETRAFA BAK     ·     L  LED     ·     R F  GÜÇ     ·     K  SES     ·     ESC  DURAKLAT",
                monoFont, 11, SpaceTheme.WithAlpha(SpaceTheme.Text, 0.6f), TextAnchor.MiddleCenter, BottomCenter, BottomCenter, new Vector2(0f, 24f), new Vector2(900f, 18f));
            AddShadow(hint);
        }

        void BuildBanners()
        {
            noticeText = Label(root, "", titleFont, 32, SpaceTheme.Primary, TextAnchor.MiddleCenter, TopCenter, TopCenter, new Vector2(0f, -86f), new Vector2(800f, 44f));
            AddShadow(noticeText);
            alertText = Label(root, "", bodyFont, 20, SpaceTheme.Danger, TextAnchor.MiddleCenter, TopCenter, TopCenter, new Vector2(0f, -176f), new Vector2(800f, 28f));
            AddShadow(alertText);
            hazardText = Label(root, "", titleFont, 30, SpaceTheme.Warning, TextAnchor.MiddleCenter, TopCenter, TopCenter, new Vector2(0f, -132f), new Vector2(900f, 40f));
            AddShadow(hazardText);
            shelterText = Label(root, "", monoFont, 13, SpaceTheme.Success, TextAnchor.MiddleCenter, BottomCenter, BottomCenter, new Vector2(0f, 104f), new Vector2(420f, 16f));
            AddShadow(shelterText);
        }

        void BuildTags()
        {
            foreach (var p in mission.Points)
                tags.Add(MakeTag(p.transform, p, null, p.Kind == PointKind.Base ? 0.12f : p.Kind == PointKind.Station ? 0.95f : 0.5f));
            foreach (var t in mission.Threats)
                tags.Add(MakeTag(t.transform, null, t, 0.1f));
            if (hazard != null)
                foreach (var s in hazard.ShelterProxies)
                    tags.Add(MakeTag(s, null, null, 0.2f, true));
        }

        Tag MakeTag(Transform target, MissionPoint point, Threat threat, float lift, bool shelter = false)
        {
            var rt = Rt("Tag", root, Center, Center, Vector2.zero, new Vector2(72f, 72f));
            var tag = new Tag { Target = target, Point = point, Threat = threat, Rt = rt, Lift = lift, Shelter = shelter };
            tag.Frame = Img(rt, SpaceTheme.Primary, bracket, Image.Type.Sliced);
            tag.Name = Label(rt, "", bodyFont, 17, SpaceTheme.Text, TextAnchor.MiddleCenter, Center, Center, new Vector2(0f, -52f), new Vector2(240f, 22f));
            tag.Info = Label(rt, "", monoFont, 12, SpaceTheme.Primary, TextAnchor.MiddleCenter, Center, Center, new Vector2(0f, -70f), new Vector2(240f, 16f));
            AddShadow(tag.Name);
            AddShadow(tag.Info);
            return tag;
        }

        // ------------------------------------------------------------------ her kare

        public void Tick(float dt)
        {
            if (!built || rootGroup.alpha < 0.01f) return;     // görünmezken (menü, sonuç ekranı, yarış) iş yapma
            var t = robot.Telemetry;
            var cmd = robot.LastCommand;

            // yakıt
            fuelFill.sizeDelta = new Vector2(BarWidth * t.Battery01, 6f);
            int fuelPct = Mathf.RoundToInt(t.Battery01 * 100f);
            if (fuelPct != shownFuel) { shownFuel = fuelPct; fuelValue.text = fuelPct + "%"; }
            Color fuelColor = t.Battery01 < 0.15f ? SpaceTheme.Danger : t.Battery01 < 0.3f ? SpaceTheme.Warning : SpaceTheme.Primary;
            fuelImage.color = fuelColor;
            fuelValue.color = fuelColor == SpaceTheme.Primary ? SpaceTheme.Text : fuelColor;

            powerFill.sizeDelta = new Vector2(BarWidth * robot.PwmLimit, 6f);
            int powerPct = Mathf.RoundToInt(robot.PwmLimit * 100f);
            if (powerPct != shownPower) { shownPower = powerPct; powerValue.text = powerPct + "%"; }

            SetMotor(motorLeft, cmd.Left * robot.PwmLimit);
            SetMotor(motorRight, cmd.Right * robot.PwmLimit);

            int yawInt = Mathf.RoundToInt(t.YawDegrees), lookInt = Mathf.RoundToInt(viewer.Yaw);
            int headingKey = yawInt * 1000 + lookInt + 500;
            if (headingKey != shownHeading)
            {
                shownHeading = headingKey;
                heading.text = $"YÖN {yawInt:000}°     BAKIŞ {(lookInt >= 0 ? "+" : "-")}{Mathf.Abs(lookInt):000}°";
            }

            Relayout();
            if (session != null)
            {
                missionHeader.text = "GÖREV " + session.Level.ToString("00") + "  ·  AKTİF";
                sectorLabel.text = Story.PlanetName + "  ·  " + session.Spec.Title;
            }
            radarLabel.text = map.ExploredArea.ToString("0.0") + " m²";
            UpdateMissionRows();
            UpdateRadar(dt);
            UpdateTags();
            UpdateBanners(dt);
            UpdateLed(dt);
            UpdateComms(dt);
            UpdateHazard();
        }

        void UpdateHazard()
        {
            if (hazard == null) return;
            float pulse = 0.65f + 0.35f * Mathf.Sin(Time.realtimeSinceStartup * 9f);
            stormOverlay.color = new Color(1f, 0.42f, 0.16f, hazard.Intensity * (hazard.Exposed ? 0.22f : 0.08f) * pulse);

            int lit = Mathf.Clamp(Mathf.CeilToInt(hazard.Signal * 4f - 0.001f), 0, 4);
            for (int i = 0; i < signalBars.Count; i++)
                signalBars[i].color = i < lit ? (lit <= 1 ? SpaceTheme.Danger : SpaceTheme.Primary) : SpaceTheme.Track;

            float blink = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup * 6f));
            switch (hazard.Phase)
            {
                case HazardPhase.Warning:
                    hazardText.text = "GÜNEŞ PARLAMASI  " + hazard.SecondsToStorm.ToString("0.0") + " SN";
                    hazardText.color = SpaceTheme.WithAlpha(SpaceTheme.Warning, blink);
                    break;
                case HazardPhase.Storm:
                    hazardText.text = hazard.Exposed ? "PARLAMA  ·  AÇIKTASIN" : "PARLAMA  ·  GÖLGEDESİN";
                    hazardText.color = hazard.Exposed ? SpaceTheme.WithAlpha(SpaceTheme.Danger, blink) : SpaceTheme.Success;
                    break;
                default:
                    hazardText.text = "";
                    break;
            }

            bool storm = hazard.Phase != HazardPhase.Calm;
            shelterText.text = storm ? (hazard.Exposed ? "GÖLGE: AÇIKTA  ·  SIĞINAĞA GİT" : "GÖLGE: KORUNAKLI") : "";
            shelterText.color = hazard.Exposed ? SpaceTheme.Warning : SpaceTheme.Success;
        }

        void UpdateLed(float dt)
        {
            ledLevel = Mathf.MoveTowards(ledLevel, robot.LedOn ? 1f : 0f, dt * 6f);
            ledFill.sizeDelta = new Vector2(LedBarWidth * ledLevel, 3f);
            ledLabel.text = robot.LedOn ? "LED AÇIK  ·  YAKIT AKIYOR" : "LED  ·  L YA DA SOL TIK (BASILI TUT)";
            ledLabel.color = robot.LedOn ? SpaceTheme.Warning : SpaceTheme.WithAlpha(SpaceTheme.Text, 0.6f);
        }

        void UpdateComms(float dt)
        {
            // Mevcut mesaj yazılıyor ya da okunuyor; süre dolunca sıradaki gelir, yoksa kart sönümlenir.
            if (commsShown < commsFull.Length)
            {
                commsShown = Mathf.Min(commsFull.Length, commsShown + CommsCharsPerSecond * dt);
                commsText.text = commsFull.Substring(0, Mathf.FloorToInt(commsShown)) + "_";
                commsHold = 0f;
            }
            else
            {
                commsText.text = commsFull;
                commsHold += dt;
                bool minRead = commsHold > 3.5f;
                if (commsQueue.Count > 0 && minRead)
                {
                    commsFull = commsQueue.Dequeue();
                    commsShown = 0f;
                }
            }

            bool visible = commsFull.Length > 0 && (commsShown < commsFull.Length || commsHold < 8f || commsQueue.Count > 0);
            commsGroup.alpha = Mathf.MoveTowards(commsGroup.alpha, visible ? 1f : 0f, dt * 2.5f);
        }

        void OnComms(string message)
        {
            if (commsFull.Length == 0) { commsFull = message; commsShown = 0f; }
            else commsQueue.Enqueue(message);
        }

        static void SetMotor(RectTransform fill, float v)
        {
            float half = MotorTrack * 0.5f;
            float w = Mathf.Abs(v) * half;
            fill.sizeDelta = new Vector2(w, 4f);
            fill.anchoredPosition = new Vector2(70f + half + Mathf.Min(0f, v) * half, fill.anchoredPosition.y);
        }

        void UpdateRadar(float dt)
        {
            float signal = hazard != null ? hazard.Signal : 1f;
            Vector2 me = ToMap(robot.transform.position);
            meRoot.anchoredPosition = me;
            meRoot.localRotation = Quaternion.Euler(0f, 0f, -robot.Telemetry.YawDegrees);
            sweep.anchoredPosition = me;
            sweep.localRotation *= Quaternion.Euler(0f, 0f, -140f * dt);
            mapRaw.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.35f, 1f, signal));

            detectTimer -= dt;
            bool refresh = detectTimer <= 0f;
            if (refresh) detectTimer = 0.2f;
            foreach (var b in blips)
            {
                bool live = b.Target.gameObject.activeInHierarchy;
                if (live && b.Point != null) live = b.Point.Revealed;
                if (live && b.Th != null)
                {
                    if (refresh)
                    {
                        b.Detected = MissionController.FlatDistance(robot.transform.position, b.Target.position) < map.Range
                                     && map.LineOfSight(robot.transform.position, b.Target.position, 0.15f);
                        b.Th.Detected = b.Detected;
                    }
                    live = b.Detected;
                }
                if (b.Image.gameObject.activeSelf != live) b.Image.gameObject.SetActive(live);
                if (!live) continue;
                Vector2 v = ToMap(b.Target.position);
                if (signal < 0.9f) v += new Vector2(Random.value - 0.5f, Random.value - 0.5f) * (1f - signal) * 14f;
                b.Image.rectTransform.anchoredPosition = v;
                Color c = b.Image.color;
                c.a = Mathf.Lerp(0.25f, 1f, signal) * (signal < 0.5f && Random.value < 0.25f ? 0f : 1f);
                b.Image.color = c;
            }
        }

        void UpdateMissionRows()
        {
            foreach (var r in rows)
            {
                if (!r.Root.gameObject.activeSelf) continue;
                if (r.Custom == 1)
                {
                    float prog = mission.SurveyProgress01;
                    bool done = mission.SurveyDone;
                    r.Fill.sizeDelta = new Vector2(r.Width * prog, 3f);
                    r.Status.text = done ? "TAMAM" : Mathf.RoundToInt(prog * 100f) + "%";
                    r.Status.color = done ? SpaceTheme.Success : prog > 0.01f ? SpaceTheme.Warning : SpaceTheme.TextDim;
                    r.Label.color = done ? SpaceTheme.TextDim : SpaceTheme.Text;
                    r.FillImage.color = done ? SpaceTheme.Success : SpaceTheme.Warning;
                    continue;
                }
                if (r.Custom == 2)
                {
                    int total = Mathf.Max(1, mission.SamplesTotal), got = mission.SamplesCollected;
                    r.Fill.sizeDelta = new Vector2(r.Width * got / total, 3f);
                    r.Status.text = got + "/" + total + (mission.SamplesCarried > 0 ? "  TAŞINIYOR" : "");
                    r.Status.color = got > 0 ? SpaceTheme.Warning : SpaceTheme.TextDim;
                    r.Label.color = SpaceTheme.Text;
                    r.FillImage.color = SpaceTheme.Warning;
                    continue;
                }

                var p = r.Point;
                if (!p.Revealed)
                {
                    r.Fill.sizeDelta = new Vector2(0f, 3f);
                    r.Status.text = "ARANIYOR";
                    r.Status.color = SpaceTheme.TextDim;
                    r.Label.color = SpaceTheme.TextDim;
                    continue;
                }
                r.Fill.sizeDelta = new Vector2(r.Width * (p.Done ? 1f : p.Progress), 3f);
                if (p.Done) { r.Status.text = "TAMAM"; r.Status.color = SpaceTheme.Success; r.Label.color = SpaceTheme.TextDim; r.FillImage.color = SpaceTheme.Success; }
                else if (p.Locked) { r.Status.text = "KİLİTLİ"; r.Status.color = SpaceTheme.TextDim; r.Label.color = SpaceTheme.TextDim; }
                else if (p.Kind == PointKind.Station && p.Progress > 0.01f) { r.Status.text = p.CompletedSectors + "/" + p.SectorCount; r.Status.color = SpaceTheme.Warning; r.Label.color = SpaceTheme.Text; r.FillImage.color = SpaceTheme.Warning; }
                else if (p.Progress > 0.01f) { r.Status.text = Mathf.RoundToInt(p.Progress * 100f) + "%"; r.Status.color = SpaceTheme.Warning; r.Label.color = SpaceTheme.Text; r.FillImage.color = SpaceTheme.Warning; }
                else { r.Status.text = p.Optional ? "İSTEĞE BAĞLI" : "BEKLİYOR"; r.Status.color = SpaceTheme.TextDim; r.Label.color = SpaceTheme.Text; r.FillImage.color = SpaceTheme.Primary; }
            }
        }

        void UpdateTags()
        {
            var cam = viewer.ViewCamera;
            foreach (var tag in tags)
            {
                if (!tag.Target.gameObject.activeInHierarchy || (tag.Point != null && !tag.Point.Revealed) || (tag.Threat != null && !tag.Threat.Detected))
                { tag.Rt.gameObject.SetActive(false); continue; }
                Vector3 world = tag.Target.position + Vector3.up * tag.Lift;
                Vector3 screen = cam.WorldToScreenPoint(rig.ToRig(world));
                bool visible = screen.z > 0.15f && screen.x > -40f && screen.x < cam.pixelWidth + 40f && screen.y > -40f && screen.y < cam.pixelHeight + 40f;
                tag.Rt.gameObject.SetActive(visible);
                if (!visible) continue;

                RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen,
                    canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : cam, out Vector2 local);
                tag.Rt.anchoredPosition = local;

                float dist = MissionController.FlatDistance(robot.transform.position, tag.Target.position);
                Color color;
                string name, info;
                if (tag.Shelter)
                {
                    name = "SIĞINAK";
                    color = SpaceTheme.Success;
                    info = dist < 0.35f ? "GÖLGEDESİN" : dist.ToString("0.0") + " m";
                }
                else if (tag.Threat != null)
                {
                    name = (tag.Threat.Kind == ThreatKind.Hunter ? "AVCI" : "SONDA");
                    switch (tag.Threat.State)
                    {
                        case ThreatState.Alert: color = SpaceTheme.Warning; info = "FARK ETTİ"; break;
                        case ThreatState.Chase: color = SpaceTheme.Danger; info = tag.Threat.Engaged ? "TEMAS" : "TAKİPTE  " + dist.ToString("0.0") + " m"; break;
                        case ThreatState.Scared: color = SpaceTheme.Primary; info = "KAÇIYOR"; break;
                        default: color = SpaceTheme.WithAlpha(SpaceTheme.Danger, 0.8f); info = "DEVRİYE  " + dist.ToString("0.0") + " m"; break;
                    }
                }
                else
                {
                    var p = tag.Point;
                    name = p.Kind == PointKind.Station ? Story.StationName : p.Label;
                    if (p.Kind == PointKind.Station && !p.Done && !p.Locked)
                    {
                        color = p.Progress > 0.01f ? SpaceTheme.Warning : SpaceTheme.Primary;
                        info = Story.StationStatus(p.CompletedSectors, p.SectorCount) + (p.ActiveSector >= 0 ? "  ·  %" + Mathf.RoundToInt(p.SectorProgress[p.ActiveSector] * 100f) : "  ·  " + dist.ToString("0.0") + " m");
                    }
                    else if (p.Done) { color = SpaceTheme.Success; info = "TAMAM"; }
                    else if (p.Locked) { color = SpaceTheme.TextDim; info = "KİLİTLİ"; }
                    else if (p.Progress > 0.01f) { color = SpaceTheme.Warning; info = "TARANIYOR " + Mathf.RoundToInt(p.Progress * 100f) + "%"; }
                    else { color = SpaceTheme.Primary; info = dist.ToString("0.0") + " m"; }
                }
                tag.Frame.color = color;
                tag.Name.text = name;
                tag.Info.text = info;
                tag.Info.color = color;
            }
        }

        void UpdateBanners(float dt)
        {
            noticeTimer -= dt;
            var c = SpaceTheme.Primary;
            c.a = Mathf.Clamp01(noticeTimer / 0.6f);
            noticeText.color = c;
            if (noticeTimer <= 0f) noticeText.text = "";

            bool engaged = false, chasing = false, scared = false;
            foreach (var t in mission.Threats) { engaged |= t.Engaged; chasing |= t.Chasing; scared |= t.Scared; }
            float blink = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup * 7f));
            if (engaged)
            {
                alertText.text = "TEHDİT TEMASI  ·  YAKIT AZALIYOR";
                alertText.color = SpaceTheme.WithAlpha(SpaceTheme.Danger, blink);
            }
            else if (chasing)
            {
                alertText.text = "TEHDİT TAKİPTE  ·  LED'İ TUT";
                alertText.color = SpaceTheme.Warning;
            }
            else if (scared)
            {
                alertText.text = "TEHDİT GERİ ÇEKİLİYOR";
                alertText.color = SpaceTheme.Primary;
            }
            else if (robot.Telemetry.Battery01 < 0.15f)
            {
                alertText.text = "YAKIT KRİTİK";
                alertText.color = SpaceTheme.WithAlpha(SpaceTheme.Danger, blink);
            }
            else alertText.text = "";
        }

        void OnNotice(string message)
        {
            noticeText.text = message;
            noticeTimer = mission.Complete ? 999f : 3.2f;
        }

        // ------------------------------------------------------------------ yardımcılar

        static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        static readonly Vector2 TopRight = new Vector2(1f, 1f);
        static readonly Vector2 TopCenter = new Vector2(0.5f, 1f);
        static readonly Vector2 BottomLeft = new Vector2(0f, 0f);
        static readonly Vector2 BottomRight = new Vector2(1f, 0f);
        static readonly Vector2 BottomCenter = new Vector2(0.5f, 0f);
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        RectTransform Card(string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rt = Rt(name, root, anchor, pivot, pos, size);
            rt.gameObject.AddComponent<Canvas>();
            Img(rt, SpaceTheme.Panel);
            var frame = Rt("Frame", rt, Center, Center, Vector2.zero, Vector2.zero);
            frame.anchorMin = Vector2.zero;
            frame.anchorMax = Vector2.one;
            frame.offsetMin = frame.offsetMax = Vector2.zero;
            Img(frame, SpaceTheme.WithAlpha(SpaceTheme.Primary, 0.9f), bracket, Image.Type.Sliced);
            return rt;
        }

        static RectTransform Rt(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static Image Img(RectTransform rt, Color color, Sprite sprite = null, Image.Type type = Image.Type.Simple)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = type;
            if (type == Image.Type.Sliced) img.fillCenter = false;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        Text Label(RectTransform parent, string text, Font font, int size, Color color, TextAnchor align,
                   Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 box)
        {
            var rt = Rt("Text", parent, anchor, pivot, pos, box);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }

        static void AddShadow(Text t)
        {
            var s = t.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, 0.85f);
            s.effectDistance = new Vector2(1.2f, -1.2f);
        }
    }
}
