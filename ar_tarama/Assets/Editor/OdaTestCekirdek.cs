using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

// Saf C# (UnityEngine yok): hem Unity Editor testi (OdaTest.cs) hem de komut satiri koşucusu (tools/tarama/test) kullanir.
namespace ArTarama
{
    // ------------------------------------------------------------------------------------------ kucuk JSON okuyucu
    public static class MiniJson
    {
        public static object Parse(string s) { int i = 0; var v = Oku(s, ref i); return v; }

        static void Bosluk(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Oku(string s, ref int i)
        {
            Bosluk(s, ref i);
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(); i++; Bosluk(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Bosluk(s, ref i); string k = (string)Oku(s, ref i); Bosluk(s, ref i); i++; // :
                    d[k] = Oku(s, ref i); Bosluk(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++; return d;
                }
            }
            if (c == '[')
            {
                var l = new List<object>(); i++; Bosluk(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Oku(s, ref i)); Bosluk(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++; return l;
                }
            }
            if (c == '"')
            {
                var sb = new StringBuilder(); i++;
                while (s[i] != '"')
                {
                    if (s[i] == '\\') { i++; char e = s[i]; sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e == 'u' ? (char)Convert.ToInt32(s.Substring(i + 1, 4), 16) : e); if (e == 'u') i += 4; }
                    else sb.Append(s[i]);
                    i++;
                }
                i++; return sb.ToString();
            }
            if (c == 't') { i += 4; return true; }
            if (c == 'f') { i += 5; return false; }
            if (c == 'n') { i += 4; return null; }
            int b = i; while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(b, i - b), CultureInfo.InvariantCulture);
        }

