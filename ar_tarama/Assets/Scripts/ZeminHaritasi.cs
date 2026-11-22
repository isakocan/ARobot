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
        public HashSet<long> duvar = new HashSet<long>();     // uzun engel (1 m ustunde de nokta var): gorusu keser
        // yumusatilmis gosterim (ZeminGeo): 5 cm alanlar ve 10 cm kafeste kontur ucgenleri (x,z), acik zemin kenar parcalari
        public float[] fa = new float[0], fe = new float[0]; public int gx0, gz0, gw, gh;
        public float[] acikUcgen = new float[0], engelUcgen = new float[0], acikKenar = new float[0];
        public float alan;                              // acik zemin, m2
        public float aci = float.NaN;                   // haritanin ana yonu (radyan, 90 derece katlari): kenarlar bu yone hizali duz cizilir
        public Dictionary<long, byte> kismi = new Dictionary<long, byte>();   // teyit bekleyen hucre -> zemin gorulen kare sayisi
        /// <summary>(x,z) cevresinde r icindeki hucrelerin teyit orani 0..1 (acik = 1, kismi = sayi / KesinKare, bos = 0).</summary>
        public float Teyit(float x, float z, float r)
        {
            int ri = (int)Math.Ceiling(r / V), cx = Ind(x), cz = Ind(z); float t = 0; int n = 0;
            for (int dz = -ri; dz <= ri; dz++) for (int dx = -ri; dx <= ri; dx++)
            {
                if (dx * dx + dz * dz > ri * ri) continue;
                long k = OdaSonuc.Anah2(cx + dx, cz + dz); n++;
                if (acik.Contains(k)) t += 1f; else if (kismi.TryGetValue(k, out byte b)) t += b / (float)ZeminHaritasi.KesinKare;
            }
            return n > 0 ? t / n : 0f;
        }
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

        /// <summary>Kusbakisi RGBA goruntu (satir 0 = en kucuk z, sutun 0 = en kucuk x; Unity Texture2D sirasi). 1 piksel = 1 hucre.
        /// Yumusatilmis alanlardan: kenarlar yumusak, kucuk lekeler yok.</summary>
        public byte[] Resim(out int W, out int H, out float ox, out float oz, int pay = 4)
        {
            if (fa.Length == 0) Yumusat();
            W = Math.Max(1, gw); H = Math.Max(1, gh); ox = gx0 * V; oz = gz0 * V;
            var p = new byte[W * H * 4];
            if (!float.IsNaN(aci) && acikUcgen.Length > 0)
            {
                // duz kenarli: AR'daki ayni ucgenler piksel basina 2x2 ornekle taranir (kenar yumusak, cizgi duz)
                var al = new float[W * H];
                for (int t = 0; t + 5 < acikUcgen.Length; t += 6)
                {
                    float ax = acikUcgen[t] / V - gx0, az = acikUcgen[t + 1] / V - gz0, bx = acikUcgen[t + 2] / V - gx0, bz = acikUcgen[t + 3] / V - gz0;
                    float cx = acikUcgen[t + 4] / V - gx0, cz = acikUcgen[t + 5] / V - gz0;
                    int x0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx)))), x1 = Math.Min(W - 1, (int)Math.Floor(Math.Max(ax, Math.Max(bx, cx))));
                    int y0 = Math.Max(0, (int)Math.Floor(Math.Min(az, Math.Min(bz, cz)))), y1 = Math.Min(H - 1, (int)Math.Floor(Math.Max(az, Math.Max(bz, cz))));
                    float d = (bx - ax) * (cz - az) - (bz - az) * (cx - ax); if (Math.Abs(d) < 1e-9f) continue;
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                            for (int q = 0; q < 4; q++)
                            {
                                float px = x + 0.25f + 0.5f * (q & 1), pz = y + 0.25f + 0.5f * (q >> 1);
                                float l1 = ((bx - px) * (cz - pz) - (bz - pz) * (cx - px)) / d, l2 = ((cx - px) * (az - pz) - (cz - pz) * (ax - px)) / d;
                                if (l1 >= 0 && l2 >= 0 && l1 + l2 <= 1) al[y * W + x] = Math.Min(1f, al[y * W + x] + 0.25f);
                            }
                }
                for (int i = 0; i < W * H; i++)
                {
                    if (al[i] <= 0f) continue;
                    int o = i * 4; p[o] = 67; p[o + 1] = 217; p[o + 2] = 190; p[o + 3] = (byte)(al[i] * 235f);
                }
            }
            else
                for (int i = 0; i < W * H; i++)
                {
                    float a = Yum(fa[i]);
                    float al = a * 235f;
                    if (al < 1f) continue;
                    int o = i * 4; p[o] = 67; p[o + 1] = 217; p[o + 2] = 190; p[o + 3] = (byte)Math.Min(255f, al);
                }
            // duvarlar sinir olarak (acik gri); esyalar cizilmez (temiz harita)
            foreach (long k in duvar)
            {
                Coz(k, out int ix, out int iz); int x = ix - gx0, y = iz - gz0;
                if (x < 0 || y < 0 || x >= W || y >= H) continue;
                int o = (y * W + x) * 4; p[o] = 214; p[o + 1] = 218; p[o + 2] = 225; p[o + 3] = 230;
            }
            return p;
        }

        /// <summary>Yumusatilmis acik zemin alani (0..1) dunya (x, z) noktasinda, cift dogrusal.</summary>
        public float AlanDeger(float x, float z)
        {
            if (fa.Length == 0) return 0f;
            float fx = x / V - 0.5f - gx0, fz = z / V - 0.5f - gz0;
            int ix = (int)Math.Floor(fx), iz = (int)Math.Floor(fz); float tx = fx - ix, tz = fz - iz;
            if (ix < 0 || iz < 0 || ix + 1 >= gw || iz + 1 >= gh) return 0f;
            float a = fa[iz * gw + ix], b = fa[iz * gw + ix + 1], c = fa[(iz + 1) * gw + ix], d = fa[(iz + 1) * gw + ix + 1];
            return (a * (1 - tx) + b * tx) * (1 - tz) + (c * (1 - tx) + d * tx) * tz;
        }

        static float Yum(float x) { x = (x - 0.35f) / 0.3f; if (x <= 0f) return 0f; if (x >= 1f) return 1f; return x * x * (3f - 2f * x); }

        /// <summary>Gosterim icin alanlar ve konturlar (arka plan is parcaciginda, Guncelle sonunda; JSON okununca da).</summary>
        public void Yumusat()
        {
            SinirHesapla();
            gx0 = x0 - 6; gz0 = z0 - 6; gw = x1 - x0 + 13; gh = z1 - z0 + 13;
            if (gw * gh > 4000000) { gw = Math.Min(gw, 2000); gh = Math.Min(gh, 2000); }
            fa = ZeminGeo.Alan(acik, gx0, gz0, gw, gh); fe = ZeminGeo.Alan(engel, gx0, gz0, gw, gh);
            var u = new List<float>(); var k = new List<float>();
            // ana yon biliniyorsa: o yone hizali izgarada bulaniksiz kontur -> duz kenarlar (puruzsuz, kullanici istegi)
            if (!float.IsNaN(aci)) ZeminGeo.DuzKontur(acik, aci, u, k);
            else ZeminGeo.Kontur(fa, gw, gh, gx0, gz0, 2, 0.5f, u, k);
            acikUcgen = u.ToArray(); acikKenar = k.ToArray(); engelUcgen = new float[0];
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
            if (!float.IsNaN(aci)) sb.Append(",\"aci\":").Append(aci.ToString("F5", c));
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
            if (j.IndexOf("\"aci\":", StringComparison.Ordinal) >= 0) s.aci = (float)Sayi(j, "aci");
            Doldur(Dizi(j, "acik"), s.acik); Doldur(Dizi(j, "engel"), s.engel); Doldur(Dizi(j, "bilinen"), s.bilinen);
            var iz = Dizi(j, "iz"); s.iz = new float[iz.Count]; for (int i = 0; i < iz.Count; i++) s.iz[i] = (float)iz[i];
            s.Yumusat();
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

        class Hucre { public int zemin, engel, yol, uzun, ust; public int kz = -1, ke = -1, ku = -1, ks = -1; public bool plan; }

        // "Kesin zemin" (kullanici istegi, 21 Kasim: pürüzlü olacağına hiç gösterme, az ama temiz ve doğru):
        // bir hucre acik zemin sayilir ancak (a) kullanici oradan yurumusse ya da (b) derinlik orayi en az KesinKare karede
        // zemin olarak gorduyse, hic guvenli engel noktasi yoksa ve ustunde (15 cm - 1,2 m) duzenli nokta gorulmediyse
        // (masa / sandalye alti). ARCore duzlemleri artik kanit degil: gorulmemis masa altina tasiyordu.
        public const int KesinKare = 8;            // 4 Hz derinlikte ~2 sn tutma (kullanici: bir kerelik is, garanti olsun)
        public const int AcmaYaricap = 2;        // hucre: 10 cm'den ince saçaklar ve kenar pürüzü atilir (morfolojik acma)
        public const int EnKucukParca = 100;     // hucre: 0,25 m2'den kucuk acik zemin parcalari atilir
        public const int DelikDoldur = 60;       // hucre: icinde engel olmayan 0,15 m2'den kucuk delikler doldurulur
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
        public string Teshis = "";
        public float ZeminY => zy;
        public bool ZeminVar => zyVar;
        public int HucreSayisi => h.Count;

        // kullanicinin kalemle cektigi zemin sinirlari. Cizgi duvar sayilir; cizginin 50 cm cevresinde kullanicinin durdugu
        // yan zemin (otomatik duvar / esya kaniti ezilir), obur yan kapali: sinir tam cizgiye oturur (kullanici duzeltmesi)
        class KalemCizgisi { public int id; public float[] xz; public float kx, kz; }
        readonly List<KalemCizgisi> cizgiler = new List<KalemCizgisi>();
        readonly object cizgiKilit = new object();
        int cizgiNo;
        public const float KalemBant = 0.5f, KalemAcikBant = 0.35f;   // obur yan 50 cm kapali, kullanici yani 35 cm acik
        /// <summary>Kalem cizgisi ekler (dunya x,z cokcizgisi; kx,kz: cizerken kullanicinin konumu). Kimligi doner.</summary>
        public int SinirCizgisiEkle(float[] xz, float kx, float kz)
        {
            lock (cizgiKilit) { var c = new KalemCizgisi { id = ++cizgiNo, xz = xz, kx = kx, kz = kz }; cizgiler.Add(c); return c.id; }
        }
        /// <summary>(x, z)'ye r'den yakin gecen kalem cizgilerini siler, silinenlerin kimliklerini doner.</summary>
        public List<int> SinirSil(float x, float z, float r)
        {
            var sil = new List<int>();
            lock (cizgiKilit)
                cizgiler.RemoveAll(c =>
                {
                    for (int i = 0; i + 3 < c.xz.Length || (i == 0 && c.xz.Length == 2); i += 2)
                    {
                        float ax = c.xz[i], az = c.xz[i + 1], bx = c.xz.Length > i + 3 ? c.xz[i + 2] : ax, bz = c.xz.Length > i + 3 ? c.xz[i + 3] : az;
                        if (ParcaUzaklik(x, z, ax, az, bx, bz, out _, out _) < r) { sil.Add(c.id); return true; }
                    }
                    return false;
                });
            return sil;
        }
        public int CizgiSayisi { get { lock (cizgiKilit) return cizgiler.Count; } }

        static float ParcaUzaklik(float px, float pz, float ax, float az, float bx, float bz, out float t, out float yan)
        {
            float dx = bx - ax, dz = bz - az, L2 = dx * dx + dz * dz;
            t = L2 > 1e-8f ? ((px - ax) * dx + (pz - az) * dz) / L2 : 0f;
            float tc = Math.Max(0f, Math.Min(1f, t));
            float qx = ax + dx * tc - px, qz = az + dz * tc - pz;
            yan = dx * (pz - az) - dz * (px - ax);
            return (float)Math.Sqrt(qx * qx + qz * qz);
        }

        /// <summary>Kalem cizgilerinden: cizgi hucreleri (duvar), kullanici yaninda zorla acik, obur yanda zorla kapali.</summary>
        void KalemHucreleri(HashSet<long> hat, HashSet<long> zAcik, HashSet<long> zKapali)
        {
            KalemCizgisi[] cs; lock (cizgiKilit) cs = cizgiler.ToArray();
            int B = (int)Math.Ceiling(KalemBant / V);
            foreach (var c in cs)
            {
                int n = c.xz.Length / 2; if (n < 2) continue;
                int x0 = int.MaxValue, x1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue;
                for (int i = 0; i < n; i++)
                {
                    int ix = ZeminSonuc.Ind(c.xz[i * 2]), iz = ZeminSonuc.Ind(c.xz[i * 2 + 1]);
                    x0 = Math.Min(x0, ix); x1 = Math.Max(x1, ix); z0 = Math.Min(z0, iz); z1 = Math.Max(z1, iz);
                }
                // kullanicinin her parcaya gore yani
                var kYan = new float[n - 1];
                for (int i = 0; i + 1 < n; i++) ParcaUzaklik(c.kx, c.kz, c.xz[i * 2], c.xz[i * 2 + 1], c.xz[i * 2 + 2], c.xz[i * 2 + 3], out _, out kYan[i]);
                for (int iz = z0 - B; iz <= z1 + B; iz++)
                    for (int ix = x0 - B; ix <= x1 + B; ix++)
                    {
                        float px = (ix + 0.5f) * V, pz = (iz + 0.5f) * V;
                        float en = float.MaxValue, enT = 0f, enYan = 0f; int enI = -1;
                        for (int i = 0; i + 1 < n; i++)
                        {
                            float d = ParcaUzaklik(px, pz, c.xz[i * 2], c.xz[i * 2 + 1], c.xz[i * 2 + 2], c.xz[i * 2 + 3], out float t, out float yan);
                            if (d < en) { en = d; enT = t; enYan = yan; enI = i; }
                        }
                        if (enI < 0) continue;
                        long k = OdaSonuc.Anah2(ix, iz);
                        if (en <= 0.04f) { hat.Add(k); continue; }
                        if (en > KalemBant) continue;
                        // ucun otesi (cizginin devami degil) taraf belirsiz: dokunma
                        if ((enI == 0 && enT < 0f) || (enI == n - 2 && enT > 1f)) continue;
                        if (Math.Abs(kYan[enI]) < 1e-6f) continue;
                        if ((enYan > 0) == (kYan[enI] > 0)) { if (en <= KalemAcikBant) zAcik.Add(k); } else zKapali.Add(k);
                    }
            }
            zAcik.ExceptWith(hat); zKapali.ExceptWith(hat); zAcik.ExceptWith(zKapali);
        }

        public void Sifirla() { lock (cizgiKilit) cizgiler.Clear(); kalici.Clear(); duvarKalici.Clear(); kamYTop = 0; kamYSay = 0; aci = float.NaN; aciKilit = false; h.Clear(); izX.Clear(); izZ.Clear(); zyVar = false; kare = 0; planHucre.Clear(); }

        Hucre Al(long k) { if (!h.TryGetValue(k, out var c)) { c = new Hucre(); h[k] = c; } return c; }

        // ---------------------------------------------------------------- kare basina (arka plan is parcacigi)
        public void Isle(DerinlikKaresi f)
        {
            kare++; kamX = f.px; kamZ = f.pz; kamYTop += f.py; kamYSay++;
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
                        if (d > ZeminMenzil) continue;
                        var c = Al(OdaSonuc.Anah2(ZeminSonuc.Ind(x), ZeminSonuc.Ind(z)));
                        if (c.kz != kare) { c.kz = kare; c.zemin++; }
                    }
                    else if (yk >= EngelAlt && yk <= 2.3f)
                    {
                        // uzun engel (duvar, dolap, kapi kasasi): 1 m ustundeki noktalar zemin gurultusu olamaz, dusuk guvende de sayilir
                        // ustunde bir sey var (masa, sandalye oturagi): dusuk guvende de sayilir; o hucre kesin zemin olamaz
                        if (yk >= 0.15f && yk <= 1.2f && (f.g == null || f.g[i] >= 10))
                        {
                            var cs = Al(OdaSonuc.Anah2(ZeminSonuc.Ind(x), ZeminSonuc.Ind(z)));
                            if (cs.ks != kare) { cs.ks = kare; cs.ust++; }
                        }
                        if (yk >= 1.0f && (f.g == null || f.g[i] >= 10))
                        {
                            var cu = Al(OdaSonuc.Anah2(ZeminSonuc.Ind(x), ZeminSonuc.Ind(z)));
                            if (cu.ku != kare) { cu.ku = kare; cu.uzun++; }
                        }
                        if (yk > EngelUst) continue;
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
        // Zemin haritasi (22 Kasim, kullanici istegi: ARCore zemini kadar temiz, duz hatli, bagli, silinmeyen):
        //  1) zemin yuksekligi: telefonun 0,5-1,9 m altindaki en genis yatay duzlem kumesi ("en alcak" degil: sahte alcak
        //     duzlem zemini 64 cm asagi kaydirmisti, 22 Kasim 15:08 taramasi)
        //  2) ana yon: dikey duzlemlerin (duvar) normallerinden, 90 derece katlari; harita bu yone hizali duz hatlarla cizilir
        //  3) duvar: en az 1 m yukselen dikey duzlemler (ana yone yapistirilir) + derinlikte 1 m ustu noktalar + kalem
        //  4) esya: derinlikte guvenli engel / masa ustu goren hucreler, ana yone hizali 20 cm karelere yuvarlanir (duz kesim)
        //  5) aday zemin: ARCore zemin duzlemleri + yurunen yol + derinlikte zemin; esya ve duvar cikar
        //  6) bagli: yalniz kullanicinin yurudugu / durdugu yerden duvar ve esya gecmeden ulasilan aday zemin
        //  7) kalici: bir kez acik bulunan hucre, sonradan esya ya da duvar kaniti gelmedikce silinmez
        public const float KabaV = 0.30f;      // esya karesi: hassasliktan cok duz ve dogru (kullanici, 22 Kasim)
        // Teyit (22 Kasim, ev4 taramasi: kamera bir yere doner donmez boyuyordu, duvar arkasi / masa ici / hic bakilmamis yer
        // zemin oluyordu): hucre ancak derinlikte en az KesinKare (~2 sn tutunca) karede zemin goruldugunde aday olur;
        // ARCore duzlemi yalniz boyle teyitli yerin 20 cm cevresine tasar. Zemin kaniti en cok 2 m'den.
        public const float ZeminMenzil = 2.0f;
        readonly HashSet<long> kalici = new HashSet<long>();
        readonly HashSet<long> duvarKalici = new HashSet<long>();
        float kamYTop; int kamYSay;
        float aci = float.NaN; bool aciKilit;

        public ZeminSonuc Guncelle(IList<OdaDuzlem> planlar, float zaman)
        {
            var s = new ZeminSonuc { zaman = zaman, kameraX = kamX, kameraZ = kamZ };
            float kamY = kamYSay > 0 ? kamYTop / kamYSay : float.NaN;
            // 1) zemin yuksekligi
            var yat = new List<KeyValuePair<float, float>>();
            foreach (var p in planlar)
                if (p.tur == 0 && p.sinir != null && p.sinir.Length >= 9) yat.Add(new KeyValuePair<float, float>(OrtY(p.sinir), OdaGeo.PolyAlan(p.sinir)));
            if (yat.Count == 0 && !zyVar) return s;
            if (yat.Count > 0)
            {
                yat.Sort((a, b) => a.Key.CompareTo(b.Key));
                float secY = float.NaN, enAlan = -1f, enAlanHepsi = -1f, secHepsi = yat[0].Key;
                for (int i = 0; i < yat.Count; i++)
                {
                    double ws = 0, ys = 0;
                    for (int j2 = i; j2 < yat.Count && yat[j2].Key - yat[i].Key < 0.10f; j2++) { ws += yat[j2].Value; ys += yat[j2].Key * yat[j2].Value; }
                    float y = (float)(ys / Math.Max(ws, 1e-6));
                    if (ws > enAlanHepsi) { enAlanHepsi = (float)ws; secHepsi = y; }
                    bool uygun = float.IsNaN(kamY) || (kamY - y > 0.5f && kamY - y < 1.9f);
                    if (uygun && ws > enAlan) { enAlan = (float)ws; secY = y; }
                }
                if (float.IsNaN(secY)) secY = secHepsi;
                if (!zyVar || Math.Abs(secY - zyPlan) > 0.3f) { zyPlan = secY; zy = secY; }      // yanlis secimden kurtul
                else zyPlan += (secY - zyPlan) * 0.3f;
                zyVar = true;
            }
            s.zeminVar = true; s.zeminY = zy;

            // 2) ana yon (90 derece katlari): dikey duzlem normalleri, alanla agirlikli; yoksa zemin duzlemi kenarlari
            if (!aciKilit)
            {
                double sx = 0, sz = 0, w = 0;
                foreach (var p in planlar)
                {
                    if (p.tur != 2 || p.sinir == null || p.sinir.Length < 9) continue;
                    double a = DikeyAlan(p.sinir); double t = Math.Atan2(p.nx, p.nz) * 4;
                    sx += Math.Cos(t) * a; sz += Math.Sin(t) * a; w += a;
                }
                if (w < 0.5)
                    foreach (var p in planlar)
                    {
                        if (p.tur != 0 || p.sinir == null || p.sinir.Length < 9 || Math.Abs(OrtY(p.sinir) - zy) > 0.10f) continue;
                        int m = p.sinir.Length / 3;
                        for (int i = 0, j2 = m - 1; i < m; j2 = i++)
                        {
                            double dx = p.sinir[i * 3] - p.sinir[j2 * 3], dz = p.sinir[i * 3 + 2] - p.sinir[j2 * 3 + 2], L = Math.Sqrt(dx * dx + dz * dz);
                            double t = Math.Atan2(dx, dz) * 4; sx += Math.Cos(t) * L * 0.3; sz += Math.Sin(t) * L * 0.3;
                        }
                    }
                if (sx * sx + sz * sz > 1e-6) aci = (float)(Math.Atan2(sz, sx) / 4);
                if (w >= 2.0) aciKilit = true;            // yeterince duvar goruldu: yon sabit (harita titremesin)
            }
            if (float.IsNaN(aci)) aci = 0f;
            s.aci = aci;
            float ca = (float)Math.Cos(aci), sa = (float)Math.Sin(aci);

            // 3) duvarlar
            var duvar = new HashSet<long>();
            foreach (var p in planlar)
            {
                if (p.tur != 2 || p.sinir == null || p.sinir.Length < 9) continue;
                int m = p.sinir.Length / 3; float yUst = float.MinValue, yAlt = float.MaxValue; double cx = 0, cz = 0;
                for (int i = 0; i < m; i++) { yUst = Math.Max(yUst, p.sinir[i * 3 + 1]); yAlt = Math.Min(yAlt, p.sinir[i * 3 + 1]); cx += p.sinir[i * 3]; cz += p.sinir[i * 3 + 2]; }
                if (yUst - zy < 1.0f || yAlt - zy > 1.2f) continue;              // alcak dikey yuzey (masa / dolap yani): duvar degil
                cx /= m; cz /= m;
                // duvar dogrultusu (normale dik), ana yone yakinsa ona yapistir
                double nAci = Math.Atan2(p.nx, p.nz); double fark = nAci - aci; fark -= Math.Round(fark / (Math.PI / 2)) * (Math.PI / 2);
                if (Math.Abs(fark) < 12 * Math.PI / 180) nAci -= fark;
                double tx = Math.Cos(nAci), tz = -Math.Sin(nAci);                  // normal (sin, cos) -> teget (cos, -sin)
                double t0 = double.MaxValue, t1 = double.MinValue;
                for (int i = 0; i < m; i++) { double t = (p.sinir[i * 3] - cx) * tx + (p.sinir[i * 3 + 2] - cz) * tz; t0 = Math.Min(t0, t); t1 = Math.Max(t1, t); }
                for (double t = t0; t <= t1; t += 0.025)
                {
                    int qx = ZeminSonuc.Ind((float)(cx + tx * t)), qz = ZeminSonuc.Ind((float)(cz + tz * t));
                    for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) duvar.Add(OdaSonuc.Anah2(qx + dx, qz + dz));
                }
            }
            foreach (var kv in h) if (kv.Value.uzun >= EngelMinKare && kv.Value.yol == 0) duvar.Add(kv.Key);
            duvarKalici.UnionWith(duvar);
            foreach (long k in duvarKalici) if (!h.TryGetValue(k, out var dc) || dc.yol == 0) duvar.Add(k);
            var kHat = new HashSet<long>(); var kAcik = new HashSet<long>(); var kKapali = new HashSet<long>();
            KalemHucreleri(kHat, kAcik, kKapali);
            duvar.ExceptWith(kAcik);                       // kullanicinin cizgisi otomatik duvari duzeltir
            foreach (long k in kHat) duvar.Add(k);

            // 4) esya: 20 cm kaba kareler (ana yone hizali); yurunen yer esya olamaz
            var kabaSay = new Dictionary<long, int>(); var kabaYol = new HashSet<long>();
            foreach (var kv in h)
            {
                var c = kv.Value;
                long kk = Kaba(kv.Key, ca, sa);
                if (c.yol > 0) { kabaYol.Add(kk); continue; }
                bool eng = c.engel >= EngelMinKare && c.engel * 2 >= c.zemin;
                bool masa = c.ust >= 3 && c.ust * 2 >= c.zemin;                    // ustunde bir sey var (masa / sandalye)
                if (eng || masa) kabaSay[kk] = (kabaSay.TryGetValue(kk, out int n) ? n : 0) + 1;
            }
            var esya = new HashSet<long>();
            foreach (var kv in kabaSay) if (kv.Value >= 2 && !kabaYol.Contains(kv.Key)) esya.Add(kv.Key);

            // 5) aday zemin. ARCore duzlemi gorulmemis yere de tasabiliyor (21 Kasim cok odali taramada son zemin duzlemi
            // butun binayi kaplayan dev bir cokgendi): duzlem hucresi yalniz derinlikte zemin gorulen ya da yurunen yerin
            // 30 cm yakininda sayilir
            var kanit = new HashSet<long>();
            foreach (var kv in h) if (kv.Value.zemin >= KesinKare || kv.Value.yol > 0) { ZeminSonuc.Coz(kv.Key, out int ix, out int iz); kanit.Add(OdaSonuc.Anah2(FloorDiv(ix, 4), FloorDiv(iz, 4))); }
            var aday = new HashSet<long>();
            foreach (var p in planlar)
            {
                if (p.tur != 0 || p.sinir == null || p.sinir.Length < 9 || Math.Abs(OrtY(p.sinir) - zy) > 0.10f) continue;
                foreach (long k in PoligonHucreleri(p.sinir))
                {
                    ZeminSonuc.Coz(k, out int ix, out int iz); int cx = FloorDiv(ix, 4), cz = FloorDiv(iz, 4); bool yakin = false;
                    for (int dz = -1; dz <= 1 && !yakin; dz++) for (int dx = -1; dx <= 1; dx++) if (kanit.Contains(OdaSonuc.Anah2(cx + dx, cz + dz))) { yakin = true; break; }
                    if (yakin) aday.Add(k);
                }
            }
            foreach (var kv in h) if (kv.Value.yol > 0 || (kv.Value.zemin >= KesinKare && kv.Value.engel == 0)) aday.Add(kv.Key);
            foreach (long k in kAcik) aday.Add(k);
            Func<long, bool> kapali = k => duvar.Contains(k) || kKapali.Contains(k) || (!kAcik.Contains(k) && esya.Contains(Kaba(k, ca, sa)));
            aday.RemoveWhere(k => kapali(k));

            // 6) bagli: tohum = yurunen yol + kameranin alti + onceden acik olanlar
            var acik = new HashSet<long>(); var yigin = new Stack<long>();
            foreach (var kv in h) if (kv.Value.yol > 0 && aday.Contains(kv.Key) && acik.Add(kv.Key)) yigin.Push(kv.Key);
            { long kc = OdaSonuc.Anah2(ZeminSonuc.Ind(kamX), ZeminSonuc.Ind(kamZ)); if (aday.Contains(kc) && acik.Add(kc)) yigin.Push(kc); }
            foreach (long k in kalici) if (aday.Contains(k) && acik.Add(k)) yigin.Push(k);
            while (yigin.Count > 0)
            {
                long k = yigin.Pop(); ZeminSonuc.Coz(k, out int ix, out int iz);
                long a1 = OdaSonuc.Anah2(ix + 1, iz), a2 = OdaSonuc.Anah2(ix - 1, iz), a3 = OdaSonuc.Anah2(ix, iz + 1), a4 = OdaSonuc.Anah2(ix, iz - 1);
                if (aday.Contains(a1) && acik.Add(a1)) yigin.Push(a1);
                if (aday.Contains(a2) && acik.Add(a2)) yigin.Push(a2);
                if (aday.Contains(a3) && acik.Add(a3)) yigin.Push(a3);
                if (aday.Contains(a4) && acik.Add(a4)) yigin.Push(a4);
            }
            // 7) kalici: bir kez boyanan yer, ancak hucrenin kendisinde engel / masa ustu kaniti, duvar ya da kalem
            // gelirse silinir (20 cm kaba esya karesi ve acma/temizlik onu kemirmez: zemin zamanla kuculuyordu)
            Func<long, bool> kaliciGider = k =>
            {
                if (duvar.Contains(k) || kKapali.Contains(k)) return true;
                if (kAcik.Contains(k) || !h.TryGetValue(k, out var c) || c.yol > 0) return false;
                // teyitli zeminde birkac engel noktasi esya kenarindaki derinlik tasmasi olabilir: silmek icin engel kaniti
                // zemin kanitindan fazla ve en az 6 karede olmali
                return (c.engel >= 6 && c.engel > c.zemin) || (c.ust >= 6 && c.ust > c.zemin);
            };
            var kalan = new HashSet<long>(); int gDuvar = 0, gEngel = 0;
            foreach (long k in kalici) { if (!kaliciGider(k)) kalan.Add(k); else if (duvar.Contains(k)) gDuvar++; else gEngel++; }
            acik.UnionWith(kalan);
            ZeminGeo.Ac(acik, 1);
            ZeminGeo.KucukleriAt(acik, 40, null);
            acik.UnionWith(kalan);
            ZeminGeo.DelikDoldur(acik, duvar, 60);
            // kalem cizgisi bir yeri ayirdiysa: kullanicinin bulundugu yere bagli olmayan parca (eski kalici dahil) atilir
            if (kHat.Count > 0)
            {
                var bag = new HashSet<long>(); var yg = new Stack<long>();
                foreach (var kv in h) if (kv.Value.yol > 0 && acik.Contains(kv.Key) && bag.Add(kv.Key)) yg.Push(kv.Key);
                { long kc = OdaSonuc.Anah2(ZeminSonuc.Ind(kamX), ZeminSonuc.Ind(kamZ)); if (acik.Contains(kc) && bag.Add(kc)) yg.Push(kc); }
                while (yg.Count > 0)
                {
                    long k = yg.Pop(); ZeminSonuc.Coz(k, out int ix, out int iz);
                    long a1 = OdaSonuc.Anah2(ix + 1, iz), a2 = OdaSonuc.Anah2(ix - 1, iz), a3 = OdaSonuc.Anah2(ix, iz + 1), a4 = OdaSonuc.Anah2(ix, iz - 1);
                    if (acik.Contains(a1) && bag.Add(a1)) yg.Push(a1);
                    if (acik.Contains(a2) && bag.Add(a2)) yg.Push(a2);
                    if (acik.Contains(a3) && bag.Add(a3)) yg.Push(a3);
                    if (acik.Contains(a4) && bag.Add(a4)) yg.Push(a4);
                }
                if (bag.Count > 0) acik = bag;
            }
            kalici.Clear(); foreach (long k in acik) kalici.Add(k);

            // teyit bekleyen (1..KesinKare-1 karede zemin gorulmus) hucreler: yonlendirme halkasi ilerlemesi
            foreach (var kv in h) if (kv.Value.zemin > 0 && kv.Value.zemin < KesinKare && !acik.Contains(kv.Key)) s.kismi[kv.Key] = (byte)kv.Value.zemin;
            // sonuc kumeleri
            s.acik = acik;
            s.duvar = duvar;
            foreach (long k in duvar) s.engel.Add(k);
            foreach (var kv in h)
            {
                if (acik.Contains(kv.Key)) continue;
                if (!kAcik.Contains(kv.Key) && esya.Contains(Kaba(kv.Key, ca, sa))) s.engel.Add(kv.Key);
                else if (!duvar.Contains(kv.Key)) s.bilinen.Add(kv.Key);
            }
            Teshis = "gitti duvar " + gDuvar + " engel " + gEngel + " | aday " + aday.Count + " acik " + acik.Count + " duvar " + duvar.Count + " esya(20cm) " + esya.Count + " aci " + (aci * 180 / Math.PI).ToString("F1") + " zy " + zy.ToString("F2") + " kamY " + kamY.ToString("F2");
            s.alan = s.acik.Count * V * V;
            var iz2 = new float[izX.Count * 2]; for (int i = 0; i < izX.Count; i++) { iz2[i * 2] = izX[i]; iz2[i * 2 + 1] = izZ[i]; }
            s.iz = iz2;
            s.Yumusat();
            YonEksik(s);
            return s;
        }

        /// <summary>Dunya hucresinin ana yone hizali 20 cm kaba karesi.</summary>
        static long Kaba(long k, float ca, float sa)
        {
            ZeminSonuc.Coz(k, out int ix, out int iz);
            float x = (ix + 0.5f) * V, z = (iz + 0.5f) * V;
            float u = x * ca + z * sa, w = -x * sa + z * ca;
            return OdaSonuc.Anah2((int)Math.Floor(u / KabaV), (int)Math.Floor(w / KabaV));
        }

        /// <summary>Dikey duzlem alani (yaklasik): yatay genislik x yukseklik.</summary>
        static double DikeyAlan(float[] p)
        {
            int m = p.Length / 3; float y0 = float.MaxValue, y1 = float.MinValue; double gen = 0;
            for (int i = 0; i < m; i++)
            {
                y0 = Math.Min(y0, p[i * 3 + 1]); y1 = Math.Max(y1, p[i * 3 + 1]);
                for (int j = i + 1; j < m; j++) gen = Math.Max(gen, Math.Sqrt((p[i * 3] - p[j * 3]) * (p[i * 3] - p[j * 3]) + (p[i * 3 + 2] - p[j * 3 + 2]) * (p[i * 3 + 2] - p[j * 3 + 2])));
            }
            return gen * (y1 - y0);
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

        /// <summary>Poligon icindeki hucreler: satir tarama (her satirda kenar kesisimleri siralanip araliklar doldurulur).</summary>
        static List<long> PoligonHucreleri(float[] p)
        {
            int m = p.Length / 3; float z0 = float.MaxValue, z1 = float.MinValue;
            for (int i = 0; i < m; i++) { z0 = Math.Min(z0, p[i * 3 + 2]); z1 = Math.Max(z1, p[i * 3 + 2]); }
            var r = new List<long>(); var kes = new List<double>();
            for (int iz = ZeminSonuc.Ind(z0); iz <= ZeminSonuc.Ind(z1); iz++)
            {
                double z = (iz + 0.5) * V; kes.Clear();
                for (int i = 0, j = m - 1; i < m; j = i++)
                {
                    double xi = p[i * 3], zi = p[i * 3 + 2], xj = p[j * 3], zj = p[j * 3 + 2];
                    if ((zi > z) != (zj > z)) kes.Add((xj - xi) * (z - zi) / (zj - zi) + xi);
                }
                kes.Sort();
                for (int k = 0; k + 1 < kes.Count; k += 2)
                {
                    int a0 = (int)Math.Ceiling(kes[k] / V - 0.5), a1 = (int)Math.Floor(kes[k + 1] / V - 0.5);
                    for (int ix = a0; ix <= a1; ix++) r.Add(OdaSonuc.Anah2(ix, iz));
                }
            }
            return r;
        }
    }
}
