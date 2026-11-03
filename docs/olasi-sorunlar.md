# Robot: bağlantı kopunca ne olur, başka hangi sorunlar çıkabilir

3 Kasım 2026'da yazıldı. Durumlar: ✅ korunuyor · 🟡 kısmen · ❌ koruma yok.

## 1. Bağlantı kopunca neler duruyor

"Bağlantı koptu" birkaç şekilde olur: Wi-Fi zayıflar, uygulama kapanır ya da arka plana atılır, telefonun ekranı kapanır, köprü (Jetson'daki program) çöker, USB kablosu gevşer. Her durumda en az bir katman robotu durdurur.

| Ne | Kim durduruyor | Ne kadar sonra | Durum |
|---|---|---|---|
| Motorlar | Unity: 0,9 sn cevap gelmezse sıfır gönderir | 0,9 sn | ✅ |
| | Köprü: geç gelen (bayat) hareket komutunu uygulamaz | 250 ms gecikme | ✅ |
| | Arduino: 300 ms yeni komut gelmezse durur (Jetson çökse bile) | 0,3 sn | ✅ |
| Motor sesi, şarkı, korna, geri bip | Arduino: 1,5 sn nabız gelmezse susar (yeni) | 1,5 sn | ✅ |
| Korna (Unity'de basılı kalırsa) | Unity: bağlantı zayıfken kornayı bırakır (yeni) | hemen | ✅ |
| Vites ve hız göstergesi | Unity: kopukken hız biriktirmez (yeni). Eskiden tuş basılıyken vites büyümeye devam ediyordu; bağlantı gelince robot o hızdan kalkıyordu | hemen | ✅ |
| Selektör | Kendisi 4 yanıp sönmede biter | 0,5 sn | ✅ |
| Işık, dörtlü | **Bilerek açık kalır.** Duran araçta dörtlünün yanması iyi; ışık da karanlıkta robotu bulmaya yarar | — | — |
| Kamera | İzleyen kalmayınca köprü kamerayı bir süre sonra kapatır | birkaç sn | ✅ |
| Unity'de video kaydı | Devam eder; kopma anı kayda girer (sonradan bakmak için iyi) | — | — |

**Bağlantı geri gelince:** Unity robotun bildirdiği durumu (ışık, dörtlü, motor sesi) kendi düğmelerine yansıtır. Yani motor sesi kopunca sustuysa, menüdeki düğme de "kapalı" görünür ve tek dokunuşla tekrar açılır. Eskiden düğme açık görünüyor ama ses gelmiyordu; ikinci basışta da köprü bunu değişiklik saymıyordu (düzeltildi).

**İsteğe bağlı fikir:** bağlantı 1,5 sn kopunca dörtlü kendiliğinden yansın, bağlantı gelince eski hâline dönsün (arızalı araç gibi). İstersen eklenir.

## 2. W/S'ye art arda hızlı basmak motorlara zarar verir mi

Dönmekte olan motora birden ters gerilim vermek, ondan çok akım çeker ve dişlilere darbe yapar. Bu yüzden üç katman var:

1. **Unity, vitesli modda:** ters yöne basınca araç önce fren yapar (tam hızdan ~0,4 sn), sonra geri vitese geçer. Hızlı basmak burada zaten yumuşar.
2. **Arduino (her modda, sabit güçte de):** yön değişirken önce sıfıra iner (tam hızdan ~140 ms). **Yeni:** sıfırda 80 ms frenli bekler, teker dursun diye. Sonra ters yönde yavaş hızlanır (~320 ms'de tam güç).
3. **Motor sürücüsü (VNH5019):** aşırı akım ve ısınmada kendini korur ve kapanır. Arduino bunu "arıza" olarak bildirir.

Sonuç: hızlı hızlı ileri-geri yapmak robotu titretir ama motoru ya da sürücüyü yakmaz. Dişlileri biraz daha çok yorar.

## 3. Olası sorunlar

### Ağ (en büyük risk)
- 🟡 **Robotun Wi-Fi'ı çok zayıf. Kopmaların asıl sebebi bu.** Aynı odada, aynı erişim noktasını bilgisayar %92 görüyor, robot %35 (−83 dBm). Bu kadar fark, antenle ilgili bir sorun demek. Intel 8265 kartın iki anten kablosu var; biri ya da ikisi gevşekse, anten takılı değilse ya da anten metal kasanın içinde kalıyorsa böyle olur. Montajdan sonra kötüleştiyse büyük ihtimalle kablo yerinden çıktı. **Bakılacak:** iki küçük altın konnektör karta oturmuş mu, antenler kasanın dışında mı?
- 🟡 **Okul ağı:**
  - Erişim noktası dolu olabilir. Açılışta "denied association (code 17)" görüldü, yani AP dolu olduğu için robotu bir süre almadı.
  - 5 GHz'de kanal 60 kullanılıyor. Bu kanalda radar algılanınca AP kanal değiştirmek zorunda kalır ve herkes ~1 dk düşer (DFS).
  - Ağ açık ve giriş sayfalı. Robot "internet yok" görüyor; şimdilik sorun değil.
  - Bazı okul ağları cihazların birbirini görmesini engeller. Şu an çalışıyor, ama ağ ayarı değişirse aniden çalışmayabilir.
- 🟡 **Robot 45 sn ağ bulamazsa kendi kurulum ağını açar.** O sırada uygulama robotu bulamaz. Telefon Robot-A78C ağına bağlanıp panelden ağ seçilir.
- ❌ **Android, internetsiz ağdan kaçabilir:** robotun kendi ağına bağlanınca Samsung "internet yok" deyip mobil veriye geçebilir (Uyarlanabilir Wi-Fi / Mobil veriye geç). O zaman paketler robota gitmez. Çözüm: telefonda bu ayarı kapatmak ya da uygulamanın robota Wi-Fi üzerinden gitmesini zorlamak (Android'de yapılabilir, henüz yok).
- 🟡 **IP değişir:** her ağda robotun adresi farklı olur. Uygulama kendisi buluyor (önce son adres, sonra jetson.local, sonra yayın, sonra tarama).

### Güç
- ❌ **Pil voltajı ölçülmüyor** (yapılacaklar B1). 4S LiPo 15 V altına inerse zarar görür; şu an yalnızca regülatör ekranından bakılıyor.
- 🟡 **5 V regülatör:** motor kalkışında voltaj düşerse Jetson kendini kapatabilir. Arduino Jetson'un USB'sinden beslendiği için o da yeniden başlar ve motorlar durur, yani güvenli. Ama sürüş kesilir.
- 🟡 **Düğmeden kapatmak:** Jetson'un SD kartı nadiren bozulabilir. Mümkünse paneldeki "güvenli kapat" kullanılmalı. Bugün düğmeden kapatıldı, açılışta sorun görülmedi.

### Motor ve mekanik
- 🟡 **Teker sıkışırsa** motor uzun süre tam akım çeker ve ısınır. Akım ölçülüyor ama sınır yok (yapılacaklar B4). Şimdilik VNH5019'un kendi koruması var.
- ❌ **Sürücü arızası** Unity'de gösterilmiyor. Arduino bildiriyor, uygulama göstermiyor. Kolay eklenir.

### Yazılım
- ✅ **Köprü çökerse** systemd 2 sn'de yeniden başlatır. Arduino seri port açılınca yeniden başlar: motorlar durur, ışık söner. Unity bunu görüp düğmelerini eşitler.
- ✅ **USB kablosu gevşerse** köprü 2 sn'de bir tekrar bağlanmayı dener; Arduino 300 ms'de durmuş olur.
- 🟡 **Tarayıcı ve Unity aynı anda sürerse** ilk hareket eden 0,6 sn'lik kilitle kazanır. Işık ve sesleri ise ikisi de değiştirebilir.
- 🟡 **Telefonda uygulama arka plana atılınca** ya da ekran kapanınca paket gitmez, robot durur (güvenli). Geri gelince yeniden bağlanır.
- 🟡 **Theta S** bir süre sonra kendi kapanabilir ya da USB canlı modda değilse görüntü gelmez (yapılacaklar A5). Bugün `/dev/video0` göründü.
- 🟡 **Isınma:** Jetson Nano 5 W modunda, H.264 kodlama donanımda. Uzun sürüşte sıcaklık izlenmeli.
