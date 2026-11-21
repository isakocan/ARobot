using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ArTarama
{
    // =====================================================================================================
    // Acik zemin haritasi (saf C#, UnityEngine yok). Hedef: AR nesnesi ve robot icin "uzerinde hicbir sey olmayan
    // zemin" — duvar/esya modeli yok. Cok odali gezinmeye uygun: harita gezdikce buyur, sinirsiz.
    //   zemin kaniti : ARCore'un zemin duzlemleri (en guvenilir kaynak), derinlikte zemin yuksekliginde gorulen noktalar,
    //                  kullanicinin yurudugu yol (kamera altindaki zemin bostur).
    //   engel kaniti : zeminden 8 cm - 1,7 m yukarida derinlik noktasi (masa, sandalye, duvar, kapi kasasi...). En az 3
    //                  ayri karede gorulmeli; ayni hucrede zemin cok daha sik goruluyorsa (gecen bir insan) engel sayilmaz.
    //   acik zemin   : zemin kaniti var, engel yok, en yakin engele en az 5 cm.
    // Hucre 5 cm. Koordinatlar Unity oturum uzayi (metre, y yukari).
    // =====================================================================================================
    public class ZeminSonuc
    {
        public bool zeminVar; public float zeminY;
        public HashSet<long> acik = new HashSet<long>(), engel = new HashSet<long>(), bilinen = new HashSet<long>();
        public float alan;                              // acik zemin, m2
        public float[] yonEksik = new float[16];        // kullanicidan 16 yonde 0,5-3 m arasi taranmamis zemin orani (engele kadar)
        public float[] sinir = new float[0];            // taranmamis alana komsu acik zemin (25 cm kaba hucre merkezleri, x,z)
        public float kameraX, kameraZ, zaman;
        public float[] iz = new float[0];               // kullanici yolu x,z (10 cm aralik)
        public int x0, z0, x1, z1;                      // hucre sinirlari (dahil)

        public const float V = 0.05f;
        public static int Ind(float v) { return (int)Math.Floor(v / V); }

        /// <summary>Nokta acik zeminde ve en yakin engele / bilinmeyene en az 'marj' uzakta mi?</summary>
        public bool AcikMi(float x, float z, float marj = 0.25f)
        {
            if (!zeminVar) return false;
            int ix = Ind(x), iz = Ind(z);
            if (!acik.Contains(OdaSonuc.Anah2(ix, iz))) return false;
            int r = (int)Math.Ceiling(marj / V), r2 = r * r;
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dz * dz > r2) continue;
                    long k = OdaSonuc.Anah2(ix + dx, iz + dz);
                    if (engel.Contains(k)) return false;
                }
            return true;
        }

        public static void Coz(long k, out int ix, out int iz) { ix = (int)((k >> 21) & 0x1FFFFF) - 1048576; iz = (int)(k & 0x1FFFFF) - 1048576; }

        /// <summary>Kusbakisi RGBA goruntu (satir 0 = en kucuk z, sutun 0 = en kucuk x; Unity Texture2D sirasi). 1 piksel = 1 hucre.</summary>
        public byte[] Resim(out int W, out int H, out float ox, out float oz, int pay = 4)
        {
            int ax = x0 - pay, az = z0 - pay; W = Math.Max(1, x1 - x0 + 1 + 2 * pay); H = Math.Max(1, z1 - z0 + 1 + 2 * pay);
            ox = ax * V; oz = az * V;
            var p = new byte[W * H * 4];
            foreach (long k in bilinen) Boya(p, W, H, k, ax, az, 60, 70, 80, 150);       // gorulmus ama acik degil (kenar payi)
            foreach (long k in acik) Boya(p, W, H, k, ax, az, 67, 217, 190, 235);
            foreach (long k in engel) Boya(p, W, H, k, ax, az, 214, 218, 225, 255);
            return p;
        }

        static void Boya(byte[] p, int W, int H, long k, int ax, int az, byte r, byte g, byte b, byte a)
        {
            Coz(k, out int ix, out int iz); int x = ix - ax, y = iz - az;
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            int o = (y * W + x) * 4; p[o] = r; p[o + 1] = g; p[o + 2] = b; p[o + 3] = a;
        }

        // ---------------------------------------------------------------- zemin.json
        public string Json(string oda)
        {
            var c = CultureInfo.InvariantCulture; var sb = new StringBuilder();
            sb.Append("{\"surum\":1,\"aciklama\":\"acik zemin haritasi; hucre 5 cm; acik/engel = [ix,iz,...] hucre indeksleri (x = ix*0.05)\"");
            sb.Append(",\"oda\":\"").Append((oda ?? "").Replace("\"", "'")).Append('"');
            sb.Append(",\"hucre\":0.05,\"zemin_y\":").Append(zeminY.ToString("F3", c)).Append(",\"alan_m2\":").Append(alan.ToString("F2", c));
            Liste(sb, "acik", acik); Liste(sb, "engel", engel); Liste(sb, "bilinen", bilinen);
            sb.Append(",\"iz\":["); for (int i = 0; i < iz.Length; i++) { if (i > 0) sb.Append(','); sb.Append(iz[i].ToString("F2", c)); }
            sb.Append("]}");
            return sb.ToString();
        }

        static void Liste(StringBuilder sb, string ad, HashSet<long> h)
        {
            sb.Append(",\"").Append(ad).Append("\":["); bool ilk = true;
            foreach (long k in h) { Coz(k, out int ix, out int iz); if (!ilk) sb.Append(','); ilk = false; sb.Append(ix).Append(',').Append(iz); }
            sb.Append(']');
        }

        public static ZeminSonuc JsonOku(string j, out string oda)
        {
            var s = new ZeminSonuc { zeminVar = true };
            oda = Metin(j, "oda");
            s.zeminY = (float)Sayi(j, "zemin_y"); s.alan = (float)Sayi(j, "alan_m2");
            Doldur(Dizi(j, "acik"), s.acik); Doldur(Dizi(j, "engel"), s.engel); Doldur(Dizi(j, "bilinen"), s.bilinen);
            var iz = Dizi(j, "iz"); s.iz = new float[iz.Count]; for (int i = 0; i < iz.Count; i++) s.iz[i] = (float)iz[i];
            s.SinirHesapla();
            return s;
        }

        static void Doldur(List<double> a, HashSet<long> h) { for (int i = 0; i + 1 < a.Count; i += 2) h.Add(OdaSonuc.Anah2((int)a[i], (int)a[i + 1])); }

        static string Metin(string j, string ad)
        {
            int i = j.IndexOf("\"" + ad + "\":\"", StringComparison.Ordinal); if (i < 0) return "";
            i += ad.Length + 4; int e = j.IndexOf('"', i); return e > i ? j.Substring(i, e - i) : "";
        }

        static double Sayi(string j, string ad)
        {
            int i = j.IndexOf("\"" + ad + "\":", StringComparison.Ordinal); if (i < 0) return 0;
            i += ad.Length + 3; int e = i; while (e < j.Length && "+-.0123456789eE".IndexOf(j[e]) >= 0) e++;
            double.TryParse(j.Substring(i, e - i), NumberStyles.Float, CultureInfo.InvariantCulture, out double v); return v;
        }

        static List<double> Dizi(string j, string ad)
        {
            var r = new List<double>();
            int i = j.IndexOf("\"" + ad + "\":[", StringComparison.Ordinal); if (i < 0) return r;
            i += ad.Length + 4; int e = j.IndexOf(']', i); if (e < 0) return r;
            foreach (var p in j.Substring(i, e - i).Split(','))
                if (double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) r.Add(v);
            return r;
        }

        /// <summary>Sinir kutusu (hucre) ve kesif siniri (25 cm kaba: acik + komsusu hic bilinmeyen).</summary>
        public void SinirHesapla()
        {
            x0 = z0 = int.MaxValue; x1 = z1 = int.MinValue;
            foreach (var h in new[] { acik, engel, bilinen })
                foreach (long k in h) { Coz(k, out int ix, out int iz); if (ix < x0) x0 = ix; if (ix > x1) x1 = ix; if (iz < z0) z0 = iz; if (iz > z1) z1 = iz; }
            if (x0 == int.MaxValue) { x0 = z0 = 0; x1 = z1 = 0; }
            var kabaAcik = new HashSet<long>(); var kabaBil = new HashSet<long>();
            foreach (long k in acik) { Coz(k, out int ix, out int iz); kabaAcik.Add(OdaSonuc.Anah2(FloorDiv(ix, 5), FloorDiv(iz, 5))); }
            foreach (var h in new[] { acik, engel, bilinen }) foreach (long k in h) { Coz(k, out int ix, out int iz); kabaBil.Add(OdaSonuc.Anah2(FloorDiv(ix, 5), FloorDiv(iz, 5))); }
            var sn = new List<float>();
            foreach (long k in kabaAcik)
            {
                Coz(k, out int cx, out int cz);
                bool kenar = !kabaBil.Contains(OdaSonuc.Anah2(cx + 1, cz)) || !kabaBil.Contains(OdaSonuc.Anah2(cx - 1, cz)) || !kabaBil.Contains(OdaSonuc.Anah2(cx, cz + 1)) || !kabaBil.Contains(OdaSonuc.Anah2(cx, cz - 1));
                if (kenar) { sn.Add((cx + 0.5f) * 0.25f); sn.Add((cz + 0.5f) * 0.25f); }
            }
            sinir = sn.ToArray();
        }

        static int FloorDiv(int a, int b) { return a >= 0 ? a / b : -((-a + b - 1) / b); }
    }

    public class ZeminHaritasi
    {
        public const float V = ZeminSonuc.V;
        public const float ZeminBant = 0.05f, EngelAlt = 0.08f, EngelUst = 1.7f;
        public const int EngelMinKare = 3;
        public static int GuvenEsik = 128; public static bool SadeceGuvenli = true;   // gercek kayitlarda dusuk guvenli noktalar yurunen yolu bile engel yapiyordu

        class Hucre { public int zemin, engel, yol; public int kz = -1, ke = -1; public bool plan; }
        readonly Dictionary<long, Hucre> h = new Dictionary<long, Hucre>();
        readonly List<float> izX = new List<float>(), izZ = new List<float>();
        readonly float[] mat = new float[9];
        float zy; bool zyVar; int kare;
        // ARCore zemini zamanla 10-20 cm kayabiliyor (gercek kayitta olculdu): her karede derinlik noktalarinin zemin
        // cevresindeki (+-30 cm) yukseklik histogramindan (1 cm, yavas unutan) tepe alinir; siniflandirma buna gore yapilir.
        readonly float[] yHist = new float[60]; float yTaban = float.NaN, zyPlan;
        float[] px_ = new float[0], py_ = new float[0], pz_ = new float[0]; int[] pi_ = new int[0];
        float kamX, kamZ;
        readonly List<long> planHucre = new List<long>();

        public readonly long[] TeshisYk = new long[90];   // teshis: engel noktalarinin zemin ustu yuksekligi (2 cm)
        public float ZeminY => zy;
        public bool ZeminVar => zyVar;
        public int HucreSayisi => h.Count;

        public void Sifirla() { h.Clear(); izX.Clear(); izZ.Clear(); zyVar = false; kare = 0; planHucre.Clear(); }

        Hucre Al(long k) { if (!h.TryGetValue(k, out var c)) { c = new Hucre(); h[k] = c; } return c; }

        // ---------------------------------------------------------------- kare basina (arka plan is parcacigi)
        public void Isle(DerinlikKaresi f)
        {
            kare++; kamX = f.px; kamZ = f.pz;
            if (!zyVar) return;
            // kullanici yolu: kamera ayakta tutuluyorsa (zeminden 0,7-2,1 m) altindaki 15 cm yaricap bos zemindir
            float hc = f.py - zy;
            if (hc > 0.7f && hc < 2.1f)
            {
                int n = izX.Count;
                if (n == 0 || (f.px - izX[n - 1]) * (f.px - izX[n - 1]) + (f.pz - izZ[n - 1]) * (f.pz - izZ[n - 1]) > 0.01f)
                {
                    izX.Add(f.px); izZ.Add(f.pz);
                    int qx = ZeminSonuc.Ind(f.px), qz = ZeminSonuc.Ind(f.pz);
                    for (int dz = -3; dz <= 3; dz++) for (int dx = -3; dx <= 3; dx++) if (dx * dx + dz * dz <= 9) Al(OdaSonuc.Anah2(qx + dx, qz + dz)).yol++;
                }
            }
            DerinlikKaresi.DonmeMatrisi(f.qx, f.qy, f.qz, f.qw, mat);
            int n0 = f.w * f.h; if (px_.Length < n0) { px_ = new float[n0]; py_ = new float[n0]; pz_ = new float[n0]; pi_ = new int[n0]; }
            int m = 0;
            if (float.IsNaN(yTaban) || Math.Abs(zyPlan - (yTaban + 0.3f)) > 0.15f) { yTaban = zyPlan - 0.3f; Array.Clear(yHist, 0, yHist.Length); }
            for (int j = 0; j < yHist.Length; j++) yHist[j] *= 0.985f;
            for (int v = 0; v < f.h; v++)
                for (int u = 0; u < f.w; u++)
                {
                    int i = v * f.w + u; float d = f.d[i];
                    if (!(d >= 0.3f && d <= 4.0f)) continue;
                    if (f.e != null && f.e[i] == OdaEtiket.Insan) continue;
                    f.YerelYon(u, v, out float lx, out float ly);
                    float wx = mat[0] * lx + mat[1] * ly + mat[2], wy = mat[3] * lx + mat[4] * ly + mat[5], wz = mat[6] * lx + mat[7] * ly + mat[8];
                    px_[m] = f.px + wx * d; py_[m] = f.py + wy * d; pz_[m] = f.pz + wz * d; pi_[m] = i; m++;
                    int b = (int)Math.Floor((py_[m - 1] - yTaban) / 0.01f); if (b >= 0 && b < yHist.Length) yHist[b] += 1f;
                }
            {
                int en = -1; float enS = 0;
                for (int b = 2; b < yHist.Length - 2; b++) { float t = yHist[b - 2] + yHist[b - 1] + yHist[b] + yHist[b + 1] + yHist[b + 2]; if (t > enS) { enS = t; en = b; } }
                if (en >= 0 && enS > 300f) zy = yTaban + (en + 0.5f) * 0.01f; else zy = zyPlan;
            }
            for (int r = 0; r < m; r++)
                {
                    int i = pi_[r]; float d = f.d[i];
                    byte e = f.e != null ? f.e[i] : (byte)0;
                    float x = px_[r], y = py_[r], z = pz_[r], yk = y - zy;
                    bool zemin = yk > -ZeminBant && yk < ZeminBant || (e == OdaEtiket.Zemin && Math.Abs(yk) < 0.10f);
                    if (zemin)
                    {
                        var c = Al(OdaSonuc.Anah2(ZeminSonuc.Ind(x), ZeminSonuc.Ind(z)));
                        if (c.kz != kare) { c.kz = kare; c.zemin++; }
                    }
                    else if (yk >= EngelAlt && yk <= EngelUst)
                    {
                        // dusuk guvenli ARCore derinligi kareden kareye ~10 cm oynuyor (gercek kayitta olculdu): zemin noktalari
                        // engel gibi gorunmesin diye engel kaniti yalniz guvenli noktadan ya da zeminden belirgin yuksekten (20 cm+)
                        byte gv = f.g != null ? f.g[i] : (byte)255;
                        if (!(gv >= GuvenEsik || (yk >= 0.20f && gv >= 20 && d < 3.0f && !SadeceGuvenli))) continue;
                        TeshisYk[Math.Min(TeshisYk.Length - 1, (int)(yk / 0.02f))]++;
                        var c = Al(OdaSonuc.Anah2(ZeminSonuc.Ind(x), ZeminSonuc.Ind(z)));
                        if (c.ke != kare) { c.ke = kare; c.engel++; }
                    }
                }
        }

        // ---------------------------------------------------------------- yapi (1 Hz)
        public ZeminSonuc Guncelle(IList<OdaDuzlem> planlar, float zaman)
        {
            var s = new ZeminSonuc { zaman = zaman, kameraX = kamX, kameraZ = kamZ };
            // zemin yuksekligi: yukari bakan yatay duzlemler yukseklige gore kumelenir; toplam alani >= 0,5 m2 olan en alcak kume
            var yat = new List<KeyValuePair<float, float>>();
            foreach (var p in planlar)
                if (p.tur == 0 && p.sinir != null && p.sinir.Length >= 9) yat.Add(new KeyValuePair<float, float>(OrtY(p.sinir), OdaGeo.PolyAlan(p.sinir)));
            if (yat.Count == 0 && !zyVar) return s;
            if (yat.Count > 0)
            {
                yat.Sort((a, b) => a.Key.CompareTo(b.Key));
                float secY = float.NaN, enAlan = 0;
                for (int i = 0; i < yat.Count && float.IsNaN(secY); i++)
                {
                    double ws = 0, ys = 0;
                    for (int j = i; j < yat.Count && yat[j].Key - yat[i].Key < 0.10f; j++) { ws += yat[j].Value; ys += yat[j].Key * yat[j].Value; }
                    if (ws >= 0.5) secY = (float)(ys / ws);
                    if (ws > enAlan) enAlan = (float)ws;
                }
                if (float.IsNaN(secY)) secY = yat[0].Key;
                zyPlan = zyVar ? zyPlan + (secY - zyPlan) * 0.3f : secY; if (!zyVar) zy = zyPlan; zyVar = true;
            }
            s.zeminVar = true; s.zeminY = zy;

            // ARCore zemin duzlemleri hucrelere dokulur
            foreach (long k in planHucre) if (h.TryGetValue(k, out var c0)) c0.plan = false;
            planHucre.Clear();
            foreach (var p in planlar)
            {
                if (p.tur != 0 || p.sinir == null || p.sinir.Length < 9) continue;
                if (Math.Abs(OrtY(p.sinir) - zyPlan) > 0.10f) continue;
                foreach (long k in PoligonHucreleri(p.sinir)) { Al(k).plan = true; planHucre.Add(k); }
            }

            // ARCore duzlemi gorulmemis yere de tasabiliyor (duvar arkasina ucgen): duzlem hucresi ancak 15 cm icinde
            // derinlikte zemin goruldu ise sayilir
            var zeminKaba = new HashSet<long>();
            foreach (var kv in h) if (kv.Value.zemin >= 1 || kv.Value.yol > 0) { ZeminSonuc.Coz(kv.Key, out int ix, out int iz); zeminKaba.Add(OdaSonuc.Anah2(FloorDiv(ix, 3), FloorDiv(iz, 3))); }
            // siniflandirma
            foreach (var kv in h)
            {
                var c = kv.Value;
                bool planKanit = false;
                if (c.plan) { ZeminSonuc.Coz(kv.Key, out int ix, out int iz); int cx = FloorDiv(ix, 3), cz = FloorDiv(iz, 3); for (int dz = -1; dz <= 1 && !planKanit; dz++) for (int dx = -1; dx <= 1; dx++) if (zeminKaba.Contains(OdaSonuc.Anah2(cx + dx, cz + dz))) { planKanit = true; break; } }
                bool bil = planKanit || c.zemin >= 2 || c.yol > 0;
                bool eng = c.engel >= EngelMinKare && c.engel * 2 >= c.zemin && !(c.yol > 0 && c.engel < 10);
                if (eng) s.engel.Add(kv.Key);
                else if (bil) s.bilinen.Add(kv.Key);
            }
            foreach (long k in s.bilinen)
            {
                ZeminSonuc.Coz(k, out int ix, out int iz); bool yakin = false;
                for (int dz = -1; dz <= 1 && !yakin; dz++) for (int dx = -1; dx <= 1; dx++) if (s.engel.Contains(OdaSonuc.Anah2(ix + dx, iz + dz))) { yakin = true; break; }
                if (!yakin) s.acik.Add(k);
            }
            s.alan = s.acik.Count * V * V;
            var iz2 = new float[izX.Count * 2]; for (int i = 0; i < izX.Count; i++) { iz2[i * 2] = izX[i]; iz2[i * 2 + 1] = izZ[i]; }
            s.iz = iz2;
            s.SinirHesapla();
            YonEksik(s);
            return s;
        }

        /// <summary>16 yonde kullanicidan 0,5-3 m arasi: engele kadar ornek noktalarin kacinda hic kanit yok.</summary>
        static void YonEksik(ZeminSonuc s)
        {
            for (int b = 0; b < 16; b++)
            {
                double a = (b + 0.5) * Math.PI / 8, dx = Math.Sin(a), dz = Math.Cos(a);
                int n = 0, eksik = 0;
                for (float r = 0.5f; r <= 3.0f; r += 0.15f)
                {
                    float x = s.kameraX + (float)(dx * r), z = s.kameraZ + (float)(dz * r);
                    int ix = ZeminSonuc.Ind(x), iz = ZeminSonuc.Ind(z);
                    if (s.engel.Contains(OdaSonuc.Anah2(ix, iz))) break;
                    bool var = false;
                    for (int qz = -2; qz <= 2 && !var; qz++) for (int qx = -2; qx <= 2; qx++) { long k = OdaSonuc.Anah2(ix + qx, iz + qz); if (s.bilinen.Contains(k) || s.engel.Contains(k)) { var = true; break; } }
                    n++; if (!var) eksik++;
                }
                s.yonEksik[b] = n == 0 ? 0f : eksik / (float)n;
            }
        }

        static int FloorDiv(int a, int b) { return a >= 0 ? a / b : -((-a + b - 1) / b); }

        static float OrtY(float[] sinir) { double t = 0; int m = sinir.Length / 3; for (int i = 0; i < m; i++) t += sinir[i * 3 + 1]; return (float)(t / m); }

        static List<long> PoligonHucreleri(float[] p)
        {
            int m = p.Length / 3; float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            for (int i = 0; i < m; i++) { x0 = Math.Min(x0, p[i * 3]); x1 = Math.Max(x1, p[i * 3]); z0 = Math.Min(z0, p[i * 3 + 2]); z1 = Math.Max(z1, p[i * 3 + 2]); }
            var r = new List<long>();
            for (int iz = ZeminSonuc.Ind(z0); iz <= ZeminSonuc.Ind(z1); iz++)
                for (int ix = ZeminSonuc.Ind(x0); ix <= ZeminSonuc.Ind(x1); ix++)
                {
                    double x = (ix + 0.5) * V, z = (iz + 0.5) * V; bool ic = false;
                    for (int i = 0, j = m - 1; i < m; j = i++)
                    {
                        double xi = p[i * 3], zi = p[i * 3 + 2], xj = p[j * 3], zj = p[j * 3 + 2];
                        if ((zi > z) != (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi) ic = !ic;
                    }
                    if (ic) r.Add(OdaSonuc.Anah2(ix, iz));
                }
            return r;
        }
    }
}
