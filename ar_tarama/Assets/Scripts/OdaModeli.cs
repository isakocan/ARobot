using System;
using System.Collections.Generic;

namespace ArTarama
{
    // =====================================================================================================
    // Oda modeli cekirdegi v4: SAF C# (UnityEngine yok). Kanit izgarasindan (OdaKanit.cs) duvar, esya kutusu,
    // zemin ve tavan uretir. Her nesne once "aday", yeterli kanit birikince "kesin" olur; kesin nesne yalnizca
    // destegi bosalirsa silinir. Koordinatlar Unity oturum uzayi (metre, y yukari). Yatay duzlem = XZ.
    // =====================================================================================================

    /// <summary>Girdi duzlemi. tur: 0 = yatay (yukari bakan), 1 = yatay (asagi bakan), 2 = dikey. sinir = dunya x,y,z ucluleri.</summary>
    public class OdaDuzlem
    {
        public string id; public int tur; public float[] sinir; public float nx, ny, nz;
    }

    public class OdaDuvar
    {
        public int id;
        public bool kesin; public float ilerleme; public float kesinZaman;
        public float ax, az, bx, bz;      // cizgi uclari (XZ)
        public bool aKose, bKose;         // uc baska bir duvarla birlesti
        public float nx, nz;              // oda icine bakan birim normal
        public float yukseklik;           // zemin -> tavan
        public float Uzunluk { get { float dx = bx - ax, dz = bz - az; return (float)Math.Sqrt(dx * dx + dz * dz); } }
        public OdaDuvar Kopya() { return (OdaDuvar)MemberwiseClone(); }
    }

    public class OdaKutu
    {
        public int id;
        public bool kesin; public float ilerleme; public float kesinZaman;
        public float cx, cy, cz;          // merkez (cy = zemin + sy/2)
        public float sx, sy, sz;          // sx: uzun kenar (yerel x), sz: kisa kenar (yerel z), sy: yukseklik
        public float aci;                 // uzun eksenin XZ duzleminde atan2(dz, dx) acisi, derece, [0,180)
        public int tur = -1;              // esya turu (OdaEtiket.EsyaAd indeksi), -1 = bilinmiyor
        public string TurAdi { get { return tur >= 0 && tur < OdaEtiket.EsyaAd.Length ? OdaEtiket.EsyaAd[tur] : "Eşya"; } }
        /// <summary>Unity Quaternion.Euler(0, yaw, 0) icin Y acisi (yerel x = uzun kenar).</summary>
        public float YawDeg { get { float y = -aci; while (y < 0f) y += 360f; return y; } }
        public OdaKutu Kopya() { return (OdaKutu)MemberwiseClone(); }

        /// <summary>XZ duzleminde 4 kose (x,z ciftleri; halka sirasi).</summary>
        public void Koseler(float[] cikis)
        {
            double r = aci * Math.PI / 180.0, c = Math.Cos(r), s = Math.Sin(r);
            float ux = (float)c, uz = (float)s, vx = (float)-s, vz = (float)c;
            float hx = sx * 0.5f, hz = sz * 0.5f;
            cikis[0] = cx - ux * hx - vx * hz; cikis[1] = cz - uz * hx - vz * hz;
            cikis[2] = cx + ux * hx - vx * hz; cikis[3] = cz + uz * hx - vz * hz;
            cikis[4] = cx + ux * hx + vx * hz; cikis[5] = cz + uz * hx + vz * hz;
            cikis[6] = cx - ux * hx + vx * hz; cikis[7] = cz - uz * hx + vz * hz;
        }
    }

