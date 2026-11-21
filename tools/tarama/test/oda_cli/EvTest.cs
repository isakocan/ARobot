using System;
using System.Collections.Generic;
using ArTarama;

// Ev koordinati testleri: isaret pozu (homografi), Poz2 cebiri, haritanin baska oturuma oturtulmasi.
public static class EvTest
{
    static int hata;
    static void Bak(string ad, bool ok, string ek = "") { Console.WriteLine((ok ? "TAMAM " : "HATA  ") + ad + " " + ek); if (!ok) hata++; }

    public static int Calistir()
    {
        // 1) homografi: kamera-yerelinde bilinen pozdaki 18 cm isaretin koseleri izdusurulur, poz geri cozulur
        var rnd = new Random(3);
        double enT = 0, enA = 0;
        for (int n = 0; n < 200; n++)
        {
            double d = 0.6 + rnd.NextDouble() * 2.4, yaw = (rnd.NextDouble() - 0.5) * 1.4, pit = (rnd.NextDouble() - 0.5) * 0.6;
            double[] t = { (rnd.NextDouble() - 0.5) * d * 0.6, (rnd.NextDouble() - 0.5) * d * 0.4, d };
            // isaret eksenleri: sag = (cos yaw, 0, sin yaw) egik; yukari ~ (0,1,0) egimli
            double[] r1 = { Math.Cos(yaw), 0, Math.Sin(yaw) };
            double[] r2 = { -Math.Sin(yaw) * Math.Sin(pit), Math.Cos(pit), Math.Cos(yaw) * Math.Sin(pit) };
            double h = 0.09; double[] X = { -h, h, h, -h }, Y = { h, h, -h, -h };
            var a = new double[4]; var b = new double[4];
            for (int i = 0; i < 4; i++)
            {
                double px = t[0] + r1[0] * X[i] + r2[0] * Y[i], py = t[1] + r1[1] * X[i] + r2[1] * Y[i], pz = t[2] + r1[2] * X[i] + r2[2] * Y[i];
                a[i] = px / pz + (rnd.NextDouble() - 0.5) * 0.001; b[i] = py / pz + (rnd.NextDouble() - 0.5) * 0.001;   // ~0,5 piksel gurultu
            }
            if (!IsaretPoz.Coz(a, b, 0.18, out var tt, out var rr1, out _)) { hata++; continue; }
            double et = Math.Sqrt((tt[0] - t[0]) * (tt[0] - t[0]) + (tt[1] - t[1]) * (tt[1] - t[1]) + (tt[2] - t[2]) * (tt[2] - t[2]));
            double ea = Math.Acos(Math.Min(1, Math.Abs(rr1[0] * r1[0] + rr1[1] * r1[1] + rr1[2] * r1[2]))) * 180 / Math.PI;
            enT = Math.Max(enT, et / d); enA = Math.Max(enA, ea);
        }
        // mesafeye gore yatay yon (yaw) hatasi: tek gozlem medyani ve 20 gozlem ortalamasi
        foreach (double dd in new[] { 0.8, 1.5, 2.5 })
        {
            var tek = new List<double>(); var ort = new List<double>();
            for (int tekrar = 0; tekrar < 100; tekrar++)
            {
                double yaw = (rnd.NextDouble() - 0.5) * 1.0; double snx = 0, snz = 0;
                double[] t = { (rnd.NextDouble() - 0.5) * dd * 0.4, 0, dd };
                double[] r1 = { Math.Cos(yaw), 0, Math.Sin(yaw) }, r2 = { 0, 1, 0 };
                for (int g = 0; g < 20; g++)
                {
                    double h = 0.09; double[] X = { -h, h, h, -h }, Y = { h, h, -h, -h };
                    var a = new double[4]; var b = new double[4];
                    for (int i = 0; i < 4; i++)
                    {
                        double px = t[0] + r1[0] * X[i], py = Y[i], pz = t[2] + r1[2] * X[i];
                        a[i] = px / pz + (rnd.NextDouble() - 0.5) * 0.002; b[i] = py / pz + (rnd.NextDouble() - 0.5) * 0.002;   // ~1 piksel
                    }
                    if (!IsaretPoz.Coz(a, b, 0.18, out _, out var q1, out _)) continue;
                    double nrm = Math.Sqrt(q1[0] * q1[0] + q1[2] * q1[2]); double ux = q1[0] / nrm, uz = q1[2] / nrm;
                    double e1 = Math.Abs(Math.Atan2(ux * r1[2] - uz * r1[0], ux * r1[0] + uz * r1[2])) * 180 / Math.PI;
                    if (g == 0) tek.Add(e1);
                    double w = 1; snx += ux * w; snz += uz * w;
                }
                double nn = Math.Sqrt(snx * snx + snz * snz); snx /= nn; snz /= nn;
                ort.Add(Math.Abs(Math.Atan2(snx * r1[2] - snz * r1[0], snx * r1[0] + snz * r1[2])) * 180 / Math.PI);
            }
            tek.Sort(); ort.Sort();
            Console.WriteLine("  " + dd + " m: tek gozlem yaw hatasi medyan " + tek[50].ToString("F1") + " / %90 " + tek[90].ToString("F1") + " derece; 20 gozlem ortalamasi medyan " + ort[50].ToString("F1") + " / %90 " + ort[90].ToString("F1"));
        }
        Bak("isaret pozu tek gozlem en kotu (bilgi; ortalama kullaniliyor)", true, "en kotu konum hatasi %" + (enT * 100).ToString("F1") + " mesafenin, sag eksen " + enA.ToString("F1") + " derece");

        // 2) Poz2: ters ve bilesim
        var P = new Poz2(1.3, -0.7, 0.9); var Q = P.Ters().Sonra(P);
        Bak("Poz2 ters", Math.Abs(Q.x) < 1e-9 && Math.Abs(Q.z) < 1e-9 && Math.Abs(Q.aci) < 1e-9);

        // 3) ev: oturum A'da isaret (2,1) bakis 30 derece; ev bu isaretten kurulur. Oturum B'de ayni isaret (5,-3) bakis 120 derece.
        //    A'daki bir zemin noktasi B'de dogru yere dusmeli.
        double ra = 30 * Math.PI / 180, rb = 120 * Math.PI / 180;
        var SA = new Poz2(2, 1, ra); var SB = new Poz2(5, -3, rb);
        var oturumAdanEve = SA.Ters();
        var ev = new Ev { ad = "test" };
        var s = new ZeminSonuc { zeminVar = true };
        // A'da isaretin 1 m onunde 0,5x0,5 m kare
        SA.Uygula(0, 1.0, out double cx, out double cz);
        for (int iz = -5; iz < 5; iz++) for (int ix = -5; ix < 5; ix++) s.acik.Add(OdaSonuc.Anah2(ZeminSonuc.Ind((float)cx) + ix, ZeminSonuc.Ind((float)cz) + iz));
        ev.Ekle(s, oturumAdanEve);
        ev.isaretler[0] = new EvIsaret { id = 0, x = 0, z = 0, aci = 0 };
        var T = Ev.Hizala(ev.isaretler[0], SB.x, SB.z, SB.aci);   // ev -> oturum B
        var b2 = ev.Oturuma(T, 0f);
        SB.Uygula(0, 1.0, out double bx, out double bz);
        double mx = 0, mz = 0; foreach (long k in b2.acik) { ZeminSonuc.Coz(k, out int ix, out int iz); mx += (ix + 0.5) * ZeminSonuc.V; mz += (iz + 0.5) * ZeminSonuc.V; }
        mx /= Math.Max(1, b2.acik.Count); mz /= Math.Max(1, b2.acik.Count);
        double e = Math.Sqrt((mx - bx) * (mx - bx) + (mz - bz) * (mz - bz));
        Bak("ev haritasi baska oturuma", e < 0.05 && Math.Abs(b2.acik.Count - 100) < 25, "merkez hatasi " + (e * 100).ToString("F1") + " cm, hucre " + b2.acik.Count + " (beklenen ~100)");
        Console.WriteLine("SONUC: " + hata + " HATA");
        return hata;
    }
}
