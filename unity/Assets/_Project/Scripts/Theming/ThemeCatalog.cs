using System;
using ARaki.Game;
using ARaki.World;
using UnityEngine;

namespace ARaki.Theming
{
    public enum ThemeId { Home = 0, Safari = 1, Space = 2, Mystery = 3 }

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

            var safari = new ThemeDef
            {
                Id = ThemeId.Safari,
                Name = "MİNYATÜR SAFARİ",
                Tagline = "Evin yerleri bir ormanın tabanı. Küçük canlıları bul, atlasa işle.",
                RobotName = "KAŞİF-7",
                CommsName = "SAHA ATLASI  //  KANAL 4",
                Palette = new ThemePalette
                {
                    Primary = C("#8FE35A"), Text = C("#F4F2E4"), TextDim = C("#9DB394"), Warning = C("#FFC857"),
                    Danger = C("#FF6B5A"), Success = C("#7CE38B"), Panel = C(8, 18, 12, 178),
                },
                SunColor = C("#FFE2A8"), SunIntensity = 2.0f,
                AmbientSky = C("#9FC48A"), AmbientEquator = C("#7C8F68"), AmbientGround = C("#4A4A38"),
                Finds = Names(new FindNames("POFUDUK", quiet), new FindNames("ATEŞBÖCEĞİ", light), new FindNames("GİZLİ YUVA", sonar),
                              new FindNames("ESKİ TOHUM", wait), new FindNames("KÖK ANA", both)),
                Chapters = new[]
                {
                    Ch("BÖLÜM 1  ·  İLK İZ", GoalKind.DeliverAny, 3, FindKind.Relic, RoomKind.Hall,
                        "İlk üç kayıt atlasta. Orman seni tanıyor.",
                        "Saha Atlası'ndan Kaşif-7'ye: hoş geldin. Bu evin zeminleri aslında bir ormanın tabanı. Küçük canlılar yerlerde, mobilyaların arasında yaşıyor.",
                        "Yaklaşınca sensörlerin onları görür. Birinin yanında bekle, kayıt alınır. Kayıtları üsse getirince atlasa işlenir."),
                    Ch("BÖLÜM 2  ·  ÜRKEK CANLAR", GoalKind.DeliverKind, 2, FindKind.Shy, RoomKind.Hall,
                        "Pofuduklar sana alıştı.",
                        "Pofuduklar gürültüden ürker. SHIFT'e basılı tutarak ya da vitesi (R/F) düşürerek sessizce yaklaş. Hızlı gidersen kaçarlar."),
                    Ch("BÖLÜM 3  ·  ATEŞBÖCEKLERİ", GoalKind.DeliverKind, 2, FindKind.Lure, RoomKind.Hall,
                        "Işığın artık orman için bir fener.",
                        "Ateşböcekleri ışığa gelir. L tuşuyla LED'i yak ve yanına çağır. Ama pil harcar; üsse dönüp şarj olmayı unutma."),
                    Ch("BÖLÜM 4  ·  GİZLİ YUVALAR", GoalKind.DeliverKind, 2, FindKind.Hidden, RoomKind.Hall,
                        "Evin en kuytu yerleri artık haritanda.",
                        "Bazı canlılar mobilyaların köşesine saklanmış. X tuşuyla sonar at; yakındaki gizli yuvaları kısa süre gösterir."),
                    Ch("BÖLÜM 5  ·  MUTFAK BAHÇESİ", GoalKind.DeliverRoom, 3, FindKind.Relic, RoomKind.Kitchen,
                        "Mutfak bahçesi atlasa eklendi.",
                        "Mutfak ormanın en canlı köşesi. Oraya git, üç kayıt getir."),
                    Ch("BÖLÜM 6  ·  KÖK ANA", GoalKind.Legend, 1, FindKind.Legend, RoomKind.Hall,
                        "Kök Ana atlasa işlendi. Orman artık yalnız değil. Gezmeye devam edebilirsin: yeni canlılar çıkmaya devam eder.",
                        "Atlasın son sayfası: Kök Ana. Orman onun etrafında büyüdü. Işığı sever ama gürültüden ürker.",
                        "LED'ini yak, çok yavaş yaklaş ve yanında bekle."),
                },
            };