    public class OdaSonuc
    {
        public bool zeminVar; public float zeminY, tavanY; public bool tavanGercek;
        public bool TavanTahmin { get { return !tavanGercek; } }
        public List<OdaDuvar> duvarlar = new List<OdaDuvar>();   // aday + kesin
        public List<OdaKutu> kutular = new List<OdaKutu>();      // aday + kesin
        public float[] zeminPoligon = new float[0];   // x,z ciftleri
        public bool zeminKapali;                      // duvarlar kapali bir dongu olusturuyor
        public float alan;                            // m2 (kesin duvarlardan)
        public bool yonVar; public float baskinAci;   // Manhattan ana yonu (derece, [0,90))
        public int voksel, surum; public float zaman;
        public HashSet<long> zeminSerbest = new HashSet<long>();   // bos zemin hucreleri (5 cm), Anah2
        public HashSet<long> zeminTaranan = new HashSet<long>();   // derinlikte zemin gorulen hucreler (5 cm), Anah2: ekranda boyanir
        public float taramaAlani;                     // taranan zemin (m2)
        public bool etiketli;                         // SegFormer etiketleri kullanildi
        public bool[] yonDuvar = new bool[16];        // kullanicidan 16 yonde (yaw dilimi) duvar bulundu mu
        public float[] yonZeminEksik = new float[16]; // kullanicidan 16 yonde 0,7-2,5 m arasi taranmamis zemin orani (0..1)
        public float zeminKapsama = -1f;              // oda poligonu icinde (esya/duvar disi) taranmis zemin orani; poligon yoksa -1
        public float acikAlan;                        // kesfedilmis bos zemin alani (m2)
        public float[] sinir = new float[0];          // kesfedilmemis bolgeye komsu bos zemin hucreleri (x,z, 25 cm)
        public float duvarKapsamaKesin, duvarKapsamaTum;   // kullanicidan 16 yonde duvar bulunan yonlerin orani
        public bool dongu;
        public float kameraX, kameraZ;

        public int KesinDuvar { get { int n = 0; foreach (var d in duvarlar) if (d.kesin) n++; return n; } }
        public int KesinKutu { get { int n = 0; foreach (var k in kutular) if (k.kesin) n++; return n; } }

        public static long Anah2(int ix, int iz) { return ((long)(ix + 1048576) << 21) | (long)(iz + 1048576); }

        /// <summary>Zemin noktasi: bos zemin kaniti var ve kutu/duvar (kesin veya aday) en az marj uzakta. Model bos ise true.</summary>
        public bool AcikZeminMi(float x, float z, float marj = 0.25f)
        {
            if (!zeminVar) return true;
            if (voksel > 0 && !zeminSerbest.Contains(Anah2(KanitIzgarasi.Ind(x), KanitIzgarasi.Ind(z)))) return false;
            foreach (var k in kutular) if (KutuMesafesi(k, x, z) < marj) return false;
            foreach (var d in duvarlar) if (DuvarMesafesi(d, x, z) < marj) return false;
            return true;
        }

        public static float KutuMesafesi(OdaKutu k, float x, float z)
        {
            double r = k.aci * Math.PI / 180.0, c = Math.Cos(r), s = Math.Sin(r);
            double dx = x - k.cx, dz = z - k.cz;
            double u = dx * c + dz * s, v = -dx * s + dz * c;
            double qu = Math.Max(Math.Abs(u) - k.sx * 0.5, 0.0), qv = Math.Max(Math.Abs(v) - k.sz * 0.5, 0.0);
            return (float)Math.Sqrt(qu * qu + qv * qv);
        }

        public static float DuvarMesafesi(OdaDuvar d, float x, float z) { return (float)OdaGeo.ParcaMesafe(d.ax, d.az, d.bx, d.bz, x, z); }
    }

    public class OdaAyar
    {
        public float Aralik = 1.0f;                     // yapi guncelleme araligi (s), en cok 1 Hz
        public float EsyaAlt = 0.08f, EsyaUst = 2.0f;
        public float TavanMin = 1.9f, TavanVarsayilan = 2.6f;
        public float DuvarSpan = 1.2f;                  // duvar kolonu icin en az dikey yayilim
        public float MinDuvarUzunluk = 0.6f;
        public float DuvarOnaySure = 2.5f; public int DuvarOnayBin = 3; public float BuyukPlanAlan = 2.0f;
        public float KutuOnaySure = 3.0f; public int KutuOnayArdisik = 3; public int KutuOnayBin = 3; public float KutuIoU = 0.6f;
        public float KutuMinAlan = 0.05f, KutuMinYukseklik = 0.2f, KutuMinKenar = 0.15f;
        public float DuvarBandi = 0.12f;
        public float ManhattanDuvar = 12f, ManhattanKutu = 15f;
        public float KoseMesafe = 0.50f;
        public float KutuSilme = 0.60f;
        public float YumusakKutu = 0.5f;
    }

    public partial class OdaModeli
    {
        public readonly OdaAyar ayar;
        public readonly KanitIzgarasi izgara;
        int sonDuvarId, sonKutuId, surum;
        float sonZaman = float.NaN;
        readonly Random rnd = new Random(7);

