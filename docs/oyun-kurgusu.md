# Oyun kurgusu — taslak v2 (isim henüz seçilmedi)

Ev gerçek, robot gerçek, her şey AR ama **hologram değil: katı, gölgeli, oyuncak/maket gibi**. Tek tema: ev. Diğer temalar kaldırılır.

## 1. Tek cümlede
Küçük robotunla evi gez, saklanan **Evcikleri** yakala, **Gölgelerle** başa çık; topladıklarını üssüne taşı, sat/işle, üssünü ve robotunu geliştir, evi Gölgelerden koru.

## 2. İsim adayları
- **TENHA** — "ıssız, sessiz yer". Kısa, Türkçe, gizemli; sessizlik oyunun ana mekaniği.
- **YADİGÂR** — hatıra eşya. Topladıkların evin hatıraları; duygusal ve özgün.
- **HOLLOWLIGHT** — "boş evin içindeki ışık"; üssün lamba teması.
- **EMBERKEEP** — "közü canlı tut"; üs savunması vurgusu.
- **LUMEN WARDEN** — küçük ışık bekçisi.
Tavsiye: **TENHA** (veya uluslararası marka istenirse HOLLOWLIGHT). Mağaza çakışması kontrol edilmedi.

## 3. Dünya
Ev bir süredir sessiz. İki grup var:
- **Evcikler** (yakalanacaklar): evin sıcaklığını taşıyan minik ruhlar. Saklanıyorlar. Yakalanınca **Öz** (para) ve hikaye parçası verirler.
- **Gölgeler** (düşmanlar): sessizliğin soğuğu. Işığa, gürültüye ve Üs lambasının sıcaklığına gelirler. Bazıları yakalanıp **Kor** (savunma kaynağı) bırakır.
- **Minik** (robot) ve **Üs: Ocak** (büyük, katı bir lamba kulesi + etrafında bina yerleri).

## 4. Üs: Ocak
Üs her yerden net görünür: katı model, sıcak ışık sütunu, uzaktayken ekran kenarında yön oku, haritada büyük simge.
Ocak'ın çevresinde **6 bina yeri** vardır (öz ile açılır/yükseltilir):

| Bina | İşlev |
|------|-------|
| **Ocak Lambası** (ana) | Isıyı verir; seviyesi bölge güvenli yarıçapını, şarj hızını büyütür |
| **Pazar** | Yakalanan Evcikleri öz karşılığı satar; nadirler pahalı |
| **Atölye** | Robotu geliştirir (motor, pil, sensör, sonar, LED, hızlı yakalama) |
| **Fener Direği** | Gölgeleri yavaşlatır / erimesini sağlar; Ocak çevresini korur |
| **Yem İstasyonu** | Yem üretir; sahaya bırakılınca Evcikleri çeker |
| **Gözlem Kulesi** | Sensör menzili, Gölge uyarısı (yönü ve mesafesi) |
| **Kafes / Bahçe** | Seçtiğin Evcikleri tutar; zamanla pasif öz getirir |

Hepsi AR'da gerçek bir maket gibi görünür; evin zemini üzerinde, robotun tabanında durur.

## 5. Yakalananlar (Evcikler) ve taktikler
Her tür farklı bir taktik ister:

| Evcik | Taktik |
|-------|--------|
| **Toz Topu** | Yavaş yaklaş (SHIFT): önce tedirgin olur, gürültü sürerse kaçar |
| **Işık Kurdu** | **Fener tut**: LED'i yak, çağır; ama pil harcar |
| **Gizli Cep** | **Sonar** at, saklandığı yeri gör |
| **Unutulmuş Tohum** | **Sabır**: yanında bekle |
| **Ürkek Yavru** | **Köşeye sıkıştır**: kaçış yönünü duvarla kapatacak şekilde yaklaş |
| **Yemlik** | **Yem bırak**: Yem İstasyonu'ndan yem alıp bir yere bırak, geri çekil, gelmesini bekle |
| **Evin Kalbi** (efsane) | Işık + tam sessizlik |

Nadirlik: yaygın %82, nadir %15, çok nadir %3. Nadirler hâleli ve sesli, kısa süre sonra kaybolur.

