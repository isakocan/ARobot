"""Tüm koşular için tutarlilik() hesapla ve her koşu klasörüne olcut2.json yaz (önbellek)."""
import glob, json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import parametre_olcut as po
KOSU = os.path.join(po.KLASOR, "..", "..", "kayitlar", "slam", "parametre", "kosu_v2")
n = 0
for k in sorted(glob.glob(os.path.join(KOSU, "*__*"))):
    if not os.path.exists(os.path.join(k, "olcut.json")) or os.path.exists(os.path.join(k, "olcut2.json")):
        continue
    try:
        json.dump(po.tutarlilik(k), open(os.path.join(k, "olcut2.json"), "w"))
        n += 1
    except Exception as e:
        print("hata", k, e)
print("hesaplanan", n)
