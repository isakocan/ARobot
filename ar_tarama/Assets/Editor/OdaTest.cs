using System;
using System.Text;
using UnityEditor;

namespace ArTarama
{
    /// <summary>
    /// Unity batchmode girisleri (cekirdek: OdaTestCekirdek.cs, Unity'siz de calisir: tools/tarama/test/oda_test.sh).
    ///   -executeMethod ArTarama.OdaTest.Calistir                       sentetik testler
    ///   -executeMethod ArTarama.OdaTest.Tekrar -kayit &lt;klasor&gt; [-ply]   kayit tekrari: oda.json + oda_ustten.png
    /// </summary>
    public static class OdaTest
    {
        public static void Calistir()
        {
            var sb = new StringBuilder();
            int hata = OdaSentetik.Calistir(s => { sb.AppendLine(s); });
            UnityEngine.Debug.Log("[OdaTest] " + sb);
            EditorApplication.Exit(hata == 0 ? 0 : 1);
        }

        public static void Tekrar()
        {
            string dir = null; bool ply = false;
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length; i++) { if (a[i] == "-kayit" && i + 1 < a.Length) dir = a[i + 1]; if (a[i] == "-ply") ply = true; }
            if (dir == null) { UnityEngine.Debug.LogError("[OdaTest] -kayit <klasor> gerekli"); EditorApplication.Exit(2); return; }
            var sb = new StringBuilder();
            int r = OdaTekrar.Calistir(dir, s => { sb.AppendLine(s); }, ply);
            UnityEngine.Debug.Log("[OdaTest tekrar] " + sb);
            EditorApplication.Exit(r);
        }
    }
}
