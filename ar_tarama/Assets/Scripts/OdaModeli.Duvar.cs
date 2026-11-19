using System;
using System.Collections.Generic;

namespace ArTarama
{
    // Duvarlar: dikey ARCore duzlemleri + doluluk kolonlarindan (>= 1,2 m dikey yayilim) 2B dogru adaylari,
    // RANSAC + en kucuk kareler, birlestirme, Manhattan duzenleme, kose kesisimi ve aday -> kesin durum makinesi.
    public partial class OdaModeli
    {
        class Aday
        {
            public double ax, az, bx, bz; public int hucre, bin; public float son, planAlan; public bool occ;
            public double Uz { get { return Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az)); } }
        }

        class DuvarDurum
        {
            public int id;
            public double ax, az, bx, bz;            // ham (yumusatilmis) uclar
            public bool kesin; public float seen, kesinZaman, ilerleme; public int bin, kayip; public float planAlan; public bool occ;
            // turetilmis (her guncellemede)
            public double dax, daz, dbx, dbz; public bool aK, bK; public double nx, nz;
            public double Uz { get { return Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az)); } }
        }

        readonly List<DuvarDurum> duvarDurum = new List<DuvarDurum>();
        double serbestMx, serbestMz; bool serbestVar;

        static void Yon(double ax, double az, double bx, double bz, out double dx, out double dz)
        {
            double l = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az)); if (l < 1e-9) { dx = 1; dz = 0; return; }
            dx = (bx - ax) / l; dz = (bz - az) / l;
        }

        // --------------------------------------------------------------------------------- aday uretimi
        List<Aday> OccAdaylari(Dictionary<long, Hucre> hucre)
        {
            var px = new List<double>(); var pz = new List<double>(); var pb = new List<int>(); var ps = new List<ushort>();
            int span = (int)Math.Round(ayar.DuvarSpan / KanitIzgarasi.V);
            foreach (var kv in hucre)
            {
                var h = kv.Value;
                if (h.maxIy - h.minIy < span) continue;
                int ix = (int)((kv.Key >> 21) & 0x1FFFFF) - 1048576, iz = (int)(kv.Key & 0x1FFFFF) - 1048576;
                px.Add((ix + 0.5) * KanitIzgarasi.V); pz.Add((iz + 0.5) * KanitIzgarasi.V); pb.Add(h.bin); ps.Add(h.son);
            }
            var sonuc = new List<Aday>();
            int n = px.Count; if (n < 12) return sonuc;
            var kul = new bool[n]; int kalan = n;
            for (int tur = 0; tur < 10 && kalan >= 12; tur++)
            {
                int[] idx = new int[kalan]; int m = 0; for (int i = 0; i < n; i++) if (!kul[i]) idx[m++] = i;
                int enIyi = 0; double eax = 0, eaz = 0, enx = 0, enz = 0;
                for (int it = 0; it < 160; it++)
                {
                    int a = idx[rnd.Next(m)], b = idx[rnd.Next(m)]; if (a == b) continue;
                    double dx = px[b] - px[a], dz = pz[b] - pz[a], l = Math.Sqrt(dx * dx + dz * dz); if (l < 0.3) continue;
                    double nx = -dz / l, nz = dx / l; int say = 0;
                    for (int q = 0; q < m; q++) { int i = idx[q]; if (Math.Abs((px[i] - px[a]) * nx + (pz[i] - pz[a]) * nz) < 0.07) say++; }
                    if (say > enIyi) { enIyi = say; eax = px[a]; eaz = pz[a]; enx = nx; enz = nz; }
                }
                if (enIyi < 12) break;
                // inlier -> PCA -> yeniden inlier
                double mx = eax, mz = eaz, dirx = -enz, dirz = enx;
                for (int pas = 0; pas < 2; pas++)
                {
                    double sx = 0, sz = 0; int c = 0; double nxx = -dirz, nzz = dirx;
                    for (int q = 0; q < m; q++) { int i = idx[q]; if (Math.Abs((px[i] - mx) * nxx + (pz[i] - mz) * nzz) < 0.07) { sx += px[i]; sz += pz[i]; c++; } }
                    if (c < 6) break;
                    sx /= c; sz /= c; double cxx = 0, cxz = 0, czz = 0;
                    for (int q = 0; q < m; q++) { int i = idx[q]; if (Math.Abs((px[i] - mx) * nxx + (pz[i] - mz) * nzz) < 0.07) { double u = px[i] - sx, v = pz[i] - sz; cxx += u * u; cxz += u * v; czz += v * v; } }
                    double th = 0.5 * Math.Atan2(2 * cxz, cxx - czz); mx = sx; mz = sz; dirx = Math.Cos(th); dirz = Math.Sin(th);
                }
                double nx2 = -dirz, nz2 = dirx;
                var ins = new List<int>();
                for (int q = 0; q < m; q++) { int i = idx[q]; if (Math.Abs((px[i] - mx) * nx2 + (pz[i] - mz) * nz2) < 0.07) ins.Add(i); }
                if (ins.Count < 12) { foreach (int i in ins) { kul[i] = true; kalan--; } continue; }
                ins.Sort((a, b) => ((px[a] - mx) * dirx + (pz[a] - mz) * dirz).CompareTo((px[b] - mx) * dirx + (pz[b] - mz) * dirz));
                int bas = 0;
                for (int q = 1; q <= ins.Count; q++)
                {
                    bool kes = q == ins.Count || ((px[ins[q]] - mx) * dirx + (pz[ins[q]] - mz) * dirz) - ((px[ins[q - 1]] - mx) * dirx + (pz[ins[q - 1]] - mz) * dirz) > 0.4;
                    if (!kes) continue;
                    int say = q - bas;
                    double t0 = (px[ins[bas]] - mx) * dirx + (pz[ins[bas]] - mz) * dirz, t1 = (px[ins[q - 1]] - mx) * dirx + (pz[ins[q - 1]] - mz) * dirz;
                    if (say >= 10 && t1 - t0 >= 0.5)
                    {
                        var ad = new Aday { ax = mx + dirx * t0, az = mz + dirz * t0, bx = mx + dirx * t1, bz = mz + dirz * t1, hucre = say, occ = true };
                        for (int r = bas; r < q; r++) { ad.bin |= pb[ins[r]]; if (ps[ins[r]] * 0.1f > ad.son) ad.son = ps[ins[r]] * 0.1f; }
                        sonuc.Add(ad);
                    }
                    bas = q;
                }
                foreach (int i in ins) { kul[i] = true; kalan--; }
            }
            return sonuc;
        }

        List<Aday> PlanAdaylari(IList<OdaDuzlem> planlar)
        {
            var l = new List<Aday>();
            foreach (var p in planlar)
            {
                if (p.tur != 2 || p.sinir == null || p.sinir.Length < 9) continue;
                double nl = Math.Sqrt((double)p.nx * p.nx + (double)p.nz * p.nz); if (nl < 0.5) continue;
                double nx = p.nx / nl, nz = p.nz / nl, dx = -nz, dz = nx;
                double tmin = double.MaxValue, tmax = double.MinValue, c = 0, ymin = double.MaxValue, ymax = double.MinValue; int m = p.sinir.Length / 3;
                for (int i = 0; i < m; i++)
                {
                    double x = p.sinir[i * 3], y = p.sinir[i * 3 + 1], z = p.sinir[i * 3 + 2];
                    double t = x * dx + z * dz; tmin = Math.Min(tmin, t); tmax = Math.Max(tmax, t);
                    c += x * nx + z * nz; ymin = Math.Min(ymin, y); ymax = Math.Max(ymax, y);
                }
                c /= m;
                if (ymax - zy < 0.4 || ymin - zy > 1.5 || tmax - tmin < 0.3) continue;
                l.Add(new Aday { ax = nx * c + dx * tmin, az = nz * c + dz * tmin, bx = nx * c + dx * tmax, bz = nz * c + dz * tmax, planAlan = (float)((tmax - tmin) * (ymax - ymin)), son = float.MaxValue });
            }
            return l;
        }

        /// <summary>Iki parcanin ayni dogruda olup olmadigi: aci, dik ofset ve eksen bosluk. bosluk negatifse ust uste biner.</summary>
        static bool Eslesir(double aax, double aaz, double abx, double abz, double bax, double baz, double bbx, double bbz, double aciDeg, double ofset, double maxBosluk, out double bosluk)
        {
            bosluk = double.MaxValue;
            Yon(aax, aaz, abx, abz, out double d1x, out double d1z); Yon(bax, baz, bbx, bbz, out double d2x, out double d2z);
            if (Math.Abs(d1x * d2z - d1z * d2x) > Math.Sin(aciDeg * Math.PI / 180.0)) return false;
            double n1x = -d1z, n1z = d1x, n2x = -d2z, n2z = d2x;
            double m1x = (aax + abx) * 0.5, m1z = (aaz + abz) * 0.5, m2x = (bax + bbx) * 0.5, m2z = (baz + bbz) * 0.5;
            double o1 = Math.Abs((m2x - m1x) * n1x + (m2z - m1z) * n1z), o2 = Math.Abs((m1x - m2x) * n2x + (m1z - m2z) * n2z);
            if (Math.Max(o1, o2) > ofset) return false;
            double ta0 = aax * d1x + aaz * d1z, ta1 = abx * d1x + abz * d1z; if (ta0 > ta1) { double t = ta0; ta0 = ta1; ta1 = t; }
            double tb0 = bax * d1x + baz * d1z, tb1 = bbx * d1x + bbz * d1z; if (tb0 > tb1) { double t = tb0; tb0 = tb1; tb1 = t; }
            bosluk = Math.Max(tb0 - ta1, ta0 - tb1);
            return bosluk <= maxBosluk;
        }

        static void Birlestir(List<Aday> l)
        {
            bool degisti = true;
            while (degisti)
            {
                degisti = false;
                for (int i = 0; i < l.Count && !degisti; i++)
                    for (int j = i + 1; j < l.Count && !degisti; j++)
                    {
                        var a = l[i]; var b = l[j];
                        if (!Eslesir(a.ax, a.az, a.bx, a.bz, b.ax, b.az, b.bx, b.bz, 10, 0.10, 0.5, out _)) continue;
                        double wa = a.Uz, wb = b.Uz, w = wa + wb;
                        Yon(a.ax, a.az, a.bx, a.bz, out double d1x, out double d1z); Yon(b.ax, b.az, b.bx, b.bz, out double d2x, out double d2z);
                        if (d1x * d2x + d1z * d2z < 0) { d2x = -d2x; d2z = -d2z; }
                        double dx = d1x * wa + d2x * wb, dz = d1z * wa + d2z * wb, dl = Math.Sqrt(dx * dx + dz * dz); dx /= dl; dz /= dl;
                        double mx = ((a.ax + a.bx) * 0.5 * wa + (b.ax + b.bx) * 0.5 * wb) / w, mz = ((a.az + a.bz) * 0.5 * wa + (b.az + b.bz) * 0.5 * wb) / w;
                        double t0 = double.MaxValue, t1 = double.MinValue;
                        foreach (double t in new[] { (a.ax - mx) * dx + (a.az - mz) * dz, (a.bx - mx) * dx + (a.bz - mz) * dz, (b.ax - mx) * dx + (b.az - mz) * dz, (b.bx - mx) * dx + (b.bz - mz) * dz }) { t0 = Math.Min(t0, t); t1 = Math.Max(t1, t); }
                        l[i] = new Aday { ax = mx + dx * t0, az = mz + dz * t0, bx = mx + dx * t1, bz = mz + dz * t1, hucre = a.hucre + b.hucre, bin = a.bin | b.bin, son = Math.Max(a.son, b.son), planAlan = a.planAlan + b.planAlan, occ = a.occ || b.occ };
                        l.RemoveAt(j); degisti = true;
                    }
            }
        }

        // --------------------------------------------------------------------------------- ana akis
        void DuvarlariGuncelle(OdaSonuc s, IList<OdaDuzlem> planlar, Dictionary<long, Hucre> hucre, float zaman, float dt)
        {
            // serbest hucre agirlik merkezi (oda ici yonu icin yedek)
            serbestVar = false; double sx = 0, sz = 0; int sn = 0;
            foreach (long k in s.zeminSerbest) { sx += ((int)((k >> 21) & 0x1FFFFF) - 1048576 + 0.5) * KanitIzgarasi.V; sz += ((int)(k & 0x1FFFFF) - 1048576 + 0.5) * KanitIzgarasi.V; sn++; }
            if (sn > 0) { serbestMx = sx / sn; serbestMz = sz / sn; serbestVar = true; }

            var adaylar = OccAdaylari(hucre); adaylar.AddRange(PlanAdaylari(planlar));
            Birlestir(adaylar);
            // yalniz plan: yeterince buyuk olmali
            adaylar.RemoveAll(a => !a.occ && (a.planAlan < 1.0f || a.Uz < 1.0));
            adaylar.RemoveAll(a => a.Uz < 0.45);

            // ---- eslestirme ----
            var cift = new List<double[]>();
            for (int i = 0; i < duvarDurum.Count; i++)
                for (int j = 0; j < adaylar.Count; j++)
                {
                    var d = duvarDurum[i]; var a = adaylar[j];
                    if (!Eslesir(d.ax, d.az, d.bx, d.bz, a.ax, a.az, a.bx, a.bz, 12, 0.15, 0.3, out double bosluk)) continue;
                    cift.Add(new[] { -bosluk, i, j });
                }
            cift.Sort((x, y) => y[0].CompareTo(x[0]));
            var dEs = new bool[duvarDurum.Count]; var aEs = new bool[adaylar.Count];
            foreach (var c in cift)
            {
                int i = (int)c[1], j = (int)c[2]; if (dEs[i] || aEs[j]) continue;
                dEs[i] = aEs[j] = true;
                var d = duvarDurum[i]; var a = adaylar[j];
                Yumusat(d, a);
                d.kayip = 0; d.bin |= a.bin; d.planAlan = Math.Max(d.planAlan, a.planAlan); d.occ |= a.occ;
                if (a.son >= zaman - 1.5f) d.seen += dt;
            }
            for (int j = 0; j < adaylar.Count; j++)
            {
                if (aEs[j]) continue;
                var a = adaylar[j];
                var d = new DuvarDurum { id = ++sonDuvarId, ax = a.ax, az = a.az, bx = a.bx, bz = a.bz, bin = a.bin, planAlan = a.planAlan, occ = a.occ, seen = a.son >= zaman - 1.5f ? dt : 0f };
                duvarDurum.Add(d); dEs = Buyut(dEs, true);
            }
            for (int i = duvarDurum.Count - 1; i >= 0; i--)
            {
                if (i < dEs.Length && dEs[i]) continue;
                var d = duvarDurum[i]; d.kayip++;
                if (!d.kesin && d.kayip >= 3) duvarDurum.RemoveAt(i);
                else if (d.kesin && d.kayip >= 2 && DestekBos(d)) duvarDurum.RemoveAt(i);
            }

            // ---- durum makinesi ----
            foreach (var d in duvarDurum)
            {
                double uz = d.Uz; int bn = KanitIzgarasi.BinSay(d.bin);
                float destek = d.planAlan >= ayar.BuyukPlanAlan ? 1f : Math.Min(1f, bn / (float)ayar.DuvarOnayBin);
                d.ilerleme = Math.Min(1f, Math.Min((float)(uz / ayar.MinDuvarUzunluk), Math.Min(d.seen / ayar.DuvarOnaySure, destek)));
                if (!d.kesin && uz >= ayar.MinDuvarUzunluk && d.seen >= ayar.DuvarOnaySure && destek >= 1f) { d.kesin = true; d.kesinZaman = zaman; d.ilerleme = 1f; }
                if (d.kesin) d.ilerleme = 1f;
            }

            Turet();
            float yuk = ty - zy;
            foreach (var d in duvarDurum)
            {
                s.duvarlar.Add(new OdaDuvar
                {
                    id = d.id, kesin = d.kesin, ilerleme = d.ilerleme, kesinZaman = d.kesinZaman,
                    ax = (float)d.dax, az = (float)d.daz, bx = (float)d.dbx, bz = (float)d.dbz, aKose = d.aK, bKose = d.bK, nx = (float)d.nx, nz = (float)d.nz, yukseklik = yuk
                });
            }
            if (duvarDurum.Count > 0)
            {
                double topl = 0, c4 = 0, s4 = 0;
                foreach (var d in duvarDurum)
                {
                    double l = d.Uz; if (l < 0.8) continue;
                    double a = Math.Atan2(d.bz - d.az, d.bx - d.ax); topl += l; c4 += l * Math.Cos(4 * a); s4 += l * Math.Sin(4 * a);
                }
                if (topl > 0) { double dom = Math.Atan2(s4, c4) / 4 * 180 / Math.PI; dom %= 90; if (dom < 0) dom += 90; if (dom > 89.99) dom = 0; s.yonVar = true; s.baskinAci = (float)dom; manhattan = dom; manhattanVar = true; }
                else manhattanVar = false;
            }
            else manhattanVar = false;
        }

        bool manhattanVar; double manhattan;

        static bool[] Buyut(bool[] a, bool deger) { var b = new bool[a.Length + 1]; Array.Copy(a, b, a.Length); b[a.Length] = deger; return b; }

        void Yumusat(DuvarDurum d, Aday a)
        {
            double ax = a.ax, az = a.az, bx = a.bx, bz = a.bz;
            Yon(d.ax, d.az, d.bx, d.bz, out double dx, out double dz); double L = d.Uz; double nx = -dz, nz = dx;
            if ((bx - ax) * dx + (bz - az) * dz < 0) { double t = ax; ax = bx; bx = t; t = az; az = bz; bz = t; }
            double tA = (ax - d.ax) * dx + (az - d.az) * dz, oA = (ax - d.ax) * nx + (az - d.az) * nz;
            double tB = (bx - d.ax) * dx + (bz - d.az) * dz, oB = (bx - d.ax) * nx + (bz - d.az) * nz;
            double t0 = tA < 0 ? tA * 0.7 : tA * 0.15, t1 = tB > L ? L + (tB - L) * 0.7 : L + (tB - L) * 0.15;
            double den = tB - tA;
            Func<double, double> ofs = t => den > 1e-6 ? (oA + (oB - oA) * (t - tA) / den) * 0.4 : (oA + oB) * 0.2;
            double nax = d.ax + dx * t0 + nx * ofs(t0), naz = d.az + dz * t0 + nz * ofs(t0);
            double nbx = d.ax + dx * t1 + nx * ofs(t1), nbz = d.az + dz * t1 + nz * ofs(t1);
            d.ax = nax; d.az = naz; d.bx = nbx; d.bz = nbz;
        }

        /// <summary>Kesin duvarin destegi bosaldiysa (ornegin duvar sanilan perde acildi) true.</summary>
        bool DestekBos(DuvarDurum d)
        {
            double L = d.Uz; if (L < 0.2) return true;
            Yon(d.ax, d.az, d.bx, d.bz, out double dx, out double dz); double nx = -dz, nz = dx;
            int toplam = 0, bos = 0;
            foreach (int hk in new[] { 10, 20 })
            {
                int iy = zk + hk;
                for (double t = 0.1; t < L - 0.1; t += 0.1)
                {
                    double x = d.ax + dx * t, z = d.az + dz * t; toplam++;
                    bool hepsiBos = true;
                    for (int o = -1; o <= 1; o++)
                    {
                        int dur = izgara.Durum(KanitIzgarasi.Ind((float)(x + nx * 0.05 * o)), iy, KanitIzgarasi.Ind((float)(z + nz * 0.05 * o)));
                        if (dur != 1) { hepsiBos = false; break; }
                    }
                    if (hepsiBos) bos++;
                }
            }
            return toplam > 0 && bos >= 0.6 * toplam;
        }

        // --------------------------------------------------------------------------------- manhattan + kose
        void Turet()
        {
            // ana yon (uzunluk agirlikli, 90 derece periyot)
            double topl = 0, c4 = 0, s4 = 0;
            foreach (var d in duvarDurum) { double l = d.Uz; if (l < 0.8) continue; double a = Math.Atan2(d.bz - d.az, d.bx - d.ax); topl += l; c4 += l * Math.Cos(4 * a); s4 += l * Math.Sin(4 * a); }
            bool dv = topl > 0; double dom = dv ? Math.Atan2(s4, c4) / 4 * 180 / Math.PI : 0;
            foreach (var d in duvarDurum)
            {
                d.dax = d.ax; d.daz = d.az; d.dbx = d.bx; d.dbz = d.bz; d.aK = d.bK = false;
                if (!dv) continue;
                double a = Math.Atan2(d.bz - d.az, d.bx - d.ax) * 180 / Math.PI;
                double fark = OdaGeo.Fark90(a, dom);
                if (Math.Abs(fark) >= ayar.ManhattanDuvar) continue;
                double yeni = (a - fark) * Math.PI / 180, mx = (d.ax + d.bx) * 0.5, mz = (d.az + d.bz) * 0.5, h = d.Uz * 0.5;
                d.dax = mx - Math.Cos(yeni) * h; d.daz = mz - Math.Sin(yeni) * h; d.dbx = mx + Math.Cos(yeni) * h; d.dbz = mz + Math.Sin(yeni) * h;
            }
            Koseler();
            foreach (var d in duvarDurum) YonBelirle(d);
        }

        void Koseler()
        {
            int n = duvarDurum.Count; double lim = ayar.KoseMesafe;
            var kullanildi = new bool[n * 2];
            var liste = new List<double[]>();   // maliyet, i, j, tip(0 kose,1 i T'de snaplanir,2 j snaplanir), px, pz
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    var a = duvarDurum[i]; var b = duvarDurum[j];
                    Yon(a.dax, a.daz, a.dbx, a.dbz, out double d1x, out double d1z); Yon(b.dax, b.daz, b.dbx, b.dbz, out double d2x, out double d2z);
                    double cr = d1x * d2z - d1z * d2x;
                    if (Math.Abs(cr) < 0.866) continue;   // aci 60-120 arasinda degil
                    // kesisim: a.A + d1 * t = b.A + d2 * u
                    double t = ((b.dax - a.dax) * d2z - (b.daz - a.daz) * d2x) / cr;
                    double px = a.dax + d1x * t, pz = a.daz + d1z * t;
                    double la = Math.Sqrt((a.dbx - a.dax) * (a.dbx - a.dax) + (a.dbz - a.daz) * (a.dbz - a.daz)), lb = Math.Sqrt((b.dbx - b.dax) * (b.dbx - b.dax) + (b.dbz - b.daz) * (b.dbz - b.daz));
                    double ta = t, tb = (px - b.dax) * d2x + (pz - b.daz) * d2z;
                    double da = Math.Min(Math.Abs(ta), Math.Abs(ta - la)), db = Math.Min(Math.Abs(tb), Math.Abs(tb - lb));
                    bool icA = ta > -0.05 && ta < la + 0.05, icB = tb > -0.05 && tb < lb + 0.05;
                    if (da < lim && db < lim) liste.Add(new[] { da + db, i, j, 0, px, pz });
                    else if (da < lim && icB) liste.Add(new[] { da + 0.2, i, j, 1, px, pz });
                    else if (db < lim && icA) liste.Add(new[] { db + 0.2, i, j, 2, px, pz });
                }
            liste.Sort((x, y) => x[0].CompareTo(y[0]));
            foreach (var c in liste)
            {
                int i = (int)c[1], j = (int)c[2], tip = (int)c[3]; double px = c[4], pz = c[5];
                var a = duvarDurum[i]; var b = duvarDurum[j];
                int ea = -1, eb = -1;
                if (tip == 0 || tip == 1) ea = UcSec(a, px, pz);
                if (tip == 0 || tip == 2) eb = UcSec(b, px, pz);
                if (ea >= 0 && kullanildi[i * 2 + ea]) continue;
                if (eb >= 0 && kullanildi[j * 2 + eb]) continue;
                if (ea >= 0) { kullanildi[i * 2 + ea] = true; if (ea == 0) { a.dax = px; a.daz = pz; a.aK = true; } else { a.dbx = px; a.dbz = pz; a.bK = true; } }
                if (eb >= 0) { kullanildi[j * 2 + eb] = true; if (eb == 0) { b.dax = px; b.daz = pz; b.aK = true; } else { b.dbx = px; b.dbz = pz; b.bK = true; } }
            }
        }

        static int UcSec(DuvarDurum d, double px, double pz)
        {
            double da = (d.dax - px) * (d.dax - px) + (d.daz - pz) * (d.daz - pz), db = (d.dbx - px) * (d.dbx - px) + (d.dbz - pz) * (d.dbz - pz);
            return da <= db ? 0 : 1;
        }

        /// <summary>Normali oda icine cevirir: once bos kanit tarafi, yoksa serbest hucre merkezi.</summary>
        void YonBelirle(DuvarDurum d)
        {
            Yon(d.dax, d.daz, d.dbx, d.dbz, out double dx, out double dz); double nx = -dz, nz = dx;
            double L = Math.Sqrt((d.dbx - d.dax) * (d.dbx - d.dax) + (d.dbz - d.daz) * (d.dbz - d.daz));
            int pos = 0, neg = 0;
            for (int k = 1; k <= 5; k++)
            {
                double t = L * k / 6.0, x = d.dax + dx * t, z = d.daz + dz * t;
                foreach (int hk in new[] { 14, 22 })
                    foreach (double m in new[] { 0.25, 0.4 })
                    {
                        if (izgara.Durum(KanitIzgarasi.Ind((float)(x + nx * m)), zk + hk, KanitIzgarasi.Ind((float)(z + nz * m))) == 1) pos++;
                        if (izgara.Durum(KanitIzgarasi.Ind((float)(x - nx * m)), zk + hk, KanitIzgarasi.Ind((float)(z - nz * m))) == 1) neg++;
                    }
            }
            bool ters;
            if (pos != neg) ters = neg > pos;
            else if (serbestVar) ters = ((serbestMx - (d.dax + d.dbx) * 0.5) * nx + (serbestMz - (d.daz + d.dbz) * 0.5) * nz) < 0;
            else ters = false;
            if (ters) { nx = -nx; nz = -nz; }
            d.nx = nx; d.nz = nz;
        }

        /// <summary>Kesin duvarlari ucuca baglayarak kapali dongu arar; kose noktalarini dondurur (yoksa null).</summary>
        List<double[]> Dongu(List<OdaDuvar> kd)
        {
            int n = kd.Count; var used = new bool[n]; var pts = new List<double[]>();
            double bx = kd[0].bx, bz = kd[0].bz; double sx = kd[0].ax, sz = kd[0].az; used[0] = true;
            pts.Add(new double[] { sx, sz }); pts.Add(new double[] { bx, bz });
            for (int adim = 0; adim < n; adim++)
            {
                if (Math.Abs(bx - sx) < 0.03 && Math.Abs(bz - sz) < 0.03 && pts.Count >= 4) { pts.RemoveAt(pts.Count - 1); return pts; }
                int en = -1; bool ters = false;
                for (int i = 0; i < n; i++)
                {
                    if (used[i]) continue;
                    if (Math.Abs(kd[i].ax - bx) < 0.03 && Math.Abs(kd[i].az - bz) < 0.03) { en = i; ters = false; break; }
                    if (Math.Abs(kd[i].bx - bx) < 0.03 && Math.Abs(kd[i].bz - bz) < 0.03) { en = i; ters = true; break; }
                }
                if (en < 0) return null;
                used[en] = true;
                if (!ters) { bx = kd[en].bx; bz = kd[en].bz; } else { bx = kd[en].ax; bz = kd[en].az; }
                pts.Add(new double[] { bx, bz });
            }
            if (Math.Abs(bx - sx) < 0.03 && Math.Abs(bz - sz) < 0.03 && pts.Count >= 4) { pts.RemoveAt(pts.Count - 1); return pts; }
            return null;
        }
    }
}
