using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ArTarama
{
    /// <summary>Kucuk JSON okuyucu (Dictionary / List / string / double / bool / null).</summary>
    public static class MiniJson
    {
        public static object Parse(string s) { int i = 0; return Oku(s, ref i); }

        static void Bosluk(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Oku(string s, ref int i)
        {
            Bosluk(s, ref i);
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(); i++; Bosluk(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Bosluk(s, ref i); string k = (string)Oku(s, ref i); Bosluk(s, ref i); i++; // :
                    d[k] = Oku(s, ref i); Bosluk(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++; return d;
                }
            }
            if (c == '[')
            {
                var l = new List<object>(); i++; Bosluk(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Oku(s, ref i)); Bosluk(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++; return l;
                }
            }
            if (c == '"')
            {
                var sb = new StringBuilder(); i++;
                while (s[i] != '"')
                {
                    if (s[i] == '\\') { i++; char e = s[i]; sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e == 'u' ? (char)Convert.ToInt32(s.Substring(i + 1, 4), 16) : e); if (e == 'u') i += 4; }
                    else sb.Append(s[i]);
                    i++;
                }
                i++; return sb.ToString();
            }
            if (c == 't') { i += 4; return true; }
            if (c == 'f') { i += 5; return false; }
            if (c == 'n') { i += 4; return null; }
            int b = i; while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(b, i - b), CultureInfo.InvariantCulture);
        }

        public static double D(object o) { return o is double d ? d : 0.0; }
        public static string S(object o) { return o as string ?? ""; }
        public static bool B(object o) { return o is bool b && b; }
        public static object Al(object o, string k) { return o is Dictionary<string, object> d && d.TryGetValue(k, out var v) ? v : null; }
    }

    /// <summary>oda.json yazici ve okuyucu. Birim metre, Unity oturum uzayi. durum: "aday" | "kesin".</summary>
    public static class OdaJson
    {
        static readonly CultureInfo ci = CultureInfo.InvariantCulture;
        static string F(float v) { return v.ToString("0.####", ci); }
        static string Durum(bool kesin) { return kesin ? "\"kesin\"" : "\"aday\""; }

        /// <summary>isaretler: id, x, y, z (null olabilir).</summary>
        public static string Yaz(OdaSonuc s, List<float[]> isaretler, string ad = null)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"surum\": 2,\n  \"koordinat\": \"Unity oturum uzayi (metre, y yukari); yaw_deg = Quaternion.Euler(0,yaw,0), boyut.x yerel x (uzun kenar), boyut.z yerel z\",\n");
            if (!string.IsNullOrEmpty(ad)) sb.Append("  \"ad\": \"").Append(ad.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\",\n");
            sb.Append("  \"floor_y\": ").Append(F(s.zeminY)).Append(",\n  \"tavan_y\": ").Append(F(s.tavanY)).Append(",\n  \"tavan_gercek\": ").Append(s.tavanGercek ? "true" : "false")
              .Append(",\n  \"tavan_tahmin\": ").Append(s.tavanGercek ? "false" : "true").Append(",\n");
            sb.Append("  \"manhattan_aci\": ").Append(s.yonVar ? F(s.baskinAci) : "null").Append(",\n");
            sb.Append("  \"alan_m2\": ").Append(F(s.alan)).Append(",\n  \"zemin_kapali\": ").Append(s.zeminKapali ? "true" : "false").Append(",\n");
            sb.Append("  \"walls\": [");
            for (int i = 0; i < s.duvarlar.Count; i++)
            {
                var d = s.duvarlar[i];
                sb.Append(i == 0 ? "\n" : ",\n").Append("    {\"id\": ").Append(d.id).Append(", \"durum\": ").Append(Durum(d.kesin)).Append(", \"a\": {\"x\": ").Append(F(d.ax)).Append(", \"z\": ").Append(F(d.az)).Append("}, \"b\": {\"x\": ").Append(F(d.bx)).Append(", \"z\": ").Append(F(d.bz))
                  .Append("}, \"yukseklik\": ").Append(F(d.yukseklik)).Append(", \"uzunluk\": ").Append(F(d.Uzunluk)).Append(", \"normal\": {\"x\": ").Append(F(d.nx)).Append(", \"z\": ").Append(F(d.nz)).Append("}}");
            }
            sb.Append(s.duvarlar.Count > 0 ? "\n  ],\n" : "],\n");
            sb.Append("  \"boxes\": [");
            for (int i = 0; i < s.kutular.Count; i++)
            {
                var k = s.kutular[i];
                sb.Append(i == 0 ? "\n" : ",\n").Append("    {\"id\": ").Append(k.id).Append(", \"durum\": ").Append(Durum(k.kesin)).Append(", \"merkez\": {\"x\": ").Append(F(k.cx)).Append(", \"y\": ").Append(F(k.cy)).Append(", \"z\": ").Append(F(k.cz))
                  .Append("}, \"boyut\": {\"x\": ").Append(F(k.sx)).Append(", \"y\": ").Append(F(k.sy)).Append(", \"z\": ").Append(F(k.sz)).Append("}, \"yaw_deg\": ").Append(F(k.YawDeg)).Append(", \"aci\": ").Append(F(k.aci)).Append("}");
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

        /// <summary>oda.json -> OdaSonuc (gorsel gosterim icin; doluluk izgarasi yok). v1 dosyalarinda tum nesneler kesin sayilir.</summary>
        public static OdaSonuc Oku(string json, out string ad)
        {
            var kok = MiniJson.Parse(json); ad = MiniJson.S(MiniJson.Al(kok, "ad"));
            var s = new OdaSonuc { zeminVar = true };
            s.zeminY = (float)MiniJson.D(MiniJson.Al(kok, "floor_y")); s.tavanY = (float)MiniJson.D(MiniJson.Al(kok, "tavan_y"));
            s.tavanGercek = MiniJson.B(MiniJson.Al(kok, "tavan_gercek"));
            var man = MiniJson.Al(kok, "manhattan_aci"); if (man is double md) { s.yonVar = true; s.baskinAci = (float)md; }
            s.alan = (float)MiniJson.D(MiniJson.Al(kok, "alan_m2")); s.zeminKapali = MiniJson.B(MiniJson.Al(kok, "zemin_kapali"));
            bool v1 = MiniJson.Al(kok, "surum") is double sv && sv < 2;
            if (MiniJson.Al(kok, "walls") is List<object> wl)
                foreach (var o in wl)
                {
                    var a = MiniJson.Al(o, "a"); var b = MiniJson.Al(o, "b"); var n = MiniJson.Al(o, "normal");
                    s.duvarlar.Add(new OdaDuvar
                    {
                        id = (int)MiniJson.D(MiniJson.Al(o, "id")), kesin = v1 || MiniJson.S(MiniJson.Al(o, "durum")) == "kesin", ilerleme = 1f,
                        ax = (float)MiniJson.D(MiniJson.Al(a, "x")), az = (float)MiniJson.D(MiniJson.Al(a, "z")), bx = (float)MiniJson.D(MiniJson.Al(b, "x")), bz = (float)MiniJson.D(MiniJson.Al(b, "z")),
                        nx = (float)MiniJson.D(MiniJson.Al(n, "x")), nz = (float)MiniJson.D(MiniJson.Al(n, "z")), yukseklik = (float)MiniJson.D(MiniJson.Al(o, "yukseklik")),
                        aKose = true, bKose = true
                    });
                }
            if (MiniJson.Al(kok, "boxes") is List<object> bl)
                foreach (var o in bl)
                {
                    var m = MiniJson.Al(o, "merkez"); var z = MiniJson.Al(o, "boyut");
                    var k = new OdaKutu
                    {
                        id = (int)MiniJson.D(MiniJson.Al(o, "id")), kesin = v1 || MiniJson.S(MiniJson.Al(o, "durum")) == "kesin", ilerleme = 1f,
                        cx = (float)MiniJson.D(MiniJson.Al(m, "x")), cy = (float)MiniJson.D(MiniJson.Al(m, "y")), cz = (float)MiniJson.D(MiniJson.Al(m, "z")),
                        sx = (float)MiniJson.D(MiniJson.Al(z, "x")), sy = (float)MiniJson.D(MiniJson.Al(z, "y")), sz = (float)MiniJson.D(MiniJson.Al(z, "z"))
                    };
                    if (MiniJson.Al(o, "aci") is double ad2) k.aci = (float)ad2; else { float y = (float)MiniJson.D(MiniJson.Al(o, "yaw_deg")); k.aci = (float)(((-y) % 180.0 + 180.0) % 180.0); }
                    s.kutular.Add(k);
                }
            if (MiniJson.Al(kok, "zemin_poligon") is List<object> pl)
            {
                var p = new List<float>(); foreach (var o in pl) { p.Add((float)MiniJson.D(MiniJson.Al(o, "x"))); p.Add((float)MiniJson.D(MiniJson.Al(o, "z"))); }
                s.zeminPoligon = p.ToArray();
            }
            if (s.alan <= 0 && s.zeminPoligon.Length >= 6)
            {
                var l = new List<double[]>(); for (int i = 0; i + 1 < s.zeminPoligon.Length; i += 2) l.Add(new double[] { s.zeminPoligon[i], s.zeminPoligon[i + 1] });
                s.alan = (float)Math.Abs(OdaGeo.Alan2D(l));
            }
            return s;
        }
    }
}
