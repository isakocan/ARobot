using System;
using System.Collections.Generic;

namespace ArTarama
{
    // =====================================================================================================
    // Bant tabanli duvar / tavan / taranan zemin (saf C#).
    //
    // Gercek taramada (20.11.2026, toplanti odasi) ARCore derinlik guveni beyaz duvarlarda cok dusuktu
    // (medyan 8/255); yuksek guven esikli voksel izgarasi neredeyse hic duvar gormedi. Burada TUM derinlik
    // noktalari kullanilir, ama yalniz esyalarin ustunde kalan bantta (zeminden 1,3-2,2 m): orada genelde
    // yalniz duvar vardir. Kusbakisi yogunlukta Manhattan ana yonu bulunur; iki eksendeki yogunluk tepeleri
    // duvar cizgileridir. Tek kare gurultusu (~10 cm) binlerce noktanin ortalamasinda kaybolur.
    // Ayni kayitta oda eni 2,95 m cikti (RoomPlan LiDAR: 3,0 m).
    // =====================================================================================================
    public partial class OdaModeli
    {
        public bool KutularAcik = false;

        public float BantAlt = 1.3f, BantUst = 2.2f;
        public float TavanAramaAlt = 2.1f, TavanAramaUst = 4.0f;

        class BantDuvar
        {
            public int id, eksen, gorulme, kayip, yan; public double ofset, t0, t1, destek;   // yan: zeminin oldugu taraf (+1/-1, u yonunde)
            public bool kesin; public float kesinZaman, ilk;
            public bool aKose, bKose;
            public double Uz { get { return t1 - t0; } }
        }
        readonly List<BantDuvar> bantDurum = new List<BantDuvar>();
        double bantAci = double.NaN;
        double kamSx, kamSz; long kamN;
        readonly List<double> izX = new List<double>(), izZ = new List<double>();   // kamera yolu (10 cm aralikla): duvar buradan gecemez

        const double Hb = 0.05;          // histogram kutusu (m)
        const double SegKutu = 0.10;     // duvar boyunca doluluk kutusu
        const double SegBosluk = 0.5;    // bu kadar bosluk ayni duvar sayilir
        const double MinSeg = 0.6;

        void BantSifirla() { bantDurum.Clear(); bantAci = double.NaN; kamSx = kamSz = 0; kamN = 0; izX.Clear(); izZ.Clear(); }

        /// <summary>Her derinlik karesinde cagrilir (Isle): kamera yolunu kaydeder.</summary>
        void IzEkle(float x, float z)
        {
            int n = izX.Count;
            if (n > 0) { double dx = x - izX[n - 1], dz = z - izZ[n - 1]; if (dx * dx + dz * dz < 0.01) return; }
            izX.Add(x); izZ.Add(z);
        }

