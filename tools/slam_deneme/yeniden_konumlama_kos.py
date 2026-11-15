"""Aşama 2 koşuları: harita y1 (ilk yarı) ve g1 (tam kayıt) ile yalnız konumlama kipi. Sonuçlar kayitlar/slam/sonuc/ok_*/ ve ok_ozet.json."""
import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from slam_kos import kos, SONUC
from slam_konum_degerlendir import konum_ozet

O = {}
def calis(ad, veri, harita, bas, bitis, kiyas=None):
    kos(ad, veri, bas=bas, bitis=bitis, map_in=harita, map_out=False, mod="konum", gercek_zaman=False)
    O[ad] = konum_ozet(os.path.join(SONUC, ad), os.path.join(SONUC, kiyas) if kiyas else None)
    json.dump(O, open(os.path.join(SONUC, "ok_ozet.json"), "w"), indent=1)
    print(ad, O[ad], flush=True)

# (a) ilk yarıdan harita, ikinci yarı
calis("ok_a_ikinci_yari", "olcum_ham", "y1/harita.msg", 2243, 4486)
# soğuk başlangıç: haritalanmış yerlerde, farklı başlangıç noktaları
for b in (60, 300, 600, 900, 1200, 1500, 1800, 2100):
    calis("ok_soguk_%d" % b, "olcum_ham", "y1/harita.msg", b, b + 150, kiyas="y1")
# (b) başka kayıtlar, tam kayıt haritası g1
calis("ok_b_5_saksi_turu", "5_saksi_turu", "g1/harita.msg", 0, -1)
calis("ok_b_4_parke", "4_parke_vitessiz", "g1/harita.msg", 0, -1)
calis("ok_b_6c_karanlik", "6c_karanlik_surus", "g1/harita.msg", 0, -1)
