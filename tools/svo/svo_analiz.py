#!/usr/bin/env python3
# svo_analiz.py cikar KLASOR  (konteyner icinde): cikti.bag -> yol.txt (TUM), bilgi.csv, ozet
# svo_analiz.py ciz KAYIT_YOLU ETIKET (Windows/host): ust gorunus PNG, stella ile Umeyama sim3 hizalama
import sys, os, csv, numpy as np


def cikar(k):
    """cikti.bag -> segmentli yol dosyalari. SVO her sifirlamada (yeniden baslatma) yeni dunya orijini ve
    yeni olcek alir; asama kTracking/kRelocalization -> kInitializing/kPaused gecisi = yeni segment."""
    import rosbag
    poz, bilgi, lc_say, seg, onceki = [], [], {}, 0, 0
    with rosbag.Bag(os.path.join(k, "cikti.bag")) as b:
        for tp, m, t in b.read_messages():
            if tp.endswith("pose_cam/0"):
                p, q = m.pose.position, m.pose.orientation
                poz.append((seg, m.header.stamp.to_sec(), p.x, p.y, p.z, q.x, q.y, q.z, q.w))
            elif tp.endswith("info"):
                bilgi.append((m.header.stamp.to_sec(), m.processing_time, m.stage, m.tracking_quality, m.num_matches, seg))
                if onceki in (2, 3) and m.stage in (0, 1):
                    seg += 1
                onceki = m.stage
            elif tp.endswith("loop_closures"):
                lc_say[(m.ns, len(m.points) > 0)] = lc_say.get((m.ns, len(m.points) > 0), 0) + 1
    P = np.array(poz) if poz else np.zeros((0, 9))
    s = ["poz sayisi: %d (segment sayisi %d)" % (len(P), len(set(P[:, 0])) if len(P) else 0),
         "info mesaji: %d" % len(bilgi), "dongu kapatma marker sayilari (ns, noktali mi): %s  [loop_detection=kirmizi tanima, loop_correction=yesil gercek duzeltme]" % lc_say]
    if len(P):
        en = max(set(P[:, 0]), key=lambda g: (P[:, 0] == g).sum())
        for g in sorted(set(P[:, 0])):
            Q = P[P[:, 0] == g]
            np.savetxt(os.path.join(k, "yol_seg%d.txt" % g), Q[:, 1:], fmt="%.6f")
            s.append("  segment %d: %d poz, t %.1f-%.1f s (video zamani), yol %.3f birim" % (
                g, len(Q), Q[0, 1], Q[-1, 1], float(np.linalg.norm(np.diff(Q[:, 2:5], axis=0), axis=1).sum())))
        Q = P[P[:, 0] == en]
        np.savetxt(os.path.join(k, "yol.txt"), Q[:, 1:], fmt="%.6f")
        s.append("yol.txt = en uzun segment (%d)" % en)
    with open(os.path.join(k, "bilgi.csv"), "w") as f:
        f.write("t,isleme_sn,asama,takip_kalitesi,eslesme,segment\n")
        for r in bilgi:
            f.write(",".join(str(x) for x in r) + "\n")
    if bilgi:
        B = np.array(bilgi)
        tr = B[(B[:, 2] == 2) & (B[:, 1] > 0)]
        if len(tr):
            s.append("isleme ms/kare (yalniz kTracking kareleri): ort %.1f, medyan %.1f, p95 %.1f, maks %.1f" % tuple(
                1000 * x for x in (tr[:, 1].mean(), np.median(tr[:, 1]), np.percentile(tr[:, 1], 95), tr[:, 1].max())))
        s.append("asama kare sayilari (0 paused,1 init,2 tracking,3 relocalization): %s" % {
            int(a): int(c) for a, c in zip(*np.unique(B[:, 2], return_counts=True))})
        s.append("sifirlama (reset) sayisi: %d" % int(B[:, 5].max()))
    open(os.path.join(k, "ozet.txt"), "w").write("\n".join(s) + "\n")
    print("\n".join(s))


def umeyama(src, dst):
    """sim3: dst ~ s R src + t (Umeyama 1991)."""
    mu_s, mu_d = src.mean(0), dst.mean(0)
    S, D = src - mu_s, dst - mu_d
    C = D.T @ S / len(src)
    U, d, Vt = np.linalg.svd(C)
    Sg = np.eye(3)
    if np.linalg.det(U) * np.linalg.det(Vt) < 0:
        Sg[2, 2] = -1
    R = U @ Sg @ Vt
    s = (d * np.diag(Sg)).sum() / (S ** 2).sum() * len(src)
    t = mu_d - s * R @ mu_s
    return s, R, t