        // pasi alanlari (Guncelle icinde)
        int zk, ck;               // zemin / tavan katman indeksleri
        float zy, ty;             // zemin ve tavan y (gosterim)
        bool tavanGercek;

        public OdaModeli(OdaAyar a = null, KanitIzgarasi g = null) { ayar = a ?? new OdaAyar(); izgara = g ?? new KanitIzgarasi(); }

        public void Sifirla()
        {
            sonDuvarId = 0; sonKutuId = 0; surum = 0; sonZaman = float.NaN;
            duvarDurum.Clear(); kutuDurum.Clear(); izgara.Sifirla(); BantSifirla(); ornekler.Clear(); oncekiMasa.Clear(); zeminBilinen = false;
        }

        public void Isle(DerinlikKaresi f) { izgara.Isle(f); IzEkle(f.px, f.pz); GozlemleriCikar(f); }

        class Hucre
        {
            public int minIy = int.MaxValue, maxIy = int.MinValue, kutuUst = int.MinValue, n, bin;
            public ushort son, ilk = ushort.MaxValue;
        }

        // ------------------------------------------------------------------------------------------------
        public OdaSonuc Guncelle(IList<OdaDuzlem> planlar, float zaman)
        {
            var s = new OdaSonuc { surum = ++surum, zaman = zaman, voksel = izgara.VokselSayisi, kameraX = izgara.SonKameraX, kameraZ = izgara.SonKameraZ };
            float dt = float.IsNaN(sonZaman) ? 1f : Math.Max(0.2f, Math.Min(3f, zaman - sonZaman));
            sonZaman = zaman;

            // ---------- zemin (en alcak buyuk yatay yukari bakan duzlem) ----------
            float zy0 = float.MaxValue; bool zv = false;
            foreach (var p in planlar)
                if (p.tur == 0 && p.sinir != null && p.sinir.Length >= 9 && OdaGeo.PolyAlan(p.sinir) >= 0.4f) { zy0 = Math.Min(zy0, OrtY(p.sinir)); zv = true; }
            if (!zv)
                foreach (var p in planlar)
                    if (p.tur == 0 && p.sinir != null && p.sinir.Length >= 9) { zy0 = Math.Min(zy0, OrtY(p.sinir)); zv = true; }
            if (!zv) return s;
            // etiketli zemin noktalari varsa zemin yuksekligi onlarin tepesidir (ARCore'un en alcak duzlemi kayabilir)
            if (izgara.EtiketNokta > 20000)
            {
                float ze = HistTepe(izgara.ZeminYHist, zy0 - 0.4f, zy0 + 0.4f, 1500);
                if (!float.IsNaN(ze)) { ZeminFark = ze - zy0; zy0 = ze; }
            }
            s.zeminVar = true; s.zeminY = zy = zy0;
            int iy0 = KanitIzgarasi.Ind(zy0);

            // ---------- gecis A: zemin tepesi + ust katman histogrami ----------
            var zh = new int[17];
            var ustSay = new Dictionary<int, int>(); var ustBb = new Dictionary<int, int[]>();
            int kap = izgara.Kapasite;
            int ustEsik = iy0 + (int)Math.Round(ayar.TavanMin / KanitIzgarasi.V) - 2;
            for (int i = 0; i < kap; i++)
            {
                if (!izgara.Var(i) || izgara.LDeger(i) <= KanitIzgarasi.DoluEsik || !izgara.Dolu(i)) continue;
                KanitIzgarasi.Coz(izgara.AnahtarAl(i), out int ix, out int iy, out int iz);
                if (Math.Abs(iy - iy0) <= 8) zh[iy - iy0 + 8]++;
                if (iy >= ustEsik)
                {
                    ustSay.TryGetValue(iy, out int c); ustSay[iy] = c + 1;
                    if (!ustBb.TryGetValue(iy, out var bb)) { bb = new[] { ix, ix, iz, iz }; ustBb[iy] = bb; }
                    else { if (ix < bb[0]) bb[0] = ix; if (ix > bb[1]) bb[1] = ix; if (iz < bb[2]) bb[2] = iz; if (iz > bb[3]) bb[3] = iz; }
                }
            }
            int zEn = 0, zEnSay = 0; for (int i = 0; i < 17; i++) if (zh[i] > zEnSay) { zEnSay = zh[i]; zEn = i; }
            zk = zEnSay >= 100 ? iy0 + zEn - 8 : iy0;

            // ---------- tavan ----------
            float tp = float.NaN;
            foreach (var p in planlar)
                if (p.tur == 1 && p.sinir != null && p.sinir.Length >= 9 && OdaGeo.PolyAlan(p.sinir) >= 0.5f)
                {
                    float y = OrtY(p.sinir);
                    if (y - zy0 > 1.8f && (float.IsNaN(tp) || y > tp)) tp = y;
                }
            if (!float.IsNaN(tp)) { ty = tp; tavanGercek = true; ck = zk + (int)Math.Round((tp - zy0) / KanitIzgarasi.V); }
            else
            {
                tavanGercek = false; ty = zy0 + ayar.TavanVarsayilan; ck = zk + (int)Math.Round(ayar.TavanVarsayilan / KanitIzgarasi.V);
                int en = int.MinValue, enS = 0, topl = 0;
                foreach (var kv in ustSay) { topl += kv.Value; if (kv.Value > enS) { enS = kv.Value; en = kv.Key; } }
                if (enS >= 600)
                {
                    int p3 = 0; for (int d = -1; d <= 1; d++) { ustSay.TryGetValue(en + d, out int c); p3 += c; }
                    int katman = Math.Max(4, en + 10 - ustEsik);
                    double digerOrt = (topl - p3) / (double)Math.Max(1, katman - 3);
                    var bb = ustBb[en];
                    double yanA = (bb[1] - bb[0] + 1) * KanitIzgarasi.V, yanB = (bb[3] - bb[2] + 1) * KanitIzgarasi.V;
                    if (p3 >= 2.5 * digerOrt && yanA >= 1.0 && yanB >= 1.0)
                    {
                        ck = en; ty = zy0 + (en - zk) * KanitIzgarasi.V; tavanGercek = true;
                    }
                }
            }
            s.tavanY = ty; s.tavanGercek = tavanGercek;

            // ---------- gecis B: hucre haritasi + zemin dilimi ----------
            var hucre = new Dictionary<long, Hucre>();
            var dilim = new Dictionary<long, byte>();
            int ustSinir = ck - 2, kutuUstKat = (int)Math.Round(ayar.EsyaUst / KanitIzgarasi.V), altKat = (int)Math.Round(ayar.EsyaAlt / KanitIzgarasi.V);
            if (altKat < 2) altKat = 2;
            for (int i = 0; i < kap; i++)
            {
                if (!izgara.Var(i)) continue;
                float lv = izgara.LDeger(i);
                bool bos = lv < KanitIzgarasi.BosEsik, dolu = !bos && lv > KanitIzgarasi.DoluEsik && izgara.Dolu(i);
                if (!bos && !dolu) continue;
                KanitIzgarasi.Coz(izgara.AnahtarAl(i), out int ix, out int iy, out int iz);
                int dl = iy - zk;
                if (dl < 2) continue;
                long hk = OdaSonuc.Anah2(ix, iz);
                if (dl <= 8)
                {
                    dilim.TryGetValue(hk, out byte b0); dilim[hk] = (byte)(b0 | (dolu ? 2 : 1));
                }
                if (!dolu || iy > ustSinir) continue;
                if (!hucre.TryGetValue(hk, out var h)) { h = new Hucre(); hucre[hk] = h; }
                if (iy < h.minIy) h.minIy = iy; if (iy > h.maxIy) h.maxIy = iy;
                if (dl >= altKat && dl <= kutuUstKat && iy > h.kutuUst) h.kutuUst = iy;
                h.n++; h.bin |= izgara.BinMaske(i);
                ushort sn = (ushort)Math.Round(izgara.SonGorulme(i) * 10f); if (sn > h.son) h.son = sn;
                float il = izgara.IlkGorulme(i); if (il < float.MaxValue) { ushort it = (ushort)Math.Round(il * 10f); if (it < h.ilk) h.ilk = it; }
            }

            // ---------- bos zemin, kesif siniri ----------
            ZeminKesif(s, dilim);

            // ---------- duvarlar (bant yontemi; tum guven degerleri), tavan yedegi, taranan zemin, kesif siniri ----------
            BantGuncelle(s, zy0, zaman);

            // ---------- kutular: gercek veride gurultu yuzunden rastgele kutu cikiyordu; duvar/zemin/tavan oturana kadar kapali ----------
            if (KutularAcik) KutulariGuncelle(s, hucre, zaman, dt);
            else if (s.etiketli) EsyaGuncelle(s, zy0, planlar, zaman);   // SegFormer: yalniz esya etiketli noktalardan, turuyle

            // ---------- zemin poligonu, kapsama ----------
            ZeminPoligonu(s);
            Kapsama(s);
            return s;
        }

