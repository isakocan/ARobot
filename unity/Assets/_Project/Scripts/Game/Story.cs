namespace ARaki.Game
{
    public readonly struct Chapter
    {
        public readonly string Title;      // "BÖLÜM 1  ·  SİNYAL"
        public readonly string[] Lines;    // merkezden gelen telsiz mesajları, sırayla

        public Chapter(string title, params string[] lines)
        {
            Title = title;
            Lines = lines;
        }
    }

    /// <summary>
    /// Oyunun hikayesi. ORBİT-9 araştırma istasyonu bir güneş fırtınasında kontrolden çıkıp bu evin salonuna
    /// "iniş" yaptı. Merkez, keşif robotu ARAKI-7'yi gönderiyor: istasyonu tara, enerjisini yeniden bağla.
    /// Metinler burada toplanır; ileride dil desteği ya da dışarıdan (LLM) üretilen görevler de buradan beslenir.
    /// </summary>
    public static class Story
    {
        public const string StationName = "ORBİT-9";

        public static Chapter For(int level)
        {
            switch (level)
            {
                case 1:
                    return new Chapter("BÖLÜM 1  ·  SİNYAL",
                        "ARAKI-7, burası Merkez. ORBİT-9 araştırma istasyonu bu sektöre düştü. Ekipten ses yok.",
                        "Önce istasyonu her yönden tara. Dur, bekle, sonra sıradaki yöne geç.",
                        "Enerji hücresi isteğe bağlı: yakıtın azalırsa şarj olabilirsin.");
                case 2:
                    return new Chapter("BÖLÜM 2  ·  ZİYARETÇİLER",
                        "İstasyonun kayıtları açıldı. Etrafta otonom devriye sondaları var; istasyonu koruyor gibi.",
                        "Görüş alanlarına girme. Yakalanırsan LED'i yüzlerine tut.");
                case 3:
                    return new Chapter("BÖLÜM 3  ·  PARLAMA",
                        "Güneş parlamaları sıklaştı. Parlama uyarısı gelince mobilyanın gölgesine gir, açıkta kalma.",
                        "Yeni bir sonda tipi görüldü: daha hızlı, daha dar bakıyor.");
                case 4:
                    return new Chapter("BÖLÜM 4  ·  KAYIP KAYIT",
                        "İstasyonun ana bilgisayarı parçalanmış. Her sektör bir veri parçası saklıyor.",
                        "Sondalar artık birbirine haber veriyor gibi. Dikkatli ol.");
                default:
                    return new Chapter($"BÖLÜM {level}  ·  SON SİNYAL",
                        "Merkez'den ARAKI-7'ye: veri aktarımı sürüyor. Bu sektör diğerlerinden zor.",
                        "Parlamalar ve sondalar tam güçte. Yakıtını ve gölgeleri iyi kullan.");
            }
        }

        public static string StationStatus(int completedSectors, int totalSectors)
        {
            if (completedSectors >= totalSectors) return "ÇEVRİMİÇİ";
            if (completedSectors == 0) return "ÇEVRİMDIŞI";
            return "BAĞLANTI  " + completedSectors + "/" + totalSectors;
        }
    }
}
