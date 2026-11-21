using System;
using System.Collections.Generic;

namespace ArTarama
{
    /// <summary>
    /// Zemin haritasinin "temiz" gosterimi (saf C#): kucuk gurultu lekeleri atilir, acik zemin ve engel alanlari
    /// yumusatilip (bulanik alan + 0,5 esik) marching squares ile duzgun kenarli cokgenlere cevrilir. Kare kare boya yerine
    /// birlesmis, yuvarlak kenarli alanlar ve ince bir kenar cizgisi cizilir.
    /// </summary>
    public static class ZeminGeo
    {
        /// <summary>8-komsu bagli bilesenler; 'min'den kucuk olanlar kumeden cikarilip 'atilan'a eklenir.</summary>
        public static void KucukleriAt(HashSet<long> h, int min, HashSet<long> atilan)
        {
            var gez = new HashSet<long>(); var yigin = new Stack<long>(); var parca = new List<long>();
            var sil = new List<long>();
            foreach (long k0 in h)
            {
                if (!gez.Add(k0)) continue;
                parca.Clear(); yigin.Push(k0);
                while (yigin.Count > 0)
                {
                    long k = yigin.Pop(); parca.Add(k); ZeminSonuc.Coz(k, out int ix, out int iz);
                    for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                    {
                        long q = OdaSonuc.Anah2(ix + dx, iz + dz);
                        if (h.Contains(q) && gez.Add(q)) yigin.Push(q);
                    }
                }
                if (parca.Count < min) sil.AddRange(parca);
            }
            foreach (long k in sil) { h.Remove(k); atilan?.Add(k); }
        }

        /// <summary>Hucre kumesinden 0..1 alan (5 cm), iki kez 3x3 kutu bulaniklastirma (yaklasik Gauss, ~10 cm).</summary>
        public static float[] Alan(HashSet<long> h, int gx0, int gz0, int W, int H)
        {
            var a = new float[W * H];
            foreach (long k in h)
            {
                ZeminSonuc.Coz(k, out int ix, out int iz); int x = ix - gx0, y = iz - gz0;
                if (x >= 0 && y >= 0 && x < W && y < H) a[y * W + x] = 1f;
            }
            var t = new float[W * H];
            for (int tekrar = 0; tekrar < 2; tekrar++) { Bulanik(a, t, W, H); var g = a; a = t; t = g; }
            return a;
        }

        static void Bulanik(float[] a, float[] b, int W, int H)
        {
            var tmp = new float[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float s = a[y * W + x]; int n = 1;
                    if (x > 0) { s += a[y * W + x - 1]; n++; }
                    if (x < W - 1) { s += a[y * W + x + 1]; n++; }
                    tmp[y * W + x] = s / n;
                }
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float s = tmp[y * W + x]; int n = 1;
                    if (y > 0) { s += tmp[(y - 1) * W + x]; n++; }
                    if (y < H - 1) { s += tmp[(y + 1) * W + x]; n++; }
                    b[y * W + x] = s / n;
                }
        }

