using System.Collections.Generic;
using ARaki.AR;
using ARaki.Game;
using ARaki.Robot;
using ARaki.Sensing;
using ARaki.Theming;
using ARaki.Video;
using ARaki.World;
using UnityEngine;
using UnityEngine.UI;

namespace ARaki.UI
{
    /// <summary>
    /// Serbest gezi arayüzü: bölüm hedefi, pil/güç/sonar durumu, ev haritası (odalar, üs, bulgular), sessizlik göstergesi,
    /// yakalama halkası, dünyadaki bulgu etiketleri, telsiz mesajları ve bildirimler. Renkler UiTheme'den gelir.
    /// </summary>
    public class ExpeditionHud : MonoBehaviour
    {
        [Header("Kaynaklar")]
        [SerializeField] MockRobotLink robot;
        [SerializeField] Expedition expedition;
        [SerializeField] ExplorationMap map;
        [SerializeField] PanoramaViewer viewer;
        [SerializeField] ARRig rig;

        [Header("Tasarım")]
        [SerializeField] Font titleFont;
        [SerializeField] Font bodyFont;
        [SerializeField] Font monoFont;
        [SerializeField] Sprite bracket;
        [SerializeField] Sprite ring;

        const float MapW = 200f;
        const float BarWidth = 202f;
        const float MotorTrack = 150f;
        const float LedBarWidth = 120f;
        const float CommsCharsPerSecond = 42f;

        class Tag { public Find Find; public RectTransform Rt; public Image Frame; public Text Name, Info; public Image Bar; public RectTransform BarFill; }
        class Blip { public Find Find; public Image Image; }

        UiKit kit;
        RectTransform noiseTrack, root, missionCard, commsCard, mapHolder, meRoot, sweep, legendFrame, noiseFill, fuelFill, powerFill, sonarFill, ledFill, motorLeft, motorRight, baseMark;
        Canvas canvas;
        CanvasGroup rootGroup, commsGroup;
        RawImage mapRaw;
        Text chapterHeader, chapterTitle, goalText, goalCount, carryText, roomText, mapRoomText, fuelValue, powerValue, sonarValue, noticeText, promptName, promptHint, ledLabel, baseText, commsText, essenceText, noiseLabel, essenceBadge;
        Image fuelImage, goalFill, captureRing, noiseImage, sonarImage, ledFillImage;
        readonly List<Tag> tags = new List<Tag>();
        readonly List<Blip> blips = new List<Blip>();
        readonly List<Text> roomLabels = new List<Text>();
        readonly Queue<string> commsQueue = new Queue<string>();
        string commsFull = "";
        float commsShown, commsHold, noticeTimer, ledLevel, detectTimer;
        int shownFuel = -1, shownPower = -1, shownGoal = -1, shownChapter = -1, shownEssence = -1, shownCarried = -1;
        bool built;

        float PxPerMeter => MapW / map.Size.x;
        float MapH => MapW * map.Size.y / map.Size.x;

        void Awake() => Build();
        void LateUpdate() => Tick(Time.deltaTime);

        void OnDestroy()
        {
            if (expedition == null) return;
            expedition.Notice -= OnNotice;
            expedition.Comms -= OnComms;
        }

        // ------------------------------------------------------------------ kurulum

        public void Build()
        {
            if (built) return;
            built = true;
            UiTheme.EnsureApplied();
            map.Init();
            kit = new UiKit { Title = titleFont, Body = bodyFont, Mono = monoFont, Bracket = bracket, Ring = ring };

            var go = new GameObject("HUD Canvas");
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>();
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
            BuildChapterCard();
            BuildComms();
            BuildStatusCard();
            BuildMotorCard();
            BuildMap();
            BuildCenter();
            BuildTags();

            expedition.Notice += OnNotice;
            expedition.Comms += OnComms;
        }

        public void SetVisible(bool visible)
        {
            if (rootGroup == null) return;
            rootGroup.alpha = visible ? 1f : 0f;
        }

