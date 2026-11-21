using System;
using System.Collections.Generic;

namespace ArTarama
{
    // =====================================================================================================
    // Etiketli esya kutulari (saf C#). SegFormer'in "sandalye / masa / koltuk / yatak / dolap / ekran / esya"
    // dedigi derinlik noktalari 5 cm voksellerde birikir (KanitIzgarasi.Esya). Kusbakisinda AYNI TURDEN komsu
    // hucreler bir esya sayilir (sandalye masaya degse de ayri kalir), her birine Manhattan yonune yaslanmis
    // en kucuk dikdortgen oturtulur; yukseklik = hucre tepelerinin %95'i. Alti (masa alti) dolu sayilir.
    // =====================================================================================================
    public partial class OdaModeli
    {
        public float ZeminFark;              // teshis: etiketli zemin - ARCore zemini
        public int EsyaMinNokta = 5;           // voksel basina en az etiketli nokta
        public float EsyaMinAlan = 0.04f;      // m2
        public float EsyaMinYukseklik = 0.15f;

        class EsyaDurum
        {
            public int id, tur, gorulme, kayip; public bool kesin; public float kesinZaman, ilk;
            public OdaKutu k;
        }
        readonly List<EsyaDurum> esyaDurum = new List<EsyaDurum>();
        int sonEsyaId = 100000;

        class EHucre { public int ust = int.MinValue; public int n; public int[] oy = new int[OdaEtiket.EsyaSon - OdaEtiket.EsyaIlk + 1]; public int tur = -1; }

