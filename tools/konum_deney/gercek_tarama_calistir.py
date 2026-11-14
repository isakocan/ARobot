"""SegFormer-B0 zemin sınırı taramasını gerçek kayıtlarda çalıştırır (zemin_degerlendir.tara_model_hepsi, her 5. kare; yöntem aynen).
  python gercek_tarama_calistir.py <maske eski|gercek> [kayit ...]
Çıktı: kayitlar/sonuclar/gercek/harita_<maske>/tarama/{model_gorunum,model_gorunum_ilk,model_serit}/theta/<kayit>.npz"""
import os
import sys
import time

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_ortak as go  # noqa: E402

if __name__ == "__main__":
    maske = sys.argv[1]
    adlar = sys.argv[2:] or go.ADLAR
    go.kur(maske)
    import zemin_degerlendir as zd
    t0 = time.time()
    for ad in adlar:
        zd.tara_model_hepsi("theta", ad, 5)
        print("tarama tamam", ad, "%.0f sn" % (time.time() - t0), flush=True)