        static float OrtY(float[] sinir) { double t = 0; int m = sinir.Length / 3; for (int i = 0; i < m; i++) t += sinir[i * 3 + 1]; return (float)(t / m); }

        // ================================================================================== bos zemin + kesif siniri
        void ZeminKesif(OdaSonuc s, Dictionary<long, byte> dilim)
        {
            var kaba = new Dictionary<long, int[]>();   // 25 cm hucre: [bos, dolu]
            int bosSay = 0;
            foreach (var kv in dilim)
            {
                int ix = (int)((kv.Key >> 21) & 0x1FFFFF) - 1048576, iz = (int)(kv.Key & 0x1FFFFF) - 1048576;
                bool bos = (kv.Value & 1) != 0, dolu = (kv.Value & 2) != 0;
                if (bos && !dolu) { s.zeminSerbest.Add(kv.Key); bosSay++; }
                long kk = OdaSonuc.Anah2((int)Math.Floor(ix / 5.0), (int)Math.Floor(iz / 5.0));
                if (!kaba.TryGetValue(kk, out var a)) { a = new int[2]; kaba[kk] = a; }
                if (dolu) a[1]++; else if (bos) a[0]++;
            }
            s.acikAlan = bosSay * KanitIzgarasi.V * KanitIzgarasi.V;
            var sin = new List<float>();
            foreach (var kv in kaba)
            {
                if (kv.Value[0] < 6 || kv.Value[1] > 0) continue;
                int cx = (int)((kv.Key >> 21) & 0x1FFFFF) - 1048576, cz = (int)(kv.Key & 0x1FFFFF) - 1048576;
                bool sinirda = !kaba.ContainsKey(OdaSonuc.Anah2(cx + 1, cz)) || !kaba.ContainsKey(OdaSonuc.Anah2(cx - 1, cz)) || !kaba.ContainsKey(OdaSonuc.Anah2(cx, cz + 1)) || !kaba.ContainsKey(OdaSonuc.Anah2(cx, cz - 1));
                if (!sinirda) continue;
                sin.Add((cx + 0.5f) * 0.25f); sin.Add((cz + 0.5f) * 0.25f);
            }
            s.sinir = sin.ToArray();
        }

