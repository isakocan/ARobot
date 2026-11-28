// Tarama uygulamasinin harita gorunumunu PC'de birebir uretir: ar_tarama/Assets/Scripts/ZeminHaritasi.cs + ZeminGeo.cs
// aynen derlenir (UnityEngine yok), zemin.json -> ZeminSonuc.JsonOku -> Resim (inceleme: pay 12).
// Cikti: <cikti>.rgba (W*H*4, satir 0 = en kucuk w) ve <cikti>.json (W, H, ou, ow, piksel, ca, sa).
using System;
using System.IO;
using System.Globalization;
using ArTarama;

static class Program
{
    static int Main(string[] a)
    {
        if (a.Length < 2) { Console.Error.WriteLine("kullanim: uygulama_harita <zemin.json> <cikti_oneki>"); return 2; }
        var s = ZeminSonuc.JsonOku(File.ReadAllText(a[0]), out string oda);
        var p = s.Resim(out int W, out int H, out float ou, out float ow, 12);
        File.WriteAllBytes(a[1] + ".rgba", p);
        var c = CultureInfo.InvariantCulture;
        File.WriteAllText(a[1] + ".json", string.Format(c,
            "{{\"W\":{0},\"H\":{1},\"ou\":{2},\"ow\":{3},\"piksel\":{4},\"ca\":{5},\"sa\":{6},\"oda\":\"{7}\",\"alan\":{8},\"oyun_alan\":{9}}}",
            W, H, ou, ow, ZeminSonuc.ResimPiksel, s.ResimCa, s.ResimSa, oda.Replace("\"", "'"), s.alan, s.oyunAlan));
        return 0;
    }
}
