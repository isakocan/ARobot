using System;
using System.Collections.Generic;

namespace ArTarama
{
    // =====================================================================================================
    // Kanita dayali doluluk izgarasi: SAF C# (UnityEngine yok), arka plan is parcaciginda calisir.
    // Her derinlik karesi (~80x45) bir gozlemdir: isin ucundaki voksel "vurus" kaniti, isin yolundaki vokseller
    // "bos" kaniti alir. Tek bir kotu kare hicbir zaman kalici nesne uretmez: dolu sayilmak icin birden cok kare
    // ve birden cok bakis yonu gerekir; bos kaniti gelince nesne kendiliginden silinir.
    // Koordinatlar Unity oturum uzayi (metre, y yukari).
    // =====================================================================================================

    /// <summary>Bir derinlik karesi (kucultulmus) + kamera pozu. Derinlik metre (z ekseni boyunca), 0 = yok.</summary>
    public class DerinlikKaresi
    {
        public int w, h;
        public float[] d;          // w*h, metre
        public byte[] g;           // w*h, guven 0..255 (null = hepsi gecerli)
        public float fx, fy, cx, cy;   // w x h izgarasina olceklenmis ic parametreler
        public float px, py, pz, qx, qy, qz, qw;
        public float zaman;        // saniye (oturum basindan)

        /// <summary>Piksel (u,v) icin kamera-yerel yon (z=1): Unity kamera cercevesi (-y_cv, -x_cv, 1).</summary>
        public void YerelYon(float u, float v, out float lx, out float ly)
        {
            lx = -(v + 0.5f - cy) / fy; ly = -(u + 0.5f - cx) / fx;
        }

        public static void DonmeMatrisi(float qx, float qy, float qz, float qw, float[] m)
        {
            float xx = qx * qx, yy = qy * qy, zz = qz * qz, xy = qx * qy, xz = qx * qz, yz = qy * qz, wx = qw * qx, wy = qw * qy, wz = qw * qz;
            m[0] = 1 - 2 * (yy + zz); m[1] = 2 * (xy - wz); m[2] = 2 * (xz + wy);
            m[3] = 2 * (xy + wz); m[4] = 1 - 2 * (xx + zz); m[5] = 2 * (yz - wx);
            m[6] = 2 * (xz - wy); m[7] = 2 * (yz + wx); m[8] = 1 - 2 * (xx + yy);
        }
    }

    public class KanitIzgarasi
    {
        public const float V = 0.05f;
        public const float LMin = -2f, LMaks = 3.5f, VurusArtis = 0.85f, BosAzalis = 0.4f;
        public const float DoluEsik = 1.5f, BosEsik = -0.5f;
        public const int MinVurus = 3, MinBin = 2;
        public const float MinMenzil = 0.3f, MaksMenzil = 4.0f;
        public const int MinGuven = 128;
        public const int MaksVoksel = 2500000;

        long[] anahtar; float[] L; byte[] vurus; ushort[] ilk, son; int[] bin; int[] vk, bk;
        int kap, mask, sayi, kare;
        const long Bos = long.MinValue;

        public int VokselSayisi { get { return sayi; } }
        public int Kapasite { get { return kap; } }
        public float SonKameraX, SonKameraY, SonKameraZ;
        public float SonZaman;
        public int KareSayisi { get { return kare; } }

        public KanitIzgarasi(int baslangicKapasite = 1 << 18) { Ayir(baslangicKapasite); }

        void Ayir(int c)
        {
            kap = c; mask = c - 1; sayi = 0;
            anahtar = new long[c]; for (int i = 0; i < c; i++) anahtar[i] = Bos;
            L = new float[c]; vurus = new byte[c]; ilk = new ushort[c]; son = new ushort[c]; bin = new int[c]; vk = new int[c]; bk = new int[c];
        }

        public void Sifirla() { Ayir(1 << 18); kare = 0; }

        public static long Anah(int ix, int iy, int iz)
        {
            return ((long)(ix + 1048576) << 42) | ((long)(iy + 1048576) << 21) | (long)(iz + 1048576);
        }
        public static void Coz(long k, out int ix, out int iy, out int iz)
        {
            ix = (int)((k >> 42) & 0x1FFFFF) - 1048576; iy = (int)((k >> 21) & 0x1FFFFF) - 1048576; iz = (int)(k & 0x1FFFFF) - 1048576;
        }
        public static int Ind(float x) { return (int)Math.Floor(x / V); }

