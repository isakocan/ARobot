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
   "izleme": {"durum":"iyi"|"zayif"|"kayip"|"tut","etiket":str(ops.),"metin":str}   izleme durumu (durum satırındaki rozet; etiket yoksa varsayılan ad); yoksa gizlenir
   "ozet":   [{"ad":str,"deger":..}]  durum satırına eklenen yöntemin kendi hücreleri (ör. anahtar kare sayısı)
  }

GENEL KATMAN TÜRLERİ (kamera görünümlerinde; hepsi arka uca bağımsız, sayfa yalnız çizer)
  {"tur":"noktalar","uzay":..,"xy":[...],"deger":[v0,v1,...],"boyut":px}
        "deger" varsa her nokta bir SKALAR taşır ve renk eşlemesiyle çizilir (ör. izlenen noktanın derinliği). Aralık ve renk eşlemesi
        katman tanımındaki "lejant" alanından gelir: {"ad","birim","aralik":[lo,hi],"harita":"turbo"}; sayfa lejantı (renk çubuğu + sayılar) çizer.
  {"tur":"cizgiler","uzay":..,"xy":[...],"deger":[parça başına skalar (isteğe bağlı)]}   (epipolar doğru parçaları, hareket vektörleri...)
  GÜVEN EŞİĞİ (genel): izgara işlemi "guven_png" (8 bit gri, "png" ile aynı boyutta, 0..255) ve nokta öğeleri "guven" ([0..1] her nokta için)
  taşıyabilir; sayfadaki tek "güven eşiği" kaydırıcısı bunun altını gizler (varsayılan zayıf kanıtı gizler). Taşımayan katman etkilenmez.
  {"tur":"skalar_resim","uzay":..,"png":"<base64 PNG, 8 bit gri>","aralik":[lo,hi]}
        Piksel başına skalar kaplama: 0 = veri yok (saydam), 1..254 = aralıkta doğrusal değer; renk eşlemesi katmanın "lejant"ından.
        DERİNLİK UYDURULMAZ: yalnız yöntemin gerçekten bildiği pikseller doldurulur.
3B BÖLÜMÜ (uzay "3d"; metre, z YUKARI, x doğu, y kuzey; yön = kuzeyden saat yönünde derece)
  Kalıcı (artımlı) işlemler "harita_islem" içinde, katman tanımında "grup":"3d" ve "tur3d":
    "noktalar"  {"katman":id,"islem":"ekle","xyz":[x,y,z,...],"deger":[...](ops.),"renk":[r,g,b,...0..1](ops.)}   artımlı; 50 bin+ nokta için tasarlandı
    "parca"     {"katman":id,"islem":"ekle","xyz":[ax,ay,az,bx,by,bz,...]}      ayrık doğru parçaları
    "cizgi"     {"katman":id,"islem":"ekle","xyz":[...]}                         tek çoklu çizgiye nokta ekler (yörünge)
    "kameralar" {"katman":id,"islem":"ekle","kameralar":[{"x","y","z","yon","egim"(ops.)}]}   anahtar kare koniği (frustum)
    "zemin"     sayfanın "doluluk" ızgarasını z=0 düzleminde doku olarak çizer (arka uç bir şey yollamaz)
  Kare üstü: katmanlar[id] = [{"tur":"kamera","uzay":"3d","x","y","z","yon","guncel":true}]  (güncel kamera, vurgulanır)

ÖĞE BİÇİMLERİ (katmanlar içinde; hepsinde "uzay"):
  {"tur":"noktalar","uzay":..,"xy":[x0,y0,x1,y1,...],"etiket":[opsiyonel kategori sayıları]}
  {"tur":"cizgiler","uzay":..,"xy":[ax,ay,bx,by, ...]}                 ayrık doğru parçaları (her 4 sayı bir parça)
  {"tur":"kirik","uzay":..,"xy":[...],"kapali":false}                   tek çoklu çizgi
  {"tur":"resim","uzay":..,"png":"<base64 PNG>"}                        görünümün tamamına gerilen RGBA maske
  {"tur":"ok","uzay":"harita","x":..,"y":..,"yon":..}                   yönlü ok (derece, kuzeyden saat yönü)
KATMAN TANIMLARI (`katman_tanimlari()`): [{"id","ad","uzay": [..],"renk":"#rrggbb","varsayilan":bool,"grup":"kamera"|"harita"}]
  Kamera katmanları sayfada "filtreler" düğmeleri olur; "harita" grubundakiler harita panelinde.
