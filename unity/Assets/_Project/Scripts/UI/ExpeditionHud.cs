using System.Collections.Generic;
using ARaki.AR;
using ARaki.Game;
using ARaki.Robot;
using ARaki.Sensing;
using ARaki.Theming;
using ARaki.Video;
using ARaki.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ARaki.UI
{
    /// <summary>
    /// Sade oyun arayüzü. Yazı yalnızca sol üstte: tek satır hedef + tek satır ipucu.
    /// Geri kalanı simgedir: pil çubuğu, taşınan kayıtlar, nişan + yakalama halkası,
    /// sağ altta ev haritası (gezdikçe açılır) ve ekran çevresinde yön üçgenleri (kırmızı = tehdit, yeşil = üs). Tab tehdide, B arkaya döner.
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

        const float CommsCharsPerSecond = 42f;
        const int MaxMarkers = 4;
        const int MaxPips = 8;

        UiKit kit;
        Canvas canvas;
        CanvasGroup rootGroup;
        RectTransform root;
        RectTransform batteryFill, sonarFill, noiseFill, noiseTrack, mapHolder, meRoot, sweep, baseMark, legendFrame;
        Image batteryImage, sonarImage, captureRing, dangerFrame, dangerTint, noiseImage;
        Text goalText, tipText, noticeText, essenceText;
        RawImage mapRaw;
        readonly List<Image> findBlips = new List<Image>();
        readonly List<Image> ghostBlips = new List<Image>();
        readonly List<Text> roomLabels = new List<Text>();
        Sprite triangle;
        readonly List<Image> pips = new List<Image>();
        readonly List<Image> markers = new List<Image>();
        readonly Queue<string> commsQueue = new Queue<string>();
        string commsFull = "";
        float commsShown, commsHold, noticeTimer;
        string shownGoal = "";
        bool built;

        // öğretici: ilk oyunda tuşları sırayla yaptırır (ENTER ile geçilir)
        const string TutorialKey = "Tenha.Tutorial";
        const int TutorialSteps = 8;
        bool tutorialDone;
        int tutStep, bPresses;
        float tutStepTime;
        bool tutSonar, tutZoom, tutTab, tutInit;
        Vector3 tutStartPos;
        float tutStartYaw;
        Image baseMarker;

        const float BatteryWidth = 150f;
        const float MapW = 190f;
        float PxPerMeter => MapW / map.Size.x;
        float MapH => MapW * map.Size.y / map.Size.x;

        void Awake() => Build();
        void LateUpdate() => Tick(Time.deltaTime);

        void OnDestroy()
        {
            if (expedition == null) return;
            expedition.Notice -= OnNotice;
            expedition.Comms -= OnComms;
            expedition.SonarFired -= OnSonarFired;
        }

        // ------------------------------------------------------------------ kurulum

        public void Build()
        {
            if (built) return;
            built = true;
            UiTheme.EnsureApplied();
            if (map != null) map.Init();
            kit = new UiKit { Title = titleFont, Body = bodyFont, Mono = monoFont, Bracket = bracket, Ring = ring };
            triangle = MakeTriangle();

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

            BuildDanger();
            BuildMarkers();
            BuildCenter();
            BuildTips();
            BuildBattery();
            BuildMap();

            expedition.Notice += OnNotice;
            expedition.Comms += OnComms;
            expedition.SonarFired += OnSonarFired;
            tutorialDone = PlayerPrefs.GetInt(TutorialKey, 0) == 1;
        }

        void OnSonarFired() => tutSonar = true;

        /// <summary>Öğreticiyi baştan başlatır (menüden "yeni oyun" ya da testler için).</summary>
        public void RestartTutorial()
        {
            tutorialDone = false;
            tutStep = 0; bPresses = 0; tutInit = false;
            PlayerPrefs.DeleteKey(TutorialKey);
        }

        public void SetVisible(bool visible)
        {
            if (rootGroup == null) return;
            rootGroup.alpha = visible ? 1f : 0f;
        }

        static Sprite MakeTriangle()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fy = y / (float)(n - 1);                       // 0 alt, 1 üst
                    float half = (1f - fy) * 0.5f;                        // yukarı doğru daralır
                    float dx = Mathf.Abs(x / (float)(n - 1) - 0.5f);
                    float edge = Mathf.Clamp01((half - dx) * n * 0.5f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(edge * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 64f);
        }

        /// <summary>Gölge yaklaşınca ekran kenarları kırmızıya döner ve titrer.</summary>
        void BuildDanger()
        {
            dangerTint = kit.Img(kit.Stretch("DangerTint", root), UiTheme.WithAlpha(UiTheme.Danger, 0f));
            dangerFrame = kit.Img(kit.Stretch("DangerFrame", root), UiTheme.WithAlpha(UiTheme.Danger, 0f), bracket, Image.Type.Sliced);
        }

        void BuildMarkers()
        {
            for (int i = 0; i < MaxMarkers; i++)
            {
                var m = kit.Img(kit.Rt("Threat", root, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(34f, 34f)), UiTheme.Danger, triangle);
                m.gameObject.SetActive(false);
                markers.Add(m);
            }
            baseMarker = kit.Img(kit.Rt("BaseArrow", root, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(26f, 26f)), UiTheme.Success, triangle);
            baseMarker.gameObject.SetActive(false);
        }

        void BuildCenter()
        {
            kit.Img(kit.Rt("Crosshair", root, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(30f, 30f)), UiTheme.WithAlpha(UiTheme.Text, 0.35f), ring);
            kit.Img(kit.Rt("Dot", root, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(3f, 3f)), UiTheme.WithAlpha(UiTheme.Text, 0.7f));

            kit.Img(kit.Rt("CaptureBack", root, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(78f, 78f)), UiTheme.WithAlpha(UiTheme.Text, 0.12f), ring);
            captureRing = kit.Img(kit.Rt("CaptureRing", root, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(78f, 78f)), UiTheme.Warning, ring);
            captureRing.type = Image.Type.Filled;
            captureRing.fillMethod = Image.FillMethod.Radial360;
            captureRing.fillOrigin = 2;
            captureRing.fillAmount = 0f;

            // sessizlik çubuğu: yalnızca ürkek bir şey ya da gölge yakınken görünür
            noiseTrack = kit.Rt("NoiseTrack", root, UiKit.Center, UiKit.Center, new Vector2(0f, -58f), new Vector2(64f, 3f));
            kit.Img(noiseTrack, UiTheme.WithAlpha(UiTheme.Text, 0.2f));
            noiseFill = kit.Rt("NoiseFill", noiseTrack, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 3f));
            noiseImage = kit.Img(noiseFill, UiTheme.Success);

            noticeText = kit.Label(root, "", titleFont, 26, UiTheme.Primary, TextAnchor.MiddleCenter, UiKit.Center, UiKit.Center, new Vector2(0f, 120f), new Vector2(900f, 34f));
            UiKit.Shadow(noticeText);
        }

        void BuildTips()
        {
            goalText = kit.Label(root, "", monoFont, 15, UiTheme.WithAlpha(UiTheme.Text, 0.8f), TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(24f, -20f), new Vector2(560f, 20f));
            UiKit.Shadow(goalText);
            tipText = kit.Label(root, "", bodyFont, 25, UiTheme.Text, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(24f, -46f), new Vector2(560f, 90f));
            tipText.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.Shadow(tipText);
        }

        void BuildBattery()
        {
            var track = kit.Rt("BatteryTrack", root, UiKit.TopRight, UiKit.TopRight, new Vector2(-24f, -24f), new Vector2(BatteryWidth, 6f));
            kit.Img(track, UiTheme.WithAlpha(UiTheme.Text, 0.2f));
            batteryFill = kit.Rt("BatteryFill", track, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(BatteryWidth, 6f));
            batteryImage = kit.Img(batteryFill, UiTheme.Primary);

            var sTrack = kit.Rt("SonarTrack", root, UiKit.TopRight, UiKit.TopRight, new Vector2(-24f, -36f), new Vector2(BatteryWidth, 2f));
            kit.Img(sTrack, UiTheme.WithAlpha(UiTheme.Text, 0.15f));
            sonarFill = kit.Rt("SonarFill", sTrack, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(BatteryWidth, 2f));
            sonarImage = kit.Img(sonarFill, UiTheme.Success);

            for (int i = 0; i < MaxPips; i++)
            {
                var p = kit.Img(kit.Rt("Pip", root, UiKit.TopRight, UiKit.TopRight, new Vector2(-24f - i * 14f, -50f), new Vector2(10f, 10f)), UiTheme.Warning);
                p.gameObject.SetActive(false);
                pips.Add(p);
            }
            essenceText = kit.Label(root, "", monoFont, 15, UiTheme.WithAlpha(UiTheme.Text, 0.75f), TextAnchor.UpperRight, UiKit.TopRight, UiKit.TopRight, new Vector2(-24f, -66f), new Vector2(200f, 18f));
            UiKit.Shadow(essenceText);
        }

        // ------------------------------------------------------------------ harita (sağ alt)

        void BuildMap()
        {
            float h = MapH;
            var card = kit.Card("Map", root, UiKit.BottomRight, UiKit.BottomRight, new Vector2(-24f, 24f), new Vector2(MapW + 16f, h + 16f), UiTheme.WithAlpha(UiTheme.Text, 0.35f));
            mapHolder = kit.Rt("Holder", card, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(MapW, h));
            kit.Img(mapHolder, new Color(0.02f, 0.02f, 0.03f, 0.6f));

            legendFrame = kit.Rt("LegendRoom", mapHolder, UiKit.BottomLeft, UiKit.BottomLeft, Vector2.zero, new Vector2(10f, 10f));
            kit.Img(legendFrame, UiTheme.WithAlpha(UiTheme.Danger, 0.9f), bracket, Image.Type.Sliced);
            legendFrame.gameObject.SetActive(false);

            var tex = kit.Rt("Tex", mapHolder, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(MapW, h));
            mapRaw = tex.gameObject.AddComponent<RawImage>();
            mapRaw.texture = map.Texture;
            mapRaw.raycastTarget = false;

            foreach (var r in HouseLayout.Rooms)
            {
                var t = kit.Label(mapHolder, r.Name, monoFont, 8, UiTheme.WithAlpha(UiTheme.Text, 0f), TextAnchor.MiddleCenter, UiKit.BottomLeft, UiKit.Center, ToMap(r.Center), new Vector2(90f, 10f));
                roomLabels.Add(t);
            }

            baseMark = kit.Rt("Base", mapHolder, UiKit.BottomLeft, UiKit.Center, ToMap(HouseLayout.BasePosition), new Vector2(10f, 10f));
            baseMark.localRotation = Quaternion.Euler(0f, 0f, 45f);
            kit.Img(baseMark, UiTheme.Success);

            sweep = kit.Rt("Sweep", mapHolder, UiKit.BottomLeft, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(1.5f, 3f * PxPerMeter));
            kit.Img(sweep, UiTheme.WithAlpha(UiTheme.Primary, 0.45f));
            meRoot = kit.Rt("Me", mapHolder, UiKit.BottomLeft, UiKit.Center, Vector2.zero, new Vector2(7f, 7f));
            kit.Img(meRoot, UiTheme.Text);
            kit.Img(kit.Rt("Nose", meRoot, UiKit.Center, new Vector2(0.5f, 0f), new Vector2(0f, 3f), new Vector2(2f, 8f)), UiTheme.Text);
        }

        Vector2 ToMap(Vector3 world) => new Vector2((world.x - map.Origin.x) / map.Size.x * MapW, (world.z - map.Origin.y) / map.Size.y * MapH);

        void UpdateMap(float dt)
        {
            Vector2 me = ToMap(robot.transform.position);
            meRoot.anchoredPosition = me;
            meRoot.localRotation = Quaternion.Euler(0f, 0f, -robot.Telemetry.YawDegrees);
            sweep.anchoredPosition = me;
            sweep.localRotation *= Quaternion.Euler(0f, 0f, -140f * dt);
            baseMark.localScale = Vector3.one * (1f + 0.15f * Mathf.Sin(Time.realtimeSinceStartup * 4f));

            // gezilen odaların adı haritada belirir
            for (int i = 0; i < roomLabels.Count; i++)
            {
                var c = roomLabels[i].color;
                c.a = expedition.IsVisited(HouseLayout.Rooms[i]) ? 0.55f : 0f;
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
                legendFrame.GetComponent<Image>().color = UiTheme.WithAlpha(UiTheme.Danger, 0.35f + 0.5f * Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup * 3f)));
            }

            SyncBlips();
        }

        void SyncBlips()
        {
            var list = expedition.Finds;
            while (findBlips.Count < list.Count)
                findBlips.Add(kit.Img(kit.Rt("Blip", mapHolder, UiKit.BottomLeft, UiKit.Center, Vector2.zero, new Vector2(7f, 7f)), UiTheme.Primary));
            for (int i = 0; i < findBlips.Count; i++)
            {
                var b = findBlips[i];
                Find f = i < list.Count ? list[i] : null;
                bool show = f != null && f.Alive && (f.Revealed || expedition.IsNearbyUnseen(f, 6f));
                if (b.gameObject.activeSelf != show) b.gameObject.SetActive(show);
                if (!show) continue;
                Vector2 p = ToMap(f.Position);
                if (!f.Revealed) p += new Vector2(Mathf.Sin(Time.time * 1.7f + i), Mathf.Cos(Time.time * 1.3f + i)) * 5f;     // henüz görülmemiş: belirsiz nokta
                b.rectTransform.anchoredPosition = p;
                var c = f.Kind == FindKind.Legend ? UiTheme.Danger : f.Tier > 1 ? UiTheme.Warning : UiTheme.Primary;
                c.a = f.Revealed ? 1f : 0.35f + 0.25f * Mathf.Sin(Time.time * 5f);
                b.color = c;
                b.rectTransform.localRotation = Quaternion.Euler(0f, 0f, f.Kind == FindKind.Legend ? 45f : 0f);
            }

            var ghosts = expedition.Ghosts;
            while (ghostBlips.Count < ghosts.Count)
                ghostBlips.Add(kit.Img(kit.Rt("GhostBlip", mapHolder, UiKit.BottomLeft, UiKit.Center, Vector2.zero, new Vector2(10f, 10f)), UiTheme.Danger, ring));
            for (int i = 0; i < ghostBlips.Count; i++)
            {
                var g = i < ghosts.Count ? ghosts[i] : null;
                bool show = g != null && g.Alive && (g.State == GhostState.Chase || Vector3.Distance(robot.transform.position, g.Position) < 4.2f);
                if (ghostBlips[i].gameObject.activeSelf != show) ghostBlips[i].gameObject.SetActive(show);
                if (!show) continue;
                ghostBlips[i].rectTransform.anchoredPosition = ToMap(g.Position);
                ghostBlips[i].color = UiTheme.WithAlpha(UiTheme.Danger, 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup * 6f)));
            }
        }

        // ------------------------------------------------------------------ olaylar

        void OnNotice(string message)
        {
            noticeText.text = message;
            noticeTimer = 2.6f;
        }

        void OnComms(string message)
        {
            if (commsFull.Length == 0) { commsFull = message; commsShown = 0f; commsHold = 0f; }
            else commsQueue.Enqueue(message);
        }

        // ------------------------------------------------------------------ her kare

        public void Tick(float dt)
        {
            if (!built || rootGroup.alpha < 0.01f) return;
            var tel = robot.Telemetry;

            // pil / sonar
            batteryFill.sizeDelta = new Vector2(BatteryWidth * tel.Battery01, 6f);
            batteryImage.color = tel.Battery01 < 0.15f ? UiTheme.Danger : tel.Battery01 < 0.3f ? UiTheme.Warning : UiTheme.Primary;
            float ready = 1f - expedition.SonarCooldownLeft / Mathf.Max(0.1f, expedition.Profile.SonarCooldown);
            sonarFill.sizeDelta = new Vector2(BatteryWidth * Mathf.Clamp01(ready), 2f);
            sonarImage.color = ready >= 1f ? UiTheme.Success : UiTheme.TextDim;

            int carried = expedition.Carried.Count;
            for (int i = 0; i < pips.Count; i++) pips[i].gameObject.SetActive(i < carried);
            essenceText.text = "ÖZ " + expedition.Essence;

            UpdateMap(dt);
            UpdateCenter();
            UpdateTips(dt);
            UpdateMarkers();
            UpdateDanger();

            noticeTimer -= dt;
            var c = UiTheme.Primary;
            c.a = Mathf.Clamp01(noticeTimer / 0.6f);
            noticeText.color = c;
            if (noticeTimer <= 0f) noticeText.text = "";

            var kb = Keyboard.current;
            if (kb != null && kb.tabKey.wasPressedThisFrame && TryThreatBearing(out float bearing, out _)) viewer.SnapTo(bearing);
        }

        /// <summary>En yakın gölgenin görüş yönüne değil robotun önüne göre açısı (derece, sağ = +) ve uzaklığı.</summary>
        bool TryThreatBearing(out float bearing, out float distance)
        {
            bearing = 0f; distance = float.MaxValue;
            Ghost best = null;
            foreach (var g in expedition.Ghosts)
            {
                if (g == null || !g.Alive) continue;
                float d = Vector3.Distance(robot.transform.position, g.Position);
                if (d < distance && (g.State == GhostState.Chase || d < 5f)) { distance = d; best = g; }
            }
            if (best == null) return false;
            bearing = BearingTo(best.Position);
            return true;
        }

        float BearingTo(Vector3 world)
        {
            Vector3 local = robot.transform.InverseTransformPoint(world);
            return Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        }

        void UpdateCenter()
        {
            var f = expedition.NearestRevealed(3.2f);
            captureRing.fillAmount = f != null ? f.Progress : 0f;

            bool relevant = (f != null && (f.Kind == FindKind.Shy || f.Kind == FindKind.Legend)) || expedition.GhostThreat > 0.05f;
            if (noiseTrack.gameObject.activeSelf != relevant) noiseTrack.gameObject.SetActive(relevant);
            if (!relevant) return;
            float noise = expedition.Noise;
            noiseFill.sizeDelta = new Vector2(64f * noise, 3f);
            noiseImage.color = noise > 0.5f ? UiTheme.Danger : noise > 0.2f ? UiTheme.Warning : UiTheme.Success;
        }

        // ---- sol üst: hedef + ipucu

        void UpdateTips(float dt)
        {
            string goal = expedition.IsFinished ? "SERBEST GEZİ" : (expedition.ChapterIndex + 1) + "/" + expedition.Theme.Chapters.Length + "  ·  " + expedition.GoalText() + "  (" + expedition.ChapterProgress + "/" + expedition.Chapter.Count + ")";
            if (goal != shownGoal) { shownGoal = goal; goalText.text = goal; }

            if (!tutorialDone && UpdateTutorial(dt)) return;

            // telsiz mesajı varsa o gösterilir (daktilo gibi), yoksa duruma göre ipucu
            if (commsFull.Length > 0)
            {
                commsShown = Mathf.Min(commsFull.Length, commsShown + dt * CommsCharsPerSecond);
                tipText.text = commsFull.Substring(0, Mathf.FloorToInt(commsShown));
                tipText.color = UiTheme.Text;
                if (commsShown >= commsFull.Length)
                {
                    commsHold += dt;
                    if (commsHold > 2.2f + commsFull.Length * 0.03f)
                    {
                        commsFull = commsQueue.Count > 0 ? commsQueue.Dequeue() : "";
                        commsShown = 0f; commsHold = 0f;
                    }
                }
                return;
            }

            string tip = ContextTip(out bool urgent);
            tipText.text = tip;
            tipText.color = urgent ? UiTheme.Danger : UiTheme.Text;
        }

        // ---- öğretici

        static readonly string[] TutorialTexts =
        {
            "W A S D ile sür",
            "Q / E ya da sağ tık basılı: etrafa bak (araba dönmez)",
            "B ile arkaya bak, tekrar B ile öne dön",
            "SHIFT basılı tutarak sür: sessiz gidersin, ürkek yaratıklar kaçmaz",
            "L basılı tut: LED yanar. Yaratıkları çeker, gölgeyi eritir",
            "X: sonar. Saklananları kısa süre gösterir",
            "Z basılı tut: dürbün (uzağı yakından gör)",
            "Gölge gelirse TAB bakışı ona çevirir. Şimdi TAB'a bas",
        };

        /// <summary>Öğretici adımını gösterir; sürüyorsa true döner (diğer ipuçları beklemede kalır).</summary>
        bool UpdateTutorial(float dt)
        {
            var kb = Keyboard.current;
            if (!tutInit)
            {
                tutInit = true;
                tutStartPos = robot.transform.position;
                tutStartYaw = viewer.Yaw;
                tutStepTime = 0f;
                tutSonar = tutZoom = tutTab = false;
                bPresses = 0;
            }
            tutStepTime += dt;

            if (kb != null)
            {
                if (kb.bKey.wasPressedThisFrame) bPresses++;
                if (kb.zKey.isPressed) tutZoom = true;
                if (kb.tabKey.wasPressedThisFrame) tutTab = true;
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) { FinishTutorial(); return false; }
            }

            bool done = false;
            switch (tutStep)
            {
                case 0: done = Vector3.Distance(robot.transform.position, tutStartPos) > 0.6f; break;
                case 1: done = Mathf.Abs(Mathf.DeltaAngle(viewer.Yaw, tutStartYaw)) > 35f; break;
                case 2: done = bPresses >= 2; break;
                case 3: done = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) && expedition.Noise > 0.01f; break;
                case 4: done = robot.LedOn; break;
                case 5: done = tutSonar; break;
                case 6: done = tutZoom; break;
                case 7: done = tutTab || tutStepTime > 12f; break;
            }

            goalText.text = "ÖĞRETİCİ  " + (tutStep + 1) + "/" + TutorialSteps + "   ·   geç: ENTER";
            shownGoal = "";
            tipText.text = TutorialTexts[tutStep];
            tipText.color = UiTheme.Primary;

            if (done)
            {
                tutStep++;
                tutInit = tutStep < TutorialSteps ? false : tutInit;
                if (tutStep >= TutorialSteps) { FinishTutorial(); return false; }
                OnNotice("TAMAM");
            }
            return true;
        }

        void FinishTutorial()
        {
            tutorialDone = true;
            PlayerPrefs.SetInt(TutorialKey, 1);
            PlayerPrefs.Save();
            OnNotice("HAZIRSIN  ·  EVCİKLERİ BUL");
        }

        string ContextTip(out bool urgent)
        {
            urgent = false;
            foreach (var g in expedition.Ghosts)
            {
                if (g == null || !g.Alive) continue;
                float d = Vector3.Distance(robot.transform.position, g.Position);
                if (g.State == GhostState.Chase && d < 6f)
                {
                    urgent = true;
                    return g.Burn > 0.1f ? "Işığı üzerinde tut, eriyor..." : "GÖLGE yaklaşıyor!  Kaç, ya da LED'i üzerine tut  ·  Tab: bak";
                }
                if (d < 3.5f) { urgent = true; return "Soğuk bir şey var yakında.  Sessiz kal  ·  Tab: bak"; }
            }

            var f = expedition.NearestRevealed(3.2f);
            if (f != null)
            {
                var names = expedition.Theme.NamesOf(f.Kind);
                string hint = names.Hint;
                if (f.State == FindState.Fleeing) hint = "kaçıyor";
                else if (f.Kind == FindKind.Shy && f.Alarm > 0.25f) hint = "tedirgin! Yavaşla  ·  SHIFT";
                else if (f.Kind == FindKind.Lure && robot.LedOn) hint = "Işığı kapatma, yanında bekle";
                else if (f.Kind == FindKind.Legend && robot.LedOn) hint = "Sessiz kal, yanında bekle";
                return names.Name + (f.Tier == 3 ? "  (çok nadir)" : f.Tier == 2 ? "  (nadir)" : "") + "  ·  " + hint;
            }
            float unseen = expedition.NearestUnseenDistance();
            if (unseen < 4.5f) return "Yakında bir şey var...  Etrafa bak";
            if (expedition.InBase) return expedition.Carried.Count > 0 ? "Teslim ediliyor..." : "Üs  ·  U: atölye";
            var room = expedition.CurrentRoom;
            return room != null ? room.Name : "";
        }

        // ---- ekran çevresinde yön üçgenleri

        void UpdateMarkers()
        {
            Vector2 half = root.rect.size * 0.5f;
            float radius = Mathf.Min(half.x, half.y) * 0.72f;
            float viewYaw = viewer.Yaw;
            int used = 0;
            float pulse = 0.65f + 0.35f * Mathf.Sin(Time.realtimeSinceStartup * 8f);

            foreach (var g in expedition.Ghosts)
            {
                if (g == null || !g.Alive || used >= MaxMarkers) continue;
                float d = Vector3.Distance(robot.transform.position, g.Position);
                bool show = g.State == GhostState.Chase || d < 4.5f;
                if (!show) continue;
                float rel = Mathf.DeltaAngle(viewYaw, BearingTo(g.Position));
                bool onScreen = Mathf.Abs(rel) < 30f;
                var m = markers[used++];
                m.gameObject.SetActive(true);
                PlaceOnRing(m.rectTransform, rel, radius, onScreen ? 0.55f : 1f);
                float size = Mathf.Lerp(24f, 46f, Mathf.Clamp01(1f - d / 6f));
                m.rectTransform.sizeDelta = new Vector2(size, size);
                m.color = UiTheme.WithAlpha(UiTheme.Danger, (g.State == GhostState.Chase ? 0.95f : 0.55f) * pulse);
            }
            for (int i = used; i < markers.Count; i++) markers[i].gameObject.SetActive(false);

            // üs oku: üs görüş alanında değilse ya da uzaksa
            float baseDistance = Vector3.Distance(robot.transform.position, expedition.BasePosition);
            float baseRel = Mathf.DeltaAngle(viewYaw, BearingTo(expedition.BasePosition));
            bool baseShow = baseDistance > 2.5f && !expedition.InBase;
            if (baseMarker.gameObject.activeSelf != baseShow) baseMarker.gameObject.SetActive(baseShow);
            if (baseShow)
            {
                PlaceOnRing(baseMarker.rectTransform, baseRel, radius * 0.92f, 1f);
                baseMarker.color = UiTheme.WithAlpha(UiTheme.Success, Mathf.Abs(baseRel) < 25f ? 0.3f : 0.8f);
            }
        }

        static void PlaceOnRing(RectTransform rt, float relDegrees, float radius, float scaleOut)
        {
            float a = relDegrees * Mathf.Deg2Rad;
            rt.anchoredPosition = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * (radius * scaleOut);
            rt.localRotation = Quaternion.Euler(0f, 0f, -relDegrees);
        }

        void UpdateDanger()
        {
            float t = expedition.GhostThreat;
            float pulse = 0.65f + 0.35f * Mathf.Sin(Time.realtimeSinceStartup * (4f + t * 8f));
            dangerTint.color = UiTheme.WithAlpha(UiTheme.Danger, t * 0.1f * pulse);
            dangerFrame.color = UiTheme.WithAlpha(UiTheme.Danger, Mathf.Clamp01(t * 1.4f) * pulse);
        }
    }
}
