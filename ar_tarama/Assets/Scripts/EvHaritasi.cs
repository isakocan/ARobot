using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ArTarama
{
    // =====================================================================================================
    // Ev = kalici acik zemin haritasi + ArUco isaretleri, "ev koordinatinda" (saf C#).
    // ARCore her oturumda koordinatlari sifirdan baslatir; ortak referans duvardaki isaretlerdir. Ev koordinati evin ilk
    // kaydinda en kucuk numarali isaretten kurulur: baslangic = isaretin onundeki zemin noktasi, +z = isaretin baktigi
    // yon (odanin icine), y = 0 zemin. Sonraki oturumlarda (duzenle, oyun, robot) herhangi bir kayitli isaret gorulunce
    // oturum koordinati eve oturtulur.
    // =====================================================================================================

    /// <summary>2B rijit donusum (XZ duzlemi): p' = R(aci) p + t. aci: +z'den +x'e dogru (Unity yaw yonu), radyan.</summary>
    public struct Poz2
    {
        public double x, z, aci;
        public Poz2(double x, double z, double aci) { this.x = x; this.z = z; this.aci = aci; }
        public static Poz2 Birim => new Poz2(0, 0, 0);

        /// <summary>Yerel (lx, lz) -> dis. Yerel +z ekseni disarda (sin aci, cos aci) yonune gider.</summary>
        public void Uygula(double lx, double lz, out double ox, out double oz)
        {
            double c = Math.Cos(aci), s = Math.Sin(aci);
            ox = x + lx * c + lz * s; oz = z - lx * s + lz * c;
        }
        public Poz2 Ters()
        {
            double c = Math.Cos(aci), s = Math.Sin(aci);
            // R^T (-t): R = [[c, s], [-s, c]] -> R^T = [[c, -s], [s, c]]
            return new Poz2(-(x * c - z * s), -(x * s + z * c), -aci);
        }
        /// <summary>this * b: once b, sonra this.</summary>
        public Poz2 Sonra(Poz2 b) { Uygula(b.x, b.z, out double ox, out double oz); return new Poz2(ox, oz, aci + b.aci); }
        public double AciDerece => aci * 180.0 / Math.PI;
    }

    public class EvIsaret
    {
        public int id; public double x, z, aci, yukseklik; public int gozlem;   // ev koordinatinda; aci = isaretin baktigi yon
    }

    public class Ev
    {
        public string id = "", ad = "Ev"; public long olusturma, guncelleme; public int taramaSayisi;
        public readonly Dictionary<int, EvIsaret> isaretler = new Dictionary<int, EvIsaret>();
        public HashSet<long> acik = new HashSet<long>(), engel = new HashSet<long>(), bilinen = new HashSet<long>(), silinen = new HashSet<long>();
        public HashSet<long> duvar = new HashSet<long>();
        public List<float> iz = new List<float>();
        public float aci = float.NaN;    // haritanin ana yonu ev koordinatinda (duz kenarli cizim icin)

        public float Alan => acik.Count * ZeminSonuc.V * ZeminSonuc.V;

        /// <summary>Gosterim icin ZeminSonuc (ev koordinatinda).</summary>
        public ZeminSonuc Sonuc()
        {
            var s = new ZeminSonuc { zeminVar = true, zeminY = 0f, aci = aci };
            s.acik = new HashSet<long>(acik); s.engel = new HashSet<long>(engel); s.bilinen = new HashSet<long>(bilinen); s.duvar = new HashSet<long>(duvar);
            foreach (long k in silinen) { s.acik.Remove(k); s.bilinen.Remove(k); s.engel.Remove(k); }
            s.alan = s.acik.Count * ZeminSonuc.V * ZeminSonuc.V; s.iz = iz.ToArray();
            s.Yumusat();
            return s;
        }

        // ------------------------------------------------------------------ donusum ve birlestirme
        /// <summary>Hucre kumesini donusturur (ters esleme: hedef hucre merkezinden kaynaga bakilir, delik kalmaz).</summary>
        public static HashSet<long> Donustur(HashSet<long> kaynak, Poz2 T)
        {
            var r = new HashSet<long>(); if (kaynak.Count == 0) return r;
            double V = ZeminSonuc.V;
            double x0 = double.MaxValue, x1 = double.MinValue, z0 = double.MaxValue, z1 = double.MinValue;
            foreach (long k in kaynak)
            {
                ZeminSonuc.Coz(k, out int ix, out int iz);
                T.Uygula((ix + 0.5) * V, (iz + 0.5) * V, out double ox, out double oz);
                if (ox < x0) x0 = ox; if (ox > x1) x1 = ox; if (oz < z0) z0 = oz; if (oz > z1) z1 = oz;
            }
            var Ti = T.Ters();
            for (int iz = (int)Math.Floor(z0 / V) - 1; iz <= (int)Math.Floor(z1 / V) + 1; iz++)
                for (int ix = (int)Math.Floor(x0 / V) - 1; ix <= (int)Math.Floor(x1 / V) + 1; ix++)
                {
                    Ti.Uygula((ix + 0.5) * V, (iz + 0.5) * V, out double sx, out double sz);
                    if (kaynak.Contains(OdaSonuc.Anah2((int)Math.Floor(sx / V), (int)Math.Floor(sz / V)))) r.Add(OdaSonuc.Anah2(ix, iz));
                }
            return r;
        }

        /// <summary>Oturum sonucunu (oturum koordinati) eve ekler. T: oturum -> ev. Yeni kanit olan hucrelerde yeni durum gecer.</summary>
        public void Ekle(ZeminSonuc s, Poz2 T)
        {
            var a = Donustur(s.acik, T); var e = Donustur(s.engel, T); var b = Donustur(s.bilinen, T); var d = Donustur(s.duvar, T);
            Kat(acik, engel, bilinen, duvar, a, e, b, d);
            for (int i = 0; i + 1 < s.iz.Length; i += 2) { T.Uygula(s.iz[i], s.iz[i + 1], out double x, out double z); iz.Add((float)x); iz.Add((float)z); }
            if (!float.IsNaN(s.aci) && float.IsNaN(aci)) aci = (float)(s.aci + T.aci);
            taramaSayisi++;
        }

        /// <summary>Ev haritasini oturum koordinatina cevirir (duzenlemede eski haritayi canli gostermek icin). T: ev -> oturum.</summary>
        public ZeminSonuc Oturuma(Poz2 T, float zeminY)
        {
            var s = new ZeminSonuc { zeminVar = true, zeminY = zeminY, aci = float.IsNaN(aci) ? float.NaN : (float)(aci + T.aci) };
            var ac = new HashSet<long>(acik); ac.ExceptWith(silinen);
            s.acik = Donustur(ac, T); s.engel = Donustur(engel, T); s.bilinen = Donustur(bilinen, T); s.duvar = Donustur(duvar, T);
            return s;
        }

        /// <summary>Iki oturum sonucunu birlestirir: yeni kanit olan hucrede yeni gecer, digerlerinde eski kalir.</summary>
        public static ZeminSonuc Birlestir(ZeminSonuc yeni, ZeminSonuc eski, HashSet<long> silinenOturum)
        {
            var s = new ZeminSonuc { zeminVar = true, zeminY = yeni.zeminY, kameraX = yeni.kameraX, kameraZ = yeni.kameraZ, zaman = yeni.zaman, iz = yeni.iz, aci = float.IsNaN(yeni.aci) ? eski.aci : yeni.aci };
            s.acik = new HashSet<long>(eski.acik); s.engel = new HashSet<long>(eski.engel); s.bilinen = new HashSet<long>(eski.bilinen); s.duvar = new HashSet<long>(eski.duvar);
            Kat(s.acik, s.engel, s.bilinen, s.duvar, yeni.acik, yeni.engel, yeni.bilinen, yeni.duvar);
            if (silinenOturum != null) foreach (long k in silinenOturum) s.acik.Remove(k);
            s.alan = s.acik.Count * ZeminSonuc.V * ZeminSonuc.V;
            Array.Copy(yeni.yonEksik, s.yonEksik, 16);
            s.Yumusat();
            return s;
        }

        /// <summary>Yeni oturumu eskinin ustune katar. Eski acik zemin yalniz yeni oturumda orada engel / duvar goruldugunde
        /// gider; yeni oturumun 'bilinen' (gorulmus ama teyit edilmemis) hucreleri eskiyi silmez (22 Kasim: kayitli evi
        /// acinca eski zemin sifirdan taraniyormus gibi kayboluyordu). Yeni acik zemin eski engelin yerine gecer.</summary>
        static void Kat(HashSet<long> acik, HashSet<long> engel, HashSet<long> bilinen, HashSet<long> duvar,
                        HashSet<long> a, HashSet<long> e, HashSet<long> b, HashSet<long> d)
        {
            acik.ExceptWith(e); acik.ExceptWith(d);
            engel.ExceptWith(a); duvar.ExceptWith(a);
            acik.UnionWith(a); engel.UnionWith(e); duvar.UnionWith(d);
            foreach (long k in b) if (!acik.Contains(k) && !engel.Contains(k) && !duvar.Contains(k)) bilinen.Add(k);
            bilinen.ExceptWith(acik); bilinen.ExceptWith(engel); bilinen.ExceptWith(duvar);
        }

        /// <summary>Silgi: ev koordinatinda (x,z) etrafinda r yaricapli hucreler acik zeminden cikarilir.</summary>
        public int Sil(double x, double z, double r)
        {
            int ri = (int)Math.Ceiling(r / ZeminSonuc.V), n = 0;
            int cx = (int)Math.Floor(x / ZeminSonuc.V), cz = (int)Math.Floor(z / ZeminSonuc.V);
            for (int dz = -ri; dz <= ri; dz++) for (int dx = -ri; dx <= ri; dx++)
            {
                if (dx * dx + dz * dz > ri * ri) continue;
                long k = OdaSonuc.Anah2(cx + dx, cz + dz);
                if ((acik.Contains(k) || bilinen.Contains(k)) && silinen.Add(k)) n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ dosya
        public string MetaJson()
        {
            var c = CultureInfo.InvariantCulture; var sb = new StringBuilder();
            sb.Append("{\"surum\":1,\"id\":\"").Append(id).Append("\",\"ad\":\"").Append(ad.Replace("\"", "'")).Append('"');
            sb.Append(",\"olusturma\":").Append(olusturma).Append(",\"guncelleme\":").Append(guncelleme).Append(",\"tarama\":").Append(taramaSayisi);
            sb.Append(",\"koordinat\":\"ev: y=0 zemin, baslangic = ilk isaretin onundeki zemin, +z isaretin baktigi yon, metre\"");
            sb.Append(",\"isaretler\":[");
            bool ilk = true;
            foreach (var m in isaretler.Values)
            {
                if (!ilk) sb.Append(','); ilk = false;
                sb.Append("{\"id\":").Append(m.id).Append(",\"x\":").Append(m.x.ToString("F3", c)).Append(",\"z\":").Append(m.z.ToString("F3", c))
                  .Append(",\"aci\":").Append(m.aci.ToString("F4", c)).Append(",\"yukseklik\":").Append(m.yukseklik.ToString("F3", c)).Append(",\"gozlem\":").Append(m.gozlem).Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public string HaritaJson()
        {
            var s = new ZeminSonuc { zeminVar = true, zeminY = 0f, acik = acik, engel = engel, bilinen = bilinen, duvar = duvar, alan = Alan, iz = iz.ToArray(), aci = aci };
            string j = s.Json(ad);
            // silinen ve duvar hucreleri de yazilir
            var sb = new StringBuilder(j.Substring(0, j.Length - 1));
            Liste(sb, "silinen", silinen); Liste(sb, "duvar", duvar);
            sb.Append('}');
            return sb.ToString();
        }

        static void Liste(StringBuilder sb, string ad, HashSet<long> h)
        {
            sb.Append(",\"").Append(ad).Append("\":["); bool ilk = true;
            foreach (long k in h) { ZeminSonuc.Coz(k, out int ix, out int iz); if (!ilk) sb.Append(','); ilk = false; sb.Append(ix).Append(',').Append(iz); }
            sb.Append(']');
        }

        public void Kaydet(string klasor)
        {
            Directory.CreateDirectory(klasor);
            guncelleme = DateTime.UtcNow.Ticks;
            File.WriteAllText(Path.Combine(klasor, "ev.json"), MetaJson(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(klasor, "zemin.json"), HaritaJson(), new UTF8Encoding(false));
        }

        public static Ev Yukle(string klasor)
        {
            var ev = new Ev();
            string mj = File.ReadAllText(Path.Combine(klasor, "ev.json"));
            ev.id = JMetin(mj, "id"); ev.ad = JMetin(mj, "ad");
            ev.olusturma = (long)JSayi(mj, "olusturma"); ev.guncelleme = (long)JSayi(mj, "guncelleme"); ev.taramaSayisi = (int)JSayi(mj, "tarama");
            int i = mj.IndexOf("\"isaretler\":[", StringComparison.Ordinal);
            if (i >= 0)
            {
                int e = mj.IndexOf(']', i);
                foreach (var parca in mj.Substring(i + 13, Math.Max(0, e - i - 13)).Split('}'))
                {
                    if (parca.IndexOf("\"id\"", StringComparison.Ordinal) < 0) continue;
                    var m = new EvIsaret { id = (int)JSayi(parca, "id"), x = JSayi(parca, "x"), z = JSayi(parca, "z"), aci = JSayi(parca, "aci"), yukseklik = JSayi(parca, "yukseklik"), gozlem = (int)JSayi(parca, "gozlem") };
                    ev.isaretler[m.id] = m;
                }
            }
            string zp = Path.Combine(klasor, "zemin.json");
            if (File.Exists(zp))
            {
                string zj = File.ReadAllText(zp);
                var s = ZeminSonuc.JsonOku(zj, out _);
                ev.acik = s.acik; ev.engel = s.engel; ev.bilinen = s.bilinen; ev.aci = s.aci;
                ev.iz = new List<float>(s.iz);
                ev.silinen = HucreListesi(zj, "silinen"); ev.duvar = HucreListesi(zj, "duvar");
            }
            return ev;
        }

        static HashSet<long> HucreListesi(string j, string ad)
        {
            var h = new HashSet<long>();
            int i = j.IndexOf("\"" + ad + "\":[", StringComparison.Ordinal); if (i < 0) return h;
            i += ad.Length + 4; int e = j.IndexOf(']', i); if (e < 0) return h;
            var p = j.Substring(i, e - i).Split(',');
            for (int k = 0; k + 1 < p.Length; k += 2)
                if (int.TryParse(p[k], out int ix) && int.TryParse(p[k + 1], out int iz)) h.Add(OdaSonuc.Anah2(ix, iz));
            return h;
        }

        static string JMetin(string j, string ad)
        {
            int i = j.IndexOf("\"" + ad + "\":\"", StringComparison.Ordinal); if (i < 0) return "";
            i += ad.Length + 4; int e = j.IndexOf('"', i); return e > i ? j.Substring(i, e - i) : "";
        }

        static double JSayi(string j, string ad)
        {
            int i = j.IndexOf("\"" + ad + "\":", StringComparison.Ordinal); if (i < 0) return 0;
            i += ad.Length + 3; int e = i; while (e < j.Length && "+-.0123456789eE".IndexOf(j[e]) >= 0) e++;
            double.TryParse(j.Substring(i, e - i), NumberStyles.Float, CultureInfo.InvariantCulture, out double v); return v;
        }

        // ------------------------------------------------------------------ isaretlerle hizalama
        /// <summary>Oturumda olculen isaret pozu (merkez x,z; baktigi yon aci) ile kayitli ev pozu -> ev->oturum donusumu.</summary>
        public static Poz2 Hizala(EvIsaret evde, double ox, double oz, double oaci)
        {
            var S = new Poz2(ox, oz, oaci);           // isaret yerel -> oturum
            var E = new Poz2(evde.x, evde.z, evde.aci);  // isaret yerel -> ev
            return S.Sonra(E.Ters());                  // ev -> isaret yerel -> oturum
        }
    }

    /// <summary>Evlerin klasoru: her ev evler/ev_yyyyMMdd_HHmmss/ icinde ev.json + zemin.json.</summary>
    public static class EvDeposu
    {
        public static string Kok = "";
        public static string Klasor(string id) { return Path.Combine(Kok, id); }

        public static List<string> Listele()
        {
            var r = new List<string>();
            if (string.IsNullOrEmpty(Kok) || !Directory.Exists(Kok)) return r;
            foreach (var d in Directory.GetDirectories(Kok)) if (File.Exists(Path.Combine(d, "ev.json"))) r.Add(Path.GetFileName(d));
            r.Sort(); r.Reverse();
            return r;
        }

        public static Ev Yeni(string ad)
        {
            var ev = new Ev { id = "ev_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture), ad = ad, olusturma = DateTime.UtcNow.Ticks };
            return ev;
        }
    }
}
