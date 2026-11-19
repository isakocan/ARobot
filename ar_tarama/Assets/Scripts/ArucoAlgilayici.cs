using System;
using System.Collections.Generic;

namespace ArTarama
{
    public struct ArucoSonuc
    {
        public int id;
        public int hamming;
        /// <summary>Isaretin TL, TR, BR, BL kosesi: x0,y0,x1,y1,... (verilen olcekle tam goruntu pikselleri).</summary>
        public float[] kose;
        public float boyut;
        public int seviye;
    }

    /// <summary>
    /// Saf C# ArUco (DICT_4X4_50) algilayici. Gri goruntu uzerinde: uyarlamali esik, 4-bagli bilesenler,
    /// bilesen govdesinden dortgen, perspektif ornekleme, sozluk eslestirme. Piramit ile 3-4 olcek taranir.
    /// Birim testler icin Unity'ye bagimli degildir.
    /// </summary>
    public class ArucoAlgilayici
    {
        public int MaksHamming = 1;
        public int YariPencere = 15;
        public int Sabit = 7;
        public int MinKenar = 18;
        public int MaksAday = 400;

        // yeniden kullanilan tamponlar
        int[] integ = new int[0], lab = new int[0], parent = new int[0];
        int[] cnt = new int[0], mnx = new int[0], mxx = new int[0], mny = new int[0], mxy = new int[0];
        byte[] bin = new byte[0];
        readonly List<Nk> hamNokta = new List<Nk>();
        readonly List<Nk> hull = new List<Nk>();
        readonly float[] ornek = new float[30 * 30];

        struct Nk { public float x, y; }

        public List<ArucoSonuc> Algila(byte[] gri, int w, int h, float olcek = 1f)
        {
            var cikti = new List<ArucoSonuc>();
            byte[] cur = gri; int cw = w, ch = h; float sc = olcek;
            for (int seviye = 0; seviye < 4 && Math.Min(cw, ch) >= 60; seviye++)
            {
                Seviye(cur, cw, ch, sc, seviye, cikti);
                // bir sonraki piramit seviyesi (2x2 ortalama)
                int nw = cw / 2, nh = ch / 2;
                var nx = new byte[nw * nh];
                for (int y = 0; y < nh; y++)
                    for (int x = 0; x < nw; x++)
                    {
                        int i = (2 * y) * cw + 2 * x;
                        nx[y * nw + x] = (byte)((cur[i] + cur[i + 1] + cur[i + cw] + cur[i + cw + 1] + 2) >> 2);
                    }
                cur = nx; cw = nw; ch = nh; sc *= 2f;
            }
            // seviyeler arasi tekrarlari ele
            var son = new List<ArucoSonuc>();
            foreach (var a in cikti)
            {
                bool tekrar = false;
                for (int i = 0; i < son.Count; i++)
                {
                    var b = son[i];
                    if (b.id != a.id) continue;
                    float ax = (a.kose[0] + a.kose[2] + a.kose[4] + a.kose[6]) * 0.25f, ay = (a.kose[1] + a.kose[3] + a.kose[5] + a.kose[7]) * 0.25f;
                    float bx = (b.kose[0] + b.kose[2] + b.kose[4] + b.kose[6]) * 0.25f, by = (b.kose[1] + b.kose[3] + b.kose[5] + b.kose[7]) * 0.25f;
                    float d = (float)Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
                    if (d < 0.5f * Math.Max(a.boyut, b.boyut))
                    {
                        tekrar = true;
                        if (a.hamming < b.hamming) son[i] = a;
                        break;
                    }
                }
                if (!tekrar) son.Add(a);
            }
            return son;
        }

        static void Buyut<T>(ref T[] d, int n) { if (d.Length < n) d = new T[n]; }

        int Bul(int a)
        {
            while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; }
            return a;
        }

