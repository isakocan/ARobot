using System;
using System.Collections.Generic;

namespace ArTarama
{
    // =====================================================================================================
    // Etiketli esya kutulari v2 (saf C#). Gurultulu noktalari dunyada kumelemek yerine (masa parcalaniyor, kutular
    // birbirine yapisiyordu) her etiketli karede esyalar GORUNTUDE ayrilir: ayni etiketli, derinligi kesintisiz
    // komsu pikseller bir "gozlem"dir. Gozlemler kareler boyunca ayni yerdeki esyaya (ornek) eklenir.
    //   * Masa / tezgah: ust yuzeyi ARCore'un yatay duzlemi (cok kararli) -> kutu = duzlemin dikdortgeni, yukseklik = duzlem.
    //   * Diger esyalar: ornegin ayak izi (birden cok karede gorulen hucreler) + ture gore makul boyut siniri
    //     (RoomPlan da sandalyeye hazir kalip boyut koyar). Yukseklik = gozlem tepelerinin medyani.
    // Ornek kimligi kalicidir: ekranda kutu titremez, sayisi kendiliginden artip azalmaz.
    // =====================================================================================================
    public partial class OdaModeli
    {
        public float ZeminFark;                 // teshis: etiketli zemin - ARCore zemini

        const int GozlemMinPiksel = 10;         // 80x45 izgarada
        const int OrnekMinGozlem = 4;
        const float OrnekMinSure = 1.5f;

        class Ornek
        {
            public int id, tur, gozlem; public float ilk, son;
            public readonly Dictionary<long, int> hucre = new Dictionary<long, int>();   // 5 cm hucre -> kac gozlemde goruldu
            public readonly List<float> ust = new List<float>(), alt = new List<float>();
            public bool kesin; public float kesinZaman;
            public int birlesti = -1;           // baska ornege katildiysa onun id'si
        }
        readonly List<Ornek> ornekler = new List<Ornek>();
        int sonOrnekId = 100000;
        bool zeminBilinen;

        // ------------------------------------------------------------------------------------------ kare basina
        readonly float[] omat = new float[9];
        int[] oEtiket = new int[0]; float[] oX = new float[0], oY = new float[0], oZ = new float[0];

        void GozlemleriCikar(DerinlikKaresi f)
        {
            if (f.e == null) return;
            int w = f.w, h = f.h, n = w * h;
            if (oEtiket.Length < n) { oEtiket = new int[n]; oX = new float[n]; oY = new float[n]; oZ = new float[n]; }
            DerinlikKaresi.DonmeMatrisi(f.qx, f.qy, f.qz, f.qw, omat);
            for (int i = 0; i < n; i++)
            {
                oEtiket[i] = -1;
                byte e = f.e[i]; float d = f.d[i];
                if (e < OdaEtiket.EsyaIlk || e > OdaEtiket.EsyaSon || !(d >= 0.3f && d <= 4.5f)) continue;
                int u = i % w, v = i / w;
                f.YerelYon(u, v, out float lx, out float ly);
                float wx = omat[0] * lx + omat[1] * ly + omat[2], wy = omat[3] * lx + omat[4] * ly + omat[5], wz = omat[6] * lx + omat[7] * ly + omat[8];
                float y = f.py + wy * d;
                if (zeminBilinen && y < zy + 0.03f) continue;
                oX[i] = f.px + wx * d; oY[i] = y; oZ[i] = f.pz + wz * d; oEtiket[i] = e;
            }
            var yigin = new Stack<int>(); var piks = new List<int>();
            for (int i0 = 0; i0 < n; i0++)
            {
                if (oEtiket[i0] < 0) continue;
                int e = oEtiket[i0]; piks.Clear(); yigin.Push(i0); oEtiket[i0] = -2 - e;   // gezildi isareti
                while (yigin.Count > 0)
                {
                    int i = yigin.Pop(); piks.Add(i);
                    int u = i % w, v = i / w; float di = f.d[i];
                    for (int k = 0; k < 4; k++)
                    {
                        int uu = u + (k == 0 ? 1 : k == 1 ? -1 : 0), vv = v + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (uu < 0 || vv < 0 || uu >= w || vv >= h) continue;
                        int j = vv * w + uu; if (oEtiket[j] != e) continue;
                        // derinlik kesintisi = farkli esya (ondeki sandalye ile arkadaki masa)
                        if (Math.Abs(f.d[j] - di) > 0.12f * di + 0.05f) continue;
                        oEtiket[j] = -2 - e; yigin.Push(j);
                    }
                }
                if (piks.Count >= GozlemMinPiksel) GozlemEkle(e - OdaEtiket.EsyaIlk, piks, f.zaman);
            }
        }

