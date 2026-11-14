# Proje durumu (kısa)

Son güncelleme: 5 Kasım 2026. Bu belge hızlı bakış içindir. Ayrıntı için: `CLAUDE.md` (kararlar), `docs/robot-nasil-calisir.md`, `docs/robot-teknik-notlar.md`, `docs/olasi-sorunlar.md`, `docs/robot-yapilacaklar.md`, `docs/oyun-kurgusu.md`, `docs/literatur-notlari.md`.

Etiketler: **kesin** = karar verildi, **taslak** = büyük ihtimalle değişir, **açık** = henüz seçilmedi.

## 1. Proje

Telefondan sürülen, 360° kameralı fiziksel bir robotla ev içinde oynanan AR keşif oyunu. Mario Kart Live: Home Circuit'ten ilham alındı, ama oyun yarış değil, Pokémon GO tarzı keşif ve yakalama. Robotun kamerası izlenir, görüntünün üstüne AR nesneleri (bulgu, gölge, üs) biner. Gerçek pil oyunda yakıttır.

Ekip: İsa Koçan (22011056), Bilal Efe Uysal (22011031). Danışman: Dr. Öğr. Üyesi Furkan Çakmak. YTÜ Bilgisayar Mühendisliği, bitirme projesi (BLM9000 çerçevesinde), Ar-Ge türü.

### Yön ve aşamalar (5 Kasım 2026, kullanıcının anlatımı)

Robot yarış için biraz yavaş olduğundan yarış yerine Pokémon GO gibi keşif ve toplama seçildi. Pokémon GO nesneleri GPS ile koyar, bizde GPS yok. Bu yüzden haritanın hangi noktasında ne olduğunu ve robotun harita içindeki yerini bilmek gerekiyor. Ek sensör olmadığı için konum ilk etapta yalnızca robot kamerasıyla bulunacak (bu net bir kural değil, bu aşamadaki durum). Yöntem yapay zeka ve görüntü işleme. AR katmanı hepsini birleştirir. Robotun fiziksel kısmı bitti, uygulamadan bağlanıp sürme çalışıyor. Wi-Fi sinyali iyileştirilebilir (anten takılınca düzelmişti). Sonraki aşamalar: oda haritalama, konum bulma, görüntü işleme / yapay zeka, AR, en sonda bölüm (level) tasarımıyla oyunu bitirme. Rapor yazım kuralı: her ayrıntıyı ve her yaşanan olayı yazma, zamanla değişebilecek şeyleri ve zayıf yanları öne çıkarma.

## 2. Ar-Ge çekirdeği: konum (kesin yaklaşım, yöntem deneylerle seçilecek)

- Robotta konum için ek sensör kullanılmıyor, konum yalnızca 360° kamera görüntüsünden bulunacak. Robotta LiDAR, IMU ve mesafe sensörü şimdilik yok ve büyük ihtimalle olmayacak. Raporlarda bu sensörlerden söz edilmiyor (kesin diye yazılmıyor).
- Oda haritası bir kez çıkarılır, depoda saklanır, oda kalıcı değişirse yeniden taranır.
- Tarama hibrit: iPhone LiDAR (ARKit), LiDAR'sız telefonda çift kamera, robotun 360° kamerası ek veri. En iyi kombinasyon deneylerle seçilecek.
- Oyun sırasında robotun 360° görüntüsü harita içinde eşleştirilir (PICCOLO / CPO benzeri panorama–nokta bulutu). Telefonda yeterince hızlı çalışmazsa işlem Jetson'a taşınır.
- Oyun mekaniği harita açılmasına değil, harita üzerine yerleştirilen AR nesnelerine dayanır. Şu an oyundaki harita (`ExplorationMap`) simülasyondur, gerçek tarama yok.

## 3. Donanım (kesin)

Jetson Nano (JetPack 4.6, Docker, ROS 2 Humble kurulu, sürüşte kullanılmıyor), Arduino Uno + Pololu Dual VNH5019, Ricoh Theta S (USB), 4 tekerlekli kızaklı sumo şasi (HN-GH12-2217Y motorlar, 12 V, 50:1), 4S LiPo 14,8 V 5200 mAh (15 V altına inilmez), röle → LED, buzzer. Encoder yok, tekerlek çapı ölçülmedi. Batarya gerilimi ölçümü planlı, henüz yok. Pin ve komut ayrıntısı `CLAUDE.md` ve `docs/robot-teknik-notlar.md` içinde.

## 4. Yazılım mimarisi (kesin)

- Video ROS'tan geçmez. Jetson'da GStreamer, donanımsal H.264 (NVENC) ya da JPEG, doğrudan telefona.
- Sürüş: Unity → UDP (8081) → Jetson sürüş köprüsü → seri 115200 → Arduino. Kopma güvenliği üç katman: Unity 0,9 sn, köprü 250 ms bayat komut reddi, Arduino 300 ms.
- Unity (`unity/`, 6000.3.26f1, URP, Input System): oyun, kokpit, 360° görüntü (equirect → küre), AR nesneleri, arayüz, ses sentezi, foto/video. Robot `IRobotLink` ile soyutlanır, `MockRobotLink` ve robotsuz demo var.
- Tarayıcı sürüş sayfası yedek, geliştirilmiyor. Robot paneli (Wi-Fi, güç) uygulamada, menü › ROBOT altında; robotta yalnızca JSON API kalıyor.
- Hedef platform Android ve iOS. Geliştirme önce Android (S21+). iOS derlemesi Mac'te. Oda taraması için iPhone 14 Pro.

