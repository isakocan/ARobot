using System;
using System.Collections.Generic;

namespace ArTarama
{
    // =====================================================================================================
    // Oda modeli cekirdegi: SAF C# (UnityEngine yok). Duzlemlerden duvar, nokta bulutundan esya kutusu uretir.
    // Birim testi ve cevrimdisi tekrar (Assets/Editor/OdaTest*.cs) ayni kodu kullanir.
    // Koordinatlar Unity oturum uzayi (metre, y yukari). Yatay duzlem = XZ.
    // =====================================================================================================

    /// <summary>Girdi duzlemi. tur: 0 = yatay (yukari bakan), 1 = yatay (asagi bakan), 2 = dikey. sinir = dunya x,y,z ucluleri.</summary>
    public class OdaDuzlem
    {
        public string id; public int tur; public float[] sinir; public float nx, ny, nz;
    }

    public class OdaDuvar
    {
        public int id;
        public float ax, az, bx, bz;      // cizgi uclari (XZ)
        public float nx, nz;              // oda icine bakan birim normal
        public float yukseklik;           // zemin -> tavan
        public float Uzunluk { get { float dx = bx - ax, dz = bz - az; return (float)Math.Sqrt(dx * dx + dz * dz); } }
        public OdaDuvar Kopya() { return (OdaDuvar)MemberwiseClone(); }
    }

    public class OdaKutu
    {
        public int id;
        public float cx, cy, cz;          // merkez (cy = zemin + sy/2)
        public float sx, sy, sz;          // sx: uzun kenar (yerel x), sz: kisa kenar (yerel z), sy: yukseklik
        public float aci;                 // uzun eksenin XZ duzleminde atan2(dz, dx) acisi, derece, [0,180)
        /// <summary>Unity Quaternion.Euler(0, yaw, 0) icin Y acisi (yerel x = uzun kenar).</summary>
        public float YawDeg { get { float y = -aci; while (y < 0f) y += 360f; return y; } }
        public OdaKutu Kopya() { return (OdaKutu)MemberwiseClone(); }

        /// <summary>XZ duzleminde 4 kose (x,z ciftleri, saat yonu degil rastgele sirali halka).</summary>
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
        public List<OdaDuvar> duvarlar = new List<OdaDuvar>();
        public List<OdaKutu> kutular = new List<OdaKutu>();
        public float[] zeminPoligon = new float[0];   // x,z ciftleri (dis bukey zarf)
        public bool yonVar; public float baskinAci;    // duvarlarin baskin yonu (derece, [0,90))
        public int kullanilanNokta;
        public int surum;

        /// <summary>Nokta, kutu kenarlarindan ve duvarlardan en az marj metre uzakta mi (zeminde bos yer)?</summary>
        public bool AcikZeminMi(float x, float z, float marj = 0.25f)
        {
            foreach (var k in kutular)
                if (KutuMesafesi(k, x, z) < marj) return false;
            foreach (var d in duvarlar)
                if (DuvarMesafesi(d, x, z) < marj) return false;
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

        public static float DuvarMesafesi(OdaDuvar d, float x, float z)
        {
            double abx = d.bx - d.ax, abz = d.bz - d.az, l2 = abx * abx + abz * abz;
            double t = l2 > 1e-9 ? ((x - d.ax) * abx + (z - d.az) * abz) / l2 : 0.0;
            t = Math.Max(0.0, Math.Min(1.0, t));
            double px = d.ax + abx * t, pz = d.az + abz * t;
            return (float)Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
        }
    }

    public class OdaAyar
    {
        public float Hucre = 0.04f;   // 5 cm yerine 4 cm: 10 cm arayla duran iki yuz hizalamadan bagimsiz ayri hucrelere duser
        public int MinNokta = 3;
        public float MinKutuAlan = 0.04f;        // m2, hucre sayisindan
        public float MinKutuYukseklik = 0.15f;
        public float MinKutuKenar = 0.10f;       // daha ince bilesenler (duvar kalintisi) atilir
        public float DuvarDisi = 0.10f;          // duvar cizgisine bu kadar yakin / arkasindaki noktalar sayilmaz
        public float EsyaAlt = 0.08f, EsyaUst = 2.0f;
        public int KayipSiniri = 4, OnayGerek = 2;
        public float AciKilidi = 15f;            // kutu baskin duvar yonune bu kadar yakinsa hizalanir
        public float MinDuvarUzunluk = 0.5f;
        public float BirlesAci = 10f, BirlesOfset = 0.08f, BirlesBosluk = 0.30f;
        public float KoseMesafe = 0.40f;
        public float KutuSilme = 0.5f;           // eslesen kutuyu yeni olcume cekme orani
    }