            var space = new ThemeDef
            {
                Id = ThemeId.Space,
                Name = "UZAK GEZEGEN",
                Tagline = "Ev, ASTRA-7 yüzeyi. Yabancı yaşamı ve kayıp istasyonu bul.",
                RobotName = "ARAKI-7",
                CommsName = "MERKEZ  //  KANAL 4",
                Palette = new ThemePalette
                {
                    Primary = C("#5FE3FF"), Text = C("#E8F6FF"), TextDim = C("#7FA3B5"), Warning = C("#FFB454"),
                    Danger = C("#FF5C5C"), Success = C("#73FF9A"), Panel = C(5, 16, 26, 170),
                },
                SunColor = C("#AFC8FF"), SunIntensity = 1.5f,
                AmbientSky = C("#4A5C9A"), AmbientEquator = C("#303A66"), AmbientGround = C("#1A1C2C"),
                Finds = Names(new FindNames("PLAZMA LARVASI", quiet), new FindNames("FOTON GÜVESİ", light), new FindNames("GÖMÜLÜ SİNYAL", sonar),
                              new FindNames("METEOR PARÇASI", wait), new FindNames("ORBİT-9 ÇEKİRDEĞİ", both)),
                Chapters = new[]
                {
                    Ch("BÖLÜM 1  ·  İNİŞ", GoalKind.DeliverAny, 3, FindKind.Relic, RoomKind.Hall,
                        "İlk örnekler laboratuvarda. ASTRA-7 canlı bir gezegen.",
                        "Merkez'den ARAKI-7'ye: ASTRA-7 yüzeyine indin. Bu bölgenin haritası yok. Sür, sensörlerinle çevreyi tara.",
                        "Yüzeyde yaşam belirtileri ve mineral parçaları var. Birinin yanında dur, örnek al. Üsse getirince laboratuvara işlenir."),
                    Ch("BÖLÜM 2  ·  PLAZMA LARVALARI", GoalKind.DeliverKind, 2, FindKind.Shy, RoomKind.Hall,
                        "Larvalar titreşimine alıştı.",
                        "Plazma larvaları titreşimden kaçıyor. SHIFT ile motorlarını kıs ya da vitesi düşür; sessizce yaklaş."),
                    Ch("BÖLÜM 3  ·  FOTON GÜVELERİ", GoalKind.DeliverKind, 2, FindKind.Lure, RoomKind.Hall,
                        "Foton güveleri artık seni izliyor.",
                        "Foton güveleri ışığa akıyor. L ile LED'ini yak ve yanına çağır. Yakıt harcadığını unutma."),
                    Ch("BÖLÜM 4  ·  GÖMÜLÜ SİNYALLER", GoalKind.DeliverKind, 2, FindKind.Hidden, RoomKind.Hall,
                        "Yüzeyin altı artık haritanda.",
                        "Yüzeyin altında zayıf sinyaller var. X ile sonar at; gömülü olanlar kısa süre ortaya çıkar."),
                    Ch("BÖLÜM 5  ·  UZAK PLATO", GoalKind.DeliverRoom, 3, FindKind.Relic, RoomKind.Kitchen,
                        "Uzak plato tarandı.",
                        "Kuzeydoğuda yoğun bir aktivite var. Orayı tara, üç örnek getir."),
                    Ch("BÖLÜM 6  ·  ORBİT-9", GoalKind.Legend, 1, FindKind.Legend, RoomKind.Hall,
                        "ORBİT-9 çevrimiçi. Veri aktarımı tamam; Merkez seni tebrik ediyor. İstersen keşfe devam et.",
                        "ORBİT-9'un çekirdeği hâlâ çalışıyor ve seni bekliyor. Işıkla çağırır, titreşimle kaçırırsın.",
                        "LED'ini yak, çok yavaş yaklaş ve yanında bekle."),
                },
            };

