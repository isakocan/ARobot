using System.Collections.Generic;
using ARaki.AR;
using ARaki.Game;
using ARaki.Robot;
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

        [Header("Tasarım")]
        [SerializeField] Font titleFont;
        [SerializeField] Font bodyFont;
        [SerializeField] Font monoFont;
        [SerializeField] Sprite bracket;   // yalnızca köşeleri çizilen 9-dilimli çerçeve
        [SerializeField] Sprite ring;      // ince daire

        float RadarRange => session != null ? session.Profile.RadarRange : 4f;   // m, yükseltmeyle büyür
        const float RadarRadius = 78f;     // px

        class Row { public MissionPoint Point; public RectTransform Root; public Text Status; public RectTransform Fill; public float Width; public Text Label; }
        class Blip { public Transform Target; public Image Image; public bool Threat; }
        class Tag { public Transform Target; public MissionPoint Point; public Threat Threat; public RectTransform Rt; public Image Frame; public Text Name; public Text Info; public float Lift; }

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
        RectTransform missionCard, commsCard;
        Text missionHeader, sectorLabel, radarLabel;
        float noticeTimer, ledLevel;
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
            int n = mission.Points.Count;
            var card = Card("Mission", TopLeft, TopLeft, new Vector2(24f, -24f), new Vector2(330f, 92f + n * 46f));
            missionCard = card;
            missionHeader = Label(card, "GÖREV 01  ·  AKTİF", monoFont, 11, SpaceTheme.Primary, TextAnchor.UpperLeft, TopLeft, TopLeft, new Vector2(16f, -12f), new Vector2(298f, 16f));
            Label(card, "HANE KEŞFİ", titleFont, 26, SpaceTheme.Text, TextAnchor.UpperLeft, TopLeft, TopLeft, new Vector2(16f, -26f), new Vector2(298f, 32f));
            Img(Rt("Rule", card, TopLeft, TopLeft, new Vector2(16f, -64f), new Vector2(298f, 1f)), SpaceTheme.WithAlpha(SpaceTheme.Primary, 0.35f));

            for (int i = 0; i < n; i++)
            {
                var p = mission.Points[i];
                var rowRoot = Rt("Row", card, TopLeft, TopLeft, new Vector2(16f, -78f - i * 46f), new Vector2(298f, 40f));
                var row = new Row { Point = p, Root = rowRoot, Width = 298f };
                row.Label = Label(rowRoot, p.Label, bodyFont, 18, SpaceTheme.Text, TextAnchor.UpperLeft, TopLeft, TopLeft, new Vector2(0f, 0f), new Vector2(200f, 22f));
                row.Status = Label(rowRoot, "", monoFont, 13, SpaceTheme.TextDim, TextAnchor.UpperRight, TopLeft, TopLeft, new Vector2(208f, -3f), new Vector2(90f, 18f));
                Img(Rt("Track", rowRoot, TopLeft, TopLeft, new Vector2(0f, -26f), new Vector2(298f, 3f)), SpaceTheme.Track);
                row.Fill = Rt("Fill", rowRoot, TopLeft, new Vector2(0f, 1f), new Vector2(0f, -26f), new Vector2(0f, 3f));
                Img(row.Fill, SpaceTheme.Primary);
                rows.Add(row);
            }
        }

        /// <summary>Görevdeki aktif hedeflere göre satırları ve kart yüksekliğini düzenler.</summary>
        void Relayout()
        {
            int index = 0;
            foreach (var r in rows)
            {
                bool active = r.Point.gameObject.activeSelf;
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
                Img(Rt("Bar", card, TopLeft, new Vector2(0f, 0f), new Vector2(170f + i * 11f, -111f), new Vector2(7f, h)),
                    i < 3 ? SpaceTheme.Primary : SpaceTheme.Track);
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

        void BuildRadar()
        {
            var card = Card("Radar", BottomRight, BottomRight, new Vector2(-24f, 24f), new Vector2(180f, 180f));
            Label(card, "RADAR", monoFont, 11, SpaceTheme.Primary, TextAnchor.UpperLeft, TopLeft, TopLeft, new Vector2(12f, -9f), new Vector2(80f, 14f));
            radarLabel = Label(card, "4 m", monoFont, 11, SpaceTheme.TextDim, TextAnchor.UpperRight, TopLeft, TopLeft, new Vector2(88f, -9f), new Vector2(80f, 14f));

            var c = Rt("Scope", card, Center, Center, new Vector2(0f, -6f), new Vector2(RadarRadius * 2f, RadarRadius * 2f));
            Img(c, SpaceTheme.WithAlpha(SpaceTheme.Primary, 0.55f), ring);
            Img(Rt("Mid", c, Center, Center, Vector2.zero, new Vector2(RadarRadius, RadarRadius)), SpaceTheme.WithAlpha(SpaceTheme.Primary, 0.25f), ring);
            Img(Rt("H", c, Center, Center, Vector2.zero, new Vector2(RadarRadius * 2f, 1f)), SpaceTheme.WithAlpha(SpaceTheme.Primary, 0.15f));
            Img(Rt("V", c, Center, Center, Vector2.zero, new Vector2(1f, RadarRadius * 2f)), SpaceTheme.WithAlpha(SpaceTheme.Primary, 0.15f));

            sweep = Rt("Sweep", c, Center, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(2f, RadarRadius));
            Img(sweep, SpaceTheme.WithAlpha(SpaceTheme.Primary, 0.75f));

            var me = Rt("Me", c, Center, Center, Vector2.zero, new Vector2(8f, 8f));
            me.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Img(me, SpaceTheme.Text);

            foreach (var p in mission.Points) blips.Add(MakeBlip(c, p.transform, false));
            foreach (var t in mission.Threats) blips.Add(MakeBlip(c, t.transform, true));
        }

        Blip MakeBlip(RectTransform parent, Transform target, bool threat)
        {
            var r = Rt("Blip", parent, Center, Center, Vector2.zero, new Vector2(threat ? 8f : 7f, threat ? 8f : 7f));
            if (threat) r.localRotation = Quaternion.Euler(0f, 0f, 45f);
            return new Blip { Target = target, Threat = threat, Image = Img(r, threat ? SpaceTheme.Danger : SpaceTheme.Primary) };
        }

        void BuildBottomBar()
        {
            heading = Label(root, "", monoFont, 15, SpaceTheme.Text, TextAnchor.MiddleCenter, BottomCenter, BottomCenter, new Vector2(0f, 50f), new Vector2(520f, 24f));
            AddShadow(heading);
            var hint = Label(root, "W A S D  SÜR     ·     Q E  ETRAFA BAK     ·     L  LED     ·     R F  GÜÇ     ·     ESC  DURAKLAT",
                monoFont, 11, SpaceTheme.WithAlpha(SpaceTheme.Text, 0.6f), TextAnchor.MiddleCenter, BottomCenter, BottomCenter, new Vector2(0f, 24f), new Vector2(900f, 18f));
            AddShadow(hint);
        }

        void BuildBanners()
        {
            noticeText = Label(root, "", titleFont, 32, SpaceTheme.Primary, TextAnchor.MiddleCenter, TopCenter, TopCenter, new Vector2(0f, -86f), new Vector2(800f, 44f));
            AddShadow(noticeText);
            alertText = Label(root, "", bodyFont, 20, SpaceTheme.Danger, TextAnchor.MiddleCenter, TopCenter, TopCenter, new Vector2(0f, -136f), new Vector2(800f, 28f));
            AddShadow(alertText);
        }

        void BuildTags()
        {
            foreach (var p in mission.Points)
                tags.Add(MakeTag(p.transform, p, null, p.Kind == PointKind.Base ? 0.12f : 0.58f));
            foreach (var t in mission.Threats)
                tags.Add(MakeTag(t.transform, null, t, 0.1f));
        }

        Tag MakeTag(Transform target, MissionPoint point, Threat threat, float lift)
        {
            var rt = Rt("Tag", root, Center, Center, Vector2.zero, new Vector2(72f, 72f));
            var tag = new Tag { Target = target, Point = point, Threat = threat, Rt = rt, Lift = lift };
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
            if (!built) return;
            var t = robot.Telemetry;
            var cmd = robot.LastCommand;

            // yakıt
            fuelFill.sizeDelta = new Vector2(BarWidth * t.Battery01, 6f);
            fuelValue.text = Mathf.RoundToInt(t.Battery01 * 100f) + "%";
            Color fuelColor = t.Battery01 < 0.15f ? SpaceTheme.Danger : t.Battery01 < 0.3f ? SpaceTheme.Warning : SpaceTheme.Primary;
            fuelImage.color = fuelColor;
            fuelValue.color = fuelColor == SpaceTheme.Primary ? SpaceTheme.Text : fuelColor;

            powerFill.sizeDelta = new Vector2(BarWidth * robot.PwmLimit, 6f);
            powerValue.text = Mathf.RoundToInt(robot.PwmLimit * 100f) + "%";

            SetMotor(motorLeft, cmd.Left * robot.PwmLimit);
            SetMotor(motorRight, cmd.Right * robot.PwmLimit);

            heading.text = $"YÖN {Mathf.RoundToInt(t.YawDegrees):000}°     BAKIŞ {(viewer.Yaw >= 0 ? "+" : "-")}{Mathf.Abs(Mathf.RoundToInt(viewer.Yaw)):000}°";

            Relayout();
            if (session != null)
            {
                missionHeader.text = "GÖREV " + session.Level.ToString("00") + "  ·  AKTİF";
                sectorLabel.text = session.Spec.Title + "  ·  SALON";
            }
            radarLabel.text = Mathf.RoundToInt(RadarRange) + " m";
            UpdateMissionRows();
            UpdateRadar(dt);
            UpdateTags();
            UpdateBanners(dt);
            UpdateLed(dt);
            UpdateComms(dt);
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

        void UpdateMissionRows()
        {
            foreach (var r in rows)
            {
                var p = r.Point;
                if (!p.gameObject.activeSelf) continue;
                r.Fill.sizeDelta = new Vector2(r.Width * (p.Done ? 1f : p.Progress), 3f);
                if (p.Done) { r.Status.text = "TAMAM"; r.Status.color = SpaceTheme.Success; r.Label.color = SpaceTheme.TextDim; r.Fill.GetComponent<Image>().color = SpaceTheme.Success; }
                else if (p.Locked) { r.Status.text = "KİLİTLİ"; r.Status.color = SpaceTheme.TextDim; r.Label.color = SpaceTheme.TextDim; }
                else if (p.Progress > 0.01f) { r.Status.text = Mathf.RoundToInt(p.Progress * 100f) + "%"; r.Status.color = SpaceTheme.Warning; r.Label.color = SpaceTheme.Text; r.Fill.GetComponent<Image>().color = SpaceTheme.Warning; }
                else { r.Status.text = "BEKLİYOR"; r.Status.color = SpaceTheme.TextDim; r.Label.color = SpaceTheme.Text; r.Fill.GetComponent<Image>().color = SpaceTheme.Primary; }
            }
        }

        void UpdateRadar(float dt)
        {
            sweep.localRotation *= Quaternion.Euler(0f, 0f, -110f * dt);
            foreach (var b in blips)
            {
                bool live = b.Target.gameObject.activeInHierarchy;
                if (b.Image.gameObject.activeSelf != live) b.Image.gameObject.SetActive(live);
                if (!live) continue;
                Vector3 rel = robot.transform.InverseTransformPoint(b.Target.position);
                Vector2 v = new Vector2(rel.x, rel.z) / RadarRange * RadarRadius;
                bool outside = v.magnitude > RadarRadius * 0.96f;
                if (outside) v = v.normalized * RadarRadius * 0.96f;
                b.Image.rectTransform.anchoredPosition = v;
                Color c = b.Image.color;
                c.a = outside ? 0.35f : 1f;
                b.Image.color = c;
            }
        }

        void UpdateTags()
        {
            var cam = viewer.ViewCamera;
            foreach (var tag in tags)
            {
                if (!tag.Target.gameObject.activeInHierarchy) { tag.Rt.gameObject.SetActive(false); continue; }
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
                if (tag.Threat != null)
                {
                    name = "TEHDİT";
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
                    name = p.Label;
                    if (p.Done) { color = SpaceTheme.Success; info = "TAMAM"; }
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
