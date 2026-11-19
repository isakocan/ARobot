#!/usr/bin/env python3
# Theta S lens 1 (esit uzaklikli balik gozu, r = f*theta) -> SVO omni (Scaramuzza ocam) yaml'i.
# SVO omni sozlesmesi (vikit omni_projection.cpp):
#   geri izdusum: p = A^-1 (uv - c);  rho=|p|;  isin = (p.x, p.y, -poly(rho)) normalize
#       poly(rho) = a0 + a1 rho + ... + a4 rho^4   (a0 < 0 -> z ileri pozitif)
#   ileri izdusum: z' = -z; theta = atan(z'/|xy|);  rho = sum inv[i] theta^i (12 terim)
#       uv = A [x,y]/|xy| * rho + c ;  A = [[1, d_c],[e, d_d]]... yaml'da (c,d,e) -> [[1,e],[d,c]] degil:
#       distortionToAffineCorrection: A = [[1.0, dist(2)], [dist(1), dist(0)]]  (dist = c d e)
# Yaml sirasi (24): poly0..4, cx, cy, c, d, e, inv0..11, maske_min_gorece, maske_max_gorece
# Ileri izdusumde theta = alfa - pi/2 (alfa: eksenden aci) -> rho = f*(theta + pi/2): DOGRUSAL,
# ters polinom birebir (inv0 = f*pi/2, inv1 = f).
# Geri polinom: w(rho) = -poly(rho) = rho / tan(rho/f) (alfa = rho/f), 4. derece ile uydurulur.
import numpy as np
from scipy.optimize import least_squares

F, CX, CY, RMAX = 180.87, 322.42, 319.04, 306.0
W = H = 640

def poly_w(a, rho):
    return -(a[0] + a[1]*rho + a[2]*rho**2 + a[3]*rho**3 + a[4]*rho**4)

def backproject(a, u, v, A=np.eye(2)):
    p = np.linalg.inv(A) @ np.array([u - CX, v - CY])
    rho = np.hypot(*p)
    b = np.array([p[0], p[1], poly_w(a, rho)])
    return b / np.linalg.norm(b)

def project(inv, A, b):
    x, y, z = b
    zz = -z
    n = np.hypot(x, y)
    th = np.arctan(zz / n)
    rho = sum(inv[i] * th**i for i in range(12))
    return A @ np.array([x / n * rho, y / n * rho]) + np.array([CX, CY])

def fit():
    rho = np.linspace(0.0, RMAX, 400)
    # hata: geri izdusumun verdigi aci ile gercek aci farki, piksele cevrilmis (f * aci)
    def res(a):
        alpha = np.arctan2(rho, poly_w(a, rho))  # eksenden aci
        return F * alpha - rho
    # ilk tahmin: w = rho/tan(rho/f) icin en kucuk kareler, a1 serbest
    r0 = rho[1:]
    w0 = r0 / np.tan(r0 / F)
    V = np.vander(r0, 5, increasing=True)
    a0 = np.linalg.lstsq(V, -w0, rcond=None)[0]
    sol = least_squares(res, a0, x_scale=np.array([1, 1e-1, 1e-3, 1e-5, 1e-8]))
    # en kucuk kareler yerine minimax (en buyuk hatayi kucult): kenarda 0.22 px -> ~0.055 px
    from scipy.optimize import minimize
    sc = np.array([1, 1e-1, 1e-3, 1e-5, 1e-8, 1.0])
    s0 = np.r_[sol.x, np.abs(res(sol.x)).max()] / sc
    o = minimize(lambda z: (z * sc)[5], s0, method="SLSQP", options={"maxiter": 500, "ftol": 1e-12},
                 constraints=[{"type": "ineq", "fun": lambda z: (z * sc)[5] - res((z * sc)[:5])},
                              {"type": "ineq", "fun": lambda z: (z * sc)[5] + res((z * sc)[:5])}])
    return (o.x * sc)[:5]

if __name__ == "__main__":
    a = fit()
    inv = np.zeros(12); inv[0] = F * np.pi / 2; inv[1] = F
    A = np.eye(2)
    # gidis-donus (piksel): geri izdusum -> ileri izdusum
    errs = []
    for r in np.linspace(0.5, RMAX, 200):
        for ang in np.linspace(0, 2*np.pi, 12, endpoint=False):
            u, v = CX + r*np.cos(ang), CY + r*np.sin(ang)
            b = backproject(a, u, v)
            uv = project(inv, A, b)
            errs.append(np.hypot(uv[0]-u, uv[1]-v))
    errs = np.array(errs)
    print("poly (a0..a4):", repr(a))
    print("gidis-donus hata px: max %.4f ort %.4f" % (errs.max(), errs.mean()))
    # dogrudan acisal hata (derece): bilinen yon -> kendi formulumuzle piksel -> geri
    for th_deg in (0, 30, 60, 90, 97):
        t = np.deg2rad(th_deg)
        d = np.array([np.sin(t)*0.6, -np.sin(t)*0.8, np.cos(t)])  # y asagi: piksel = c + (dx,-dy)/|dxy|*theta*f
        ideal = np.array([CX, CY]) + np.array([d[0], -d[1]]) / np.hypot(d[0], d[1]) * t * F if t > 0 else np.array([CX, CY])
        print("aci %3d derece: ideal piksel %s" % (th_deg, np.round(ideal, 2)))
    data = list(a) + [CX, CY, 1.0, 0.0, 0.0] + list(inv) + [0.0, RMAX / (H / 2.0)]
    with open("theta_s_on_lens.yaml", "w") as f:
        f.write("""# Ricoh Theta S, 1. lens (on), 640x640 kirpim; SVO omni (Scaramuzza ocam) modeli.
# Uretici: tools/svo/kamera_fit.py. Esit uzaklikli balik gozu r = f*theta (f=180.87 px/rad), ~194 derece gorus.
# Son iki sayi: gorece maske yaricaplari (kucuk, buyuk), yaricap = yukseklik/2 (320 px) ile carpilir -> 306 px disi maskeli.
cameras:
- camera:
    distortion:
      parameters:
        cols: 1
        rows: 0
        data: []
      type: none
    image_height: %d
    image_width: %d
    intrinsics:
      cols: 1
      data: [%s]
      rows: 24
    label: cam0
    line-delay-nanoseconds: 0
    type: omni
    mask: theta_s_on_lens_maske.png
  T_B_C:
    cols: 4
    rows: 4
    data: [ 1., 0., 0., 0.,
            0., 1., 0., 0.,
            0., 0., 1., 0.,
            0., 0., 0., 1.]
label: theta_s_on_lens
""" % (H, W, ", ".join(repr(float(x)) for x in data)))
    print("yaml yazildi")
