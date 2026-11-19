using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ArTarama
{
    public static class OdaJson
    {
        static readonly CultureInfo ci = CultureInfo.InvariantCulture;
        static string F(float v) { return v.ToString("0.####", ci); }

        /// <summary>isaretler: id, x, y, z (null olabilir). Birim: metre, Unity oturum uzayi.</summary>
        public static string Yaz(OdaSonuc s, List<float[]> isaretler)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"surum\": 1,\n  \"koordinat\": \"Unity oturum uzayi (metre, y yukari); yaw_deg = Quaternion.Euler(0,yaw,0), boyut.x yerel x (uzun kenar), boyut.z yerel z\",\n");
            sb.Append("  \"floor_y\": ").Append(F(s.zeminY)).Append(",\n  \"tavan_y\": ").Append(F(s.tavanY)).Append(",\n  \"tavan_gercek\": ").Append(s.tavanGercek ? "true" : "false").Append(",\n");
            sb.Append("  \"walls\": [");
            for (int i = 0; i < s.duvarlar.Count; i++)
            {
                var d = s.duvarlar[i];
                sb.Append(i == 0 ? "\n" : ",\n").Append("    {\"id\": ").Append(d.id).Append(", \"a\": {\"x\": ").Append(F(d.ax)).Append(", \"z\": ").Append(F(d.az)).Append("}, \"b\": {\"x\": ").Append(F(d.bx)).Append(", \"z\": ").Append(F(d.bz))
                  .Append("}, \"yukseklik\": ").Append(F(d.yukseklik)).Append(", \"uzunluk\": ").Append(F(d.Uzunluk)).Append(", \"normal\": {\"x\": ").Append(F(d.nx)).Append(", \"z\": ").Append(F(d.nz)).Append("}}");
            }
            sb.Append(s.duvarlar.Count > 0 ? "\n  ],\n" : "],\n");
            sb.Append("  \"boxes\": [");
            for (int i = 0; i < s.kutular.Count; i++)
            {
                var k = s.kutular[i];
                sb.Append(i == 0 ? "\n" : ",\n").Append("    {\"id\": ").Append(k.id).Append(", \"merkez\": {\"x\": ").Append(F(k.cx)).Append(", \"y\": ").Append(F(k.cy)).Append(", \"z\": ").Append(F(k.cz))
                  .Append("}, \"boyut\": {\"x\": ").Append(F(k.sx)).Append(", \"y\": ").Append(F(k.sy)).Append(", \"z\": ").Append(F(k.sz)).Append("}, \"yaw_deg\": ").Append(F(k.YawDeg)).Append("}");
            }
            sb.Append(s.kutular.Count > 0 ? "\n  ],\n" : "],\n");
            sb.Append("  \"zemin_poligon\": [");
            for (int i = 0; i + 1 < s.zeminPoligon.Length; i += 2) sb.Append(i == 0 ? "" : ", ").Append("{\"x\": ").Append(F(s.zeminPoligon[i])).Append(", \"z\": ").Append(F(s.zeminPoligon[i + 1])).Append("}");
            sb.Append("],\n  \"isaretler\": [");
            if (isaretler != null)
                for (int i = 0; i < isaretler.Count; i++)
                    sb.Append(i == 0 ? "" : ", ").Append("{\"id\": ").Append((int)isaretler[i][0]).Append(", \"x\": ").Append(F(isaretler[i][1])).Append(", \"y\": ").Append(F(isaretler[i][2])).Append(", \"z\": ").Append(F(isaretler[i][3])).Append("}");
            sb.Append("]\n}\n");
            return sb.ToString();
        }
    }
}