        public static double D(object o) { return o is double d ? d : 0.0; }
        public static string S(object o) { return o as string ?? ""; }
        public static object Al(object o, string k) { return o is Dictionary<string, object> d && d.TryGetValue(k, out var v) ? v : null; }
    }

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

    // ------------------------------------------------------------------------------------------ sentetik veri + testler
    public static class OdaSentetik
    {
        static readonly CultureInfo ci = CultureInfo.InvariantCulture;

        static double Gauss(Random r) { double u = 1.0 - r.NextDouble(), v = r.NextDouble(); return Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2 * Math.PI * v); }

        public const float ZeminY = 0.30f;

        /// <summary>4 x 5 m oda: 4 duvar (parcali, gurultulu, kose bosluklu), zemin ve tavan duzlemi.</summary>
        public static List<OdaDuzlem> Oda4x5(Random r)
        {
            var l = new List<OdaDuzlem>();
            float[][] kose = { new[] { 0f, 0f }, new[] { 4f, 0f }, new[] { 4f, 5f }, new[] { 0f, 5f } };
            for (int w = 0; w < 4; w++)
            {
                var a = kose[w]; var b = kose[(w + 1) % 4];
                float len = (float)Math.Sqrt((b[0] - a[0]) * (b[0] - a[0]) + (b[1] - a[1]) * (b[1] - a[1]));
                // parcalar [t0,t1] (m): tam, bolunmus + bosluk, ve ust uste binen kucuk plan
                float[][] parca;
                if (w == 0) parca = new[] { new[] { 0.15f, 1.9f }, new[] { 2.1f, len - 0.15f }, new[] { 1.0f, 2.5f } };
                else if (w == 2) parca = new[] { new[] { 0.15f, 1.2f }, new[] { 1.35f, 2.6f }, new[] { 2.7f, len - 0.15f } };
                else parca = new[] { new[] { 0.15f, len - 0.15f } };
                foreach (var p in parca) l.Add(DuvarPlani(r, a, b, len, p[0], p[1], ZeminY + (float)(r.NextDouble() * 0.25), ZeminY + 2.0f + (float)(r.NextDouble() * 0.5)));
            }
            l.Add(YatayPlan(0, 0.1f, 0.1f, 3.9f, 4.9f, ZeminY));
            l.Add(YatayPlan(1, 0.1f, 0.1f, 3.9f, 4.9f, ZeminY + 2.6f));
            return l;
        }

        static OdaDuzlem YatayPlan(int tur, float x0, float z0, float x1, float z1, float y)
        {
            return new OdaDuzlem { id = "yatay" + tur, tur = tur, nx = 0, ny = tur == 0 ? 1 : -1, nz = 0, sinir = new[] { x0, y, z0, x1, y, z0, x1, y, z1, x0, y, z1 } };
        }

        static OdaDuzlem DuvarPlani(Random r, float[] a, float[] b, float len, float t0, float t1, float y0, float y1)
        {
            float dx = (b[0] - a[0]) / len, dz = (b[1] - a[1]) / len;
            float nx = -dz, nz = dx;                           // dikey yon: odanin icine (x,z saat yonu duzenlemesi: kose sirasi saat yonunun tersi)
            float ofs = (float)(Gauss(r) * 0.008), egim = (float)(Gauss(r) * 0.4 * Math.PI / 180.0);
            float cx = a[0] + dx * (t0 + t1) * 0.5f, cz = a[1] + dz * (t0 + t1) * 0.5f;
            var pts = new List<float>();
            float[][] kose = { new[] { t0, y0 }, new[] { t1, y0 }, new[] { t1, y1 }, new[] { t0, y1 } };
            foreach (var k in kose)
            {
                float t = k[0] - (t0 + t1) * 0.5f;
                float ex = t * (float)Math.Cos(egim), ez = t * (float)Math.Sin(egim);   // plan icinde kucuk donme
                float x = cx + dx * ex + nx * (ez + ofs), z = cz + dz * ex + nz * (ez + ofs);
                pts.Add(x + (float)(Gauss(r) * 0.005)); pts.Add(k[1]); pts.Add(z + (float)(Gauss(r) * 0.005));
            }
            float sg = r.Next(2) == 0 ? 1f : -1f;             // ARCore normal yonu bazen disa bakar
            return new OdaDuzlem { id = "duvar", tur = 2, sinir = pts.ToArray(), nx = nx * sg, ny = 0, nz = nz * sg };
        }

        public static void KutuNoktalari(List<float> o, Random r, float cx, float cz, float phiDeg, float lx, float lz, float h, float adim, float gurultu)
        {
            double ph = phiDeg * Math.PI / 180.0, c = Math.Cos(ph), s = Math.Sin(ph);
            Action<double, double, double> ekle = (u, v, hh) =>
            {
                double x = cx + u * c - v * s, z = cz + u * s + v * c;
                o.Add((float)(x + Gauss(r) * gurultu)); o.Add((float)(ZeminY + hh + Gauss(r) * gurultu)); o.Add((float)(z + Gauss(r) * gurultu));
            };
            for (double u = -lx / 2; u <= lx / 2 + 1e-6; u += adim) for (double v = -lz / 2; v <= lz / 2 + 1e-6; v += adim) ekle(u, v, h);   // ust
            for (double u = -lx / 2; u <= lx / 2 + 1e-6; u += adim) for (double hh = 0; hh <= h; hh += adim) { ekle(u, -lz / 2, hh); ekle(u, lz / 2, hh); }
            for (double v = -lz / 2; v <= lz / 2 + 1e-6; v += adim) for (double hh = 0; hh <= h; hh += adim) { ekle(-lx / 2, v, hh); ekle(lx / 2, v, hh); }
        }

        public class Beklenen { public string ad; public float cx, cz, lx, lz, h, phi; public bool kare; }

        public static List<Beklenen> Esyalar()
        {
            return new List<Beklenen>
            {
                new Beklenen { ad = "masa", cx = 1.503f, cz = 2.003f, lx = 1.6f, lz = 0.8f, h = 0.75f, phi = 0 },
                new Beklenen { ad = "sandalye1", cx = 1.203f, cz = 2.723f, lx = 0.45f, lz = 0.45f, h = 0.9f, phi = 0, kare = true },
                new Beklenen { ad = "sandalye2", cx = 1.803f, cz = 2.723f, lx = 0.45f, lz = 0.45f, h = 0.9f, phi = 0, kare = true },
                new Beklenen { ad = "kanepe", cx = 2.65f, cz = 3.97f, lx = 2.0f, lz = 0.9f, h = 0.45f, phi = 30 },
            };
        }

        public static float[] Nokta(Random r, List<Beklenen> esya, out int sayi)
        {
            var o = new List<float>();
            foreach (var e in esya) KutuNoktalari(o, r, e.cx, e.cz, e.phi, e.lx, e.lz, e.h, 0.035f, 0.008f);
            // zemin
            for (float x = 0.1f; x < 3.9f; x += 0.08f) for (float z = 0.1f; z < 4.9f; z += 0.08f) { o.Add(x); o.Add(ZeminY + (float)(Gauss(r) * 0.012)); o.Add(z); }
            // duvarlar (nokta olarak)
            for (float t = 0; t < 5f; t += 0.08f) for (float y = 0; y < 2.5f; y += 0.08f)
                {
                    float n1 = (float)(Gauss(r) * 0.01);
                    o.Add(n1); o.Add(ZeminY + y); o.Add(t);
                    o.Add(4f + n1); o.Add(ZeminY + y); o.Add(t);
                    if (t < 4f) { o.Add(t); o.Add(ZeminY + y); o.Add(n1); o.Add(t); o.Add(ZeminY + y); o.Add(5f + n1); }
                }
            int toplam = o.Count / 3; int aykiri = (int)(toplam * 0.03);
            for (int i = 0; i < aykiri; i++) { o.Add((float)(r.NextDouble() * 4)); o.Add(ZeminY + (float)(r.NextDouble() * 2.6)); o.Add((float)(r.NextDouble() * 5)); }
            sayi = o.Count / 3; return o.ToArray();
        }

        // -------------------------------------------------------------------------------------------- testler
        /// <summary>Tum sentetik testleri calistirir, tablo yazar, basarisiz sayisini dondurur.</summary>
        public static int Calistir(Action<string> yaz)
        {
            int hata = 0;
            var r = new Random(12345);
            var planlar = Oda4x5(r);

            // ---- (a) duvarlar ----
            yaz("=== (a) 4x5 m oda: duvarlar ===");
            var m = new OdaModeli();
            OdaSonuc s = null;
            for (int i = 0; i < 3; i++) s = m.Guncelle(planlar, new float[0], 0);
            yaz("zemin y=" + s.zeminY.ToString("F3", ci) + " tavan y=" + s.tavanY.ToString("F3", ci) + " (beklenen 0.300 / 2.900, gercek tavan=" + s.tavanGercek + ")");
            if (Math.Abs(s.zeminY - 0.3f) > 0.02f || Math.Abs(s.tavanY - 2.9f) > 0.03f) { yaz("HATA: zemin/tavan"); hata++; }
            float[] beklenenUz = { 4f, 5f, 4f, 5f };
            float[][] koseler = { new[] { 0f, 0f }, new[] { 4f, 0f }, new[] { 4f, 5f }, new[] { 0f, 5f } };
            yaz("duvar sayisi: " + s.duvarlar.Count + " (beklenen 4)");
            if (s.duvarlar.Count != 4) { yaz("HATA: duvar sayisi"); hata++; }
            yaz("id | A(x,z) | B(x,z) | uzunluk (bek.) | normal | kose hatasi");
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
                float ka = 1e9f;
                foreach (var k in koseler) { ka = Math.Min(ka, Math.Min(H(d.ax, d.az, k), H(d.bx, d.bz, k))); }
                float kb = 0; foreach (float e in new[] { H4(d.ax, d.az, koseler), H4(d.bx, d.bz, koseler) }) kb = Math.Max(kb, e);
                float uhata = Math.Abs(d.Uzunluk - beklenenUz[bi]);
                yaz(d.id + " | (" + d.ax.ToString("F2", ci) + "," + d.az.ToString("F2", ci) + ") | (" + d.bx.ToString("F2", ci) + "," + d.bz.ToString("F2", ci) + ") | " + d.Uzunluk.ToString("F3", ci) + " (" + beklenenUz[bi].ToString("F1", ci) + ") | (" + d.nx.ToString("F2", ci) + "," + d.nz.ToString("F2", ci) + ") | " + kb.ToString("F3", ci) + " m");
                if (uhata > 0.03f || kb > 0.03f) { yaz("HATA: duvar " + d.id + " uzunluk/kose"); hata++; }
                kul[bi] = true;
                float icH = (2f - 0f); // normal oda icine mi (merkez 2,2.5)
                if ((2f - (d.ax + d.bx) / 2) * d.nx + (2.5f - (d.az + d.bz) / 2) * d.nz <= 0) { yaz("HATA: normal disa bakiyor"); hata++; }
            }
            foreach (var k in kul) if (!k) { yaz("HATA: bir duvar yok"); hata++; break; }

            // ---- (b) esya ----
            yaz("");
            yaz("=== (b) esya kutulari ===");
            var esya = Esyalar();
            var nk = Nokta(r, esya, out int ns);
            yaz("nokta sayisi: " + ns + " (%3 aykiri dahil)");
            m = new OdaModeli();
            var idler = new List<string>();
            for (int i = 0; i < 4; i++)
            {
                s = m.Guncelle(planlar, nk, ns);
                var sb = new StringBuilder(); foreach (var k in s.kutular) sb.Append(k.id).Append(' ');
                idler.Add(sb.ToString().Trim());
            }
            yaz("guncelleme 1-4 kutu kimlikleri: [" + string.Join("] [", idler.ToArray()) + "] (kimlikler sabit olmali, ilk guncellemede bos)");
            if (idler[3] != idler[2] || idler[0] != "") { yaz("HATA: kimlik kararsiz / dogrulama gecikmesi"); hata++; }
            yaz("kutu sayisi: " + s.kutular.Count + " (beklenen 4, en cok 5)");
            if (s.kutular.Count < 4 || s.kutular.Count > 5) { yaz("HATA: kutu sayisi"); hata++; }
            yaz("ad | beklenen (uzun x kisa x yuk, aci) | olculen | boyut hatasi | aci hatasi | merkez hatasi");
            var kutuKul = new bool[s.kutular.Count];
            foreach (var e in esya)
            {
                int bi = -1; float bd = 1e9f;
                for (int i = 0; i < s.kutular.Count; i++)
                {
                    float d = (float)Math.Sqrt((s.kutular[i].cx - e.cx) * (s.kutular[i].cx - e.cx) + (s.kutular[i].cz - e.cz) * (s.kutular[i].cz - e.cz));
                    if (d < bd) { bd = d; bi = i; }
                }
                if (bi < 0 || bd > 0.3f) { yaz(e.ad + " | KUTU YOK"); hata++; continue; }
                kutuKul[bi] = true; var k = s.kutular[bi];
                float ex = Math.Max(e.lx, e.lz), ez = Math.Min(e.lx, e.lz);
                float hx = Math.Abs(k.sx - ex), hz = Math.Abs(k.sz - ez), hy = Math.Abs(k.sy - e.h);
                double dac = Math.Abs(k.aci - (e.kare ? 0 : e.phi)); dac %= (e.kare ? 90.0 : 180.0); dac = Math.Min(dac, (e.kare ? 90.0 : 180.0) - dac);
                yaz(e.ad + " | " + ex.ToString("F2", ci) + "x" + ez.ToString("F2", ci) + "x" + e.h.ToString("F2", ci) + " @" + e.phi.ToString("F0", ci) + " | " + k.sx.ToString("F2", ci) + "x" + k.sz.ToString("F2", ci) + "x" + k.sy.ToString("F2", ci) + " @" + k.aci.ToString("F1", ci) + " | " + Math.Max(hx, Math.Max(hz, hy)).ToString("F3", ci) + " m | " + dac.ToString("F1", ci) + " deg | " + bd.ToString("F3", ci) + " m");
                if (hx > 0.10f || hz > 0.10f || hy > 0.10f) { yaz("HATA: " + e.ad + " boyut"); hata++; }
                if (dac > 5.0) { yaz("HATA: " + e.ad + " aci"); hata++; }
            }
            for (int i = 0; i < kutuKul.Length; i++) if (!kutuKul[i]) { yaz("HATA: eslesmeyen (aykiri/fazla) kutu id=" + s.kutular[i].id + " merkez=(" + s.kutular[i].cx.ToString("F2", ci) + "," + s.kutular[i].cz.ToString("F2", ci) + ") boyut=" + s.kutular[i].sx.ToString("F2", ci) + "x" + s.kutular[i].sz.ToString("F2", ci)); hata++; }

            // ---- (c) acik zemin sorgusu ----
            yaz("");
            yaz("=== (c) acik zemin sorgusu ===");
            Func<float, float, bool> q = (x, z) => s.AcikZeminMi(x, z);
            bool[] bek = { false, false, true, false, true };
            bool[] ol = { q(1.5f, 2.0f), q(1.5f, 2.55f), q(3.2f, 1.0f), q(0.1f, 2.0f), q(3.0f, 0.8f) };
            string[] ad = { "masanin ustu", "masa kenarindan 15 cm (sandalye bolgesi)", "bos zemin (3.2,1.0)", "duvardan 10 cm", "bos zemin (3.0,0.8)" };
            for (int i = 0; i < 5; i++) { yaz(ad[i] + ": " + ol[i] + " (beklenen " + bek[i] + ")"); if (ol[i] != bek[i]) { yaz("HATA: acik zemin"); hata++; } }
            yaz("");
            yaz(hata == 0 ? "SONUC: TUM SENTETIK TESTLER GECTI" : "SONUC: " + hata + " HATA");
            return hata;
        }

        static float H(float x, float z, float[] k) { return (float)Math.Sqrt((x - k[0]) * (x - k[0]) + (z - k[1]) * (z - k[1])); }
        static float H4(float x, float z, float[][] ks) { float m = 1e9f; foreach (var k in ks) m = Math.Min(m, H(x, z, k)); return m; }
    }

    // ------------------------------------------------------------------------------------------ kayit tekrari (offline)
    public static class OdaTekrar
    {
        static readonly CultureInfo ci = CultureInfo.InvariantCulture;

        static void QuatDondur(float qx, float qy, float qz, float qw, float vx, float vy, float vz, out float ox, out float oy, out float oz)
        {
            // v' = v + 2*q.w*(q.xyz x v) + 2*q.xyz x (q.xyz x v)
            float tx = 2 * (qy * vz - qz * vy), ty = 2 * (qz * vx - qx * vz), tz = 2 * (qx * vy - qy * vx);
            ox = vx + qw * tx + (qy * tz - qz * ty); oy = vy + qw * ty + (qz * tx - qx * tz); oz = vz + qw * tz + (qx * ty - qy * tx);
        }

        public static int Calistir(string klasor, Action<string> yaz, bool plyEkle)
        {
            if (!Directory.Exists(klasor)) { yaz("klasor yok: " + klasor); return 2; }
            // duzlemler
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
            foreach (var p in planlar) yaz("  " + p.tur + " " + p.id + " alan=" + OdaModeli.PolyAlan(p.sinir).ToString("F2", ci) + " y=" + (p.sinir[1]).ToString("F2", ci) + " n=(" + p.nx.ToString("F2", ci) + "," + p.ny.ToString("F2", ci) + "," + p.nz.ToString("F2", ci) + ")");

            // kareler: derinlik -> nokta (uygulamadaki NoktaBulutu.DerinlikIsle ile ayni mantik, 4 cm voksel)
            var pts = new List<float>(); var gorulen = new HashSet<long>(); var iz = new List<float[]>();
            foreach (var satir in File.ReadAllLines(Path.Combine(klasor, "kareler.jsonl")))
            {
                if (string.IsNullOrWhiteSpace(satir)) continue;
                var k = MiniJson.Parse(satir);
                string df = MiniJson.S(MiniJson.Al(k, "depth_file")); if (df == "") continue;
                float px = (float)MiniJson.D(MiniJson.Al(k, "px")), py = (float)MiniJson.D(MiniJson.Al(k, "py")), pz = (float)MiniJson.D(MiniJson.Al(k, "pz"));
                float qx = (float)MiniJson.D(MiniJson.Al(k, "qx")), qy = (float)MiniJson.D(MiniJson.Al(k, "qy")), qz = (float)MiniJson.D(MiniJson.Al(k, "qz")), qw = (float)MiniJson.D(MiniJson.Al(k, "qw"));
                iz.Add(new[] { px, py, pz });
                float fx = (float)MiniJson.D(MiniJson.Al(k, "fx")), fy = (float)MiniJson.D(MiniJson.Al(k, "fy")), cx = (float)MiniJson.D(MiniJson.Al(k, "cx")), cy = (float)MiniJson.D(MiniJson.Al(k, "cy"));
                int iw = (int)MiniJson.D(MiniJson.Al(k, "intrinsics_w")), ih = (int)MiniJson.D(MiniJson.Al(k, "intrinsics_h"));
                int dw = (int)MiniJson.D(MiniJson.Al(k, "depth_w")), dh = (int)MiniJson.D(MiniJson.Al(k, "depth_h"));
                bool f32 = MiniJson.S(MiniJson.Al(k, "depth_format")) == "DepthFloat32";
                var db = File.ReadAllBytes(Path.Combine(klasor, df));
                byte[] gb = null; string gf = MiniJson.S(MiniJson.Al(k, "conf_file")); if (gf != "" && File.Exists(Path.Combine(klasor, gf))) gb = File.ReadAllBytes(Path.Combine(klasor, gf));
                float sx = iw / (float)dw, sy = ih / (float)dh;
                Func<int, int, float> oku = (i, j) =>
                {
                    int o = (j * dw + i) * (f32 ? 4 : 2);
                    return f32 ? BitConverter.ToSingle(db, o) : (db[o] | (db[o + 1] << 8)) * 0.001f;
                };
                for (int j = 0; j < dh - 1; j += 2)
                    for (int i = 0; i < dw - 1; i += 2)
                    {
                        float z = oku(i, j);
                        if (z < 0.3f || z > 4.5f) continue;
                        if (gb != null && gb[j * dw + i] < 0.3f * 255f) continue;
                        float z2 = oku(i + 1, j), z3 = oku(i, j + 1);
                        if (Math.Abs(z2 - z) > 0.12f * z || Math.Abs(z3 - z) > 0.12f * z) continue;
                        float ppx = (i + 0.5f) * sx, ppy = (j + 0.5f) * sy;
                        float lx = (cy - ppy) / fy * z, ly = (cx - ppx) / fx * z;   // Unity kamera cercevesi: (-y_cv, -x_cv, z)
                        QuatDondur(qx, qy, qz, qw, lx, ly, z, out float wx, out float wy, out float wz);
                        wx += px; wy += py; wz += pz;
                        long key = ((long)Math.Floor(wx / 0.04f) + 1048576L) << 42 | ((long)Math.Floor(wy / 0.04f) + 1048576L) << 21 | ((long)Math.Floor(wz / 0.04f) + 1048576L);
                        if (!gorulen.Add(key)) continue;
                        pts.Add(wx); pts.Add(wy); pts.Add(wz);
                    }
            }
            int derinNokta = pts.Count / 3;
            yaz("derinlikten nokta: " + derinNokta + ", kamera konumu sayisi: " + iz.Count);
            if (plyEkle)
            {
                string ply = Path.Combine(klasor, "noktalar.ply");
                if (File.Exists(ply))
                {
                    bool ust = false; int e = 0;
                    foreach (var l in File.ReadAllLines(ply))
                    {
                        if (!ust) { if (l.StartsWith("end_header")) ust = true; continue; }
                        var t = l.Split(' '); if (t.Length < 3) continue;
                        pts.Add(float.Parse(t[0], ci)); pts.Add(float.Parse(t[1], ci)); pts.Add(float.Parse(t[2], ci)); e++;
                    }
                    yaz("noktalar.ply eklendi: " + e);
                }
            }
            var pa = pts.ToArray(); int pn = pa.Length / 3;

            var model = new OdaModeli(); OdaSonuc s = null;
            for (int i = 0; i < 4; i++) s = model.Guncelle(planlar, pa, pn);
            yaz("zemin var=" + s.zeminVar + " y=" + s.zeminY.ToString("F3", ci) + " tavan y=" + s.tavanY.ToString("F3", ci) + " gercek=" + s.tavanGercek);
            yaz("duvar: " + s.duvarlar.Count + ", kutu: " + s.kutular.Count);
            foreach (var d in s.duvarlar) yaz("  duvar " + d.id + " (" + d.ax.ToString("F2", ci) + "," + d.az.ToString("F2", ci) + ")->(" + d.bx.ToString("F2", ci) + "," + d.bz.ToString("F2", ci) + ") uz=" + d.Uzunluk.ToString("F2", ci));
            foreach (var kk in s.kutular) yaz("  kutu " + kk.id + " merkez=(" + kk.cx.ToString("F2", ci) + "," + kk.cy.ToString("F2", ci) + "," + kk.cz.ToString("F2", ci) + ") boyut=" + kk.sx.ToString("F2", ci) + "x" + kk.sy.ToString("F2", ci) + "x" + kk.sz.ToString("F2", ci) + " aci=" + kk.aci.ToString("F1", ci));

            File.WriteAllText(Path.Combine(klasor, "oda.json"), OdaJson.Yaz(s, null), new UTF8Encoding(false));
            string png = Path.Combine(klasor, "oda_ustten.png");
            Ciz(png, s, pa, pn, iz);
            yaz("yazildi: " + Path.Combine(klasor, "oda.json") + " ve " + png);
            return 0;
        }

        static void Ciz(string yol, OdaSonuc s, float[] pa, int pn, List<float[]> iz)
        {
            double x0 = 1e9, x1 = -1e9, z0 = 1e9, z1 = -1e9;
            Action<double, double> kap = (x, z) => { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); z0 = Math.Min(z0, z); z1 = Math.Max(z1, z); };
            for (int i = 0; i < pn; i++) kap(pa[i * 3], pa[i * 3 + 2]);
            foreach (var d in s.duvarlar) { kap(d.ax, d.az); kap(d.bx, d.bz); }
            foreach (var p in iz) kap(p[0], p[2]);
            if (x0 > x1) { x0 = -1; x1 = 1; z0 = -1; z1 = 1; }
            x0 -= 0.4; x1 += 0.4; z0 -= 0.4; z1 += 0.4;
            const int W = 1000; double olcek = Math.Min((W - 40) / (x1 - x0), (W - 40) / (z1 - z0));
            int Wd = (int)((x1 - x0) * olcek) + 40, Hd = (int)((z1 - z0) * olcek) + 40;
            var im = new OdaResim(Wd, Hd, 24, 27, 32);
            Func<double, double> X = x => 20 + (x - x0) * olcek; Func<double, double> Y = z => Hd - 20 - (z - z0) * olcek;
            // 1 m izgara
            for (double g = Math.Ceiling(x0); g <= x1; g += 1) im.Cizgi(X(g), 20, X(g), Hd - 20, 1, 60, 66, 74, 0.8f);
            for (double g = Math.Ceiling(z0); g <= z1; g += 1) im.Cizgi(20, Y(g), Wd - 20, Y(g), 1, 60, 66, 74, 0.8f);
            // zemin poligonu
            if (s.zeminPoligon.Length >= 6)
            {
                int n = s.zeminPoligon.Length / 2; var xs = new double[n]; var ys = new double[n];
                for (int i = 0; i < n; i++) { xs[i] = X(s.zeminPoligon[i * 2]); ys[i] = Y(s.zeminPoligon[i * 2 + 1]); }
                im.Dolgu(xs, ys, 230, 232, 236, 0.10f);
            }
            // noktalar (yukseklige gore renk: mavi - turkuaz - kehribar)
            for (int i = 0; i < pn; i++)
            {
                double t = Math.Max(0, Math.Min(1, (pa[i * 3 + 1] - s.zeminY) / 2.8));
                byte r = (byte)(t < 0.5 ? 64 + (66 - 64) * t * 2 : 66 + (255 - 66) * (t - 0.5) * 2), g = (byte)(t < 0.5 ? 115 + (217 - 115) * t * 2 : 217 + (194 - 217) * (t - 0.5) * 2), b = (byte)(t < 0.5 ? 255 + (190 - 255) * t * 2 : 190 + (77 - 190) * (t - 0.5) * 2);
                im.Disk(X(pa[i * 3]), Y(pa[i * 3 + 2]), 1.3, r, g, b, 0.75f);
            }
            // kamera izi
            for (int i = 1; i < iz.Count; i++) im.Cizgi(X(iz[i - 1][0]), Y(iz[i - 1][2]), X(iz[i][0]), Y(iz[i][2]), 2, 255, 255, 255, 0.9f);
            // kutular
            var kose = new float[8];
            foreach (var k in s.kutular)
            {
                k.Koseler(kose);
                var xs = new double[4]; var ys = new double[4]; for (int i = 0; i < 4; i++) { xs[i] = X(kose[i * 2]); ys[i] = Y(kose[i * 2 + 1]); }
                im.Dolgu(xs, ys, 255, 255, 255, 0.22f);
                for (int i = 0; i < 4; i++) { int j = (i + 1) % 4; im.Cizgi(xs[i], ys[i], xs[j], ys[j], 3, 255, 255, 255, 1f); }
            }
            // duvarlar
            foreach (var d in s.duvarlar) im.Cizgi(X(d.ax), Y(d.az), X(d.bx), Y(d.bz), 6, 255, 255, 255, 1f);
            im.Yaz(yol);
        }
    }
}
