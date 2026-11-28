"""Nokta haritası (PC; torch + XFeat): robotun 360 görüntüsündeki tanınabilir noktaların odadaki 3B yerleri.

Fikir: robot yerini bildiği her yerde (önce işaret yakınken, sonra haritanın kendisiyle) 15 cm'de bir anahtar kare
saklar: görüntüdeki noktalar (XFeat), robotun yeri ve yönü. Yakın iki anahtar karede ortak görülen noktalar üçgenlenir
(iki ışının kesiştiği yer) -> odadaki 3B yerleri. Sonra herhangi bir karede bu noktaların hangi yönde göründüğüne
bakılıp robotun yeri ve yönü çözülür (işaretin yaptığının aynısı, ama yüzlerce noktayla ve işaretsiz). Harita işaretten
uzağa doğru büyür: yeni anahtar karenin yeri haritadan bulunur, onun noktaları da haritaya eklenir.

Panorama: ham çift balık gözünden eş dikdörtgen, enlem -25..85 (tavan dahil), derece başına 4 piksel. İşaretin
üstündeki ve robotun gövdesindeki noktalar atılır (harita işarete değil odaya dayansın)."""
import math, os, sys
import cv2
import numpy as np
import torch

_BURASI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(_BURASI, "..", "..", "robot", "jetson", "konum"))
import teta, orb_hafiza

PX, ALT, UST = 4, -25.0, 85.0
GEN, YUK = 360 * PX, int((UST - ALT) * PX)
H = 0.17
TOL, ENLEM_TOL = 6.0, 4.0
CIHAZ = "cuda" if torch.cuda.is_available() else "cpu"
MX, MY = teta.es_dikdortgen_haritasi(GEN, YUK, ALT, UST)


def da(a): return (a + 180.0) % 360.0 - 180.0


def panorama(g): return cv2.remap(g, MX, MY, cv2.INTER_LINEAR)


def nokta_maskesi(lon, lat, gor, govde):
    tut = np.ones(len(lon), bool)
    for q in gor or []:
        mes, yon, y = q[1], q[2], q[3]
        r = math.degrees(math.atan2(0.18, max(0.1, mes))) + 3.0
        ey = math.degrees(math.atan2(abs(y), max(0.1, mes)))
        tut &= ~((np.abs(da(lon - yon)) < r) & (np.abs(lat) < ey + r))
    if govde is not None:
        gh, gw = govde.shape
        gx = np.clip(((lon / 360.0 + 0.5) * gw).astype(int), 0, gw - 1)
        gy = ((orb_hafiza.UST - lat) * gh / (orb_hafiza.UST - orb_hafiza.ALT)).astype(int)
        ic = (gy >= 0) & (gy < gh)
        g = np.ones(len(lon), bool); g[ic] = govde[gy[ic], gx[ic]] > 0
        tut &= g
    return tut


class XFeat:
    def __init__(self, nokta=1500):
        self.m = torch.hub.load("verlab/accelerated_features", "XFeat", pretrained=True, top_k=nokta, trust_repo=True, verbose=False)
        self.nokta = nokta

    @torch.no_grad()
    def cikar(self, pan, gor=None, govde=None):
        t = torch.from_numpy(pan).float()[None, None].repeat(1, 3, 1, 1).to(CIHAZ) / 255.0
        o = self.m.detectAndCompute(t, top_k=self.nokta)[0]
        xy = o["keypoints"].cpu().numpy(); d = o["descriptors"]
        lon = (xy[:, 0] / GEN - 0.5) * 360.0; lat = UST - xy[:, 1] / PX
        m = nokta_maskesi(lon, lat, gor, govde)
        return dict(lon=lon[m].astype(np.float32), lat=lat[m].astype(np.float32), d=d[torch.from_numpy(m).to(d.device)])

    @torch.no_grad()
    def esle(self, a, b, esik=0.82):
        if len(a["d"]) < 8 or len(b["d"]) < 8: return np.zeros(0, int), np.zeros(0, int)
        s = a["d"] @ b["d"].T
        i1 = s.argmax(1); i2 = s.argmax(0)
        ok = (i2[i1] == torch.arange(len(i1), device=s.device)) & (s.max(1).values > esik)
        ia = torch.where(ok)[0]
        return ia.cpu().numpy(), i1[ia].cpu().numpy()


def kayma(q, k, ia, ib):
    """Boylam kayması doğrulaması -> (uyan, kayma derece: k'dan q'ya sağa dönüş +, uyan maskesi)"""
    if len(ia) < 6: return 0, 0.0, None
    dl = da(q["lon"][ia] - k["lon"][ib]); db = q["lat"][ia] - k["lat"][ib]
    en, kay, um = 0, 0.0, None
    for c in np.unique(np.round(dl / 2.0) * 2.0):
        u = (np.abs(da(dl - c)) < TOL) & (np.abs(db) < ENLEM_TOL)
        if u.sum() > en: en, kay, um = int(u.sum()), float(np.median(dl[u])), u
    return en, -kay, um


