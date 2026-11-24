using System;

namespace ArTarama
{
    /// <summary>
    /// ARCore poz sicramasi duzeltmesi (saf C#, 24 Kasim). ARCore bazen tek karede kamerayi metrelerce tasiyip yonunu
    /// degistiriyor (8. tarama, 23 Kasim 22:24: 0,3 sn'de 1,66 m, 30 cm asagi, 142 derece). Oda yerinde, kayan ARCore'un
    /// koordinati: sicramadan sonra kameranin zeminden yuksekligi ayni kaldi (1,26 -> 1,29 m) ama zemin 30 cm asagida
    /// goruldu; sonraki engel noktalarinin eski haritayla ortusmesi ham koordinatta 858'de 67, bu duzeltmeyle 568.
    /// Duzeltme: duzeltilmis = Ry(A) * ham + T. Sicrama gorulunce A ve T, yeni ham poz bir onceki duzeltilmis poza denk
    /// gelecek sekilde guncellenir (o kisa anda kamera yerinde sayilir). Zemin hep yatay oldugu icin yalniz y ekseni donusu.
    /// </summary>
    public class PozSureklilik
    {
        public double A, Tx, Ty, Tz;            // radyan, metre
        public int Sayi;                        // kac sicrama duzeltildi
        public string Son = "";                 // son sicramanin ozeti (gunluk)
        // sicrama: 1 sn icinde ya 30 cm'den uzun ve 2,5 m/sn'den hizli yer degistirme, ya 20 cm'den fazla ve 1,2 m/sn'den
        // hizli dusey kayma (insan telefonu boyle tasiyamaz; normal yurume 1 m/sn, egilme yavas)
        public const double EnFazlaAra = 1.0, Mesafe = 0.30, Hiz = 2.5, Dusey = 0.20, DuseyHiz = 1.2;

        bool var_; double hx, hy, hz, hzaman; readonly double[] hr = new double[9];

        public void Sifirla() { var_ = false; A = Tx = Ty = Tz = 0; Sayi = 0; Son = ""; }

        /// <summary>Yeni ham poz (oturum uzayi). Sicrama ise donusumu gunceller ve true doner.</summary>
        public bool Isle(double x, double y, double z, double qx, double qy, double qz, double qw, double zaman)
        {
            var r = new double[9]; Matris(qx, qy, qz, qw, r);
            bool sic = false;
            if (var_)
            {
                double dt = zaman - hzaman, dx = x - hx, dy = y - hy, dz = z - hz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (dt > 1e-4 && dt <= EnFazlaAra && ((d > Mesafe && d / dt > Hiz) || (Math.Abs(dy) > Dusey && Math.Abs(dy) / dt > DuseyHiz)))
                {
                    // eski ham yonelim = Ry(a) * yeni ham yonelim (ayni fiziksel yon): D = R0 * R1^T'nin y donusu
                    double d00 = 0, d02 = 0, d20 = 0, d22 = 0;
                    for (int k = 0; k < 3; k++) { d00 += hr[k] * r[k]; d02 += hr[k] * r[6 + k]; d20 += hr[6 + k] * r[k]; d22 += hr[6 + k] * r[6 + k]; }
                    double a = Math.Atan2(d02 - d20, d00 + d22);
                    Uygula(hx, hy, hz, out double cx, out double cy, out double cz);      // eski duzeltilmis konum
                    A = Normal(A + a);
                    double ca = Math.Cos(A), sa = Math.Sin(A);
                    Tx = cx - (ca * x + sa * z); Ty = cy - y; Tz = cz - (-sa * x + ca * z);
                    Sayi++; sic = true;
                    Son = "poz sicramasi " + Sayi + ": " + d.ToString("F2") + " m / " + dt.ToString("F2") + " sn, dusey " + dy.ToString("F2") + " m, donus " + (a * 180 / Math.PI).ToString("F0") + " derece; duzeltildi";
                }
            }
            hx = x; hy = y; hz = z; hzaman = zaman; Array.Copy(r, hr, 9); var_ = true;
            return sic;
        }

        public void Uygula(double x, double y, double z, out double ox, out double oy, out double oz)
        {
            double ca = Math.Cos(A), sa = Math.Sin(A);
            ox = ca * x + sa * z + Tx; oy = y + Ty; oz = -sa * x + ca * z + Tz;
        }

        /// <summary>Yonelim: q' = Ry(A) * q (Hamilton).</summary>
        public void Uygula(double qx, double qy, double qz, double qw, out double ox, out double oy, out double oz, out double ow)
        {
            double c = Math.Cos(A / 2), s = Math.Sin(A / 2);
            ow = c * qw - s * qy; ox = c * qx + s * qz; oy = c * qy + s * qw; oz = c * qz - s * qx;
        }

        /// <summary>Derinlik karesinin pozunu yerinde duzeltir (PC tekrar oynatma; telefonda XR Origin tasindigi icin
        /// kamera pozu zaten duzeltilmis gelir).</summary>
        public bool Kare(DerinlikKaresi f)
        {
            bool s = Isle(f.px, f.py, f.pz, f.qx, f.qy, f.qz, f.qw, f.zaman);
            Uygula(f.px, f.py, f.pz, out double x, out double y, out double z);
            Uygula(f.qx, f.qy, f.qz, f.qw, out double qx, out double qy, out double qz, out double qw);
            f.px = (float)x; f.py = (float)y; f.pz = (float)z; f.qx = (float)qx; f.qy = (float)qy; f.qz = (float)qz; f.qw = (float)qw;
            return s;
        }

        static double Normal(double a) { while (a > Math.PI) a -= 2 * Math.PI; while (a < -Math.PI) a += 2 * Math.PI; return a; }

        static void Matris(double qx, double qy, double qz, double qw, double[] m)
        {
            double xx = qx * qx, yy = qy * qy, zz = qz * qz, xy = qx * qy, xz = qx * qz, yz = qy * qz, wx = qw * qx, wy = qw * qy, wz = qw * qz;
            m[0] = 1 - 2 * (yy + zz); m[1] = 2 * (xy - wz); m[2] = 2 * (xz + wy);
            m[3] = 2 * (xy + wz); m[4] = 1 - 2 * (xx + zz); m[5] = 2 * (yz - wx);
            m[6] = 2 * (xz - wy); m[7] = 2 * (yz + wx); m[8] = 1 - 2 * (xx + yy);
        }
    }
}