    public class OdaModeli
    {
        public readonly OdaAyar ayar;
        int sonDuvarId, sonKutuId, surum;
        List<OdaDuvar> oncekiDuvar = new List<OdaDuvar>();
        readonly List<KutuDurum> kutular = new List<KutuDurum>();

        class KutuDurum { public OdaKutu k; public int onay, kayip; }

        public OdaModeli(OdaAyar a = null) { ayar = a ?? new OdaAyar(); }

        public void Sifirla() { sonDuvarId = 0; sonKutuId = 0; surum = 0; oncekiDuvar = new List<OdaDuvar>(); kutular.Clear(); }

        // ------------------------------------------------------------------------------------------------
        public OdaSonuc Guncelle(IList<OdaDuzlem> planlar, float[] nk, int nSayi)
        {
            var s = new OdaSonuc { surum = ++surum, kullanilanNokta = nSayi };

            // ---------- zemin ----------
            float zy = float.MaxValue; bool zv = false;
            foreach (var p in planlar)
                if (p.tur == 0 && p.sinir != null && p.sinir.Length >= 9 && PolyAlan(p.sinir) >= 0.4f) { zy = Math.Min(zy, OrtY(p.sinir)); zv = true; }
            if (!zv)
                foreach (var p in planlar)
                    if (p.tur == 0 && p.sinir != null && p.sinir.Length >= 9) { zy = Math.Min(zy, OrtY(p.sinir)); zv = true; }
            if (!zv) return s;
            s.zeminVar = true; s.zeminY = zy;

            // ---------- tavan ----------
            float ty = float.NaN;
            foreach (var p in planlar)
                if (p.tur == 1 && p.sinir != null && p.sinir.Length >= 9 && PolyAlan(p.sinir) >= 0.5f)
                {
                    float y = OrtY(p.sinir);
                    if (y - zy > 1.8f && (float.IsNaN(ty) || y > ty)) ty = y;
                }
            if (!float.IsNaN(ty)) { s.tavanY = ty; s.tavanGercek = true; }
            else
            {
                var hl = new List<float>();
                for (int i = 0; i < nSayi; i++) { float h = nk[i * 3 + 1] - zy; if (h > 0f && h < 4.5f) hl.Add(h); }
                float yuk = 2.6f;
                if (hl.Count >= 50) { hl.Sort(); yuk = Math.Max(2.2f, Math.Min(3.5f, hl[(int)(0.95f * (hl.Count - 1))])); }
                s.tavanY = zy + yuk;
            }
            float oda = s.tavanY - zy;

            // ---------- oda merkezi (duvar normallerini ice cevirmek icin) ----------
            double rx = 0, rz = 0; int rn = 0;
            foreach (var p in planlar)
                if (p.tur == 0 && p.sinir != null && Math.Abs(OrtY(p.sinir) - zy) < 0.2f)
                    for (int i = 0; i + 2 < p.sinir.Length; i += 3) { rx += p.sinir[i]; rz += p.sinir[i + 2]; rn++; }

            // ---------- duvarlar ----------
            var adaylar = new List<Aday>();
            foreach (var p in planlar)
            {
                if (p.tur != 2 || p.sinir == null || p.sinir.Length < 9) continue;
                double nl = Math.Sqrt((double)p.nx * p.nx + (double)p.nz * p.nz);
                if (nl < 0.5) continue;
                double nx = p.nx / nl, nz = p.nz / nl, dx = -nz, dz = nx;
                double tmin = double.MaxValue, tmax = double.MinValue, c = 0, ymin = double.MaxValue, ymax = double.MinValue; int m = p.sinir.Length / 3;
                for (int i = 0; i < m; i++)
                {
                    double x = p.sinir[i * 3], y = p.sinir[i * 3 + 1], z = p.sinir[i * 3 + 2];
                    double t = x * dx + z * dz; tmin = Math.Min(tmin, t); tmax = Math.Max(tmax, t);
                    c += x * nx + z * nz; ymin = Math.Min(ymin, y); ymax = Math.Max(ymax, y);
                }
                c /= m;
                if (ymax - zy < 0.4 || ymin - zy > 1.5 || tmax - tmin < 0.3) continue;
                var a = new Aday { nx = nx, nz = nz };
                a.Ayarla(nx, nz, c, tmin, tmax);
                a.w = tmax - tmin;
                adaylar.Add(a);
            }
            // birlestir
            bool degisti = true;
            while (degisti)
            {
                degisti = false;
                for (int i = 0; i < adaylar.Count && !degisti; i++)
                    for (int j = i + 1; j < adaylar.Count && !degisti; j++)
                    {
                        var b = Birlestir(adaylar[i], adaylar[j]);
                        if (b == null) continue;
                        adaylar.RemoveAt(j); adaylar[i] = b; degisti = true;
                    }
            }
            var duvarlar = new List<OdaDuvar>();
            foreach (var a in adaylar)
            {
                double l = Math.Sqrt((a.bx - a.ax) * (a.bx - a.ax) + (a.bz - a.az) * (a.bz - a.az));
                if (l < ayar.MinDuvarUzunluk) continue;
                double mx = (a.ax + a.bx) * 0.5, mz = (a.az + a.bz) * 0.5;
                double nx = a.nx, nz = a.nz;
                if (rn > 0 && (rx / rn - mx) * nx + (rz / rn - mz) * nz < 0) { nx = -nx; nz = -nz; double tx = a.ax, tz = a.az; a.ax = a.bx; a.az = a.bz; a.bx = tx; a.bz = tz; }
                duvarlar.Add(new OdaDuvar { ax = (float)a.ax, az = (float)a.az, bx = (float)a.bx, bz = (float)a.bz, nx = (float)nx, nz = (float)nz, yukseklik = oda });
            }
            KoseleriBirlestir(duvarlar);
            KimlikEsle(duvarlar);
            s.duvarlar = duvarlar; oncekiDuvar = new List<OdaDuvar>(); foreach (var d in duvarlar) oncekiDuvar.Add(d.Kopya());

            // baskin yon (en uzun duvar)
            if (duvarlar.Count > 0)
            {
                OdaDuvar en = duvarlar[0]; foreach (var d in duvarlar) if (d.Uzunluk > en.Uzunluk) en = d;
                double a = Math.Atan2(en.bz - en.az, en.bx - en.ax) * 180.0 / Math.PI;
                a %= 90.0; if (a < 0) a += 90.0;
                s.yonVar = true; s.baskinAci = (float)a;
            }

            // ---------- zemin poligonu ----------
            var zp = new List<double[]>();
            if (duvarlar.Count >= 3) foreach (var d in duvarlar) { zp.Add(new double[] { d.ax, d.az }); zp.Add(new double[] { d.bx, d.bz }); }
            else
                foreach (var p in planlar)
                    if (p.tur == 0 && p.sinir != null && Math.Abs(OrtY(p.sinir) - zy) < 0.2f)
                        for (int i = 0; i + 2 < p.sinir.Length; i += 3) zp.Add(new double[] { p.sinir[i], p.sinir[i + 2] });
            var hull = Zarf(zp);
            s.zeminPoligon = new float[hull.Count * 2];
            for (int i = 0; i < hull.Count; i++) { s.zeminPoligon[i * 2] = (float)hull[i][0]; s.zeminPoligon[i * 2 + 1] = (float)hull[i][1]; }

            // ---------- esya kutulari ----------
            KutulariGuncelle(s, nk, nSayi);
            foreach (var kd in kutular) if (kd.onay >= ayar.OnayGerek) s.kutular.Add(kd.k.Kopya());
            return s;
        }

