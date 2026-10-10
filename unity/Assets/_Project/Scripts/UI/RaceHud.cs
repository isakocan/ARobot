using ARaki.Game;
using ARaki.Robot;
using ARaki.Video;
using UnityEngine;
using UnityEngine.UI;

namespace ARaki.UI
{
    /// <summary>
    /// Yarış arayüzü: zamanlama kulesi (toplam süre, tur, kapı göstergeleri), tur süreleri, pist haritası,
    /// ölçekli hız, yakıt, geri sayım ve sıradaki kapıyı gösteren yön oku. Renkler RaceTheme'den gelir.
    /// </summary>
    public class RaceHud : MonoBehaviour
    {
        [Header("Kaynaklar")]
        [SerializeField] MockRobotLink robot;
        [SerializeField] RaceController race;
        [SerializeField] PanoramaViewer viewer;
        [SerializeField] GameSession session;

        [Header("Tasarım")]
        [SerializeField] Font titleFont;
        [SerializeField] Font bodyFont;
        [SerializeField] Font monoFont;
        [SerializeField] Sprite bracket;
        [SerializeField] Sprite ring;

        const int MapW = 180, MapH = 150;
        const float MapScale = 30f;          // piksel / metre
        const float FuelBar = 160f;

        UiKit kit;
        RectTransform root, arrow, dot, nextMark, miniCard;
        CanvasGroup rootGroup;
        Canvas canvas;
        bool built;

        Text timer, lapLabel, notice, countdown, speed, fuelValue, recordText, gearValue, wrongWay;
        readonly Text[] lapRows = new Text[4];
        readonly Image[] chips = new Image[RaceController.MaxGates];
        readonly Image[] arrowLegs = new Image[2];
        RectTransform fuelFill;
        Image fuelImage;
        RawImage miniImage;
        Texture2D miniTexture;
        float noticeTimer, countdownPop;
        int shownTenths = -1, shownFuel = -1, shownSpeed = -1, shownGear = -1, shownChips = -1, shownLap = -1;
        RaceSpec mapSpec;
        bool hasMap;

        void Awake() => Build();
        void LateUpdate() => Tick(Time.deltaTime);

        void OnDestroy()
        {
            if (race == null) return;
            race.Notice -= OnNotice;
            race.CountdownTick -= OnCountdown;
            if (miniTexture != null) Destroy(miniTexture);
        }

        // ------------------------------------------------------------------ kurulum

        public void Build()
        {
            if (built) return;
            built = true;
            kit = new UiKit { Title = titleFont, Body = bodyFont, Mono = monoFont, Bracket = bracket, Ring = ring };

            var go = new GameObject("Race HUD Canvas");
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
            rootGroup.alpha = 0f;

            BuildTower();
            BuildLaps();
            BuildMap();
            BuildSpeed();
            BuildCenter();

            race.Notice += OnNotice;
            race.CountdownTick += OnCountdown;
        }

        public void SetVisible(bool visible)
        {
            if (rootGroup == null) return;
            rootGroup.alpha = visible ? 1f : 0f;
            if (visible) hasMap = false;     // yeni pist: haritayı yeniden çiz
        }

        static Color Border(float a = 0.9f) => new Color(RaceTheme.Primary.r, RaceTheme.Primary.g, RaceTheme.Primary.b, a);

        RectTransform RaceCard(string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rt = kit.Rt(name, root, anchor, pivot, pos, size);
            rt.gameObject.AddComponent<Canvas>();      // kendi alt-canvas'ı: yalnızca bu kart yeniden çizilir
            kit.Img(rt, RaceTheme.Panel);
            kit.Img(kit.Stretch("Frame", rt), Border(), bracket, Image.Type.Sliced);
            return rt;
        }