        // ================================================================================== zemin poligonu + kapsama
        void ZeminPoligonu(OdaSonuc s)
        {
            var kd = new List<OdaDuvar>(); foreach (var d in s.duvarlar) if (d.kesin) kd.Add(d);
            s.zeminPoligon = new float[0]; s.zeminKapali = false; s.alan = 0;
            if (kd.Count >= 3)
            {
                var halka = Dongu(kd);
                if (halka != null && halka.Count >= 3)
                {
                    s.zeminKapali = true; s.dongu = true;
                    var pl = new float[halka.Count * 2]; for (int i = 0; i < halka.Count; i++) { pl[i * 2] = (float)halka[i][0]; pl[i * 2 + 1] = (float)halka[i][1]; }
                    s.zeminPoligon = pl; s.alan = (float)Math.Abs(OdaGeo.Alan2D(halka)); return;
                }
            }
            var pts = new List<double[]>();
            if (kd.Count >= 2) foreach (var d in kd) { pts.Add(new double[] { d.ax, d.az }); pts.Add(new double[] { d.bx, d.bz }); }
            else if (s.sinir.Length == 0) return;
            if (pts.Count < 3) return;
            var h = OdaGeo.Zarf(pts);
            if (h.Count < 3) return;
            var p2 = new float[h.Count * 2]; for (int i = 0; i < h.Count; i++) { p2[i * 2] = (float)h[i][0]; p2[i * 2 + 1] = (float)h[i][1]; }
            s.zeminPoligon = p2; s.alan = (float)Math.Abs(OdaGeo.Alan2D(h));
        }

