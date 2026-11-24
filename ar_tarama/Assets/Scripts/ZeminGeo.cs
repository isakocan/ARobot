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

        /// <summary>Kenar duzlestirme (24 Kasim, kullanici: "detayli alalim ama duz hatlarda, biraz kirpilmasi sorun degil").
        /// Odanin ana yonune (aci) hizali iki eksende: (1) en cok 2 hucrelik girintiler doldurulur (yasak = engel / duvar
        /// hucresine tasmaz), (2) iki eksenin hicbirinde en az 3 hucrelik dizinin parcasi olmayan cikinti hucreleri atilir.
        /// Esya bicimi (detay) kalir, 5 cm'lik testere disi ve tek hucre sacaklar gider.</summary>
        public static void Duzlestir(HashSet<long> acik, float aci, Func<long, bool> yasak, int tekrar = 2)
        {
            if (acik.Count == 0) return;
            float ux = (float)Math.Cos(aci), uz = (float)Math.Sin(aci);
            var eksen = new[] { (ux, uz), (-uz, ux) };
            float V = ZeminSonuc.V;
            Func<float, float, long> hucre = (x, z) => OdaSonuc.Anah2(ZeminSonuc.Ind(x), ZeminSonuc.Ind(z));
            for (int t = 0; t < tekrar; t++)
            {
                // 1) girinti doldurma
                var aday = new HashSet<long>();
                foreach (long k in acik)
                {
                    ZeminSonuc.Coz(k, out int ix, out int iz);
                    for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) { long q = OdaSonuc.Anah2(ix + dx, iz + dz); if (!acik.Contains(q)) aday.Add(q); }
                }
                var ekle = new List<long>();
                foreach (long k in aday)
                {
                    if (yasak != null && yasak(k)) continue;
                    ZeminSonuc.Coz(k, out int ix, out int iz); float x = (ix + 0.5f) * V, z = (iz + 0.5f) * V; bool dol = false;
                    foreach (var (ex, ez) in eksen)
                    {
                        for (int a1 = 1; a1 <= 2 && !dol; a1++)
                            for (int a2 = 1; a1 + a2 <= 3 && !dol; a2++)
                                if (acik.Contains(hucre(x - ex * V * a1, z - ez * V * a1)) && acik.Contains(hucre(x + ex * V * a2, z + ez * V * a2))) dol = true;
                        if (dol) break;
                    }
                    if (dol) ekle.Add(k);
                }
                foreach (long k in ekle) acik.Add(k);
                // 2) cikinti atma
                var sil = new List<long>();
                foreach (long k in acik)
                {
                    ZeminSonuc.Coz(k, out int ix, out int iz); float x = (ix + 0.5f) * V, z = (iz + 0.5f) * V; bool kal = false;
                    foreach (var (ex, ez) in eksen)
                    {
                        int n = 1;
                        for (int a = 1; a <= 2 && acik.Contains(hucre(x + ex * V * a, z + ez * V * a)); a++) n++;
                        for (int a = 1; a <= 2 && acik.Contains(hucre(x - ex * V * a, z - ez * V * a)); a++) n++;
                        if (n >= 3) { kal = true; break; }
                    }
                    if (!kal) sil.Add(k);
                }
                foreach (long k in sil) acik.Remove(k);
            }
        }

        /// <summary>Morfolojik acma (r hucre yaricapli disk): once asindir, sonra genislet. r'den ince her sey gider.</summary>
        public static void Ac(HashSet<long> h, int r)
        {
            if (r <= 0 || h.Count == 0) return;
            var ofs = new List<(int, int)>();
            for (int dz = -r; dz <= r; dz++) for (int dx = -r; dx <= r; dx++) if (dx * dx + dz * dz <= r * r) ofs.Add((dx, dz));
            var cekirdek = new List<long>();
            foreach (long k in h)
            {
                ZeminSonuc.Coz(k, out int ix, out int iz); bool tam = true;
                foreach (var o in ofs) if (!h.Contains(OdaSonuc.Anah2(ix + o.Item1, iz + o.Item2))) { tam = false; break; }
                if (tam) cekirdek.Add(k);
            }
            var yeni = new HashSet<long>();
            foreach (long k in cekirdek)
            {
                ZeminSonuc.Coz(k, out int ix, out int iz);
                foreach (var o in ofs) { long q = OdaSonuc.Anah2(ix + o.Item1, iz + o.Item2); if (h.Contains(q)) yeni.Add(q); }
            }
            h.IntersectWith(yeni);
        }

        /// <summary>Acik zemin icindeki kucuk delikleri (en cok 'enCok' hucre, icinde engel yok, kenara degmiyor) doldurur.</summary>
        public static void DelikDoldur(HashSet<long> acik, HashSet<long> engel, int enCok)
        {
            if (acik.Count == 0) return;
            int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue;
            foreach (long k in acik) { ZeminSonuc.Coz(k, out int ix, out int iz); if (ix < x0) x0 = ix; if (ix > x1) x1 = ix; if (iz < z0) z0 = iz; if (iz > z1) z1 = iz; }
            var gez = new HashSet<long>(); var yigin = new Stack<long>(); var parca = new List<long>(); var ekle = new List<long>();
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    long k0 = OdaSonuc.Anah2(x, z);
                    if (acik.Contains(k0) || !gez.Add(k0)) continue;
                    parca.Clear(); yigin.Push(k0); bool gecersiz = false;
                    while (yigin.Count > 0)
                    {
                        long k = yigin.Pop(); parca.Add(k); ZeminSonuc.Coz(k, out int ix, out int iz);
                        if (ix <= x0 || ix >= x1 || iz <= z0 || iz >= z1 || engel.Contains(k) || parca.Count > enCok) gecersiz = true;
                        if (parca.Count > enCok) break;
                        foreach (var q in new[] { OdaSonuc.Anah2(ix + 1, iz), OdaSonuc.Anah2(ix - 1, iz), OdaSonuc.Anah2(ix, iz + 1), OdaSonuc.Anah2(ix, iz - 1) })
                            if (!acik.Contains(q) && gez.Add(q)) yigin.Push(q);
                    }
                    if (gecersiz)
                    {
                        // tasan parcanin kalanini da gezilmis say (dis bolge), tekrar baslamasin
                        while (yigin.Count > 0) { long k = yigin.Pop(); ZeminSonuc.Coz(k, out int ix, out int iz); foreach (var q in new[] { OdaSonuc.Anah2(ix + 1, iz), OdaSonuc.Anah2(ix - 1, iz), OdaSonuc.Anah2(ix, iz + 1), OdaSonuc.Anah2(ix, iz - 1) }) { ZeminSonuc.Coz(q, out int qx, out int qz); if (qx >= x0 && qx <= x1 && qz >= z0 && qz <= z1 && !acik.Contains(q) && gez.Add(q)) yigin.Push(q); } }
                        continue;
                    }
                    ekle.AddRange(parca);
                }
            foreach (long k in ekle) acik.Add(k);
        }

        /// <summary>Acik zemin kumesinin ana yone (aci) hizali izgarada duz kenarli konturu: hucre merkezleri dondurulmus
        /// 5 cm izgaraya orneklenir, 3x3 cogunluk suzgeci (tek hucrelik basamak gurultusu), bulaniksiz marching squares;
        /// koseler dunyaya geri dondurulur. Kenarlar ana yone paralel duz cizgiler (koselerde 45 derece pah).</summary>
        public static void DuzKontur(HashSet<long> acik, float aci, List<float> ucgen, List<float> kenar, int kapama = 2, int acma = 2)
        {
            if (acik.Count == 0) return;
            float ca = (float)Math.Cos(aci), sa = (float)Math.Sin(aci);
            const float C = 0.10f;
            var k0 = Izgara(acik, ca, sa, out int CW, out int CH, out int cu0, out int cw0, out _);
            if (k0 == null) return;
            // 24 Kasim: eski sabit 2+2 adim (3x3 cekirdek, 10 cm izgara) 50 cm'den dar her kolu siliyor, ana yon yanlis
            // secilince (19:45: -45 derece) alani elmas / X bicimine ceviriyordu. Adim sayisi artik parametre.
            k0 = Morf(k0, CW, CH, kapama, acma);
            // cizimde ayri kalan kucuk adaciklar (0,25 m2'den kucuk) atilir
            {
                var gez = new bool[CW * CH]; var yig = new Stack<int>(); var parca = new List<int>();
                for (int i0 = 0; i0 < CW * CH; i0++)
                {
                    if (!k0[i0] || gez[i0]) continue;
                    parca.Clear(); yig.Push(i0); gez[i0] = true;
                    while (yig.Count > 0)
                    {
                        int i = yig.Pop(); parca.Add(i); int x = i % CW, y = i / CW;
                        if (x > 0 && k0[i - 1] && !gez[i - 1]) { gez[i - 1] = true; yig.Push(i - 1); }
                        if (x < CW - 1 && k0[i + 1] && !gez[i + 1]) { gez[i + 1] = true; yig.Push(i + 1); }
                        if (y > 0 && k0[i - CW] && !gez[i - CW]) { gez[i - CW] = true; yig.Push(i - CW); }
                        if (y < CH - 1 && k0[i + CW] && !gez[i + CW]) { gez[i + CW] = true; yig.Push(i + CW); }
                    }
                    if (parca.Count < 25) foreach (int i in parca) k0[i] = false;
                }
            }
            // 5 cm izgaraya buyut (kontur 5 cm'de: koselerde yalniz 2,5 cm pah)
            int W = CW * 2, H = CH * 2; int gu0 = cu0 * 2, gw0 = cw0 * 2;
            var b = new float[W * H];
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (k0[(y >> 1) * CW + (x >> 1)]) b[y * W + x] = 1f;
            var uc = new List<float>(); var ke = kenar != null ? new List<float>() : null;
            Kontur(b, W, H, gu0, gw0, 1, 0.5f, uc, ke);
            for (int i = 0; i + 1 < uc.Count; i += 2) { float u = uc[i], w = uc[i + 1]; ucgen.Add(u * ca - w * sa); ucgen.Add(u * sa + w * ca); }
            if (ke != null) for (int i = 0; i + 1 < ke.Count; i += 2) { float u = ke[i], w = ke[i + 1]; kenar.Add(u * ca - w * sa); kenar.Add(u * sa + w * ca); }
        }

        /// <summary>Hucre kumesinin ana yone (ca, sa) hizali 10 cm izgarasi: kaba hucre, 4 ornegin en az 3'u aciksa acik.
        /// kismi: 1-2 ornegi acik (kenar izgaraya oturmuyor) kaba hucre sayisi.</summary>
        static bool[] Izgara(HashSet<long> acik, float ca, float sa, out int CW, out int CH, out int cu0, out int cw0, out int kismi)
        {
            float V = ZeminSonuc.V; const float C = 0.10f; kismi = 0;
            float u0 = float.MaxValue, u1 = float.MinValue, w0 = float.MaxValue, w1 = float.MinValue;
            foreach (long k in acik)
            {
                ZeminSonuc.Coz(k, out int ix, out int iz); float x = (ix + 0.5f) * V, z = (iz + 0.5f) * V;
                float u = x * ca + z * sa, w = -x * sa + z * ca;
                if (u < u0) u0 = u; if (u > u1) u1 = u; if (w < w0) w0 = w; if (w > w1) w1 = w;
            }
            cu0 = (int)Math.Floor(u0 / C) - 3; cw0 = (int)Math.Floor(w0 / C) - 3;
            CW = (int)Math.Floor(u1 / C) - cu0 + 4; CH = (int)Math.Floor(w1 / C) - cw0 + 4;
            if ((long)CW * CH > 1000000) return null;
            var k0 = new bool[CW * CH];
            for (int y = 0; y < CH; y++)
                for (int x = 0; x < CW; x++)
                {
                    int n = 0;
                    for (int q = 0; q < 4; q++)
                    {
                        float u = (cu0 + x + 0.25f + 0.5f * (q & 1)) * C, w = (cw0 + y + 0.25f + 0.5f * (q >> 1)) * C;
                        float wx = u * ca - w * sa, wz = u * sa + w * ca;
                        if (acik.Contains(OdaSonuc.Anah2((int)Math.Floor(wx / V), (int)Math.Floor(wz / V)))) n++;
                    }
                    k0[y * CW + x] = n >= 3; if (n == 1 || n == 2) kismi++;
                }
            return k0;
        }

        static bool[] Morf(bool[] k0, int CW, int CH, int kapama, int acma)
        {
            for (int i = 0; i < kapama; i++) k0 = Kare(k0, CW, CH, true);
            for (int i = 0; i < kapama; i++) k0 = Kare(k0, CW, CH, false);
            for (int i = 0; i < acma; i++) k0 = Kare(k0, CW, CH, false);
            for (int i = 0; i < acma; i++) k0 = Kare(k0, CW, CH, true);
            return k0;
        }

        /// <summary>Kumenin duz kenarli cizimi icin en iyi izgara acisi (0-90 derece): 5 derece adimla, sonra en iyinin
        /// +-4 derecesinde 1 derece adimla; hata = izgaraya oturmayan kaba hucre + duzlestirmenin degistirdigi kaba hucre.
        /// 19:45 taramasi: odanin ana yonu -45 secilmisti, oyun alani icin en iyi aci 0 (fark 2,2 -> 0,75 m2).</summary>
        public static float EnIyiAci(HashSet<long> acik, int kapama, int acma)
        {
            if (acik.Count == 0) return 0f;
            int enD = 0, enH = int.MaxValue;
            Func<int, int> hata = d =>
            {
                double a = d * Math.PI / 180; var k0 = Izgara(acik, (float)Math.Cos(a), (float)Math.Sin(a), out int CW, out int CH, out _, out _, out int kismi);
                if (k0 == null) return int.MaxValue;
                var k1 = Morf(k0, CW, CH, kapama, acma); int f = 0; for (int i = 0; i < k0.Length; i++) if (k0[i] != k1[i]) f++;
                return f + kismi;
            };
            for (int d = 0; d < 90; d += 5) { int h = hata(d); if (h < enH) { enH = h; enD = d; } }
            int d0 = enD; for (int d = d0 - 4; d <= d0 + 4; d++) { if (d == d0) continue; int h = hata(d); if (h < enH) { enH = h; enD = d; } }
            return (float)(enD * Math.PI / 180);
        }

        /// <summary>3x3 kare cekirdekle genisletme (genislet=true) ya da asindirma.</summary>
        static bool[] Kare(bool[] a, int W, int H, bool genislet)
        {
            var b = new bool[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    bool r = !genislet;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx, yy = y + dy; bool v = xx >= 0 && yy >= 0 && xx < W && yy < H && a[yy * W + xx];
                            if (genislet && v) r = true; else if (!genislet && !v) r = false;
                        }
                    b[y * W + x] = r;
                }
            return b;
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