        /// <summary>Zemin bilindikten sonra cagrilir: s.duvarlar, tavan (duzlem yoksa), s.zeminTaranan, s.sinir.</summary>
        void BantGuncelle(OdaSonuc s, float zy0, float zaman)
        {
            kamSx += izgara.SonKameraX; kamSz += izgara.SonKameraZ; kamN++;
            double kmx = kamSx / kamN, kmz = kamSz / kamN;

            // Etiket (SegFormer) varsa: duvar yalniz "duvar/acilis" etiketli noktalardan ve HER yukseklikte (zemin+15 cm .. tavan-15 cm);
            // esya yuzleri karismaz. Yoksa: tum noktalar, esya ustu bant (1,3-2,2 m).
            bool etiketli = izgara.EtiketNokta > 20000;
            s.etiketli = etiketli;
            if (etiketli && !s.tavanGercek)
            {
                float tyE = HistTepe(izgara.TavanYHist, zy0 + 1.8f, zy0 + 4.5f, 1500);
                if (!float.IsNaN(tyE)) { ty = tyE; tavanGercek = true; s.tavanY = ty; s.tavanGercek = true; }
            }
            var kaynak = etiketli ? izgara.SutunDuvar : izgara.Sutun;
            float altY = etiketli ? zy0 + 0.15f : zy0 + BantAlt;
            float ustY = etiketli ? (s.tavanGercek ? s.tavanY - 0.15f : zy0 + 2.4f) : zy0 + BantUst;
            int kA = KanitIzgarasi.SutunKatmani(altY), kU = KanitIzgarasi.SutunKatmani(ustY);
            int kZ0 = KanitIzgarasi.SutunKatmani(zy0 - 0.08f), kZ1 = KanitIzgarasi.SutunKatmani(zy0 + 0.08f);
            int kT0 = KanitIzgarasi.SutunKatmani(zy0 + TavanAramaAlt), kT1 = KanitIzgarasi.SutunKatmani(zy0 + TavanAramaUst);
            int nk = KanitIzgarasi.SutunKatman;
            var tavanSay = new long[nk]; var tavanHucre = new int[nk];

            var bx = new List<double>(); var bz = new List<double>(); var bw = new List<double>();
            double toplam = 0;
            foreach (var kv in kaynak)
            {
                var h = kv.Value;
                int ix = (int)((kv.Key >> 21) & 0x1FFFFF) - 1048576, iz = (int)(kv.Key & 0x1FFFFF) - 1048576;
                int w = 0; for (int k = Math.Max(0, kA); k <= Math.Min(nk - 1, kU); k++) w += h[k];
                if (w > 0) { bx.Add((ix + 0.5) * KanitIzgarasi.V); bz.Add((iz + 0.5) * KanitIzgarasi.V); bw.Add(w); toplam += w; }
            }
            if (etiketli)
            {
                foreach (var kv in izgara.ZeminHucre) if (kv.Value >= 2) s.zeminTaranan.Add(kv.Key);
            }
            else
                foreach (var kv in izgara.Sutun)
                {
                    var h = kv.Value;
                    int zs = 0; for (int k = Math.Max(0, kZ0); k <= Math.Min(nk - 1, kZ1); k++) zs += h[k];
                    if (zs >= 2) s.zeminTaranan.Add(kv.Key);
                    for (int k = Math.Max(0, kT0); k <= Math.Min(nk - 1, kT1); k++) if (h[k] > 0) { tavanSay[k] += h[k]; tavanHucre[k]++; }
                }
            s.taramaAlani = s.zeminTaranan.Count * KanitIzgarasi.V * KanitIzgarasi.V;

            // ---------- tavan (duzlem yoksa): ust katmanlarda genis ve belirgin tepe ----------
            if (!s.tavanGercek)
            {
                int en = -1; long enS = 0, top = 0; int katSay = 0;
                for (int k = Math.Max(0, kT0); k <= Math.Min(nk - 1, kT1); k++) { top += tavanSay[k]; katSay++; if (tavanSay[k] > enS) { enS = tavanSay[k]; en = k; } }
                if (en >= 0 && katSay > 3)
                {
                    long p3 = tavanSay[en] + (en > 0 ? tavanSay[en - 1] : 0) + (en + 1 < nk ? tavanSay[en + 1] : 0);
                    double digerOrt = (top - p3) / (double)Math.Max(1, katSay - 3);
                    if (tavanHucre[en] >= 300 && enS >= 3000 && p3 >= 2.5 * digerOrt)
                    {
                        // katman icinde agirlikli ortalama
                        double ys = 0, ws = 0;
                        for (int k = Math.Max(0, en - 1); k <= Math.Min(nk - 1, en + 1); k++) { ys += tavanSay[k] * (KanitIzgarasi.SutunAlt + (k + 0.5) * KanitIzgarasi.SutunKat); ws += tavanSay[k]; }
                        ty = (float)(ys / ws); tavanGercek = true; s.tavanY = ty; s.tavanGercek = true;
                    }
                }
            }
            float yuk = s.tavanY - zy0;

            s.duvarlar.Clear();
            if (toplam < 1500 || bx.Count < 50) { bantDurumKayip(); SinirHesapla(s); return; }

            // ---------- Manhattan ana yonu ----------
            double aci = AciBul(bx, bz, bw);
            if (!double.IsNaN(bantAci))
            {
                double d = AciFark90(aci, bantAci);
                if (Math.Abs(d) < 2.5) aci = bantAci;                     // kucuk oynamalarda sabit tut
                else if (bantDurum.Exists(q => q.kesin)) aci = bantAci + Math.Max(-1.0, Math.Min(1.0, d));   // kesin duvar varsa yavas don
            }
            bantAci = ((aci % 90) + 90) % 90;
            s.yonVar = true; s.baskinAci = (float)bantAci;
            double r = bantAci * Math.PI / 180.0, c = Math.Cos(r), sn = Math.Sin(r);

            // ---------- iki eksende duvar cizgileri ----------
            var yeni = new List<BantDuvar>();
            int n = bx.Count;
            var u0 = new double[n]; var u1 = new double[n];
            for (int i = 0; i < n; i++) { u0[i] = bx[i] * c + bz[i] * sn; u1[i] = -bx[i] * sn + bz[i] * c; }
            for (int eksen = 0; eksen < 2; eksen++)
            {
                var u = eksen == 0 ? u0 : u1; var t = eksen == 0 ? u1 : u0;
                double mn = double.MaxValue, mx = double.MinValue; for (int i = 0; i < n; i++) { if (u[i] < mn) mn = u[i]; if (u[i] > mx) mx = u[i]; }
                int nb = (int)((mx - mn) / Hb) + 3; var hs = new double[nb];
                for (int i = 0; i < n; i++) hs[(int)((u[i] - mn) / Hb) + 1] += bw[i];
                var hz = new double[nb]; for (int i = 1; i + 1 < nb; i++) hz[i] = 0.25 * hs[i - 1] + 0.5 * hs[i] + 0.25 * hs[i + 1];
                double hmax = 0; foreach (var v in hz) if (v > hmax) hmax = v;
                double esik = Math.Max(250.0, 0.10 * hmax);
                for (int i = 1; i + 1 < nb; i++)
                {
                    if (hz[i] < esik) continue;
                    bool tepe = true;
                    for (int j = Math.Max(0, i - 8); j <= Math.Min(nb - 1, i + 8); j++) if (j != i && (hz[j] > hz[i] || (hz[j] == hz[i] && j < i))) { tepe = false; break; }
                    if (!tepe) continue;
                    // ofset: tepe cevresinde agirlikli ortalama
                    double us = 0, ws = 0;
                    for (int k = 0; k < n; k++) { double dd = u[k] - (mn + (i - 0.5) * Hb); if (Math.Abs(dd) <= 0.075) { us += u[k] * bw[k]; ws += bw[k]; } }
                    if (ws <= 0) continue;
                    double ofs = us / ws;
                    // duvar boyunca parcalar
                    double tmn = double.MaxValue, tmx = double.MinValue;
                    for (int k = 0; k < n; k++) if (Math.Abs(u[k] - ofs) <= 0.10) { if (t[k] < tmn) tmn = t[k]; if (t[k] > tmx) tmx = t[k]; }
                    if (tmx <= tmn) continue;
                    int sb = (int)((tmx - tmn) / SegKutu) + 1; var sw = new double[sb];
                    for (int k = 0; k < n; k++) if (Math.Abs(u[k] - ofs) <= 0.10) sw[Math.Min(sb - 1, (int)((t[k] - tmn) / SegKutu))] += bw[k];
                    // kamera yolunun 35 cm yakinindan duvar gecemez: o kutular kesik sayilir (bosluk koprulenmez)
                    var yasak = new bool[sb];
                    for (int q = 0; q < izX.Count; q++)
                    {
                        double qu = eksen == 0 ? izX[q] * c + izZ[q] * sn : -izX[q] * sn + izZ[q] * c;
                        double qt = eksen == 0 ? -izX[q] * sn + izZ[q] * c : izX[q] * c + izZ[q] * sn;
                        if (Math.Abs(qu - ofs) > 0.35) continue;
                        int k0 = (int)Math.Floor((qt - 0.35 - tmn) / SegKutu), k1 = (int)Math.Floor((qt + 0.35 - tmn) / SegKutu);
                        for (int k = Math.Max(0, k0); k <= Math.Min(sb - 1, k1); k++) yasak[k] = true;
                    }
                    // zemin kurali: gercek duvarin BIR yaninda zemin gorulur (0,2-1,2 m), oteki yaninin dibinde (0,15-0,6 m) gorulmez.
                    // Iki yani da zeminse oda icinde bir yuzeydir (dolap, ekran); hic zemin yoksa oda disina tasan uzantidir.
                    var zyan = new sbyte[sb];
                    if (s.zeminTaranan.Count > 200)
                        for (int b = 0; b < sb; b++)
                        {
                            if (yasak[b] || sw[b] <= 0) continue;
                            double tb = tmn + (b + 0.5) * SegKutu;
                            // dis yan 30 cm'den baslar: derinlik gurultusu (~10 cm) zemin noktalarini duvarin hemen arkasina da dusurur
                            double icA = ZeminOrani(s.zeminTaranan, eksen, ofs, tb, +1, 0.2, 1.5, c, sn), icB = ZeminOrani(s.zeminTaranan, eksen, ofs, tb, -1, 0.2, 1.5, c, sn);
                            double disA = ZeminOrani(s.zeminTaranan, eksen, ofs, tb, +1, 0.3, 0.9, c, sn), disB = ZeminOrani(s.zeminTaranan, eksen, ofs, tb, -1, 0.3, 0.9, c, sn);
                            if (icA >= 0.15 && disB <= 0.15 && icA > disB + 0.1) zyan[b] = 1;
                            else if (icB >= 0.15 && disA <= 0.15 && icB > disA + 0.1) zyan[b] = -1;
                            else yasak[b] = true;
                        }
                    // kutu esigi: bu cizgideki dolu kutularin medyaninin %20'si (seyrek, rastlantisal noktalar parca uzatmasin)
                    var dolular = new List<double>(); for (int b = 0; b < sb; b++) if (sw[b] > 0) dolular.Add(sw[b]);
                    dolular.Sort(); double med = dolular.Count > 0 ? dolular[dolular.Count / 2] : 0;
                    double kutuEsik = Math.Max(12, 0.2 * med);
                    int bas = -1, son = -1; double dest = 0; int yan = 0;
                    for (int b = 0; b <= sb; b++)
                    {
                        bool kes = b < sb && yasak[b];
                        bool dolu = b < sb && !kes && sw[b] >= kutuEsik;
                        if (kes && bas >= 0) { Ekle(yeni, eksen, ofs, tmn, bas, son, dest, yan); bas = -1; continue; }
                        if (dolu)
                        {
                            if (bas < 0) { bas = b; dest = 0; yan = 0; }
                            else if ((b - son - 1) * SegKutu > SegBosluk) { Ekle(yeni, eksen, ofs, tmn, bas, son, dest, yan); bas = b; dest = 0; yan = 0; }
                            son = b; dest += sw[b]; yan += zyan[b];
                        }
                    }
                    if (bas >= 0) Ekle(yeni, eksen, ofs, tmn, bas, son, dest, yan);
                }
            }

            // ---------- durum takibi (aday -> kesin) ----------
            var esl = new bool[bantDurum.Count]; int eskiSay = bantDurum.Count; var eklenen = new List<BantDuvar>();
            foreach (var y in yeni)
            {
                int en = -1; double enD = 1e9;
                for (int i = 0; i < eskiSay; i++)
                {
                    var b = bantDurum[i]; if (esl[i] || b.eksen != y.eksen) continue;
                    double dof = Math.Abs(b.ofset - y.ofset); if (dof > 0.15) continue;
                    double ort = Math.Min(b.t1, y.t1) - Math.Max(b.t0, y.t0); if (ort < -0.3) continue;
                    if (dof < enD) { enD = dof; en = i; }
                }
                if (en < 0) { y.id = ++sonDuvarId; y.ilk = zaman; y.gorulme = 1; eklenen.Add(y); continue; }
                var d = bantDurum[en]; esl[en] = true;
                d.ofset = 0.6 * d.ofset + 0.4 * y.ofset;
                d.t0 = y.t0 < d.t0 ? y.t0 : 0.7 * d.t0 + 0.3 * y.t0;   // buyurken hemen, kuculurken yavas
                d.t1 = y.t1 > d.t1 ? y.t1 : 0.7 * d.t1 + 0.3 * y.t1;
                d.destek = y.destek; d.gorulme++; d.kayip = 0; if (y.yan != 0) d.yan = y.yan;
            }
            for (int i = esl.Length - 1; i >= 0; i--)
            {
                if (esl[i]) continue;
                var b = bantDurum[i]; b.kayip++;
                if ((!b.kesin && b.kayip >= 3) || b.kayip >= 15) bantDurum.RemoveAt(i);
            }
            bantDurum.AddRange(eklenen);
            BantBirlestir();
            foreach (var b in bantDurum)
                if (!b.kesin && b.gorulme >= 3 && b.Uz >= 0.8 && b.destek >= 1200) { b.kesin = true; b.kesinZaman = zaman; }

            // ---------- koseler: dik duvarlarin uclari 50 cm icindeyse kesisim noktasina ----------
            var cizim = new List<BantDuvar>();
            foreach (var b in bantDurum) cizim.Add(new BantDuvar { id = b.id, eksen = b.eksen, ofset = b.ofset, t0 = b.t0, t1 = b.t1, kesin = b.kesin, gorulme = b.gorulme, destek = b.destek, kesinZaman = b.kesinZaman, yan = b.yan });
            // ---------- ic kopya: ayni yana (zemin tarafi) bakan, ortusen, 60 cm'den yakin iki paralel duvardan icteki
            // (zemine dogru olan) duvar degil, duvara dayali esya yuzudur; disteki kalir ----------
            for (int i = cizim.Count - 1; i >= 0; i--)
            {
                var a = cizim[i]; if (a.yan == 0) continue;
                foreach (var b in cizim)
                {
                    if (ReferenceEquals(a, b) || b.eksen != a.eksen || b.yan != a.yan) continue;
                    double fark = (a.ofset - b.ofset) * a.yan;   // >0: a, b'ye gore oda icinde
                    if (fark <= 0.05 || fark > 0.6) continue;
                    double ort = Math.Min(a.t1, b.t1) - Math.Max(a.t0, b.t0);
                    if (ort < 0.3 || b.Uz < 0.5 * a.Uz) continue;
                    cizim.RemoveAt(i); break;
                }
            }

            foreach (var a in cizim)
                foreach (var b in cizim)
                {
                    if (a.eksen == b.eksen || a.id >= b.id) continue;
                    // a cizgisi: u_a = a.ofset, t boyunca; b: u_b = b.ofset. Kesisim: a uzerinde t = b.ofset, b uzerinde t = a.ofset.
                    if (!UcYakin(a, b.ofset, out bool aBas) || !UcYakin(b, a.ofset, out bool bBas)) continue;
                    if (aBas) { a.t0 = b.ofset; a.aKose = true; } else { a.t1 = b.ofset; a.bKose = true; }
                    if (bBas) { b.t0 = a.ofset; b.aKose = true; } else { b.t1 = a.ofset; b.bKose = true; }
                }

            // ---------- dunyaya ----------
            foreach (var b in cizim)
            {
                if (b.Uz < MinSeg * 0.8) continue;
                // hicbir koseye baglanmayan kisa parca (cogu dolap/ekran yuzu) kesin cizilmez
                if (b.kesin && !b.aKose && !b.bKose && b.Uz < 1.5) b.kesin = false;
                double ax0, az0, bx0, bz0;
                if (b.eksen == 0) { Dunya(b.ofset, b.t0, c, sn, out ax0, out az0); Dunya(b.ofset, b.t1, c, sn, out bx0, out bz0); }
                else { Dunya(b.t0, b.ofset, c, sn, out ax0, out az0); Dunya(b.t1, b.ofset, c, sn, out bx0, out bz0); }
                double dx = bx0 - ax0, dz = bz0 - az0, L = Math.Sqrt(dx * dx + dz * dz); if (L < 1e-6) continue;
                double nx = -dz / L, nz = dx / L;
                double mx = (ax0 + bx0) * 0.5, mz = (az0 + bz0) * 0.5;
                if (b.yan != 0)
                {
                    // zeminin oldugu taraf = oda ici: u ekseninin dunya yonu
                    double ux = b.eksen == 0 ? c : -sn, uz = b.eksen == 0 ? sn : c;
                    if ((ux * nx + uz * nz) * b.yan < 0) { nx = -nx; nz = -nz; }
                }
                else if ((kmx - mx) * nx + (kmz - mz) * nz < 0) { nx = -nx; nz = -nz; }
                float ilr = b.kesin ? 1f : (float)Math.Min(0.6, Math.Min(b.gorulme / 3.0, Math.Min(b.Uz / 0.8, b.destek / 1200.0)));
                s.duvarlar.Add(new OdaDuvar
                {
                    id = b.id, kesin = b.kesin, ilerleme = ilr, kesinZaman = b.kesinZaman,
                    ax = (float)ax0, az = (float)az0, bx = (float)bx0, bz = (float)bz0, aKose = b.aKose, bKose = b.bKose,
                    nx = (float)nx, nz = (float)nz, yukseklik = yuk
                });
            }
            SinirHesapla(s);
        }