        // ================================================================================== duvar yardimcilari
        class Aday
        {
            public double nx, nz, ax, az, bx, bz, w;
            /// <summary>n.x = c dogrusu uzerinde t araligi [t0,t1] (d = (-nz, nx)).</summary>
            public void Ayarla(double nx, double nz, double c, double t0, double t1)
            {
                this.nx = nx; this.nz = nz; double dx = -nz, dz = nx;
                ax = nx * c + dx * t0; az = nz * c + dz * t0; bx = nx * c + dx * t1; bz = nz * c + dz * t1;
            }
        }

        Aday Birlestir(Aday a, Aday b)
        {
            double dot = a.nx * b.nx + a.nz * b.nz;
            if (Math.Abs(dot) < Math.Cos(ayar.BirlesAci * Math.PI / 180.0)) return null;
            double sg = dot < 0 ? -1 : 1, bnx = b.nx * sg, bnz = b.nz * sg;
            // dik ofset: her birinin orta noktasinin digerinin dogrusuna uzakligi
            double ma_x = (a.ax + a.bx) * 0.5, ma_z = (a.az + a.bz) * 0.5, mb_x = (b.ax + b.bx) * 0.5, mb_z = (b.az + b.bz) * 0.5;
            double o1 = Math.Abs((mb_x - ma_x) * a.nx + (mb_z - ma_z) * a.nz);
            double o2 = Math.Abs((ma_x - mb_x) * bnx + (ma_z - mb_z) * bnz);
            if (Math.Max(o1, o2) > ayar.BirlesOfset) return null;
            // aralik (a'nin ekseninde)
            double adx = -a.nz, adz = a.nx;
            double ta0 = a.ax * adx + a.az * adz, ta1 = a.bx * adx + a.bz * adz; if (ta0 > ta1) { double t = ta0; ta0 = ta1; ta1 = t; }
            double tb0 = b.ax * adx + b.az * adz, tb1 = b.bx * adx + b.bz * adz; if (tb0 > tb1) { double t = tb0; tb0 = tb1; tb1 = t; }
            double bosluk = Math.Max(Math.Max(tb0 - ta1, ta0 - tb1), 0.0);
            if (bosluk > ayar.BirlesBosluk) return null;
            double w = a.w + b.w;
            double nx = a.nx * a.w + bnx * b.w, nz = a.nz * a.w + bnz * b.w, nl = Math.Sqrt(nx * nx + nz * nz); nx /= nl; nz /= nl;
            double c = (a.w * (ma_x * nx + ma_z * nz) + b.w * (mb_x * nx + mb_z * nz)) / w;
            double dx = -nz, dz = nx;
            double t0 = double.MaxValue, t1 = double.MinValue;
            foreach (double t in new[] { a.ax * dx + a.az * dz, a.bx * dx + a.bz * dz, b.ax * dx + b.az * dz, b.bx * dx + b.bz * dz }) { t0 = Math.Min(t0, t); t1 = Math.Max(t1, t); }
            var r = new Aday { w = w };
            r.Ayarla(nx, nz, c, t0, t1);
            return r;
        }