        void EsyaGuncelle(OdaSonuc s, float zy0, float zaman)
        {
            if (izgara.Esya.Count == 0) return;
            int iyZ = KanitIzgarasi.Ind(zy0 + 0.03f);
            int iyT = s.tavanGercek ? KanitIzgarasi.Ind(s.tavanY - 0.25f) : KanitIzgarasi.Ind(zy0 + 2.2f);
            var hc = new Dictionary<long, EHucre>();
            foreach (var kv in izgara.Esya)
            {
                var v = kv.Value; if (v[0] < EsyaMinNokta) continue;
                KanitIzgarasi.Coz(kv.Key, out int ix, out int iy, out int iz);
                if (iy < iyZ || iy > iyT) continue;
                // icinden bakilip arkasi gorulmus voksel (yuksek guvenli bos kanit) esya olamaz
                if (izgara.Durum(ix, iy, iz) == 1) continue;
                long a = OdaSonuc.Anah2(ix, iz);
                if (!hc.TryGetValue(a, out var h)) { h = new EHucre(); hc[a] = h; }
                if (iy > h.ust) h.ust = iy;
                h.n += v[0];
                for (int j = 1; j < v.Length; j++) h.oy[j - 1] += v[j];
            }
            foreach (var h in hc.Values) { int en = 0; for (int j = 1; j < h.oy.Length; j++) if (h.oy[j] > h.oy[en]) en = j; h.tur = en; }
            // zemin kaniti agir basan hucre esya degildir (bazi karelerde model zemini esya sanabiliyor)
            var sil = new List<long>();
            foreach (var kv in hc)
            {
                izgara.ZeminHucre.TryGetValue(kv.Key, out int zn);
                if (zn > kv.Value.n) sil.Add(kv.Key);
            }
            // kullanicinin yurudugu yerde (kamera yolu +-30 cm) esya olamaz
            var yol = new HashSet<long>();
            for (int q = 0; q < izX.Count; q++)
            {
                int qx = KanitIzgarasi.Ind((float)izX[q]), qz = KanitIzgarasi.Ind((float)izZ[q]);
                for (int dz = -6; dz <= 6; dz++) for (int dx = -6; dx <= 6; dx++) if (dx * dx + dz * dz <= 36) yol.Add(OdaSonuc.Anah2(qx + dx, qz + dz));
            }
            foreach (var kv in hc) if (yol.Contains(kv.Key)) sil.Add(kv.Key);
            // duvarin disinda ya da duvar cizgisine 10 cm'den yakin hucreler (duvar gurultusu) atilir
            var kd = new List<OdaDuvar>(); foreach (var d in s.duvarlar) if (d.kesin) kd.Add(d);
            foreach (var kv in hc)
            {
                HucreXZ(kv.Key, out double hx, out double hz);
                if (DuvarDisinda(kd, hx, hz, 0.10)) sil.Add(kv.Key);
            }
            foreach (long k in sil) hc.Remove(k);

            // ayni turden bilesenler; 10 cm'lik bosluklar ayni esya sayilir (Chebyshev 2)
            var gezildi = new HashSet<long>();
            var gruplar = new List<List<long>>(); var grupTur = new List<int>();
            foreach (var kv in hc)
            {
                if (gezildi.Contains(kv.Key)) continue;
                int tur = kv.Value.tur;
                var yigin = new Stack<long>(); yigin.Push(kv.Key); gezildi.Add(kv.Key);
                var hucreler = new List<long>();
                while (yigin.Count > 0)
                {
                    long k = yigin.Pop(); hucreler.Add(k);
                    int ix = (int)((k >> 21) & 0x1FFFFF) - 1048576, iz = (int)(k & 0x1FFFFF) - 1048576;
                    for (int dz = -2; dz <= 2; dz++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            if (dx == 0 && dz == 0) continue;
                            long q = OdaSonuc.Anah2(ix + dx, iz + dz);
                            if (gezildi.Contains(q) || !hc.TryGetValue(q, out var hq) || hq.tur != tur) continue;
                            gezildi.Add(q); yigin.Push(q);
                        }
                }
                gruplar.Add(hucreler); grupTur.Add(tur);
            }
            double aci0 = s.yonVar ? s.baskinAci : double.NaN;
            var kutuG = new List<OdaKutu>(); var hucG = new List<List<long>>();
            for (int g = 0; g < gruplar.Count; g++)
            {
                var k = KutuOtur(hc, gruplar[g], grupTur[g], zy0, aci0);
                if (k == null) continue; kutuG.Add(k); hucG.Add(gruplar[g]);
            }
            // ayni turden ust uste binen kutular tek esyadir (kucugunun %20'si ortusuyorsa birlestir)
            for (bool degisti = true; degisti;)
            {
                degisti = false;
                for (int i = 0; i < kutuG.Count && !degisti; i++)
                    for (int j = i + 1; j < kutuG.Count && !degisti; j++)
                    {
                        if (kutuG[i].tur != kutuG[j].tur || OdaGeo.KucugeOran(kutuG[i], kutuG[j]) < 0.2f) continue;
                        var birlesik = new List<long>(hucG[i]); birlesik.AddRange(hucG[j]);
                        var k = KutuOtur(hc, birlesik, kutuG[i].tur, zy0, aci0);
                        if (k == null) continue;
                        kutuG[i] = k; hucG[i] = birlesik; kutuG.RemoveAt(j); hucG.RemoveAt(j); degisti = true;
                    }
            }
            var yeni = new List<OdaKutu>();
            foreach (var k in kutuG) if (!DuvarDisinda(kd, k.cx, k.cz, 0.0)) yeni.Add(k);

            // ---------- takip: ayni tur + IoU / yakin merkez ----------
            var es = new bool[esyaDurum.Count];
            int eskiSay = esyaDurum.Count; var eklenen = new List<EsyaDurum>();
            for (int n = 0; n < yeni.Count; n++)
            {
                var y = yeni[n]; int en = -1; double enS = 0;
                for (int i = 0; i < eskiSay; i++)
                {
                    if (es[i] || esyaDurum[i].tur != y.tur) continue;
                    double iou = OdaGeo.IoU(esyaDurum[i].k, y);
                    double dm = Math.Sqrt((esyaDurum[i].k.cx - y.cx) * (esyaDurum[i].k.cx - y.cx) + (esyaDurum[i].k.cz - y.cz) * (esyaDurum[i].k.cz - y.cz));
                    double sk = iou > 0.1 ? iou : (dm < 0.3 ? 0.1 : 0);
                    if (sk > enS) { enS = sk; en = i; }
                }
                if (en < 0) { eklenen.Add(new EsyaDurum { id = ++sonEsyaId, tur = y.tur, gorulme = 1, ilk = zaman, k = y }); continue; }
                var d = esyaDurum[en]; es[en] = true; d.gorulme++; d.kayip = 0;
                // yumusatma: buyume hemen, kuculme ve kayma yavas
                var o = d.k; float a = 0.5f;
                o.cx += (y.cx - o.cx) * a; o.cz += (y.cz - o.cz) * a;
                o.sx = y.sx > o.sx ? y.sx : o.sx + (y.sx - o.sx) * 0.3f; o.sz = y.sz > o.sz ? y.sz : o.sz + (y.sz - o.sz) * 0.3f;
                o.sy = y.sy > o.sy ? y.sy : o.sy + (y.sy - o.sy) * 0.3f; o.cy = zy0 + o.sy * 0.5f;
                if (OdaGeo.Cember180(o.aci, y.aci) > 5) o.aci = y.aci;
            }
            for (int i = es.Length - 1; i >= 0; i--)
            {
                if (es[i]) continue;
                var d = esyaDurum[i]; d.kayip++;
                if ((!d.kesin && d.kayip >= 2) || d.kayip >= 8) esyaDurum.RemoveAt(i);
            }
            esyaDurum.AddRange(eklenen);
            foreach (var d in esyaDurum)
                if (!d.kesin && d.gorulme >= 3 && zaman - d.ilk >= 2.5f) { d.kesin = true; d.kesinZaman = zaman; }

            foreach (var d in esyaDurum)
            {
                var c = d.k.Kopya(); c.id = d.id; c.kesin = d.kesin; c.kesinZaman = d.kesinZaman; c.tur = d.tur;
                c.ilerleme = d.kesin ? 1f : Math.Min(0.6f, d.gorulme / 3f);
                s.kutular.Add(c);
            }
        }