        /// <summary>1 cm yukseklik histograminda [y0,y1] araliginda en yuksek tepe (komsu +-3 cm agirlikli ortalama). Yetersizse NaN.</summary>
        static float HistTepe(int[] h, float y0, float y1, int minSay)
        {
            int i0 = Math.Max(0, (int)((y0 - KanitIzgarasi.YAlt) / 0.01f)), i1 = Math.Min(h.Length - 1, (int)((y1 - KanitIzgarasi.YAlt) / 0.01f));
            int en = -1, enS = 0;
            for (int i = i0; i <= i1; i++) { int t = 0; for (int j = Math.Max(0, i - 3); j <= Math.Min(h.Length - 1, i + 3); j++) t += h[j]; if (t > enS) { enS = t; en = i; } }
            if (en < 0 || enS < minSay) return float.NaN;
            double ys = 0, ws = 0; for (int j = Math.Max(0, en - 3); j <= Math.Min(h.Length - 1, en + 3); j++) { ys += h[j] * (KanitIzgarasi.YAlt + (j + 0.5) * 0.01); ws += h[j]; }
            return (float)(ys / ws);
        }

        void bantDurumKayip()
        {
            for (int i = bantDurum.Count - 1; i >= 0; i--) { var b = bantDurum[i]; b.kayip++; if (!b.kesin && b.kayip >= 3) bantDurum.RemoveAt(i); }
        }