        void KoseleriBirlestir(List<OdaDuvar> d)
        {
            double lim = ayar.KoseMesafe, cos60 = 0.5;
            for (int i = 0; i < d.Count; i++)
                for (int j = i + 1; j < d.Count; j++)
                {
                    var a = d[i]; var b = d[j];
                    double dot = a.nx * b.nx + a.nz * b.nz;
                    if (Math.Abs(dot) > cos60) continue;   // aci 60-120 arasinda degil
                    double ca = a.ax * a.nx + a.az * a.nz, cb = b.ax * b.nx + b.az * b.nz;
                    double det = a.nx * b.nz - a.nz * b.nx;
                    if (Math.Abs(det) < 1e-6) continue;
                    double X = (ca * b.nz - cb * a.nz) / det, Z = (a.nx * cb - b.nx * ca) / det;
                    bool aA = En(a, X, Z, out double da), bB = En(b, X, Z, out double db);   // true: A ucu yakin
                    bool yakinA = da < lim, yakinB = db < lim;
                    bool icA = Icinde(a, X, Z), icB = Icinde(b, X, Z);
                    bool tasi = false;
                    if (yakinA && yakinB) tasi = true;
                    else if (yakinA && icB) tasi = true;
                    else if (yakinB && icA) tasi = true;
                    if (!tasi) continue;
                    if (yakinA) { if (aA) { a.ax = (float)X; a.az = (float)Z; } else { a.bx = (float)X; a.bz = (float)Z; } }
                    if (yakinB) { if (bB) { b.ax = (float)X; b.az = (float)Z; } else { b.bx = (float)X; b.bz = (float)Z; } }
                }
        }