        void Kapsama(OdaSonuc s)
        {
            float ox = s.kameraX, oz = s.kameraZ; int kk = 0, kt = 0;
            for (int b = 0; b < 16; b++)
            {
                double a = (b + 0.5) * Math.PI / 8, dx = Math.Sin(a), dz = Math.Cos(a);
                bool k = false, t = false;
                foreach (var d in s.duvarlar)
                    if (OdaGeo.IsinParca(ox, oz, dx, dz, d.ax, d.az, d.bx, d.bz) > 0) { t = true; if (d.kesin) k = true; }
                if (k) kk++; if (t) kt++;
                s.yonDuvar[b] = t;
            }
            s.duvarKapsamaKesin = kk / 16f; s.duvarKapsamaTum = kt / 16f;
            ZeminKapsama(s);
        }

        /// <summary>Kat plani icin zeminin nerelerinin taranmadigi: kullanicinin cevresinde 16 yonde ve oda poligonunda.</summary>
        void ZeminKapsama(OdaSonuc s)
        {
            const float H = 0.15f;
            var kaba = new HashSet<long>();
            foreach (long k in s.zeminTaranan)
            {
                int ix = (int)((k >> 21) & 0x1FFFFF) - 1048576, iz = (int)(k & 0x1FFFFF) - 1048576;
                kaba.Add(OdaSonuc.Anah2((int)Math.Floor(ix * KanitIzgarasi.V / H), (int)Math.Floor(iz * KanitIzgarasi.V / H)));
            }
            bool poly = s.zeminKapali && s.zeminPoligon.Length >= 6;
            Func<float, float, bool> bilinen = (x, z) =>
            {
                int cx = (int)Math.Floor(x / H), cz = (int)Math.Floor(z / H);
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) if (kaba.Contains(OdaSonuc.Anah2(cx + dx, cz + dz))) return true;
                foreach (var k in s.kutular) if (k.kesin && OdaSonuc.KutuMesafesi(k, x, z) < 0.1f) return true;
                foreach (var d in s.duvarlar) if (d.kesin && OdaSonuc.DuvarMesafesi(d, x, z) < 0.25f) return true;
                return false;
            };
            Func<float, float, bool> odada = (x, z) =>
            {
                if (poly) return IcindeMi(s.zeminPoligon, x, z);
                foreach (var d in s.duvarlar) { if (!d.kesin) continue; double t = OdaGeo.IsinParca(s.kameraX, s.kameraZ, x - s.kameraX, z - s.kameraZ, d.ax, d.az, d.bx, d.bz); if (t > 0 && t < 1) return false; }
                return true;
            };
            float ox = s.kameraX, oz = s.kameraZ;
            for (int b = 0; b < 16; b++)
            {
                double a = (b + 0.5) * Math.PI / 8, dx = Math.Sin(a), dz = Math.Cos(a);
                int n = 0, eksik = 0;
                for (float r = 0.7f; r <= 2.5f; r += 0.3f)
                {
                    float x = ox + (float)(dx * r), z = oz + (float)(dz * r);
                    if (!odada(x, z)) break;
                    n++; if (!bilinen(x, z)) eksik++;
                }
                s.yonZeminEksik[b] = n == 0 ? 0f : eksik / (float)n;
            }
            if (poly)
            {
                float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
                for (int i = 0; i + 1 < s.zeminPoligon.Length; i += 2) { x0 = Math.Min(x0, s.zeminPoligon[i]); x1 = Math.Max(x1, s.zeminPoligon[i]); z0 = Math.Min(z0, s.zeminPoligon[i + 1]); z1 = Math.Max(z1, s.zeminPoligon[i + 1]); }
                int n = 0, b2 = 0;
                for (float z = z0 + 0.15f; z < z1; z += 0.3f)
                    for (float x = x0 + 0.15f; x < x1; x += 0.3f)
                    {
                        if (!IcindeMi(s.zeminPoligon, x, z)) continue;
                        n++; if (bilinen(x, z)) b2++;
                    }
                s.zeminKapsama = n > 0 ? b2 / (float)n : -1f;
            }
        }

        static bool IcindeMi(float[] p, float x, float z)
        {
            bool ic = false; int m = p.Length / 2;
            for (int i = 0, j = m - 1; i < m; j = i++)
            {
                float xi = p[i * 2], zi = p[i * 2 + 1], xj = p[j * 2], zj = p[j * 2 + 1];
                if ((zi > z) != (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi) ic = !ic;
            }
            return ic;
        }
    }
}