        static void Ekle(List<BantDuvar> l, int eksen, double ofs, double tmn, int bas, int son, double dest, int yan)
        {
            double t0 = tmn + bas * SegKutu, t1 = tmn + (son + 1) * SegKutu;
            if (t1 - t0 < MinSeg) return;
            if (dest < 300) return;
            l.Add(new BantDuvar { eksen = eksen, ofset = ofs, t0 = t0, t1 = t1, destek = dest, yan = Math.Sign(yan) });
        }

        /// <summary>Duvar cizgisinin bir yaninda (u = ofs + yon*d, d in [d0,d1]) t +-10 cm seridinde zemin hucresi orani.</summary>
        static double ZeminOrani(HashSet<long> zemin, int eksen, double ofs, double tb, int yon, double d0, double d1, double c, double sn)
        {
            int var = 0, top = 0;
            for (double d = d0; d <= d1 + 1e-6; d += 0.1)
                for (double dt = -0.1; dt <= 0.1 + 1e-6; dt += 0.1)
                {
                    double uu = ofs + yon * d, tt = tb + dt, x, z;
                    if (eksen == 0) Dunya(uu, tt, c, sn, out x, out z); else Dunya(tt, uu, c, sn, out x, out z);
                    top++;
                    if (zemin.Contains(OdaSonuc.Anah2(KanitIzgarasi.Ind((float)x), KanitIzgarasi.Ind((float)z)))) var++;
                }
            return top > 0 ? var / (double)top : 0;
        }