STATİK KATMANLAR (`statik_katmanlar()`): bağlanınca bir kez yollanan, sabit çizimler ({katman_id: [öğe]}), ör. gövde maskesi, ufuk çizgisi.
ID'Lİ KÜME İŞLEMLERİ (noktaların sonradan hareket edebildiği/silinebildiği yöntemler: SLAM nokta bulutu, anahtar kareler):
  {"katman":id,"islem":"guncelle","kimlik":[id...],"xyz":[x,y,z,...],"guven":[0..1,...](ops.),"sil":[id...](ops.)}
        id varsa YERİNDE günceller, yoksa ekler; "sil" listesindekileri kaldırır. Kameralar için "xyz" yerine "kameralar":[{"x","y","z","yon"}] (kimlik sırasıyla).
        Sunucu birikimi tutar, istemciye yalnız değişen idler gider (yeni bağlanana tam özet).
  {"katman":id,"islem":"temizle_katman"}                                tek katmanın birikimini boşaltır (çevrim kapamada yenileme)
  Katman tanımında "tur2d":"cizgi"|"elmas"|"kume"|"izgara" (2B harita çizimi) ve "tur3d":"noktalar"|"parca"|"cizgi"|"kameralar"|"kume"|"zemin"; 3B "kume" ve
  "kameralar" katmanları "kume":<2B kümenin katman kimliği> ile aynı birikimi paylaşabilir.
HARİTA İŞLEMLERİ ("harita_islem" öğeleri):
  {"katman":"yorunge","islem":"ekle","xy":[x,y,...]}                   çoklu çizgiye nokta ekler
  {"katman":"anahtar","islem":"ekle","xy":[x,y,...]}                   nokta kümesine ekler
  {"katman":"doluluk","islem":"yerlestir","x0":m,"y0":m,"hucre":m,"w":int,"h":int,"png":b64}
        gri PNG, w x h hücre; (x0, y0) = SOL-ALT köşenin dünya konumu; 0 = dolu, 128 = bilinmiyor, 255 = boş; satır 0 = kuzey
  {"islem":"temizle"}                                                   harita katmanlarının hepsini boşaltır (sıfırlama)
"""


class ArkaUc:
    """Konum/harita yöntemi için küçük sözleşme. Alt sınıf en az `feed`, `sifirla`, `katman_tanimlari` yazar.
    Sayfa HİÇBİR yöntemi varsaymaz: katmanlar, harita türleri, durum alanları, profil/ayrıntı değerleri bu sınıfın bildirdiklerinden gelir;
    bildirilmeyen (boş) şey sayfada gizlenir. `yontem_adi` başlıkta "Yöntem: ..." olarak her zaman görünür."""

    ad = "arkauc"
    yontem_adi = "bilinmeyen yöntem"          # başlıkta görünür (kısa, Türkçe, hangi yöntemin çalıştığı belli olsun)
    yontem_aciklama = ""                       # ipucu metni (isteğe bağlı)
    varsayilan_gorunum = "ham"                 # bu yöntem seçilince sayfanın açacağı kamera görünümü ("ham" | "pano")

    def olcek_bilgisi(self):
        """Uzunluk birimi: {"birim":"m","metrik":True} ya da {"birim":"birim","metrik":False,"not":"ölçek bilinmiyor"}. Sayfa eksen/ölçek çubuğu/durum
        etiketlerini buna göre yazar (metrik değilse "m" yazmaz, uyarı notu gösterir)."""
        return {"birim": "m", "metrik": True}

    def kamera_panelleri(self):
        """Sayfanın solundaki küçük kamera panelleri: [{"id","ad","katmanlar":[kamera katman kimlikleri]}]. None = tek panel, tüm kamera katmanları."""
        return None

    def guven_bilgisi(self):
        """Güven eşiği kaydırıcısı: {"ad":"Güven eşiği","birim":"gözlem","carpan":5,"min":1,"max":5,"varsayilan":2} (eşik = değer/carpan, katmandaki guven [0..1] ile karşılaştırılır).
        None = kaydırıcı yalnız güven verisi gelince (genel % olarak) görünür."""
        return None

    def eylemler(self):
        """Sayfadaki küçük "Eylemler" menüsü: {"dugmeler":[{"id","ad","uyari"?}], "secimler":[{"id","ad","deger","secenekler":[{"id","ad"}]}]}. Boşsa menü gizlenir."""
        return {"dugmeler": [], "secimler": []}

    def eylem(self, kimlik, arg=None):
        """Bir eylemi çalıştır (ayrı iş parçacığında çağrılır, bloklayabilir). Dönüş {"ok":bool,"mesaj":str}."""
        return {"ok": False, "mesaj": "eylem yok: %s" % kimlik}

    def katman_istegi(self, acik):
        """Sayfada şu an AÇIK katman kimlikleri (küme). Pahalı katmanlar yalnız istenirse hesaplanabilir."""

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
