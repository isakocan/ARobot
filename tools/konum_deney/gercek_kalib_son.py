"""Gorev 2 son adimi: kalibrasyon kumesi (2a+4) uzerinde SECILEN asama (A: egim, yan egim, yon) + belirsizlik (cift ornekleme bootstrap, kayit basina ayri cozum, ileri/geri ayri yon)
ve kamera_gercek.json yazimi.
  python gercek_kalib_son.py
Cikti: kamera_gercek.json (A), kamera_gercek_B.json (A + mercek merkezleri; karsilastirma icin, kullanilmaz), kalib/onbellek/son.json"""
import copy
import json
import os
import sys

import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_kalib_model as km  # noqa: E402
import gercek_kalib_coz as kc  # noqa: E402

KAL = ["2a_vitessiz_duz", "4_parke_vitessiz"]
TUM = ["2a_vitessiz_duz", "2b_otomatik_duz", "4_parke_vitessiz", "5_saksi_turu", "olcum_ham"]


def json_yaz(P, dosya, aciklama, asama, belirsizlik=None):
    with open(os.path.join(km.KLASOR, "kamera.json"), encoding="utf-8") as f:
        a = json.load(f)
    a = km.ayar_uret(P, a)
    a["ad"] = "Ricoh Theta S, USB 1280x720 cift balik gozu (GERCEK ROBOT, oz-kalibre, asama %s)" % asama
    a["montaj"]["aciklama_ek"] = ("EK ALANLAR (kamera.Kamera bunlari BILMEZ, gercek_kalib_model.KameraGercek okur): yan_egim = kameranin zemine gore robot ileri ekseni (z) etrafinda egimi (derece); "
                                  "M = Rz(yatma) Rx(egim) Rz(yan_egim) Ry(yon). egim = sag ekseni (x) etrafinda, yon = yukari eksen etrafinda ilerleme yonune gore kayma. Robot cercevesi = ZEMINE gore cerceve.")
    a["montaj_kaynak"] = aciklama
    a["kalibrasyon"] = dict(yontem="duz gidis kare ciftlerinde zemin noktalarinin katı oteleme tutarliligi (geri-projeksiyon hatasi, Cauchy), h=0,17 m SABIT, 2,00 m gercegi KULLANILMADI",
                            kume="2a_vitessiz_duz + 4_parke_vitessiz", asama=asama, kod="tools/konum_deney/gercek_kalib_coz.py, gercek_kalib_son.py", belirsizlik=belirsizlik)
    json.dump(a, open(os.path.join(km.KLASOR, dosya), "w", encoding="utf-8"), indent=2, ensure_ascii=False)