## 6. Düşmanlar (Gölgeler) ve taktikler
Gölgeler toplanmaz; **kaçılır, savuşturulur, avlanır**.

| Gölge | Davranış | Karşı taktik |
|-------|----------|--------------|
| **Gezgin** | Gürültü + ışıkla gelir, yavaş | **Fener tutarak durdur**: LED'i yöneltip 2 sn tut, donar; ya da kaç, üsse koş |
| **Fısıltı** | Hızlı, kısa menzilli, ışığı umursamaz, gürültüye hassas | **Don ve sessiz kal**: robotu durdur, LED'i kapat; geçip gider |
| **Bekçi** | Bir kapıyı/koridoru kapatır | **Sonar** ile uyutulur, ya da Fener Direği tuzağına çekilir |
| **Soğuk Dalga** (olay) | Belirli aralıkla Ocak'a yürür | Fener Direkleri + müdahale; Ocak Isı'sı düşerse şarj kapanır |

Ortak taktikler:
- **Fener tutma:** LED hem Evcik çeker hem Gölge dondurur; ışığı kullanma kararı.
- **Yem tuzağı:** Gölgeyi Fener Direği'ne çek, erisin; **Kor** düşer.
- **Hareketsizlik:** durup LED'i kapatınca fark edilme menzili düşer.
- **Kapıdan kaçış:** dar kapıda robot geçer, Gölge yavaşlar.
- **Üs sığınağı:** Ocak çevresi güvenli.

## 7. Evi koruma
Ocak'ın **Isı**'sı vardır. Gölgeler Ocak'a ulaşırsa Isı düşer; sıfırlanırsa şarj ve Pazar kapanır, ev "soğur" (kısa süre, kayıp küçük). Bu yüzden:
- Bina yatırımı (Fener Direği, Kule) anlamlı olur.
- Uzun gezide Ocak savunmasız kalır; döngüde risk/ödül.
- "Sakin mod" (isteğe bağlı): savunma olaylarını kapatır, sadece keşif.

## 8. Oynanış döngüsü
Çık → Bul/Yakala → Taşı (risk) → Üs: **sat** (öz) ya da **Defter'e işle** (hikaye) ya da **Kafes'te tut** → Binaları/robotu geliştir → Soğuk Dalga'ya hazırlan → tekrar çık.
Teslim edilen her Evcik ya hikayeyi ilerletir ya satılır; ikisi aynı anda olmaz, bu da küçük bir karar yaratır.

## 9. Hikaye yapısı (kısa)
7 bölüm. Her bölüm bir taktik öğretir (bekleme, sessizlik, fener, ilk Gölge ve kaçış, sonar, yem, ev çapında Soğuk Dalga), sonuncusu Evin Kalbi. Final: Gölgeler yok edilmez, sakinleşir. Sonra serbest gezi + dalgalar sürer.

## 10. Pokémon GO'dan farkı
Gerçek ev, robot gözü, taktik tabanlı yakalama (dokunma yok), düşman + savunma + üs inşası, ilerlemenin evin kendisinde görünmesi.

## 11. Sadeleştirme ve yapım sırası
**Faz A (temizlik, 1 tur):** diğer temalar silinir, ad değişir, ürkek yaratık dengesi (uyarı aşaması, 5 kaçış), Üs katı ve belirgin (yön oku, ışık sütunu), HUD'a "üs yönü".
**Faz B (ekonomi):** Pazar, Atölye (mevcut laboratuvar), satma/işleme ayrımı, Kafes.
**Faz C (düşman):** Fener ile dondurma, Fısıltı, Bekçi, Yem tuzağı.
**Faz D (üs ve savunma):** bina yerleri, Fener Direği, Gözlem Kulesi, Soğuk Dalga olayı, Ocak Isı sistemi.
**Faz E (hikaye ve cila):** 7 bölüm metinleri, defter ekranı, ses.
Gerçek robot tarafı (konum hatası sim, ArUco düzeltmesi) bunlarla paralel.

## 12. Açık sorular
- İsim: TENHA mı yoksa yabancı bir isim mi?
- Soğuk Dalga (üs savunması) zorunlu mu, Sakin mod seçilebilir mi?
- Fener ile Gölge dondurma: öldürmek (eritmek) de olsun mu?
