using System;
using System.Collections.Generic;
using ARaki.Game;
using ARaki.Theming;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ARaki.UI
{
    /// <summary>Oyun ekranları: ana menü (tema seçimi dahil), duraklatma, laboratuvar (yükseltmeler) ve final. Fare ve klavye ile kullanılır.</summary>
    public class ScreenUi : MonoBehaviour
    {
        [SerializeField] GameSession session;
        [SerializeField] ExpeditionHud hud;

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
            public bool Selected;
        }

        class Screen
        {
            public CanvasGroup Group;
            public readonly List<Button> Buttons = new List<Button>();
        }

        class LabCard
        {
            public RectTransform Rt;
            public Image Frame, Hover;
            public Text Key, Title, Description, Level, Cost;
            public UpgradeId Id;
        }

        UiKit kit;
        RectTransform root;
        bool built;
        readonly Dictionary<GameState, Screen> screens = new Dictionary<GameState, Screen>();
        Screen current;
        Text menuSubtitle, menuTagline, menuInfo, pauseSub, labEssence, endText, endStats;
        Button continueButton;
        readonly List<Button> themeButtons = new List<Button>();
        readonly List<LabCard> cards = new List<LabCard>();

        void Awake() => Build();

        void OnDestroy()
        {
            if (session != null) session.StateChanged -= Show;
        }

        public void Build()
        {
            if (built) return;
            built = true;
            UiTheme.EnsureApplied();
            kit = new UiKit { Title = titleFont, Body = bodyFont, Mono = monoFont, Bracket = bracket, Ring = ring };

            var go = new GameObject("Screens Canvas");
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = Application.isPlaying ? RenderMode.ScreenSpaceOverlay : RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = hud != null ? hud.GetComponentInChildren<Canvas>(true)?.worldCamera : null;
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
            BuildLab();
            BuildEnding();

            foreach (var s in screens.Values) SetGroup(s.Group, false);
            session.StateChanged += Show;
        }

        // ------------------------------------------------------------------ ekranlar

        Screen NewScreen(GameState state, float dim)
        {
            var group = kit.Stretch(state + "Screen", root);
            var cg = group.gameObject.AddComponent<CanvasGroup>();
            if (dim > 0f) kit.Img(kit.Stretch("Dim", group), new Color(0.01f, 0.02f, 0.03f, dim));
            var s = new Screen { Group = cg };
            screens[state] = s;
            return s;
        }

        void BuildMenu()
        {
            var s = NewScreen(GameState.Menu, 0.25f);
            var g = s.Group.GetComponent<RectTransform>();
            var def = ThemeCatalog.Def;

            var side = kit.Rt("Side", g, UiKit.TopLeft, UiKit.TopLeft, Vector2.zero, new Vector2(620f, 0f));
            side.anchorMin = new Vector2(0f, 0f); side.anchorMax = new Vector2(0f, 1f); side.sizeDelta = new Vector2(620f, 0f);
            kit.Img(side, UiTheme.WithAlpha(new Color(0.01f, 0.03f, 0.04f), 0.66f));
            var line = kit.Rt("SideLine", g, UiKit.TopLeft, UiKit.TopLeft, new Vector2(620f, 0f), new Vector2(2f, 0f));
            line.anchorMin = new Vector2(0f, 0f); line.anchorMax = new Vector2(0f, 1f); line.sizeDelta = new Vector2(2f, 0f);
            kit.Img(line, UiTheme.WithAlpha(UiTheme.Primary, 0.55f));

            kit.Label(g, "BİTİRME PROJESİ  ·  AR KEŞİF OYUNU", monoFont, 12, UiTheme.Primary, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(88f, -92f), new Vector2(500f, 18f));
            var title = kit.Label(g, "ARAKI", titleFont, 150, UiTheme.Text, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(80f, -108f), new Vector2(520f, 170f));
            UiKit.Shadow(title);
            menuSubtitle = kit.Label(g, def.Name, titleFont, 30, UiTheme.Primary, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(92f, -280f), new Vector2(500f, 36f));
            kit.Img(kit.Rt("Rule", g, UiKit.TopLeft, UiKit.TopLeft, new Vector2(92f, -326f), new Vector2(420f, 1f)), UiTheme.WithAlpha(UiTheme.Primary, 0.4f));
            menuTagline = kit.Label(g, def.Tagline, bodyFont, 19, UiTheme.TextDim, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(92f, -338f), new Vector2(430f, 52f));
            menuTagline.horizontalOverflow = HorizontalWrapMode.Wrap;

            continueButton = AddButton(s, "OYNA", "ENTER", new Vector2(92f, -410f), new Vector2(420f, 54f), UiTheme.Primary, () => session.Continue(), g, UiKit.TopLeft);
            AddButton(s, "YENİ OYUN", "N", new Vector2(92f, -472f), new Vector2(420f, 44f), UiTheme.TextDim, () => session.NewGame(), g, UiKit.TopLeft, 22);

            kit.Label(g, "TEMA  (görünüm ve hikaye; oynanış aynı)", monoFont, 11, UiTheme.TextDim, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(92f, -536f), new Vector2(420f, 16f));
            string[] names = { "SAFARİ", "GEZEGEN", "GİZEM" };
            ThemeId[] ids = { ThemeId.Safari, ThemeId.Space, ThemeId.Mystery };
            for (int i = 0; i < 3; i++)
            {
                var id = ids[i];
                var b = AddButton(s, names[i], (i + 1).ToString(), new Vector2(92f + i * 142f, -558f), new Vector2(134f, 44f), ThemeCatalog.Get(id).Palette.Primary, () => session.SetTheme(id), g, UiKit.TopLeft, 19);
                b.Selected = ThemeCatalog.Current == id;
                themeButtons.Add(b);
            }
            AddButton(s, "ÇIKIŞ", "ESC", new Vector2(92f, -622f), new Vector2(420f, 40f), UiTheme.TextDim, () => session.QuitGame(), g, UiKit.TopLeft, 22);

            menuInfo = kit.Label(g, "", monoFont, 13, UiTheme.TextDim, TextAnchor.LowerLeft, UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(92f, 22f), new Vector2(480f, 18f));

            var card = kit.Card("Controls", g, UiKit.BottomRight, UiKit.BottomRight, new Vector2(-40f, 40f), new Vector2(330f, 322f));
            kit.Label(card, "KONTROLLER", monoFont, 11, UiTheme.Primary, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(16f, -12f), new Vector2(200f, 16f));
            string[,] keys =
            {
                { "W A S D", "Sür" }, { "SHIFT", "Sessiz (ürkekler için)" }, { "Q E  /  SAĞ TIK", "Etrafa bak" }, { "L  /  SOL TIK", "LED (basılı tut)" },
                { "X", "Sonar (gizlileri bul)" }, { "R F", "Vites" }, { "U", "Laboratuvar (üste)" }, { "K", "Sesi aç / kapat" }, { "ESC", "Duraklat" },
            };
            for (int i = 0; i < 9; i++)
            {
                float y = -38f - i * 30f;
                kit.Label(card, keys[i, 0], monoFont, 13, UiTheme.Text, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(16f, y), new Vector2(150f, 18f));
                kit.Label(card, keys[i, 1], bodyFont, 16, UiTheme.TextDim, TextAnchor.UpperRight, UiKit.TopLeft, UiKit.TopLeft, new Vector2(120f, y - 2f), new Vector2(194f, 20f));
            }
        }

        void BuildPause()
        {
            var s = NewScreen(GameState.Paused, 0.55f);
            var g = s.Group.GetComponent<RectTransform>();
            var card = kit.Card("Card", g, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(460f, 300f));
            kit.Label(card, "DURAKLATILDI", titleFont, 44, UiTheme.Text, TextAnchor.MiddleCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -34f), new Vector2(420f, 52f));
            pauseSub = kit.Label(card, "", monoFont, 12, UiTheme.Primary, TextAnchor.MiddleCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -92f), new Vector2(420f, 16f));
            AddButton(s, "DEVAM", "ESC", new Vector2(0f, -136f), new Vector2(380f, 50f), UiTheme.Primary, () => session.Resume(), card, UiKit.TopCenter);
            AddButton(s, "ANA MENÜ", "M", new Vector2(0f, -198f), new Vector2(380f, 50f), UiTheme.TextDim, () => session.ToMenu(), card, UiKit.TopCenter);
        }

        void BuildLab()
        {
            var s = NewScreen(GameState.Lab, 0.66f);
            var g = s.Group.GetComponent<RectTransform>();
            kit.Label(g, "ÜS  ·  LABORATUVAR", monoFont, 13, UiTheme.Primary, TextAnchor.MiddleCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -56f), new Vector2(600f, 18f));
            kit.Label(g, "GELİŞTİRME", titleFont, 54, UiTheme.Text, TextAnchor.MiddleCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -78f), new Vector2(700f, 64f));
            labEssence = kit.Label(g, "", monoFont, 18, UiTheme.Warning, TextAnchor.MiddleCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -146f), new Vector2(700f, 24f));
            kit.Label(g, "Bulguları teslim ederek öz kazanırsın. Seçtiğin geliştirme robotun gerçek davranışını değiştirir.", bodyFont, 18, UiTheme.TextDim, TextAnchor.MiddleCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -176f), new Vector2(900f, 24f));

            for (int i = 0; i < UpgradeCatalog.All.Length; i++)
            {
                int col = i % 3, row = i / 3;
                var pos = new Vector2((col - 1) * 352f, 48f - row * 190f);
                var card = kit.Card("Upgrade" + i, g, UiKit.Center, UiKit.Center, pos, new Vector2(332f, 172f));
                var lc = new LabCard { Rt = card, Id = (UpgradeId)i };
                lc.Frame = card.Find("Frame").GetComponent<Image>();
                lc.Key = kit.Label(card, (i + 1).ToString(), titleFont, 40, UiTheme.Primary, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(16f, -8f), new Vector2(50f, 48f));
                lc.Title = kit.Label(card, "", titleFont, 25, UiTheme.Text, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(60f, -14f), new Vector2(262f, 30f));
                lc.Description = kit.Label(card, "", bodyFont, 17, UiTheme.TextDim, TextAnchor.UpperLeft, UiKit.TopLeft, UiKit.TopLeft, new Vector2(18f, -58f), new Vector2(298f, 66f));
                lc.Description.horizontalOverflow = HorizontalWrapMode.Wrap;
                lc.Level = kit.Label(card, "", monoFont, 12, UiTheme.Primary, TextAnchor.LowerLeft, UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(18f, 14f), new Vector2(160f, 16f));
                lc.Cost = kit.Label(card, "", monoFont, 14, UiTheme.Warning, TextAnchor.LowerRight, UiKit.BottomRight, UiKit.BottomRight, new Vector2(-18f, 14f), new Vector2(150f, 18f));
                lc.Hover = kit.Img(kit.Rt("Hover", card, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(332f, 172f)), new Color(1f, 1f, 1f, 0f));
                cards.Add(lc);
            }
            kit.Label(g, "1-6  satın al    ·    U / ESC  geri dön", monoFont, 12, UiTheme.TextDim, TextAnchor.MiddleCenter, UiKit.BottomCenter, UiKit.BottomCenter, new Vector2(0f, 36f), new Vector2(700f, 18f));
        }

        void BuildEnding()
        {
            var s = NewScreen(GameState.Ending, 0.6f);
            var g = s.Group.GetComponent<RectTransform>();
            var card = kit.Card("Card", g, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(640f, 420f), UiTheme.WithAlpha(UiTheme.Success, 0.9f));
            kit.Label(card, "HİKAYE TAMAMLANDI", monoFont, 12, UiTheme.Success, TextAnchor.MiddleCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -28f), new Vector2(560f, 16f));
            kit.Label(card, "TEBRİKLER", titleFont, 56, UiTheme.Text, TextAnchor.MiddleCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -50f), new Vector2(560f, 66f));
            endText = kit.Label(card, "", bodyFont, 20, UiTheme.Text, TextAnchor.UpperCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -134f), new Vector2(560f, 110f));
            endText.horizontalOverflow = HorizontalWrapMode.Wrap;
            endStats = kit.Label(card, "", monoFont, 14, UiTheme.TextDim, TextAnchor.UpperCenter, UiKit.TopCenter, UiKit.TopCenter, new Vector2(0f, -262f), new Vector2(560f, 40f));
            AddButton(s, "GEZMEYE DEVAM", "ENTER", new Vector2(0f, -330f), new Vector2(400f, 52f), UiTheme.Success, () => session.Resume(), card, UiKit.TopCenter);
        }

        Button AddButton(Screen s, string label, string key, Vector2 pos, Vector2 size, Color accent, Action action, RectTransform parent, Vector2 anchor, int fontSize = 26)
        {
            bool small = size.x < 200f;
            var rt = kit.Rt("Button", parent, anchor, anchor == UiKit.TopCenter ? UiKit.TopCenter : UiKit.TopLeft, pos, size);
            var b = new Button { Rt = rt, Action = action, Accent = accent };
            b.Fill = kit.Img(rt, new Color(accent.r, accent.g, accent.b, 0.08f));
            kit.Img(kit.Stretch("Frame", rt), UiTheme.WithAlpha(accent, 0.8f), bracket, Image.Type.Sliced);
            if (small)
            {
                b.Label = kit.Label(rt, label, titleFont, fontSize, UiTheme.Text, TextAnchor.MiddleCenter, UiKit.Center, UiKit.Center, new Vector2(12f, 0f), new Vector2(size.x - 40f, size.y));
                b.Key = kit.Label(rt, key, monoFont, 13, UiTheme.WithAlpha(accent, 0.9f), TextAnchor.MiddleCenter, UiKit.Center, UiKit.Center, new Vector2(-size.x * 0.5f + 16f, 0f), new Vector2(24f, 20f));
            }
            else
            {
                b.Label = kit.Label(rt, label, titleFont, fontSize, UiTheme.Text, TextAnchor.MiddleLeft, UiKit.Center, UiKit.Center, new Vector2(-30f, 0f), size - new Vector2(100f, 0f));
                b.Key = kit.Label(rt, key, monoFont, 13, UiTheme.WithAlpha(accent, 0.9f), TextAnchor.MiddleRight, UiKit.Center, UiKit.Center, new Vector2(size.x * 0.5f - 62f, 0f), new Vector2(100f, 20f));
            }
            s.Buttons.Add(b);
            return b;
        }

        // ------------------------------------------------------------------ durum

        public void Show(GameState state)
        {
            if (!built) Build();
            screens.TryGetValue(state, out current);
            foreach (var kv in screens) SetGroup(kv.Value.Group, kv.Key == state);
            hud?.SetVisible(state == GameState.Playing || state == GameState.Paused);

            var e = session.Expedition;
            switch (state)
            {
                case GameState.Menu:
                    {
                        continueButton.Label.text = e.HasSave ? "DEVAM ET" : "OYNA";
                        var def = ThemeCatalog.Def;
                        var save = SaveData.Load(ThemeCatalog.Current);
                        menuInfo.text = save != null
                            ? "KAYITLI  ·  BÖLÜM " + (Mathf.Min(save.chapter, def.Chapters.Length - 1) + 1) + "/" + def.Chapters.Length + "  ·  ÖZ " + save.essence + "  ·  " + save.total + " BULGU"
                            : "BU TEMADA HENÜZ KAYIT YOK";
                        for (int i = 0; i < themeButtons.Count; i++) themeButtons[i].Selected = (int)ThemeCatalog.Current == i;
                        break;
                    }
                case GameState.Paused:
                    pauseSub.text = ThemeCatalog.Def.Name + "  ·  " + e.Chapter.Title;
                    break;
                case GameState.Lab:
                    FillLab();
                    break;
                case GameState.Ending:
                    endText.text = ThemeCatalog.Def.Chapters[ThemeCatalog.Def.Chapters.Length - 1].Outro;
                    endStats.text = e.TotalDelivered + " bulgu teslim edildi   ·   öz " + e.Essence;
                    break;
            }
        }

        static void SetGroup(CanvasGroup g, bool on)
        {
            g.alpha = on ? 1f : 0f;
            g.blocksRaycasts = on;
            g.gameObject.SetActive(on);
        }

        void FillLab()
        {
            var e = session.Expedition;
            labEssence.text = "ÖZ  " + e.Essence;
            foreach (var c in cards)
            {
                var def = UpgradeCatalog.Get(c.Id);
                int lv = e.Profile.Level(c.Id);
                bool maxed = lv >= def.MaxLevel;
                c.Title.text = def.Title;
                c.Description.text = def.Description;
                c.Level.text = maxed ? "EN ÜST SEVİYE" : "SEVİYE " + lv + "  →  " + (lv + 1) + "  /  " + def.MaxLevel;
                c.Cost.text = maxed ? "" : e.Profile.Cost(c.Id) + " ÖZ";
                bool can = e.CanBuy(c.Id);
                c.Cost.color = can ? UiTheme.Success : UiTheme.Danger;
                c.Frame.color = UiTheme.WithAlpha(can ? UiTheme.Primary : UiTheme.TextDim, can ? 0.9f : 0.5f);
            }
        }

        // ------------------------------------------------------------------ giriş

        void Update()
        {
            if (current == null) return;
            if (session.State == GameState.Lab && Time.frameCount % 6 == 0) FillLab();   // klavyeyle alınan yükseltme kartlara yansısın

            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 pos = mouse.position.ReadValue();
            bool click = mouse.leftButton.wasPressedThisFrame;

            foreach (var b in current.Buttons)
            {
                bool hover = RectTransformUtility.RectangleContainsScreenPoint(b.Rt, pos, null);
                float a = b.Selected ? 0.38f : hover ? 0.28f : 0.08f;
                b.Fill.color = new Color(b.Accent.r, b.Accent.g, b.Accent.b, a);
                if (hover && click) { b.Action(); return; }
            }

            if (session.State == GameState.Lab)
            {
                foreach (var c in cards)
                {
                    bool hover = RectTransformUtility.RectangleContainsScreenPoint(c.Rt, pos, null);
                    c.Hover.color = new Color(UiTheme.Primary.r, UiTheme.Primary.g, UiTheme.Primary.b, hover ? 0.1f : 0f);
                    if (hover && click) { session.Buy(c.Id); FillLab(); break; }
                }
            }
        }
    }
}