            var mystery = new ThemeDef
            {
                Id = ThemeId.Mystery,
                Name = "EV GİZEMİ",
                Tagline = "Bu ev bir şey saklıyor. İpuçlarını topla, hatırayı çöz.",
                RobotName = "ÇIRAK-7",
                CommsName = "BÜRO  //  KANAL 4",
                Palette = new ThemePalette
                {
                    Primary = C("#E8C27A"), Text = C("#F3E9D8"), TextDim = C("#A8998A"), Warning = C("#F0A04B"),
                    Danger = C("#D4474F"), Success = C("#8FD9A8"), Panel = C(14, 10, 12, 205),
                },
                SunColor = C("#9A9AD0"), SunIntensity = 0.9f,
                AmbientSky = C("#3A3452"), AmbientEquator = C("#2A2438"), AmbientGround = C("#17131C"),
                Finds = Names(new FindNames("TOZ HAYALETİ", quiet), new FindNames("FENER RUHU", light), new FindNames("SAKLI NOT", sonar),
                              new FindNames("ESKİ ANAHTAR", wait), new FindNames("EVİN HATIRASI", both)),
                Chapters = new[]
                {
                    Ch("BÖLÜM 1  ·  İLK İPUCU", GoalKind.DeliverAny, 3, FindKind.Relic, RoomKind.Hall,
                        "Üç ipucu masada. Ev konuşmaya başladı.",
                        "Büro'dan Çırak-7'ye: bu ev bir şey saklıyor. Eski kayıtlar odalarda hayaletlerin ve ipuçlarının dolaştığını söylüyor.",
                        "Birine yaklaşınca dur, bekle: kayıt alınır. Her kaydı üsse getir; Büro hepsini birleştirip hikayeyi çıkaracak."),
                    Ch("BÖLÜM 2  ·  TOZ HAYALETLERİ", GoalKind.DeliverKind, 2, FindKind.Shy, RoomKind.Hall,
                        "Hayaletler artık senden çekinmiyor.",
                        "Toz hayaletleri gürültüden çekilir. SHIFT'e basılı tutarak ya da vitesi düşürerek sessizce yaklaş."),
                    Ch("BÖLÜM 3  ·  FENER RUHLARI", GoalKind.DeliverKind, 2, FindKind.Lure, RoomKind.Hall,
                        "Fener ruhları yolunu buldu.",
                        "Fener ruhları ışığın peşinden gelir. L ile LED'ini yak ve onlara yolu göster. Pili izle."),
                    Ch("BÖLÜM 4  ·  SAKLI NOTLAR", GoalKind.DeliverKind, 2, FindKind.Hidden, RoomKind.Hall,
                        "Gizli notlar masada. Hikaye şekilleniyor.",
                        "Notlar mobilyaların arkasına gizlenmiş. X ile sonar at; yakındaki gizli şeyleri kısa süre gösterir."),
                    Ch("BÖLÜM 5  ·  MUTFAKTAKİ SIR", GoalKind.DeliverRoom, 3, FindKind.Relic, RoomKind.Kitchen,
                        "Mutfağın sırrı çözüldü.",
                        "Mutfakta bir şey var; oradan dönenler hep aynı şeyi anlatıyor. Git, üç kayıt getir."),
                    Ch("BÖLÜM 6  ·  EVİN HATIRASI", GoalKind.Legend, 1, FindKind.Legend, RoomKind.Hall,
                        "Evin hatırası paylaşıldı: burası bir zamanlar kalabalık, neşeli bir yuvaydı. Gizem çözüldü. Dilersen gezmeye devam et.",
                        "Her şey tek bir hatıraya çıkıyor. Işıkla çağırır, sessizce beklersen yaklaşır.",
                        "LED'ini yak, çok yavaş yaklaş ve yanında bekle."),
                },
            };

            var home = new ThemeDef
            {
                Id = ThemeId.Home,
                Name = "EVİMİZ",
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

            return new[] { home, safari, space, mystery };
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
