using System;
using System.Collections.Generic;
using ARaki.Game;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ARaki.UI
{
    /// <summary>
    /// Oyun ekranları: ana menü, duraklatma, kazanma, kaybetme ve yükseltme seçimi.
    /// GameSession'ın durumunu dinler; fare ve klavye ile kullanılır.
    /// </summary>
    public class ScreenUi : MonoBehaviour
    {
        [SerializeField] GameSession session;
        [SerializeField] SpaceHud hud;
        [SerializeField] RaceHud raceHud;

        [Header("Tasarım")]
        [SerializeField] Font titleFont;
        [SerializeField] Font bodyFont;
        [SerializeField] Font monoFont;
        [SerializeField] Sprite bracket;
        [SerializeField] Sprite ring;

        class Button
        {
            public RectTransform Rt;
            public Image Fill;
            public Text Label, Key;
            public Action Action;
            public Color Accent;
        }

        class Screen
        {
            public CanvasGroup Group;
            public readonly List<Button> Buttons = new List<Button>();
        }

        UiKit kit;
        RectTransform root;
        bool built;
        readonly Dictionary<GameState, Screen> screens = new Dictionary<GameState, Screen>();
        GameState shown = (GameState)(-1);
        Screen current, raceWin;
        Text rwSub, rwTitle, rwMedal, rwTotal, rwBest, rwRecord, rwBadge;
        readonly Text[] rwLaps = new Text[3];

        // dinamik içerik
        Text menuBest, winTitle, winSub, winTime, winFuel, winObjective, winTimeBonus, winFuelBonus, winTotal, winBest, pauseSub, loseSub;
        readonly Image[] stars = new Image[3];
        readonly List<UpgradeCard> cards = new List<UpgradeCard>();
        Text promptMenu, promptWin, promptLose;

        class UpgradeCard
        {
            public RectTransform Rt;
            public Image Frame, Fill;
            public Text Key, Title, Description, Level;
            public GameObject Root;
            public int Index;
        }

        void Awake() => Build();

        void OnDestroy()
        {
            if (session != null) session.StateChanged -= OnState;
        }

        public void Build()
        {
            if (built) return;
            built = true;
            kit = new UiKit { Title = titleFont, Body = bodyFont, Mono = monoFont, Bracket = bracket, Ring = ring };

            var go = new GameObject("Screens Canvas");
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = Application.isPlaying ? RenderMode.ScreenSpaceOverlay : RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = hudCamera();
            canvas.planeDistance = 0.9f;
            canvas.sortingOrder = 20;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            root = go.GetComponent<RectTransform>();

            BuildMenu();
            BuildPause();
            BuildWin();
            BuildLose();
            BuildUpgrade();
            BuildRaceWin();

            foreach (var s in screens.Values) SetGroup(s.Group, false);
            SetGroup(raceWin.Group, false);
            session.StateChanged += OnState;
        }

        Camera hudCamera() => hud != null ? hud.GetComponentInChildren<Canvas>(true)?.worldCamera : null;

        // ------------------------------------------------------------------ ekranlar

        Screen NewScreen(GameState state, float dim, bool register = true)
        {
            var group = kit.Stretch(state + "Screen", root);
            var cg = group.gameObject.AddComponent<CanvasGroup>();
            if (dim > 0f) kit.Img(kit.Stretch("Dim", group), new Color(0.01f, 0.03f, 0.05f, dim));
            var s = new Screen { Group = cg };
            if (register) screens[state] = s;
            return s;
        }

        void BuildMenu()
        {
            var s = NewScreen(GameState.Menu, 0.3f);
            var g = s.Group.GetComponent<RectTransform>();

            // sol: koyu şerit + başlık
            kit.Img(kit.Rt("Side", g, UiKit.TopLeft, UiKit.TopLeft, Vector2.zero, new Vector2(620f, 0f)).Also(r => { r.anchorMax = new Vector2(0f, 1f); r.anchorMin = new Vector2(0f, 0f); r.sizeDelta = new Vector2(620f, 0f); }),
                new Color(0.01f, 0.04f, 0.07f, 0.62f));
            kit.Img(kit.Rt("SideLine", g, UiKit.TopLeft, UiKit.TopLeft, new Vector2(620f, 0f), new Vector2(2f, 0f)).Also(r => { r.anchorMax = new Vector2(0f, 1f); r.anchorMin = new Vector2(0f, 0f); r.sizeDelta = new Vector2(2f, 0f); }),
                SpaceTheme.WithAlpha(SpaceTheme.Primary, 0.55f));

            kit.Label(g, "BİTİRME PROJESİ  ·  AR KEŞİF OYUNU", monoFont, 12, SpaceTheme.Primary, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(88f, -128f), new Vector2(500f, 18f));
            var title = kit.Label(g, "ARAKI", titleFont, 168, SpaceTheme.Text, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(80f, -150f), new Vector2(520f, 190f));
            UiKit.Shadow(title);
            kit.Label(g, "GEZEGEN KEŞİF GÖREVİ", titleFont, 30, SpaceTheme.Primary, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(92f, -330f), new Vector2(500f, 36f));
            kit.Img(kit.Rt("Rule", g, UiKit.TopLeft, UiKit.TopLeft, new Vector2(92f, -380f), new Vector2(420f, 1f)), SpaceTheme.WithAlpha(SpaceTheme.Primary, 0.4f));

            AddButton(s, "GÖREVE BAŞLA", "ENTER", new Vector2(92f, -420f), new Vector2(380f, 54f), SpaceTheme.Primary, () => session.StartGame(), g, UiKit.TopLeft);
            AddButton(s, "ÇIKIŞ", "ESC", new Vector2(92f, -486f), new Vector2(380f, 54f), SpaceTheme.TextDim, () => session.QuitGame(), g, UiKit.TopLeft);

            menuBest = kit.Label(g, "", monoFont, 13, SpaceTheme.TextDim, TextAnchor.LowerLeft, UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(92f, 40f), new Vector2(480f, 18f));

            // sağ alt: kontroller
            var card = kit.Card("Controls", g, UiKit.BottomRight, UiKit.BottomRight, new Vector2(-40f, 40f), new Vector2(310f, 246f));
            kit.Label(card, "KONTROLLER", monoFont, 11, SpaceTheme.Primary, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(16f, -12f), new Vector2(200f, 16f));
            string[,] keys =
            {
                { "W A S D", "Sür" }, { "Q E  /  SAĞ TIK", "Etrafa bak" }, { "L  /  SOL TIK", "LED (basılı tut)" },
                { "R F", "Motor gücü" }, { "K", "Sesi aç / kapat" }, { "ESC", "Duraklat" },
            };
            for (int i = 0; i < 6; i++)
            {
                float y = -38f - i * 33f;
                kit.Label(card, keys[i, 0], monoFont, 13, SpaceTheme.Text, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(16f, y), new Vector2(160f, 18f));
                kit.Label(card, keys[i, 1], bodyFont, 16, SpaceTheme.TextDim, TextAnchor.UpperRight, UiKit.TopLeft, UiKit.TopLeft, new Vector2(150f, y - 2f), new Vector2(144f, 20f));
            }
        }

        void BuildPause()
        {
            var s = NewScreen(GameState.Paused, 0.55f);
            var g = s.Group.GetComponent<RectTransform>();
            var card = kit.Card("Card", g, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(460f, 300f));
            kit.Label(card, "DURAKLATILDI", titleFont, 44, SpaceTheme.Text, TextAnchor.MiddleCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -34f), new Vector2(420f, 52f));
            pauseSub = kit.Label(card, "", monoFont, 12, SpaceTheme.Primary, TextAnchor.MiddleCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -92f), new Vector2(420f, 16f));
            AddButton(s, "DEVAM", "ESC", new Vector2(0f, -136f), new Vector2(380f, 50f), SpaceTheme.Primary, () => session.Resume(), card, UiKit.TopCenter);
            AddButton(s, "ANA MENÜ", "M", new Vector2(0f, -198f), new Vector2(380f, 50f), SpaceTheme.TextDim, () => session.ToMenu(), card, UiKit.TopCenter);
        }

        void BuildWin()
        {
            var s = NewScreen(GameState.Won, 0.55f);
            var g = s.Group.GetComponent<RectTransform>();
            var card = kit.Card("Card", g, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(620f, 500f), SpaceTheme.WithAlpha(SpaceTheme.Success, 0.9f));
            winSub = kit.Label(card, "", monoFont, 12, SpaceTheme.Success, TextAnchor.MiddleCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -26f), new Vector2(560f, 16f));
            winTitle = kit.Label(card, "GÖREV TAMAMLANDI", titleFont, 46, SpaceTheme.Text, TextAnchor.MiddleCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -48f), new Vector2(560f, 56f));

            for (int i = 0; i < 3; i++)
            {
                var d = kit.Rt("Star" + i, card, UiKit.TopCenter, UiKit.Center, new Vector2(-46f + i * 46f, -134f), new Vector2(24f, 24f));
                d.localRotation = Quaternion.Euler(0f, 0f, 45f);
                stars[i] = kit.Img(d, SpaceTheme.Warning);
            }

            float y0 = -178f;
            winTime = Row(card, "SÜRE", y0);
            winFuel = Row(card, "KALAN YAKIT", y0 - 34f);
            winObjective = Row(card, "HEDEF PUANI", y0 - 68f);
            winTimeBonus = Row(card, "SÜRE BONUSU", y0 - 102f);
            winFuelBonus = Row(card, "YAKIT BONUSU", y0 - 136f);
            kit.Img(kit.Rt("Rule", card, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, y0 - 174f), new Vector2(500f, 1f)), SpaceTheme.WithAlpha(SpaceTheme.Success, 0.5f));
            kit.Label(card, "TOPLAM PUAN", bodyFont, 18, SpaceTheme.TextDim, TextAnchor.MiddleLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(60f, y0 - 194f), new Vector2(200f, 30f));
            winTotal = kit.Label(card, "", titleFont, 44, SpaceTheme.Success, TextAnchor.MiddleRight, UiKit.TopLeft, UiKit.TopLeft, new Vector2(260f, y0 - 188f), new Vector2(300f, 48f));
            winBest = kit.Label(card, "", monoFont, 12, SpaceTheme.TextDim, TextAnchor.MiddleRight, UiKit.TopLeft, UiKit.TopLeft, new Vector2(260f, y0 - 234f), new Vector2(300f, 16f));

            AddButton(s, "YÜKSELTME SEÇ", "ENTER", new Vector2(0f, -430f), new Vector2(400f, 50f), SpaceTheme.Success, () => session.ShowUpgrades(), card, UiKit.TopCenter);
            promptWin = null;
        }

        Text Row(RectTransform card, string label, float y)
        {
            kit.Label(card, label, bodyFont, 18, SpaceTheme.TextDim, TextAnchor.MiddleLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(60f, y), new Vector2(240f, 28f));
            return kit.Label(card, "", monoFont, 18, SpaceTheme.Text, TextAnchor.MiddleRight, UiKit.TopLeft, UiKit.TopLeft, new Vector2(300f, y), new Vector2(260f, 28f));
        }

        void BuildRaceWin()
        {
            var s = NewScreen(GameState.Won, 0.55f, false);
            raceWin = s;
            var g = s.Group.GetComponent<RectTransform>();
            var card = kit.Card("Card", g, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(620f, 540f), RaceTheme.Primary);
            rwSub = kit.Label(card, "", monoFont, 12, RaceTheme.Primary, TextAnchor.MiddleCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -26f), new Vector2(560f, 16f));
            rwTitle = kit.Label(card, "YARIŞ BİTTİ", titleFont, 50, RaceTheme.Text, TextAnchor.MiddleCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -48f), new Vector2(560f, 60f));
            rwMedal = kit.Label(card, "", titleFont, 28, RaceTheme.Primary, TextAnchor.MiddleCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -112f), new Vector2(560f, 34f));
            rwBadge = kit.Label(card, "", monoFont, 12, RaceTheme.Good, TextAnchor.MiddleCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -146f), new Vector2(560f, 16f));

            float y0 = -184f;
            rwTotal = RaceRow(card, "TOPLAM SÜRE", y0);
            rwBest = RaceRow(card, "EN HIZLI TUR", y0 - 34f);
            for (int i = 0; i < 3; i++) rwLaps[i] = RaceRow(card, "TUR " + (i + 1), y0 - 78f - i * 30f);
            kit.Img(kit.Rt("Rule", card, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, y0 - 176f), new Vector2(500f, 1f)), new Color(RaceTheme.Primary.r, RaceTheme.Primary.g, RaceTheme.Primary.b, 0.5f));
            rwRecord = kit.Label(card, "", monoFont, 13, RaceTheme.TextDim, TextAnchor.MiddleCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, y0 - 194f), new Vector2(520f, 18f));

            AddButton(s, "SONRAKİ PİST", "ENTER", new Vector2(0f, -432f), new Vector2(400f, 50f), RaceTheme.Primary, () => session.Continue(), card, UiKit.TopCenter);
            AddButton(s, "ANA MENÜ", "M", new Vector2(0f, -488f), new Vector2(400f, 50f), SpaceTheme.TextDim, () => session.ToMenu(), card, UiKit.TopCenter);
        }

        Text RaceRow(RectTransform card, string label, float y)
        {
            kit.Label(card, label, bodyFont, 18, RaceTheme.TextDim, TextAnchor.MiddleLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(60f, y), new Vector2(240f, 28f));
            return kit.Label(card, "", monoFont, 18, RaceTheme.Text, TextAnchor.MiddleRight, UiKit.TopLeft, UiKit.TopLeft, new Vector2(300f, y), new Vector2(260f, 28f));
        }

        void BuildLose()
        {
            var s = NewScreen(GameState.Lost, 0.6f);
            var g = s.Group.GetComponent<RectTransform>();
            var card = kit.Card("Card", g, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(560f, 340f), SpaceTheme.WithAlpha(SpaceTheme.Danger, 0.9f));
            kit.Label(card, "SİSTEM HATASI", monoFont, 12, SpaceTheme.Danger, TextAnchor.MiddleCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -28f), new Vector2(500f, 16f));
            kit.Label(card, "YAKIT BİTTİ", titleFont, 56, SpaceTheme.Text, TextAnchor.MiddleCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -50f), new Vector2(500f, 66f));
            loseSub = kit.Label(card, "", bodyFont, 18, SpaceTheme.TextDim, TextAnchor.UpperCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -128f), new Vector2(460f, 60f));
            loseSub.horizontalOverflow = HorizontalWrapMode.Wrap;
            AddButton(s, "TEKRAR DENE", "ENTER", new Vector2(0f, -208f), new Vector2(400f, 50f), SpaceTheme.Danger, () => session.Retry(), card, UiKit.TopCenter);
            AddButton(s, "ANA MENÜ", "M", new Vector2(0f, -268f), new Vector2(400f, 50f), SpaceTheme.TextDim, () => session.ToMenu(), card, UiKit.TopCenter);
        }

        void BuildUpgrade()
        {
            var s = NewScreen(GameState.Upgrade, 0.62f);
            var g = s.Group.GetComponent<RectTransform>();
            kit.Label(g, "ÜSSE DÖNÜŞ", monoFont, 13, SpaceTheme.Primary, TextAnchor.MiddleCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -86f), new Vector2(600f, 18f));
            kit.Label(g, "YÜKSELTME SEÇ", titleFont, 54, SpaceTheme.Text, TextAnchor.MiddleCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -108f), new Vector2(700f, 64f));
            kit.Label(g, "Seçtiğin yükseltme robotun gerçek davranışını değiştirir.", bodyFont, 18, SpaceTheme.TextDim, TextAnchor.MiddleCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -176f), new Vector2(700f, 24f));

            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 372f;
                var card = kit.Card("Upgrade" + i, g, UiKit.Center, UiKit.Center, new Vector2(x, -30f), new Vector2(344f, 340f));
                var uc = new UpgradeCard { Rt = card, Index = i, Root = card.gameObject };
                uc.Frame = card.Find("Frame").GetComponent<Image>();
                uc.Key = kit.Label(card, (i + 1).ToString(), titleFont, 54, SpaceTheme.Primary, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(20f, -14f), new Vector2(80f, 64f));
                uc.Title = kit.Label(card, "", titleFont, 30, SpaceTheme.Text, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(20f, -92f), new Vector2(304f, 36f));
                uc.Description = kit.Label(card, "", bodyFont, 19, SpaceTheme.TextDim, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(20f, -140f), new Vector2(304f, 120f));
                uc.Description.horizontalOverflow = HorizontalWrapMode.Wrap;
                uc.Level = kit.Label(card, "", monoFont, 13, SpaceTheme.Primary, TextAnchor.LowerLeft, UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(20f, 18f), new Vector2(304f, 18f));
                uc.Fill = kit.Img(kit.Rt("Hover", card, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(344f, 340f)), new Color(1f, 1f, 1f, 0f));
                cards.Add(uc);
            }
        }

        Button AddButton(Screen s, string label, string key, Vector2 pos, Vector2 size, Color accent, Action action, RectTransform parent, Vector2 anchor)
        {
            var rt = kit.Rt("Button", parent, anchor, anchor == UiKit.TopCenter ? UiKit.TopCenter : UiKit.TopLeft, pos, size);
            var b = new Button { Rt = rt, Action = action, Accent = accent };
            b.Fill = kit.Img(rt, new Color(accent.r, accent.g, accent.b, 0.08f));
            kit.Img(kit.Stretch("Frame", rt), SpaceTheme.WithAlpha(accent, 0.8f), bracket, Image.Type.Sliced);
            b.Label = kit.Label(rt, label, titleFont, 26, SpaceTheme.Text, TextAnchor.MiddleLeft, UiKit.Center, UiKit.Center, new Vector2(0f, 0f), size - new Vector2(100f, 0f));
            b.Label.rectTransform.anchoredPosition = new Vector2(-(100f) * 0.5f + 20f, 0f);
            b.Key = kit.Label(rt, key, monoFont, 13, SpaceTheme.WithAlpha(accent, 0.9f), TextAnchor.MiddleRight, UiKit.Center, UiKit.Center, new Vector2(size.x * 0.5f - 62f, 0f), new Vector2(100f, 20f));
            s.Buttons.Add(b);
            return b;
        }

        // ------------------------------------------------------------------ durum

        void OnState(GameState state) => Show(state);

        public void Show(GameState state)
        {
            if (!built) Build();
            shown = state;
            bool race = session.Mode == GameMode.Race;
            Screen active = null;
            if (state == GameState.Won && race) active = raceWin;
            else screens.TryGetValue(state, out active);
            foreach (var kv in screens) SetGroup(kv.Value.Group, kv.Value == active);
            SetGroup(raceWin.Group, raceWin == active);
            current = active;
            bool play = state == GameState.Playing || state == GameState.Paused;
            hud?.SetVisible(play && !race);
            raceHud?.SetVisible(play && race);

            switch (state)
            {
                case GameState.Menu:
                    string raceRecord = RaceController.BestTotalRecord > 0f ? "   ·   YARIŞ REKORU  " + RaceController.FormatLap(RaceController.BestTotalRecord) : "";
                    menuBest.text = (session.BestScore > 0 ? "EN İYİ PUAN  " + session.BestScore : "HENÜZ PUAN YOK") + raceRecord;
                    break;
                case GameState.Paused:
                    pauseSub.text = (race ? session.RaceSpec.Title : session.Spec.Title) + "  ·  SEVİYE " + session.Level;
                    break;
                case GameState.Won:
                    if (race) FillRaceWin(); else FillWin();
                    break;
                case GameState.Lost:
                    loseSub.text = race
                        ? "Yakıt yarış bitmeden tükendi. Daha düşük vites ve temiz bir çizgi daha az yakıt harcar."
                        : "Robot enerji kaybetti. LED'i kısa tut, bekçilerden kaçın; üsse dönerek şarj olabileceğini unutma.";
                    break;
                case GameState.Upgrade: FillUpgrades(); break;
            }
        }

        static void SetGroup(CanvasGroup g, bool on)
        {
            g.alpha = on ? 1f : 0f;
            g.blocksRaycasts = on;
            g.gameObject.SetActive(on);
        }

        void FillWin()
        {
            var r = session.Result;
            winSub.text = session.Spec.Title + "  ·  SEVİYE " + session.Level;
            winTime.text = FormatTime(r.Seconds);
            winFuel.text = "%" + Mathf.RoundToInt(r.Fuel * 100f);
            winObjective.text = "+" + r.ObjectivePoints;
            winTimeBonus.text = "+" + r.TimeBonus;
            winFuelBonus.text = "+" + r.FuelBonus;
            winTotal.text = r.Total.ToString();
            winBest.text = r.NewBest ? "YENİ REKOR" : "EN İYİ  " + r.Best;
            winBest.color = r.NewBest ? SpaceTheme.Warning : SpaceTheme.TextDim;
            for (int i = 0; i < 3; i++) stars[i].color = i < r.Stars ? SpaceTheme.Warning : SpaceTheme.Track;
        }

        void FillRaceWin()
        {
            var r = session.Race.Result;
            var spec = session.Race.Spec;
            rwSub.text = spec.Title + "  ·  " + spec.Laps + " TUR  ·  " + spec.Length.ToString("0.0") + " M";
            rwTotal.text = RaceController.FormatLap(r.TotalSeconds);
            rwBest.text = RaceController.FormatLap(r.BestLap);
            for (int i = 0; i < 3; i++)
            {
                bool has = r.LapTimes != null && i < r.LapTimes.Length;
                rwLaps[i].text = has ? RaceController.FormatLap(r.LapTimes[i]) : "--";
                rwLaps[i].color = has && Mathf.Approximately(r.LapTimes[i], r.BestLap) ? RaceTheme.Good : RaceTheme.Text;
            }
            string[] names = { "", "BRONZ MADALYA", "GÜMÜŞ MADALYA", "ALTIN MADALYA" };
            Color[] colors = { RaceTheme.Text, new Color32(0xCD, 0x7F, 0x32, 255), new Color32(0xC9, 0xD1, 0xD9, 255), RaceTheme.Primary };
            rwMedal.text = names[(int)r.Medal];
            rwMedal.color = colors[(int)r.Medal];
            rwBadge.text = r.NewBestTotal ? "YENİ REKOR" : r.NewBestLap ? "YENİ EN HIZLI TUR" : "";
            rwRecord.text = "REKOR  " + RaceController.FormatLap(r.BestTotalRecord) + "   ·   EN HIZLI TUR  " + RaceController.FormatLap(r.BestLapRecord);
        }

        void FillUpgrades()
        {
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                bool has = i < session.Offers.Count;
                c.Root.SetActive(has);
                if (!has) continue;
                var def = UpgradeCatalog.Get(session.Offers[i]);
                int lv = session.Profile.Level(def.Id);
                c.Title.text = def.Title;
                c.Description.text = def.Description;
                c.Level.text = "SEVİYE " + lv + "  →  " + (lv + 1) + "  /  " + def.MaxLevel;
            }
        }

        static string FormatTime(float s) => Mathf.FloorToInt(s / 60f).ToString("00") + ":" + Mathf.FloorToInt(s % 60f).ToString("00");

        // ------------------------------------------------------------------ giriş

        void Update()
        {
            var screen = current;
            if (screen == null) return;
            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 pos = mouse.position.ReadValue();
            bool click = mouse.leftButton.wasPressedThisFrame;

            foreach (var b in screen.Buttons)
            {
                bool hover = RectTransformUtility.RectangleContainsScreenPoint(b.Rt, pos, null);
                b.Fill.color = new Color(b.Accent.r, b.Accent.g, b.Accent.b, hover ? 0.28f : 0.08f);
                if (hover && click) b.Action();
            }

            if (shown == GameState.Upgrade)
            {
                foreach (var c in cards)
                {
                    if (!c.Root.activeSelf) continue;
                    bool hover = RectTransformUtility.RectangleContainsScreenPoint(c.Rt, pos, null);
                    c.Fill.color = new Color(SpaceTheme.Primary.r, SpaceTheme.Primary.g, SpaceTheme.Primary.b, hover ? 0.12f : 0f);
                    if (hover && click) { session.ChooseUpgrade(c.Index); break; }
                }
            }
        }
    }

    static class RectExtensions
    {
        public static RectTransform Also(this RectTransform rt, Action<RectTransform> configure)
        {
            configure(rt);
            return rt;
        }
    }
}
