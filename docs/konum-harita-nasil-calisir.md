# Kameradan konum ve harita: nasıl çalışıyor

Durum: 13 Kasım 2026. Buradaki her şey şimdilik simülasyonda denendi; gerçek robotta hiçbiri çalışmadı. Plan ve takvim için: `ar-konum-yol-haritasi.md`. Kod: `tools/konum_deney/`.

## 1. Büyük resim

Robotun tek algılayıcısı 360° kamera. Ondan üç şey çıkarıyoruz ve birleştiriyoruz:

```
kamera karesi
   │
   ├─► (A) dönüş açısı ve ilerleme yönü      "ne kadar döndüm, hangi yöne gidiyorum"
   │
   ├─► (B) her yönde zemin nerede bitiyor     "önümde, sağımda, arkamda ne kadar boş yer var"
   │
   └─► (C) harita                             (A) ile bulunan konumdan (B) ölçümlerini biriktir
```

(A) ve (B) birbirinden bağımsız. (C) ikisini kullanır. Şu an (A)'nın dönüş kısmı ve (B) çalışıyor; (C) robotun gerçek konumu hazır verilerek denendi. Eksik halka: ilerlenen mesafe (Aşama 4).

## 2. Kamera: piksel hangi yöne bakıyor

Theta S iki balık gözü mercekle 1280×720'lik bir kare verir: yan yana iki daire. Soldaki daire robotun önüne bakar.

Balık gözü mercekte kural basittir: bir noktanın merceğin baktığı eksenle yaptığı açı ne kadar büyükse, görüntüde merkezden o kadar uzağa düşer (uzaklık = açı × sabit; bizde sabit yaklaşık 181 piksel/radyan, yani 3,16 piksel/derece). Mercek merkezleri ve bu sabit, sizin daha önce `lens_ayarla.py` ile ölçtüğünüz değerler.

Bu kuralla her pikselin baktığı yön hesaplanır. Yön bilinince kare, düz bir panoramaya (eşdikdörtgen) açılır:
- yatay eksen: robotun çevresi, orta sütun = ön, sağa doğru saat yönünde,
- dikey eksen: yukarı-aşağı açı, orta satır = ufuk.

Bu panoramada iki özellik işimize yarar:
- Robot dönerse görüntü yatayda kayar (saat yönünde dönüş → görüntü sola kayar).
- Bir pikselin ufkun kaç derece altında olduğu doğrudan satır numarasından okunur.

Dosya: `kamera.py`, değerler: `kamera.json`.

## 3. (A) Dönüş açısı