        RectTransform Card(string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rt = kit.Card(name, root, anchor, pivot, pos, size);
            rt.gameObject.AddComponent<Canvas>();       // kendi alt-canvas'ı: bir kartın metni değişince tüm HUD yeniden çizilmez
            return rt;
        }

        void BuildCrosshair()
        {
            var r = kit.Rt("Crosshair", root, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(30f, 30f));
            kit.Img(r, UiTheme.WithAlpha(UiTheme.Text, 0.35f), ring);
            kit.Img(kit.Rt("Dot", root, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(3f, 3f)), UiTheme.WithAlpha(UiTheme.Text, 0.7f));

            // yakalama ilerlemesi: çarpı ortasındaki halka dolar
            kit.Img(kit.Rt("CaptureBack", root, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(78f, 78f)), UiTheme.WithAlpha(UiTheme.Text, 0.12f), ring);
            var cr = kit.Rt("CaptureRing", root, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(78f, 78f));
            captureRing = kit.Img(cr, UiTheme.Warning, ring);
            captureRing.type = Image.Type.Filled;
            captureRing.fillMethod = Image.FillMethod.Radial360;
            captureRing.fillOrigin = 2;
            captureRing.fillAmount = 0f;
        }

        void BuildChapterCard()
        {
            missionCard = Card("Chapter", UiKit.TopLeft, UiKit.TopLeft, new Vector2(24f, -24f), new Vector2(340f, 156f));
            chapterHeader = kit.Label(missionCard, "", monoFont, 11, UiTheme.Primary, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(16f, -12f), new Vector2(308f, 16f));
            chapterTitle = kit.Label(missionCard, "", titleFont, 25, UiTheme.Text, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(16f, -27f), new Vector2(308f, 32f));
            kit.Img(kit.Rt("Rule", missionCard, UiKit.TopLeft, UiKit.TopLeft, new Vector2(16f, -64f), new Vector2(308f, 1f)), UiTheme.WithAlpha(UiTheme.Primary, 0.35f));
            goalText = kit.Label(missionCard, "", bodyFont, 18, UiTheme.Text, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(16f, -74f), new Vector2(240f, 44f));
            goalText.horizontalOverflow = HorizontalWrapMode.Wrap;
            goalCount = kit.Label(missionCard, "", monoFont, 16, UiTheme.Warning, TextAnchor.UpperRight, UiKit.TopLeft, UiKit.TopLeft, new Vector2(244f, -76f), new Vector2(80f, 20f));
            kit.Img(kit.Rt("Track", missionCard, UiKit.TopLeft, UiKit.TopLeft, new Vector2(16f, -122f), new Vector2(308f, 3f)), UiTheme.Track);
            var fill = kit.Rt("Fill", missionCard, UiKit.TopLeft, UiKit.TopLeft, new Vector2(16f, -122f), new Vector2(0f, 3f));
            goalFill = kit.Img(fill, UiTheme.Warning);
            carryText = kit.Label(missionCard, "", monoFont, 12, UiTheme.TextDim, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(16f, -132f), new Vector2(308f, 16f));
        }

        void BuildComms()
        {
            commsCard = Card("Comms", UiKit.TopLeft, UiKit.TopLeft, new Vector2(24f, -192f), new Vector2(340f, 104f));
            commsGroup = commsCard.gameObject.AddComponent<CanvasGroup>();
            commsGroup.alpha = 0f;
            kit.Label(commsCard, expedition.Theme.CommsName, monoFont, 11, UiTheme.Primary, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(16f, -11f), new Vector2(280f, 16f));
            kit.Img(kit.Rt("Dot", commsCard, UiKit.TopRight, UiKit.TopRight, new Vector2(-16f, -13f), new Vector2(6f, 6f)), UiTheme.Success);
            commsText = kit.Label(commsCard, "", bodyFont, 17, UiTheme.Text, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(16f, -32f), new Vector2(308f, 66f));
            commsText.horizontalOverflow = HorizontalWrapMode.Wrap;
            commsText.verticalOverflow = VerticalWrapMode.Truncate;
        }

