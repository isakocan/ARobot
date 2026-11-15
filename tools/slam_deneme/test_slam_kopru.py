"""Köprü uçtan uca sınaması: olcum_ham'ın ilk 300 karesi (ham çift balık gözü JPEG + komut) gerçek zamanda (15 kare/sn) istemciden köprüye.
Beklenen: başlatma sonrası kareler Tracking, poz geliyor, anahtar nokta ipuçları geliyor, harita kaydet/yükle/mod değiştir çalışıyor.
Çalıştırma: python test_slam_kopru.py   (köprüyü kendisi başlatır; WSL ve kurulum gerekir) ya da pytest."""
import json
import os
import sys
import time

import numpy as np

KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)
sys.path.insert(0, os.path.join(KLASOR, "..", "konum_deney"))
from kayit import Kayit  # noqa: E402
from slam_istemci import SlamIstemci  # noqa: E402
from slam_kopru_surec import KopruSurec  # noqa: E402

KAYIT = os.path.join(KLASOR, "..", "..", "kayitlar", "gercek", "olcum_ham")
N = 300
SONUC = os.path.join(KLASOR, "..", "..", "kayitlar", "slam", "sonuc", "kopru_test")


def calistir(gercek_zaman=True):
    os.makedirs(SONUC, exist_ok=True)
    k = Kayit(KAYIT)
    t0_ms = int(k.t_ms[0])
    sonuc = dict(kare=[], izlenen=0, ipucu_olan=0)
    with KopruSurec(port=9761) as sunucu:
        ist = SlamIstemci(port=9761)
        basla = time.perf_counter()
        for i in range(N):
            t_ms = int(k.t_ms[i])
            if gercek_zaman:
                hedef = basla + (t_ms - t0_ms) / 1000.0
                bekle = hedef - time.perf_counter()
                if bekle > 0:
                    time.sleep(bekle)
            kom = k.komut(t_ms)
            y = ist.feed(k.jpeg_bayt(i), t_ms, tuple(kom), tam_durum=(i % 50 == 49))
            sonuc["kare"].append(dict(i=i, durum=y["durum"], uctan_uca=y["sureler_ms"]["uctan_uca"], donusum=y["sureler_ms"]["donusum"],
                                     takip=y["sureler_ms"]["takip"], sunucu=y["sureler_ms"]["toplam_sunucu"], ipucu=len(y["ipuclari"]),
                                     poz="poz_cw" in y, tam="noktalar" in y))
            if y["durum"] == "Tracking":
                sonuc["izlenen"] += 1
            if y["ipuclari"]:
                sonuc["ipucu_olan"] += 1
        # harita kaydet, konum kipine geç, kareleri sürdür, yükle
        e = ist.kaydet("/root/slam/cikti/kopru_test.msg")
        mk = ist.mod("konum")
        yk = [ist.feed(k.jpeg_bayt(i), int(k.t_ms[i]), (0, 0)) for i in range(N, N + 30)]
        sonuc["konum_kipi_izlenen"] = sum(1 for y in yk if y["durum"] == "Tracking")
        sonuc["konum_kipi_harita_nokta_degisim"] = yk[-1]["harita_nokta_sayisi"] - yk[0]["harita_nokta_sayisi"]
        l = ist.yukle("/root/slam/cikti/kopru_test.msg", "konum")
        yl = [ist.feed(k.jpeg_bayt(i), int(k.t_ms[i]), (0, 0)) for i in range(N, N + 60)]
        ilk = next((j for j, y in enumerate(yl) if y["durum"] == "Tracking"), None)
        sonuc["yukle_sonrasi_ilk_konumlanan_kare"] = ilk
        sonuc["kaydet_ok"] = e.get("ok")
        durum = ist.durum()
        sonuc["durum_nokta"] = len(durum["noktalar"])
        ist.kapat(sunucuyu_durdur=True)
    kare = sonuc["kare"]
    ue = np.array([q["uctan_uca"] for q in kare[60:]])
    sonuc["ozet"] = dict(izlenen_oran_baslatma_sonrasi=float(np.mean([q["durum"] == "Tracking" for q in kare[60:]])),
                         uctan_uca_ms_medyan=float(np.median(ue)), uctan_uca_ms_p95=float(np.percentile(ue, 95)), uctan_uca_ms_maks=float(ue.max()),
                         donusum_ms_medyan=float(np.median([q["donusum"] for q in kare[60:]])), takip_ms_medyan=float(np.median([q["takip"] for q in kare[60:]])),
                         sunucu_ms_medyan=float(np.median([q["sunucu"] for q in kare[60:]])))
    with open(os.path.join(SONUC, "test.json"), "w") as f:
        json.dump(sonuc, f, indent=1)
    return sonuc


def test_kopru():
    s = calistir()
    assert s["ozet"]["izlenen_oran_baslatma_sonrasi"] > 0.95
    assert s["ipucu_olan"] > 200
    assert s["kaydet_ok"]
    assert s["konum_kipi_izlenen"] >= 25
    assert s["ozet"]["uctan_uca_ms_medyan"] < 66


if __name__ == "__main__":
    s = calistir()
    print(json.dumps({k: v for k, v in s.items() if k != "kare"}, indent=1))