def isin(lon, lat, yaw):
    lo, la = np.radians(lon), np.radians(lat)
    r, u, f = np.cos(la) * np.sin(lo), np.sin(la), np.cos(la) * np.cos(lo)
    c, s = math.cos(yaw), math.sin(yaw)
    return np.stack([r * c + f * s, u, -r * s + f * c], 1)


def tahmin(P, x, z, yaw):
    v = P - np.array([x, H, z]); c, s = math.cos(yaw), math.sin(yaw)
    r = v[:, 0] * c - v[:, 2] * s; f = v[:, 0] * s + v[:, 2] * c
    return np.degrees(np.arctan2(r, f)), np.degrees(np.arctan2(v[:, 1], np.hypot(r, f)))


def coz(P, lon, lat, x0, z0, y0, it=12):
    p = np.array([x0, z0, y0], float)
    for _ in range(it):
        lo, la = tahmin(P, *p); e = np.concatenate([da(lo - lon), la - lat]); J = np.zeros((len(e), 3))
        for k in range(3):
            q = p.copy(); q[k] += 1e-4; l2, a2 = tahmin(P, *q)
            J[:, k] = (np.concatenate([da(l2 - lon), a2 - lat]) - e) / 1e-4
        w = 1.0 / np.maximum(1.0, np.abs(e) / 1.5); Jw = J * w[:, None]
        dp = np.linalg.lstsq(Jw.T @ J + 1e-6 * np.eye(3), -(Jw.T @ e), rcond=None)[0]; p += dp
        if np.abs(dp).max() < 1e-4: break
    return p


