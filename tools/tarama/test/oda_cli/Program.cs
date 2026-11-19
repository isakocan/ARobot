using System;
using ArTarama;

// Komut satiri kosucusu: Unity olmadan OdaModeli sentetik testleri ve kayit tekrari.
//   oda_test.sh            -> sentetik testler
//   oda_test.sh <kayit_klasoru> [ply]  -> tekrar (oda.json + oda_ustten.png)
public static class Program
{
    public static int Main(string[] a)
    {
        if (a.Length == 0) return OdaSentetik.Calistir(Console.WriteLine) == 0 ? 0 : 1;
        return OdaTekrar.Calistir(a[0], Console.WriteLine, a.Length > 1 && a[1] == "ply");
    }
}