        void BuildStatusCard()
        {
            var card = Card("Status", UiKit.TopRight, UiKit.TopRight, new Vector2(-24f, -24f), new Vector2(232f, 134f));
            kit.Label(card, "PİL", bodyFont, 15, UiTheme.TextDim, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(15f, -10f), new Vector2(100f, 20f));
            fuelValue = kit.Label(card, "", monoFont, 14, UiTheme.Text, TextAnchor.UpperRight, UiKit.TopLeft, UiKit.TopLeft, new Vector2(117f, -11f), new Vector2(100f, 20f));
            kit.Img(kit.Rt("Track", card, UiKit.TopLeft, UiKit.TopLeft, new Vector2(15f, -34f), new Vector2(BarWidth, 6f)), UiTheme.Track);
            fuelFill = kit.Rt("Fill", card, UiKit.TopLeft, UiKit.TopLeft, new Vector2(15f, -34f), new Vector2(0f, 6f));
            fuelImage = kit.Img(fuelFill, UiTheme.Primary);

            kit.Label(card, "VİTES  (R F)", bodyFont, 15, UiTheme.TextDim, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(15f, -50f), new Vector2(120f, 20f));
            powerValue = kit.Label(card, "", monoFont, 14, UiTheme.Text, TextAnchor.UpperRight, UiKit.TopLeft, UiKit.TopLeft, new Vector2(117f, -51f), new Vector2(100f, 20f));
            kit.Img(kit.Rt("Track", card, UiKit.TopLeft, UiKit.TopLeft, new Vector2(15f, -74f), new Vector2(BarWidth, 6f)), UiTheme.Track);
            powerFill = kit.Rt("Fill", card, UiKit.TopLeft, UiKit.TopLeft, new Vector2(15f, -74f), new Vector2(0f, 6f));
            kit.Img(powerFill, UiTheme.Primary);

            kit.Label(card, "SONAR  (X)", bodyFont, 15, UiTheme.TextDim, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(15f, -90f), new Vector2(120f, 20f));
            sonarValue = kit.Label(card, "", monoFont, 14, UiTheme.Text, TextAnchor.UpperRight, UiKit.TopLeft, UiKit.TopLeft, new Vector2(117f, -91f), new Vector2(100f, 20f));
            kit.Img(kit.Rt("Track", card, UiKit.TopLeft, UiKit.TopLeft, new Vector2(15f, -114f), new Vector2(BarWidth, 6f)), UiTheme.Track);
            sonarFill = kit.Rt("Fill", card, UiKit.TopLeft, UiKit.TopLeft, new Vector2(15f, -114f), new Vector2(0f, 6f));
            sonarImage = kit.Img(sonarFill, UiTheme.Primary);
        }

        void BuildMotorCard()
        {
            var card = Card("Motors", UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(24f, 24f), new Vector2(236f, 88f));
            kit.Label(card, "MOTOR PWM", monoFont, 11, UiTheme.Primary, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(15f, -11f), new Vector2(200f, 16f));
            motorLeft = MotorRow(card, "SOL", -34f);
            motorRight = MotorRow(card, "SAĞ", -60f);
        }

        RectTransform MotorRow(RectTransform card, string name, float y)
        {
            kit.Label(card, name, bodyFont, 15, UiTheme.TextDim, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(15f, y), new Vector2(50f, 20f));
            kit.Img(kit.Rt("Track", card, UiKit.TopLeft, new Vector2(0f, 0.5f), new Vector2(70f, y - 9f), new Vector2(MotorTrack, 4f)), UiTheme.Track);
            kit.Img(kit.Rt("Zero", card, UiKit.TopLeft, new Vector2(0.5f, 0.5f), new Vector2(70f + MotorTrack * 0.5f, y - 9f), new Vector2(1f, 10f)), UiTheme.WithAlpha(UiTheme.Text, 0.45f));
            var fill = kit.Rt("Fill", card, UiKit.TopLeft, new Vector2(0f, 0.5f), new Vector2(70f + MotorTrack * 0.5f, y - 9f), new Vector2(0f, 4f));
            kit.Img(fill, UiTheme.Primary);
            return fill;
        }