        static void HucreXZ(long k, out double x, out double z)
        {
            int ix = (int)((k >> 21) & 0x1FFFFF) - 1048576, iz = (int)(k & 0x1FFFFF) - 1048576;
            x = (ix + 0.5) * KanitIzgarasi.V; z = (iz + 0.5) * KanitIzgarasi.V;
        }

        OdaKutu KutuOtur(Dictionary<long, EHucre> hc, List<long> hucreler, int tur, float zy0, double aci0)
        {
            int nTop = 0; var ustler = new List<int>(hucreler.Count);
            foreach (long k in hucreler) { var h = hc[k]; nTop += h.n; ustler.Add(h.ust); }
            double alan = hucreler.Count * KanitIzgarasi.V * KanitIzgarasi.V;
            if (alan < EsyaMinAlan || nTop < 40) return null;
            var pts = new List<double[]>(hucreler.Count * 4); double V = KanitIzgarasi.V;
            foreach (long k in hucreler)
            {
                int ix = (int)((k >> 21) & 0x1FFFFF) - 1048576, iz = (int)(k & 0x1FFFFF) - 1048576;
                double x0 = ix * V, z0 = iz * V;
                pts.Add(new[] { x0, z0 }); pts.Add(new[] { x0 + V, z0 }); pts.Add(new[] { x0, z0 + V }); pts.Add(new[] { x0 + V, z0 + V });
            }
            var hull = OdaGeo.Zarf(pts);
            double phi = OdaGeo.EnKucukDikdortgenAcisi(hull);
            if (!double.IsNaN(aci0))
            {
                double pd = phi * 180.0 / Math.PI, f = OdaGeo.Fark90(pd, aci0);
                if (Math.Abs(f) <= 15.0) phi = (pd - f) * Math.PI / 180.0;
            }
            OdaGeo.Dikdortgen(hull, phi, out double cx, out double cz, out double lu, out double lv);
            lu = Math.Max(V, lu - V * 0.5); lv = Math.Max(V, lv - V * 0.5);
            ustler.Sort();
            int p95 = ustler[(int)(0.75 * (ustler.Count - 1))];   // ust yuzeyin cogunlugu (tek tuk yuksek gurultu degil)
            float yuk = (p95 + 1) * KanitIzgarasi.V - zy0;
            if (yuk < EsyaMinYukseklik) return null;
            double aciDeg = OdaGeo.Mod180(phi * 180.0 / Math.PI);
            if (lu < lv) { double t = lu; lu = lv; lv = t; aciDeg = OdaGeo.Mod180(aciDeg + 90.0); }
            return new OdaKutu { cx = (float)cx, cz = (float)cz, sx = (float)lu, sz = (float)lv, sy = yuk, cy = zy0 + yuk * 0.5f, aci = (float)aciDeg, tur = tur };
        }

        /// <summary>Nokta, bir kesin duvarin oda disi (normalin tersi) tarafinda ve o duvarin boyu icinde mi?</summary>
        static bool DuvarDisinda(List<OdaDuvar> kd, double x, double z, double pay = 0.05)
        {
            foreach (var d in kd)
            {
                double dx = d.bx - d.ax, dz = d.bz - d.az, L2 = dx * dx + dz * dz; if (L2 < 1e-6) continue;
                double t = ((x - d.ax) * dx + (z - d.az) * dz) / L2; if (t < 0 || t > 1) continue;
                double icMesafe = (x - d.ax) * d.nx + (z - d.az) * d.nz;
                if (icMesafe < pay) return true;
            }
            return false;
        }
    }
}