### İlk fikir ve neden yetmedi
Panoramanın yatay kaymasını ölçmek (M1). Robot yerinde dönerken iyi çalışıyor (tur sonunda 2°'den az hata). Ama robot ilerlerken bozuluyor: yakındaki duvar görüntüde hızlı, uzaktaki yavaş kayar. Koridorda bir yan yakın, öbür yan uzaksa net bir kayma kalır ve yöntem "dönüyorum" sanır. Düz gidişte 46°'ye kadar yanıldı.

### Çalışan yöntem (M2D)
1. **Nokta bul.** Karede köşe gibi belirgin noktalar bulunur (ORB, en çok 800 nokta). Her nokta çevresindeki desenle tanımlanır.
2. **Eşleştir.** Aynı noktalar, daha önce saklanmış bir "anahtar kare"de aranır. Yaklaşık 300 eşleşme çıkar.
3. **Yöne çevir.** Her nokta, Bölüm 2'deki kuralla bir yön okuna dönüştürülür. Artık elimizde "bu nokta anahtar karede şu yöndeydi, şimdi bu yönde" çiftleri var.
4. **Hareketi çöz.** Robot düz zeminde olduğu için hareketinin yalnızca iki bilinmeyeni vardır: ne kadar döndü ve hangi yöne ilerledi. Olası (dönüş, ilerleme yönü) çiftleri taranır; her çift için "eşleşmelerin kaçı bu hareketle tutarlı" sayılır. En çok eşleşmeyi açıklayan çift kazanır. Tutarsız eşleşmeler (yanlış eşleşme, yürüyen insan) oy veremediği için sonucu bozmaz.

Örnek: robot 10° sağa dönüp 20 cm ilerledi. Uzak duvardaki noktaların hepsi yaklaşık 10° sola kayar. Yakındaki sandalyenin noktaları buna ek olarak ilerleme yüzünden biraz daha kayar. M1 bunları tek bir ortalama kaymaya indirgeyip yanılıyordu; M2D ise "10° dönüş + şu yöne ilerleme" çiftinin hem duvarı hem sandalyeyi birlikte açıkladığını bulur.

### Anahtar kare neden var
Her kare bir öncekiyle karşılaştırılsaydı, karede 0,1°'lik hata bile 15 kare/sn'de hızla birikirdi. Onun yerine kare, yakın geçmişte saklanmış sabit bir kareyle karşılaştırılır. Robot yerinde on tur dönse de hep aynı anahtar kareye bakar, hata birikmez. Robot yaklaşık 30 cm uzaklaşınca ya da eşleşme kalitesi düşünce yeni anahtar kare alınır; hata yalnızca o anlarda eklenir.

### Sonuç (sim, gerçek kamera benzetimi)
Yerinde dönüşte en çok 1,1°, düz gidişte 0,8°, ev turunda (21 m, kapılardan geçerek) 2,3°. Motor komutunu hiç kullanmadan da 2,6°. Model yok, tamamen klasik görüntü işleme.

Dosyalar: `yontem_pusula_nokta.py` (M2D), `yontem_pusula_profil.py` (M1), `pusula_ortak.py`.

## 4. (B) Zemin nerede bitiyor → mesafe

### Geometri
Kamera yerden h = 25,5 cm yukarıda ve zemin düz. Zemindeki bir noktaya bakarken ufkun α derece altına bakıyorsak, o nokta yatayda şu kadar uzaktadır:

```
mesafe = h / tan(α)
```

| Ufkun altında α | Mesafe |
|---|---|
| 27° | 0,5 m |
| 14° | 1 m |
| 7,3° | 2 m |
| 4,9° | 3 m |
| 2,9° | 5 m |

Yani her yön için "zemin görüntüde hangi satırda bitiyor" bulunursa, o yöndeki ilk engelin mesafesi çıkar. 360 yön için yapılınca bir lidar taraması gibi olur.

### Neden yaklaşık 3 m'den öteye güvenilmez
Tablodaki açılar uzaklaştıkça sıkışıyor: 3 m ile 5 m arası yalnızca 2°, yani gerçek kamerada 6 piksel. Bir piksellik hata 1 m'de 2 cm, 3 m'de 18-20 cm, 5 m'de 50 cm yapar. Bu kameranın fiziksel sınırı; yöntemle aşılamaz. Bu yüzden 3,2 m'den uzak ölçümler haritaya "engel var" diye yazılmaz, yalnızca "buraya kadar boş" diye yazılır. Robot zaten her yere yaklaştığı için yakın ölçüm yeter.

### Zemini kim buluyor
İki yol denendi.

**Modelsiz (klasik).** Robotun dibindeki zemin kesin zemindir. Oradan renk ve doku öğrenilir, her yönde dışa doğru "hâlâ zemine benziyor mu" diye yürünür. Sorun: duvar zeminle benzer renkteyse, kapı eşiğinde zemin malzemesi değişirse ya da robot duvara yakınsa yanılıyor. Yönlerin %40'ında büyük hata verdi. Tek başına kullanılamaz.

**Hazır model (SegFormer-B0).** Binlerce iç mekân fotoğrafıyla eğitilmiş küçük bir ağ; her piksele "zemin, halı, duvar, sandalye..." etiketi verir. Bizim veriyle eğitilmedi, olduğu gibi kullanıldı. Model düz fotoğrafla eğitildiği için panorama 8 yöne bakan 8 düz görüntüye bölünür, her biri modelden geçer, sonuçlar panoramaya geri yapıştırılır. Zemin ve halı "zemin" sayılır; her yönde zeminin bittiği satır sınırdır. Kapı eşiğindeki malzeme değişimini ve halıyı doğru çözdü. Büyük hata oranı %15-29.

Dosyalar: `zemin_klasik.py`, `zemin_model.py`, `zemin_ortak.py`.

## 5. (C) Harita nasıl birikiyor

Ev 5 cm'lik karelere bölünür. Her kare bir sayı tutar: sıfırdan büyükse "dolu olduğuna dair kanıt var", küçükse "boş olduğuna dair kanıt var", sıfırsa "bilinmiyor".

Her kamera karesinde, 360 yönün her biri için:
1. Robotun konumundan o yöne bir çizgi çekilir.
2. Çizginin zemin sınırına kadar geçtiği karelere "boş" kanıtı eklenir.
3. Sınırın düştüğü kareye "dolu" kanıtı eklenir (yalnızca sınır 3,2 m içindeyse ve ölçüm güvenliyse).

Tek ölçüm yanlış olabilir; ama aynı kare onlarca kareden, farklı açılardan görülür ve kanıtlar toplanır. Bir yanlış "dolu" oyu, yirmi "boş" oyu karşısında silinir. Sonunda her kare boş, dolu ya da bilinmiyor olarak etiketlenir.

Oyun bu haritayı şöyle kullanır: sanal nesne yalnızca "boş" kareye konur; nesne ile robot arasındaki çizgi "dolu" kareden geçiyorsa nesne gizlenir.

Dosyalar: `harita_kur.py`, `harita_olc.py`.

## 6. Deney tezgâhı

Yöntemleri robot olmadan sınamak için:
- **Sim kayıtları** (`kayitlar/sim/`): Unity'deki eşyalı evde 9 sürüş. Her karede robotun gerçek konumu, gerçek harita ve her yönde gerçek zemin sınırı biliniyor. Motor komutları bilerek kusurlu (gerçek hareket %15'e kadar sapıyor).
- **Gerçek kamera benzetimi** (`kayitlar/theta/`): aynı kayıtlar çift balık gözüne çevrilmiş, gövde maskesi, bulanıklık, gürültü ve sıkıştırma eklenmiş.
- **Ölçüm araçları**: bir yöntemin çıktısını gerçekle karşılaştırıp hatayı derece ve santimetre olarak verir.
- **Robotta ham kayıt**: robot açılınca gerçek kareleri ve komutları aynı biçimde kaydedecek kod (robotta denenmedi).

