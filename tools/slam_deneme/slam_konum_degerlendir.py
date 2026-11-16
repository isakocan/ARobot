"""Yeniden konumlama (yalnız konumlama kipi) sonuçlarının değerlendirmesi.
konum_ozet(sonuc_klasoru, harita_klasoru=None): konumlanan kare oranı, soğuk başlangıçta ilk konumlanan kareye kadar kare/saniye,
  ve (aynı kayıtın daha önce haritalanmış karelerinde) harita anahtar karelerinin konumuna göre hata (harita birimi)."""
import json
import os

import numpy as np


def oku(klasor):
    P = [json.loads(s) for s in open(os.path.join(klasor, "poz.jsonl"), encoding="utf-8")]
    t = np.array([p["t_ms"] for p in P]) / 1000.0
    izl = np.array([bool(p.get("izleniyor")) for p in P])
    c = np.full((len(P), 3), np.nan)
    R = np.full((len(P), 3, 3), np.nan)
    for i, p in enumerate(P):
        if izl[i] and p.get("poz_cw"):
            T = np.array(p["poz_cw"]).reshape(4, 4)
            R[i] = T[:3, :3]        # poz_cw alanı gerçekte T_wc
            c[i] = T[:3, 3]
    return P, t, izl, c, R


def konum_ozet(sonuc, harita=None, tur_ort_hiz=None):
    P, t, izl, c, R = oku(sonuc)
    out = dict(kare=len(P), konumlanan=int(izl.sum()), oran=float(izl.mean()))
    ilk = np.where(izl)[0]
    out["ilk_konumlanan_kare"] = int(ilk[0]) if len(ilk) else None
    out["ilk_konumlanan_sn"] = float(t[ilk[0]] - t[0]) if len(ilk) else None
    # ilk konumlandıktan sonra tekrar kayıp
    if len(ilk):
        s = izl[ilk[0]:]
        out["ilk_sonrasi_oran"] = float(s.mean())
        out["ilk_sonrasi_kayip_gecis"] = int(((~s[1:]) & s[:-1]).sum())
    if harita:
        hm = json.load(open(os.path.join(harita, "harita.json")))
        kf = hm["anahtar_kareler"]
        kt = np.array([k["t"] for k in kf])
        kc = np.array([np.array(k["poz_wc"]).reshape(4, 4)[:3, 3] for k in kf])
        kR = np.array([np.array(k["poz_wc"]).reshape(4, 4)[:3, :3] for k in kf])
        hata, yhata = [], []
        for i in np.where(izl)[0]:
            j = np.argmin(np.abs(kt - t[i]))
            if abs(kt[j] - t[i]) < 1e-3:
                hata.append(np.linalg.norm(c[i] - kc[j]))
                f1, f2 = R[i] @ [0, 0, 1], kR[j] @ [0, 0, 1]
                yhata.append(np.degrees(np.arccos(np.clip(f1 @ f2, -1, 1))))
        out["anahtar_kare_ile_karsilastirilan"] = len(hata)
        if hata:
            out["konum_hata_birim_medyan"] = float(np.median(hata))
            out["konum_hata_birim_p90"] = float(np.percentile(hata, 90))
            out["yon_hata_derece_medyan"] = float(np.median(yhata))
            out["yon_hata_derece_p90"] = float(np.percentile(yhata, 90))
    return out
