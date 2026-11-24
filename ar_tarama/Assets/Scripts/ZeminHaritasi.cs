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
        /// <summary>Kesinlesmis hucreler (24 Kasim): durumu (acik / engel) en az 2 guncellemedir degismedi ve kaniti doygun.
        /// Boyada dolgun, kesinlesmemis acik soluk; "burasi tamam" bunlarin oranindan.</summary>
        public HashSet<long> kesin = new HashSet<long>();
        public float[] voksel = new float[0];           // LIDAR gorunumu: kameranin cevresindeki dolu kupler (x, y, z, tur)
        /// <summary>(x,z) cevresinde r icindeki hucrelerin teyit orani 0..1 (acik = 1, kismi = sayi / KesinKare, bos = 0).</summary>
        public float Teyit(float x, float z, float r)
        {
            int ri = (int)Math.Ceiling(r / V), cx = Ind(x), cz = Ind(z); float t = 0; int n = 0;
            for (int dz = -ri; dz <= ri; dz++) for (int dx = -ri; dx <= ri; dx++)
            {
                if (dx * dx + dz * dz > ri * ri) continue;
                long k = OdaSonuc.Anah2(cx + dx, cz + dz); n++;
                if (acik.Contains(k)) t += 1f; else if (kismi.TryGetValue(k, out byte b)) t += b / (float)ZeminHaritasi.KesinNokta;
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

        /// <summary>Resim cercevesi: haritanin ana yonune (aci) hizali eksenler, piksel = ResimPiksel metre. Dunya (x,z) ->
        /// resim (u,w): u = x cos + z sin, w = -x sin + z cos. Gosterimde resim aci kadar dondurulur; boylece egik duz kenarlar
        /// piksel izgarasina paralel olur, merdiven gibi gorunmez.</summary>
        /// <summary>Sinir hucre hucre (24 Kasim, kullanici: "puruzlu olsun, derinlikteki noktalari hassas alsin"): eski duz
        /// kontur 10 cm izgarada 20 cm kapama/acma yapip kenari kaydiriyordu. false: eski duz kontur.</summary>
        public static bool HucreKontur = true;
        public const float ResimPiksel = 0.025f;
        public float ResimCa = 1f, ResimSa = 0f;
        public float ResimAciDerece => (float)(Math.Atan2(ResimSa, ResimCa) * 180.0 / Math.PI);
        public void DunyadanResme(float x, float z, out float u, out float w) { u = x * ResimCa + z * ResimSa; w = -x * ResimSa + z * ResimCa; }
        public void ResimdenDunyaya(float u, float w, out float x, out float z) { x = u * ResimCa - w * ResimSa; z = u * ResimSa + w * ResimCa; }

        /// <summary>Kusbakisi RGBA goruntu, ana yone hizali cercevede (satir 0 = en kucuk w, sutun 0 = en kucuk u; Texture2D
        /// sirasi). ou, ow: sol alt kosenin resim cercevesi koordinati (metre). AR'daki ayni ucgenler 2x2 ornekle taranir.</summary>
        public byte[] Resim(out int W, out int H, out float ou, out float ow, int pay = 4)
        {
            if (fa.Length == 0) Yumusat();
            float a0 = float.IsNaN(aci) ? 0f : aci;
            ResimCa = (float)Math.Cos(a0); ResimSa = (float)Math.Sin(a0);
            float P = ResimPiksel;
            float u0 = float.MaxValue, u1 = float.MinValue, w0 = float.MaxValue, w1 = float.MinValue;
            var uc = new float[acikUcgen.Length];
            for (int i = 0; i + 1 < acikUcgen.Length; i += 2)
            {
                DunyadanResme(acikUcgen[i], acikUcgen[i + 1], out float u, out float w); uc[i] = u; uc[i + 1] = w;
                if (u < u0) u0 = u; if (u > u1) u1 = u; if (w < w0) w0 = w; if (w > w1) w1 = w;
            }
            if (uc.Length == 0) { W = H = 1; ou = ow = 0f; return new byte[4]; }
            float m = pay * V;
            ou = (float)Math.Floor((u0 - m) / P) * P; ow = (float)Math.Floor((w0 - m) / P) * P;
            W = Math.Max(1, (int)Math.Ceiling((u1 + m - ou) / P)); H = Math.Max(1, (int)Math.Ceiling((w1 + m - ow) / P));
            if ((long)W * H > 4000000) { W = Math.Min(W, 2000); H = Math.Min(H, 2000); }
            var p = new byte[W * H * 4];
            var al = Tara(acikUcgen, W, H, ou, ow, P);
            for (int i = 0; i < W * H; i++)
            {
                if (al[i] <= 0f) continue;
                int o = i * 4; p[o] = 67; p[o + 1] = 217; p[o + 2] = 190; p[o + 3] = (byte)(al[i] * 235f);
            }
            // oyun alani (koyu mavi, AR'daki ayni duz hatli alan) acik zeminin ustune
            if (oyunUcgen != null && oyunUcgen.Length >= 6)
            {
                var ao = Tara(oyunUcgen, W, H, ou, ow, P);
                for (int i = 0; i < W * H; i++)
                {
                    float a = ao[i] * 0.85f; if (a <= 0f) continue;
                    int o = i * 4;
                    p[o] = (byte)(p[o] * (1 - a) + 30 * a); p[o + 1] = (byte)(p[o + 1] * (1 - a) + 80 * a); p[o + 2] = (byte)(p[o + 2] * (1 - a) + 225 * a);
                    p[o + 3] = (byte)Math.Max(p[o + 3], (int)(a * 255f));
                }
            }
            // sinir: acik zeminin duz kenari ince acik gri cizgi (duvar / esya ayrimi yok; dagik duvar hucreleri leke gibi
            // gorunuyordu). Kenar parcalari AR'daki ayni duz kontur; hizali cercevede yatay / dikey piksel cizgisi olur.
            for (int i = 0; i + 3 < acikKenar.Length; i += 4)
            {
                DunyadanResme(acikKenar[i], acikKenar[i + 1], out float ua, out float wa); DunyadanResme(acikKenar[i + 2], acikKenar[i + 3], out float ub, out float wb);
                float ax = (ua - ou) / P, az = (wa - ow) / P, bx = (ub - ou) / P, bz = (wb - ow) / P;
                int n = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(bx - ax), Math.Abs(bz - az)) * 2));
                for (int q = 0; q <= n; q++)
                {
                    int x = (int)Math.Floor(ax + (bx - ax) * q / n), y = (int)Math.Floor(az + (bz - az) * q / n);
                    if (x < 0 || y < 0 || x >= W || y >= H) continue;
                    int o = (y * W + x) * 4; p[o] = 214; p[o + 1] = 218; p[o + 2] = 225; p[o + 3] = 240;
                }
            }
            return p;
        }

        /// <summary>Ucgen listesini (dunya x,z) resim izgarasina 2x2 ornekle tarar: her piksel 0..1 kaplama.</summary>
        float[] Tara(float[] ucgen, int W, int H, float ou, float ow, float P)
        {
            var al = new float[W * H];
            for (int t = 0; t + 5 < ucgen.Length; t += 6)
            {
                DunyadanResme(ucgen[t], ucgen[t + 1], out float ua, out float wa); DunyadanResme(ucgen[t + 2], ucgen[t + 3], out float ub, out float wb); DunyadanResme(ucgen[t + 4], ucgen[t + 5], out float uc2, out float wc);
                float ax = (ua - ou) / P, az = (wa - ow) / P, bx = (ub - ou) / P, bz = (wb - ow) / P;
                float cx = (uc2 - ou) / P, cz = (wc - ow) / P;
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
            return al;
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
            if (HucreKontur)
            {
                // sade (24 Kasim): hucre hucre, bulaniksiz kontur; sinir derinligin gordugu yerden gecer (puruzlu olabilir)
                var b = new float[gw * gh];
                foreach (long q in acik) { Coz(q, out int ix, out int iz); int x = ix - gx0, y = iz - gz0; if (x >= 0 && y >= 0 && x < gw && y < gh) b[y * gw + x] = 1f; }
                ZeminGeo.Kontur(b, gw, gh, gx0, gz0, 1, 0.5f, u, k);
            }
            else if (!float.IsNaN(aci)) ZeminGeo.DuzKontur(acik, aci, u, k);
            else ZeminGeo.Kontur(fa, gw, gh, gx0, gz0, 2, 0.5f, u, k);
            acikUcgen = u.ToArray(); acikKenar = k.ToArray(); engelUcgen = new float[0];
            OyunHesapla();
        }

        /// <summary>Oyun alani (24 Kasim, kullanici: "oyun icin kullanilabilecek yerler, acik zemin guveni yuksek, daha duz
        /// hatlarda, koyu mavi"): kesinlesmis acik zemin (kesin bilgisi yoksa butun acik zemin), engel / duvar hucresinden
        /// en az OyunPay hucre uzak; 10 cm'den ince seritler ve 0,25 m2'den kucuk adaciklar atilir; ana yone hizali duz kontur.</summary>
        public HashSet<long> oyun = new HashSet<long>();
        public float[] oyunUcgen = new float[0];
        public float oyunAlan, oyunAci;
        public const int OyunPay = 3, OyunEnKucuk = 100;
        void OyunHesapla()
        {
            oyun = new HashSet<long>();
            var yasak = new HashSet<long>(); int r2 = OyunPay * OyunPay;
            foreach (long q in engel)
            {
                Coz(q, out int ix, out int iz);
                for (int dz = -OyunPay; dz <= OyunPay; dz++) for (int dx = -OyunPay; dx <= OyunPay; dx++) if (dx * dx + dz * dz <= r2) yasak.Add(OdaSonuc.Anah2(ix + dx, iz + dz));
            }
            bool kesinVar = kesin != null && kesin.Count > 0;
            foreach (long q in acik) if (!yasak.Contains(q) && (!kesinVar || kesin.Contains(q))) oyun.Add(q);
            ZeminGeo.Ac(oyun, 2);
            ZeminGeo.KucukleriAt(oyun, OyunEnKucuk, null);
            var u = new List<float>();
            oyunAci = ZeminGeo.EnIyiAci(oyun, 1, 1);
            ZeminGeo.DuzKontur(oyun, oyunAci, u, null, 1, 1);
            oyunUcgen = u.ToArray(); oyunAlan = oyun.Count * V * V;
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
        public const float ZeminBant = 0.05f, EngelAlt = 0.08f, EngelUst = 2.0f;   // iki sinif: zemin (+-5 cm) ve 8 cm-2 m arasi her sey
        public const int EngelMinKare = 3;
        public static string DokumYolu;            // PC teshis: her guncellemede hucre dokumu (null = kapali)
        public static int GuvenEsik = 128; public static bool SadeceGuvenli = true;   // gercek kayitlarda dusuk guvenli noktalar yurunen yolu bile engel yapiyordu

        class Hucre { public int zemin, zN, engel, yol, uzun, ust; public int kz = -1, ke = -1, ku = -1, ks = -1; public bool plan;
            public int dF, dO, dN, dU; public int kdF = -1, kdO = -1, kdN = -1, kdU = -1; }

        // "Kesin zemin" (kullanici istegi, 21 Kasim: pürüzlü olacağına hiç gösterme, az ama temiz ve doğru):
        // bir hucre acik zemin sayilir ancak (a) kullanici oradan yurumusse ya da (b) derinlik orayi en az KesinKare karede
        // zemin olarak gorduyse, hic guvenli engel noktasi yoksa ve ustunde (15 cm - 1,2 m) duzenli nokta gorulmediyse
        // (masa / sandalye alti). ARCore duzlemleri artik kanit degil: gorulmemis masa altina tasiyordu.
        // Derinlik agirlikli teyit (kullanici, 22 Kasim: derinlik gercege cok yakin, daha cok kullan): hucre en az KesinKare
        // farkli karede ve toplam en az KesinNokta guvenli zemin noktasiyla goruldugunde teyitli. Yakinda (cok nokta) hizli,
        // uzakta (az nokta) daha uzun tutmak gerekir.
        public const int KesinKare = 3, KesinNokta = 6;
        static bool Teyitli(Hucre c) => c.zemin >= KesinKare && c.zN >= KesinNokta;
        // zemin mi diger mi: cogunluk oyu (kullanici, 22 Kasim: derinlik yalniz "zemin" ve "diger" ayirsin)
        /// <summary>Yalniz derinlik modu: bu hucrede karelerin en az dortte birinde zeminden 8 cm - 2 m yukarida bir sey
        /// goruluyor (masa ustu, sandalye, dolap). Altinda zemin gorunse de (masa alti) engel; dagınık gurultu orani gecmez.</summary>
        static bool KaliciEngel(Hucre c) { int o = Math.Max(c.engel, c.ust); return o >= 3 && o * EngelOranBolen >= c.zemin; }
        public static int UstGuven = 10, EngelOranBolen = 4; public static float EngelMenzil = 9f;
        public static volatile string TeshisSon = "";
        public static bool SadeceDerinlik = true;
        public static int IsinGuven = 64; public static float IsinMenzil = 3.0f;
        readonly DolulukHaritasi dol = new DolulukHaritasi();  // 23 Kasim: zemin yalniz derinlikten; ARCore duzlemi aday/duvar vermez
        // Engel payi (24 Kasim): 23 Kasim'da pay tamamen kaldirilinca (5 cm kare -> 0) boya kenarlarda titredi; simdi yalniz
        // kesinlesmis engelin 4 komsusu (eski payin yarisi kadar alan), kapi esigi / kenar gurultusu pay dogurmaz.
        public const int EngelPay = 1;
        // Sutun durumu histerezisle (24 Kasim, kullanici: "ayni degerleri gorunce degismesin"): hucre bir kez acik ya da engel
        // olunca ancak karsi yonde guclu kanit gelirse degisir. Kup degeri: carpma +9, gecis -4 (DolulukHaritasi).
        public const int EngelGir = 18;            // bilinmeyen -> engel: en az iki karede carpildi
        public const int EngelKalici = 25;         // acik -> engel: en az uc karede (net) carpildi
        public const int BosaltCik = -4;           // engel -> acik: sutundaki butun kupler sonradan bos goruldu (24 Kasim: -8 cok
                                                   // sikiydi, defalarca bos gorulen hucre -5'te takilip engel kaliyordu)
        public const int ZeminDoygun = 35;         // kesin zemin: zemin kupu doygun (4+ kare)
        readonly Dictionary<long, sbyte> sDurum = new Dictionary<long, sbyte>();   // 1 acik, 2 engel
        readonly Dictionary<long, byte> sYas = new Dictionary<long, byte>();       // durum kac guncellemedir ayni
        public static bool LidarAcik;
        public float Zy => zy;
        /// <summary>Teshis (PC): derinlikte zemini en az iki karede gorulmus hucreler (boyama gecikmesi olcumu).</summary>
        /// <summary>Teshis: hucrelerin sutun durumu sayilari [bilinmeyen (kanit zayif), acik, engel] ve bilinmeyenlerde en dolu kup dagilimi.</summary>
        public string DurumDokum(IEnumerable<long> hucreler)
        {
            int[] n = new int[3]; int z1 = 0, z2 = 0, z3 = 0; var su = dol.Sutunlar(zy);
            foreach (long k in hucreler)
            {
                sDurum.TryGetValue(k, out sbyte d); n[d]++;
                if (d == 0 && su.TryGetValue(k, out var c)) { if (c.enCok > 0 && c.enCok < EngelGir) z1++; else if (c.enCok <= 0) z2++; else z3++; }
            }
            return "bilinmeyen " + n[0] + " (zayif engel kaniti " + z1 + ", bos " + z2 + ", diger " + z3 + "), acik " + n[1] + ", engel " + n[2];
        }
        /// <summary>Teshis CSV: ix,iz,durum,enCok,zeminD,uzun (zemini ya da engeli gorulen her sutun).</summary>
        public string DurumCsv()
        {
            var sb = new System.Text.StringBuilder("ix,iz,durum,encok,zemind,uzun\n");
            foreach (var kv in dol.Sutunlar(zy))
            {
                ZeminSonuc.Coz(kv.Key, out int ix, out int iz); sDurum.TryGetValue(kv.Key, out sbyte d);
                sb.Append(ix).Append(',').Append(iz).Append(',').Append(d).Append(',').Append(kv.Value.enCok).Append(',').Append(kv.Value.zeminD).Append(',').Append(kv.Value.uzun ? 1 : 0).Append('\n');
            }
            return sb.ToString();
        }
        public HashSet<long> ZeminGorulen() { var r = new HashSet<long>(); foreach (var kv in dol.Sutunlar(zy)) if (kv.Value.zeminD >= DolulukHaritasi.ZeminKesin) r.Add(kv.Key); return r; }
        public static bool Histerezis = true;
        public static bool Duzlestirme = true;    // kenarlari ana yone hizali duzlestir (ZeminGeo.Duzlestir)
        public const int BuyukParca = 100;        // hucre (0,25 m2): bu buyuklukteki bagimsiz acik zemin de boyanir     // false: 23 Kasim kurali (her guncellemede yeniden karar, pay yok); karsilastirma icin              // LIDAR gorunumu istendi: guncellemede kupler kopyalanir
        float kamYSon;
        static bool EngelHucresi(Hucre c) => (c.engel >= EngelMinKare && c.engel > c.zemin) || (c.ust >= 3 && c.ust > c.zemin);
        public const int AcmaYaricap = 2;        // hucre: 10 cm'den ince saçaklar ve kenar pürüzü atilir (morfolojik acma)
        public const int EnKucukParca = 100;     // hucre: 0,25 m2'den kucuk acik zemin parcalari atilir
        public const int DelikDoldur = 60;       // hucre: icinde engel olmayan 0,15 m2'den kucuk delikler doldurulur
        readonly Dictionary<long, Hucre> h = new Dictionary<long, Hucre>();
        readonly List<float> izX = new List<float>(), izZ = new List<float>();
        readonly float[] mat = new float[9];
        float zy; bool zyVar; int kare;
        // ARCore zemini zamanla 10-20 cm kayabiliyor (gercek kayitta olculdu): her karede derinlik noktalarinin zemin
        // cevresindeki (+-30 cm) yukseklik histogramindan (1 cm, yavas unutan) tepe alinir; siniflandirma buna gore yapilir.
        // Zemin yuksekligi derinlikten (24 Kasim): 1 cm'lik yukseklik histogrami 4 m genisliginde ve bir kez kurulur. Eskiden
        // ARCore zemin duzlemi 15 cm oynayinca histogram sifirlanip zemin ARCore'a uyuyordu; 24 Kasim taramasinda ARCore 30 cm
        // asagida sahte duzlem bulunca zemin -0,18'den -0,48'e atladi, boyanan alan 7,7'den 2,1 m2'ye coktu.
        readonly float[] yHist = new float[400]; float yTaban = float.NaN, zyPlan; bool zyDerin; int zyDegisim; float zyAday;
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

        /// <summary>Kalem cizgileri (kayit icin, oturum koordinati): {"cizgiler":[{"xz":[x,z,...],"kx":..,"kz":..}]}.</summary>
        public string KalemJson()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder("{\"cizgiler\":[");
            KalemCizgisi[] cs; lock (cizgiKilit) cs = cizgiler.ToArray();
            for (int i = 0; i < cs.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"xz\":[");
                for (int j = 0; j < cs[i].xz.Length; j++) { if (j > 0) sb.Append(','); sb.Append(cs[i].xz[j].ToString("F3", ci)); }
                sb.Append("],\"kx\":").Append(cs[i].kx.ToString("F3", ci)).Append(",\"kz\":").Append(cs[i].kz.ToString("F3", ci)).Append('}');
            }
            return sb.Append("]}").ToString();
        }

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

        // Isinma (24 Kasim): bes kaydin besinde de ARCore derinliginin olcegi ilk 3-10 sn oturmuyor; kamera yerinde dururken
        // zemin 15-30 cm asagida goruluyor, sonra yaklasiyor (22:26 taramasi: kamera-zemin 1,56 -> 1,30 m). O karelerin
        // noktalari haritaya yanlis yerde (yukseklik ve uzaklik ~%20 yanlis) isleniyor, zemin tahminini de yaniltiyordu.
        // Kare ancak her karenin kendi zemin tepesi son 3 sn'de en az 8 karede 4 cm icinde sabitse islenir; bir kez
        // oturunca (ARCore olcegi oturdu) bir daha beklenmez.
        public const float IsinmaPencere = 3f, IsinmaAralik = 0.04f; public const int IsinmaKare = 8;
        public static bool IsinmaKullan = true;
        readonly List<float> kareZemin = new List<float>(), kareZaman = new List<float>();
        readonly float[] kHist = new float[180];
        bool isindi;
        public bool Isindi_ => isindi || !IsinmaKullan;
        public float IsinmaZamani = -1f;

        float sonKareZemin = float.NaN;
        /// <summary>Bu karenin kendi zemin tepesi (kameranin 0,3-2,1 m altinda en alcak guclu tepe); yoksa NaN.</summary>
        float KareZemini(DerinlikKaresi f, int m)
        {
            Array.Clear(kHist, 0, kHist.Length); float alt = f.py - 2.1f; int say = 0;
            for (int r = 0; r < m; r++) { int b = (int)Math.Floor((py_[r] - alt) / 0.01f); if (b >= 0 && b < kHist.Length && py_[r] < f.py - 0.3f) { kHist[b] += 1f; say++; } }
            if (say < 300) return float.NaN;
            float en = 0f; var sm = new float[kHist.Length];
            for (int b = 2; b < kHist.Length - 2; b++) { sm[b] = kHist[b - 2] + kHist[b - 1] + kHist[b] + kHist[b + 1] + kHist[b + 2]; en = Math.Max(en, sm[b]); }
            for (int b = 3; b < kHist.Length - 3; b++) if (sm[b] >= 0.35f * en && sm[b] >= sm[b - 1] && sm[b] >= sm[b + 1]) return alt + (b + 0.5f) * 0.01f;
            return float.NaN;
        }

        bool Isindi(DerinlikKaresi f, int m)
        {
            sonKareZemin = KareZemini(f, m);
            if (isindi || !IsinmaKullan) return true;
            // bu karenin zemin tepesi: kameranin 0,3-2,1 m altinda, 1 cm, 5'li yumusatma, en gucluun %35'ini gecen en alcak tepe
            if (float.IsNaN(sonKareZemin)) return false;
            kareZemin.Add(sonKareZemin); kareZaman.Add(f.zaman);
            while (kareZaman.Count > 0 && f.zaman - kareZaman[0] > IsinmaPencere) { kareZaman.RemoveAt(0); kareZemin.RemoveAt(0); }
            if (kareZemin.Count < IsinmaKare) return false;
            float lo = float.MaxValue, hi = float.MinValue; foreach (float z in kareZemin) { lo = Math.Min(lo, z); hi = Math.Max(hi, z); }
            if (hi - lo > IsinmaAralik) return false;
            isindi = true; IsinmaZamani = f.zaman;
            return true;
        }

        public void Sifirla() { isindi = false; IsinmaZamani = -1f; kareZemin.Clear(); kareZaman.Clear(); lock (cizgiKilit) cizgiler.Clear(); kalici.Clear(); duvarKalici.Clear(); kamYTop = 0; kamYSay = 0; aci = float.NaN; aciKilit = false; h.Clear(); dol.Temizle(); izX.Clear(); izZ.Clear(); zyVar = false; kare = 0; planHucre.Clear(); sDurum.Clear(); sYas.Clear(); zyDerin = false; yTaban = float.NaN; Array.Clear(yHist, 0, yHist.Length); }

        Hucre Al(long k) { if (!h.TryGetValue(k, out var c)) { c = new Hucre(); h[k] = c; } return c; }

        // ---------------------------------------------------------------- kare basina (arka plan is parcacigi)
        public void Isle(DerinlikKaresi f)
        {
            kare++; kamX = f.px; kamZ = f.pz; kamYTop += f.py; kamYSay++; kamYSon = f.py;
            DerinlikKaresi.DonmeMatrisi(f.qx, f.qy, f.qz, f.qw, mat);
            int n0 = f.w * f.h; if (px_.Length < n0) { px_ = new float[n0]; py_ = new float[n0]; pz_ = new float[n0]; pi_ = new int[n0]; }
            int m = 0;
            for (int v = 0; v < f.h; v++)
                for (int u = 0; u < f.w; u++)
                {
                    int i = v * f.w + u; float d = f.d[i];
                    if (!(d >= 0.3f && d <= 4.0f)) continue;
                    if (f.e != null && f.e[i] == OdaEtiket.Insan) continue;
                    f.YerelYon(u, v, out float lx, out float ly);
                    float wx = mat[0] * lx + mat[1] * ly + mat[2], wy = mat[3] * lx + mat[4] * ly + mat[5], wz = mat[6] * lx + mat[7] * ly + mat[8];
                    px_[m] = f.px + wx * d; py_[m] = f.py + wy * d; pz_[m] = f.pz + wz * d; pi_[m] = i; m++;
                }
            bool ilkIsinma = !isindi;
            if (!Isindi(f, m)) return;
            if (ilkIsinma && IsinmaKullan && kareZemin.Count > 0)
            {
                // isinma bitti: son 3 sn'nin zemini (4 cm icinde sabit) dogrudan derinlik zemini olur
                var ks = new List<float>(kareZemin); ks.Sort(); zy = ks[ks.Count / 2]; zyDerin = true;
                if (!zyVar) { zyPlan = zy; zyVar = true; }
            }
            if (!zyVar) return;
            if (Dogrudan) DogrudanSay(f, m);
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
            if (float.IsNaN(yTaban) || f.py - 2.3f < yTaban || f.py - 0.2f > yTaban + yHist.Length * 0.01f)
            { yTaban = f.py - 2.6f; Array.Clear(yHist, 0, yHist.Length); }
            for (int j = 0; j < yHist.Length; j++) yHist[j] *= 0.997f;   // yari omur ~20 sn: masaya kisa bakis zemini unutturmaz
            for (int r = 0; r < m; r++) { int b = (int)Math.Floor((py_[r] - yTaban) / 0.01f); if (b >= 0 && b < yHist.Length) yHist[b] += 1f; }
            {
                // aday tepeler: kameranin 0,4-2,0 m altinda, en gucluun en az %35'i; zemin en alcak buyuk yatay yuzeydir (masa
                // ustu daha yukarida). Buyuk degisim (5 cm+) ancak 30 kare surerse kabul (tek bakista atlamaz).
                float enS = 0f; var sm = new float[yHist.Length];
                for (int b = 2; b < yHist.Length - 2; b++) { sm[b] = yHist[b - 2] + yHist[b - 1] + yHist[b] + yHist[b + 1] + yHist[b + 2]; }
                int b0 = Math.Max(2, (int)((f.py - 2.0f - yTaban) / 0.01f)), b1 = Math.Min(yHist.Length - 3, (int)((f.py - 0.4f - yTaban) / 0.01f));
                for (int b = b0; b <= b1; b++) enS = Math.Max(enS, sm[b]);
                int sec = -1;
                if (enS > 300f)
                    for (int b = b0; b <= b1; b++) if (sm[b] >= 0.35f * enS && sm[b] >= sm[b - 1] && sm[b] >= sm[b + 1]) { sec = b; break; }
                if (sec >= 0)
                {
                    float yeni = yTaban + (sec + 0.5f) * 0.01f;
                    // 24 Kasim (21:33 taramasi): ilk karelerde ARCore oturmadan gelen yanlis yukseklik (-0,94; gercek -0,13)
                    // ilk bulusta kilitlenip 33 sn kaldi, o surede zemin boyanmadi. Simdi: ilk kilit 8 tutarli kare ister
                    // (ARCore zemin duzlemiyle 15 cm'den fazla celisirse 40); kilitliyken 5 cm icindeki degisim izlenir
                    // (ARCore dusey kaymasi); daha buyuk degisim, mevcut zeminin histogramda destegi kalmadiysa 8, yoksa
                    // 60 tutarli kare ister (masaya kisa bakis zemini oynatmaz).
                    if (zyDerin && Math.Abs(yeni - zy) <= 0.05f) { zy = yeni; zyDegisim = 0; }
                    else
                    {
                        if (Math.Abs(yeni - zyAday) <= 0.05f) zyDegisim++; else { zyAday = yeni; zyDegisim = 1; }
                        int gerek;
                        if (!zyDerin) gerek = zyVar && Math.Abs(yeni - zyPlan) > 0.15f ? 40 : 8;
                        else
                        {
                            int bz = (int)Math.Floor((zy - yTaban) / 0.01f);
                            float destek = bz >= 2 && bz < yHist.Length - 2 ? sm[bz] : 0f;
                            gerek = destek < 0.15f * enS ? 8 : 60;
                        }
                        if (zyDegisim >= gerek) { zy = yeni; zyDerin = true; zyDegisim = 0; }
                    }
                }
                if (!zyDerin && zyVar) zy = zyPlan;
            }
            // derinlik LIDAR gibi: guvenli her ikinci pikselden isin (aradan gecilen kupler bos, carpilan dolu)
            if (SadeceDerinlik)
            {
                dol.YeniKare();
                for (int gecis = 0; gecis < 2; gecis++)
                    for (int r = 0; r < m; r++)
                    {
                        int i = pi_[r], uu = i % f.w, vv = i / f.w;
                        if (((uu | vv) & 1) != 0) continue;
                        if (f.g != null && f.g[i] < IsinGuven) continue;
                        bool carp = f.d[i] <= IsinMenzil;
                        if (gecis == 0) { if (carp) dol.Carpma(px_[r], py_[r], pz_[r]); }
                        else dol.Isin(f.px, f.py, f.pz, px_[r], py_[r], pz_[r], false, zy + 0.05f);
                    }
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
                        if (f.g == null || f.g[i] >= 64) c.zN++;
                    }
                    else if (yk >= EngelAlt && yk <= 2.3f)
                    {
                        // uzun engel (duvar, dolap, kapi kasasi): 1 m ustundeki noktalar zemin gurultusu olamaz, dusuk guvende de sayilir
                        // ustunde bir sey var (masa, sandalye oturagi): dusuk guvende de sayilir; o hucre kesin zemin olamaz
                        if (yk >= 0.15f && yk <= 1.2f && (f.g == null || f.g[i] >= UstGuven) && d <= EngelMenzil)
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
        //  3) sinir: zemine inen her dikey duzlem (duvar / esya ayrimi yok, ana yone yapistirilir) + derinlikte 1 m ustu noktalar + kalem
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
        float aci = float.NaN; bool aciKilit; int YukSon;

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
                if (!zyVar || Math.Abs(secY - zyPlan) > 0.3f) { zyPlan = secY; if (!zyDerin) zy = secY; }      // yanlis secimden kurtul (derinlik kurduysa zemin onun)
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
                    double a = DikeyAlan(p.sinir); double t = -Math.Atan2(p.nx, p.nz) * 4;   // normal +z'den saat yonu, aci +x'ten ters yon
                    sx += Math.Cos(t) * a; sz += Math.Sin(t) * a; w += a;
                }
                // dikey duzlem azsa (beyaz duvarda ARCore cogu zaman hic bulmuyor, 22 Kasim 20:54 taramasi): derinlikte 1 m
                // ustu gorulen hucreler (duvar, dolap) hangi acida en keskin cizgileri olusturuyorsa o
                int yukSay = 0;
                if (w < 2.0)
                {
                    var yx = new List<float>(); var yz = new List<float>();
                    foreach (var kv in h)
                        if (kv.Value.uzun >= EngelMinKare && kv.Value.yol == 0) { ZeminSonuc.Coz(kv.Key, out int ix, out int iz); yx.Add((ix + 0.5f) * V); yz.Add((iz + 0.5f) * V); }
                    yukSay = yx.Count; YukSon = yukSay;
                    if (yukSay >= 150)
                    {
                        double enA = 0, enP = -1;
                        for (int adim = 0; adim < 2; adim++)
                        {
                            double bas = adim == 0 ? 0 : enA - 1.0 * Math.PI / 180, son = adim == 0 ? Math.PI / 2 : enA + 1.0 * Math.PI / 180, ad = adim == 0 ? Math.PI / 180 : 0.1 * Math.PI / 180;
                            for (double t = bas; t < son; t += ad)
                            {
                                double pu = Keskinlik(yx, yz, Math.Cos(t), Math.Sin(t));
                                if (pu > enP) { enP = pu; enA = t; }
                            }
                        }
                        enA = enA % (Math.PI / 2); if (enA > Math.PI / 4) enA -= Math.PI / 2;
                        sx = Math.Cos(enA * 4) * 10; sz = Math.Sin(enA * 4) * 10; w = Math.Max(w, 0.5);
                        if (yukSay >= 1200) aciKilit = true;
                    }
                }
                if (w < 0.5)
                    foreach (var p in planlar)
                    {
                        if (p.tur != 0 || p.sinir == null || p.sinir.Length < 9 || Math.Abs(OrtY(p.sinir) - zy) > 0.10f) continue;
                        int m = p.sinir.Length / 3;
                        for (int i = 0, j2 = m - 1; i < m; j2 = i++)
                        {
                            double dx = p.sinir[i * 3] - p.sinir[j2 * 3], dz = p.sinir[i * 3 + 2] - p.sinir[j2 * 3 + 2], L = Math.Sqrt(dx * dx + dz * dz);
                            double t = -Math.Atan2(dx, dz) * 4; sx += Math.Cos(t) * L * 0.3; sz += Math.Sin(t) * L * 0.3;
                        }
                    }
                if (sx * sx + sz * sz > 1e-6) aci = (float)(Math.Atan2(sz, sx) / 4);
                if (w >= 2.0) aciKilit = true;            // yeterince duvar goruldu: yon sabit (harita titremesin)
            }
            if (float.IsNaN(aci)) aci = 0f;
            s.aci = aci;
            if (Dogrudan) { DogrudanSonuc(s, kamY); return s; }
            if (SadeceDerinlik && Sade) { SadeSonuc(s, kamY); return s; }
            float ca = (float)Math.Cos(aci), sa = (float)Math.Sin(aci);

            // 3) duvarlar
            var duvar = new HashSet<long>(); int dikeySay = 0;
            foreach (var p in planlar)
            {
                if (p.tur != 2 || p.sinir == null || p.sinir.Length < 9) continue;
                int m = p.sinir.Length / 3; float yUst = float.MinValue, yAlt = float.MaxValue; double cx = 0, cz = 0;
                for (int i = 0; i < m; i++) { yUst = Math.Max(yUst, p.sinir[i * 3 + 1]); yAlt = Math.Min(yAlt, p.sinir[i * 3 + 1]); cx += p.sinir[i * 3]; cz += p.sinir[i * 3 + 2]; }
                // esya / duvar ayrimi yok (kullanici, 22 Kasim): zemine inen her dikey duzlem (duvar, dolap, kanepe yani)
                // sinirdir; duz bir cizgi olarak islenir. Yalniz zemine degmeyen (masa ustu kenari gibi) ya da cok alcak olan atlanir.
                if (yAlt - zy > 0.30f || yUst - zy < 0.25f) continue;
                dikeySay++;
                if (SadeceDerinlik) continue;             // duvar derinlikten (1 m ustu noktalar); duzlem yalniz ana yon icin
                cx /= m; cz /= m;
                // duvar dogrultusu (normale dik), ana yone yakinsa ona yapistir
                double nAci = Math.Atan2(p.nx, p.nz); double fark = nAci + aci; fark -= Math.Round(fark / (Math.PI / 2)) * (Math.PI / 2);
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
            // yurunen yer duvar olamaz (acik kapi kanadi gibi uzun dikey duzlem kapi agzini kapatiyordu)
            duvar.RemoveWhere(k => h.TryGetValue(k, out var yc) && yc.yol > 0);
            duvarKalici.UnionWith(duvar);
            foreach (long k in duvarKalici) if (!h.TryGetValue(k, out var dc) || dc.yol == 0) duvar.Add(k);
            var kHat = new HashSet<long>(); var kAcik = new HashSet<long>(); var kKapali = new HashSet<long>();
            KalemHucreleri(kHat, kAcik, kKapali);
            duvar.ExceptWith(kAcik);                       // kullanicinin cizgisi otomatik duvari duzeltir
            foreach (long k in kHat) duvar.Add(k);

            // 4) esya: 20 cm kaba kareler (ana yone hizali); yurunen yer esya olamaz
            // 22 Kasim (ev 2): eski kural (30 cm karede 2 engelli hucre yeter) derinligin daginik gurultusuyle koridorlari
            // kapatiyordu (149 kaba kare = 13 m2). Simdi oylama: hucre engelse engel kareleri zemin karelerinden fazla; kaba
            // kare, gozlenen hucrelerinin en az dortte biri engelse esya.
            var kabaSay = new Dictionary<long, int>(); var kabaGoz = new Dictionary<long, int>(); var kabaYol = new HashSet<long>();
            foreach (var kv in h)
            {
                var c = kv.Value;
                long kk = Kaba(kv.Key, ca, sa);
                if (c.yol > 0) { kabaYol.Add(kk); continue; }
                if (c.zemin + c.engel + c.ust == 0) continue;
                kabaGoz[kk] = (kabaGoz.TryGetValue(kk, out int g) ? g : 0) + 1;
                if (EngelHucresi(c)) kabaSay[kk] = (kabaSay.TryGetValue(kk, out int n) ? n : 0) + 1;
            }
            var esya = new HashSet<long>();
            if (!SadeceDerinlik)
                foreach (var kv in kabaSay) if (kv.Value >= 3 && kv.Value * 4 >= kabaGoz[kv.Key] && !kabaYol.Contains(kv.Key)) esya.Add(kv.Key);
            // yalniz derinlik (23 Kasim): hucre basina kalici engel (engel / ust kanitlarinin zemine orani), 5 cm payla
            var engelH = new HashSet<long>(); Dictionary<long, DolulukHaritasi.Sutun> sutun = null;
            if (SadeceDerinlik)
            {
                sutun = dol.Sutunlar(zy);
                foreach (var kv in sutun)
                {
                    var su = kv.Value; sDurum.TryGetValue(kv.Key, out sbyte d0); sbyte d = d0;
                    bool zeminVar = su.zeminD >= DolulukHaritasi.ZeminKesin;
                    if (!Histerezis) d = su.enCok > 0 ? (sbyte)2 : zeminVar ? (sbyte)1 : (sbyte)0;
                    else if (d == 0) { if (su.enCok >= EngelGir) d = 2; else if (zeminVar && su.enCok <= 0) d = 1; }
                    else if (d == 1) { if (su.enCok >= EngelKalici) d = 2; }
                    else if (su.enCok <= BosaltCik && zeminVar) d = 1;
                    if (d != d0) { sDurum[kv.Key] = d; sYas[kv.Key] = 0; }
                    else if (d != 0) { sYas.TryGetValue(kv.Key, out byte y); if (y < 255) sYas[kv.Key] = (byte)(y + 1); }
                }
                foreach (var kv in sDurum) if (kv.Value == 2 && !(h.TryGetValue(kv.Key, out var yc) && yc.yol > 0)) engelH.Add(kv.Key);
                var pay = new HashSet<long>(engelH);
                foreach (long k in engelH)
                {
                    if (!Histerezis || !sDurum.TryGetValue(k, out sbyte sdk) || sdk != 2) continue;     // engel durumu (histerezisli: pay titremez)
                    ZeminSonuc.Coz(k, out int ix, out int iz);
                    for (int q = 0; q < 4; q++)
                    {
                        long a = OdaSonuc.Anah2(ix + (q == 0 ? EngelPay : q == 1 ? -EngelPay : 0), iz + (q == 2 ? EngelPay : q == 3 ? -EngelPay : 0));
                        // cok kesin gorulmus zemin pay icin kapatilmaz (esya dibine kadar dogru zemin)
                        if (sutun.TryGetValue(a, out var sa2) && sa2.zeminD >= ZeminDoygun && sa2.enCok <= BosaltCik) continue;
                        pay.Add(a);
                    }
                }
                foreach (var kv in h) if (kv.Value.yol > 0) pay.Remove(kv.Key);
                engelH = pay;
            }

            // 5) aday zemin. ARCore duzlemi gorulmemis yere de tasabiliyor (21 Kasim cok odali taramada son zemin duzlemi
            // butun binayi kaplayan dev bir cokgendi): duzlem hucresi yalniz derinlikte zemin gorulen ya da yurunen yerin
            // 30 cm yakininda sayilir
            var kanit = new HashSet<long>();
            foreach (var kv in h) if (Teyitli(kv.Value) || kv.Value.yol > 0) { ZeminSonuc.Coz(kv.Key, out int ix, out int iz); kanit.Add(OdaSonuc.Anah2(FloorDiv(ix, 4), FloorDiv(iz, 4))); }
            var aday = new HashSet<long>();
            foreach (var p in planlar)
            {
                if (SadeceDerinlik) break;            // ARCore duzlemleri aday vermez (kullanici, 23 Kasim: duzlemler cok kotu)
                if (p.tur != 0 || p.sinir == null || p.sinir.Length < 9 || Math.Abs(OrtY(p.sinir) - zy) > 0.10f) continue;
                foreach (long k in PoligonHucreleri(p.sinir))
                {
                    ZeminSonuc.Coz(k, out int ix, out int iz); int cx = FloorDiv(ix, 4), cz = FloorDiv(iz, 4); bool yakin = false;
                    for (int dz = -1; dz <= 1 && !yakin; dz++) for (int dx = -1; dx <= 1; dx++) if (kanit.Contains(OdaSonuc.Anah2(cx + dx, cz + dz))) { yakin = true; break; }
                    if (yakin) aday.Add(k);
                }
            }
            if (SadeceDerinlik)
            {
                foreach (var kv in sDurum) if (kv.Value == 1) aday.Add(kv.Key);
                // uzakta derinlik seyrek: zemin hucreleri arasinda bosluk kaliyor, parcalar birbirine baglanmiyordu. Engel olmayan
                // ve 8 komsusunun en az 4'u zemin olan hucre de aday (tek gecislik kapama)
                var ek = new List<long>();
                foreach (long k in aday)
                {
                    ZeminSonuc.Coz(k, out int ix, out int iz);
                    for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                    {
                        long q = OdaSonuc.Anah2(ix + dx, iz + dz);
                        if (aday.Contains(q) || (sDurum.TryGetValue(q, out sbyte dq) && dq == 2)) continue;
                        int n = 0;
                        for (int ez = -1; ez <= 1; ez++) for (int ex = -1; ex <= 1; ex++) if ((ex != 0 || ez != 0) && aday.Contains(OdaSonuc.Anah2(ix + dx + ex, iz + dz + ez))) n++;
                        if (n >= 4) ek.Add(q);
                    }
                }
                foreach (long q in ek) aday.Add(q);
                foreach (var kv in h) if (kv.Value.yol > 0) aday.Add(kv.Key);
            }
            else
                foreach (var kv in h) if (kv.Value.yol > 0 || (Teyitli(kv.Value) && !EngelHucresi(kv.Value))) aday.Add(kv.Key);
            foreach (long k in kAcik) aday.Add(k);
            Func<long, bool> kapali = k => duvar.Contains(k) || kKapali.Contains(k) || (!kAcik.Contains(k) && (esya.Contains(Kaba(k, ca, sa)) || engelH.Contains(k)));
            aday.RemoveWhere(k => kapali(k));

            // 6) bagli: tohum = yurunen yol + kameranin alti + onceden acik olanlar
            var acik = new HashSet<long>(); var yigin = new Stack<long>();
            foreach (var kv in h) if (kv.Value.yol > 0 && aday.Contains(kv.Key) && acik.Add(kv.Key)) yigin.Push(kv.Key);
            { long kc = OdaSonuc.Anah2(ZeminSonuc.Ind(kamX), ZeminSonuc.Ind(kamZ)); if (aday.Contains(kc) && acik.Add(kc)) yigin.Push(kc); }
            foreach (long k in kalici) if (aday.Contains(k) && acik.Add(k)) yigin.Push(k);
            // 24 Kasim: derinlikte acik bulunan buyuk zemin parcasi da tohum (en az BuyukParca hucre). Yalniz yurunen yere
            // bagli olani boyamak ARCore duzlemleri doneminden kalma (duzlem duvar arkasina tasiyordu); derinlikte gorulen
            // genis zemin gercek, araya giren ince engel seridi yuzunden boyanmiyordu (Ev 7: zemini gorulen hucrelerin %15'i).
            if (SadeceDerinlik)
            {
                var gez = new HashSet<long>(acik); var yg = new Stack<long>(); var parca = new List<long>();
                foreach (long k0 in aday)
                {
                    if (!gez.Add(k0)) continue;
                    parca.Clear(); yg.Push(k0);
                    while (yg.Count > 0)
                    {
                        long k = yg.Pop(); parca.Add(k); ZeminSonuc.Coz(k, out int ix, out int iz);
                        long b1 = OdaSonuc.Anah2(ix + 1, iz), b2 = OdaSonuc.Anah2(ix - 1, iz), b3 = OdaSonuc.Anah2(ix, iz + 1), b4 = OdaSonuc.Anah2(ix, iz - 1);
                        if (aday.Contains(b1) && gez.Add(b1)) yg.Push(b1);
                        if (aday.Contains(b2) && gez.Add(b2)) yg.Push(b2);
                        if (aday.Contains(b3) && gez.Add(b3)) yg.Push(b3);
                        if (aday.Contains(b4) && gez.Add(b4)) yg.Push(b4);
                    }
                    if (parca.Count >= BuyukParca) foreach (long k in parca) acik.Add(k);
                }
            }
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
                // 22 Kasim (ev 2): bir kez boyanan yer gidince kullanici guvenini kaybediyor; silmek icin engel iki kat baskin olmali
                if (SadeceDerinlik) return sDurum.TryGetValue(k, out sbyte sd) && sd == 2;
                return (c.engel >= 8 && c.engel > 2 * c.zemin) || (c.ust >= 8 && c.ust > 2 * c.zemin);
            };
            var kalan = new HashSet<long>(); int gDuvar = 0, gEngel = 0;
            foreach (long k in kalici) { if (!kaliciGider(k)) kalan.Add(k); else if (duvar.Contains(k)) gDuvar++; else gEngel++; }
            acik.UnionWith(kalan);
            if (AcmaKullan) ZeminGeo.Ac(acik, 1);
            ZeminGeo.KucukleriAt(acik, 40, null);
            acik.UnionWith(kalan);
            if (DelikEngelli) { var dk = new HashSet<long>(duvar); dk.UnionWith(engelH); ZeminGeo.DelikDoldur(acik, dk, DelikEnCok); }
            else ZeminGeo.DelikDoldur(acik, duvar, DelikEnCok);
            if (SadeceDerinlik && Duzlestirme) ZeminGeo.Duzlestir(acik, aci, k => duvar.Contains(k) || (engelH.Contains(k) && !kAcik.Contains(k)) || kKapali.Contains(k));
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
                // 24 Kasim: yalniz kalem cizgisine degen kopuk parca atilir (cizgi onu ayirdi). Cizgiyle ilgisi olmayan uzak
                // zemin parcasi kalir (Ev 7: 30 cm'lik tek cizgi odanin obur ucundaki genis zemini de siliyordu).
                if (bag.Count > 0)
                {
                    var gez = new HashSet<long>(bag); var parca = new List<long>(); var at = new List<long>();
                    foreach (long k0 in acik)
                    {
                        if (!gez.Add(k0)) continue;
                        parca.Clear(); yg.Push(k0); bool degiyor = false;
                        while (yg.Count > 0)
                        {
                            long k = yg.Pop(); parca.Add(k); ZeminSonuc.Coz(k, out int ix, out int iz);
                            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                            {
                                long q = OdaSonuc.Anah2(ix + dx, iz + dz);
                                if (kHat.Contains(q)) degiyor = true;
                                if ((dx == 0 || dz == 0) && acik.Contains(q) && gez.Add(q)) yg.Push(q);
                            }
                        }
                        if (degiyor) at.AddRange(parca);
                    }
                    foreach (long k in at) acik.Remove(k);
                }
            }
            kalici.Clear(); foreach (long k in acik) kalici.Add(k);

            // teyit bekleyen (1..KesinKare-1 karede zemin gorulmus) hucreler: yonlendirme halkasi ilerlemesi
            if (SadeceDerinlik) { foreach (var kv in sutun) if (kv.Value.zeminKismi && !kv.Value.zemin && !acik.Contains(kv.Key)) s.kismi[kv.Key] = (byte)(KesinNokta / 2); }
            else foreach (var kv in h) if (kv.Value.zemin > 0 && !Teyitli(kv.Value) && !acik.Contains(kv.Key)) s.kismi[kv.Key] = (byte)Math.Min(KesinNokta, Math.Min(kv.Value.zemin * 2, kv.Value.zN));
            // kesinlesmis hucreler ve LIDAR gorunumu
            if (SadeceDerinlik)
            {
                foreach (var kv in sDurum)
                {
                    if (!sYas.TryGetValue(kv.Key, out byte y) || y < 2 || !sutun.TryGetValue(kv.Key, out var su)) continue;
                    if ((kv.Value == 1 && su.zeminD >= ZeminDoygun && acik.Contains(kv.Key)) || (kv.Value == 2 && su.enCok >= EngelKalici)) s.kesin.Add(kv.Key);
                }
                foreach (long k in acik) if (h.TryGetValue(k, out var yc) && yc.yol > 0) s.kesin.Add(k);
                if (LidarAcik) s.voksel = dol.Noktalar(kamX, kamYSon, kamZ, 4.0f, zy, 40000);
            }
            // sonuc kumeleri
            s.acik = acik;
            s.duvar = duvar;
            foreach (long k in duvar) s.engel.Add(k);
            foreach (var kv in h)
            {
                if (acik.Contains(kv.Key)) continue;
                if (!kAcik.Contains(kv.Key) && (esya.Contains(Kaba(kv.Key, ca, sa)) || engelH.Contains(kv.Key))) s.engel.Add(kv.Key);
                else if (!duvar.Contains(kv.Key)) s.bilinen.Add(kv.Key);
            }
            TeshisSon = Teshis = "yuksek " + YukSon + " dikey " + dikeySay + " duvarH " + duvar.Count + " | gitti duvar " + gDuvar + " engel " + gEngel + " | aday " + aday.Count + " acik " + acik.Count + " duvar " + duvar.Count + " esya(20cm) " + esya.Count + " aci " + (aci * 180 / Math.PI).ToString("F1") + " zy " + zy.ToString("F2") + " kamY " + kamY.ToString("F2");
            if (DokumYolu != null)
            {
                // teshis (yalniz PC tekrar oynatma): hucre basina sayaclar ve hangi kumede oldugu
                var sb = new System.Text.StringBuilder("ix,iz,zemin,zN,engel,ust,uzun,yol,teyit,aday,duvar,esya,acik\n");
                var hepsi = new HashSet<long>(h.Keys); hepsi.UnionWith(acik); hepsi.UnionWith(duvar);
                foreach (long k in hepsi)
                {
                    ZeminSonuc.Coz(k, out int ix, out int iz); h.TryGetValue(k, out var c); c = c ?? new Hucre();
                    sb.Append(ix).Append(',').Append(iz).Append(',').Append(c.zemin).Append(',').Append(c.zN).Append(',').Append(c.engel).Append(',').Append(c.ust).Append(',').Append(c.uzun).Append(',').Append(c.yol).Append(',')
                      .Append(Teyitli(c) ? 1 : 0).Append(',').Append(aday.Contains(k) ? 1 : 0).Append(',').Append(duvar.Contains(k) ? 1 : 0).Append(',').Append(esya.Contains(Kaba(k, ca, sa)) || engelH.Contains(k) ? 1 : 0).Append(',').Append(acik.Contains(k) ? 1 : 0).Append('\n');
                }
                System.IO.File.WriteAllText(DokumYolu, sb.ToString());
            }
            s.alan = s.acik.Count * V * V;
            var iz2 = new float[izX.Count * 2]; for (int i = 0; i < izX.Count; i++) { iz2[i * 2] = izX[i]; iz2[i * 2 + 1] = izZ[i]; }
            s.iz = iz2;
            s.Yumusat();
            YonEksik(s);
            return s;
        }

        // ------------------------------------------------------------------------------------------------ sade kural
        // 24 Kasim (kullanici: "koydugum kurallari bosver, en bastan amaci belirle"; "lidardaki mavi noktalarin ard arda
        // oldugu yerleri hassas alsin, sinirlari ona gore"). Amac: robotun gidebilecegi ve AR nesnesinin konabilecegi zemin
        // = derinligin zemin olarak gordugu ve ustunde hicbir sey olmayan yer. Kural yalniz 3B doluluk haritasindan:
        //   acik  : zemin yuzeyi kupu en az iki ayri karede carpildi (LIDAR gorunumundeki zemin noktasi) ve zeminin
        //           8 cm - 2 m ustundeki hicbir kup dolu degil (masa / sandalye alti engel kalir: AR nesnesi oraya konmaz)
        //   engel : o sutunda en az iki karede carpilmis kup var
        //   yurunen yer acik (kullanici orada durdu)
        // Baglantililik, pay, duzlestirme, kalici boya, kalem yok. Yalniz 3 hucreden kucuk tekil lekeler atilir ve icinde
        // engel olmayan en cok 2 hucrelik delikler doldurulur. Sinir hucre hucre (puruzlu ama derinlikle ayni) cizilir.
        // Durum zaten kuplerin log-olasiligindan geliyor (tek karede degismez), ayrica histerezis gerekmiyor.
        // ------------------------------------------------------------------------------------------------ dogrudan derinlik
        // 24 Kasim (kullanici: "en soldaki, direkt depth'ten gelen en iyisiydi, kapiyi bile farketmis; onun uzerinden gidelim,
        // adaciklari silelim"). Kayitlardaki referans haritanin kurali, canli: her kare kendi zeminini bulur (ARCore dusey
        // kaymasina dayanikli); guvenli (128+) ve 3 m'den yakin noktalar hucreye sayilir:
        //   dF: zemin (+-4 cm) gorulen kare, dO: zeminden 15 cm - 2 m nokta gorulen kare, dN: hucreye nokta dusen kare.
        //   acik  = dF >= 2 ve engel degil;  engel = dO >= 3 ve dO >= %30 dN (esya kenarindaki tek tuk tasma engel yapmaz)
        // Sonra yalniz 0,25 m2'den kucuk adaciklar atilir. Baglanti / pay / duzlestirme yok.
        public static bool Dogrudan = true;
        public const int DogrudanAdacik = 100, DogrudanKesin = 4;
        public const float DogrudanOran = 0.3f, DogrudanUst = 0.15f, DogrudanMenzil = 3.0f;

        void DogrudanSay(DerinlikKaresi f, int m)
        {
            float zf = float.IsNaN(sonKareZemin) ? zy : sonKareZemin;
            for (int r = 0; r < m; r++)
            {
                int i = pi_[r];
                if (f.d[i] > DogrudanMenzil || (f.g != null && f.g[i] < 128)) continue;
                float hk = py_[r] - zf; if (hk <= -0.04f || hk >= 2.0f) continue;
                var c = Al(OdaSonuc.Anah2(ZeminSonuc.Ind(px_[r]), ZeminSonuc.Ind(pz_[r])));
                if (c.kdN != kare) { c.kdN = kare; c.dN++; }
                if (hk < 0.04f) { if (c.kdF != kare) { c.kdF = kare; c.dF++; } }
                else if (hk > DogrudanUst) { if (c.kdO != kare) { c.kdO = kare; c.dO++; } if (hk > 1.0f && c.kdU != kare) { c.kdU = kare; c.dU++; } }
            }
        }

        static bool DogrudanEngel(Hucre c) => c.dO >= 3 && c.dO >= DogrudanOran * c.dN;

        void DogrudanSonuc(ZeminSonuc s, float kamY)
        {
            var acik = new HashSet<long>(); var engel = new HashSet<long>(); var duvar = new HashSet<long>();
            foreach (var kv in h)
            {
                var c = kv.Value;
                if (c.yol > 0) { acik.Add(kv.Key); continue; }
                if (DogrudanEngel(c)) { engel.Add(kv.Key); if (c.dU >= 3) duvar.Add(kv.Key); }
                else if (c.dF >= 2) acik.Add(kv.Key);
            }
            ZeminGeo.KucukleriAt(acik, DogrudanAdacik, null);
            foreach (var kv in h)
            {
                var c = kv.Value;
                if (acik.Contains(kv.Key)) { if (c.dF >= DogrudanKesin || c.yol > 0) s.kesin.Add(kv.Key); }
                else if (engel.Contains(kv.Key)) { if (c.dO >= 5) s.kesin.Add(kv.Key); }
                else if (c.dN > 0) { s.bilinen.Add(kv.Key); if (c.dF == 1) s.kismi[kv.Key] = (byte)(KesinNokta / 2); }
            }
            if (LidarAcik) s.voksel = dol.Noktalar(kamX, kamYSon, kamZ, 4.0f, zy, 40000);
            s.acik = acik; s.engel = engel; s.duvar = duvar;
            TeshisSon = Teshis = "dogrudan | acik " + acik.Count + " engel " + engel.Count + " duvar " + duvar.Count + " aci " + (aci * 180 / Math.PI).ToString("F1") + " zy " + zy.ToString("F2") + " kamY " + kamY.ToString("F2");
            s.alan = acik.Count * V * V;
            var iz2 = new float[izX.Count * 2]; for (int i = 0; i < izX.Count; i++) { iz2[i * 2] = izX[i]; iz2[i * 2 + 1] = izZ[i]; }
            s.iz = iz2;
            s.Yumusat();
            YonEksik(s);
        }

        public static int DelikEnCok = 60; public static bool DelikEngelli, AcmaKullan = true;
        public static bool Sade = false;   // olculdu (24 Kasim, 5 kayit): zemini %95 boyuyor ama esya kenarina 2,4 kat fazla tasiyor; varsayilan eski kural
        public const int SadeLeke = 3, SadeDelik = 2;
        public static int SadeEngel = 35, SadeBosalt = 10, SadeAcik = 18, SadePay = 0; public static bool SadePay8;

        void SadeSonuc(ZeminSonuc s, float kamY)
        {
            var sutun = dol.Sutunlar(zy);
            var acik = new HashSet<long>(); var engel = new HashSet<long>(); var duvar = new HashSet<long>();
            foreach (var kv in sutun)
            {
                var su = kv.Value; sDurum.TryGetValue(kv.Key, out sbyte d0); sbyte d;
                bool zem = su.zeminD >= DolulukHaritasi.ZeminKesin;
                // durum gecisi (titreme olcumuyle secildi): acik bir hucre ancak engel kaniti SadeEngel'e cikarsa engel olur,
                // engel ancak kup SadeBosalt'in altina inerse acilir (esya kenarinda +9 / -4 gidip gelen kup boyayi oynatmasin)
                if (d0 == 1) d = su.enCok >= SadeEngel ? (sbyte)2 : (sbyte)1;
                else if (d0 == 2) d = su.enCok < SadeBosalt && zem ? (sbyte)1 : (sbyte)2;
                else d = su.enCok >= EngelGir ? (sbyte)2 : zem && su.enCok < SadeAcik ? (sbyte)1 : (sbyte)0;
                if (d != d0) sDurum[kv.Key] = d;
                if (d == 2) { engel.Add(kv.Key); if (su.uzun) duvar.Add(kv.Key); }
                else if (d == 1) acik.Add(kv.Key);
            }
            if (SadePay > 0)
            {
                var at = new List<long>();
                foreach (long k in acik)
                {
                    ZeminSonuc.Coz(k, out int ix, out int iz); bool yakin = false;
                    for (int dz = -SadePay; dz <= SadePay && !yakin; dz++) for (int dx = -SadePay; dx <= SadePay; dx++)
                        if ((SadePay8 || dx == 0 || dz == 0) && (dx != 0 || dz != 0) && engel.Contains(OdaSonuc.Anah2(ix + dx, iz + dz))) { yakin = true; break; }
                    if (yakin) at.Add(k);
                }
                foreach (long k in at) acik.Remove(k);
            }
            foreach (var kv in h) if (kv.Value.yol > 0) { acik.Add(kv.Key); engel.Remove(kv.Key); duvar.Remove(kv.Key); }
            ZeminGeo.KucukleriAt(acik, SadeLeke, null);
            ZeminGeo.DelikDoldur(acik, engel, SadeDelik);
            foreach (var kv in sutun)
            {
                if (acik.Contains(kv.Key)) { if (kv.Value.zeminD >= ZeminDoygun) s.kesin.Add(kv.Key); }
                else if (engel.Contains(kv.Key)) { if (kv.Value.enCok >= EngelKalici) s.kesin.Add(kv.Key); }
                else { s.bilinen.Add(kv.Key); if (kv.Value.zeminKismi) s.kismi[kv.Key] = (byte)(KesinNokta / 2); }
            }
            foreach (var kv in h) if (kv.Value.yol > 0) s.kesin.Add(kv.Key);
            if (LidarAcik) s.voksel = dol.Noktalar(kamX, kamYSon, kamZ, 4.0f, zy, 40000);
            s.acik = acik; s.engel = engel; s.duvar = duvar;
            TeshisSon = Teshis = "sade | acik " + acik.Count + " engel " + engel.Count + " duvar " + duvar.Count + " aci " + (aci * 180 / Math.PI).ToString("F1") + " zy " + zy.ToString("F2") + (zyDerin ? " derinlik" : " duzlem") + " (duzlem " + zyPlan.ToString("F2") + ", aday " + zyAday.ToString("F2") + " x" + zyDegisim + ") kamY " + kamY.ToString("F2");
            s.alan = acik.Count * V * V;
            var iz2 = new float[izX.Count * 2]; for (int i = 0; i < izX.Count; i++) { iz2[i * 2] = izX[i]; iz2[i * 2 + 1] = izZ[i]; }
            s.iz = iz2;
            s.Yumusat();
            YonEksik(s);
        }

        /// <summary>Noktalarin (c, s) yonune ve ona dik yone 5 cm'lik izdusum histogramlarinin kareler toplami: duvar
        /// cizgileri bu eksenlere paralelse tepeler keskin, toplam buyuk.</summary>
        static double Keskinlik(List<float> xs, List<float> zs, double c, double s)
        {
            var hu = new Dictionary<int, int>(); var hw = new Dictionary<int, int>();
            for (int i = 0; i < xs.Count; i++)
            {
                int u = (int)Math.Floor((xs[i] * c + zs[i] * s) / 0.05), w = (int)Math.Floor((-xs[i] * s + zs[i] * c) / 0.05);
                hu[u] = (hu.TryGetValue(u, out int a) ? a : 0) + 1; hw[w] = (hw.TryGetValue(w, out int b) ? b : 0) + 1;
            }
            double t = 0; foreach (var v in hu.Values) t += (double)v * v; foreach (var v in hw.Values) t += (double)v * v;
            return t;
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
