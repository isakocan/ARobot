# Konum deney düzeneği (çevrimdışı)

Kamera tabanlı konum/harita yöntemlerini kayıtlı veri üstünde geliştirmek ve ölçmek için ortak altyapı. Yalnızca numpy + OpenCV (+ grafik için matplotlib, yoksa OpenCV yedeği).

## Dosyalar
| Dosya | İşi |
|---|---|
| `kayit.py` | Kayıt biçimi v1 okuyucu (`Kayit`) ve yazıcı (`KayitYazici`). Kare varyantı A (`kareler/*.jpg`) ve B (`kareler.mjpg`); komut araması (sıfırıncı derece tutma), gerçek konum, harita (`HaritaIzgara`), mesafe dosyaları. Kareleri henüz yazılmamış kayıt da açılır (resim = None). |
| `kamera.py` + `kamera.json` | Theta S geometrisinin tek kaynağı. Piksel <-> ışın, çift balık gözü <-> eşdikdörtgen (hazır remap tabloları, çakışmada gölgelendirici gibi karışım). Eksen sözleşmeleri dosyanın başında. Parametrelerin kaynak satırları `kamera.json` içinde. |
| `maske_uret.py`, `kamera_maske.json/.png`, `maske_onizleme.jpg` | Robot gövdesi maskesi (YAKLAŞIK, elle izlendi) ve gerçek kare üstünde önizleme. |
| `theta_benzet.py` | Sim kaydını gerçek Theta S gibi gösteren kayda çevirir (izdüşüm, gövde, bulanıklık, hareket bulanıklığı, gürültü, JPEG). |
| `olc.py` | `tahmin.csv` ile `gercek_konum.csv` karşılaştırır: yaw ve konum hatası, `sonuc.json`, `grafik.png`. |
| `calistir.py` | Bir yöntemi kayıt üstünde çalıştırır, süre ölçer, `olc`'u çağırır. |
| `yontem_gercek.py` | Hile: gerçek konumu verir (hatalar 0 olmalı). |
| `yontem_komut.py` | Yalnızca motor komutundan kestirim (dead reckoning). |
| `ornek_kayit_uret.py` | Unity'siz sentetik oda kaydı (test düzeneği). |
| `test_konum_deney.py`, `testleri_calistir.py` | Testler. |

## Kayıt biçimi (özet)
`bilgi.json`, `kareler/000000.jpg...` ya da `kareler.mjpg`, `kareler.csv` (`kare,t_ms[,ofset,bayt]`), `komutlar.csv` (`t_ms,sol,sag`), isteğe bağlı `gercek_konum.csv` (`kare,t_ms,x_m,y_m,yon_derece`), sim için `gercek_harita.png/.json`, `gercek_mesafe_*.csv`. x doğu, y kuzey, yön kuzeyden saat yönünde. Eşdikdörtgende orta sütun = robot ilerisi, sütunlar sağa artar. Ayrıntı: `kayit.py` başındaki açıklama.

## Bir yöntemi çalıştırmak
```
python tools/konum_deney/calistir.py --kayit kayitlar/sim/salon_turu --yontem yontem_komut
```
Çıktı `<kayit>/sonuc_<yontem>/` içine: `tahmin.csv`, `sonuc.json`, `grafik.png`; ekrana Türkçe özet ve kare başına süre (ort, p95). Ölçüt, tahminin ilk geçerli karesini gerçek başlangıç konumuna oturtur (yaw farkı kadar döndürüp kaydırır); yalnızca yaw veren yöntemlerde konum ölçütü hesaplanmaz.

## Yeni yöntem eklemek
`tools/konum_deney/` içine `yontem_<ad>.py` koy:
```python
class Yontem:
    def __init__(self, bilgi, kamera): ...        # bilgi = bilgi.json; kamera = kamera.Kamera
    def guncelle(self, kare_bgr, t_ms, komut):    # komut = (sol, sag) PWM
        return x_m, y_m, yon_derece               # bilinmeyen None/NaN olabilir
```
Kare kayıttaki gibi gelir (çift balık gözü ya da eşdikdörtgen). Eşdikdörtgene çevirmek için `kamera.balikgozu_to_esdikdortgen(kare, 1280, 640)`. Sonra `calistir.py --yontem yontem_<ad>`.

## Sim kaydını gerçek kameraya benzetmek
```
python tools/konum_deney/theta_benzet.py kayitlar/sim/salon_turu kayitlar/theta/salon_turu --hareket_bulaniklik
```
Bayraklar: `--govde_yok`, `--sigma`, `--gurultu_yok`, `--jpeg_kalite` (0 = ~250 KB hedefi), `--varyant B`. Seçilen değerler yeni `bilgi.json` içinde `theta_benzetim` altında. Keskinlik karşılaştırması: `--sigma_tahmin gercek_kare.jpg sim_panorama.png`.

## Bilinen sınırlar
- Gövde maskesi masada duran kameradan elle izlendi; belirsizlik yaklaşık +-15-25 piksel. Robotun gerçek montajıyla yeniden çizilmeli.
- Ön mercek ham karede soldaki daire (pilin göründüğü taraf); `yon=0` ile uyumlu, 12 Kasım 2026'da doğrulandı. Robotta saklı "Yön" ayarının sayısal değeri depoda yok.
- Keskinlik ve gürültü sayıları tek gerçek kare / 10 statik kareden çıkarıldı.
- Hareket bulanıklığı yalnızca dönmeden (yaw), öteleme bulanıklığı yok.

## Testler
`python tools/konum_deney/testleri_calistir.py` (pytest varsa `python -m pytest tools/konum_deney`). Sayısal sonuçlar çıktının sonunda.
