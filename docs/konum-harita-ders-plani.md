# Harita ve konum: ders planı

Amaç: iki temel konuyu (haritalama, konum bulma) baştan öğrenip proje için en uygun yöntemi bilerek seçmek.
Her ders sohbette işlenir: kısa anlatım (görselli) → 2-3 soru → küçük araştırma ödevi.
Sıra: dersler → ikimiz ayrı ayrı araştırma → aday seçimi → uyarlama → test.

Adı geçen yöntemler aday listesidir; araştırma aşamasında kaynağından doğrulanacak.

## Ön bilgi (1 ders)

| Ders | Konu | Bizdeki karşılığı |
|---|---|---|
| Ö1 | Kamera bir açıölçerdir: piksel → yön, 360° görüntü, kalibrasyon | Eğim öz-kalibrasyonu (2,3°), gövde maskesi |

## A. Haritalama

| Ders | Konu | Cevaplanacak soru |
|---|---|---|
| H1 | Harita nedir, türleri: ızgara, nokta bulutu, oda-kapı grafı, yoğun 3B, kat planı, anlamsal | Oyun haritadan tam olarak ne istiyor? |
| H2 | Tek kamerayla mesafe: bilinen geometri (zemin, tavan çizgisi), hareketle üçgenleme, öğrenilmiş derinlik, aktif sensör | Hangisi ne kadar uzağı, ne kadar doğru ölçer? |
| H3 | Ölçümü haritaya işlemek: doluluk ızgarası, belirsizlik, bilinmeyen alan, hareketli nesne | Tek kare yanlışsa harita nasıl doğru kalır? |
| H4 | Güncel yöntemler: klasik 2B (Hector, Cartographer), görsel SLAM haritaları (ORB-SLAM3, Stella), öğrenilmiş yoğun (DROID-SLAM, MASt3R), oda yerleşimi (HorizonNet), panorama derinlik, Gaussian splatting | Jetson Nano / telefon / PC'de hangisi çalışır? |
| H5 | Karar: gereksinim × aday tablosu, 2-3 aday, deney planı | Hangisini deniyoruz, neyle ölçüyoruz? |

Araştırma arası: ikimiz ayrı kaynak tararız, bulduklarımızı karşılaştırırız.

## B. Konum bulma

| Ders | Konu | Cevaplanacak soru |
|---|---|---|
| K1 | Poz nedir; göreli (adım sayma) ve mutlak (haritaya göre) konum; sürüklenme; kaçırılmış robot | Hata neden birikir, ne durdurur? |
| K2 | Adım sayma kaynakları: motor komutu, görsel odometri, IMU; tek kamerada ölçek sorunu | Bizim yön ve ilerleme ölçümümüz bunların neresinde? |
| K3 | Haritaya göre konum: yer tanıma, hafıza kareleri, nokta eşleme, tarama eşleme, parçacık filtresi | Hafıza kareleri sürerken nasıl kullanılır? |
| K4 | Birleştirme: Kalman / parçacık filtresi, poz grafı, döngü kapama | Aynı yere dönünce harita ve konum nasıl düzelir? |
| K5 | Karar: aday seçimi, deney planı | |

Araştırma arası: aynı şekilde.

## C. Birleşim

| Ders | Konu |
|---|---|
| B1 | SLAM: harita ve konumu birlikte çözmek; ilk keşif ile sonraki oyun arasındaki fark |
| B2 | Oyuna bağlama: sanal nesnenin kayması, eşya arkasına gizlenme, işlem bütçesi (Jetson, telefon) |
| B3 | Canlı deneme: donanıma uygun hazır bir SLAM'i kurmak. Önce bantlı ham kayıtta (2,00 m yer gerçeği), sonra robot açıkken anlık |

## Kullanıcının getirdiği konular (yeri gelince işlenecek)

- ROS kullanmalı mıyız, haritalamada ne kazandırır → H4
- Ölçek sorunu için yapay zekâ (Depth Anything gibi derinlik modelleri) → H2, H4
- LSD-SLAM (TUM; cvg.cit.tum.de/research/vslam/lsdslam, github.com/tum-vision/lsd_slam) → H4, B1
- IMU: Arduino boş pini ya da Jetson, ne kadar iyileştirir, takılamazsa ne olur → K2
- Bitirme çerçevesi: yalnız kamera ile elde edilen sonuç. IMU takılabilirse kameralı + IMU'lu karşılaştırma eklenir → K5, B3

## Durum

- [ ] Ö1  - [ ] H1  - [ ] H2  - [ ] H3  - [ ] H4  - [ ] H5
- [ ] K1  - [ ] K2  - [ ] K3  - [ ] K4  - [ ] K5
- [ ] B1  - [ ] B2  - [ ] B3
