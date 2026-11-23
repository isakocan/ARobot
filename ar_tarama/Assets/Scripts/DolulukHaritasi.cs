using System;
using System.Collections.Generic;

namespace ArTarama
{
    /// <summary>
    /// Derinliği LIDAR gibi kullanan 3B doluluk haritası (OctoMap'in yöntemi, 23 Kasım). Her derinlik pikseli kameradan
    /// çıkan bir ışındır: ışının çarptığı 5 cm'lik küp "dolu" yönünde, aradan geçtiği küpler "boş" yönünde bir oy alır
    /// (log-olasılık, sınırlı). Böylece:
    ///  - bir karede yanlış görülen nokta (derinlik gürültüsü, kenar taşması) sonraki ışınlar içinden geçince silinir,
    ///  - masa / sandalye / dolap gibi gerçek şeyler her bakışta yeniden çarpıldığı için dolu kalır,
    ///  - "zemin görüldü mü" ve "üstü boş mu" aynı haritadan, eşik oyunu olmadan okunur.
    /// Zemin haritası (ZeminHaritasi) bir sütunu açık sayar: zemin yüksekliğindeki küp dolu (zemin yüzeyi görüldü) ve
    /// zeminden 8 cm ile 2 m arasındaki hiçbir küp dolu değil. Sütunda dolu küp varsa engel (masa altı dahil).
    /// Bellek: 16x16x16 küplük parçalar, yalnız görülen yerler (bir oda ~ birkaç MB).
    /// </summary>
    public class DolulukHaritasi
    {
        public const float V = 0.05f;
        const int P = 16, PB = 4;                          // parça kenarı (2^4)
        // log-olasılık x10 (OctoMap varsayılanları: çarpma +0,85, geçiş -0,4, sınır -2 / +3,5)
        public const sbyte Carp = 9, Gec = -4, Alt = -20, Ust = 35;

        readonly Dictionary<long, sbyte[]> parca = new Dictionary<long, sbyte[]>();
        readonly Dictionary<long, ushort[]> damga = new Dictionary<long, ushort[]>();   // küpün son güncellendiği kare
        ushort kareNo = 1;
        /// <summary>Yeni kare: her küp bir karede en çok bir oy alır (bir karede aynı küpe düşen onlarca piksel tek
        /// gözlemdir; yoksa zemin bir bakışta kesinleşirdi). Önce çarpmalar, sonra geçişler: çarpılan küp o karede boşalmaz.</summary>
        public void YeniKare() { kareNo++; if (kareNo == 0) { kareNo = 1; foreach (var d in damga.Values) System.Array.Clear(d, 0, d.Length); } }
        public const int ZeminKesin = 15;   // zemin yüzeyi küpü en az iki ayrı karede çarpılmış
        public int ParcaSayisi => parca.Count;

        static long Anah(int x, int y, int z) => ((long)(x + (1 << 20)) << 42) | ((long)(y + (1 << 20)) << 21) | (long)(z + (1 << 20));
        static int Ind(float a) => (int)Math.Floor(a / V);
        public static int EngelAltKup(float zy) => (int)Math.Ceiling((zy + 0.08f) / V - 1e-4f);

        sbyte[] Parca(int cx, int cy, int cz, bool yarat)
        {
            long k = Anah(cx, cy, cz);
            if (parca.TryGetValue(k, out var p)) return p;
            if (!yarat) return null;
            p = new sbyte[P * P * P]; parca[k] = p; damga[k] = new ushort[P * P * P]; return p;
        }

        /// <summary>Küpün değeri (0 = hiç görülmedi, &gt;0 dolu, &lt;0 boş).</summary>
        public sbyte Deger(int x, int y, int z)
        {
            var p = Parca(x >> PB, y >> PB, z >> PB, false);
            return p == null ? (sbyte)0 : p[((x & (P - 1)) * P + (y & (P - 1))) * P + (z & (P - 1))];
        }

        void Ekle(int x, int y, int z, int dv)
        {
            var p = Parca(x >> PB, y >> PB, z >> PB, true);
            int i = ((x & (P - 1)) * P + (y & (P - 1))) * P + (z & (P - 1));
            var dm = damga[Anah(x >> PB, y >> PB, z >> PB)];
            if (dm[i] == kareNo) return;
            dm[i] = kareNo;
            int v = p[i];
            if (v == 0) v = dv > 0 ? 1 : -1;                 // ilk görülüş (0 "bilinmiyor" için ayrılmış)
            v += dv; if (v > Ust) v = Ust; if (v < Alt) v = Alt; if (v == 0) v = dv > 0 ? 1 : -1;
            p[i] = (sbyte)v;
        }

