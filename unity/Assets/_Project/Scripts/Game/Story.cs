using UnityEngine;

namespace ARaki.Game
{
    public readonly struct Chapter
    {
        public readonly string Title;      // "BÖLÜM 1  ·  İNİŞ"
        public readonly string[] Lines;    // merkezden gelen telsiz mesajları, sırayla

        public Chapter(string title, params string[] lines)
        {
            Title = title;
            Lines = lines;
        }
    }

    /// <summary>
    /// Oyunun hikayesi. Keşif robotu ARAKI-7, uzak bir gezegenin (ASTRA-7) yüzeyine indirildi. Gezegenin haritası yok;
    /// robot sensörleriyle çevreyi haritalayacak, mineral numuneleri toplayacak ve yıllar önce kaybolan ORBİT-9
    /// araştırma istasyonunun zayıf sinyalini bulup tarayacak. Üs, robotun ineceği/döneceği kamp noktası.
    /// Metinler burada toplanır; ileride dil desteği ya da dışarıdan (LLM) üretilen görevler de buradan beslenir.
    /// </summary>
    public static class Story
    {
        public const string PlanetName = "ASTRA-7";
        public const string StationName = "ORBİT-9";

        public static Chapter For(int level)
        {
            switch (level)
            {
                case 1:
                    return new Chapter("BÖLÜM 1  ·  İNİŞ",
                        "ARAKI-7, burası Merkez. " + PlanetName + " yüzeyine hoş geldin. Bu bölgenin haritası yok.",
                        "Sensörlerin açık: sür, çevreyi haritala. Karanlık bölgeler henüz keşfedilmedi.",
                        "Yüzeyde mineral numuneleri var. Topla, üsse getirince şarj olur, kayıtlarını teslim edersin.");
                case 2:
                    return new Chapter("BÖLÜM 2  ·  BEKÇİLER",
                        "Haritaladığın bölgede hareket var. Yüzeyde otonom bekçiler dolaşıyor; kim bıraktı, bilmiyoruz.",
                        "Görüş alanlarına girme. Yakalanırsan LED'i yüzlerine tut.");
                case 3:
                    return new Chapter("BÖLÜM 3  ·  PARLAMA",
                        "Yıldız parlamaları sıklaştı. Uyarı gelince en yakın gölgeye gir; açıkta kalırsan yakıtını kaybedersin.",
                        "Yeni bir bekçi tipi görüldü: daha hızlı, daha dar bakıyor.");
                case 4:
                    return new Chapter("BÖLÜM 4  ·  KAYIP KAYIT",
                        "Haritan büyüdükçe " + StationName + " kayıtları çözülüyor. İstasyonun ana bilgisayarı parçalanmış.",
                        "Her sektör bir veri parçası saklıyor. Bekçiler birbirine haber veriyor gibi; dikkatli ol.");
                default:
                    return new Chapter($"BÖLÜM {level}  ·  SON SİNYAL",
                        "Merkez'den ARAKI-7'ye: veri aktarımı sürüyor. Bu bölge diğerlerinden zor.",
                        "Parlamalar ve bekçiler tam güçte. Yakıtını, gölgeleri ve üssü iyi kullan.");
            }
        }

        /// <summary>Üssün konumuna göre sinyalin yönü ve yaklaşık uzaklığı (kuzey = +Z).</summary>
        public static string Hint(Vector3 basePosition, Vector3 stationPosition)
        {
            var d = stationPosition - basePosition;
            d.y = 0f;
            if (d.sqrMagnitude < 0.01f) return null;
            string[] names = { "kuzeyden", "kuzeydoğudan", "doğudan", "güneydoğudan", "güneyden", "güneybatıdan", "batıdan", "kuzeybatıdan" };
            float angle = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;
            int index = Mathf.RoundToInt(angle / 45f) % 8;
            return "Zayıf bir " + StationName + " sinyali yakalıyoruz: " + names[index] + ", yaklaşık " + Mathf.RoundToInt(d.magnitude) + " metre.";
        }

        public static string StationStatus(int completedSectors, int totalSectors)
        {
            if (completedSectors >= totalSectors) return "ÇEVRİMİÇİ";
            if (completedSectors == 0) return "ÇEVRİMDIŞI";
            return "BAĞLANTI  " + completedSectors + "/" + totalSectors;
        }
    }
}