## 7. Hangisi model, hangisi klasik

| İş | Yöntem | Model var mı |
|---|---|---|
| Piksel → yön | Balık gözü geometrisi | Hayır |
| Dönüş açısı, ilerleme yönü | Nokta eşleştirme + düzlemsel hareket | Hayır |
| Zemin / zemin değil | SegFormer-B0 | Evet (hazır, eğitilmedi) |
| Zemin sınırı → mesafe | h / tan(α) | Hayır |
| Harita | Izgarada kanıt biriktirme | Hayır |

## 8. Sonuçlar

**Dönüş açısı:** hedef tuttu (Bölüm 3).

**Harita** (robotun gerçek konumu verilerek, ev turu, gerçek kamera benzetimi):
- Zemin sınırı kusursuz verilirse: boş alanın %91'i doğru (IoU 0,91), duvar konumu 1 cm içinde. Geometri ve harita kodu çalışıyor.
- Hazır modelle: boş alan IoU 0,83, bulunan duvarlar gerçeğine ortalama 8 cm uzakta. Evin planı (koridor, odalar, kapı boşlukları) haritada tanınıyor.
- Zayıf yan: gerçekte dolu olan karelerin ancak yarısı (%52) "dolu" olarak işaretlenebildi. İnce ayaklar ve dar aralıklar kaçıyor.
- Bu son sayının bir kısmı ölçütün kendisinden geliyor: kusursuz zemin sınırıyla bile %70'te kalıyor. Sebebi henüz ayrıştırılmadı.

**Hız** (bu bilgisayar, RTX 3050 Ti): dönüş açısı kare başına 73 ms; zemin modeli 300 ms (8 görüntü), ekran belleği 1,8 GB. Jetson'da ölçülmedi.

## 9. Kanıtlanmayanlar

- **Gerçek kamera.** Hiçbir yöntem robottan alınmış ham görüntüde denenmedi.
- **Model gerçek ortamda.** Sürüş videosundan alınan 16 karede göz kararı denendi: iyi pozlanmış karelerde (karanlık lobi, parlak karo) zemin düzgün bulundu; robotun ışığıyla bembeyaz patlamış zeminde bulunamadı. O kareler uygulamanın ekran kaydıydı (menüler üstünde, düşük çözünürlük), yani adil bir sınama değil. Sonuç: belirsiz.
- **İlerlenen mesafe.** Harita denemesinde robotun konumu hazır verildi. Gerçekte konumu da kameradan bulmak gerekecek; dönüş ve ilerleme yönü var, mesafe yok.
- **Jetson.** Zemin modeli ve dönüş yöntemi Jetson Nano'da hiç çalıştırılmadı.
- **Modelin lisansı.** SegFormer ağırlıkları NVIDIA'nın araştırma amaçlı lisansıyla dağıtılıyor olabilir; model kartından doğrulanmadı.
- **Robotun sarsılması.** Dönüş yöntemi kameranın dik durduğunu varsayıyor.