class Harita:
    """Anahtar kareler + 3B noktalar. Bir nokta birden çok anahtar karede görülebilir (iz): demet ayarı bu bağlarla
    bütün haritayı birbiriyle tutarlı hale getirir."""

    def __init__(self, yontem, aralik=0.15, komsu=1.5):
        self.Y = yontem; self.aralik = aralik; self.komsu = komsu
        self.kareler = []          # dict(no, x, z, yaw, oz, P={nokta indeksi: nokta no}, kaynak, onsel)
        self.Pn = []               # nokta no -> 3B yer
        self.gozlem = []           # nokta no -> [(kare no, nokta indeksi)]
        self.rng = np.random.default_rng(0)

    def nokta_sayisi(self):
        return len(self.Pn)

    def bos_yer(self, x, z):
        return all(math.hypot(k["x"] - x, k["z"] - z) >= self.aralik for k in self.kareler)

    def ekle(self, oz, x, z, yaw, kaynak, onsel=None):
        """onsel: (s_yer m, s_yon radyan): işaretten bilinen yerin güveni; demet ayarında kare o kadar yerine bağlı kalır."""
        b = dict(no=len(self.kareler), x=x, z=z, yaw=yaw, oz=oz, P={}, kaynak=kaynak, onsel=onsel)
        kom = sorted([k for k in self.kareler if 0.12 <= math.hypot(k["x"] - x, k["z"] - z) <= self.komsu],
                     key=lambda k: math.hypot(k["x"] - x, k["z"] - z))[:8]
        self.kareler.append(b)
        for a in kom:
            self._ucgenle(a, b)
        return b

    def _ucgenle(self, a, b):
        ib_, ia_ = self.Y.esle(b["oz"], a["oz"])           # b indeksleri, a indeksleri
        n, _, um = kayma(b["oz"], a["oz"], ib_, ia_)
        if n < 8: return
        ib_, ia_ = ib_[um], ia_[um]
        qa, qb = a["oz"], b["oz"]
        da_ = isin(qa["lon"][ia_], qa["lat"][ia_], a["yaw"]); db_ = isin(qb["lon"][ib_], qb["lat"][ib_], b["yaw"])
        pa = np.array([a["x"], H, a["z"]]); pb = np.array([b["x"], H, b["z"]]); w = pb - pa
        A_ = (da_ * da_).sum(1); B_ = (da_ * db_).sum(1); C_ = (db_ * db_).sum(1); D_ = (da_ * w).sum(1); E_ = (db_ * w).sum(1)
        den = A_ * C_ - B_ * B_; ok = den > 1e-6
        s = np.where(ok, (D_ * C_ - B_ * E_) / np.where(ok, den, 1), 0); t = np.where(ok, (D_ * B_ - A_ * E_) / np.where(ok, den, 1), 0)
        P1 = pa + da_ * s[:, None]; P2 = pb + db_ * t[:, None]; P = (P1 + P2) / 2
        ayrik = np.linalg.norm(P1 - P2, axis=1); paralaks = np.degrees(np.arccos(np.clip(B_, -1, 1)))
        ok &= (s > 0.3) & (t > 0.3) & (s < 8) & (t < 8) & (paralaks > 2.0) & (ayrik < 0.02 + 0.02 * s) & (P[:, 1] > -0.05) & (P[:, 1] < 3.0)
        for k in np.where(ok)[0]:
            ia, ib = int(ia_[k]), int(ib_[k])
            pa_no, pb_no = a["P"].get(ia), b["P"].get(ib)
            if pa_no is None and pb_no is None:
                no = len(self.Pn); self.Pn.append(P[k].copy()); self.gozlem.append([(a["no"], ia), (b["no"], ib)])
                a["P"][ia] = no; b["P"][ib] = no
            elif pa_no is not None and pb_no is None:
                b["P"][ib] = pa_no; self.gozlem[pa_no].append((b["no"], ib))
            elif pb_no is not None and pa_no is None:
                a["P"][ia] = pb_no; self.gozlem[pb_no].append((a["no"], ia))

    def yerles(self, oz, x0=None, z0=None, y0=None, yaricap=1.0, en_cok=6, en_az=15):
        """-> (x, z, yaw, uyan nokta, kullanılan anahtar kare sayısı) ya da None. x0 None: her yerde ara (konum bilinmiyor)."""
        if not self.kareler: return None
        if x0 is None:
            en = (0, None, 0.0)
            for k in self.kareler:
                ia, ib = self.Y.esle(oz, k["oz"]); n, kay, _ = kayma(oz, k["oz"], ia, ib)
                if n > en[0]: en = (n, k, kay)
            if en[0] < 25: return None
            x0, z0, y0 = en[1]["x"], en[1]["z"], en[1]["yaw"] + math.radians(en[2])
        aday = sorted([k for k in self.kareler if k["P"] and math.hypot(k["x"] - x0, k["z"] - z0) <= yaricap],
                      key=lambda k: math.hypot(k["x"] - x0, k["z"] - z0))[:en_cok]
        P, lon, lat, gor = [], [], [], set()
        for k in aday:
            ia, ib = self.Y.esle(oz, k["oz"])
            for a_, b_ in zip(ia, ib):
                no = k["P"].get(int(b_))
                if no is not None and (no, int(a_)) not in gor:
                    gor.add((no, int(a_))); P.append(self.Pn[no]); lon.append(oz["lon"][a_]); lat.append(oz["lat"][a_])
        if len(P) < en_az: return None
        P, lon, lat = np.array(P), np.array(lon), np.array(lat)
        en = (0, None)
        for _ in range(80):
            k = self.rng.choice(len(P), 3, replace=False)
            p = coz(P[k], lon[k], lat[k], x0, z0, y0, it=6)
            if math.hypot(p[0] - x0, p[1] - z0) > yaricap + 0.3: continue
            lo, la = tahmin(P, *p); u = np.hypot(da(lo - lon), la - lat) < 2.0
            if u.sum() > en[0]: en = (int(u.sum()), u)
        if en[0] < en_az: return None
        p = coz(P[en[1]], lon[en[1]], lat[en[1]], x0, z0, y0)
        lo, la = tahmin(P, *p); u = np.hypot(da(lo - lon), la - lat) < 2.0
        if u.sum() < en_az: return None
        p = coz(P[u], lon[u], lat[u], *p)
        self.son = (lon[u], lat[u])          # uyan noktalar (görsel için)
        return float(p[0]), float(p[1]), float(p[2]), int(u.sum()), len(aday)

    def duzelt(self, s_aci=0.5, tur=2, yazdir=True, s_enlem=None, onsel_k=1.0):  # 26 Kas: s_enlem 2 / onsel_k 0.3 denendi, daha kötü
        """Demet ayarı: bütün anahtar karelerin yeri / yönü ve bütün noktaların 3B yeri birlikte düzeltilir; amaç her
        noktanın her karede görüldüğü yönü (boylam, enlem) en iyi tutturmak. İşaretten bilinen kareler önsel
        güvenleriyle yerlerine bağlı: harita işarete göre sabit kalır, zincirde biriken kayma dağılır. s_aci: bir
        görüşün açı belirsizliği (derece). Sağlam kayıp (soft L1) yanlış eşleşmeleri bastırır; her turdan sonra
        3 dereceden çok tutmayan görüşler atılır."""
        from scipy.optimize import least_squares
        from scipy.sparse import lil_matrix
        K, N = len(self.kareler), len(self.Pn)
        if K < 2 or N < 10: return
        obs = [(no, kn, idx) for no, g in enumerate(self.gozlem) for kn, idx in g]
        for k in self.kareler:                 # işaret bağı hep ilk (işaretten ölçülen) yere
            if k["onsel"] is not None and "onsel_poz" not in k: k["onsel_poz"] = (k["x"], k["z"], k["yaw"])
        for t_ in range(tur):
            O = np.array([(no, kn) for no, kn, _ in obs])
            L = np.array([[self.kareler[kn]["oz"]["lon"][idx], self.kareler[kn]["oz"]["lat"][idx]] for _, kn, idx in obs])
            onc = [k for k in self.kareler if k["onsel"] is not None]
            on_deg = np.array([k["onsel_poz"] for k in onc]) if onc else np.zeros((0, 3))
            on_s = onsel_k * np.array([[k["onsel"][0], k["onsel"][0], k["onsel"][1]] for k in onc]) if onc else np.zeros((0, 3))
            on_no = np.array([k["no"] for k in onc], int)
            x0 = np.concatenate([np.array([[k["x"], k["z"], k["yaw"]] for k in self.kareler]).ravel(), np.array(self.Pn).ravel()])
            M = len(O)

            W = np.ones(M)                     # yeniden ağırlıklandırma (yalnız nokta görüşlerine; işaret bağına değil)

            def art(v):
                po = v[:3 * K].reshape(K, 3); pt = v[3 * K:].reshape(N, 3)
                p = po[O[:, 1]]; P = pt[O[:, 0]]
                vx = P[:, 0] - p[:, 0]; vy = P[:, 1] - H; vz = P[:, 2] - p[:, 1]
                c, s = np.cos(p[:, 2]), np.sin(p[:, 2])
                r = vx * c - vz * s; f = vx * s + vz * c
                lo = np.degrees(np.arctan2(r, f)); la = np.degrees(np.arctan2(vy, np.hypot(r, f)))
                e1 = da(lo - L[:, 0]) / s_aci; e2 = (la - L[:, 1]) / (s_enlem or s_aci)
                d = po[on_no] - on_deg; d[:, 2] = (d[:, 2] + math.pi) % (2 * math.pi) - math.pi
                return np.concatenate([e1 * W, e2 * W, (d / on_s).ravel()])

            A = lil_matrix((2 * M + 3 * len(onc), 3 * K + 3 * N), dtype=int)
            r_ = np.arange(M)
            for j in range(3):
                A[r_, 3 * O[:, 1] + j] = 1; A[M + r_, 3 * O[:, 1] + j] = 1
                A[r_, 3 * K + 3 * O[:, 0] + j] = 1; A[M + r_, 3 * K + 3 * O[:, 0] + j] = 1
            for j, kn in enumerate(on_no):
                for q in range(3): A[2 * M + 3 * j + q, 3 * kn + q] = 1
            e0 = art(x0); v = x0.copy()
            for _ in range(3):                 # soft L1 benzeri: büyük hatalı görüşün ağırlığı düşer
                v = least_squares(art, v, jac_sparsity=A, loss="linear", x_scale="jac", max_nfev=30, method="trf").x
                W[:] = 1.0; e_ = art(v); h = np.hypot(e_[:M], e_[M:2 * M])
                W[:] = 1.0 / np.sqrt(np.sqrt(1.0 + (h / 3.0) ** 2))
            W[:] = 1.0; e = art(v)
            po = v[:3 * K].reshape(K, 3); pt = v[3 * K:].reshape(N, 3)
            once = np.hypot(e0[:M] * s_aci, e0[M:2 * M] * (s_enlem or s_aci)); sonra = np.hypot(e[:M] * s_aci, e[M:2 * M] * (s_enlem or s_aci))
            if yazdir:
                kay = [math.hypot(po[k["no"], 0] - k["x"], po[k["no"], 1] - k["z"]) for k in self.kareler]
                kyon = [abs(da(math.degrees(po[k["no"], 2] - k["yaw"]))) for k in self.kareler]
                print("  demet ayari tur %d: %d kare, %d nokta, %d gorus; aci hatasi ortanca %.2f -> %.2f, %%90 %.2f -> %.2f der; kareler en cok %.2f m / %.1f der oynadi; isaret kareleri onselden ortanca %.1f der" % (
                    t_ + 1, K, N, M, np.median(once), np.median(sonra), np.percentile(once, 90), np.percentile(sonra, 90), max(kay), max(kyon), np.median([abs(da(math.degrees(po[k["no"], 2] - k["onsel_poz"][2]))) for k in onc]) if onc else 0), flush=True)
            for k in self.kareler:
                k["x"], k["z"], k["yaw"] = float(po[k["no"], 0]), float(po[k["no"], 1]), float(po[k["no"], 2])
            self.Pn = [pt[i].copy() for i in range(N)]
            obs = [o for o, h in zip(obs, sonra) if h < 3.0]
        tut = {(kn, idx) for _, kn, idx in obs}
        for k in self.kareler:
            k["P"] = {idx: no for idx, no in k["P"].items() if (k["no"], idx) in tut}
