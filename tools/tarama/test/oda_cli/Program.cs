using System;
using ArTarama;

// Komut satiri kosucusu: Unity olmadan oda modeli sentetik testleri ve kayit tekrari.
//   oda_test.sh [cikti_klasoru]                  -> sentetik testler (PNG/JSON cikti klasorune)
//   oda_test.sh <kayit_klasoru> [cikti_klasoru]  -> tekrar (oda.json + oda_ustten.png)
//   oda_test.sh evresim <ev_klasoru> <png>       -> kayitli evin 2B haritasi (isaretlerle)
public static class Program
{
    public static int Main(string[] a)
    {
        if (a.Length == 1 && a[0] == "ev") return EvTest.Calistir();
        if (a.Length == 3 && a[0] == "oyunasama") { OdaTekrar.OyunAsamaPng(a[1], a[2], Console.WriteLine); return 0; }
        if (a.Length == 3 && a[0] == "evresim") { OdaTekrar.EvPng(a[1], a[2], Console.WriteLine); return 0; }
        if (a.Length == 0) return OdaSentetik.Calistir(Console.WriteLine) == 0 ? 0 : 1;
        if (!System.IO.File.Exists(System.IO.Path.Combine(a[0], "kareler.jsonl")))
            return OdaSentetik.Calistir(Console.WriteLine, a[0]) == 0 ? 0 : 1;
        return OdaTekrar.Calistir(a[0], Console.WriteLine, a.Length > 1 ? a[1] : null);
    }
}