        void GozlemEkle(int tur, List<int> piks, float zaman)
        {
            var hc = new HashSet<long>(); var ys = new List<float>(piks.Count);
            foreach (int i in piks) { hc.Add(OdaSonuc.Anah2(KanitIzgarasi.Ind(oX[i]), KanitIzgarasi.Ind(oZ[i]))); ys.Add(oY[i]); }
            ys.Sort();
            float ust = ys[(int)(0.9 * (ys.Count - 1))], alt = ys[(int)(0.1 * (ys.Count - 1))];
            // eslesme: ayni tur, hucrelerin bir kismi ornegin hucreleriyle (1 hucre pay) cakisiyor
            Ornek en = null; double enS = 0;
            foreach (var o in ornekler)
            {
                if (o.birlesti >= 0 || o.tur != tur) continue;
                int c = 0;
                foreach (long k in hc) if (KomsuVar(o.hucre, k)) c++;
                double s = c / (double)hc.Count;
                if (s > enS) { enS = s; en = o; }
            }
            if (en == null || enS < 0.25)
            {
                en = new Ornek { id = ++sonOrnekId, tur = tur, ilk = zaman };
                ornekler.Add(en);
            }
            en.gozlem++; en.son = zaman;
            foreach (long k in hc) { en.hucre.TryGetValue(k, out int c); en.hucre[k] = c + 1; }
            if (en.ust.Count < 400) { en.ust.Add(ust); en.alt.Add(alt); }
            else { int r = rnd.Next(400); en.ust[r] = ust; en.alt[r] = alt; }
        }

        static bool KomsuVar(Dictionary<long, int> d, long k)
        {
            int ix = (int)((k >> 21) & 0x1FFFFF) - 1048576, iz = (int)(k & 0x1FFFFF) - 1048576;
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) if (d.ContainsKey(OdaSonuc.Anah2(ix + dx, iz + dz))) return true;
            return false;
        }

        // ------------------------------------------------------------------------------------------ yapi guncellemesi
        // ture gore makul boyut: uzun kenar alt-ust, kisa kenar alt-ust, yukseklik alt-ust (metre)
        static readonly float[][] Kalip =
        {
            new[] { 0.40f, 0.75f, 0.40f, 0.70f, 0.70f, 1.10f },   // sandalye
            new[] { 0.50f, 4.00f, 0.40f, 2.00f, 0.55f, 1.00f },   // masa (duzlemsiz)
            new[] { 0.40f, 3.00f, 0.40f, 1.80f, 0.55f, 1.10f },   // koltuk
            new[] { 0.40f, 2.30f, 0.40f, 2.00f, 0.35f, 1.30f },   // yatak (yanlis tanimada zemini sisirmesin diye alt sinir kucuk)
            new[] { 0.30f, 3.00f, 0.25f, 0.80f, 0.40f, 2.40f },   // dolap/raf
            new[] { 0.20f, 2.00f, 0.03f, 0.40f, 0.20f, 1.40f },   // ekran
            new[] { 0.15f, 2.00f, 0.15f, 2.00f, 0.15f, 2.00f },   // diger
        };