        static bool En(OdaDuvar d, double x, double z, out double mesafe)
        {
            double da = Math.Sqrt((d.ax - x) * (d.ax - x) + (d.az - z) * (d.az - z)), db = Math.Sqrt((d.bx - x) * (d.bx - x) + (d.bz - z) * (d.bz - z));
            mesafe = Math.Min(da, db); return da <= db;
        }

        static bool Icinde(OdaDuvar d, double x, double z)
        {
            double l = d.Uzunluk; if (l < 1e-6) return false;
            double ex = (d.bx - d.ax) / l, ez = (d.bz - d.az) / l;
            double t = (x - d.ax) * ex + (z - d.az) * ez;
            return t > -0.05 && t < l + 0.05;
        }

        void KimlikEsle(List<OdaDuvar> yeni)
        {
            var kullanildi = new bool[oncekiDuvar.Count];
            foreach (var d in yeni)
            {
                int en = -1; double enOrtak = 0;
                for (int i = 0; i < oncekiDuvar.Count; i++)
                {
                    if (kullanildi[i]) continue;
                    var o = oncekiDuvar[i];
                    if (Math.Abs(o.nx * d.nx + o.nz * d.nz) < 0.95) continue;
                    double mx = (d.ax + d.bx) * 0.5, mz = (d.az + d.bz) * 0.5;
                    if (Math.Abs((mx - o.ax) * o.nx + (mz - o.az) * o.nz) > 0.15) continue;
                    double ol = o.Uzunluk; if (ol < 1e-6) continue;
                    double ex = (o.bx - o.ax) / ol, ez = (o.bz - o.az) / ol;
                    double t0 = (d.ax - o.ax) * ex + (d.az - o.az) * ez, t1 = (d.bx - o.ax) * ex + (d.bz - o.az) * ez; if (t0 > t1) { double t = t0; t0 = t1; t1 = t; }
                    double ortak = Math.Min(t1, ol) - Math.Max(t0, 0.0);
                    if (ortak > enOrtak) { enOrtak = ortak; en = i; }
                }
                if (en >= 0 && enOrtak > 0.05) { d.id = oncekiDuvar[en].id; kullanildi[en] = true; }
                else d.id = ++sonDuvarId;
            }
        }

        // ================================================================================== esya kutulari
        void KutulariGuncelle(OdaSonuc s, float[] nk, int n)
        {
            float cell = ayar.Hucre, zy = s.zeminY;
            var sayac = new Dictionary<long, int>();
            for (int i = 0; i < n; i++)
            {
                float x = nk[i * 3], y = nk[i * 3 + 1], z = nk[i * 3 + 2];
                float h = y - zy;
                if (h < ayar.EsyaAlt || h > ayar.EsyaUst) continue;
                if (DuvaraYakin(s.duvarlar, x, z)) continue;
                long k = Anahtar((int)Math.Floor(x / cell), (int)Math.Floor(z / cell));
                sayac.TryGetValue(k, out int c); sayac[k] = c + 1;
            }
            var dolu = new HashSet<long>();
            foreach (var kv in sayac) if (kv.Value >= ayar.MinNokta) dolu.Add(kv.Key);

            // 1 hucrelik delikleri kapat (morfolojik kapama yerine: yalniz neredeyse tamamen cevrili bos hucreler)
            var ekle = new List<long>();
            var aday = new HashSet<long>();
            foreach (var k in dolu)
            {
                int i = Ix(k), j = Jx(k);
                for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++) { long a = Anahtar(i + di, j + dj); if (!dolu.Contains(a)) aday.Add(a); }
            }
            foreach (var a in aday)
            {
                int i = Ix(a), j = Jx(a), kom = 0;
                for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++) if ((di != 0 || dj != 0) && dolu.Contains(Anahtar(i + di, j + dj))) kom++;
                if (kom >= 7) ekle.Add(a);   // yalniz yuzeydeki delikler (8 komsudan en az 7'si dolu); esyalar arasi bosluk (en cok 6 komsu) kapanmaz
            }
            foreach (var a in ekle) dolu.Add(a);

