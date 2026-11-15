"""Geliştirici ekranı arka uç sözleşmesi.

Sayfa ve sunucu konum/harita yönteminin ne olduğunu bilmez; yalnızca bu sözleşmedeki sözlükleri taşır ve çizer.
Yeni bir yöntem (ör. harici bir SLAM kitaplığı) `ArkaUc` alt sınıfı yazılıp `calistir.py --arkauc <modul>:<Sinif>` ile seçilir,
sayfaya dokunulmaz.

KOORDİNAT UZAYLARI (katman öğelerinde `uzay`):
  "pano"   eşdikdörtgen panorama, normalize (u, v) in [0, 1] (sol-üst = 0,0). u: azimut, orta = ileri.
  "ham"    çift balık gözü ham kare (1280x720), normalize (u, v) in [0, 1].
  "harita" üstten bakış, metre (x doğu, y kuzey; poz sözleşmesi tools/konum_deney/konum_ortak.py ile aynı, yön = kuzeyden saat yönünde derece).

feed(kare_jpeg, t_ms, komut) -> durum sözlüğü:
  {
   "kare":  int                       arka ucun kare sayacı
   "t_ms":  int                       kare zamanı (girişteki)
   "poz":   {"x","y","yon", ...}      metre, metre, derece; ek anahtarlar (sigma, guven ...) serbest, sayfa "poz_ek" içinde gösterir
   "katmanlar": { katman_id: [öğe, ...] }          kare üstü (her karede yeniden verilen) çizimler; öğe biçimleri aşağıda
   "harita_islem": [ işlem, ... ]                  haritada KALICI değişiklikler (sunucu birikimini tutar, yeni istemciye özet verir)
   "sure_ms": { ad: ms }              adlandırılmış aşama süreleri (karedeki son değer; eşzamansız aşamalar son bitenin süresi)
   "bilgi":  { "profil": [{"ad","deger","birim"}], "ek": [{"ad","deger"}] }     durum şeridi için adlandırılmış değerler
   "uyari":  [str]                    isteğe bağlı kısa uyarılar
  }

ÖĞE BİÇİMLERİ (katmanlar içinde; hepsinde "uzay"):
  {"tur":"noktalar","uzay":..,"xy":[x0,y0,x1,y1,...],"etiket":[opsiyonel kategori sayıları]}
  {"tur":"cizgiler","uzay":..,"xy":[ax,ay,bx,by, ...]}                 ayrık doğru parçaları (her 4 sayı bir parça)
  {"tur":"kirik","uzay":..,"xy":[...],"kapali":false}                   tek çoklu çizgi
  {"tur":"resim","uzay":..,"png":"<base64 PNG>"}                        görünümün tamamına gerilen RGBA maske
  {"tur":"ok","uzay":"harita","x":..,"y":..,"yon":..}                   yönlü ok (derece, kuzeyden saat yönü)
KATMAN TANIMLARI (`katman_tanimlari()`): [{"id","ad","uzay": [..],"renk":"#rrggbb","varsayilan":bool,"grup":"kamera"|"harita"}]
  Kamera katmanları sayfada "filtreler" düğmeleri olur; "harita" grubundakiler harita panelinde.
STATİK KATMANLAR (`statik_katmanlar()`): bağlanınca bir kez yollanan, sabit çizimler ({katman_id: [öğe]}), ör. gövde maskesi, ufuk çizgisi.
HARİTA İŞLEMLERİ ("harita_islem" öğeleri):
  {"katman":"yorunge","islem":"ekle","xy":[x,y,...]}                   çoklu çizgiye nokta ekler
  {"katman":"anahtar","islem":"ekle","xy":[x,y,...]}                   nokta kümesine ekler
  {"katman":"doluluk","islem":"yerlestir","x0":m,"y0":m,"hucre":m,"w":int,"h":int,"png":b64}
        gri PNG, w x h hücre; (x0, y0) = SOL-ALT köşenin dünya konumu; 0 = dolu, 128 = bilinmiyor, 255 = boş; satır 0 = kuzey
  {"islem":"temizle"}                                                   harita katmanlarının hepsini boşaltır (sıfırlama)
"""


class ArkaUc:
    """Konum/harita yöntemi için küçük sözleşme. Alt sınıf en az `feed`, `sifirla`, `katman_tanimlari` yazar."""

    ad = "arkauc"

    def katman_tanimlari(self):
        return []

    def statik_katmanlar(self):
        return {}

    def gorunumleri_ayarla(self, gorunumler):
        """Sayfanın şu an görmek istediği kamera görünümleri (küme: {"pano","ham"}); arka uç yalnız bunlar için öğe üretebilir (tasarruf)."""

    def feed(self, kare_jpeg, t_ms, komut):
        """Tek kare işler. kare_jpeg: ham JPEG baytı, t_ms: tam sayı ms (kaynağın saati), komut: (sol, sag) PWM -255..255 (ileri +)."""
        raise NotImplementedError

    def sifirla(self):
        """Konum ve haritayı başa al (poz 0,0,0; harita boş)."""
        raise NotImplementedError

    def panorama_goruntu(self, kare_bgr, genislik=1024):
        """İsteğe bağlı: sayfadaki 'panorama' görünümü için BGR eşdikdörtgen görüntü (yoksa None; sayfa yalnız ham gösterir)."""
        return None

    def kapat(self):
        pass