        void EsyaGuncelle(OdaSonuc s, float zy0, IList<OdaDuzlem> planlar, float zaman)
        {
            zeminBilinen = true;
            double aci0 = s.yonVar ? s.baskinAci : double.NaN;
            var kd = new List<OdaDuvar>(); foreach (var d in s.duvarlar) if (d.kesin) kd.Add(d);
            var yol = YolHucreleri(0.30);

            // --- once ayri gorulmus ayni esya: kucugunun hucrelerinin %40'i ortaksa birlestir
            for (int i = 0; i < ornekler.Count; i++)
            {
                var a = ornekler[i]; if (a.birlesti >= 0) continue;
                for (int j = i + 1; j < ornekler.Count; j++)
                {
                    var b = ornekler[j]; if (b.birlesti >= 0 || b.tur != a.tur) continue;
                    var kucuk = a.hucre.Count < b.hucre.Count ? a : b; var buyuk = kucuk == a ? b : a;
                    int c = 0; foreach (long k in kucuk.hucre.Keys) if (KomsuVar(buyuk.hucre, k)) c++;
                    if (c < 0.4 * kucuk.hucre.Count) continue;
                    foreach (var kv in b.hucre) { a.hucre.TryGetValue(kv.Key, out int x); a.hucre[kv.Key] = x + kv.Value; }
                    a.ust.AddRange(b.ust); a.alt.AddRange(b.alt); a.gozlem += b.gozlem;
                    a.ilk = Math.Min(a.ilk, b.ilk); a.son = Math.Max(a.son, b.son); a.kesin |= b.kesin;
                    b.birlesti = a.id;
                }
            }
            ornekler.RemoveAll(o => o.birlesti >= 0);

            // --- masa ustleri: zeminden 0,35-1,2 m yukarida yatay ARCore duzlemleri. Duzlem sinirlari zamanla sisebilir:
            //     5 cm hucrelere dokulur, yurunen yol ve duvar disi kirpilir, kalan her bagli parca bir masa olur.
            var masalar = new List<OdaKutu>();
            // masa / koltuk / yatak etiketli hucreler (en az 2 gozlemde), sandalyenin baskin oldugu hucreler haric
            var masaHucre = new Dictionary<long, int>(); var sandHucre = new Dictionary<long, int>();
            foreach (var o in ornekler)
                foreach (var kv in o.hucre)
                {
                    if (kv.Value < 2) continue;
                    var hedef = o.tur == 0 ? sandHucre : (o.tur == 1 || o.tur == 2 || o.tur == 3) ? masaHucre : null;
                    if (hedef == null) continue;
                    hedef.TryGetValue(kv.Key, out int c); hedef[kv.Key] = c + kv.Value;
                }
            foreach (var kv in sandHucre) if (masaHucre.TryGetValue(kv.Key, out int mc) && kv.Value > mc) masaHucre.Remove(kv.Key);
            foreach (var p in planlar)
            {
                if (p.tur != 0 || p.sinir == null || p.sinir.Length < 9) continue;
                float y = OrtY(p.sinir), yk = y - zy0;
                if (yk < 0.35f || yk > 1.2f) continue;
                if (OdaGeo.PolyAlan(p.sinir) < 0.12f) continue;
                var hucre = new HashSet<long>();
                int kat = KanitIzgarasi.SutunKatmani(y);
                foreach (long k in PoligonHucreleri(p.sinir))
                {
                    if (yol.Contains(k)) continue;
                    // duzlemin bu hucresinde masa yuksekliginde gercekten derinlik noktasi gorulmus olmali
                    // (ARCore duzlemi zamanla bos zeminin ustune de yayilabiliyor); zemin goruluyorsa masa yok
                    int ns = 0;
                    if (izgara.Sutun.TryGetValue(k, out var sh)) for (int q = Math.Max(0, kat - 1); q <= Math.Min(sh.Length - 1, kat + 1); q++) ns += sh[q];
                    if (ns < 3) continue;
                    izgara.ZeminHucre.TryGetValue(k, out int zn); if (zn > 3 * ns) continue;
                    // masa yuksekligindeki sandalye arkaliklari da nokta verir: etiket varsa hucre masa etiketli olmali
                    if (masaHucre.Count > 0 && !KomsuVar(masaHucre, k)) continue;
                    HucreXZ(k, out double hx, out double hz);
                    if (DuvarDisinda(kd, hx, hz, 0.05)) continue;
                    hucre.Add(k);
                }
                foreach (var parca in Bilesenler(hucre))
                {
                    if (parca.Count * KanitIzgarasi.V * KanitIzgarasi.V < 0.12) continue;
                    var k = Dikdortgenle(OdaGeo.Zarf(HucreKoseleri(parca)), aci0, 0, 45.0);   // masa: oda yonune yaslanir
                    if (k == null) continue;
                    // turu: altindaki etiketli esyalar (sandalye oturagi da yatay duzlemdir -> sandalyeye birak)
                    var oy = new int[Kalip.Length];
                    foreach (var o in ornekler)
                        foreach (var kv in o.hucre) if (parca.Contains(kv.Key)) oy[o.tur] += kv.Value;
                    int enT = -1; for (int t = 0; t < oy.Length; t++) if (oy[t] > 0 && (enT < 0 || oy[t] > oy[enT])) enT = t;
                    if (enT == 0 || enT == 4 || enT == 5) continue;
                    if (enT < 0 && parca.Count * KanitIzgarasi.V * KanitIzgarasi.V < 0.25) continue;
                    k.tur = enT < 0 ? 1 : enT;
                    k.sy = yk; k.cy = zy0 + yk * 0.5f;
                    masalar.Add(k);
                }
            }
            // ayni masanin parcalari (ARCore bazen bir masaya iki duzlem verir)
            for (bool degisti = true; degisti;)
            {
                degisti = false;
                for (int i = 0; i < masalar.Count && !degisti; i++)
                    for (int j = i + 1; j < masalar.Count && !degisti; j++)
                    {
                        if (Math.Abs(masalar[i].sy - masalar[j].sy) > 0.08f || OdaGeo.KucugeOran(masalar[i], masalar[j]) < 0.1f) continue;
                        var pts = new List<double[]>(); var c = new float[8];
                        foreach (var m in new[] { masalar[i], masalar[j] }) { m.Koseler(c); for (int q = 0; q < 4; q++) pts.Add(new double[] { c[q * 2], c[q * 2 + 1] }); }
                        var k = Dikdortgenle(OdaGeo.Zarf(pts), aci0, 0, 45.0); if (k == null) continue;
                        k.tur = masalar[i].tur; k.sy = Math.Max(masalar[i].sy, masalar[j].sy); k.cy = zy0 + k.sy * 0.5f;
                        masalar[i] = k; masalar.RemoveAt(j); degisti = true;
                    }
            }

            // --- ornekler -> kutular
            var cikis = new List<KeyValuePair<Ornek, OdaKutu>>();
            foreach (var o in ornekler)
            {
                if (o.gozlem < OrnekMinGozlem || o.son - o.ilk < OrnekMinSure) continue;
                int mx = 0; foreach (int c in o.hucre.Values) if (c > mx) mx = c;
                int esik = Math.Max(2, (int)Math.Ceiling(0.3 * mx));
                var pts = new List<double[]>(); int say = 0;
                foreach (var kv in o.hucre)
                {
                    if (kv.Value < esik || yol.Contains(kv.Key)) continue;
                    izgara.ZeminHucre.TryGetValue(kv.Key, out int zn); if (zn > 4 * kv.Value) continue;
                    HucreXZ(kv.Key, out double hx, out double hz);
                    if (DuvarDisinda(kd, hx, hz, 0.05)) continue;
                    if (o.tur == 1) { bool ic = false; foreach (var m in masalar) if (OdaSonuc.KutuMesafesi(m, (float)hx, (float)hz) <= 0.05f) { ic = true; break; } if (ic) continue; }
                    double V = KanitIzgarasi.V, x0 = hx - V * 0.5, z0 = hz - V * 0.5;
                    pts.Add(new[] { x0, z0 }); pts.Add(new[] { x0 + V, z0 }); pts.Add(new[] { x0, z0 + V }); pts.Add(new[] { x0 + V, z0 + V });
                    say++;
                }
                if (say < 4) continue;
                var k = Dikdortgenle(OdaGeo.Zarf(pts), aci0, KanitIzgarasi.V * 0.5);
                if (k == null) continue;
                var kl = Kalip[o.tur];
                k.sx = Sinirla(k.sx, kl[0], kl[1]); k.sz = Sinirla(k.sz, kl[2], kl[3]);
                if (k.sz > k.sx) { float t = k.sx; k.sx = k.sz; k.sz = t; k.aci = (float)OdaGeo.Mod180(k.aci + 90.0); }
                float ust = Medyan(o.ust), alt = Medyan(o.alt);
                if (s.tavanGercek) ust = Math.Min(ust, s.tavanY - 0.05f);
                bool havada = (o.tur == 5 || o.tur == 6) && alt - zy0 > 0.3f;   // masa ustundeki ekran, duvardaki TV
                if (havada) continue;   // kat plani: zemine degmeyen esya (masa ustu ekran, duvar TV) zemini kapatmaz
                { float yk = Sinirla(ust - zy0, kl[4], kl[5]); k.sy = yk; k.cy = zy0 + yk * 0.5f; }
                if (DuvarDisinda(kd, k.cx, k.cz, 0.0)) continue;
                k.tur = o.tur; k.id = o.id;
                cikis.Add(new KeyValuePair<Ornek, OdaKutu>(o, k));
            }

            // --- kat plani temizligi: buyuk kismi baska bir kutunun icinde kalan kutu ayri bir zemin isgali degildir
            var hepsi = new List<OdaKutu>(masalar); foreach (var kv in cikis) hepsi.Add(kv.Value);
            var at = new HashSet<OdaKutu>();
            for (int i = 0; i < hepsi.Count; i++)
                for (int j = 0; j < hepsi.Count; j++)
                {
                    if (i == j || at.Contains(hepsi[i]) || at.Contains(hepsi[j])) continue;
                    var a1 = hepsi[i]; var b1 = hepsi[j];
                    bool aKucuk = a1.sx * a1.sz < b1.sx * b1.sz || (a1.sx * a1.sz == b1.sx * b1.sz && i > j);
                    if (aKucuk && OdaGeo.KucugeOran(a1, b1) > 0.5f) at.Add(a1);
                }
            masalar.RemoveAll(m => at.Contains(m)); cikis.RemoveAll(kv => at.Contains(kv.Value));
            foreach (var m in masalar) DuvardanIceIt(kd, m);
            foreach (var kv in cikis) DuvardanIceIt(kd, kv.Value);

            // --- cikti (ornek kimligi = kutu kimligi; masa kimligi ayni yerdeki onceki masadan)
            var kullanilan = new HashSet<int>(); int mid = 0;
            foreach (var m in masalar)
            {
                m.id = -1;
                foreach (var e in oncekiMasa) if (!kullanilan.Contains(e.id) && OdaGeo.KucugeOran(e, m) > 0.3f) { m.id = e.id; m.kesinZaman = e.kesinZaman; break; }
                if (m.id < 0) { do m.id = 900000 + (sonMasaId++); while (kullanilan.Contains(m.id)); m.kesinZaman = zaman; }
                kullanilan.Add(m.id); m.kesin = true; m.ilerleme = 1f; mid++;
                s.kutular.Add(m);
            }
            oncekiMasa.Clear(); foreach (var m in masalar) oncekiMasa.Add(m.Kopya());
            foreach (var kv in cikis)
            {
                var o = kv.Key; var k = kv.Value;
                if (!o.kesin) { o.kesin = true; o.kesinZaman = zaman; }
                k.kesin = true; k.ilerleme = 1f; k.kesinZaman = o.kesinZaman;
                s.kutular.Add(k);
            }
        }
        readonly List<OdaKutu> oncekiMasa = new List<OdaKutu>();
        int sonMasaId;