        /// <summary>
        /// Marching squares (doldurulmus): alan F'nin 'esik' ustu bolgesi, 'adim' hucrelik kafeste ucgenlere (x,z uclu
        /// koseler, dunya metre) ve kenar parcalarina (x0,z0,x1,z1) cevrilir. Hucre merkezi = (gx0 + i + 0,5) * V.
        /// </summary>
        public static void Kontur(float[] F, int W, int H, int gx0, int gz0, int adim, float esik, List<float> ucgen, List<float> kenar)
        {
            float V = ZeminSonuc.V;
            var px = new float[8]; var pz = new float[8]; var kesisim = new bool[8];
            var cx = new int[4]; var cz = new int[4]; var cv = new float[4];
            for (int y = 0; y + adim < H; y += adim)
                for (int x = 0; x + adim < W; x += adim)
                {
                    // koseler saat yonunun tersine: sol-alt, sag-alt, sag-ust, sol-ust
                    cx[0] = x; cz[0] = y; cx[1] = x + adim; cz[1] = y; cx[2] = x + adim; cz[2] = y + adim; cx[3] = x; cz[3] = y + adim;
                    int ic = 0;
                    for (int i = 0; i < 4; i++) { cv[i] = F[cz[i] * W + cx[i]]; if (cv[i] >= esik) ic++; }
                    if (ic == 0) continue;
                    int n = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        int j = (i + 1) & 3;
                        bool ai = cv[i] >= esik, aj = cv[j] >= esik;
                        if (ai) { px[n] = (gx0 + cx[i] + 0.5f) * V; pz[n] = (gz0 + cz[i] + 0.5f) * V; kesisim[n] = false; n++; }
                        if (ai != aj)
                        {
                            float t = (esik - cv[i]) / (cv[j] - cv[i]);
                            px[n] = (gx0 + cx[i] + (cx[j] - cx[i]) * t + 0.5f) * V; pz[n] = (gz0 + cz[i] + (cz[j] - cz[i]) * t + 0.5f) * V; kesisim[n] = true; n++;
                        }
                    }
                    for (int i = 1; i + 1 < n; i++)
                    {
                        ucgen.Add(px[0]); ucgen.Add(pz[0]); ucgen.Add(px[i]); ucgen.Add(pz[i]); ucgen.Add(px[i + 1]); ucgen.Add(pz[i + 1]);
                    }
                    if (kenar != null && ic < 4)
                        for (int i = 0; i < n; i++)
                        {
                            int j = (i + 1) % n;
                            if (kesisim[i] && kesisim[j]) { kenar.Add(px[i]); kenar.Add(pz[i]); kenar.Add(px[j]); kenar.Add(pz[j]); }
                        }
                }
        }
    }

    /// <summary>
    /// Kullanicinin bulundugu yerden gorus tablosu (saf C#): 720 isin (0,5 derece), 5 cm adimla 'duvar' (uzun engel)
    /// hucresine kadarki mesafe. Duvarin arkasindaki zemin boyasi ve konan nesneler cizilmez (baska odadan gorunmesin).
    /// </summary>
    public class GorusTablosu
    {
        public const int Isin = 720; public const float Menzil = 9f;
        readonly float[] mesafe = new float[Isin];
        public float ox, oz; public bool Hazir;

        public void Kur(HashSet<long> duvar, float x, float z)
        {
            ox = x; oz = z; Hazir = true;
            float V = ZeminSonuc.V;
            for (int b = 0; b < Isin; b++)
            {
                double a = (b + 0.5) * 2 * Math.PI / Isin; float dx = (float)Math.Sin(a), dz = (float)Math.Cos(a);
                float r = 0.3f, son = Menzil;
                for (; r < Menzil; r += V)
                {
                    long k = OdaSonuc.Anah2(ZeminSonuc.Ind(x + dx * r), ZeminSonuc.Ind(z + dz * r));
                    if (duvar.Contains(k)) { son = r; break; }
                }
                mesafe[b] = son;
            }
            // tek isin delikleri (duvardaki gurultu bosluklari) komsularla kapatilir
            var t = (float[])mesafe.Clone();
            for (int b = 0; b < Isin; b++)
            {
                float a = t[(b + Isin - 1) % Isin], c = t[(b + 1) % Isin];
                if (t[b] > Math.Max(a, c) + 0.5f) mesafe[b] = Math.Max(a, c);
            }
        }

        /// <summary>0..1 gorunurluk: duvarin onunde 1, arkasinda 0 (15 cm yumusak gecis).</summary>
        public float Gorunur(float x, float z)
        {
            if (!Hazir) return 1f;
            float dx = x - ox, dz = z - oz; float r = (float)Math.Sqrt(dx * dx + dz * dz);
            if (r < 0.3f) return 1f;
            double a = Math.Atan2(dx, dz); if (a < 0) a += 2 * Math.PI;
            int b = Math.Min(Isin - 1, (int)(a / (2 * Math.PI) * Isin));
            float d = mesafe[b] + 0.15f - r;
            return d >= 0.15f ? 1f : d <= 0f ? 0f : d / 0.15f;
        }
    }
}
