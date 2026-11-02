# Jetson tarafı

Bağlantı: bilgisayarda `ssh jetson` (kullanıcı `bs`, `jetson.local`, port 443; anahtar `~/.ssh/id_ed25519`).

## Tek seferlik kurulum

```bash
scp -r robot/jetson/kurulum.sh robot/jetson/robot_paneli jetson:~/kurulum/
ssh -t jetson 'sudo bash ~/kurulum/kurulum.sh'
```

Kurar: `python3-serial`, `v4l-utils`, `x11vnc`; `bs` → `dialout`; robot paneli (`robot-paneli`); monitörsüz 1920x1080 masaüstü; VNC (`uzak-masaustu`, yalnızca localhost). Gerekirse sonunda yeniden başlatır.
`arduino-cli` (1.5.1, `arduino:avr`) `~/.local/bin` içinde, sudo gerekmez.

## Robot paneli (`robot_paneli/`)

Tarayıcıda `http://jetson.local` ya da robotun IP adresi. İki işi var: Wi-Fi kurulumu ve güç.

- Açılışta NetworkManager kayıtlı ağları dener. 45 sn bağlantı yoksa robot kendi ağını açar: `Robot-XXXX`. XXXX, Wi-Fi kartının MAC adresinin son 4 hanesi; şifre `/etc/robot/kurulum_agi.json`.
- O ağa giren telefonda panel kendiliğinden açılır (açılmazsa `http://10.42.0.1`). Ağ seçilip şifre yazılır; robot geçer ve o ağı en öncelikli yapar. Olmazsa kurulum ağını hata mesajıyla geri açar.
- Kurulum ağında 5 dk kimse yoksa kayıtlı ağlar yeniden denenir.
- Robot bir ağdayken "kurulum moduna al" ile başka ağa geçirilir.
- Giriş sayfası isteyen ağlarda (Yıldız EDUROAM, FortiGate) panelde "Ağ girişi" çıkar. Bilgiler `/etc/robot/ag_girisleri.json` içinde (yalnızca root); robot oturum düşünce kendisi yeniden girer.
- Bağlıyken panel internet durumunu anlık gösterir: var / giriş bekleniyor / yok, ne kadardır böyle olduğu, son 10 dakikanın renkli şeridi ve "geldi / gitti" listesi (ne kadar kesik kaldığıyla). İnternet varken 10 sn'de, yokken 3 sn'de bir kontrol edilir.
- **Kapatma:** pilin şalterini kapatmadan önce panelden **Kapat**. Sayfa, robot cevap vermeyi bırakınca 15 sn daha bekleyip "Robot kapandı" der; güç ondan sonra kesilir.
- Günlük: `ssh jetson 'journalctl -u robot-paneli -n 50'`

## Sürüş köprüsü (`surus_koprusu/`)

Tarayıcıda `http://<robot>:8080`: canlı 360° görüntü, vitesli sürüş, kokpit, yan aynalar, ışık/selektör/dörtlü, korna, motor sesi, melodiler. Kısayollar sayfada `?` düğmesinde. Nasıl çalıştığı ve sayıların gerekçesi: `docs/robot-teknik-notlar.md`; sıradaki işler: `docs/robot-yapilacaklar.md`.

- `surus_koprusu.py`: Arduino (seri), kamera (GStreamer: JPEG + donanım H.264), web sunucusu.
- `websoket.py`: küçük WebSocket sunucusu. `/kontrol` sürüş komutları ve durum, `/goruntu-h264` görüntü.
- `surus.html`: sayfa. 360° düzeltme gölgelendiricide; lens ölçüleri `tools/lens_ayarla.py` ile kameranın karelerinden hesaplandı.
- Kamera takılış açısı sayfadaki ayarlardan düzeltilir, `~/.config/robot/kamera_ayari.json` içine kaydedilir.

Kullanıcı servisi, sudo gerekmez (bs otomatik giriş yaptığı için açılışta başlar):

```bash
scp -r robot/jetson/surus_koprusu jetson:~/robot/
ssh jetson 'mkdir -p ~/.config/systemd/user && cp ~/robot/surus_koprusu/surus-koprusu.service ~/.config/systemd/user/ && systemctl --user daemon-reload && systemctl --user enable --now surus-koprusu'
```

Günlük: `ssh jetson 'journalctl --user -u surus-koprusu -n 50'`

Arduino tarafı: `robot/arduino/robot_kontrol` (seri 115200). Jetson üzerinden yükleme:
`ssh jetson '~/.local/bin/arduino-cli compile --upload -p /dev/ttyACM0 --fqbn arduino:avr:uno ~/robot/arduino/robot_kontrol'`
(Yüklerken köprü servisi seri portu tutuyorsa önce `systemctl --user stop surus-koprusu`.)

## Jetson ekranı

Masaüstündeki **Jetson Ekranı** kısayolu (`tools/jetson_ekrani.ps1`): SSH tüneli açar, TigerVNC ile bağlanır.

## Robotu bulma

Okul ağında `jetson.local` çalışmaz. `tools/robotu_bul.ps1` önce bu adı dener, olmazsa alt ağı tarayıp robotu panelinden tanır ve adresi `~/.ssh/config` içindeki `Host jetson` kaydına yazar (`HostKeyAlias` sayesinde anahtar doğrulaması bozulmaz). Masaüstündeki **Robot Paneli** kısayolu bunu `-Panel` ile çalıştırıp paneli açar; Jetson Ekranı da önce bunu çalıştırır.