        /// <summary>Kesisim t'si duvarin bir ucuna yakin mi: ucun 0,5 m disinda (uzatma) ya da 1,3 m icinde (koseyi gecen tasmayi kesme).</summary>
        static bool UcYakin(BantDuvar d, double t, out bool bas)
        {
            bas = Math.Abs(t - d.t0) < Math.Abs(t - d.t1);
            double uc = bas ? d.t0 : d.t1;
            bool disarida = bas ? t < uc : t > uc;
            double uz = Math.Abs(t - uc);
            if (disarida) return uz <= 0.5;
            return uz <= Math.Min(1.3, 0.45 * d.Uz);
        }

        static void Dunya(double a0, double a1, double c, double s, out double x, out double z) { x = a0 * c - a1 * s; z = a0 * s + a1 * c; }

        static double AciFark90(double a, double b) { double d = ((a - b) % 90 + 135) % 90 - 45; return d; }

        /// <summary>Iki eksendeki 5 cm histogramlarin "keskinligini" (kareler toplami) en buyuten aci, [0,90).</summary>
        static double AciBul(List<double> x, List<double> z, List<double> w)
        {
            int n = x.Count; double enS = -1, enA = 0;
            Func<double, double> skor = a =>
            {
                double r = a * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
                var h0 = new Dictionary<int, double>(); var h1 = new Dictionary<int, double>();
                for (int i = 0; i < n; i++)
                {
                    int k0 = (int)Math.Floor((x[i] * c + z[i] * s) / Hb), k1 = (int)Math.Floor((-x[i] * s + z[i] * c) / Hb);
                    h0.TryGetValue(k0, out double v0); h0[k0] = v0 + w[i];
                    h1.TryGetValue(k1, out double v1); h1[k1] = v1 + w[i];
                }
                double t = 0; foreach (var v in h0.Values) t += v * v; foreach (var v in h1.Values) t += v * v; return t;
            };
            for (double a = 0; a < 90; a += 2) { double v = skor(a); if (v > enS) { enS = v; enA = a; } }
            for (double a = enA - 2; a <= enA + 2; a += 0.5) { double v = skor(a); if (v > enS) { enS = v; enA = a; } }
            return ((enA % 90) + 90) % 90;
        }

