using System;
using System.Collections.Generic;

namespace ArTarama
{
    // Esya kutulari: dolu vokseller (zeminden 0,08-2,0 m) -> 2B hucre bilesenleri -> Manhattan eksenine oturan dikdortgen.
    // Kutu zeminden kendi ust yuzeyine kadar tek parca "massif" kutudur (masanin altini modellemeyiz).
    // Durum makinesi: aday -> kesin (3 ardisik kararli guncelleme, >= 3 sn, >= 2 bakis yonu).
    public partial class OdaModeli
    {
        class KutuDurum
        {
            public int id; public OdaKutu k; public OdaKutu ham;
            public int kararli, bin, kayip; public float seen, kesinZaman, ilerleme; public bool kesin;
            public long[] hucreler = new long[0]; public int[] ustler = new int[0];
        }

        class YKutu
        {
            public OdaKutu k; public int bin; public float son; public long[] hucreler; public int[] ustler;
        }

        readonly List<KutuDurum> kutuDurum = new List<KutuDurum>();

        static void HucreCoz(long k, out int ix, out int iz) { ix = (int)((k >> 21) & 0x1FFFFF) - 1048576; iz = (int)(k & 0x1FFFFF) - 1048576; }

        List<YKutu> YeniKutular(Dictionary<long, Hucre> hucre)
        {
            int altKat = Math.Max(2, (int)Math.Round(ayar.EsyaAlt / KanitIzgarasi.V));
            int span = (int)Math.Round(ayar.DuvarSpan / KanitIzgarasi.V);
            var ust = new Dictionary<long, int>();
            foreach (var kv in hucre)
            {
                var h = kv.Value; if (h.kutuUst == int.MinValue || h.kutuUst - zk < altKat) continue;
                HucreCoz(kv.Key, out int ix, out int iz);
                double x = (ix + 0.5) * KanitIzgarasi.V, z = (iz + 0.5) * KanitIzgarasi.V;
                bool uzak = true; bool tall = h.maxIy - h.minIy >= span;
                foreach (var d in duvarDurum)
                {
                    double m = OdaGeo.ParcaMesafe(d.dax, d.daz, d.dbx, d.dbz, x, z);
                    if ((d.kesin && m < ayar.DuvarBandi) || (!d.kesin && tall && m < 0.15)) { uzak = false; break; }
                }
                if (uzak) ust[kv.Key] = h.kutuUst;
            }
            // yalniz kalan lekeleri (ucan piksel artiklari) at: en az 2 komsu hucre gerekir
            var lekeli = new List<long>();
            foreach (var kv in ust)
            {
                HucreCoz(kv.Key, out int ci0, out int cj0); int kom = 0;
                for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++) if ((di != 0 || dj != 0) && ust.ContainsKey(OdaSonuc.Anah2(ci0 + di, cj0 + dj))) kom++;
                if (kom < 2) lekeli.Add(kv.Key);
            }
            foreach (long k in lekeli) ust.Remove(k);
            // 1 hucrelik delikleri kapat (8 komsudan en az 7'si dolu bos hucre)
            var delik = new Dictionary<long, int>();
            foreach (var kv in ust)
            {
                HucreCoz(kv.Key, out int ci1, out int cj1);
                for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++)
                {
                    long a = OdaSonuc.Anah2(ci1 + di, cj1 + dj);
                    if (ust.ContainsKey(a) || delik.ContainsKey(a)) continue;
                    int kom = 0, mx = int.MinValue;
                    for (int ej = -1; ej <= 1; ej++) for (int ei = -1; ei <= 1; ei++)
                        if ((ei != 0 || ej != 0) && ust.TryGetValue(OdaSonuc.Anah2(ci1 + di + ei, cj1 + dj + ej), out int tv)) { kom++; if (tv > mx) mx = tv; }
                    if (kom >= 7) delik[a] = mx;
                }
            }
            foreach (var kv in delik) { ust[kv.Key] = kv.Value; hucre[kv.Key] = new Hucre { bin = 0, son = 0, minIy = kv.Value, maxIy = kv.Value, kutuUst = kv.Value }; }
            // ust yukseklikleri 3x3 ortanca ile yumusat (voksel niceleme / gurultu +-1 katman titremesi)
            var yum = new Dictionary<long, int>(); var tmp = new List<int>(9);
            foreach (var kv in ust)
            {
                HucreCoz(kv.Key, out int ci2, out int cj2); tmp.Clear();
                for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++) if (ust.TryGetValue(OdaSonuc.Anah2(ci2 + di, cj2 + dj), out int tv)) tmp.Add(tv);
                tmp.Sort(); yum[kv.Key] = tmp.Count >= 3 ? tmp[tmp.Count / 2] : kv.Value;
            }
            var ham = ust; ust = yum;
            var etiket = new HashSet<long>(); var sonuc = new List<YKutu>();
            foreach (var kv in ust)
            {
                if (etiket.Contains(kv.Key)) continue;
                var liste = new List<long>(); var yig = new Stack<long>(); yig.Push(kv.Key); etiket.Add(kv.Key);
                while (yig.Count > 0)
                {
                    long c = yig.Pop(); liste.Add(c); HucreCoz(c, out int ci, out int cj); int ct = ust[c];
                    for (int dj = -1; dj <= 1; dj++)
                        for (int di = -1; di <= 1; di++)
                        {
                            if (di == 0 && dj == 0) continue;
                            long a = OdaSonuc.Anah2(ci + di, cj + dj);
                            if (etiket.Contains(a) || !ust.TryGetValue(a, out int at)) continue;
                            if (Math.Abs(at - ct) <= 2) { etiket.Add(a); yig.Push(a); }
                        }
                }
                if (liste.Count * KanitIzgarasi.V * KanitIzgarasi.V < ayar.KutuMinAlan * 0.5f) continue;
                var yk = KutuFit(liste, ham, hucre);
                if (yk != null) sonuc.Add(yk);
            }
            return sonuc;
        }

        YKutu KutuFit(List<long> liste, Dictionary<long, int> ust, Dictionary<long, Hucre> hucre)
        {
            var pts = new List<double[]>(); var tops = new List<int>(); int bin = 0; ushort son = 0;
            foreach (long c in liste)
            {
                HucreCoz(c, out int ix, out int iz);
                pts.Add(new double[] { (ix + 0.5) * KanitIzgarasi.V, (iz + 0.5) * KanitIzgarasi.V }); tops.Add(ust[c]);
                var h = hucre[c]; bin |= h.bin; if (h.son > son) son = h.son;
            }
            var sirali = new List<int>(tops); sirali.Sort();
            int p95 = sirali[(int)(0.95 * (sirali.Count - 1))];
            float yuk = (p95 - zk) * KanitIzgarasi.V;
            if (yuk < ayar.KutuMinYukseklik) return null;
            var hull = OdaGeo.Zarf(pts);
            double phi = OdaGeo.EnKucukDikdortgenAcisi(hull);
            OdaGeo.Dikdortgen(hull, phi, out double cx, out double cz, out double lu, out double lv);
            if (lv > lu) { double t = lu; lu = lv; lv = t; phi += Math.PI / 2; }
            double deg = OdaGeo.Mod180(phi * 180.0 / Math.PI);
            if (manhattanVar)
            {
                double fark = Math.Abs(OdaGeo.Fark90(deg, manhattan));
                if (fark < ayar.ManhattanKutu)
                {
                    double a1 = OdaGeo.Mod180(manhattan), a2 = OdaGeo.Mod180(manhattan + 90.0);
                    double hedef = OdaGeo.Cember180(deg, a1) <= OdaGeo.Cember180(deg, a2) ? a1 : a2;
                    OdaGeo.Dikdortgen(hull, hedef * Math.PI / 180.0, out cx, out cz, out lu, out lv);
                    if (lv > lu) { double t = lu; lu = lv; lv = t; hedef += 90.0; }
                    deg = OdaGeo.Mod180(hedef);
                }
            }
            // kenar bosluklarini kirp: uc sutun/satirlar tepe doluluguna gore cok seyrekse (artik ucan piksel) olcuye katilmaz
            Kirp(pts, deg, ref cx, ref cz, ref lu, ref lv);
            // hucre merkezlerinden olculdugu icin en kucuk bile bir hucre kalinligi tasir; cok ince seritler kutu degildir
            if (lv < ayar.KutuMinKenar || lu * lv < ayar.KutuMinAlan) return null;
            // 5 cm voksel niceleme: ortalama olcum sapmasi +1,5-2,5 cm; sikı kutu icin bu kadar daralt
            lu -= 0.02; lv -= 0.02; yuk -= 0.01f;
            return new YKutu
            {
                k = new OdaKutu { cx = (float)cx, cz = (float)cz, sx = (float)lu, sz = (float)lv, sy = yuk, aci = (float)deg },
                bin = bin, son = son * 0.1f, hucreler = liste.ToArray(), ustler = tops.ToArray()
            };
        }

        static void Kirp(List<double[]> pts, double deg, ref double cx, ref double cz, ref double lu, ref double lv)
        {
            double r = deg * Math.PI / 180.0, c = Math.Cos(r), sn = Math.Sin(r);
            var us = new double[pts.Count]; var vs = new double[pts.Count];
            for (int i = 0; i < pts.Count; i++) { us[i] = pts[i][0] * c + pts[i][1] * sn; vs[i] = -pts[i][0] * sn + pts[i][1] * c; }
            Aralik(us, out double u0, out double u1); Aralik(vs, out double v0, out double v1);
            double uc = (u0 + u1) * 0.5, vc = (v0 + v1) * 0.5;
            cx = uc * c - vc * sn; cz = uc * sn + vc * c; lu = u1 - u0; lv = v1 - v0;
        }

        /// <summary>1B profil: 5 cm kutulara say; uctaki kutular en yogun kutunun %30'undan azsa at.</summary>
        static void Aralik(double[] d, out double mn, out double mx)
        {
            double enK = double.MaxValue, enB = double.MinValue; foreach (double v in d) { if (v < enK) enK = v; if (v > enB) enB = v; }
            int n = (int)Math.Floor((enB - enK) / 0.05) + 1; var say = new int[n]; int tepe = 0;
            foreach (double v in d) { int b = Math.Min(n - 1, (int)Math.Floor((v - enK) / 0.05)); say[b]++; if (say[b] > tepe) tepe = say[b]; }
            int a = 0, z = n - 1;
            while (a < z && say[a] < 0.3 * tepe) a++;
            while (z > a && say[z] < 0.3 * tepe) z--;
            mn = double.MaxValue; mx = double.MinValue;
            foreach (double v in d) { int b = Math.Min(n - 1, (int)Math.Floor((v - enK) / 0.05)); if (b < a || b > z) continue; if (v < mn) mn = v; if (v > mx) mx = v; }
            if (mn > mx) { mn = enK; mx = enB; }
        }

        void KutulariGuncelle(OdaSonuc s, Dictionary<long, Hucre> hucre, float zaman, float dt)
        {
            var yeni = YeniKutular(hucre);
            var cift = new List<float[]>();
            for (int o = 0; o < kutuDurum.Count; o++)
                for (int n = 0; n < yeni.Count; n++)
                {
                    float iou = OdaGeo.IoU(kutuDurum[o].ham, yeni[n].k);
                    if (iou >= 0.3f) cift.Add(new[] { iou, o, n });
                }
            cift.Sort((a, b) => b[0].CompareTo(a[0]));
            var oEs = new bool[kutuDurum.Count]; var nEs = new bool[yeni.Count];
            foreach (var c in cift)
            {
                int o = (int)c[1], n = (int)c[2]; if (oEs[o] || nEs[n]) continue;
                oEs[o] = nEs[n] = true;
                var d = kutuDurum[o]; var y = yeni[n];
                d.kararli = c[0] >= ayar.KutuIoU ? d.kararli + 1 : 1;
                d.ham = y.k; d.kayip = 0; d.bin |= y.bin; d.hucreler = y.hucreler; d.ustler = y.ustler;
                if (y.son >= zaman - 1.5f) d.seen += dt;
                var k = d.k; float r = ayar.YumusakKutu;
                k.cx += (y.k.cx - k.cx) * r; k.cz += (y.k.cz - k.cz) * r;
                double da = OdaGeo.Fark180(y.k.aci, k.aci);
                if (Math.Abs(k.sx - k.sz) < 0.08 && Math.Abs(da) > 45) da -= Math.Sign(da) * 90.0;
                k.aci = (float)OdaGeo.Mod180(k.aci + da * r);
                k.sx += (y.k.sx - k.sx) * r; k.sz += (y.k.sz - k.sz) * r; k.sy += (y.k.sy - k.sy) * r;
            }
            for (int o = kutuDurum.Count - 1; o >= 0; o--)
            {
                if (oEs[o]) continue;
                var d = kutuDurum[o]; d.kayip++;
                if (!d.kesin && d.kayip >= 2) kutuDurum.RemoveAt(o);
                else if (d.kesin && d.kayip >= 2 && SerbestOran(d) >= 0.6) kutuDurum.RemoveAt(o);
            }
            for (int n = 0; n < yeni.Count; n++)
            {
                if (nEs[n]) continue;
                var y = yeni[n];
                kutuDurum.Add(new KutuDurum { id = ++sonKutuId, k = y.k.Kopya(), ham = y.k, kararli = 1, bin = y.bin, seen = y.son >= zaman - 1.5f ? dt : 0f, hucreler = y.hucreler, ustler = y.ustler });
            }
            foreach (var d in kutuDurum)
            {
                int bn = KanitIzgarasi.BinSay(d.bin);
                d.ilerleme = Math.Min(1f, Math.Min(d.kararli / (float)ayar.KutuOnayArdisik, Math.Min(d.seen / ayar.KutuOnaySure, bn / (float)ayar.KutuOnayBin)));
                if (!d.kesin && d.kararli >= ayar.KutuOnayArdisik && d.seen >= ayar.KutuOnaySure && bn >= ayar.KutuOnayBin) { d.kesin = true; d.kesinZaman = zaman; }
                if (d.kesin) d.ilerleme = 1f;
                var c = d.k.Kopya(); c.id = d.id; c.kesin = d.kesin; c.ilerleme = d.ilerleme; c.kesinZaman = d.kesinZaman;
                c.cy = zy + c.sy * 0.5f;
                if (c.sx < c.sz) { float t = c.sx; c.sx = c.sz; c.sz = t; c.aci = (float)OdaGeo.Mod180(c.aci + 90.0); }
                DuvaraYasla(c, s.duvarlar);
                s.kutular.Add(c);
            }
        }

        /// <summary>Duvara 20 cm'den yakin ve ona paralel kenar duvara kadar uzatilir (duvar bandi disarida biraktigi pay).</summary>
        static void DuvaraYasla(OdaKutu k, List<OdaDuvar> duvarlar)
        {
            foreach (var w in duvarlar)
            {
                if (!w.kesin) continue;
                double wl = w.Uzunluk; if (wl < 0.3) continue;
                double wx = (w.bx - w.ax) / wl, wz = (w.bz - w.az) / wl;
                double r = k.aci * Math.PI / 180.0, ux = Math.Cos(r), uz = Math.Sin(r), vx = -uz, vz = ux;
                double crU = wx * uz - wz * ux, crV = wx * vz - wz * vx;
                bool ekseniU; double yari;
                if (Math.Abs(crU) < 0.14) { ekseniU = false; yari = k.sz * 0.5; }      // duvar u'ya paralel: kutunun v kenari duvara bakar
                else if (Math.Abs(crV) < 0.14) { ekseniU = true; yari = k.sx * 0.5; }
                else continue;
                double nx = -wz, nz = wx;
                double dist = (k.cx - w.ax) * nx + (k.cz - w.az) * nz;
                double t = (k.cx - w.ax) * wx + (k.cz - w.az) * wz;
                if (t < -0.1 || t > wl + 0.1) continue;
                double gap = Math.Abs(dist) - yari;
                if (gap <= 0.005 || gap > 0.20) continue;
                double isaret = dist > 0 ? -1 : 1;    // duvara dogru yon
                double ax = ekseniU ? ux : vx, az = ekseniU ? uz : vz;
                double pa = ax * nx + az * nz;        // eksen ile duvar normali hizasi (+-1)
                double yonu = (isaret * pa) >= 0 ? 1 : -1;
                k.cx += (float)(ax * yonu * gap * 0.5); k.cz += (float)(az * yonu * gap * 0.5);
                if (ekseniU) k.sx += (float)gap; else k.sz += (float)gap;
            }
        }

        double SerbestOran(KutuDurum d)
        {
            int adim = Math.Max(1, d.hucreler.Length / 60), top = 0, bos = 0;
            for (int i = 0; i < d.hucreler.Length; i += adim)
            {
                HucreCoz(d.hucreler[i], out int ix, out int iz); top++;
                if (izgara.Durum(ix, d.ustler[i], iz) == 1) bos++;
            }
            return top == 0 ? 1 : bos / (double)top;
        }
    }
}