        void BuildMap()
        {
            float h = MapH;
            var card = Card("Map", UiKit.BottomRight, UiKit.BottomRight, new Vector2(-24f, 24f), new Vector2(MapW + 24f, h + 40f));
            kit.Label(card, "HARİTA", monoFont, 11, UiTheme.Primary, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(12f, -9f), new Vector2(80f, 14f));
            mapRoomText = kit.Label(card, "", monoFont, 11, UiTheme.Text, TextAnchor.UpperRight, UiKit.TopLeft, UiKit.TopLeft, new Vector2(70f, -9f), new Vector2(MapW - 58f, 14f));

            mapHolder = kit.Rt("Holder", card, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -28f), new Vector2(MapW, h));
            kit.Img(mapHolder, new Color(0.01f, 0.03f, 0.04f, 0.88f));

            // hedef oda vurgusu (efsane bulgu için)
            legendFrame = kit.Rt("LegendRoom", mapHolder, UiKit.BottomLeft, UiKit.BottomLeft, Vector2.zero, new Vector2(10f, 10f));
            kit.Img(legendFrame, UiTheme.WithAlpha(UiTheme.Danger, 0.9f), bracket, Image.Type.Sliced);
            legendFrame.gameObject.SetActive(false);

            var tex = kit.Rt("Tex", mapHolder, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(MapW, h));
            mapRaw = tex.gameObject.AddComponent<RawImage>();
            mapRaw.texture = map.Texture;
            mapRaw.raycastTarget = false;

            foreach (var r in HouseLayout.Rooms)
            {
                var t = kit.Label(mapHolder, r.Name, monoFont, 8, UiTheme.WithAlpha(UiTheme.Text, 0.0f), TextAnchor.MiddleCenter, UiKit.BottomLeft, UiKit.Center, ToMap(r.Center), new Vector2(90f, 10f));
                roomLabels.Add(t);
            }

            baseMark = kit.Rt("Base", mapHolder, UiKit.BottomLeft, UiKit.Center, ToMap(HouseLayout.BasePosition), new Vector2(9f, 9f));
            baseMark.localRotation = Quaternion.Euler(0f, 0f, 45f);
            kit.Img(baseMark, UiTheme.Success);

            sweep = kit.Rt("Sweep", mapHolder, UiKit.BottomLeft, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(1.5f, 3f * PxPerMeter));
            kit.Img(sweep, UiTheme.WithAlpha(UiTheme.Primary, 0.45f));
            meRoot = kit.Rt("Me", mapHolder, UiKit.BottomLeft, UiKit.Center, Vector2.zero, new Vector2(7f, 7f));
            kit.Img(meRoot, UiTheme.Text);
            kit.Img(kit.Rt("Nose", meRoot, UiKit.Center, new Vector2(0.5f, 0f), new Vector2(0f, 3f), new Vector2(2f, 8f)), UiTheme.Text);
        }

