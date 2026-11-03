using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ARaki.Robot
{
    /// <summary>
    /// Arduino kodundan (robot_kontrol.ino + sarkilar*.h) çıkarılmış sayılar ve melodiler. Sahne kurulurken
    /// Resources/RobotFirmware.txt olarak üretilir (RobotSceneBuilder); böylece şarkı menüsü ve robotsuz
    /// demonun buzzer'ı Arduino'yla hep aynıdır. Biçim:
    ///   SABIT AD DEGER
    ///   MELODI grup|ad
    ///   NOTA hz ms hz ms ...
    /// </summary>
    public class FirmwareData
    {
        public struct Song
        {
            public string Group, Title;
            public int[] Notes;     // hz, ms çiftleri; hz 0 = sessizlik
        }

        public readonly Dictionary<string, int> Constants = new Dictionary<string, int>();
        public readonly List<Song> Songs = new List<Song>();

        public int this[string name] => Constants.TryGetValue(name, out int v) ? v : 0;

        static FirmwareData cached;

        public static FirmwareData Load()
        {
            if (cached != null) return cached;
            var asset = Resources.Load<TextAsset>("RobotFirmware");
            cached = Parse(asset != null ? asset.text : "");
            return cached;
        }

        public static FirmwareData Parse(string text)
        {
            var d = new FirmwareData();
            string group = "", title = "";
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("SABIT "))
                {
                    var p = line.Split(' ');
                    d.Constants[p[1]] = int.Parse(p[2], CultureInfo.InvariantCulture);
                }
                else if (line.StartsWith("MELODI "))
                {
                    var p = line.Substring(7).Split('|');
                    group = p[0];
                    title = p.Length > 1 ? p[1] : p[0];
                }
                else if (line.StartsWith("NOTA"))
                {
                    var p = line.Substring(4).Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                    var notes = new int[p.Length];
                    for (int i = 0; i < p.Length; i++) notes[i] = int.Parse(p[i], CultureInfo.InvariantCulture);
                    d.Songs.Add(new Song { Group = group, Title = title, Notes = notes });
                }
            }
            return d;
        }
    }
}