        /// <summary>Işının ucu: o küp dolu yönünde bir oy (Isin'den önce, aynı karenin bütün uçları için çağrılır).</summary>
        public void Carpma(float hx, float hy, float hz) => Ekle(Ind(hx), Ind(hy), Ind(hz), Carp);

        /// <summary>(ox,oy,oz) kameradan (hx,hy,hz) noktasına ışın: aradaki küpler boş, son küp dolu (carp=false ise
        /// yalnız boşaltır: menzil dışı / güvensiz uç). bosaltmaAlt: bu yüksekliğin altındaki küpler boşaltılmaz (zemin).</summary>
        public void Isin(float ox, float oy, float oz, float hx, float hy, float hz, bool carp, float bosaltmaAlt = float.MinValue)
        {
            int yAlt = bosaltmaAlt == float.MinValue ? int.MinValue : Ind(bosaltmaAlt);
            int x = Ind(ox), y = Ind(oy), z = Ind(oz), ex = Ind(hx), ey = Ind(hy), ez = Ind(hz);
            float dx = hx - ox, dy = hy - oy, dz = hz - oz;
            int sx = dx > 0 ? 1 : -1, sy = dy > 0 ? 1 : -1, sz = dz > 0 ? 1 : -1;
            float tdx = dx != 0 ? Math.Abs(V / dx) : float.MaxValue, tdy = dy != 0 ? Math.Abs(V / dy) : float.MaxValue, tdz = dz != 0 ? Math.Abs(V / dz) : float.MaxValue;
            float tx = dx != 0 ? ((sx > 0 ? (x + 1) * V - ox : ox - x * V) / Math.Abs(dx)) : float.MaxValue;
            float ty = dy != 0 ? ((sy > 0 ? (y + 1) * V - oy : oy - y * V) / Math.Abs(dy)) : float.MaxValue;
            float tz = dz != 0 ? ((sz > 0 ? (z + 1) * V - oz : oz - z * V) / Math.Abs(dz)) : float.MaxValue;
            for (int adim = 0; adim < 200; adim++)
            {
                if (x == ex && y == ey && z == ez) break;
                if (y > yAlt) Ekle(x, y, z, Gec);       // zemin yuzeyi kupleri yatik isinla bosaltilmaz (uzaktaki zemin kayboluyordu)
                if (tx < ty && tx < tz) { x += sx; tx += tdx; }
                else if (ty < tz) { y += sy; ty += tdy; }
                else { z += sz; tz += tdz; }
            }
            if (carp) Ekle(ex, ey, ez, Carp);
        }

        /// <summary>Sütun özeti (2B hücre başına): zemin görüldü mü, 8 cm - 2 m arası dolu küp var mı, 1 m üstü dolu var mı.</summary>
        public struct Sutun
        {
            public bool zemin, engel, uzun, bos, zeminKismi;
            public sbyte enCok;     // 10 cm - 2 m arasındaki en dolu küp (görülmemişse sbyte.MinValue): engel kanıtının gücü
            public sbyte zeminD;    // zemin yüzeyindeki en dolu küp: zemin kaç kez görüldü (10 bir, 19 iki, 28 üç, 35 doygun)
        }