        void BuildTower()
        {
            var card = RaceCard("Tower", UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -22f), new Vector2(330f, 112f));
            kit.Label(card, "TOPLAM SÜRE", monoFont, 11, RaceTheme.Primary, TextAnchor.UpperCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -10f), new Vector2(300f, 14f));
            timer = kit.Label(card, "0:00.0", monoFont, 46, RaceTheme.Text, TextAnchor.MiddleCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -24f), new Vector2(300f, 54f));
            UiKit.Shadow(timer);
            lapLabel = kit.Label(card, "TUR 1 / 3", titleFont, 22, RaceTheme.Primary, TextAnchor.MiddleLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(18f, -82f), new Vector2(120f, 24f));

            // kapı göstergeleri: geçilen yeşil, sıradaki sarı yanıp söner, diğerleri soluk
            for (int i = 0; i < chips.Length; i++)
            {
                var r = kit.Rt("Chip", card, UiKit.TopRight, UiKit.TopRight, new Vector2(-18f - (chips.Length - 1 - i) * 17f, -88f), new Vector2(12f, 12f));
                r.localRotation = Quaternion.Euler(0f, 0f, 45f);
                chips[i] = kit.Img(r, RaceTheme.Track);
            }
        }

        void BuildLaps()
        {
            var card = RaceCard("Laps", UiKit.TopLeft, UiKit.TopLeft, new Vector2(24f, -24f), new Vector2(214f, 150f));
            kit.Label(card, "TUR SÜRELERİ", monoFont, 11, RaceTheme.Primary, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(15f, -11f), new Vector2(180f, 14f));
            for (int i = 0; i < 3; i++)
            {
                float y = -34f - i * 27f;
                kit.Label(card, "T" + (i + 1), bodyFont, 18, RaceTheme.TextDim, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(15f, y), new Vector2(40f, 22f));
                lapRows[i] = kit.Label(card, "--:--.-", monoFont, 18, RaceTheme.Text, TextAnchor.UpperRight, UiKit.TopLeft, UiKit.TopLeft, new Vector2(60f, y), new Vector2(138f, 22f));
            }
            kit.Img(kit.Rt("Rule", card, UiKit.TopLeft, UiKit.TopLeft, new Vector2(15f, -118f), new Vector2(184f, 1f)), Border(0.3f));
            recordText = kit.Label(card, "", monoFont, 12, RaceTheme.TextDim, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(15f, -125f), new Vector2(184f, 16f));
        }

        void BuildMap()
        {
            miniCard = RaceCard("Map", UiKit.TopRight, UiKit.TopRight, new Vector2(-24f, -24f), new Vector2(MapW + 24f, MapH + 36f));
            kit.Label(miniCard, "PİST", monoFont, 11, RaceTheme.Primary, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(12f, -9f), new Vector2(80f, 14f));
            var holder = kit.Rt("Holder", miniCard, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -26f), new Vector2(MapW, MapH));
            miniImage = holder.gameObject.AddComponent<RawImage>();
            miniImage.raycastTarget = false;
            miniImage.color = Color.white;

            dot = kit.Rt("Me", holder, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(10f, 14f));
            kit.Img(dot, RaceTheme.Text);
            nextMark = kit.Rt("Next", holder, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(18f, 18f));
            kit.Img(nextMark, RaceTheme.Primary, ring);
        }

        void BuildSpeed()
        {
            var card = RaceCard("Speed", UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(24f, 24f), new Vector2(236f, 100f));
            speed = kit.Label(card, "0", monoFont, 44, RaceTheme.Text, TextAnchor.MiddleLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(15f, -8f), new Vector2(110f, 52f));
            kit.Label(card, "KM/H  ÖLÇEK", monoFont, 11, RaceTheme.TextDim, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(112f, -26f), new Vector2(110f, 14f));
            kit.Label(card, "VİTES  (R F)", monoFont, 11, RaceTheme.TextDim, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(112f, -44f), new Vector2(110f, 14f));
            gearValue = kit.Label(card, "8", titleFont, 22, RaceTheme.Primary, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(196f, -38f), new Vector2(30f, 24f));

            kit.Label(card, "YAKIT", bodyFont, 14, RaceTheme.TextDim, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(15f, -66f), new Vector2(60f, 18f));
            fuelValue = kit.Label(card, "", monoFont, 13, RaceTheme.Text, TextAnchor.UpperRight, UiKit.TopLeft, UiKit.TopLeft, new Vector2(150f, -67f), new Vector2(70f, 18f));
            kit.Img(kit.Rt("Track", card, UiKit.TopLeft, UiKit.TopLeft, new Vector2(15f, -88f), new Vector2(FuelBar + 45f, 5f)), RaceTheme.Track);
            fuelFill = kit.Rt("Fill", card, UiKit.TopLeft, UiKit.TopLeft, new Vector2(15f, -88f), new Vector2(0f, 5f));
            fuelImage = kit.Img(fuelFill, RaceTheme.Primary);
        }

        void BuildCenter()
        {
            countdown = kit.Label(root, "", titleFont, 190, RaceTheme.Text, TextAnchor.MiddleCenter, UiKit.Center, UiKit.Center, new Vector2(0f, 40f), new Vector2(600f, 220f));
            UiKit.Shadow(countdown);
            notice = kit.Label(root, "", titleFont, 34, RaceTheme.Primary, TextAnchor.MiddleCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -150f), new Vector2(800f, 44f));
            UiKit.Shadow(notice);
            wrongWay = kit.Label(root, "", titleFont, 30, RaceTheme.Accent, TextAnchor.MiddleCenter, UiKit.Center, UiKit.Center, new Vector2(0f, 120f), new Vector2(600f, 40f));
            UiKit.Shadow(wrongWay);

            // yön oku: iki bacaklı yukarı bakan "^"; sıradaki kapının yönüne döner
            arrow = kit.Rt("Arrow", root, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(40f, 40f));
            for (int i = 0; i < 2; i++)
            {
                var leg = kit.Rt("Leg", arrow, UiKit.Center, UiKit.Center, new Vector2(i == 0 ? -8f : 8f, 0f), new Vector2(26f, 6f));
                leg.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 45f : -45f);
                arrowLegs[i] = kit.Img(leg, RaceTheme.Primary);
            }

            var hint = kit.Label(root, "W A S D  SÜR     ·     Q E  ETRAFA BAK     ·     R F  VİTES     ·     K  SES     ·     ESC  DURAKLAT",
                monoFont, 11, new Color(RaceTheme.Text.r, RaceTheme.Text.g, RaceTheme.Text.b, 0.55f), TextAnchor.MiddleCenter, UiKit.BottomCenter, UiKit.BottomCenter, new Vector2(0f, 24f), new Vector2(900f, 18f));
            UiKit.Shadow(hint);
        }

        // ------------------------------------------------------------------ olaylar

        void OnNotice(string message)
        {
            notice.text = message;
            noticeTimer = 2.6f;
            if (message == "TERS YÖN") { wrongWay.text = "TERS YÖN"; wrongWayTimer = 1.4f; }
        }

        float wrongWayTimer;

        void OnCountdown(int n)
        {
            countdownPop = 1f;
            countdown.text = n > 0 ? n.ToString() : "BAŞLA";
            countdown.color = n > 0 ? RaceTheme.Text : RaceTheme.Primary;
        }

        // ------------------------------------------------------------------ her kare

        public void Tick(float dt)
        {
            if (!built || rootGroup.alpha < 0.01f || race.Phase == RacePhase.Idle) return;

            if (!hasMap && race.Spec.Path != null) DrawMap();

            // süre ve tur
            int tenths = Mathf.FloorToInt(race.RaceTime * 10f);
            if (tenths != shownTenths)
            {
                shownTenths = tenths;
                timer.text = RaceController.FormatLap(race.RaceTime);
            }
            int lapKey = race.Lap * 100 + race.LapTimes.Count;
            if (lapKey != shownLap)
            {
                shownLap = lapKey;
                lapLabel.text = "TUR " + Mathf.Min(race.Lap, race.Spec.Laps) + " / " + race.Spec.Laps;
                float rec = RaceController.BestTotalRecord;
                recordText.text = rec > 0f ? "REKOR  " + RaceController.FormatLap(rec) : "HENÜZ REKOR YOK";
            }
            UpdateLaps();
            UpdateChips();

            // hız ve yakıt
            var tel = robot.Telemetry;
            int kmh = Mathf.RoundToInt(Mathf.Abs(tel.LeftSpeed + tel.RightSpeed) * 0.5f * 3.6f * 10f);
            if (kmh != shownSpeed) { shownSpeed = kmh; speed.text = kmh.ToString(); }
            int gear = Mathf.RoundToInt(robot.PwmLimit * 10f);
            if (gear != shownGear) { shownGear = gear; gearValue.text = gear.ToString(); }
            int fuel = Mathf.RoundToInt(tel.Battery01 * 100f);
            if (fuel != shownFuel)
            {
                shownFuel = fuel;
                fuelValue.text = fuel + "%";
                fuelFill.sizeDelta = new Vector2((FuelBar + 45f) * tel.Battery01, 5f);
                fuelImage.color = tel.Battery01 < 0.15f ? RaceTheme.Accent : tel.Battery01 < 0.3f ? RaceTheme.Primary : RaceTheme.Good;
            }

            UpdateMapMarkers();
            UpdateArrow();
            UpdateBanners(dt);
        }

        void UpdateLaps()
        {
            for (int i = 0; i < 3; i++)
            {
                if (i < race.LapTimes.Count)
                {
                    lapRows[i].text = RaceController.FormatLap(race.LapTimes[i]);
                    lapRows[i].color = Mathf.Approximately(race.LapTimes[i], race.BestLapThisRace) && race.LapTimes.Count > 1 ? RaceTheme.Good : RaceTheme.Text;
                }
                else if (i == race.Lap - 1 && race.Phase == RacePhase.Running)
                {
                    lapRows[i].text = RaceController.FormatLap(race.LapTime);
                    lapRows[i].color = RaceTheme.Primary;
                }
                else
                {
                    lapRows[i].text = "--:--.-";
                    lapRows[i].color = RaceTheme.TextDim;
                }
            }
        }

        void UpdateChips()
        {
            float blink = Mathf.Sin(Time.realtimeSinceStartup * 7f) > 0f ? 1f : 0.45f;
            for (int i = 0; i < chips.Length; i++)
            {
                bool used = i < race.GateCount;
                if (chips[i].gameObject.activeSelf != used) chips[i].gameObject.SetActive(used);
                if (!used) continue;
                Color c;
                if (race.IsGatePassed(i)) c = RaceTheme.Good;
                else if (i == race.NextGate) c = new Color(RaceTheme.Primary.r, RaceTheme.Primary.g, RaceTheme.Primary.b, blink);
                else c = i == 0 ? RaceTheme.Accent : new Color(1f, 1f, 1f, 0.28f);
                chips[i].color = c;
            }
        }

        void UpdateBanners(float dt)
        {
            noticeTimer -= dt;
            var nc = RaceTheme.Primary;
            nc.a = Mathf.Clamp01(noticeTimer / 0.5f);
            notice.color = nc;
            if (noticeTimer <= 0f && notice.text.Length > 0) notice.text = "";

            wrongWayTimer -= dt;
            if (wrongWayTimer <= 0f) wrongWay.text = "";

            // geri sayım: her sayı büyükten küçüğe "pop" ile gelir
            if (race.Phase == RacePhase.Countdown || (race.Phase == RacePhase.Running && race.RaceTime < 1f))
            {
                countdownPop = Mathf.Max(0f, countdownPop - dt * 1.4f);
                float scale = 1f + countdownPop * 0.35f;
                countdown.rectTransform.localScale = new Vector3(scale, scale, 1f);
                var c = countdown.color;
                c.a = race.Phase == RacePhase.Running ? Mathf.Clamp01(1f - race.RaceTime) : 1f;
                countdown.color = c;
            }
            else if (countdown.text.Length > 0) countdown.text = "";
        }

        void UpdateArrow()
        {
            bool show = race.Phase == RacePhase.Running;
            Vector3 dir = race.DirectionToNextGate();
            Vector3 local = robot.transform.InverseTransformDirection(dir);
            float gateAngle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float rel = Mathf.DeltaAngle(viewer.Yaw, gateAngle);
            float away = Mathf.Abs(rel);
            // kapı ekranın ortasındaysa ok gizlenir; yan taraftaysa o yöne dönük gösterilir
            float alpha = show ? Mathf.Clamp01((away - 22f) / 25f) : 0f;
            float rad = rel * Mathf.Deg2Rad;
            arrow.anchoredPosition = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad) * 0.6f) * 150f + new Vector2(0f, 10f);
            arrow.localRotation = Quaternion.Euler(0f, 0f, -rel);
            foreach (var g in arrowLegs)
            {
                var c = g.color;
                c.a = alpha;
                g.color = c;
            }
        }

        // ------------------------------------------------------------------ harita

        float mapScale = MapScale;
        Vector2 mapCenter;

        Vector2 ToMap(Vector3 world) => (new Vector2(world.x, world.z) - mapCenter) * mapScale;

        /// <summary>Haritayı pistin sınırlarına sığdırır (küçük pist büyük gösterilir).</summary>
        void FitMap(Vector3[] path)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (var p in path)
            {
                min = Vector2.Min(min, new Vector2(p.x, p.z));
                max = Vector2.Max(max, new Vector2(p.x, p.z));
            }
            mapCenter = (min + max) * 0.5f;
            float w = Mathf.Max(0.5f, max.x - min.x), h = Mathf.Max(0.5f, max.y - min.y);
            mapScale = Mathf.Min((MapW - 28f) / w, (MapH - 28f) / h, 80f);
        }

        void DrawMap()
        {
            hasMap = true;
            mapSpec = race.Spec;
            if (miniTexture == null)
            {
                miniTexture = new Texture2D(MapW, MapH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                miniImage.texture = miniTexture;
            }
            var px = new Color32[MapW * MapH];
            var path = mapSpec.Path;
            FitMap(path);
            var edge = new Color32(255, 194, 26, 230);
            var fill = new Color32(26, 26, 30, 255);
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < path.Length; i++)
                {
                    var a = ToMap(path[i]);
                    var b = ToMap(path[(i + 1) % path.Length]);
                    for (int s = 0; s < 3; s++)
                    {
                        var p = Vector2.Lerp(a, b, s / 3f);
                        Disc(px, p, pass == 0 ? 4.2f : 2.6f, pass == 0 ? edge : fill);
                    }
                }
            // kapı noktaları ve başlangıç çizgisi
            for (int g = 0; g < mapSpec.Gates.Length; g++)
                Disc(px, ToMap(mapSpec.Gates[g]), g == 0 ? 3.4f : 1.7f, g == 0 ? new Color32(255, 59, 48, 255) : new Color32(240, 240, 240, 255));

            miniTexture.SetPixels32(px);
            miniTexture.Apply(false);
        }

        static void Disc(Color32[] px, Vector2 center, float radius, Color32 color)
        {
            int cx = Mathf.RoundToInt(center.x) + MapW / 2, cy = Mathf.RoundToInt(center.y) + MapH / 2;
            int r = Mathf.CeilToInt(radius);
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                {
                    if (x * x + y * y > radius * radius) continue;
                    int ix = cx + x, iy = cy + y;
                    if (ix < 0 || iy < 0 || ix >= MapW || iy >= MapH) continue;
                    px[iy * MapW + ix] = color;
                }
        }

        void UpdateMapMarkers()
        {
            dot.anchoredPosition = ToMap(robot.transform.position);
            dot.localRotation = Quaternion.Euler(0f, 0f, -robot.Telemetry.YawDegrees);
            if (race.Spec.Gates != null)
            {
                nextMark.anchoredPosition = ToMap(race.Spec.Gates[Mathf.Clamp(race.NextGate, 0, race.GateCount - 1)]);
                float s = 1f + 0.25f * Mathf.Sin(Time.realtimeSinceStartup * 6f);
                nextMark.localScale = new Vector3(s, s, 1f);
            }
        }
    }
}