        HashSet<long> YolHucreleri(double r)
        {
            var yol = new HashSet<long>(); int ri = (int)Math.Round(r / KanitIzgarasi.V), r2 = ri * ri;
            for (int q = 0; q < izX.Count; q++)
            {
                int qx = KanitIzgarasi.Ind((float)izX[q]), qz = KanitIzgarasi.Ind((float)izZ[q]);
                for (int dz = -ri; dz <= ri; dz++) for (int dx = -ri; dx <= ri; dx++) if (dx * dx + dz * dz <= r2) yol.Add(OdaSonuc.Anah2(qx + dx, qz + dz));
            }
            return yol;
        }

        /// <summary>Kutu bir kesin duvarin disina tasiyorsa (duvar boyu icinde) duvar normali boyunca iceri kaydirilir.</summary>
        static void DuvardanIceIt(List<OdaDuvar> kd, OdaKutu k)
        {
            var c = new float[8];
            foreach (var d in kd)
            {
                double dx = d.bx - d.ax, dz = d.bz - d.az, L2 = dx * dx + dz * dz; if (L2 < 1e-6) continue;
                double t = ((k.cx - d.ax) * dx + (k.cz - d.az) * dz) / L2; if (t < 0 || t > 1) continue;
                double ic = (k.cx - d.ax) * d.nx + (k.cz - d.az) * d.nz; if (ic < 0 || ic > 1.5) continue;
                k.Koseler(c); double en = 0;
                for (int q = 0; q < 4; q++) { double m = (c[q * 2] - d.ax) * d.nx + (c[q * 2 + 1] - d.az) * d.nz; if (m < en) en = m; }
                if (en < 0) { k.cx -= (float)(en * d.nx); k.cz -= (float)(en * d.nz); }
            }
        }

