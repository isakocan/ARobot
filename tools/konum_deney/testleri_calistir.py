"""pytest yoksa testleri çalıştırır:  python tools/konum_deney/testleri_calistir.py  (pytest varsa: python -m pytest tools/konum_deney)"""
import os
import sys
import time
import traceback

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import test_konum_deney as t  # noqa: E402

basarisiz = 0
adlar = [a for a in dir(t) if a.startswith("test_") and a != "test_temizlik"] + ["test_temizlik"]
for ad in adlar:
    a = time.time()
    try:
        getattr(t, ad)()
        print("GEÇTİ  %-45s %.1f sn" % (ad, time.time() - a))
    except Exception:
        basarisiz += 1
        print("KALDI  %s" % ad)
        traceback.print_exc()
print("\nSayısal sonuçlar:")
for k, v in t.SONUCLAR.items():
    print("  %s = %s" % (k, v))
print("\n%d test, %d başarısız" % (len(adlar), basarisiz))
sys.exit(1 if basarisiz else 0)
