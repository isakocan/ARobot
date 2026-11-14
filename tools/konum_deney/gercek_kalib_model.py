"""Gercek kamera icin kalibrasyon cekirdegi: parametreli kamera (egim, yan egim, yon, mercek merkezleri, f), eslesmelerden zemin yer degistirmesi.
KameraGercek: kamera.Kamera'nin alt sinifi. Fark: montaj matrisine ek 'yan_egim' (robot ileri ekseni z etrafinda donme, derece):
    d_kam = Rz(yatma) Rx(egim) Rz(yan_egim) Ry(yon) d_robot        (yan_egim = 0 ise kamera.Kamera ile ayni)
Robot cercevesi = ZEMINE gore cerceve (y yukari, zemin duzlemi y = -h). egim/yan_egim kameranin zemine gore eksik eginimini, yon ilerleme yonune gore kaymasini verir.
Parametre sozlugu P: egim, yan_egim, yon, m1x, m1y, m2x, m2y, f1, f2 (varsayilan = kamera.json)."""
import copy
import json
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_ortak as go  # noqa: E402
from kamera import Kamera, _Rx, _Ry, _Rz  # noqa: E402

KLASOR = os.path.dirname(os.path.abspath(__file__))
H = 0.17
KALIB = os.path.join(go.SONUC, "kalib")
ONB = os.path.join(KALIB, "onbellek")
ISIM = ["egim", "yan_egim", "yon", "m1x", "m1y", "m2x", "m2y", "f1", "f2"]


class KameraGercek(Kamera):
    def __init__(self, ayar=None):
        super().__init__(ayar)
        m = self.ayar["montaj"]
        self.M = _Rz(float(m["yatma"])) @ _Rx(float(m["egim"])) @ _Rz(float(m.get("yan_egim", 0.0))) @ _Ry(float(m["yon"]))


def nominal():
    with open(os.path.join(KLASOR, "kamera.json"), encoding="utf-8") as f:
        a = json.load(f)
    return dict(egim=a["montaj"]["egim"], yan_egim=0.0, yon=a["montaj"]["yon"], m1x=a["lens1"]["merkez_x"], m1y=a["lens1"]["merkez_y"],
                m2x=a["lens2"]["merkez_x"], m2y=a["lens2"]["merkez_y"], f1=a["lens1"]["f"], f2=a["lens2"]["f"])


def ayar_uret(P, taban=None):
    if taban is None:
        with open(os.path.join(KLASOR, "kamera.json"), encoding="utf-8") as f:
            taban = json.load(f)
    a = copy.deepcopy(taban)
    a["montaj"]["egim"] = float(P["egim"])
    a["montaj"]["yan_egim"] = float(P.get("yan_egim", 0.0))
    a["montaj"]["yon"] = float(P["yon"])
    a["lens1"].update(merkez_x=float(P["m1x"]), merkez_y=float(P["m1y"]), f=float(P["f1"]))
    a["lens2"].update(merkez_x=float(P["m2x"]), merkez_y=float(P["m2y"]), f=float(P["f2"]))
    return a


def kamera(P=None):
    return KameraGercek(ayar_uret(P or nominal()))


def yer(kam, lens, u, v, h=H, cuk_min_der=2.5):
    """Piksel (u, v) -> zemin noktasi (x sag, z ileri) m, yer uzakligi r, gecerli (ufkun yeterince altinda). Tek lens (0|1) dizisi."""
    d, ok = kam.piksel_to_isin(u, v, lens)
    dy = d[..., 1]
    gec = ok & (dy < -np.sin(np.radians(cuk_min_der)))
    s = np.where(gec, h / np.where(gec, -dy, 1.0), 0.0)
    x, z = d[..., 0] * s, d[..., 2] * s
    return x, z, np.hypot(x, z), gec


def yer_cift(kam, E, h=H, cuk_min_der=2.5):
    """Eslesme dizisi E (lens, u1, v1, u2, v2) icin her iki karenin zemin noktalari: dict P1(x,z), P2(x,z), r1, gec."""
    n = len(E["lens"])
    X1 = np.zeros(n); Z1 = np.zeros(n); X2 = np.zeros(n); Z2 = np.zeros(n); R1 = np.zeros(n); R2 = np.zeros(n); G = np.zeros(n, bool)
    for k in (0, 1):
        m = E["lens"] == k
        if not m.any():
            continue
        x1, z1, r1, g1 = yer(kam, k, E["u1"][m].astype(np.float64), E["v1"][m].astype(np.float64), h, cuk_min_der)
        x2, z2, r2, g2 = yer(kam, k, E["u2"][m].astype(np.float64), E["v2"][m].astype(np.float64), h, cuk_min_der)
        X1[m], Z1[m], X2[m], Z2[m], R1[m], R2[m], G[m] = x1, z1, x2, z2, r1, r2, g1 & g2
    return dict(x1=X1, z1=Z1, x2=X2, z2=Z2, r1=R1, r2=R2, gec=G)


def eslesme_yukle(ad):
    z = np.load(os.path.join(ONB, "eslesme_%s.npz" % ad))
    E = {k: z[k] for k in z.files}
    from kayit import Kayit
    kay = Kayit(os.path.join(go.GERCEK, ad))
    t = kay.t_ms / 1000.0
    E["dt"] = (t[E["cift"][:, 1]] - t[E["cift"][:, 0]]) if len(E["cift"]) else np.zeros(0)
    E["t0"] = t[E["cift"][:, 0]] if len(E["cift"]) else np.zeros(0)
    kom = np.array([kay.komut(int(x)) for x in kay.t_ms])
    kg = np.vstack([np.zeros((3, 2)), kom[:-3]])
    E["isaret"] = np.sign(kg[E["cift"][:, 0], 0]) if len(E["cift"]) else np.zeros(0)
    E["ad"] = ad
    return E


def sigma_px(r, h=H):
    """Tek piksel (0,0055 rad) hatasinin zemin uzakligindaki metrik karsiligi (yer yer degistirmesi gurultu olcegi), m."""
    return 0.0055 * (r * r + h * h) / h * np.sqrt(2.0)