        /// <summary>x,y,z ucluleri sinirindaki poligonun icine merkezi dusen 5 cm hucreler.</summary>
        static List<long> PoligonHucreleri(float[] p)
        {
            int m = p.Length / 3; float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            for (int i = 0; i < m; i++) { x0 = Math.Min(x0, p[i * 3]); x1 = Math.Max(x1, p[i * 3]); z0 = Math.Min(z0, p[i * 3 + 2]); z1 = Math.Max(z1, p[i * 3 + 2]); }
            var r = new List<long>();
            for (int iz = KanitIzgarasi.Ind(z0); iz <= KanitIzgarasi.Ind(z1); iz++)
                for (int ix = KanitIzgarasi.Ind(x0); ix <= KanitIzgarasi.Ind(x1); ix++)
                {
                    double x = (ix + 0.5) * KanitIzgarasi.V, z = (iz + 0.5) * KanitIzgarasi.V; bool ic = false;
                    for (int i = 0, j = m - 1; i < m; j = i++)
                    {
                        double xi = p[i * 3], zi = p[i * 3 + 2], xj = p[j * 3], zj = p[j * 3 + 2];
                        if ((zi > z) != (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi) ic = !ic;
                    }
                    if (ic) r.Add(OdaSonuc.Anah2(ix, iz));
                }
            return r;
        }

        static List<HashSet<long>> Bilesenler(HashSet<long> h)
        {
            var r = new List<HashSet<long>>(); var gez = new HashSet<long>(); var y = new Stack<long>();
            foreach (long k0 in h)
            {
                if (!gez.Add(k0)) continue;
                var p = new HashSet<long> { k0 }; y.Push(k0);
                while (y.Count > 0)
                {
                    long k = y.Pop(); int ix = (int)((k >> 21) & 0x1FFFFF) - 1048576, iz = (int)(k & 0x1FFFFF) - 1048576;
                    for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                    {
                        long q = OdaSonuc.Anah2(ix + dx, iz + dz);
                        if (h.Contains(q) && gez.Add(q)) { p.Add(q); y.Push(q); }
                    }
                }
                r.Add(p);
            }
            return r;
        }

        static List<double[]> HucreKoseleri(IEnumerable<long> hs)
        {
            var pts = new List<double[]>(); double V = KanitIzgarasi.V;
            foreach (long k in hs)
            {
                int ix = (int)((k >> 21) & 0x1FFFFF) - 1048576, iz = (int)(k & 0x1FFFFF) - 1048576; double x0 = ix * V, z0 = iz * V;
                pts.Add(new[] { x0, z0 }); pts.Add(new[] { x0 + V, z0 }); pts.Add(new[] { x0, z0 + V }); pts.Add(new[] { x0 + V, z0 + V });
            }
            return pts;
        }

        static float Sinirla(float x, float a, float b) { return x < a ? a : x > b ? b : x; }
        static float Medyan(List<float> l) { if (l.Count == 0) return 0f; var c = new List<float>(l); c.Sort(); return c[c.Count / 2]; }

        static void HucreXZ(long k, out double x, out double z)
        {
            int ix = (int)((k >> 21) & 0x1FFFFF) - 1048576, iz = (int)(k & 0x1FFFFF) - 1048576;
            x = (ix + 0.5) * KanitIzgarasi.V; z = (iz + 0.5) * KanitIzgarasi.V;
        }

        /// <summary>Zarfa en kucuk dikdortgen; Manhattan yonune 15 derece icindeyse yaslanir; her yandan 'daralt' kadar kucultulur.</summary>
        static OdaKutu Dikdortgenle(List<double[]> hull, double aci0, double daralt, double yaslanma = 15.0)
        {
            if (hull.Count < 3) return null;
            double phi = OdaGeo.EnKucukDikdortgenAcisi(hull);
            if (!double.IsNaN(aci0))
            {
                double pd = phi * 180.0 / Math.PI, f = OdaGeo.Fark90(pd, aci0);
                if (Math.Abs(f) <= yaslanma) phi = (pd - f) * Math.PI / 180.0;
            }
            OdaGeo.Dikdortgen(hull, phi, out double cx, out double cz, out double lu, out double lv);
            lu = Math.Max(0.05, lu - daralt); lv = Math.Max(0.05, lv - daralt);
            double aciDeg = OdaGeo.Mod180(phi * 180.0 / Math.PI);
            if (lu < lv) { double t = lu; lu = lv; lv = t; aciDeg = OdaGeo.Mod180(aciDeg + 90.0); }
            return new OdaKutu { cx = (float)cx, cz = (float)cz, sx = (float)lu, sz = (float)lv, aci = (float)aciDeg };
        }

        /// <summary>Nokta, bir kesin duvarin oda disi tarafinda ya da duvar cizgisine 'pay'dan yakin mi (o duvarin boyu icinde)?</summary>
        static bool DuvarDisinda(List<OdaDuvar> kd, double x, double z, double pay)
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
