using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

// Saf C# (UnityEngine yok): hem Unity Editor testi (OdaTest.cs) hem de komut satiri koşucusu (tools/tarama/test) kullanir.
namespace ArTarama
{
    // ------------------------------------------------------------------------------------------ PNG yazici
    public class OdaResim
    {
        public readonly int w, h; readonly byte[] px;
        public OdaResim(int w, int h, byte r, byte g, byte b) { this.w = w; this.h = h; px = new byte[w * h * 3]; for (int i = 0; i < w * h; i++) { px[i * 3] = r; px[i * 3 + 1] = g; px[i * 3 + 2] = b; } }
        public void Nokta(int x, int y, byte r, byte g, byte b, float a = 1f)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            int o = (y * w + x) * 3;
            px[o] = (byte)(px[o] * (1 - a) + r * a); px[o + 1] = (byte)(px[o + 1] * (1 - a) + g * a); px[o + 2] = (byte)(px[o + 2] * (1 - a) + b * a);
        }
        public void Disk(double x, double y, double yaricap, byte r, byte g, byte b, float a = 1f)
        {
            int R = (int)Math.Ceiling(yaricap);
            for (int dy = -R; dy <= R; dy++) for (int dx = -R; dx <= R; dx++) if (dx * dx + dy * dy <= yaricap * yaricap) Nokta((int)Math.Round(x) + dx, (int)Math.Round(y) + dy, r, g, b, a);
        }
        public void Cizgi(double x0, double y0, double x1, double y1, double kalinlik, byte r, byte g, byte b, float a = 1f)
        {
            double l = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)); int n = Math.Max(1, (int)(l / Math.Max(0.5, kalinlik * 0.4)));
            for (int i = 0; i <= n; i++) Disk(x0 + (x1 - x0) * i / n, y0 + (y1 - y0) * i / n, kalinlik * 0.5, r, g, b, a);
        }
        public void Kesik(double x0, double y0, double x1, double y1, double kalinlik, byte r, byte g, byte b, float a, double dash)
        {
            double l = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)); int n = Math.Max(1, (int)(l / dash));
            for (int i = 0; i < n; i += 2) Cizgi(x0 + (x1 - x0) * i / n, y0 + (y1 - y0) * i / n, x0 + (x1 - x0) * (i + 1) / n, y0 + (y1 - y0) * (i + 1) / n, kalinlik, r, g, b, a);
        }
        public void Dolgu(double[] xs, double[] ys, byte r, byte g, byte b, float a)
        {
            double mn = double.MaxValue, mx = double.MinValue; foreach (var y in ys) { mn = Math.Min(mn, y); mx = Math.Max(mx, y); }
            for (int y = (int)Math.Floor(mn); y <= (int)Math.Ceiling(mx); y++)
            {
                var kes = new List<double>(); double yy = y + 0.5;
                for (int i = 0; i < xs.Length; i++)
                {
                    int j = (i + 1) % xs.Length;
                    if ((ys[i] <= yy && ys[j] > yy) || (ys[j] <= yy && ys[i] > yy)) kes.Add(xs[i] + (yy - ys[i]) / (ys[j] - ys[i]) * (xs[j] - xs[i]));
                }
                kes.Sort();
                for (int k = 0; k + 1 < kes.Count; k += 2) for (int x = (int)Math.Ceiling(kes[k] - 0.5); x <= (int)Math.Floor(kes[k + 1] - 0.5); x++) Nokta(x, y, r, g, b, a);
            }
        }
        public void Yaz(string yol)
        {
            var ham = new byte[(w * 3 + 1) * h];
            for (int y = 0; y < h; y++) Buffer.BlockCopy(px, y * w * 3, ham, y * (w * 3 + 1) + 1, w * 3);
            byte[] sik;
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x78); ms.WriteByte(0x01);
                using (var ds = new DeflateStream(ms, CompressionMode.Compress, true)) ds.Write(ham, 0, ham.Length);
                uint a = 1, b2 = 0; foreach (byte v in ham) { a = (a + v) % 65521; b2 = (b2 + a) % 65521; }
                uint ad = (b2 << 16) | a; ms.WriteByte((byte)(ad >> 24)); ms.WriteByte((byte)(ad >> 16)); ms.WriteByte((byte)(ad >> 8)); ms.WriteByte((byte)ad);
                sik = ms.ToArray();
            }
            using (var f = new FileStream(yol, FileMode.Create))
            {
                f.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
                var ihdr = new byte[13]; Be(ihdr, 0, (uint)w); Be(ihdr, 4, (uint)h); ihdr[8] = 8; ihdr[9] = 2;
                Parca(f, "IHDR", ihdr); Parca(f, "IDAT", sik); Parca(f, "IEND", new byte[0]);
            }
        }
        static void Be(byte[] d, int o, uint v) { d[o] = (byte)(v >> 24); d[o + 1] = (byte)(v >> 16); d[o + 2] = (byte)(v >> 8); d[o + 3] = (byte)v; }
        static void Parca(Stream f, string tip, byte[] veri)
        {
            var l = new byte[4]; Be(l, 0, (uint)veri.Length); f.Write(l, 0, 4);
            var t = Encoding.ASCII.GetBytes(tip); f.Write(t, 0, 4); f.Write(veri, 0, veri.Length);
            uint crc = 0xFFFFFFFF; foreach (byte b in t) crc = Crc(crc, b); foreach (byte b in veri) crc = Crc(crc, b); crc ^= 0xFFFFFFFF;
            Be(l, 0, crc); f.Write(l, 0, 4);
        }
        static uint[] tablo;
        static uint Crc(uint c, byte b)
        {
            if (tablo == null) { tablo = new uint[256]; for (uint n = 0; n < 256; n++) { uint k = n; for (int i = 0; i < 8; i++) k = (k & 1) != 0 ? 0xEDB88320 ^ (k >> 1) : k >> 1; tablo[n] = k; } }
            return tablo[(c ^ b) & 0xFF] ^ (c >> 8);
        }
    }

    // ------------------------------------------------------------------------------------------ cizim (sonuc -> kusbakisi PNG)
    public static class OdaKusbakisi
    {
        public static void Ciz(string yol, OdaSonuc s, List<float[]> iz, List<float[]> hayalet = null)
        {
            double x0 = 1e9, x1 = -1e9, z0 = 1e9, z1 = -1e9;
            Action<double, double> kap = (x, z) => { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); z0 = Math.Min(z0, z); z1 = Math.Max(z1, z); };
            foreach (var d in s.duvarlar) { kap(d.ax, d.az); kap(d.bx, d.bz); }
            foreach (var p in iz) kap(p[0], p[2]);
            for (int i = 0; i + 1 < s.sinir.Length; i += 2) kap(s.sinir[i], s.sinir[i + 1]);
            if (x0 > x1) { x0 = -1; x1 = 1; z0 = -1; z1 = 1; }
            x0 -= 0.5; x1 += 0.5; z0 -= 0.5; z1 += 0.5;
            const int W = 1000; double olcek = Math.Min((W - 40) / (x1 - x0), (W - 40) / (z1 - z0));
            int Wd = (int)((x1 - x0) * olcek) + 40, Hd = (int)((z1 - z0) * olcek) + 40;
            var im = new OdaResim(Wd, Hd, 24, 27, 32);
            Func<double, double> X = x => 20 + (x - x0) * olcek; Func<double, double> Y = z => Hd - 20 - (z - z0) * olcek;
            for (double g = Math.Ceiling(x0); g <= x1; g += 1) im.Cizgi(X(g), 20, X(g), Hd - 20, 1, 60, 66, 74, 0.8f);
            for (double g = Math.Ceiling(z0); g <= z1; g += 1) im.Cizgi(20, Y(g), Wd - 20, Y(g), 1, 60, 66, 74, 0.8f);
            // kesfedilmis bos zemin
            foreach (long k in s.zeminSerbest)
            {
                int ix = (int)((k >> 21) & 0x1FFFFF) - 1048576, jz = (int)(k & 0x1FFFFF) - 1048576;
                im.Nokta((int)X((ix + 0.5) * 0.05), (int)Y((jz + 0.5) * 0.05), 90, 100, 112, 0.5f);
            }
            if (s.zeminPoligon.Length >= 6)
            {
                int n = s.zeminPoligon.Length / 2; var xs = new double[n]; var ys = new double[n];
                for (int i = 0; i < n; i++) { xs[i] = X(s.zeminPoligon[i * 2]); ys[i] = Y(s.zeminPoligon[i * 2 + 1]); }
                im.Dolgu(xs, ys, 230, 232, 236, 0.10f);
            }
            for (int i = 1; i < iz.Count; i++) im.Cizgi(X(iz[i - 1][0]), Y(iz[i - 1][2]), X(iz[i][0]), Y(iz[i][2]), 1.5, 120, 200, 190, 0.5f);
            var kose = new float[8];
            foreach (var k in s.kutular)
            {
                k.Koseler(kose);
                var xs = new double[4]; var ys = new double[4]; for (int i = 0; i < 4; i++) { xs[i] = X(kose[i * 2]); ys[i] = Y(kose[i * 2 + 1]); }
                if (k.kesin) im.Dolgu(xs, ys, 255, 255, 255, 0.22f);
                for (int i = 0; i < 4; i++) { int j = (i + 1) % 4; if (k.kesin) im.Cizgi(xs[i], ys[i], xs[j], ys[j], 3, 255, 255, 255, 1f); else im.Kesik(xs[i], ys[i], xs[j], ys[j], 1.5, 200, 205, 215, 0.6f, 5); }
            }
            foreach (var d in s.duvarlar)
            {
                if (d.kesin) im.Cizgi(X(d.ax), Y(d.az), X(d.bx), Y(d.bz), 7, 255, 255, 255, 1f);
                else im.Kesik(X(d.ax), Y(d.az), X(d.bx), Y(d.bz), 2.5, 200, 205, 215, 0.7f, 8);
                if (d.kesin) { im.Disk(X(d.ax), Y(d.az), 5, 67, 217, 190, 1f); im.Disk(X(d.bx), Y(d.bz), 5, 67, 217, 190, 1f); }
            }
            if (hayalet != null) foreach (var h in hayalet) im.Disk(X(h[0]), Y(h[1]), 6, 235, 87, 87, 0.9f);
            im.Yaz(yol);
        }
    }

    // ------------------------------------------------------------------------------------------ sentetik oda + derinlik uretimi
    public class SimKutu { public string ad; public double cx, cz, phi, lx, lz, h; public bool kare; }
    public class SimHayalet { public double x0, y0, z0, x1, y1, z1; }

    public class SimOda
    {
        public double W = 4, D = 5, Zemin = 0.30, Tavan = 2.9;
        public List<SimKutu> kutular = new List<SimKutu>();
        public List<SimHayalet> hayaletler = new List<SimHayalet>();

        /// <summary>Isin (yon z=1 yerel olcekli, bu yuzden t = derinlik): en yakin kesisim.</summary>
        public double Vur(double ox, double oy, double oz, double dx, double dy, double dz)
        {
            double t = double.MaxValue;
            if (dx > 1e-9) t = Math.Min(t, (W - ox) / dx); else if (dx < -1e-9) t = Math.Min(t, (0 - ox) / dx);
            if (dy > 1e-9) t = Math.Min(t, (Tavan - oy) / dy); else if (dy < -1e-9) t = Math.Min(t, (Zemin - oy) / dy);
            if (dz > 1e-9) t = Math.Min(t, (D - oz) / dz); else if (dz < -1e-9) t = Math.Min(t, (0 - oz) / dz);
            foreach (var k in kutular)
            {
                double r = k.phi * Math.PI / 180.0, c = Math.Cos(r), s = Math.Sin(r);
                double px = ox - k.cx, pz = oz - k.cz;
                double lox = px * c + pz * s, loz = -px * s + pz * c, ldx = dx * c + dz * s, ldz = -dx * s + dz * c;
                Aralik(lox, ldx, -k.lx / 2, k.lx / 2, out double t0, out double t1);
                Aralik(loz, ldz, -k.lz / 2, k.lz / 2, out double u0, out double u1);
                Aralik(oy, dy, Zemin, Zemin + k.h, out double v0, out double v1);
                double en = Math.Max(t0, Math.Max(u0, v0)), ex = Math.Min(t1, Math.Min(u1, v1));
                if (en <= ex && en > 1e-6 && en < t) t = en;
            }
            foreach (var h in hayaletler)
            {
                Aralik(ox, dx, h.x0, h.x1, out double t0, out double t1); Aralik(oy, dy, h.y0, h.y1, out double u0, out double u1); Aralik(oz, dz, h.z0, h.z1, out double v0, out double v1);
                double en = Math.Max(t0, Math.Max(u0, v0)), ex = Math.Min(t1, Math.Min(u1, v1));
                if (en <= ex && en > 1e-6 && en < t) t = en;
            }
            return t;
        }
        static void Aralik(double o, double d, double a, double b, out double t0, out double t1)
        {
            if (Math.Abs(d) < 1e-12) { if (o >= a && o <= b) { t0 = double.MinValue; t1 = double.MaxValue; } else { t0 = 1; t1 = 0; } return; }
            double u = (a - o) / d, v = (b - o) / d; t0 = Math.Min(u, v); t1 = Math.Max(u, v);
        }

        public bool KutuIcinde(double x, double z, double marj)
        {
            foreach (var k in kutular)
            {
                double r = k.phi * Math.PI / 180.0, c = Math.Cos(r), s = Math.Sin(r);
                double px = x - k.cx, pz = z - k.cz; double u = px * c + pz * s, v = -px * s + pz * c;
                if (Math.Abs(u) < k.lx / 2 + marj && Math.Abs(v) < k.lz / 2 + marj) return true;
            }
            return false;
        }
    }

    public class SimKare { public double x, y, z, yaw, pitch; public float t; }

    public static class OdaSim
    {
        static double Gauss(Random r) { double u = 1.0 - r.NextDouble(), v = r.NextDouble(); return Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2 * Math.PI * v); }
        public const int W = 80, H = 45;
        public const float Fx = 1435.59f, Fy = 1435.53f, Cx = 957.68f, Cy = 545.46f, IW = 1920, IH = 1080;

        public static void Kuat(double yaw, double pitch, out float qx, out float qy, out float qz, out float qw, out double fx, out double fy, out double fz)
        {
            double y = yaw * Math.PI / 180, p = pitch * Math.PI / 180;
            fx = Math.Sin(y) * Math.Cos(p); fy = Math.Sin(p); fz = Math.Cos(y) * Math.Cos(p);
            double rx = fz, rz = -fx, rl = Math.Sqrt(rx * rx + rz * rz); rx /= rl; rz /= rl; double ry = 0;
            // up = cross(fwd, right)
            double ux = fy * rz - fz * ry, uy = fz * rx - fx * rz, uz = fx * ry - fy * rx;
            double r00 = rx, r01 = ux, r02 = fx, r10 = ry, r11 = uy, r12 = fy, r20 = rz, r21 = uz, r22 = fz;
            double tr = r00 + r11 + r22, X, Y, Z, Wq;
            if (tr > 0) { double s = Math.Sqrt(tr + 1) * 2; Wq = 0.25 * s; X = (r21 - r12) / s; Y = (r02 - r20) / s; Z = (r10 - r01) / s; }
            else if (r00 > r11 && r00 > r22) { double s = Math.Sqrt(1 + r00 - r11 - r22) * 2; Wq = (r21 - r12) / s; X = 0.25 * s; Y = (r01 + r10) / s; Z = (r02 + r20) / s; }
            else if (r11 > r22) { double s = Math.Sqrt(1 + r11 - r00 - r22) * 2; Wq = (r02 - r20) / s; X = (r01 + r10) / s; Y = 0.25 * s; Z = (r12 + r21) / s; }
            else { double s = Math.Sqrt(1 + r22 - r00 - r11) * 2; Wq = (r10 - r01) / s; X = (r02 + r20) / s; Y = (r12 + r21) / s; Z = 0.25 * s; }
            qx = (float)X; qy = (float)Y; qz = (float)Z; qw = (float)Wq;
        }

        /// <summary>Sentetik odayi bir kameradan 80x45 derinlik karesine isinlar (gurultu, ucan piksel, aykiri, guven dahil).</summary>
        public static DerinlikKaresi Render(SimOda oda, SimKare k, Random r, bool gurultu = true)
        {
            var f = new DerinlikKaresi { w = W, h = H, d = new float[W * H], g = new byte[W * H], fx = Fx * W / IW, fy = Fy * H / IH, cx = Cx * W / IW, cy = Cy * H / IH, px = (float)k.x, py = (float)k.y, pz = (float)k.z, zaman = k.t };
            Kuat(k.yaw, k.pitch, out f.qx, out f.qy, out f.qz, out f.qw, out _, out _, out _);
            var m = new float[9]; DerinlikKaresi.DonmeMatrisi(f.qx, f.qy, f.qz, f.qw, m);
            for (int v = 0; v < H; v++)
                for (int u = 0; u < W; u++)
                {
                    f.YerelYon(u, v, out float lx, out float ly);
                    double wx = m[0] * lx + m[1] * ly + m[2], wy = m[3] * lx + m[4] * ly + m[5], wz = m[6] * lx + m[7] * ly + m[8];
                    double t = oda.Vur(k.x, k.y, k.z, wx, wy, wz);
                    f.d[v * W + u] = (float)t; f.g[v * W + u] = 220;
                }
            if (!gurultu) return f;
            var temiz = (float[])f.d.Clone();
            for (int v = 0; v < H; v++)
                for (int u = 0; u < W; u++)
                {
                    int i = v * W + u; float t = temiz[i];
                    double sig = 0.005 + 0.008 * t;
                    float d = (float)(t + Gauss(r) * sig);
                    float kom = u + 1 < W ? temiz[i + 1] : t, alt = v + 1 < H ? temiz[i + W] : t;
                    bool kenar = Math.Abs(kom - t) > 0.25f || Math.Abs(alt - t) > 0.25f;
                    if (kenar) { if (r.NextDouble() < 0.35) d = t + (Math.Abs(kom - t) > 0.25f ? kom - t : alt - t) * (float)r.NextDouble(); f.g[i] = (byte)(r.NextDouble() < 0.5 ? 60 : 200); }
                    if (r.NextDouble() < 0.004) d = (float)(0.5 + r.NextDouble() * 3.3);
                    if (d > KanitIzgarasi.MaksMenzil) d = 0;
                    f.d[i] = d;
                }
            return f;
        }

        public static List<OdaDuzlem> Duzlemler(Random r, bool duvarlar, bool tavan, float zeminYanlilik)
        {
            var l = new List<OdaDuzlem>();
            float zy = (float)0.30;
            if (duvarlar)
            {
                float[][] kose = { new[] { 0f, 0f }, new[] { 4f, 0f }, new[] { 4f, 5f }, new[] { 0f, 5f } };
                for (int w = 0; w < 4; w++)
                {
                    var a = kose[w]; var b = kose[(w + 1) % 4];
                    float len = (float)Math.Sqrt((b[0] - a[0]) * (b[0] - a[0]) + (b[1] - a[1]) * (b[1] - a[1]));
                    float[][] parca;
                    if (w == 0) parca = new[] { new[] { 0.15f, 1.9f }, new[] { 2.1f, len - 0.15f }, new[] { 1.0f, 2.5f } };
                    else if (w == 2) parca = new[] { new[] { 0.15f, 1.2f }, new[] { 1.35f, 2.6f }, new[] { 2.7f, len - 0.15f } };
                    else parca = new[] { new[] { 0.15f, len - 0.15f } };
                    foreach (var p in parca) l.Add(DuvarPlani(r, a, b, len, p[0], p[1], zy + (float)(r.NextDouble() * 0.25), zy + 2.0f + (float)(r.NextDouble() * 0.5)));
                }
            }
            l.Add(Yatay(0, 0.1f, 0.1f, 3.9f, 4.9f, zy + zeminYanlilik));
            if (tavan) l.Add(Yatay(1, 0.1f, 0.1f, 3.9f, 4.9f, zy + 2.6f));
            return l;
        }

        static OdaDuzlem Yatay(int tur, float x0, float z0, float x1, float z1, float y)
        {
            return new OdaDuzlem { id = "yatay" + tur, tur = tur, nx = 0, ny = tur == 0 ? 1 : -1, nz = 0, sinir = new[] { x0, y, z0, x1, y, z0, x1, y, z1, x0, y, z1 } };
        }

        static OdaDuzlem DuvarPlani(Random r, float[] a, float[] b, float len, float t0, float t1, float y0, float y1)
        {
            float dx = (b[0] - a[0]) / len, dz = (b[1] - a[1]) / len;
            float nx = -dz, nz = dx;
            float ofs = (float)(Gauss(r) * 0.008), egim = (float)(Gauss(r) * 0.4 * Math.PI / 180.0);
            float cx = a[0] + dx * (t0 + t1) * 0.5f, cz = a[1] + dz * (t0 + t1) * 0.5f;
            var pts = new List<float>();
            float[][] kose = { new[] { t0, y0 }, new[] { t1, y0 }, new[] { t1, y1 }, new[] { t0, y1 } };
            foreach (var k in kose)
            {
                float t = k[0] - (t0 + t1) * 0.5f;
                float ex = t * (float)Math.Cos(egim), ez = t * (float)Math.Sin(egim);
                float x = cx + dx * ex + nx * (ez + ofs), z = cz + dz * ex + nz * (ez + ofs);
                pts.Add(x + (float)(Gauss(r) * 0.005)); pts.Add(k[1]); pts.Add(z + (float)(Gauss(r) * 0.005));
            }
            float sg = r.Next(2) == 0 ? 1f : -1f;
            return new OdaDuzlem { id = "duvar", tur = 2, sinir = pts.ToArray(), nx = nx * sg, ny = 0, nz = nz * sg };
        }

        public static SimOda Oda()
        {
            var o = new SimOda();
            o.kutular.Add(new SimKutu { ad = "masa", cx = 1.503, cz = 2.003, lx = 1.6, lz = 0.8, h = 0.75, phi = 0 });
            o.kutular.Add(new SimKutu { ad = "sandalye1", cx = 1.203, cz = 2.78, lx = 0.45, lz = 0.45, h = 0.9, phi = 0, kare = true });
            o.kutular.Add(new SimKutu { ad = "sandalye2", cx = 1.803, cz = 2.78, lx = 0.45, lz = 0.45, h = 0.9, phi = 0, kare = true });
            o.kutular.Add(new SimKutu { ad = "kanepe", cx = 2.65, cz = 3.97, lx = 2.0, lz = 0.9, h = 0.45, phi = 30 });
            return o;
        }

        /// <summary>Dolasma yolu: konumlar boyunca her konumda yaw taramasi + tavan bakislari. dt = 0.25 s.</summary>
        public static List<SimKare> Yol(SimOda oda, float t0, int baslangicKonum = 0, int konumSayisi = 100)
        {
            double[][] konum = {
                new[] { 0.7, 0.7 }, new[] { 2.0, 0.6 }, new[] { 3.3, 0.7 }, new[] { 3.3, 1.9 }, new[] { 2.9, 2.9 },
                new[] { 0.6, 1.7 }, new[] { 0.6, 3.1 }, new[] { 0.7, 4.3 }, new[] { 2.0, 1.1 }, new[] { 2.2, 3.1 }, new[] { 3.2, 4.4 }, new[] { 1.4, 3.6 }
            };
            var l = new List<SimKare>(); float t = t0;
            for (int ki = baslangicKonum; ki < konum.Length && ki < baslangicKonum + konumSayisi; ki++)
            {
                var p = konum[ki]; if (oda.KutuIcinde(p[0], p[1], 0.25)) continue;
                double y0 = ki * 37 % 360;
                for (int s = 0; s < 24; s++) l.Add(new SimKare { x = p[0], y = oda.Zemin + 1.4, z = p[1], yaw = y0 + s * 15, pitch = -8 + 6 * Math.Sin(s * 0.7), t = t += 0.25f });
                for (int s = 0; s < 8; s++) l.Add(new SimKare { x = p[0], y = oda.Zemin + 1.4, z = p[1], yaw = y0 + s * 45, pitch = 38, t = t += 0.25f });
            }
            return l;
        }
    }

    // ------------------------------------------------------------------------------------------ testler
    public static class OdaSentetik
    {
        static readonly CultureInfo ci = CultureInfo.InvariantCulture;
        static string F(double v, int n = 2) { return v.ToString("F" + n, ci); }

        public class Kosu
        {
            public OdaModeli model; public OdaSonuc son; public List<double> isleMs = new List<double>(), guncMs = new List<double>();
            public Dictionary<int, float> duvarKesin = new Dictionary<int, float>(), kutuKesin = new Dictionary<int, float>();
            public List<float[]> iz = new List<float[]>();
            public List<OdaSonuc> sonuclar = new List<OdaSonuc>();
            public float sonGuncelleme = -10;
        }

        public static Kosu Yeni() { return new Kosu { model = new OdaModeli() }; }

        public static void Besle(Kosu k, DerinlikKaresi f, List<OdaDuzlem> planlar)
        {
            var sw = Stopwatch.StartNew();
            k.model.Isle(f);
            sw.Stop(); k.isleMs.Add(sw.Elapsed.TotalMilliseconds);
            k.iz.Add(new[] { f.px, f.py, f.pz });
            if (f.zaman - k.sonGuncelleme >= 1.0f - 1e-4f)
            {
                k.sonGuncelleme = f.zaman;
                sw.Restart();
                k.son = k.model.Guncelle(planlar, f.zaman);
                sw.Stop(); k.guncMs.Add(sw.Elapsed.TotalMilliseconds);
                k.sonuclar.Add(k.son);
                foreach (var d in k.son.duvarlar) if (d.kesin && !k.duvarKesin.ContainsKey(d.id)) k.duvarKesin[d.id] = f.zaman;
                foreach (var b in k.son.kutular) if (b.kesin && !k.kutuKesin.ContainsKey(b.id)) k.kutuKesin[b.id] = f.zaman;
            }
        }

        public static Kosu Calistir(SimOda oda, List<SimKare> yol, List<OdaDuzlem> planlar, Random r)
        {
            var k = Yeni();
            k.model.KutularAcik = true;   // uygulamada kapali; sentetik testte kutu kodu denetlenmeye devam etsin
            foreach (var kare in yol) Besle(k, OdaSim.Render(oda, kare, r), planlar);
            return k;
        }

        public static string OzetAl(List<double> l)
        {
            if (l.Count == 0) return "-";
            var a = new List<double>(l); a.Sort();
            double top = 0; foreach (var v in a) top += v;
            return "ort " + F(top / a.Count, 2) + " ms, p95 " + F(a[(int)(0.95 * (a.Count - 1))], 2) + " ms, en kotu " + F(a[a.Count - 1], 2) + " ms (n=" + a.Count + ")";
        }

        static float H(float x, float z, float[] k) { return (float)Math.Sqrt((x - k[0]) * (x - k[0]) + (z - k[1]) * (z - k[1])); }

        /// <summary>Sentetik testler (derinlik karelerinden tam boru hatti). Basarisiz sayisini dondurur.</summary>
        public static int Calistir(Action<string> yaz, string ciktiDir = null)
        {
            int hata = 0;
            var r = new Random(12345);
            var oda = OdaSim.Oda();
            var yol = OdaSim.Yol(oda, 0f);
            yaz("yol: " + yol.Count + " kare (" + F(yol.Count * 0.25, 0) + " sn @4 Hz), " + (yol.Count / 32) + " konum x 32 bakis; derinlik 80x45, gurultu sigma = 5 mm + %0,8 mesafe, ucan piksel, %0,4 aykiri");
            float[][] koseler = { new[] { 0f, 0f }, new[] { 4f, 0f }, new[] { 4f, 5f }, new[] { 0f, 5f } };
            float[] beklenenUz = { 4f, 5f, 4f, 5f };

            foreach (int senaryo in new[] { 1, 2 })
            {
                yaz("");
                yaz(senaryo == 1 ? "=== (A) tam boru hatti: derinlik + ARCore duzlemleri (zemin +2 cm yanli, tavan duzlemi var) ===" : "=== (B) yalniz derinlik: yalniz zemin duzlemi (dikey/tavan duzlemi yok) ===");
                var planlar = OdaSim.Duzlemler(new Random(99), senaryo == 1, senaryo == 1, 0.02f);
                var k = Calistir(oda, yol, planlar, new Random(5 + senaryo));
                var s = k.son;
                yaz("zemin y=" + F(s.zeminY, 3) + " (bek. 0.300 + 0.02 yanlilik), tavan y=" + F(s.tavanY, 3) + " (bek. 2.900) gercek=" + s.tavanGercek + ", Manhattan acisi=" + (s.yonVar ? F(s.baskinAci, 1) : "yok") + ", voksel=" + s.voksel);
                if (Math.Abs(s.tavanY - 2.9f) > 0.10f) { yaz("HATA: tavan y"); hata++; }
                if (!s.tavanGercek) { yaz("HATA: tavan bulunamadi (tahmin)"); hata++; }
                int kd = s.KesinDuvar; yaz("duvar: " + s.duvarlar.Count + " (kesin " + kd + ", beklenen 4 kesin)");
                if (kd != 4) { yaz("HATA: kesin duvar sayisi"); hata++; }
                yaz("id | durum | A(x,z) | B(x,z) | uzunluk (bek.) | uzunluk hatasi | kose hatasi | onay zamani");
                var kul = new bool[4];
                foreach (var d in s.duvarlar)
                {
                    int bi = -1; float bf = 1e9f;
                    for (int w = 0; w < 4; w++)
                    {
                        float mx = (koseler[w][0] + koseler[(w + 1) % 4][0]) / 2, mz = (koseler[w][1] + koseler[(w + 1) % 4][1]) / 2;
                        float f = Math.Abs((d.ax + d.bx) / 2 - mx) + Math.Abs((d.az + d.bz) / 2 - mz);
                        if (f < bf) { bf = f; bi = w; }
                    }
                    float kb = 0; foreach (float e in new[] { H4(d.ax, d.az, koseler), H4(d.bx, d.bz, koseler) }) kb = Math.Max(kb, e);
                    float uh = Math.Abs(d.Uzunluk - beklenenUz[bi]);
                    k.duvarKesin.TryGetValue(d.id, out float oz);
                    yaz(d.id + " | " + (d.kesin ? "kesin" : "aday") + " | (" + F(d.ax) + "," + F(d.az) + ") | (" + F(d.bx) + "," + F(d.bz) + ") | " + F(d.Uzunluk, 3) + " (" + F(beklenenUz[bi], 1) + ") | " + F(uh, 3) + " m | " + F(kb, 3) + " m | " + (d.kesin ? F(oz, 0) + " s" : "-"));
                    if (!d.kesin) { yaz("HATA: aday duvar kalmis"); hata++; continue; }
                    if (uh > 0.05f) { yaz("HATA: duvar " + d.id + " uzunluk > 5 cm"); hata++; }
                    kul[bi] = true;
                    if ((2f - (d.ax + d.bx) / 2) * d.nx + (2.5f - (d.az + d.bz) / 2) * d.nz <= 0) { yaz("HATA: normal disa bakiyor"); hata++; }
                }
                foreach (var b in kul) if (!b) { yaz("HATA: bir duvar yok"); hata++; break; }
                yaz("alan: " + F(s.alan, 2) + " m2 (bek. 20.00), dongu=" + s.dongu);
                if (Math.Abs(s.alan - 20f) > 0.6f) { yaz("HATA: alan"); hata++; }

                yaz("");
                yaz("esya kutulari: kesin " + s.KesinKutu + ", aday " + (s.kutular.Count - s.KesinKutu) + " (beklenen 4 kesin, 0 aday)");
                yaz("ad | beklenen (uzun x kisa x yuk, aci) | olculen | boyut hatasi (max) | aci hatasi | merkez hatasi | onay zamani");
                var kutuKul = new bool[s.kutular.Count];
                foreach (var e in oda.kutular)
                {
                    int bi = -1; float bd = 1e9f;
                    for (int i = 0; i < s.kutular.Count; i++)
                    {
                        float d = (float)Math.Sqrt((s.kutular[i].cx - e.cx) * (s.kutular[i].cx - e.cx) + (s.kutular[i].cz - e.cz) * (s.kutular[i].cz - e.cz));
                        if (d < bd) { bd = d; bi = i; }
                    }
                    if (bi < 0 || bd > 0.3f) { yaz(e.ad + " | KUTU YOK"); hata++; continue; }
                    kutuKul[bi] = true; var kt = s.kutular[bi];
                    double ex = Math.Max(e.lx, e.lz), ez = Math.Min(e.lx, e.lz);
                    double hx = Math.Abs(kt.sx - ex), hz = Math.Abs(kt.sz - ez), hy = Math.Abs(kt.sy - e.h);
                    double dac = Math.Abs(kt.aci - (e.kare ? 0 : e.phi)); dac %= (e.kare ? 90.0 : 180.0); dac = Math.Min(dac, (e.kare ? 90.0 : 180.0) - dac);
                    k.kutuKesin.TryGetValue(kt.id, out float oz);
                    yaz(e.ad + " | " + F(ex) + "x" + F(ez) + "x" + F(e.h) + " @" + F(e.phi, 0) + " | " + F(kt.sx) + "x" + F(kt.sz) + "x" + F(kt.sy) + " @" + F(kt.aci, 1) + (kt.kesin ? "" : " (ADAY)") + " | " + F(Math.Max(hx, Math.Max(hz, hy)), 3) + " m (x" + F(kt.sx - ex, 3) + " z" + F(kt.sz - ez, 3) + " y" + F(kt.sy - e.h, 3) + ") | " + F(dac, 1) + " deg | " + F(bd, 3) + " m | " + (kt.kesin ? F(oz, 0) + " s" : "-"));
                    if (!kt.kesin) { yaz("HATA: " + e.ad + " kesinlesmedi"); hata++; }
                    if (hx > 0.10 || hz > 0.10 || hy > 0.10) { yaz("HATA: " + e.ad + " boyut > 10 cm"); hata++; }
                    if (dac > 5.0) { yaz("HATA: " + e.ad + " aci"); hata++; }
                }
                for (int i = 0; i < kutuKul.Length; i++) if (!kutuKul[i]) { yaz("HATA: eslesmeyen (hayalet/fazla) kutu id=" + s.kutular[i].id + " " + (s.kutular[i].kesin ? "kesin" : "aday") + " merkez=(" + F(s.kutular[i].cx) + "," + F(s.kutular[i].cz) + ") boyut=" + F(s.kutular[i].sx) + "x" + F(s.kutular[i].sz) + "x" + F(s.kutular[i].sy)); hata++; }

                // sure
                yaz("");
                yaz("sure: derinlik karesi butunlestirme " + OzetAl(k.isleMs));
                yaz("      yapi guncellemesi (1 Hz)    " + OzetAl(k.guncMs));
                yaz("      izgara: " + k.model.izgara.VokselSayisi + " voksel, kapasite " + k.model.izgara.Kapasite);
                if (ciktiDir != null) { Directory.CreateDirectory(ciktiDir); OdaKusbakisi.Ciz(Path.Combine(ciktiDir, senaryo == 1 ? "sentetik_A.png" : "sentetik_B.png"), s, k.iz); File.WriteAllText(Path.Combine(ciktiDir, senaryo == 1 ? "sentetik_A_oda.json" : "sentetik_B_oda.json"), OdaJson.Yaz(s, null), new UTF8Encoding(false)); }

                if (senaryo == 1)
                {
                    // ---- acik zemin sorgusu ----
                    yaz("");
                    yaz("=== (C) acik zemin sorgusu (kristal yerlestirme) ===");
                    string[] ad = { "masanin ustu (1.5,2.0)", "masa kenarindan 15 cm (1.5,2.55)", "bos zemin (3.2,1.0)", "duvardan 10 cm (0.1,2.0)", "bos zemin (3.0,0.8)", "kanepe ici (2.65,3.97)" };
                    float[][] pt = { new[] { 1.5f, 2.0f }, new[] { 1.5f, 2.55f }, new[] { 3.2f, 1.0f }, new[] { 0.1f, 2.0f }, new[] { 3.0f, 0.8f }, new[] { 2.65f, 3.97f } };
                    bool[] bek = { false, false, true, false, true, false };
                    for (int i = 0; i < pt.Length; i++)
                    {
                        bool o = s.AcikZeminMi(pt[i][0], pt[i][1]);
                        yaz(ad[i] + ": " + o + " (beklenen " + bek[i] + ")"); if (o != bek[i]) { yaz("HATA: acik zemin"); hata++; }
                    }
                    // ---- json gidis-donus ----
                    yaz("");
                    yaz("=== (D) oda.json gidis-donus ===");
                    string js = OdaJson.Yaz(s, null, "Test"); var s2 = OdaJson.Oku(js, out string adOku);
                    yaz("yazildi " + js.Length + " bayt; okunan: ad=" + adOku + " duvar=" + s2.duvarlar.Count + " (kesin " + s2.KesinDuvar + ") kutu=" + s2.kutular.Count + " (kesin " + s2.KesinKutu + ") tavan_tahmin=" + s2.TavanTahmin + " alan=" + F(s2.alan));
                    if (s2.duvarlar.Count != s.duvarlar.Count || s2.kutular.Count != s.kutular.Count || s2.KesinDuvar != s.KesinDuvar || s2.KesinKutu != s.KesinKutu) { yaz("HATA: json gidis-donus"); hata++; }

                    // ---- hayalet testleri ----
                    hata += Hayalet(yaz, oda, yol, planlar, r, ciktiDir);
                }
            }
            hata += KocTesti(yaz);
            yaz("");
            yaz(hata == 0 ? "SONUC: TUM SENTETIK TESTLER GECTI" : "SONUC: " + hata + " HATA");
            return hata;
        }

        // ------------------------------------------------------------------------------------ hayalet testleri
        static int Hayalet(Action<string> yaz, SimOda oda, List<SimKare> yol, List<OdaDuzlem> planlar, Random r, string ciktiDir)
        {
            int hata = 0;
            yaz("");
            yaz("=== (E) hayalet testleri ===");
            // E1/E2: tek karede kopan hayalet (havada 30 cm kup ve zeminde 30 cm kup)
            double[][] bl = { new[] { 2.0, 1.45, 1.0 }, new[] { 3.0, 0.45, 1.3 } };   // merkez x, y, z (havada / zeminde)
            string[] bad = { "havada 30 cm kup (2.0, 1.45, 1.0)", "zeminde 30 cm kup (3.0, 0.45, 1.3)" };
            for (int bi = 0; bi < 2; bi++)
            {
                var k = Yeni(); int enYuksek = 0; double[] c = bl[bi];
                var hy = new SimHayalet { x0 = c[0] - 0.15, x1 = c[0] + 0.15, y0 = c[1] - 0.15, y1 = c[1] + 0.15, z0 = c[2] - 0.15, z1 = c[2] + 0.15 };
                int enjek = 90;   // kare indeksi
                for (int i = 0; i < yol.Count; i++)
                {
                    var kare = yol[i]; oda.hayaletler.Clear();
                    if (i == enjek)
                    {
                        // kamerayi hayalete bakacak sekilde yeniden yonlendir (tek kare)
                        var ky = new SimKare { x = 1.0, y = 1.7, z = 0.6, yaw = Math.Atan2(c[0] - 1.0, c[2] - 0.6) * 180 / Math.PI, pitch = -5, t = kare.t };
                        oda.hayaletler.Add(hy); Besle(k, OdaSim.Render(oda, ky, r), planlar); oda.hayaletler.Clear();
                    }
                    Besle(k, OdaSim.Render(oda, kare, r), planlar);
                    if (k.son != null) foreach (var kt in k.son.kutular) if (Math.Sqrt((kt.cx - c[0]) * (kt.cx - c[0]) + (kt.cz - c[2]) * (kt.cz - c[2])) < 0.35 && !IsMobilya(oda, kt)) enYuksek = Math.Max(enYuksek, kt.kesin ? 2 : 1);
                }
                string durum = enYuksek == 0 ? "hic olusmadi" : enYuksek == 1 ? "ADAY oldu" : "KESIN oldu";
                yaz("E" + (bi + 1) + " tek karelik hayalet, " + bad[bi] + ": " + durum + " (beklenen: kesin olmamali)");
                if (enYuksek == 2) { yaz("HATA: hayalet kesin kutu oldu"); hata++; }
            }
            // E3: iki bakis yonunden uc kare (aday olur), sonra icinden bakilinca kaybolur
            {
                var k = Yeni(); double[] c = { 2.0, 1.45, 1.0 };
                var hy = new SimHayalet { x0 = c[0] - 0.15, x1 = c[0] + 0.15, y0 = c[1] - 0.15, y1 = c[1] + 0.15, z0 = c[2] - 0.15, z1 = c[2] + 0.15 };
                int kesim = 120; int i = 0; float t = 0;
                for (; i < kesim; i++) { Besle(k, OdaSim.Render(oda, yol[i], r), planlar); t = yol[i].t; }
                // hayalet kareleri: 4 kare +x yonunden, 4 kare -x yonunden (farkli yaw dilimleri), 1 Hz araliklarla guncelleme
                double[][] kamlar = { new[] { 0.8, 0.5 }, new[] { 3.4, 0.9 } };
                int gorulduAday = 0; int enSonDurum = 0;
                oda.hayaletler.Add(hy);
                for (int g = 0; g < 2; g++)
                    for (int s = 0; s < 4; s++)
                    {
                        t += 0.25f; var ky = HedefeBak(kamlar[g][0], 2.3, kamlar[g][1], c, (s - 1.5) * 3, t);
                        Besle(k, OdaSim.Render(oda, ky, r), planlar);
                    }
                oda.hayaletler.Clear();
                // iki guncelleme (kararlilik icin yetmez) icin zaman ilerlet: ayni konumdan hayalet kare olmadan degil, hayaletli kareler tekrar
                oda.hayaletler.Add(hy);
                for (int g = 0; g < 2; g++) for (int s = 0; s < 4; s++) { t += 0.25f; var ky = HedefeBak(kamlar[g][0], 2.3, kamlar[g][1], c, (s - 1.5) * 3, t); Besle(k, OdaSim.Render(oda, ky, r), planlar); }
                oda.hayaletler.Clear();
                foreach (var kt in k.son.kutular) if (Math.Sqrt((kt.cx - c[0]) * (kt.cx - c[0]) + (kt.cz - c[2]) * (kt.cz - c[2])) < 0.35 && !IsMobilya(oda, kt)) { gorulduAday = kt.kesin ? 2 : 1; }
                yaz("E3 hayalet 2 bakis yonunden 16 kare gorundu: " + (gorulduAday == 0 ? "aday OLUSMADI (cok sikı: kanit kurali tek basina engelledi)" : gorulduAday == 1 ? "aday olustu" : "KESIN oldu (beklenmedi)"));
                if (gorulduAday == 2) { yaz("HATA: hayalet kesinlesti"); hata++; }
                // simdi icinden bakilan kareler: ayni iki konumdan hayalet olmadan bakilir (isinlar hayalet yerinden gecip arkadaki duvara varir)
                for (int g = 0; g < 2; g++)
                    for (int s2 = 0; s2 < 6; s2++)
                    {
                        t += 0.25f; var ky = HedefeBak(kamlar[g][0], 2.3, kamlar[g][1], c, (s2 - 2.5) * 3, t);
                        Besle(k, OdaSim.Render(oda, ky, r), planlar);
                        foreach (var kt in k.son.kutular) if (Math.Sqrt((kt.cx - c[0]) * (kt.cx - c[0]) + (kt.cz - c[2]) * (kt.cz - c[2])) < 0.35 && !IsMobilya(oda, kt)) enSonDurum = kt.kesin ? 2 : 1;
                    }
                for (int z = 0; z < 8; z++) { t += 0.25f; Besle(k, OdaSim.Render(oda, new SimKare { x = 2.0, y = 1.7, z = 0.5, yaw = 0, pitch = -10, t = t }, r), planlar); }
                // 40 kare daha cekilince hayalet kalmamali: son durum
                bool var = false; foreach (var kt in k.son.kutular) if (Math.Sqrt((kt.cx - c[0]) * (kt.cx - c[0]) + (kt.cz - c[2]) * (kt.cz - c[2])) < 0.35 && !IsMobilya(oda, kt)) var = true;
                yaz("E3 icinden bakildiktan sonra (12 kare + 2 sn): " + (var ? "HAYALET HALA VAR" : "hayalet silindi") + (gorulduAday == 0 ? " (aday zaten olusmamisti)" : ""));
                if (var) { yaz("HATA: hayalet silinmedi"); hata++; }
            }
            return hata;
        }

        static SimKare HedefeBak(double x, double y, double z, double[] hedef, double yawJitter, float t)
        {
            double dx = hedef[0] - x, dz = hedef[2] - z, yatay = Math.Sqrt(dx * dx + dz * dz);
            return new SimKare { x = x, y = y, z = z, yaw = Math.Atan2(dx, dz) * 180 / Math.PI + yawJitter, pitch = Math.Atan2(hedef[1] - y, yatay) * 180 / Math.PI, t = t };
        }

        static bool IsMobilya(SimOda oda, OdaKutu kt)
        {
            foreach (var m in oda.kutular) if (Math.Sqrt((kt.cx - m.cx) * (kt.cx - m.cx) + (kt.cz - m.cz) * (kt.cz - m.cz)) < 0.5) return true;
            return false;
        }

        // ------------------------------------------------------------------------------------ koc testi
        static int KocTesti(Action<string> yaz)
        {
            int hata = 0;
            yaz("");
            yaz("=== (F) yonlendirme kocu: durum -> mesaj ===");
            var duz = new OdaSonuc { zeminVar = true, zeminY = 0.3f, tavanY = 2.9f, tavanGercek = false, voksel = 100 };
            Func<KocGirdi> g = () => new KocGirdi { takipte = true, oncedenTakip = true, ortancaDerinlik = 2f, guvenOrani = 0.8f, pitch = 0, zaman = 100f, sonuc = duz };
            var satirlar = new List<string[]>();
            Action<string, KocGirdi, string, int> dene = (ad, girdi, beklenen, kural) =>
            {
                var koc = new OdaKoc(); var m = koc.Guncelle(girdi);
                bool ok = m.yazi == beklenen && m.kural == kural;
                yaz(ad + " -> \"" + m.yazi + "\" [kural " + m.kural + (m.donus != 0 ? ", kenar oku " + (m.donus > 0 ? "sag" : "sol") : "") + (m.okVar ? ", ilerleme oku yaw=" + F(m.okYaw, 0) : "") + "]" + (ok ? "" : "  <-- BEKLENEN: \"" + beklenen + "\""));
                if (!ok) { hata++; yaz("HATA: koc"); }
            };
            var a = g(); a.takipte = false; a.neden = 1; dene("izleme kaybi (asiri hareket)", a, "Daha yavaş hareket et", 1);
            a = g(); a.takipte = false; a.neden = 2; dene("izleme kaybi (isik az)", a, "Ortam biraz karanlık, ışığı açabilirsin", 1);
            a = g(); a.takipte = false; a.neden = 4; dene("izleme kaybi (yeniden konumlan)", a, "Kamerayı az önceki yere çevir", 1);
            a = g(); a.hizli = true; dene("hizli donus", a, "Daha yavaş hareket et", 1);
            a = g(); a.ortancaDerinlik = 0.4f; dene("cok yakin (medyan 0,4 m)", a, "Biraz geri çekil", 2);
            a = g(); a.ortancaDerinlik = 4.8f; dene("cok uzak (medyan 4,8 m)", a, "Duvara biraz yaklaş", 2);
            a = g(); a.sonuc = new OdaSonuc { zeminVar = false }; dene("zemin yok", a, "Telefonu hafifçe zemine doğru eğ", 4);
            var s5 = new OdaSonuc { zeminVar = true, tavanGercek = false, voksel = 100, duvarKapsamaTum = 0.5f };
            s5.duvarlar.Add(new OdaDuvar { id = 1, ax = 0, az = 3, bx = 3, bz = 3, kesin = true }); s5.duvarlar.Add(new OdaDuvar { id = 2, ax = 3, az = 3, bx = 3, bz = 0, kesin = true });
            a = g(); a.sonuc = s5; dene("duvarlar yonlerin %50'sini kapsiyor, tavan yok", a, "Telefonu biraz yukarı eğ, tavanı tara", 4);
            var s6 = new OdaSonuc { zeminVar = true, tavanGercek = true, voksel = 100 }; s6.duvarlar.Add(new OdaDuvar { id = 1, ax = 0, az = 3, bx = 2, bz = 3, kesin = false, ilerleme = 0.7f });
            a = g(); a.sonuc = s6; a.kx = 1; a.kz = 0; a.yaw = 0; dene("aday duvar gorus alaninda, onaya yakin", a, "Burada bir saniye bekle", 3);
            a = g(); a.sonuc = new OdaSonuc { zeminVar = true, tavanGercek = true, voksel = 100, dongu = true, duvarKapsamaKesin = 1f }; dene("duvarlar kapali dongu", a, "Oda tamamlandı. Bitirmek için Bitir'e dokun.", 7);
            // yon kapsama: kaplanan dilimler disinda
            {
                var koc = new OdaKoc(); var gi = g(); gi.zaman = 100f;
                // baslangic metnini gecmek icin zaman ilerlet, sonra 0..90 derece kapsa
                koc.Guncelle(gi);
                for (int y = 0; y <= 90; y += 6) { gi = g(); gi.zaman = 120f; gi.yaw = y; koc.Guncelle(gi); }
                gi = g(); gi.zaman = 130f; gi.yaw = 90f; var m = koc.Guncelle(gi);
                bool ok = m.kural == 5 && m.yazi.StartsWith("Bu taraf");
                yaz("yon kapsama: 0-90 derece tarandi, bakis 90 -> \"" + m.yazi + "\" [kural " + m.kural + ", kenar oku " + (m.donus > 0 ? "sag" : "sol") + "]" + (ok ? "" : "  <-- BEKLENEN kural 5"));
                if (!ok) { hata++; yaz("HATA: koc kapsama"); }
                // tum dilimler dolu + sinir hucreleri + 6 sn onaysiz -> ilerle
                var s7 = new OdaSonuc { zeminVar = true, tavanGercek = true, voksel = 100, sinir = new[] { 4f, 0f, 5f, 1f } }; for (int b = 0; b < 16; b++) s7.yonDuvar[b] = true;
                for (int y = 0; y < 360; y += 8) { gi = g(); gi.zaman = 140f + y * 0.01f; gi.yaw = y; gi.sonuc = s7; koc.Guncelle(gi); }
                gi = g(); gi.zaman = 160f; gi.yaw = 0; gi.sonuc = s7; m = koc.Guncelle(gi);
                ok = m.kural == 6 && m.okVar;
                yaz("yerel kapsama tamam, 6 sn onay yok, sinir var -> \"" + m.yazi + "\" [kural " + m.kural + ", ilerleme oku yaw=" + F(m.okYaw, 0) + "]" + (ok ? "" : "  <-- BEKLENEN kural 6"));
                if (!ok) { hata++; yaz("HATA: koc ilerle"); }
            }
            // debounce
            {
                var koc = new OdaKoc(); var gi = g(); gi.takipte = false; gi.neden = 1; gi.zaman = 100f; koc.Guncelle(gi);
                gi = g(); gi.zaman = 100.5f; var m = koc.Guncelle(gi);
                bool tut = m.yazi == "Daha yavaş hareket et";
                gi = g(); gi.zaman = 101.6f; m = koc.Guncelle(gi); bool degisti = m.yazi != "Daha yavaş hareket et";
                yaz("debounce: 0,5 sn sonra ayni mesaj " + (tut ? "korundu" : "DEGISTI") + ", 1,6 sn sonra " + (degisti ? "degisti" : "DEGISMEDI"));
                if (!tut || !degisti) { hata++; yaz("HATA: debounce"); }
            }
            return hata;
        }

        static float H4(float x, float z, float[][] ks) { float m = 1e9f; foreach (var k in ks) m = Math.Min(m, H(x, z, k)); return m; }
    }

    // ------------------------------------------------------------------------------------------ kayit tekrari (offline)
    public static class OdaTekrar
    {
        static readonly CultureInfo ci = CultureInfo.InvariantCulture;
        static string F(double v, int n = 2) { return v.ToString("F" + n, ci); }

        static void QuatDondur(float qx, float qy, float qz, float qw, float vx, float vy, float vz, out float ox, out float oy, out float oz)
        {
            float tx = 2 * (qy * vz - qz * vy), ty = 2 * (qz * vx - qx * vz), tz = 2 * (qx * vy - qy * vx);
            ox = vx + qw * tx + (qy * tz - qz * ty); oy = vy + qw * ty + (qz * tx - qx * tz); oz = vz + qw * tz + (qx * ty - qy * tx);
        }

        public static void ZeminPng(ZeminSonuc zs, string yol)
        {
            var rgba = zs.Resim(out int W, out int H, out float ox, out float oz, 10);
            const int K = 1; const float P = ZeminSonuc.ResimPiksel;   // resim ana yone hizali cercevede (u, w), 2,5 cm piksel
            var r = new OdaResim(W * K, H * K, 24, 27, 32);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int o = (y * W + x) * 4; if (rgba[o + 3] == 0) continue;
                    float a = rgba[o + 3] / 255f;
                    for (int q = 0; q < K; q++) for (int t = 0; t < K; t++) r.Nokta(x * K + t, (H - 1 - y) * K + q, rgba[o], rgba[o + 1], rgba[o + 2], a);
                }
            for (int i = 0; i + 3 < zs.iz.Length; i += 2)
            {
                zs.DunyadanResme(zs.iz[i], zs.iz[i + 1], out float ua, out float wa); zs.DunyadanResme(zs.iz[i + 2], zs.iz[i + 3], out float ub, out float wb);
                r.Cizgi((ua - ox) / P * K, (H - (wa - oz) / P) * K, (ub - ox) / P * K, (H - (wb - oz) / P) * K, 1.5, 255, 160, 60, 0.8f);
            }
            r.Yaz(yol);
        }

        public static int Calistir(string klasor, Action<string> yaz, string ciktiDir = null)
        {
            if (!Directory.Exists(klasor)) { yaz("klasor yok: " + klasor); return 2; }
            ciktiDir = ciktiDir ?? klasor;
            var planlar = new List<OdaDuzlem>();
            var dj = MiniJson.Parse(File.ReadAllText(Path.Combine(klasor, "duzlemler.json")));
            foreach (var po in (List<object>)MiniJson.Al(dj, "planes"))
            {
                string al = MiniJson.S(MiniJson.Al(po, "alignment"));
                int tur = al == "HorizontalUp" ? 0 : al == "HorizontalDown" ? 1 : al == "Vertical" ? 2 : -1;
                if (tur < 0) continue;
                var bl = new List<float>();
                foreach (var bo in (List<object>)MiniJson.Al(po, "boundary_world")) { bl.Add((float)MiniJson.D(MiniJson.Al(bo, "x"))); bl.Add((float)MiniJson.D(MiniJson.Al(bo, "y"))); bl.Add((float)MiniJson.D(MiniJson.Al(bo, "z"))); }
                float qx = (float)MiniJson.D(MiniJson.Al(po, "qx")), qy = (float)MiniJson.D(MiniJson.Al(po, "qy")), qz = (float)MiniJson.D(MiniJson.Al(po, "qz")), qw = (float)MiniJson.D(MiniJson.Al(po, "qw"));
                QuatDondur(qx, qy, qz, qw, 0, 1, 0, out float nx, out float ny, out float nz);
                planlar.Add(new OdaDuzlem { id = MiniJson.S(MiniJson.Al(po, "id")), tur = tur, sinir = bl.ToArray(), nx = nx, ny = ny, nz = nz });
            }
            yaz("duzlem: " + planlar.Count + " (yatay yukari " + planlar.FindAll(p => p.tur == 0).Count + ", asagi " + planlar.FindAll(p => p.tur == 1).Count + ", dikey " + planlar.FindAll(p => p.tur == 2).Count + ")");
            foreach (var p in planlar) yaz("  " + p.tur + " alan=" + F(OdaGeo.PolyAlan(p.sinir)) + " y=" + F(p.sinir[1]) + " n=(" + F(p.nx) + "," + F(p.ny) + "," + F(p.nz) + ")");

            var k = OdaSentetik.Yeni(); double ilkT = -1; int kareSay = 0;
            if (Environment.GetEnvironmentVariable("ZEMIN_GUVENLI") == "1") ZeminHaritasi.SadeceGuvenli = true;
            ZeminHaritasi.DokumYolu = Environment.GetEnvironmentVariable("ZEMIN_DOKUM");   // son guncellemenin hucre dokumu
            var zh = new ZeminHaritasi(); zh.Guncelle(planlar, 0f);
            HashSet<long> araOnce = null;
            foreach (var satir in File.ReadAllLines(Path.Combine(klasor, "kareler.jsonl")))
            {
                if (string.IsNullOrWhiteSpace(satir)) continue;
                var kj = MiniJson.Parse(satir);
                string df = MiniJson.S(MiniJson.Al(kj, "depth_file")); if (df == "") continue;
                double ts = MiniJson.D(MiniJson.Al(kj, "image_timestamp_s")); if (ilkT < 0) ilkT = ts;
                int iw = (int)MiniJson.D(MiniJson.Al(kj, "intrinsics_w")), ih = (int)MiniJson.D(MiniJson.Al(kj, "intrinsics_h"));
                int dw = (int)MiniJson.D(MiniJson.Al(kj, "depth_w")), dh = (int)MiniJson.D(MiniJson.Al(kj, "depth_h"));
                bool f32 = MiniJson.S(MiniJson.Al(kj, "depth_format")) == "DepthFloat32";
                var db = File.ReadAllBytes(Path.Combine(klasor, df));
                byte[] gb = null; string gf = MiniJson.S(MiniJson.Al(kj, "conf_file")); if (gf != "" && File.Exists(Path.Combine(klasor, gf))) gb = File.ReadAllBytes(Path.Combine(klasor, gf));
                byte[] eb = null; string ef = df.Replace("derinlik_", "etiket_"); if (File.Exists(Path.Combine(klasor, ef))) eb = File.ReadAllBytes(Path.Combine(klasor, ef));
                int adim = Environment.GetEnvironmentVariable("YARIM") == "1" ? 2 : 1;   // telefon artik tam cozunurluk kullaniyor
                int w = dw / adim, h = dh / adim;
                var f = new DerinlikKaresi
                {
                    w = w, h = h, d = new float[w * h], g = new byte[w * h],
                    fx = (float)MiniJson.D(MiniJson.Al(kj, "fx")) * w / iw, fy = (float)MiniJson.D(MiniJson.Al(kj, "fy")) * h / ih, cx = (float)MiniJson.D(MiniJson.Al(kj, "cx")) * w / iw, cy = (float)MiniJson.D(MiniJson.Al(kj, "cy")) * h / ih,
                    px = (float)MiniJson.D(MiniJson.Al(kj, "px")), py = (float)MiniJson.D(MiniJson.Al(kj, "py")), pz = (float)MiniJson.D(MiniJson.Al(kj, "pz")),
                    qx = (float)MiniJson.D(MiniJson.Al(kj, "qx")), qy = (float)MiniJson.D(MiniJson.Al(kj, "qy")), qz = (float)MiniJson.D(MiniJson.Al(kj, "qz")), qw = (float)MiniJson.D(MiniJson.Al(kj, "qw")),
                    zaman = (float)(ts - ilkT)
                };
                for (int v = 0; v < h; v++)
                    for (int u = 0; u < w; u++)
                    {
                        int o = ((adim * v) * dw + adim * u) * (f32 ? 4 : 2);
                        f.d[v * w + u] = f32 ? BitConverter.ToSingle(db, o) : (db[o] | (db[o + 1] << 8)) * 0.001f;
                        f.g[v * w + u] = gb != null ? gb[(adim * v) * dw + adim * u] : (byte)255;
                        if (eb != null) { if (f.e == null) f.e = new byte[w * h]; f.e[v * w + u] = eb[(adim * v) * dw + adim * u]; }
                    }
                OdaSentetik.Besle(k, f, planlar); kareSay++;
                zh.Isle(f);
                // ZEMIN_ARA=n: her n karede ara sonuc (alan zamanla nasil degisiyor; once taranan yer siliniyor mu)
                if (int.TryParse(Environment.GetEnvironmentVariable("ZEMIN_ARA"), out int ara) && ara > 0 && kareSay % ara == 0)
                {
                    var za = zh.Guncelle(planlar, f.zaman);
                    int kayip = 0; if (araOnce != null) foreach (long q in araOnce) if (!za.acik.Contains(q)) kayip++;
                    araOnce = new HashSet<long>(za.acik);
                    yaz("ARA kare " + kareSay + " t=" + F(f.zaman, 1) + " acik=" + F(za.alan) + " m2 hucre=" + za.acik.Count + " kayip=" + kayip + " | " + zh.Teshis);
                    if (ciktiDir != null) { Directory.CreateDirectory(ciktiDir); ZeminPng(za, Path.Combine(ciktiDir, "ara_" + kareSay.ToString("0000") + ".png")); }
                }
            }
            // KALEM="x0,z0,x1,z1,...|kx,kz": kalem cizgisi (kullanici kx,kz'de) son guncellemeden once eklenir
            string kalemT = Environment.GetEnvironmentVariable("KALEM");
            if (!string.IsNullOrEmpty(kalemT))
            {
                var pr = kalemT.Split('|'); var ci = CultureInfo.InvariantCulture;
                var xz = Array.ConvertAll(pr[0].Split(','), q => float.Parse(q, ci)); var kk2 = Array.ConvertAll(pr[1].Split(','), q => float.Parse(q, ci));
                var once = zh.Guncelle(planlar, k.model.izgara.SonZaman + 0.2f);
                yaz("KALEM oncesi acik=" + F(once.alan) + " m2");
                zh.SinirCizgisiEkle(xz, kk2[0], kk2[1]);
            }
            // son kare sonrasi bir son guncelleme
            zh.Guncelle(planlar, k.model.izgara.SonZaman + 0.5f);
            var sw0 = Stopwatch.StartNew();
            var zs = zh.Guncelle(planlar, k.model.izgara.SonZaman + 1f);
            yaz("ZEMIN: y=" + F(zs.zeminY, 3) + " acik=" + F(zs.alan) + " m2, duvar hucre=" + zs.duvar.Count + ", ucgen=" + zs.acikUcgen.Length / 6 + "+" + zs.engelUcgen.Length / 6 + ", engel hucre=" + zs.engel.Count + ", bilinen=" + zs.bilinen.Count + ", sinir=" + zs.sinir.Length / 2 + ", guncelleme " + sw0.ElapsedMilliseconds + " ms, hucre " + zh.HucreSayisi);
            { var sb = new StringBuilder("engel yk (2cm): "); for (int q = 0; q < 30; q++) sb.Append(zh.TeshisYk[q]).Append(' '); yaz(sb.ToString()); }
            Directory.CreateDirectory(ciktiDir);
            ZeminPng(zs, Path.Combine(ciktiDir, "zemin_ustten.png"));
            File.WriteAllText(Path.Combine(ciktiDir, "zemin.json"), zs.Json("test"), new UTF8Encoding(false));
            var s = k.model.Guncelle(planlar, k.model.izgara.SonZaman + 1f);
            yaz("kare: " + kareSay + ", voksel: " + k.model.izgara.VokselSayisi + ", dolu voksel: " + k.model.izgara.DoluSayisi());
            yaz("etiketli=" + s.etiketli + " etiketli nokta=" + k.model.izgara.EtiketNokta + " esya voksel=" + k.model.izgara.Esya.Count);
            yaz("zemin var=" + s.zeminVar + " y=" + F(s.zeminY, 3) + " tavan y=" + F(s.tavanY, 3) + " gercek=" + s.tavanGercek + " (tahmin=" + s.TavanTahmin + ")");
            yaz("duvar: " + s.duvarlar.Count + " (kesin " + s.KesinDuvar + "), kutu: " + s.kutular.Count + " (kesin " + s.KesinKutu + "), bos zemin alani: " + F(s.acikAlan) + " m2, sinir hucresi: " + s.sinir.Length / 2);
            yaz("zemin farki (etiket - ARCore): " + F(k.model.ZeminFark) + " m, tavan-zemin: " + F(s.tavanY - s.zeminY) + " m");
            foreach (var d in s.duvarlar) yaz("  duvar " + d.id + " " + (d.kesin ? "kesin" : "aday") + " ilerleme=" + F(d.ilerleme) + " (" + F(d.ax) + "," + F(d.az) + ")->(" + F(d.bx) + "," + F(d.bz) + ") uz=" + F(d.Uzunluk));
            foreach (var kk in s.kutular) yaz("  kutu " + kk.id + " " + kk.TurAdi + " " + (kk.kesin ? "kesin" : "aday") + " ilerleme=" + F(kk.ilerleme) + " merkez=(" + F(kk.cx) + "," + F(kk.cy) + "," + F(kk.cz) + ") boyut=" + F(kk.sx) + "x" + F(kk.sy) + "x" + F(kk.sz) + " aci=" + F(kk.aci, 1));
            yaz("sure: butunlestirme " + OdaSentetik.OzetAl(k.isleMs) + "; yapi " + OdaSentetik.OzetAl(k.guncMs));
            Directory.CreateDirectory(ciktiDir);
            File.WriteAllText(Path.Combine(ciktiDir, "oda.json"), OdaJson.Yaz(s, null), new UTF8Encoding(false));
            string png = Path.Combine(ciktiDir, "oda_ustten.png");
            OdaKusbakisi.Ciz(png, s, k.iz);
            yaz("yazildi: " + Path.Combine(ciktiDir, "oda.json") + " ve " + png);
            return 0;
        }
    }
}
