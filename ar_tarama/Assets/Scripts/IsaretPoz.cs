using System;

namespace ArTarama
{
    /// <summary>
    /// Kare isaretin (kenar s metre) 4 kosesinden kamera-yerel pozu (saf C#): duzlem -> normallestirilmis goruntu
    /// homografisi (DLT, 8x8), ardindan [r1 r2 t] ayristirmasi. Koseler TL, TR, BR, BL; isaret duzleminde
    /// TL(-s/2,+s/2) TR(+s/2,+s/2) BR(+s/2,-s/2) BL(-s/2,-s/2). Girdi: normallestirilmis goruntu koordinatlari (a,b) —
    /// kamera-yerel isin (a, b, 1). Cikti: merkez t ve isaretin sag (r1) ve yukari (r2) eksenleri, kamera-yerelinde.
    /// </summary>
    public static class IsaretPoz
    {
        public static bool Coz(double[] a, double[] b, double s, out double[] t, out double[] r1, out double[] r2)
        {
            t = r1 = r2 = null;
            double h = s * 0.5;
            double[] X = { -h, h, h, -h }, Y = { h, h, -h, -h };
            var M = new double[8, 9];
            for (int i = 0; i < 4; i++)
            {
                M[2 * i, 0] = X[i]; M[2 * i, 1] = Y[i]; M[2 * i, 2] = 1; M[2 * i, 6] = -a[i] * X[i]; M[2 * i, 7] = -a[i] * Y[i]; M[2 * i, 8] = a[i];
                M[2 * i + 1, 3] = X[i]; M[2 * i + 1, 4] = Y[i]; M[2 * i + 1, 5] = 1; M[2 * i + 1, 6] = -b[i] * X[i]; M[2 * i + 1, 7] = -b[i] * Y[i]; M[2 * i + 1, 8] = b[i];
            }
            // Gauss eleme (kismi pivot): H = [h0..h7, 1]
            for (int c = 0; c < 8; c++)
            {
                int p = c; for (int r = c + 1; r < 8; r++) if (Math.Abs(M[r, c]) > Math.Abs(M[p, c])) p = r;
                if (Math.Abs(M[p, c]) < 1e-12) return false;
                if (p != c) for (int k = 0; k < 9; k++) { double tmp = M[c, k]; M[c, k] = M[p, k]; M[p, k] = tmp; }
                for (int r = 0; r < 8; r++)
                {
                    if (r == c) continue;
                    double f = M[r, c] / M[c, c];
                    for (int k = c; k < 9; k++) M[r, k] -= f * M[c, k];
                }
            }
            var H = new double[9]; for (int i = 0; i < 8; i++) H[i] = M[i, 8] / M[i, i]; H[8] = 1;
            // sutunlar: h1 = (H0,H3,H6), h2 = (H1,H4,H7), h3 = (H2,H5,H8)
            double[] h1 = { H[0], H[3], H[6] }, h2 = { H[1], H[4], H[7] }, h3 = { H[2], H[5], H[8] };
            double n1 = Norm(h1), n2 = Norm(h2); if (n1 < 1e-12 || n2 < 1e-12) return false;
            double l = 2.0 / (n1 + n2);
            if (h3[2] * l < 0) l = -l;   // isaret kameranin onunde
            r1 = new[] { h1[0] * l, h1[1] * l, h1[2] * l }; r2 = new[] { h2[0] * l, h2[1] * l, h2[2] * l }; t = new[] { h3[0] * l, h3[1] * l, h3[2] * l };
            // r1, r2 dikle (Gram-Schmidt) ve birim yap
            Birim(r1); double d = r1[0] * r2[0] + r1[1] * r2[1] + r1[2] * r2[2];
            r2[0] -= d * r1[0]; r2[1] -= d * r1[1]; r2[2] -= d * r1[2]; Birim(r2);
            return t[2] > 0.05;
        }

        static double Norm(double[] v) { return Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]); }
        static void Birim(double[] v) { double n = Norm(v); if (n > 1e-12) { v[0] /= n; v[1] /= n; v[2] /= n; } }
    }
}