        void BantBirlestir()
        {
            bool degisti = true;
            while (degisti)
            {
                degisti = false;
                for (int i = 0; i < bantDurum.Count && !degisti; i++)
                    for (int j = i + 1; j < bantDurum.Count && !degisti; j++)
                    {
                        var a = bantDurum[i]; var b = bantDurum[j];
                        if (a.eksen != b.eksen || Math.Abs(a.ofset - b.ofset) > 0.12) continue;
                        if (Math.Min(a.t1, b.t1) - Math.Max(a.t0, b.t0) < -0.4) continue;
                        bool aKalir = a.kesin != b.kesin ? a.kesin : a.gorulme >= b.gorulme;
                        var k = aKalir ? a : b; var g = aKalir ? b : a;
                        k.t0 = Math.Min(k.t0, g.t0); k.t1 = Math.Max(k.t1, g.t1); k.destek = Math.Max(k.destek, g.destek);
                        k.gorulme = Math.Max(k.gorulme, g.gorulme); k.kayip = Math.Min(k.kayip, g.kayip);
                        if (g.kesin && !k.kesin) { k.kesin = true; k.kesinZaman = g.kesinZaman; }
                        bantDurum.Remove(g); degisti = true;
                    }
            }
        }

        /// <summary>Kesif siniri: taranan zeminin (25 cm kaba hucre) taranmamis komsusu olan, duvarin dibinde olmayan hucreler.</summary>
        void SinirHesapla(OdaSonuc s)
        {
            var kaba = new Dictionary<long, int>();
            foreach (long k in s.zeminTaranan)
            {
                int ix = (int)((k >> 21) & 0x1FFFFF) - 1048576, iz = (int)(k & 0x1FFFFF) - 1048576;
                long kk = OdaSonuc.Anah2((int)Math.Floor(ix / 5.0), (int)Math.Floor(iz / 5.0));
                kaba.TryGetValue(kk, out int c0); kaba[kk] = c0 + 1;
            }
            var sin = new List<float>();
            foreach (var kv in kaba)
            {
                if (kv.Value < 4) continue;
                int cx = (int)((kv.Key >> 21) & 0x1FFFFF) - 1048576, cz = (int)(kv.Key & 0x1FFFFF) - 1048576;
                bool sinirda = false;
                for (int d = 0; d < 4 && !sinirda; d++)
                {
                    int ox = d == 0 ? 1 : d == 1 ? -1 : 0, oz = d == 2 ? 1 : d == 3 ? -1 : 0;
                    if (!kaba.TryGetValue(OdaSonuc.Anah2(cx + ox, cz + oz), out int nn) || nn < 4) sinirda = true;
                }
                if (!sinirda) continue;
                float x = (cx + 0.5f) * 0.25f, z = (cz + 0.5f) * 0.25f;
                bool duvarDibi = false;
                foreach (var dv in s.duvarlar) if (OdaSonuc.DuvarMesafesi(dv, x, z) < 0.45f) { duvarDibi = true; break; }
                if (duvarDibi) continue;
                sin.Add(x); sin.Add(z);
            }
            s.sinir = sin.ToArray();
        }
    }
}