        int Kok(long k)
        {
            ulong h = (ulong)k * 0x9E3779B97F4A7C15UL;
            return (int)(h >> 40) & mask;
        }

        public int Bul(long k)
        {
            int i = Kok(k);
            while (true)
            {
                long a = anahtar[i];
                if (a == k) return i;
                if (a == Bos) return -1;
                i = (i + 1) & mask;
            }
        }

        int BulEkle(long k)
        {
            int i = Kok(k);
            while (true)
            {
                long a = anahtar[i];
                if (a == k) return i;
                if (a == Bos)
                {
                    if (sayi >= MaksVoksel) return -1;
                    if ((sayi + 1) * 2 > kap) { Buyut(); return BulEkle(k); }
                    anahtar[i] = k; sayi++;
                    L[i] = 0f; vurus[i] = 0; ilk[i] = ushort.MaxValue; son[i] = 0; bin[i] = 0; vk[i] = 0; bk[i] = 0;
                    return i;
                }
                i = (i + 1) & mask;
            }
        }

        void Buyut()
        {
            var ak = anahtar; var l = L; var v = vurus; var i0 = ilk; var s0 = son; var b0 = bin; var v1 = vk; var b1 = bk; int eski = kap;
            Ayir(kap * 2);
            for (int i = 0; i < eski; i++)
            {
                if (ak[i] == Bos) continue;
                int j = Kok(ak[i]);
                while (anahtar[j] != Bos) j = (j + 1) & mask;
                anahtar[j] = ak[i]; sayi++;
                L[j] = l[i]; vurus[j] = v[i]; ilk[j] = i0[i]; son[j] = s0[i]; bin[j] = b0[i]; vk[j] = v1[i]; bk[j] = b1[i];
            }
        }

        // ------------------------------------------------------------------------------------ sorgular
        public bool Var(int slot) { return anahtar[slot] != Bos; }
        public long AnahtarAl(int slot) { return anahtar[slot]; }
        public float LDeger(int slot) { return L[slot]; }
        public int Vurus(int slot) { return vurus[slot]; }
        public int BinMaske(int slot) { return bin[slot]; }
        public float SonGorulme(int slot) { return son[slot] * 0.1f; }
        public float IlkGorulme(int slot) { return ilk[slot] == ushort.MaxValue ? float.MaxValue : ilk[slot] * 0.1f; }

        /// <summary>Bakis yonu dilimi sayisi (32 dilim, her biri 11,25 derece).</summary>
        public static int BinSay(int m) { uint u = (uint)m; int n = 0; while (u != 0) { n += (int)(u & 1u); u >>= 1; } return n; }

        public bool Dolu(int slot) { return L[slot] > DoluEsik && vurus[slot] >= MinVurus && BinSay(bin[slot]) >= MinBin; }
        public bool BosMu(int slot) { return L[slot] < BosEsik; }

        /// <summary>0 = bilinmiyor, 1 = bos, 2 = dolu.</summary>
        public int Durum(int ix, int iy, int iz)
        {
            int s = Bul(Anah(ix, iy, iz));
            if (s < 0) return 0;
            if (Dolu(s)) return 2;
            if (L[s] < BosEsik) return 1;
            return 0;
        }

        // ------------------------------------------------------------------------------------ butunlestirme
        float[] mat = new float[9];
        float[] ex = new float[0], ey = new float[0], ez = new float[0]; int[] eb = new int[0];