## 5. Ölçümler (5 Kasım 2026'ya kadar)

| Ölçüm | Değer |
|---|---|
| Görüntü JPEG / H.264 | 20–50 Mbit/s / ~4 Mbit/s (15 kare/sn) |
| Komut gidip gelme (ping) | 3 ms (H.264 modunda, panelden) |
| Robot Wi-Fi sinyali | −70…−83 dBm (5 GHz, okul ağı), aynı yerde bilgisayar %92, robot %35 |
| Jetson | işlemci %10–35, RAM 1,2/4 GB, işlemci 42 °C, Wi-Fi kartı 67 °C, 5 W modu |
| Unity JPEG çözme | donanım çözücüde işlemci %52 → %9 |
| Motor rampası | kalkış ~320 ms, duruş ~140 ms |
| Uçtan uca görüntü gecikmesi | ölçülmedi (`tools/gecikme_olc.py` hazır) |
| Henüz ölçülmeyenler | tekerlek çapı, gerçek hız, pil gerilimi, Wi-Fi menzili |

## 6. Oyun (taslak)

Tek tema: Ev. Beş bulgu türü, her biri ayrı fiil ister: ürkek (SHIFT ile sessiz yaklaş), ışığa gelen (LED), gizli (sonar), hareketsiz (bekle), efsane (ışık + sessizlik). Aynı anda en çok 3 bulgu. Gölge toplanmaz, kaçılır, LED ile donar. Yakalananlar robotun hafızasında taşınır, üste teslim edilir, pil biterse kaybolur. Öz ile 6 yükseltme alınır (gerçek davranışa yansır, ör. PWM limiti), 6 bölümlük hikaye ilerler. Adı geçici: BATMOBİL. Kodda 6 bölüm var, `oyun-kurgusu.md` taslağı 7 bölüm ve Faz B–E (Pazar, Atölye, yeni gölgeler, bina yerleri, Soğuk Dalga) önerir, uygulanmadı.

## 7. Ara rapor 1

- Taslak: `Proje_Şablon_2026version/AraRapor.tex` (Overleaf'e yüklenir, kaynakça `kaynaklar.bib`, logo `assets/logo.png`).
- Kapsam (Ar-Ge): Bölüm 1 (Amaç/Kapsam, Literatür) ve Bölüm 2 (Sistem Analizi, Fizibilite: yazılım, donanım, zaman). Bölüm 3–5 sonraki raporlarda. Özet/Abstract final raporda.
- Dil Türkçe. Bitince İngilizceye çevrilecek (`\DILBAYRAGI` 1 → 0, başlıklardaki Türkçe kısımlar silinecek).
- Yönerge: `Proje_Şablon_2026version/` içindeki HTML belgeleri (bölüm içerikleri, sık yapılan hatalar).

## 8. Yapılacaklar (öncelik sırasıyla)

1. Robota son yazılımı yükle ve dene, uçtan uca gecikmeyi ve Wi-Fi menzilini ölç (JPEG / H.264, 2,4 GHz, doğrudan `Robot-XXXX` bağlantısı).
2. Anten kablolarını kontrol et (sinyal çok zayıf).
3. Oda taraması deneyleri (iPhone LiDAR, çift kamera, robot kamerası) ve konum yöntemini (PICCOLO/CPO) geliştirme bilgisayarında dene.
4. Pil gerilimi ölçümü (gerilim bölücü → A2), akım sınırı, sürücü arızası göstergesi.
5. Unity'de H.264 kararlılığı, titreşim geri bildirimi, telefonda Build & Run.
6. Jetson Nano'da derinlik modeli (MiDaS / Depth Anything) hızını ölç (kaynakta değer yok).
7. Oyun Faz B–E ve hikaye metinleri.

## 9. Açık kararlar

Görsel tema/yön (Ev şimdilik), Wi-Fi modeli (ev Wi-Fi'ı mı, robotun kendi ağı mı, hotspot mu), hikaye/görev için LLM kullanımı, Jetson 5 W / 10 W, kamera (Theta S USB mi, HDMI yakalama mı), Soğuk Dalga zorunlu mu.

## 10. Başlıca riskler

Düşük gecikmeli video ve zayıf Wi-Fi (en büyük risk), konum yönteminin telefonda hızı, bakış açısı farkı (oda taraması insan boyunda, robot yerde), pil 15 V altı, Jetson ısınma/güç, Android'in internetsiz ağdan mobil veriye kaçması, SD kart bozulması (panelden güvenli kapat). Ayrıntı: `docs/olasi-sorunlar.md`.

## 11. Git notu

Dal `main`. Commit edilmemiş çalışma dosyaları var (Unity ayarları, malzemeler, `Proje_Şablon_2026version/`, `unity/.utmp/`). Satır sonları (CRLF/LF) için `.gitattributes` tutarlı hâle getirilmeli.
