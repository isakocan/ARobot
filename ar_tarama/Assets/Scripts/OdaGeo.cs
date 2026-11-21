using System;
using System.Collections.Generic;

namespace ArTarama
{
    /// <summary>2B geometri yardimcilari (saf C#).</summary>
    public static class OdaGeo
    {
        public static double Mod180(double a) { a %= 180.0; if (a < 0) a += 180.0; return a; }
        public static double Cember180(double a, double b) { double d = Math.Abs(Mod180(a) - Mod180(b)); return Math.Min(d, 180.0 - d); }
        /// <summary>a-b farki, [-90, 90) araligina katlanir (180 derece periyot).</summary>
        public static double Fark180(double a, double b) { double d = ((a - b) % 180.0 + 270.0) % 180.0 - 90.0; return d; }
        public static double Fark90(double a, double b) { double d = ((a - b) % 90.0 + 135.0) % 90.0 - 45.0; return d; }

        public static float PolyAlan(float[] sinir)
        {
            int m = sinir.Length / 3; double a = 0;
            for (int i = 0; i < m; i++) { int j = (i + 1) % m; a += sinir[i * 3] * sinir[j * 3 + 2] - sinir[j * 3] * sinir[i * 3 + 2]; }
            return (float)Math.Abs(a * 0.5);
        }

        public static double Alan2D(List<double[]> p)
        {
            double a = 0; for (int i = 0; i < p.Count; i++) { var u = p[i]; var v = p[(i + 1) % p.Count]; a += u[0] * v[1] - v[0] * u[1]; }
            return a * 0.5;
        }