        /// <summary>Bir derinlik karesini izgaraya isler. Gecen sure olculerek raporlanir.</summary>
        public void Isle(DerinlikKaresi f)
        {
            kare++;
            SonKameraX = f.px; SonKameraY = f.py; SonKameraZ = f.pz; SonZaman = f.zaman;
            ushort zt = (ushort)Math.Min(65534f, Math.Max(0f, f.zaman * 10f));
            DerinlikKaresi.DonmeMatrisi(f.qx, f.qy, f.qz, f.qw, mat);
            int n = f.w * f.h;
            if (ex.Length < n) { ex = new float[n]; ey = new float[n]; ez = new float[n]; eb = new int[n]; }
            int m = 0;
            for (int v = 0; v < f.h; v++)
                for (int u = 0; u < f.w; u++)
                {
                    int i = v * f.w + u;
                    float d = f.d[i];
                    if (!(d >= MinMenzil && d <= MaksMenzil)) continue;
                    if (f.g != null && f.g[i] < MinGuven) continue;
                    f.YerelYon(u, v, out float lx, out float ly);
                    float wx = mat[0] * lx + mat[1] * ly + mat[2], wy = mat[3] * lx + mat[4] * ly + mat[5], wz = mat[6] * lx + mat[7] * ly + mat[8];
                    ex[m] = f.px + wx * d; ey[m] = f.py + wy * d; ez[m] = f.pz + wz * d;
                    // bakis yonu: isin yaw'i, 32 dilim (11,25 derece). 8 dilimde (45 derece) bir voksel icin
                    // kullanicinin 1-1,5 m yana yurumesi gerekiyordu; kesinlesme 15-45 sn suruyordu.
                    double yaw = Math.Atan2(wx, wz); if (yaw < 0) yaw += 2 * Math.PI;
                    eb[m] = (int)(yaw / (Math.PI / 16)) & 31;
                    m++;
                }
            // gecis 1: vuruslar (her voksel kare basina bir kez)
            for (int r = 0; r < m; r++)
            {
                int s = BulEkle(Anah(Ind(ex[r]), Ind(ey[r]), Ind(ez[r])));
                if (s < 0) continue;
                bin[s] |= 1 << eb[r];
                if (vk[s] == kare) continue;
                vk[s] = kare;
                float nl = L[s] + VurusArtis; L[s] = nl > LMaks ? LMaks : nl;
                if (vurus[s] < 255) vurus[s]++;
                if (ilk[s] == ushort.MaxValue) ilk[s] = zt;
                son[s] = zt;
            }
            // gecis 2: bos kaniti (3B DDA), son voksel ve onundeki 1 voksel haric
            for (int r = 0; r < m; r++) BosIsin(f.px, f.py, f.pz, ex[r], ey[r], ez[r]);
        }

        void BosIsin(float ox, float oy, float oz, float tx, float ty, float tz)
        {
            float dx = tx - ox, dy = ty - oy, dz = tz - oz;
            float len = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (len <= MinMenzil + 2 * V) return;
            // baslangic: kameradan MinMenzil ileride
            float k0 = MinMenzil / len;
            float sx = ox + dx * k0, sy = oy + dy * k0, sz = oz + dz * k0;
            int ix = Ind(sx), iy = Ind(sy), iz = Ind(sz);
            int jx = Ind(tx), jy = Ind(ty), jz = Ind(tz);
            int top = Math.Abs(jx - ix) + Math.Abs(jy - iy) + Math.Abs(jz - iz);
            if (top < 2) return;
            float ddx = tx - sx, ddy = ty - sy, ddz = tz - sz;
            int stx = ddx > 0 ? 1 : -1, sty = ddy > 0 ? 1 : -1, stz = ddz > 0 ? 1 : -1;
            float tdx = ddx != 0 ? Math.Abs(V / ddx) : float.MaxValue, tdy = ddy != 0 ? Math.Abs(V / ddy) : float.MaxValue, tdz = ddz != 0 ? Math.Abs(V / ddz) : float.MaxValue;
            float bx = ddx > 0 ? (ix + 1) * V - sx : sx - ix * V, by = ddy > 0 ? (iy + 1) * V - sy : sy - iy * V, bz = ddz > 0 ? (iz + 1) * V - sz : sz - iz * V;
            float tmx = ddx != 0 ? bx * tdx / V : float.MaxValue, tmy = ddy != 0 ? by * tdy / V : float.MaxValue, tmz = ddz != 0 ? bz * tdz / V : float.MaxValue;
            for (int n = 0; n < top - 1; n++)
            {
                int s = BulEkle(Anah(ix, iy, iz));
                if (s >= 0 && vk[s] != kare && bk[s] != kare)
                {
                    bk[s] = kare;
                    float nl = L[s] - BosAzalis; L[s] = nl < LMin ? LMin : nl;
                }
                if (tmx < tmy && tmx < tmz) { ix += stx; tmx += tdx; }
                else if (tmy < tmz) { iy += sty; tmy += tdy; }
                else { iz += stz; tmz += tdz; }
            }
        }

        // ------------------------------------------------------------------------------------ yardimcilar (yapi katmani)
        /// <summary>Tum dolu vokselleri sirayla verir (yinelemeli dongu icin kapasite + Var + Dolu).</summary>
        public int DoluSayisi()
        {
            int n = 0; for (int i = 0; i < kap; i++) if (anahtar[i] != Bos && Dolu(i)) n++; return n;
        }
    }
}
