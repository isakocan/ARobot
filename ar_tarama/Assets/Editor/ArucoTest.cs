using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;

namespace ArTarama
{
    /// <summary>
    /// Cevrimdisi ArUco testi: -arucoVeri &lt;klasor&gt; icindeki 960x540 ham gri (.gray) goruntuleri tarar, sonuc.txt yazar.
    /// Uretim: tools/aruco/test/uret.py, skorlama: tools/aruco/test/skorla.py
    /// </summary>
    public static class ArucoTest
    {
        public static void Calistir()
        {
            string dir = null;
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-arucoVeri") dir = a[i + 1];
            if (dir == null) { UnityEngine.Debug.LogError("[ArucoTest] -arucoVeri <klasor> gerekli"); EditorApplication.Exit(2); return; }
            var det = new ArucoAlgilayici(); var sb = new StringBuilder(); var ci = CultureInfo.InvariantCulture;
            double toplam = 0; int n = 0;
            foreach (var f in Directory.GetFiles(dir, "*.gray"))
            {
                var g = File.ReadAllBytes(f);
                var sw = Stopwatch.StartNew();
                var r = det.Algila(g, 960, 540, 1f);
                sw.Stop(); toplam += sw.Elapsed.TotalMilliseconds; n++;
                string ad = Path.GetFileNameWithoutExtension(f);
                foreach (var s in r)
                {
                    sb.Append(ad).Append(' ').Append(s.id).Append(' ').Append(s.hamming).Append(' ').Append(s.seviye);
                    foreach (var k in s.kose) sb.Append(' ').Append(k.ToString("F2", ci));
                    sb.Append('\n');
                }
            }
            File.WriteAllText(Path.Combine(dir, "sonuc.txt"), sb.ToString());
            UnityEngine.Debug.Log("[ArucoTest] bitti: " + n + " goruntu, ort " + (toplam / Math.Max(n, 1)).ToString("F1", ci) + " ms/goruntu (Editor)");
            EditorApplication.Exit(0);
        }
    }
}