            // 8 komsulu bilesenler
            var etiket = new Dictionary<long, int>();
            var bilesenler = new List<List<long>>();
            foreach (var k in dolu)
            {
                if (etiket.ContainsKey(k)) continue;
                int id = bilesenler.Count; var liste = new List<long>(); var kuyruk = new Stack<long>();
                kuyruk.Push(k); etiket[k] = id;
                while (kuyruk.Count > 0)
                {
                    long c = kuyruk.Pop(); liste.Add(c); int ci = Ix(c), cj = Jx(c);
                    for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++)
                        {
                            if (di == 0 && dj == 0) continue;
                            long a = Anahtar(ci + di, cj + dj);
                            if (dolu.Contains(a) && !etiket.ContainsKey(a)) { etiket[a] = id; kuyruk.Push(a); }
                        }
                }
                bilesenler.Add(liste);
            }
            // yukseklikler
            var yuks = new List<float>[bilesenler.Count]; var bpts = new List<double[]>[bilesenler.Count];
            for (int i = 0; i < yuks.Length; i++) { yuks[i] = new List<float>(); bpts[i] = new List<double[]>(); }
            for (int i = 0; i < n; i++)
            {
                float x = nk[i * 3], y = nk[i * 3 + 1], z = nk[i * 3 + 2];
                float h = y - zy;
                if (h < ayar.EsyaAlt || h > ayar.EsyaUst) continue;
                if (etiket.TryGetValue(Anahtar((int)Math.Floor(x / cell), (int)Math.Floor(z / cell)), out int e)) { yuks[e].Add(h); bpts[e].Add(new double[] { x, z }); }
            }