        void BuildCenter()
        {
            roomText = kit.Label(root, "", monoFont, 12, UiTheme.WithAlpha(UiTheme.Text, 0.7f), TextAnchor.MiddleCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -24f), new Vector2(500f, 18f));
            noticeText = kit.Label(root, "", titleFont, 32, UiTheme.Primary, TextAnchor.MiddleCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -56f), new Vector2(900f, 44f));
            UiKit.Shadow(noticeText);

            promptName = kit.Label(root, "", titleFont, 26, UiTheme.Text, TextAnchor.MiddleCenter, UiKit.BottomCenter, UiKit.BottomCenter, new Vector2(0f, 190f), new Vector2(700f, 34f));
            UiKit.Shadow(promptName);
            promptHint = kit.Label(root, "", monoFont, 13, UiTheme.Primary, TextAnchor.MiddleCenter, UiKit.BottomCenter, UiKit.BottomCenter, new Vector2(0f, 168f), new Vector2(700f, 18f));
            UiKit.Shadow(promptHint);

            // sessizlik göstergesi (ürkek bulgular için)
            noiseLabel = kit.Label(root, "SESSİZLİK", monoFont, 10, UiTheme.WithAlpha(UiTheme.Text, 0.7f), TextAnchor.MiddleCenter, UiKit.BottomCenter, UiKit.BottomCenter, new Vector2(0f, 148f), new Vector2(200f, 14f));
            noiseTrack = kit.Rt("NoiseTrack", root, UiKit.BottomCenter, UiKit.BottomCenter, new Vector2(0f, 140f), new Vector2(140f, 3f));
            kit.Img(noiseTrack, UiTheme.Track);
            noiseFill = kit.Rt("NoiseFill", root, UiKit.BottomCenter, new Vector2(0.5f, 0f), new Vector2(0f, 140f), new Vector2(0f, 3f));
            noiseImage = kit.Img(noiseFill, UiTheme.Success);

            ledLabel = kit.Label(root, "", monoFont, 12, UiTheme.TextDim, TextAnchor.MiddleCenter, UiKit.BottomCenter, UiKit.BottomCenter, new Vector2(0f, 90f), new Vector2(520f, 16f));
            UiKit.Shadow(ledLabel);
            kit.Img(kit.Rt("LedTrack", root, UiKit.BottomCenter, UiKit.BottomCenter, new Vector2(0f, 82f), new Vector2(LedBarWidth, 3f)), UiTheme.Track);
            ledFill = kit.Rt("LedFill", root, UiKit.BottomCenter, new Vector2(0.5f, 0f), new Vector2(0f, 82f), new Vector2(0f, 3f));
            ledFillImage = kit.Img(ledFill, UiTheme.Warning);

            baseText = kit.Label(root, "", titleFont, 22, UiTheme.Success, TextAnchor.MiddleCenter, UiKit.BottomCenter, UiKit.BottomCenter, new Vector2(0f, 112f), new Vector2(700f, 28f));
            UiKit.Shadow(baseText);

            var hint = kit.Label(root, "W A S D  SÜR     ·     SHIFT  SESSİZ     ·     Q E  ETRAFA BAK     ·     L  LED     ·     X  SONAR     ·     U  LAB (ÜSTE)     ·     ESC",
                monoFont, 11, UiTheme.WithAlpha(UiTheme.Text, 0.6f), TextAnchor.MiddleCenter, UiKit.BottomCenter, UiKit.BottomCenter, new Vector2(0f, 24f), new Vector2(1000f, 18f));
            UiKit.Shadow(hint);
        }

        void BuildTags()
        {
            for (int i = 0; i < 10; i++)
            {
                var rt = kit.Rt("Tag", root, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(72f, 72f));
                var tag = new Tag { Rt = rt };
                tag.Frame = kit.Img(rt, UiTheme.Primary, bracket, Image.Type.Sliced);
                tag.Name = kit.Label(rt, "", bodyFont, 17, UiTheme.Text, TextAnchor.MiddleCenter, UiKit.Center, UiKit.Center, new Vector2(0f, -52f), new Vector2(240f, 22f));
                tag.Info = kit.Label(rt, "", monoFont, 12, UiTheme.Primary, TextAnchor.MiddleCenter, UiKit.Center, UiKit.Center, new Vector2(0f, -70f), new Vector2(240f, 16f));
                UiKit.Shadow(tag.Name);
                UiKit.Shadow(tag.Info);
                rt.gameObject.SetActive(false);
                tags.Add(tag);
            }
        }

        // ------------------------------------------------------------------ olaylar

        void OnNotice(string message)
        {
            noticeText.text = message;
            noticeTimer = 3.0f;
        }

        void OnComms(string message)
        {
            if (commsFull.Length == 0) { commsFull = message; commsShown = 0f; }
            else commsQueue.Enqueue(message);
        }

        // ------------------------------------------------------------------ her kare

        public void Tick(float dt)
        {
            if (!built || rootGroup.alpha < 0.01f) return;
            var tel = robot.Telemetry;

            // pil / vites / sonar
            fuelFill.sizeDelta = new Vector2(BarWidth * tel.Battery01, 6f);
            int fuel = Mathf.RoundToInt(tel.Battery01 * 100f);
            if (fuel != shownFuel) { shownFuel = fuel; fuelValue.text = fuel + "%"; }
            fuelImage.color = tel.Battery01 < 0.15f ? UiTheme.Danger : tel.Battery01 < 0.3f ? UiTheme.Warning : UiTheme.Primary;

            powerFill.sizeDelta = new Vector2(BarWidth * robot.PwmLimit, 6f);
            int power = Mathf.RoundToInt(robot.PwmLimit * 100f);
            if (power != shownPower) { shownPower = power; powerValue.text = power + "%"; }

            float ready = 1f - expedition.SonarCooldownLeft / Mathf.Max(0.1f, expedition.Profile.SonarCooldown);
            sonarFill.sizeDelta = new Vector2(BarWidth * ready, 6f);
            bool sonarReady = expedition.SonarCooldownLeft <= 0f;
            sonarImage.color = sonarReady ? UiTheme.Success : UiTheme.Warning;
            sonarValue.text = sonarReady ? "HAZIR" : expedition.SonarCooldownLeft.ToString("0.0") + " sn";

            SetMotor(motorLeft, robot.LastCommand.Left * robot.PwmLimit);
            SetMotor(motorRight, robot.LastCommand.Right * robot.PwmLimit);

            UpdateChapter();
            UpdateMap(dt);
            UpdateTags();
            UpdatePrompt();
            UpdateLed(dt);
            UpdateComms(dt);
            UpdateBanners(dt);
        }

        void UpdateChapter()
        {
            var c = expedition.Chapter;
            int key = expedition.ChapterIndex * 100 + expedition.ChapterProgress + (expedition.IsFinished ? 5000 : 0);
            if (key != shownChapter)
            {
                shownChapter = key;
                int chapters = expedition.Theme.Chapters.Length;
                string title = c.Title;
                int dot = title.IndexOf('·');
                chapterHeader.text = expedition.IsFinished ? "SERBEST GEZİ" : "BÖLÜM " + (expedition.ChapterIndex + 1) + " / " + chapters;
                chapterTitle.text = expedition.IsFinished ? "KEŞFE DEVAM" : (dot >= 0 ? title.Substring(dot + 1).Trim() : title);
                goalText.text = expedition.IsFinished ? "Yeni bulgular çıkmaya devam ediyor. Öz topla, robotu geliştir." : expedition.GoalText();
                goalCount.text = expedition.IsFinished ? "" : expedition.ChapterProgress + " / " + c.Count;
                goalFill.rectTransform.sizeDelta = new Vector2(308f * (expedition.IsFinished ? 1f : Mathf.Clamp01(expedition.ChapterProgress / (float)c.Count)), 3f);
            }
            int carried = expedition.Carried.Count;
            if (carried != shownCarried || expedition.Essence != shownEssence)
            {
                shownCarried = carried;
                shownEssence = expedition.Essence;
                carryText.text = "TAŞINAN " + carried + "   ·   ÖZ " + expedition.Essence + (carried > 0 ? "   ·   ÜSSE GÖTÜR" : "");
                carryText.color = carried > 0 ? UiTheme.Warning : UiTheme.TextDim;
            }
        }

        void UpdateMap(float dt)
        {
            Vector2 me = ToMap(robot.transform.position);
            meRoot.anchoredPosition = me;
            meRoot.localRotation = Quaternion.Euler(0f, 0f, -robot.Telemetry.YawDegrees);
            sweep.anchoredPosition = me;
            sweep.localRotation *= Quaternion.Euler(0f, 0f, -140f * dt);
            baseMark.localScale = Vector3.one * (1f + 0.15f * Mathf.Sin(Time.realtimeSinceStartup * 4f));

            var room = expedition.CurrentRoom;
            roomText.text = room != null ? room.Name : "";
            mapRoomText.text = room != null ? room.Name : "";

            // gezilen odaların adı haritada belirir
            for (int i = 0; i < roomLabels.Count; i++)
            {
                bool seen = expedition.IsVisited(HouseLayout.Rooms[i]);
                var c = roomLabels[i].color;
                c.a = seen ? 0.55f : 0f;
                roomLabels[i].color = c;
            }

            // efsane bulgunun odası
            var legend = expedition.IsLegendPending ? expedition.LegendRoom : null;
            if (legendFrame.gameObject.activeSelf != (legend != null)) legendFrame.gameObject.SetActive(legend != null);
            if (legend != null)
            {
                Vector2 a = ToMap(new Vector3(legend.Rect.xMin, 0f, legend.Rect.yMin));
                Vector2 b = ToMap(new Vector3(legend.Rect.xMax, 0f, legend.Rect.yMax));
                legendFrame.anchoredPosition = a;
                legendFrame.sizeDelta = b - a;
                var img = legendFrame.GetComponent<Image>();
                img.color = UiTheme.WithAlpha(UiTheme.Danger, 0.35f + 0.5f * Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup * 3f)));
            }

            SyncBlips();
        }

        void SyncBlips()
        {
            var list = expedition.Finds;
            while (blips.Count < list.Count)
            {
                var r = kit.Rt("Blip", mapHolder, UiKit.BottomLeft, UiKit.Center, Vector2.zero, new Vector2(7f, 7f));
                blips.Add(new Blip { Image = kit.Img(r, UiTheme.Primary) });
            }
            for (int i = 0; i < blips.Count; i++)
            {
                var b = blips[i];
                Find f = i < list.Count ? list[i] : null;
                bool show = f != null && f.Alive && (f.Revealed || expedition.IsNearbyUnseen(f, 4.2f));
                if (b.Image.gameObject.activeSelf != show) b.Image.gameObject.SetActive(show);
                if (!show) continue;
                Vector2 p = ToMap(f.Position);
                if (!f.Revealed)    // henüz görülmemiş: belirsiz "yakında bir şey var" noktası
                    p += new Vector2(Mathf.Sin(Time.time * 1.7f + i), Mathf.Cos(Time.time * 1.3f + i)) * 5f;
                b.Image.rectTransform.anchoredPosition = p;
                var c = KindColor(f.Kind);
                c.a = f.Revealed ? 1f : 0.35f + 0.25f * Mathf.Sin(Time.time * 5f);
                b.Image.color = c;
                b.Image.rectTransform.localRotation = Quaternion.Euler(0f, 0f, f.Kind == FindKind.Legend ? 45f : 0f);
                b.Image.rectTransform.localScale = Vector3.one * (f.Kind == FindKind.Legend ? 1.5f : 1f);
            }
        }

        Color KindColor(FindKind kind)
        {
            switch (kind)
            {
                case FindKind.Shy: return UiTheme.Warning;
                case FindKind.Lure: return UiTheme.Primary;
                case FindKind.Hidden: return UiTheme.Text;
                case FindKind.Legend: return UiTheme.Danger;
                default: return UiTheme.Success;
            }
        }

        Vector2 ToMap(Vector3 world) => new Vector2((world.x - map.Origin.x) / map.Size.x * MapW, (world.z - map.Origin.y) / map.Size.y * MapH);

        void UpdateTags()
        {
            var cam = viewer.ViewCamera;
            var list = expedition.Finds;
            int used = 0;
            for (int i = 0; i < list.Count && used < tags.Count; i++)
            {
                var f = list[i];
                if (f == null || !f.Alive || !f.Revealed) continue;
                Vector3 world = f.Position + Vector3.up * (f.Kind == FindKind.Lure || f.Kind == FindKind.Legend ? 0.42f : 0.3f);
                Vector3 screen = cam.WorldToScreenPoint(rig.ToRig(world));
                bool visible = screen.z > 0.15f && screen.x > -40f && screen.x < cam.pixelWidth + 40f && screen.y > -40f && screen.y < cam.pixelHeight + 40f;
                if (!visible) continue;

                var tag = tags[used++];
                tag.Find = f;
                tag.Rt.gameObject.SetActive(true);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen,
                    canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : cam, out Vector2 local);
                tag.Rt.anchoredPosition = local;

                float dist = Vector3.Distance(new Vector3(robot.transform.position.x, 0f, robot.transform.position.z), f.Position);
                var names = expedition.Theme.NamesOf(f.Kind);
                Color color = KindColor(f.Kind);
                tag.Frame.color = color;
                tag.Name.text = names.Name + (f.Tier > 1 ? "  ★" : "");
                tag.Info.text = f.Progress > 0.02f ? "KAYIT  %" + Mathf.RoundToInt(f.Progress * 100f) : dist.ToString("0.0") + " m";
                tag.Info.color = color;
            }
            for (int i = used; i < tags.Count; i++)
                if (tags[i].Rt.gameObject.activeSelf) tags[i].Rt.gameObject.SetActive(false);
        }

        void UpdatePrompt()
        {
            var f = expedition.NearestRevealed(3.2f);
            float progress = 0f;
            if (f != null)
            {
                var names = expedition.Theme.NamesOf(f.Kind);
                promptName.text = names.Name + (f.Tier > 1 ? "  ★" : "");
                string hint = names.Hint.ToUpperInvariant();
                if (f.Kind == FindKind.Lure && robot.LedOn) hint = "IŞIĞI KAPATMA  ·  YANINDA BEKLE";
                else if (f.Kind == FindKind.Legend && robot.LedOn) hint = "SESSİZ KAL  ·  YANINDA BEKLE";
                promptHint.text = f.State == FindState.Fleeing ? "KAÇIYOR" : hint;
                promptHint.color = f.State == FindState.Fleeing ? UiTheme.Danger : UiTheme.Primary;
                progress = f.Progress;
            }
            else
            {
                promptName.text = "";
                promptHint.text = "";
            }
            captureRing.fillAmount = progress;

            // sessizlik göstergesi: ürkek bir bulgu yakındaysa göster
            bool shyNear = f != null && (f.Kind == FindKind.Shy || f.Kind == FindKind.Legend);
            float noise = expedition.Noise;
            noiseLabel.gameObject.SetActive(shyNear);
            noiseTrack.gameObject.SetActive(shyNear);
            noiseFill.gameObject.SetActive(shyNear);
            if (shyNear)
            {
                noiseFill.sizeDelta = new Vector2(140f * noise, 3f);
                noiseImage.color = noise < 0.2f ? UiTheme.Success : noise < 0.45f ? UiTheme.Warning : UiTheme.Danger;
            }

            // üs ipucu
            if (expedition.InBase)
                baseText.text = "ÜS  ·  U: LABORATUVAR  ·  ÖZ " + expedition.Essence;
            else baseText.text = "";
        }

        void UpdateLed(float dt)
        {
            ledLevel = Mathf.MoveTowards(ledLevel, robot.LedOn ? 1f : 0f, dt * 6f);
            ledFill.sizeDelta = new Vector2(LedBarWidth * ledLevel, 3f);
            ledLabel.text = robot.LedOn ? "LED AÇIK  ·  PİL AKIYOR" : "LED  ·  L YA DA SOL TIK (BASILI TUT)";
            ledLabel.color = robot.LedOn ? UiTheme.Warning : UiTheme.WithAlpha(UiTheme.Text, 0.6f);
        }

        void UpdateComms(float dt)
        {
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
                if (commsQueue.Count > 0 && commsHold > 3.5f)
                {
                    commsFull = commsQueue.Dequeue();
                    commsShown = 0f;
                }
            }
            bool visible = commsFull.Length > 0 && (commsShown < commsFull.Length || commsHold < 8f || commsQueue.Count > 0);
            commsGroup.alpha = Mathf.MoveTowards(commsGroup.alpha, visible ? 1f : 0f, dt * 2.5f);
        }

        void UpdateBanners(float dt)
        {
            noticeTimer -= dt;
            var c = UiTheme.Primary;
            c.a = Mathf.Clamp01(noticeTimer / 0.6f);
            noticeText.color = c;
            if (noticeTimer <= 0f) noticeText.text = "";
        }

        static void SetMotor(RectTransform fill, float v)
        {
            float half = MotorTrack * 0.5f;
            float w = Mathf.Abs(v) * half;
            fill.sizeDelta = new Vector2(w, 4f);
            fill.anchoredPosition = new Vector2(70f + half + Mathf.Min(0f, v) * half, fill.anchoredPosition.y);
        }
    }
}
