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
            foreach (long k in sil) hc.Remove(k);

            // duvarin disinda kalan hucreler (oda disi) atilir
            var kd = new List<OdaDuvar>(); foreach (var d in s.duvarlar) if (d.kesin) kd.Add(d);

            // ayni turden 8-komsu bilesenler
            var gezildi = new HashSet<long>(); var yeni = new List<OdaKutu>(); var yeniTur = new List<int>();
            double aci0 = s.yonVar ? s.baskinAci : double.NaN;
            foreach (var kv in hc)
            {
                if (gezildi.Contains(kv.Key)) continue;
                int tur = kv.Value.tur;
                var yigin = new Stack<long>(); yigin.Push(kv.Key); gezildi.Add(kv.Key);
                var hucreler = new List<long>(); var ustler = new List<int>(); int nTop = 0;
                while (yigin.Count > 0)
                {
                    long k = yigin.Pop(); var h = hc[k]; hucreler.Add(k); ustler.Add(h.ust); nTop += h.n;
                    int ix = (int)((k >> 21) & 0x1FFFFF) - 1048576, iz = (int)(k & 0x1FFFFF) - 1048576;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dz == 0) continue;
                            long q = OdaSonuc.Anah2(ix + dx, iz + dz);
                            if (gezildi.Contains(q) || !hc.TryGetValue(q, out var hq) || hq.tur != tur) continue;
                            gezildi.Add(q); yigin.Push(q);
                        }
                }
                double alan = hucreler.Count * KanitIzgarasi.V * KanitIzgarasi.V;
                if (alan < EsyaMinAlan || nTop < 40) continue;
                // dikdortgen: hucre kose noktalarinin zarfi
                var pts = new List<double[]>(hucreler.Count * 4);
                double mx = 0, mz = 0;
                foreach (long k in hucreler)
                {
                    int ix = (int)((k >> 21) & 0x1FFFFF) - 1048576, iz = (int)(k & 0x1FFFFF) - 1048576;
                    double x0 = ix * KanitIzgarasi.V, z0 = iz * KanitIzgarasi.V, V = KanitIzgarasi.V;
                    pts.Add(new[] { x0, z0 }); pts.Add(new[] { x0 + V, z0 }); pts.Add(new[] { x0, z0 + V }); pts.Add(new[] { x0 + V, z0 + V });
                    mx += x0 + V * 0.5; mz += z0 + V * 0.5;
                }
                mx /= hucreler.Count; mz /= hucreler.Count;
                if (DuvarDisinda(kd, mx, mz)) continue;
                var hull = OdaGeo.Zarf(pts);
                double phi = OdaGeo.EnKucukDikdortgenAcisi(hull);
                if (!double.IsNaN(aci0))
                {
                    double pd = phi * 180.0 / Math.PI, f = OdaGeo.Fark90(pd, aci0);
                    if (Math.Abs(f) <= 15.0) phi = (pd - f) * Math.PI / 180.0;
                }
                OdaGeo.Dikdortgen(hull, phi, out double cx, out double cz, out double lu, out double lv);
                // hucre kenarina kadar sisme payi: her yandan yarim hucre kadar daralt
                lu = Math.Max(KanitIzgarasi.V, lu - KanitIzgarasi.V * 0.5); lv = Math.Max(KanitIzgarasi.V, lv - KanitIzgarasi.V * 0.5);
                ustler.Sort();
                int p95 = ustler[(int)(0.95 * (ustler.Count - 1))];
                float yuk = (p95 + 1) * KanitIzgarasi.V - zy0;
                if (yuk < EsyaMinYukseklik) continue;
                double aciDeg = OdaGeo.Mod180(phi * 180.0 / Math.PI);
                if (lu < lv) { double t = lu; lu = lv; lv = t; aciDeg = OdaGeo.Mod180(aciDeg + 90.0); }
                yeni.Add(new OdaKutu { cx = (float)cx, cz = (float)cz, sx = (float)lu, sz = (float)lv, sy = yuk, cy = zy0 + yuk * 0.5f, aci = (float)aciDeg, tur = tur });
                yeniTur.Add(tur);
            }

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

        /// <summary>Nokta, bir kesin duvarin oda disi (normalin tersi) tarafinda ve o duvarin boyu icinde mi?</summary>
        static bool DuvarDisinda(List<OdaDuvar> kd, double x, double z)
        {
            foreach (var d in kd)
            {
                double dx = d.bx - d.ax, dz = d.bz - d.az, L2 = dx * dx + dz * dz; if (L2 < 1e-6) continue;
                double t = ((x - d.ax) * dx + (z - d.az) * dz) / L2; if (t < 0 || t > 1) continue;
                double icMesafe = (x - d.ax) * d.nx + (z - d.az) * d.nz;
                if (icMesafe < -0.05) return true;
            }
            return false;
        }
    }
}