        void Seviye(byte[] g, int w, int h, float olcek, int seviye, List<ArucoSonuc> cikti)
        {
            int n = w * h, W1 = w + 1;
            Buyut(ref integ, W1 * (h + 1)); Buyut(ref lab, n); Buyut(ref bin, n);
            for (int x = 0; x < W1; x++) integ[x] = 0;
            for (int y = 0; y < h; y++)
            {
                int row = 0, o = (y + 1) * W1;
                integ[o] = 0;
                for (int x = 0; x < w; x++)
                {
                    row += g[y * w + x];
                    integ[o + x + 1] = integ[o - W1 + x + 1] + row;
                }
            }
            int r = YariPencere;
            for (int y = 0; y < h; y++)
            {
                int y0 = Math.Max(0, y - r), y1 = Math.Min(h - 1, y + r);
                for (int x = 0; x < w; x++)
                {
                    int x0 = Math.Max(0, x - r), x1 = Math.Min(w - 1, x + r);
                    int sum = integ[(y1 + 1) * W1 + x1 + 1] - integ[y0 * W1 + x1 + 1] - integ[(y1 + 1) * W1 + x0] + integ[y0 * W1 + x0];
                    int alan = (x1 - x0 + 1) * (y1 - y0 + 1);
                    bin[y * w + x] = (byte)(g[y * w + x] * alan < sum - Sabit * alan ? 1 : 0);
                }
            }

            // 4-bagli bilesenler
            int nl = 0;
            if (parent.Length < 1024) parent = new int[1024];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (bin[i] == 0) { lab[i] = 0; continue; }
                    int l = x > 0 ? lab[i - 1] : 0;
                    int u = y > 0 ? lab[i - w] : 0;
                    if (l == 0 && u == 0)
                    {
                        nl++;
                        if (nl >= parent.Length) Array.Resize(ref parent, parent.Length * 2);
                        parent[nl] = nl; lab[i] = nl;
                    }
                    else if (l != 0 && u != 0)
                    {
                        lab[i] = l;
                        int a = Bul(l), b = Bul(u);
                        if (a != b) { if (a < b) parent[b] = a; else parent[a] = b; }
                    }
                    else lab[i] = l != 0 ? l : u;
                }
            Buyut(ref cnt, nl + 1); Buyut(ref mnx, nl + 1); Buyut(ref mxx, nl + 1); Buyut(ref mny, nl + 1); Buyut(ref mxy, nl + 1);
            for (int k = 0; k <= nl; k++) { cnt[k] = 0; mnx[k] = int.MaxValue; mxx[k] = -1; mny[k] = int.MaxValue; mxy[k] = -1; }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    int l = lab[i];
                    if (l == 0) continue;
                    l = Bul(l); lab[i] = l;
                    cnt[l]++;
                    if (x < mnx[l]) mnx[l] = x; if (x > mxx[l]) mxx[l] = x;
                    if (y < mny[l]) mny[l] = y; if (y > mxy[l]) mxy[l] = y;
                }

            int aday = 0;
            int maxK = (int)(Math.Min(w, h) * 0.98f);
            for (int l = 1; l <= nl && aday < MaksAday; l++)
            {
                if (parent[l] != l || cnt[l] == 0) continue;
                int bw = mxx[l] - mnx[l] + 1, bh = mxy[l] - mny[l] + 1;
                if (bw < MinKenar || bh < MinKenar || bw > maxK || bh > maxK) continue;
                float asp = bw / (float)bh;
                if (asp > 3.5f || asp < 1f / 3.5f) continue;
                if (cnt[l] < 0.15f * bw * bh) continue;
                aday++;
                Aday(g, w, h, l, mnx[l], mxx[l], mny[l], mxy[l], cnt[l], olcek, seviye, cikti);
            }
        }

        static float Capraz(Nk o, Nk a, Nk b) { return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x); }

        void Aday(byte[] g, int w, int h, int etiket, int x0, int x1, int y0, int y1, int sayi, float olcek, int seviye, List<ArucoSonuc> cikti)
        {
            hamNokta.Clear();
            for (int y = y0; y <= y1; y++)
            {
                int o = y * w;
                int sol = -1, sag = -1;
                for (int x = x0; x <= x1; x++) if (lab[o + x] == etiket) { sol = x; break; }
                if (sol < 0) continue;
                for (int x = x1; x >= sol; x--) if (lab[o + x] == etiket) { sag = x; break; }
                hamNokta.Add(new Nk { x = sol, y = y });
                if (sag != sol) hamNokta.Add(new Nk { x = sag, y = y });
            }
            if (hamNokta.Count < 4) return;
            hamNokta.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            // monoton zincir kabuk
            hull.Clear();
            for (int i = 0; i < hamNokta.Count; i++)
            {
                while (hull.Count >= 2 && Capraz(hull[hull.Count - 2], hull[hull.Count - 1], hamNokta[i]) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(hamNokta[i]);
            }
            int alt = hull.Count + 1;
            for (int i = hamNokta.Count - 2; i >= 0; i--)
            {
                while (hull.Count >= alt && Capraz(hull[hull.Count - 2], hull[hull.Count - 1], hamNokta[i]) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(hamNokta[i]);
            }
            hull.RemoveAt(hull.Count - 1);
            int m = hull.Count;
            if (m < 4) return;
            double hullAlan = 0;
            for (int i = 0; i < m; i++) { var a = hull[i]; var b = hull[(i + 1) % m]; hullAlan += a.x * b.y - b.x * a.y; }
            hullAlan = Math.Abs(hullAlan) * 0.5;

            // kosegen: birbirine en uzak iki kabuk noktasi
            int ia = 0, ic = 0; float enUzak = -1;
            for (int i = 0; i < m; i++)
                for (int j = i + 1; j < m; j++)
                {
                    float dx = hull[i].x - hull[j].x, dy = hull[i].y - hull[j].y;
                    float d = dx * dx + dy * dy;
                    if (d > enUzak) { enUzak = d; ia = i; ic = j; }
                }
            var A = hull[ia]; var C = hull[ic];
            // iki zincirde AC dogrusuna en uzak noktalar
            int ib = -1, id = -1; float eb = 0, ed = 0;
            for (int k = (ia + 1) % m; k != ic; k = (k + 1) % m)
            {
                float d = Math.Abs(Capraz(A, C, hull[k]));
                if (d > eb) { eb = d; ib = k; }
            }
            for (int k = (ic + 1) % m; k != ia; k = (k + 1) % m)
            {
                float d = Math.Abs(Capraz(A, C, hull[k]));
                if (d > ed) { ed = d; id = k; }
            }
            if (ib < 0 || id < 0) return;
            var q = new Nk[] { A, hull[ib], C, hull[id] };

            // dortgen kalitesi
            double qAlan = 0;
            for (int i = 0; i < 4; i++) { var a = q[i]; var b = q[(i + 1) % 4]; qAlan += a.x * b.y - b.x * a.y; }
            qAlan = Math.Abs(qAlan) * 0.5;
            if (qAlan < 150) return;
            if (qAlan < 0.88 * hullAlan) return;
            if (sayi < 0.40 * qAlan) return;
            float mn = float.MaxValue, mx = 0;
            for (int i = 0; i < 4; i++)
            {
                var a = q[i]; var b = q[(i + 1) % 4];
                float l = (float)Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));
                mn = Math.Min(mn, l); mx = Math.Max(mx, l);
            }
            if (mn < 0.25f * mx) return;

            // piksel merkezi -> dis kose: merkezden 0.7 px disari
            float cx = (q[0].x + q[1].x + q[2].x + q[3].x) * 0.25f, cy = (q[0].y + q[1].y + q[2].y + q[3].y) * 0.25f;
            for (int i = 0; i < 4; i++)
            {
                float dx = q[i].x - cx, dy = q[i].y - cy; float l = (float)Math.Sqrt(dx * dx + dy * dy);
                if (l > 1e-3f) { q[i].x += dx / l * 0.7f; q[i].y += dy / l * 0.7f; }
            }
            // gorsel saat yonu (y asagi): capraz > 0
            if (Capraz(q[0], q[1], q[2]) < 0) { var t = q[1]; q[1] = q[3]; q[3] = t; }

            if (!Coz(g, w, h, q, out int id0, out int ham, out int dn)) return;
            var s = new ArucoSonuc { id = id0, hamming = ham, kose = new float[8], seviye = seviye, boyut = (float)Math.Sqrt(qAlan) * olcek };
            for (int i = 0; i < 4; i++)
            {
                var p = q[(dn + i) & 3];
                s.kose[i * 2] = (p.x + 0.5f) * olcek - 0.5f;
                s.kose[i * 2 + 1] = (p.y + 0.5f) * olcek - 0.5f;
            }
            cikti.Add(s);
        }

        static void Homografi(Nk[] q, out float a, out float b, out float c, out float d, out float e, out float f, out float gg, out float hh)
        {
            float x0 = q[0].x, y0 = q[0].y, x1 = q[1].x, y1 = q[1].y, x2 = q[2].x, y2 = q[2].y, x3 = q[3].x, y3 = q[3].y;
            float dx1 = x1 - x2, dx2 = x3 - x2, dx3 = x0 - x1 + x2 - x3;
            float dy1 = y1 - y2, dy2 = y3 - y2, dy3 = y0 - y1 + y2 - y3;
            float det = dx1 * dy2 - dx2 * dy1;
            if (Math.Abs(dx3) < 1e-6f && Math.Abs(dy3) < 1e-6f || Math.Abs(det) < 1e-9f) { gg = 0; hh = 0; }
            else { gg = (dx3 * dy2 - dx2 * dy3) / det; hh = (dx1 * dy3 - dx3 * dy1) / det; }
            a = x1 - x0 + gg * x1; b = x3 - x0 + hh * x3; c = x0;
            d = y1 - y0 + gg * y1; e = y3 - y0 + hh * y3; f = y0;
        }

        static float Bilin(byte[] g, int w, int h, float x, float y)
        {
            if (x < 0) x = 0; if (y < 0) y = 0;
            if (x > w - 1.001f) x = w - 1.001f; if (y > h - 1.001f) y = h - 1.001f;
            int ix = (int)x, iy = (int)y; float fx = x - ix, fy = y - iy;
            int i = iy * w + ix;
            float a = g[i] + (g[i + 1] - g[i]) * fx;
            float b = g[i + w] + (g[i + w + 1] - g[i + w]) * fx;
            return a + (b - a) * fy;
        }

        bool Coz(byte[] g, int w, int h, Nk[] q, out int id, out int hamming, out int donus)
        {
            id = -1; hamming = 99; donus = 0;
            Homografi(q, out float a, out float b, out float c, out float d, out float e, out float f, out float gg, out float hh);
            const int N = 30; // 6 hucre x 5 ornek
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    float u = (i + 0.5f) / N, v = (j + 0.5f) / N;
                    float den = gg * u + hh * v + 1f;
                    if (Math.Abs(den) < 1e-6f) return false;
                    float px = (a * u + b * v + c) / den, py = (d * u + e * v + f) / den;
                    ornek[j * N + i] = Bilin(g, w, h, px, py);
                }
            // Otsu
            var hist = new int[256];
            for (int i = 0; i < ornek.Length; i++) hist[Math.Min(255, Math.Max(0, (int)ornek[i]))]++;
            int toplam = ornek.Length; double sumAll = 0; for (int i = 0; i < 256; i++) sumAll += i * hist[i];
            double sumB = 0; int wB = 0; double enBuyuk = -1; int esik = 128;
            for (int t = 0; t < 256; t++)
            {
                wB += hist[t]; if (wB == 0) continue;
                int wF = toplam - wB; if (wF == 0) break;
                sumB += t * hist[t];
                double mB = sumB / wB, mF = (sumAll - sumB) / wF;
                double v = (double)wB * wF * (mB - mF) * (mB - mF);
                if (v > enBuyuk) { enBuyuk = v; esik = t; }
            }
            // hucre ortalamalari (her hucrenin orta 3x3 ornegi)
            var bit = new int[36]; double koyuT = 0, aciT = 0; int koyuN = 0, aciN = 0;
            for (int r = 0; r < 6; r++)
                for (int k = 0; k < 6; k++)
                {
                    float s = 0;
                    for (int j = 1; j <= 3; j++) for (int i = 1; i <= 3; i++) s += ornek[(r * 5 + j) * N + k * 5 + i];
                    s /= 9f;
                    int bt = s > esik ? 1 : 0;
                    bit[r * 6 + k] = bt;
                    if (bt == 1) { aciT += s; aciN++; } else { koyuT += s; koyuN++; }
                }
            if (koyuN == 0 || aciN == 0) return false;
            if (aciT / aciN - koyuT / koyuN < 35) return false;
            int sinirHata = 0;
            for (int r = 0; r < 6; r++)
                for (int k = 0; k < 6; k++)
                    if ((r == 0 || r == 5 || k == 0 || k == 5) && bit[r * 6 + k] == 1) sinirHata++;
            if (sinirHata > 2) return false;

            var ic = new int[16];
            for (int r = 0; r < 4; r++) for (int k = 0; k < 4; k++) ic[r * 4 + k] = bit[(r + 1) * 6 + k + 1];
            int eniyi = 99, eniyiId = -1, eniyiK = 0;
            var tmp = new int[16];
            for (int k = 0; k < 4; k++)
            {
                int kod = 0; for (int i = 0; i < 16; i++) kod = (kod << 1) | ic[i];
                for (int s = 0; s < ArucoSozluk.Kodlar.Length; s++)
                {
                    int x = kod ^ ArucoSozluk.Kodlar[s];
                    int hd = 0; while (x != 0) { hd++; x &= x - 1; }
                    if (hd < eniyi) { eniyi = hd; eniyiId = s; eniyiK = k; }
                }
                // 90 derece saat yonu: yeni[r][c] = eski[3-c][r]
                for (int r = 0; r < 4; r++) for (int c2 = 0; c2 < 4; c2++) tmp[r * 4 + c2] = ic[(3 - c2) * 4 + r];
                Array.Copy(tmp, ic, 16);
            }
            if (eniyi > MaksHamming) return false;
            id = eniyiId; hamming = eniyi; donus = (4 - eniyiK) & 3;
            return true;
        }
    }
}