        static double Capraz(double[] o, double[] a, double[] b) { return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]); }

        /// <summary>Bukey zarf (Andrew monoton zinciri). Girdi x,z ciftleri.</summary>
        public static List<double[]> Zarf(List<double[]> p)
        {
            var a = new List<double[]>(p);
            a.Sort((u, v) => u[0] != v[0] ? u[0].CompareTo(v[0]) : u[1].CompareTo(v[1]));
            var t = new List<double[]>();
            foreach (var q in a) if (t.Count == 0 || t[t.Count - 1][0] != q[0] || t[t.Count - 1][1] != q[1]) t.Add(q);
            if (t.Count < 3) return t;
            var h = new List<double[]>();
            for (int i = 0; i < t.Count; i++)
            {
                while (h.Count >= 2 && Capraz(h[h.Count - 2], h[h.Count - 1], t[i]) <= 0) h.RemoveAt(h.Count - 1);
                h.Add(t[i]);
            }
            int alt = h.Count + 1;
            for (int i = t.Count - 2; i >= 0; i--)
            {
                while (h.Count >= alt && Capraz(h[h.Count - 2], h[h.Count - 1], t[i]) <= 0) h.RemoveAt(h.Count - 1);
                h.Add(t[i]);
            }
            h.RemoveAt(h.Count - 1);
            return h;
        }

        /// <summary>Zarfin kenar yonlerini deneyerek en kucuk alanli sinirlayici dikdortgenin acisi (radyan).</summary>
        public static double EnKucukDikdortgenAcisi(List<double[]> h)
        {
            if (h.Count < 2) return 0;
            if (h.Count == 2) return Math.Atan2(h[1][1] - h[0][1], h[1][0] - h[0][0]);
            double enAlan = double.MaxValue, enAci = 0;
            for (int i = 0; i < h.Count; i++)
            {
                var a = h[i]; var b = h[(i + 1) % h.Count];
                double ang = Math.Atan2(b[1] - a[1], b[0] - a[0]);
                Dikdortgen(h, ang, out _, out _, out double lu, out double lv);
                double alan = lu * lv;
                if (alan < enAlan - 1e-12) { enAlan = alan; enAci = ang; }
            }
            return enAci;
        }

        /// <summary>phi yonlu eksenlerde sinirlayici dikdortgen (merkez, u ve v boyu).</summary>
        public static void Dikdortgen(List<double[]> h, double phi, out double cx, out double cz, out double lu, out double lv)
        {
            double c = Math.Cos(phi), s = Math.Sin(phi);
            double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
            foreach (var p in h)
            {
                double u = p[0] * c + p[1] * s, v = -p[0] * s + p[1] * c;
                u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); v0 = Math.Min(v0, v); v1 = Math.Max(v1, v);
            }
            double uc = (u0 + u1) * 0.5, vc = (v0 + v1) * 0.5;
            cx = uc * c - vc * s; cz = uc * s + vc * c;
            lu = u1 - u0; lv = v1 - v0;
        }

        // ---------------- dondurulmus dikdortgen IoU (Sutherland-Hodgman) ----------------
        public static float IoU(OdaKutu a, OdaKutu b)
        {
            var pa = new float[8]; var pb = new float[8]; a.Koseler(pa); b.Koseler(pb);
            double ia = Kesisim(pa, pb);
            double ua = (double)a.sx * a.sz + (double)b.sx * b.sz - ia;
            return ua > 1e-9 ? (float)(ia / ua) : 0f;
        }

        /// <summary>Iki dondurulmus dikdortgenin kesisim alaninin kucuk olanin alanina orani.</summary>
        public static float KucugeOran(OdaKutu a, OdaKutu b)
        {
            var pa = new float[8]; var pb = new float[8]; a.Koseler(pa); b.Koseler(pb);
            double m = Math.Min((double)a.sx * a.sz, (double)b.sx * b.sz);
            return m > 1e-9 ? (float)(Kesisim(pa, pb) / m) : 0f;
        }

        static double Kesisim(float[] a, float[] b)
        {
            var poly = new List<double[]>();
            for (int i = 0; i < 4; i++) poly.Add(new double[] { a[i * 2], a[i * 2 + 1] });
            if (Alan2D(poly) < 0) poly.Reverse();
            var clip = new List<double[]>();
            for (int i = 0; i < 4; i++) clip.Add(new double[] { b[i * 2], b[i * 2 + 1] });
            if (Alan2D(clip) < 0) clip.Reverse();
            for (int e = 0; e < 4 && poly.Count > 0; e++)
            {
                var p1 = clip[e]; var p2 = clip[(e + 1) % 4];
                var cikis = new List<double[]>();
                for (int i = 0; i < poly.Count; i++)
                {
                    var cur = poly[i]; var prev = poly[(i + poly.Count - 1) % poly.Count];
                    bool curIc = Capraz(p1, p2, cur) >= 0, prevIc = Capraz(p1, p2, prev) >= 0;
                    if (curIc) { if (!prevIc) cikis.Add(Kes(prev, cur, p1, p2)); cikis.Add(cur); }
                    else if (prevIc) cikis.Add(Kes(prev, cur, p1, p2));
                }
                poly = cikis;
            }
            return poly.Count < 3 ? 0.0 : Math.Abs(Alan2D(poly));
        }

        static double[] Kes(double[] a, double[] b, double[] p1, double[] p2)
        {
            double dx = b[0] - a[0], dz = b[1] - a[1], ex = p2[0] - p1[0], ez = p2[1] - p1[1];
            double den = dx * ez - dz * ex;
            if (Math.Abs(den) < 1e-12) return a;
            double t = ((p1[0] - a[0]) * ez - (p1[1] - a[1]) * ex) / den;
            return new[] { a[0] + dx * t, a[1] + dz * t };
        }

        /// <summary>Noktanin dogru parcasina uzakligi.</summary>
        public static double ParcaMesafe(double ax, double az, double bx, double bz, double x, double z)
        {
            double abx = bx - ax, abz = bz - az, l2 = abx * abx + abz * abz;
            double t = l2 > 1e-9 ? ((x - ax) * abx + (z - az) * abz) / l2 : 0.0;
            t = Math.Max(0.0, Math.Min(1.0, t));
            double px = ax + abx * t, pz = az + abz * t;
            return Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
        }

        /// <summary>Isin (ox,oz,dx,dz) ile parcanin kesisimi: isin parametresi (m) veya -1.</summary>
        public static double IsinParca(double ox, double oz, double dx, double dz, double ax, double az, double bx, double bz)
        {
            double ex = bx - ax, ez = bz - az;
            double den = dx * ez - dz * ex;
            if (Math.Abs(den) < 1e-9) return -1;
            double t = ((ax - ox) * ez - (az - oz) * ex) / den;
            double u = ((ax - ox) * dz - (az - oz) * dx) / den;
            return t > 0 && u >= 0 && u <= 1 ? t : -1;
        }

        /// <summary>Basit coklu gen (konkav olabilir) icin kulak kesme ucgenleme; indeks uclulerini dondurur.</summary>
        public static List<int> Ucgenle(List<double[]> p)
        {
            var idx = new List<int>(); int n = p.Count;
            if (n < 3) return idx;
            var v = new List<int>(); for (int i = 0; i < n; i++) v.Add(i);
            if (Alan2D(p) < 0) v.Reverse();
            int koruma = 0;
            while (v.Count > 3 && koruma++ < 10000)
            {
                bool kesti = false;
                for (int i = 0; i < v.Count; i++)
                {
                    int a = v[(i + v.Count - 1) % v.Count], b = v[i], c = v[(i + 1) % v.Count];
                    if (Capraz(p[a], p[b], p[c]) <= 1e-9) continue;
                    bool ic = false;
                    for (int j = 0; j < v.Count; j++)
                    {
                        int k = v[j]; if (k == a || k == b || k == c) continue;
                        if (Capraz(p[a], p[b], p[k]) >= 0 && Capraz(p[b], p[c], p[k]) >= 0 && Capraz(p[c], p[a], p[k]) >= 0) { ic = true; break; }
                    }
                    if (ic) continue;
                    idx.Add(a); idx.Add(b); idx.Add(c); v.RemoveAt(i); kesti = true; break;
                }
                if (!kesti) break;
            }
            if (v.Count == 3) { idx.Add(v[0]); idx.Add(v[1]); idx.Add(v[2]); }
            return idx;
        }
    }
}