            var yeni = new List<OdaKutu>();
            for (int b = 0; b < bilesenler.Count; b++)
            {
                var liste = bilesenler[b];
                if (liste.Count * cell * cell < ayar.MinKutuAlan) continue;
                if (bpts[b].Count < 3) continue;
                var hull = Zarf(bpts[b]);   // hucre icindeki gercek noktalarin zarfi (hucre merkezlerinden daha keskin aci/boyut)
                double phi = EnKucukDikdortgenAcisi(hull);
                Dikdortgen(hull, phi, 0.0, out double cx, out double cz, out double lu, out double lv);
                if (lv > lu) { double t = lu; lu = lv; lv = t; phi += Math.PI / 2; }
                double deg = Mod180(phi * 180.0 / Math.PI);
                if (s.yonVar)
                {
                    double fark = Math.Abs(((deg - s.baskinAci) % 90.0 + 90.0) % 90.0); fark = Math.Min(fark, 90.0 - fark);
                    if (fark < ayar.AciKilidi)
                    {
                        double a1 = Mod180(s.baskinAci), a2 = Mod180(s.baskinAci + 90.0);
                        double hedef = Cember180(deg, a1) <= Cember180(deg, a2) ? a1 : a2;
                        Dikdortgen(hull, hedef * Math.PI / 180.0, 0.0, out cx, out cz, out lu, out lv);
                        if (lv > lu) { double t = lu; lu = lv; lv = t; hedef += 90.0; }
                        deg = Mod180(hedef);
                    }
                }
                var hl = yuks[b]; if (hl.Count == 0) continue;
                hl.Sort(); float h95 = hl[(int)(0.95 * (hl.Count - 1))];
                if (h95 < ayar.MinKutuYukseklik || lv < ayar.MinKutuKenar) continue;
                yeni.Add(new OdaKutu { cx = (float)cx, cz = (float)cz, sx = (float)lu, sz = (float)lv, sy = h95, cy = zy + h95 * 0.5f, aci = (float)deg });
            }
            Esle(yeni);
        }

        void Esle(List<OdaKutu> yeni)
        {
            var cift = new List<float[]>();   // iou, eski, yeni
            for (int o = 0; o < kutular.Count; o++)
                for (int n = 0; n < yeni.Count; n++)
                {
                    float iou = IoU(kutular[o].k, yeni[n]);
                    if (iou >= 0.3f) cift.Add(new float[] { iou, o, n });
                }
            cift.Sort((a, b) => b[0].CompareTo(a[0]));
            var oEs = new bool[kutular.Count]; var nEs = new bool[yeni.Count];
            foreach (var c in cift)
            {
                int o = (int)c[1], n = (int)c[2];
                if (oEs[o] || nEs[n]) continue;
                oEs[o] = nEs[n] = true;
                var k = kutular[o].k; var y = yeni[n]; float r = ayar.KutuSilme;
                k.cx += (y.cx - k.cx) * r; k.cz += (y.cz - k.cz) * r; k.cy += (y.cy - k.cy) * r;
                double d = ((y.aci - k.aci) % 180.0 + 270.0) % 180.0 - 90.0;
                if (Math.Abs(k.sx - k.sz) < 0.08 && Math.Abs(d) > 45) { d -= Math.Sign(d) * 90.0; }
                k.aci = (float)Mod180(k.aci + d * r);
                k.sx += (y.sx - k.sx) * r; k.sz += (y.sz - k.sz) * r; k.sy += (y.sy - k.sy) * r;
                kutular[o].onay++; kutular[o].kayip = 0;
            }
            for (int o = kutular.Count - 1; o >= 0; o--)
            {
                if (oEs[o]) continue;
                kutular[o].kayip++;
                if (kutular[o].kayip >= ayar.KayipSiniri || kutular[o].onay < ayar.OnayGerek) kutular.RemoveAt(o);
            }
            for (int n = 0; n < yeni.Count; n++)
            {
                if (nEs[n]) continue;
                yeni[n].id = ++sonKutuId;
                kutular.Add(new KutuDurum { k = yeni[n], onay = 1, kayip = 0 });
            }
        }

        static bool DuvaraYakin(List<OdaDuvar> duvarlar, float x, float z)
        {
            foreach (var d in duvarlar)
            {
                float l = d.Uzunluk; if (l < 1e-4f) continue;
                float ex = (d.bx - d.ax) / l, ez = (d.bz - d.az) / l;
                float t = (x - d.ax) * ex + (z - d.az) * ez;
                if (t < -0.1f || t > l + 0.1f) continue;
                float sd = (x - d.ax) * d.nx + (z - d.az) * d.nz;
                if (sd < 0.10f) return true;
            }
            return false;
        }

        // ================================================================================== geometri
        static long Anahtar(int i, int j) { return ((long)i << 32) | (uint)j; }
        static int Ix(long k) { return (int)(k >> 32); }
        static int Jx(long k) { return (int)(uint)(k & 0xFFFFFFFFL); }

        static double Mod180(double a) { a %= 180.0; if (a < 0) a += 180.0; return a; }
        static double Cember180(double a, double b) { double d = Math.Abs(Mod180(a) - Mod180(b)); return Math.Min(d, 180.0 - d); }

        public static float PolyAlan(float[] sinir)
        {
            int m = sinir.Length / 3; double a = 0;
            for (int i = 0; i < m; i++)
            {
                int j = (i + 1) % m;
                a += sinir[i * 3] * sinir[j * 3 + 2] - sinir[j * 3] * sinir[i * 3 + 2];
            }
            return (float)Math.Abs(a * 0.5);
        }

        static float OrtY(float[] sinir) { double t = 0; int m = sinir.Length / 3; for (int i = 0; i < m; i++) t += sinir[i * 3 + 1]; return (float)(t / m); }

        /// <summary>Bukey zarf (Andrew monoton zinciri). Girdi x,z ciftleri.</summary>
        public static List<double[]> Zarf(List<double[]> p)
        {
            var a = new List<double[]>(p);
            a.Sort((u, v) => u[0] != v[0] ? u[0].CompareTo(v[0]) : u[1].CompareTo(v[1]));
            // tekrarlari at
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

        static double Capraz(double[] o, double[] a, double[] b) { return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]); }

        /// <summary>Zarfin kenar yonlerini deneyerek en kucuk alanli sinirlayici dikdortgenin acisi (radyan).</summary>
        static double EnKucukDikdortgenAcisi(List<double[]> h)
        {
            if (h.Count < 2) return 0;
            if (h.Count == 2) return Math.Atan2(h[1][1] - h[0][1], h[1][0] - h[0][0]);
            double enAlan = double.MaxValue, enAci = 0;
            for (int i = 0; i < h.Count; i++)
            {
                var a = h[i]; var b = h[(i + 1) % h.Count];
                double ang = Math.Atan2(b[1] - a[1], b[0] - a[0]);
                Dikdortgen(h, ang, 0f, out _, out _, out double lu, out double lv);
                double alan = lu * lv;
                if (alan < enAlan - 1e-12) { enAlan = alan; enAci = ang; }
            }
            return enAci;
        }

        /// <summary>phi yonlu eksenlerde sinirlayici dikdortgen; sisirme: her kenara 'sisir' eklenir (hucre merkezlerinden hucre kenarlarina).</summary>
        static void Dikdortgen(List<double[]> h, double phi, double sisir, out double cx, out double cz, out double lu, out double lv)
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
            lu = u1 - u0 + sisir; lv = v1 - v0 + sisir;
        }

        // ---------------- dondurulmus dikdortgen IoU (Sutherland-Hodgman) ----------------
        static float IoU(OdaKutu a, OdaKutu b)
        {
            var pa = new float[8]; var pb = new float[8]; a.Koseler(pa); b.Koseler(pb);
            double ia = Kesisim(pa, pb);
            double ua = (double)a.sx * a.sz + (double)b.sx * b.sz - ia;
            return ua > 1e-9 ? (float)(ia / ua) : 0f;
        }

        static double Kesisim(float[] a, float[] b)
        {
            var poly = new List<double[]>();
            for (int i = 0; i < 4; i++) poly.Add(new double[] { a[i * 2], a[i * 2 + 1] });
            if (Alan(poly) < 0) poly.Reverse();
            var clip = new List<double[]>();
            for (int i = 0; i < 4; i++) clip.Add(new double[] { b[i * 2], b[i * 2 + 1] });
            if (Alan(clip) < 0) clip.Reverse();
            for (int e = 0; e < 4 && poly.Count > 0; e++)
            {
                var p1 = clip[e]; var p2 = clip[(e + 1) % 4];
                var cikis = new List<double[]>();
                for (int i = 0; i < poly.Count; i++)
                {
                    var cur = poly[i]; var prev = poly[(i + poly.Count - 1) % poly.Count];
                    bool curIc = Capraz(p1, p2, cur) >= 0, prevIc = Capraz(p1, p2, prev) >= 0;
                    if (curIc)
                    {
                        if (!prevIc) cikis.Add(Kes(prev, cur, p1, p2));
                        cikis.Add(cur);
                    }
                    else if (prevIc) cikis.Add(Kes(prev, cur, p1, p2));
                }
                poly = cikis;
            }
            return poly.Count < 3 ? 0.0 : Math.Abs(Alan(poly));
        }

        static double Alan(List<double[]> p)
        {
            double a = 0; for (int i = 0; i < p.Count; i++) { var u = p[i]; var v = p[(i + 1) % p.Count]; a += u[0] * v[1] - v[0] * u[1]; }
            return a * 0.5;
        }

        static double[] Kes(double[] a, double[] b, double[] p1, double[] p2)
        {
            double dx = b[0] - a[0], dz = b[1] - a[1], ex = p2[0] - p1[0], ez = p2[1] - p1[1];
            double den = dx * ez - dz * ex;
            if (Math.Abs(den) < 1e-12) return a;
            double t = ((p1[0] - a[0]) * ez - (p1[1] - a[1]) * ex) / den;
            return new[] { a[0] + dx * t, a[1] + dz * t };
        }
    }
}