def yol_uzunlugu(P):
    return float(np.linalg.norm(np.diff(P, axis=0), axis=1).sum())


def ciz(kayit, etiket):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    k = os.path.join(kayit, "svo", etiket)
    yol = np.loadtxt(os.path.join(k, "yol.txt"), ndmin=2)
    out = {}
    if len(yol) < 3:
        print("yol yok / cok kisa:", len(yol)); return
    # yol_ust.png icin ust dizine de yaz
    ts = np.array([float(r["t_ms"]) / 1000 for r in csv.DictReader(open(os.path.join(kayit, "kareler.csv")))])
    idx = np.abs(ts[None, :] - yol[:, 0:1]).argmin(1)  # svo zaman damgasi -> kare indeksi
    st = np.loadtxt(os.path.join(kayit, "harita", "izler", "frame_trajectory.txt"))
    st_idx = np.round(st[:, 0] * 15).astype(int)
    st_map = {i: r for i, r in zip(st_idx, st[:, 1:4])}
    ortak = [(a, st_map[i]) for a, i in zip(yol[:, 1:4], idx) if i in st_map]
    A = np.array([o[0] for o in ortak]); B = np.array([o[1] for o in ortak])
    s, R, t = umeyama(A, B)
    Aa = (s * (R @ A.T)).T + t
    rmse = float(np.sqrt(((Aa - B) ** 2).sum(1).mean()))
    L = yol_uzunlugu(B)
    msg = ("eslesen poz %d / svo %d / stella %d; olcek(svo->stella) %.3f; RMSE %.4f; stella yol uzunlugu %.3f; RMSE/yol = %.1f%%"
           % (len(A), len(yol), len(st), s, rmse, L, 100 * rmse / L))
    print(msg); open(os.path.join(k, "hizalama.txt"), "w").write(msg + "\n")
    # en genis yayilimli duzlem (PCA'nin ilk iki ekseni)
    allp = np.vstack([Aa, B]); u, sv, vt = np.linalg.svd(allp - allp.mean(0), full_matrices=False)
    P = lambda X: (X - allp.mean(0)) @ vt[:2].T
    fig, ax = plt.subplots(1, 2, figsize=(14, 6.5))
    ax[0].plot(*P(B).T, "-", c="tab:blue", label="stella_vslam"); ax[0].plot(*P(Aa).T, "-", c="tab:red", label="SVO Pro (sim3 hizali)")
    ax[0].plot(*P(B)[0], "go"); ax[0].plot(*P(B)[-1], "ks")
    ax[0].set_aspect("equal"); ax[0].legend(); ax[0].grid(alpha=.3)
    ax[0].set_title("%s\nustten (en genis duzlem), RMSE %.3f = %.1f%% yol" % (os.path.basename(kayit), rmse, 100 * rmse / L), fontsize=9)
    Y = yol[:, 1:4]; uy, sy, vy = np.linalg.svd(Y - Y.mean(0), full_matrices=False)
    Q = (Y - Y.mean(0)) @ vy[:2].T
    ax[1].plot(*Q.T, "-", c="tab:red"); ax[1].plot(*Q[0], "go"); ax[1].plot(*Q[-1], "ks")
    ax[1].set_aspect("equal"); ax[1].grid(alpha=.3); ax[1].set_title("SVO Pro ham yol (kendi olcegi), %d poz" % len(Y), fontsize=9)
    fig.tight_layout(); fig.savefig(os.path.join(k, "yol_karsilastirma.png"), dpi=110)
    fig2, a2 = plt.subplots(figsize=(6.5, 6.5))
    a2.plot(*Q.T, "-", c="tab:red"); a2.plot(*Q[0], "go"); a2.plot(*Q[-1], "ks"); a2.set_aspect("equal"); a2.grid(alpha=.3)
    a2.set_title("SVO Pro yol (ust gorunus, en genis duzlem)", fontsize=10)
    fig2.savefig(os.path.join(k, "yol_ust.png"), dpi=110)


if __name__ == "__main__":
    if sys.argv[1] == "cikar":
        cikar(sys.argv[2])
    elif sys.argv[1] == "ciz":
        ciz(sys.argv[2], sys.argv[3])
