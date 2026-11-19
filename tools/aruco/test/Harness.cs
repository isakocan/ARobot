using System; using System.IO; using System.Diagnostics; using System.Text; using System.Globalization;
using ArTarama;
class Harness {
  static void Main(string[] a) {
    string dir = a[0]; var det = new ArucoAlgilayici(); var sb = new StringBuilder();
    var ci = CultureInfo.InvariantCulture; var sw = new Stopwatch(); double toplam = 0; int n = 0;
    foreach (var f in Directory.GetFiles(dir, "*.gray")) {
      var g = File.ReadAllBytes(f); sw.Restart();
      var r = det.Algila(g, 960, 540, 1f); sw.Stop(); toplam += sw.Elapsed.TotalMilliseconds; n++;
      string ad = Path.GetFileNameWithoutExtension(f);
      foreach (var s in r) { sb.Append(ad).Append(' ').Append(s.id).Append(' ').Append(s.hamming).Append(' ').Append(s.seviye);
        foreach (var k in s.kose) sb.Append(' ').Append(k.ToString("F2", ci)); sb.Append('\n'); }
    }
    File.WriteAllText(Path.Combine(dir, "sonuc.txt"), sb.ToString());
    Console.WriteLine("ort sure ms/goruntu: " + (toplam / n).ToString("F1", ci) + " n=" + n);
  }
}
