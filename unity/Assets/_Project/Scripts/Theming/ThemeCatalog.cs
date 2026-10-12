using System;
using ARaki.Game;
using ARaki.World;
using UnityEngine;

namespace ARaki.Theming
{
    public enum ThemeId { Home = 0 }

    public class ThemePalette
    {
        public Color Primary, Text, TextDim, Warning, Danger, Success, Panel;
    }

    public class FindNames
    {
        public string Name;     // "POFUDUK"
        public string Hint;     // ne yapılacağı: "Sessiz yaklaş  ·  SHIFT"
        public FindNames(string name, string hint) { Name = name; Hint = hint; }
    }

    public enum GoalKind { DeliverAny, DeliverKind, DeliverRoom, Legend }

    public class ChapterDef
    {
        public string Title;
        public GoalKind Goal;
        public int Count = 1;
        public FindKind Kind;
        public RoomKind Room;
        public string[] Intro;
        public string Outro;
    }

    /// <summary>Bir görsel/hikaye teması: renkler, ışık, bulgu adları ve bölümler. Oynanış her temada aynıdır.</summary>
    public class ThemeDef
    {
        public ThemeId Id;
        public string Name;           // "MİNYATÜR SAFARİ"
        public string Tagline;        // menüde kısa açıklama
        public string RobotName;
        public string CommsName;
        public ThemePalette Palette;
        public Color SunColor;
        public float SunIntensity;
        public Color AmbientSky, AmbientEquator, AmbientGround;
        public FindNames[] Finds;     // FindKind sırasıyla
        public ChapterDef[] Chapters;

        public FindNames NamesOf(FindKind kind) => Finds[(int)kind];
    }

    /// <summary>Üç temanın tanımı ve seçili tema. Seçim kayıtlıdır; tema değişince sahne yeniden yüklenir.</summary>
    public static class ThemeCatalog
    {
        const string Key = "ARaki.Theme.v2";
        static ThemeId? current;
        static readonly ThemeDef[] defs = Build();

        public static ThemeId Current
        {
            get
            {
                if (current == null) current = (ThemeId)Mathf.Clamp(PlayerPrefs.GetInt(Key, 0), 0, defs.Length - 1);
                return current.Value;
            }
        }

        public static ThemeDef Def => defs[(int)Current];
        public static ThemeDef Get(ThemeId id) => defs[(int)id];
        public static ThemeDef[] All => defs;

        /// <summary>Temayı seçer ve arayüz renklerini günceller. Arayüz yeniden kurulana kadar eski renkler kalır.</summary>
        public static void Set(ThemeId id, bool save = true)
        {
            current = id;
            if (save) { PlayerPrefs.SetInt(Key, (int)id); PlayerPrefs.Save(); }
            UiTheme.Apply(defs[(int)id].Palette);
        }

        static Color C(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }
        static Color C(byte r, byte g, byte b, byte a) => new Color32(r, g, b, a);

        static FindNames[] Names(params FindNames[] n) => n;

        static ChapterDef Ch(string title, GoalKind goal, int count, FindKind kind, RoomKind room, string outro, params string[] intro) =>
            new ChapterDef { Title = title, Goal = goal, Count = count, Kind = kind, Room = room, Outro = outro, Intro = intro };

