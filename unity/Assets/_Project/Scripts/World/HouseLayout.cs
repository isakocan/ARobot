using System.Collections.Generic;
using UnityEngine;

namespace ARaki.World
{
    public enum RoomKind { Hall, Living, Kitchen, Bedroom, Bath, Study }

    /// <summary>İki oda arasındaki geçit. AlongX: duvar x ekseni boyunca uzanır (kapı z = sabit bir duvarda).</summary>
    public readonly struct Door
    {
        public readonly Vector3 Center;
        public readonly bool AlongX;
        public readonly float Width;

        public Door(float x, float z, bool alongX, float width = 1.0f)
        {
            Center = new Vector3(x, 0f, z);
            AlongX = alongX;
            Width = width;
        }
    }

    public class RoomInfo
    {
        public string Id;
        public string Name;
        public RoomKind Kind;
        public Rect Rect;               // x = xmin, y = zmin
        public int TargetFinds;         // odada aynı anda bulunacak bulgu sayısı
        public float NextRefillTime;    // boşalan odanın yeniden dolacağı an

        public Vector3 Center => new Vector3(Rect.center.x, 0f, Rect.center.y);
        public float Area => Rect.width * Rect.height;
        public bool Contains(Vector3 p, float margin = 0f) =>
            p.x > Rect.xMin + margin && p.x < Rect.xMax - margin && p.z > Rect.yMin + margin && p.z < Rect.yMax - margin;
    }

    /// <summary>
    /// Evin planı: koridorun iki yanında odalar. Hem sahne kurucusu (duvarlar, kapılar, eşyalar) hem de oyun
    /// (hangi odada olduğun, bulguların nerede çıkabileceği) bu tek kaynaktan beslenir. Birimler metre; +Z kuzey.
    ///
    ///   kuzey  |  YATAK ODASI  |  BANYO  |       MUTFAK        |
    ///          +--- kapı ------+- kapı --+------- kapı ---------+
    ///          |                  KORİDOR                      |
    ///          +------ geniş açıklık ------+------- kapı -------+
    ///   güney  |             SALON         |  ÇALIŞMA ODASI     |
    /// </summary>
    public static class HouseLayout
    {
        public const float WallHeight = 2.5f;
        public const float WallThickness = 0.06f;

        public static readonly Rect Bounds = new Rect(-5.5f, -4.7f, 11f, 8.9f);
        public static readonly Vector3 BasePosition = new Vector3(-2.7f, 0f, -1.7f);
        public static readonly float BaseYaw = 20f;

        public static readonly RoomInfo Hall = new RoomInfo { Id = "hall", Name = "KORİDOR", Kind = RoomKind.Hall, Rect = new Rect(-5.5f, -0.7f, 11f, 1.4f), TargetFinds = 1 };
        public static readonly RoomInfo Bedroom = new RoomInfo { Id = "bedroom", Name = "YATAK ODASI", Kind = RoomKind.Bedroom, Rect = new Rect(-5.5f, 0.7f, 3.8f, 3.5f), TargetFinds = 2 };
        public static readonly RoomInfo Bath = new RoomInfo { Id = "bath", Name = "BANYO", Kind = RoomKind.Bath, Rect = new Rect(-1.7f, 0.7f, 2.7f, 3.5f), TargetFinds = 1 };
        public static readonly RoomInfo Kitchen = new RoomInfo { Id = "kitchen", Name = "MUTFAK", Kind = RoomKind.Kitchen, Rect = new Rect(1.0f, 0.7f, 4.5f, 3.5f), TargetFinds = 2 };
        public static readonly RoomInfo Living = new RoomInfo { Id = "living", Name = "SALON", Kind = RoomKind.Living, Rect = new Rect(-5.5f, -4.7f, 7.3f, 4.0f), TargetFinds = 2 };
        public static readonly RoomInfo Study = new RoomInfo { Id = "study", Name = "ÇALIŞMA ODASI", Kind = RoomKind.Study, Rect = new Rect(1.8f, -4.7f, 3.7f, 4.0f), TargetFinds = 2 };

        public static readonly IReadOnlyList<RoomInfo> Rooms = new[] { Hall, Bedroom, Bath, Kitchen, Living, Study };

        /// <summary>Her kapı iki odanın ortak duvarındadır; koridor-oda kapıları z = ±0.7'de.</summary>
        public static readonly IReadOnlyList<Door> Doors = new[]
        {
            new Door(-3.6f, 0.7f, true),            // koridor - yatak odası
            new Door(-0.35f, 0.7f, true),           // koridor - banyo
            new Door(3.2f, 0.7f, true),             // koridor - mutfak
            new Door(-1.8f, -0.7f, true, 1.7f),     // koridor - salon (geniş açıklık)
            new Door(3.6f, -0.7f, true),            // koridor - çalışma odası
            new Door(1.8f, -2.9f, false),           // salon - çalışma odası
        };

        public static RoomInfo RoomAt(Vector3 p)
        {
            foreach (var r in Rooms) if (r.Contains(p)) return r;
            // duvar kalınlığı/kapı eşiğinde: en yakın odayı seç
            RoomInfo best = null;
            float bestDist = float.MaxValue;
            foreach (var r in Rooms)
            {
                float dx = Mathf.Max(r.Rect.xMin - p.x, 0f, p.x - r.Rect.xMax);
                float dz = Mathf.Max(r.Rect.yMin - p.z, 0f, p.z - r.Rect.yMax);
                float d = dx * dx + dz * dz;
                if (d < bestDist) { bestDist = d; best = r; }
            }
            return best;
        }

        public static bool NearDoor(Vector3 p, float distance)
        {
            foreach (var d in Doors)
                if (Vector3.Distance(new Vector3(p.x, 0f, p.z), d.Center) < distance) return true;
            return false;
        }

        /// <summary>Bir odanın kapı açıklıkları (duvar parçalarını kesmek için).</summary>
        public static List<Door> DoorsOnWall(bool alongX, float fixedCoordinate)
        {
            var list = new List<Door>();
            foreach (var d in Doors)
            {
                if (d.AlongX != alongX) continue;
                float c = alongX ? d.Center.z : d.Center.x;
                if (Mathf.Abs(c - fixedCoordinate) < 0.01f) list.Add(d);
            }
            return list;
        }
    }
}