if __name__ == "__main__":
    V = kc.veri_hazirla(KAL)
    P0 = km.nominal()
    PA, oz = kc.coz(V, kc.ASAMALAR["A"], P0, "A (2a+4)")
    PB, ozb = kc.coz(V, kc.ASAMALAR["B"], PA, "B (2a+4)")
    son = dict(A=PA, B=PB, ozet_A=oz, ozet_B=ozb)
    # bootstrap: cift ornekleme (tekrarli), asama A
    rng = np.random.default_rng(7)
    boot = []
    for b in range(40):
        idx = np.sort(rng.choice(V["K"], V["K"], replace=True))
        # yeniden indeksle: cift k'nin eslesmeleri, tekrar sayisi kadar
        yeni = {k: [] for k in ("lens", "u1", "v1", "u2", "v2", "c")}
        tau0, sg = [], []
        for n, k in enumerate(idx):
            m = V["c"] == k
            for key in ("lens", "u1", "v1", "u2", "v2"):
                yeni[key].append(V[key][m])
            yeni["c"].append(np.full(m.sum(), n))
            tau0.append(V["tau0"][k]); sg.append(V["sgn"][k])
        W = dict(lens=np.concatenate(yeni["lens"]), u1=np.concatenate(yeni["u1"]), v1=np.concatenate(yeni["v1"]), u2=np.concatenate(yeni["u2"]), v2=np.concatenate(yeni["v2"]),
                 c=np.concatenate(yeni["c"]), tau0=np.array(tau0), sgn=np.array(sg), K=len(idx))
        import io, contextlib
        with contextlib.redirect_stdout(io.StringIO()):
            Pb, _ = kc.coz(W, kc.ASAMALAR["A"], P0, "boot")
        boot.append([Pb["egim"], Pb["yan_egim"], Pb["yon"]])
    boot = np.array(boot)
    son["bootstrap_A"] = dict(ort=boot.mean(0).tolist(), std=boot.std(0).tolist(), p5=np.percentile(boot, 5, 0).tolist(), p95=np.percentile(boot, 95, 0).tolist(), isim=["egim", "yan_egim", "yon"])
    print("bootstrap A (egim, yan_egim, yon): ort", np.round(boot.mean(0), 2), "std", np.round(boot.std(0), 2), flush=True)
    # ileri / geri ayri yon
    for ad_, isaret in (("ileri", 1), ("geri", -1)):
        m = V["sgn"] == isaret
        if m.sum() < 8:
            continue
        sec = {"2a_vitessiz_duz": np.array([False] * 0)}
        # secim: cift_secimi ile
        cs = {}
        for ad in KAL:
            E = km.eslesme_yukle(ad)
            cs[ad] = (E["isaret"] == isaret)
        V2 = kc.veri_hazirla(KAL, cift_secimi=cs)
        import io, contextlib
        with contextlib.redirect_stdout(io.StringIO()):
            Pk, _ = kc.coz(V2, kc.ASAMALAR["A"], P0, ad_)
        son["yon_" + ad_] = dict(cift=int(V2["K"]), egim=Pk["egim"], yan_egim=Pk["yan_egim"], yon=Pk["yon"])
        print(ad_, "cift", V2["K"], {k: round(Pk[k], 2) for k in kc.ASAMALAR["A"]}, flush=True)
    # kayit basina
    kb = {}
    for ad in TUM + ["6c_karanlik_surus"]:
        Vk = kc.veri_hazirla([ad])
        if Vk["K"] < 8:
            continue
        import io, contextlib
        with contextlib.redirect_stdout(io.StringIO()):
            Pk, _ = kc.coz(Vk, kc.ASAMALAR["A"], P0, ad)
        kb[ad] = dict(cift=int(Vk["K"]), egim=Pk["egim"], yan_egim=Pk["yan_egim"], yon=Pk["yon"])
        print("kayit", ad, kb[ad], flush=True)
    son["kayit_basina_A"] = kb
    json.dump(son, open(os.path.join(km.ONB, "son.json"), "w", encoding="utf-8"), indent=1)
    kb_ = son["kayit_basina_A"]
    ort = {k: [float(np.mean([v[k] for a, v in kb_.items() if a != "6c_karanlik_surus"])), float(np.std([v[k] for a, v in kb_.items() if a != "6c_karanlik_surus"]))] for k in ("egim", "yan_egim", "yon")}
    bel = dict(bootstrap_std_derece=dict(zip(son["bootstrap_A"]["isim"], son["bootstrap_A"]["std"])),
               kayitlar_arasi_ort_std_derece=ort, kayitlar_arasi_not="2a, 2b, 4, 5, olcum_ham tek tek cozumleri (6c: 16 cift, karanlik, guvenilmez, disarida). Gercekci belirsizlik bootstrap degil kayitlar arasi yayilim (~0,2-0,5 derece).",
               ileri_geri_yon=dict(ileri=son.get("yon_ileri"), geri=son.get("yon_geri")),
               merkezler_ve_f="KALIBRE EDILMEDI: asama B (merkezler) kayitlar arasi tutarsiz (m1y 317-330 px) ve bagimsiz olcum_ham sinamasinda kotulesti (1,68 -> 1,41 m); asama C (f) h ile olcek dejenere")
    json_yaz(PA, "kamera_gercek.json", "OZ-KALIBRASYON asama A (egim, yan_egim, yon); mercek merkezleri/f kamera.json'dakiler", "A", bel)
    json_yaz(PB, "kamera_gercek_B.json", "OZ-KALIBRASYON asama B (A + mercek merkezleri); YALNIZ KARSILASTIRMA", "B")
    print("A:", {k: round(PA[k], 3) for k in km.ISIM})