        /// <summary>Görülen her sütunun durumu. zy: zemin yüksekliği (dünya y).</summary>
        public Dictionary<long, Sutun> Sutunlar(float zy)
        {
            var s = new Dictionary<long, Sutun>();
            int yz0 = Ind(zy - 0.05f), yz1 = Ind(zy + 0.05f);      // zemin yüzeyi küpleri
            // engel bandi zeminin en az 8 cm ustundeki ilk kupten baslar (24 Kasim: Ind(zy + 0,10) kupu zy + 5 cm'den
            // baslayabiliyordu; zemin tahmini 1 cm oynayinca bant 5 cm asagi atliyor, zemin gurultusu bir anda 1455 hucreyi
            // engel yapiyordu)
            int ye0 = EngelAltKup(zy), ye1 = Ind(zy + 2.0f), yu = Ind(zy + 1.0f);
            foreach (var kv in parca)
            {
                long k = kv.Key;
                int cx = (int)((k >> 42) & ((1 << 21) - 1)) - (1 << 20), cy = (int)((k >> 21) & ((1 << 21) - 1)) - (1 << 20), cz = (int)(k & ((1 << 21) - 1)) - (1 << 20);
                int y0 = cy * P, y1 = y0 + P - 1;
                if (y1 < yz0 || y0 > ye1) continue;
                var p = kv.Value;
                for (int lx = 0; lx < P; lx++)
                    for (int lz = 0; lz < P; lz++)
                    {
                        bool zemin = false, engel = false, uzun = false, bos = false, var = false, kismi = false;
                        sbyte enCok = sbyte.MinValue, zd = sbyte.MinValue;
                        for (int ly = 0; ly < P; ly++)
                        {
                            int y = y0 + ly; sbyte v = p[(lx * P + ly) * P + lz];
                            if (v == 0) continue;
                            if (y >= yz0 && y <= yz1) { var = true; if (v > zd) zd = v; if (v >= ZeminKesin) zemin = true; else if (v > 0) kismi = true; }
                            else if (y >= ye0 && y <= ye1) { var = true; if (v > enCok) enCok = v; if (v > 0) { engel = true; if (y >= yu) uzun = true; } else bos = true; }
                        }
                        if (!var) continue;
                        long a = OdaSonuc.Anah2(cx * P + lx, cz * P + lz);
                        if (!s.TryGetValue(a, out var o)) { o.enCok = sbyte.MinValue; o.zeminD = sbyte.MinValue; }
                        o.zemin |= zemin; o.engel |= engel; o.uzun |= uzun; o.bos |= bos; o.zeminKismi |= kismi;
                        if (enCok > o.enCok) o.enCok = enCok; if (zd > o.zeminD) o.zeminD = zd;
                        s[a] = o;
                    }
            }
            return s;
        }

        /// <summary>LIDAR görünümü: kameranın r yakınındaki kesin dolu küpler (x, y, z, tür; tür 0 zemin, 1 engel, 2 uzun
        /// (1 m üstü), 3 zayıf / tek görülmüş). En çok enCok nokta.</summary>
        public float[] Noktalar(float kx, float ky, float kz, float r, float zy, int enCok)
        {
            var l = new List<float>(4096);
            int yz0 = Ind(zy - 0.05f), yz1 = Ind(zy + 0.05f), ye0 = EngelAltKup(zy), yu = Ind(zy + 1.0f);
            int cx0 = Ind(kx - r) >> PB, cx1 = Ind(kx + r) >> PB, cz0 = Ind(kz - r) >> PB, cz1 = Ind(kz + r) >> PB;
            int cy0 = Ind(zy - 0.1f) >> PB, cy1 = Ind(zy + 2.4f) >> PB;
            for (int cx = cx0; cx <= cx1; cx++)
                for (int cz = cz0; cz <= cz1; cz++)
                    for (int cy = cy0; cy <= cy1; cy++)
                    {
                        if (!parca.TryGetValue(Anah(cx, cy, cz), out var p)) continue;
                        for (int i = 0; i < p.Length; i++)
                        {
                            sbyte v = p[i]; if (v <= 0) continue;
                            int lx = i / (P * P), ly = (i / P) % P, lz = i % P;
                            int x = cx * P + lx, y = cy * P + ly, z = cz * P + lz;
                            float wx = (x + 0.5f) * V, wy = (y + 0.5f) * V, wz = (z + 0.5f) * V;
                            if ((wx - kx) * (wx - kx) + (wz - kz) * (wz - kz) > r * r) continue;
                            float tur;
                            if (y >= yz0 && y <= yz1) { if (v < ZeminKesin) continue; tur = 0; }
                            else if (y >= ye0) tur = v < 18 ? 3 : (y >= yu ? 2 : 1);
                            else continue;
                            l.Add(wx); l.Add(wy); l.Add(wz); l.Add(tur);
                            if (l.Count >= enCok * 4) return l.ToArray();
                        }
                    }
            return l.ToArray();
        }

        public void Temizle() { parca.Clear(); damga.Clear(); }
    }
}