        static ThemeDef[] Build()
        {
            const string quiet = "Sessiz yaklaş  ·  SHIFT";
            const string light = "LED'i yak  ·  L";
            const string sonar = "Sonar at  ·  X";
            const string wait = "Yanında bekle";
            const string both = "Işıkla çağır, sessiz kal";

            var home = new ThemeDef
            {
                Id = ThemeId.Home,
                Name = GameInfo.Title,
                Tagline = "Gördüğün ev, gerçek ev. Küçük evcikleri bul, ama gölgelerden kaç.",
                RobotName = "MİNİK",
                CommsName = "EV DEFTERİ  //  KANAL 4",
                Palette = new ThemePalette
                {
                    Primary = C("#FFB86B"), Text = C("#FFF6EA"), TextDim = C("#B8A898"), Warning = C("#FFD166"),
                    Danger = C("#FF6B6B"), Success = C("#8FE3A0"), Panel = C(24, 16, 12, 185),
                },
                SunColor = C("#FFF0D6"), SunIntensity = 2.0f,
                AmbientSky = C("#BFC7D6"), AmbientEquator = C("#A89C8E"), AmbientGround = C("#5C5248"),
                Finds = Names(new FindNames("TOZ TOPU", quiet), new FindNames("IŞIK KURDU", light), new FindNames("GİZLİ CEP", sonar),
                              new FindNames("UNUTULMUŞ TOHUM", wait), new FindNames("EVİN KALBİ", both)),
                Chapters = new[]
                {
                    Ch("BÖLÜM 1  ·  EVİN SESİ", GoalKind.DeliverAny, 3, FindKind.Relic, RoomKind.Hall,
                        "Üç kayıt Ev Defteri'nde. Ev seni tanımaya başladı.",
                        "Ev Defteri'nden Minik'e: bu evde senin boyundaki küçük evcikler yaşıyor. Sen robot olunca onları görebiliyorsun.",
                        "Seyrek çıkarlar: odaları dolaş, sensörün haritayı doldurdukça bir şey belirebilir. Yanında bekle, kayıt al, üsse getir."),
                    Ch("BÖLÜM 2  ·  TOZ TOPLARI", GoalKind.DeliverKind, 2, FindKind.Shy, RoomKind.Hall,
                        "Toz topları sana alıştı. Ama dikkat: gölgeler uyandı.",
                        "Toz topları gürültüden ürker. SHIFT'e basılı tut ya da vitesi düşür, sessizce yaklaş.",
                        "Bir de uyarı: ev bazen soğuyor. Soğuk bir şey yaklaşırsa o bir GÖLGE. Onlardan koş ya da üsse sığın."),
                    Ch("BÖLÜM 3  ·  IŞIK KURTLARI", GoalKind.DeliverKind, 2, FindKind.Lure, RoomKind.Hall,
                        "Işık kurtları yolunu buldu.",
                        "Işık kurtları LED'e gelir. L ile ışığı yak ve çağır. Ama gölgeler de ışığı fark eder; ışıkla fazla oyalanma."),
                    Ch("BÖLÜM 4  ·  GİZLİ CEPLER", GoalKind.DeliverKind, 2, FindKind.Hidden, RoomKind.Hall,
                        "Evin gizli ceplerini artık biliyorsun.",
                        "Bazıları mobilyaların köşesine saklanmış. X ile sonar at; yakındakileri kısa süre gösterir."),
                    Ch("BÖLÜM 5  ·  MUTFAK", GoalKind.DeliverRoom, 3, FindKind.Relic, RoomKind.Kitchen,
                        "Mutfak da artık senin.",
                        "Mutfak evin kalbi gibi; en çok orada şey çıkar. Git, üç kayıt getir. Gölgelere dikkat."),
                    Ch("BÖLÜM 6  ·  EVİN KALBİ", GoalKind.Legend, 1, FindKind.Legend, RoomKind.Hall,
                        "Evin Kalbi Ev Defteri'nde. Ev artık huzurlu. İstersen gezmeye devam et: yeni evcikler çıkar, gölgeler de.",
                        "Son kayıt: Evin Kalbi. Işığı sever ama gürültüden ürker.",
                        "LED'ini yak, çok yavaş yaklaş ve yanında bekle."),
                },
            };

            return new[] { home };
        }
    }

    /// <summary>Arayüzün tek renk kaynağı. ThemeCatalog.Set çağrılınca seçili temanın renkleri buraya yazılır.</summary>
    public static class UiTheme
    {
        public static Color Primary = new Color32(0x5F, 0xE3, 0xFF, 255);
        public static Color Text = new Color32(0xE8, 0xF6, 0xFF, 255);
        public static Color TextDim = new Color32(0x7F, 0xA3, 0xB5, 255);
        public static Color Warning = new Color32(0xFF, 0xB4, 0x54, 255);
        public static Color Danger = new Color32(0xFF, 0x5C, 0x5C, 255);
        public static Color Success = new Color32(0x73, 0xFF, 0x9A, 255);
        public static Color Panel = new Color32(0x05, 0x10, 0x1A, 168);
        public static Color Track = new Color32(0xFF, 0xFF, 0xFF, 28);

        static bool applied;

        public static void Apply(ThemePalette p)
        {
            Primary = p.Primary; Text = p.Text; TextDim = p.TextDim; Warning = p.Warning;
            Danger = p.Danger; Success = p.Success; Panel = p.Panel;
            applied = true;
        }

        /// <summary>Henüz uygulanmadıysa kayıtlı temanın renklerini uygular (arayüz kurulmadan önce çağrılır).</summary>
        public static void EnsureApplied()
        {
            if (!applied) Apply(ThemeCatalog.Def.Palette);
        }

        public static Color WithAlpha(Color c, float a) { c.a = a; return c; }
    }
}
