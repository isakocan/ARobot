"""Yalnızca motor komutlarından konum kestirimi (dead reckoning). Görüntüye bakmaz.
Kip: ileri hız v = v_tam * (sol+sag)/2/255, dönüş hızı (saat yönü) w = w_tam * (sol-sag)/510.
Varsayılanlar 0,40 m/s ve 60 derece/sn; bilgi.json'da "kayma_modeli" varsa (sim üreteci: komut_donusumu.pwm_255_dogrusal_hiz_m_sn
ve pwm_255_donus_derece_sn) oradan okunur. Sim üretecinin kayma modeli (yavaş yürüyen hız hatası, sabit sapma, motor gecikmesi)
burada YOK: yöntem bilerek saf nominal komut kestirimidir.
Başlangıç (0, 0, yaw 0); ölçüt hizalamayı kendisi yapar. Aralıktaki komut, iki ucundaki komutların ortalaması alınır
(komut kareler arasında değişirse bir kare gecikme hatasını yarıya indirir)."""
import numpy as np


def _nominal(bilgi):
    """(v_tam, w_tam): tam PWM'de (sol=sag=255) ileri hız m/s ve tam yerinde dönüşte (sol=255, sag=-255) derece/sn.
    Öncelik: kayma_modeli.komut_donusumu.pwm_255_* (sim üreteci); sonra kayma_modeli'ndeki nominal_*/V_*/W_* sayıları;
    hiçbiri yoksa 0,40 ve 60."""
    v, w = 0.40, 60.0
    km = (bilgi or {}).get("kayma_modeli")
    if not isinstance(km, dict):
        return v, w
    for k, d in km.items():
        kl = str(k).lower()
        if not isinstance(d, (int, float)) or "carpan" in kl:
            continue
        if kl.startswith("v") or "hiz" in kl or kl.startswith("ileri"):
            v = float(d)
        elif kl.startswith("w") or "donus" in kl or kl.startswith("don"):
            w = float(d)
    cd = km.get("komut_donusumu")
    if isinstance(cd, dict):
        for k, d in cd.items():
            if not isinstance(d, (int, float)):
                continue
            if "hiz_m" in k:
                v = float(d)
            elif "donus" in k:
                w = float(d)
    return v, w


class Yontem:
    def __init__(self, bilgi=None, kamera=None, v_tam=None, w_tam=None):
        v0, w0 = _nominal(bilgi)
        self.v_tam = v_tam if v_tam is not None else v0
        self.w_tam = w_tam if w_tam is not None else w0
        self.x = self.y = self.yon = 0.0
        self.t_onceki = None
        self.k_onceki = (0.0, 0.0)

    def guncelle(self, kare_bgr, t_ms, komut):
        self.komut_simdi = (float(komut[0]), float(komut[1]))
        if self.t_onceki is not None:
            dt = (t_ms - self.t_onceki) / 1000.0
            sol = (self.k_onceki[0] + self.komut_simdi[0]) / 2.0     # yamuk kuralı: aralığın iki ucundaki komutun ortalaması
            sag = (self.k_onceki[1] + self.komut_simdi[1]) / 2.0
            v = self.v_tam * (sol + sag) / 2.0 / 255.0
            w = self.w_tam * (sol - sag) / 510.0
            # yay üzerinde tam entegrasyon: yön dt boyunca değişir
            y0 = np.radians(self.yon)
            y1 = np.radians(self.yon + w * dt)
            if abs(y1 - y0) > 1e-9:
                self.x += v / (np.radians(w)) * (-np.cos(y1) + np.cos(y0))
                self.y += v / (np.radians(w)) * (np.sin(y1) - np.sin(y0))
            else:
                self.x += v * dt * np.sin(y0)
                self.y += v * dt * np.cos(y0)
            self.yon = (self.yon + w * dt) % 360.0
        self.t_onceki = t_ms
        self.k_onceki = (float(komut[0]), float(komut[1]))
        return self.x, self.y, self.yon
