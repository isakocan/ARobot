using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ARaki.Drive
{
    /// <summary>
    /// Koddan çizilen düz şekiller (kokpit, göstergeler, direksiyon). Doku kullanmaz; her çözünürlükte keskin kalır.
    /// Koordinatlar bu RectTransform'un yerel pikselleri (pivot noktası 0,0).
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class VectorShape : MaskableGraphic
    {
        struct Tri { public Vector2 a, b, c; public Color32 ca, cb, cc; }

        readonly List<Tri> tris = new List<Tri>();

        public VectorShape Clear() { tris.Clear(); SetVerticesDirty(); return this; }

        void Add(Vector2 a, Vector2 b, Vector2 c, Color ca, Color cb, Color cc) =>
            tris.Add(new Tri { a = a, b = b, c = c, ca = ca, cb = cb, cc = cc });

        void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color ca, Color cb, Color cc, Color cd)
        {
            Add(a, b, c, ca, cb, cc);
            Add(a, c, d, ca, cc, cd);
        }

        /// <summary>Dışbükey çokgen (yelpaze üçgenleme).</summary>
        public VectorShape Polygon(IList<Vector2> p, Color c)
        {
            for (int i = 1; i < p.Count - 1; i++) Add(p[0], p[i], p[i + 1], c, c, c);
            SetVerticesDirty();
            return this;
        }

        /// <summary>Üst kenarı bir çizgi olan, aşağıya bottomY'ye kadar dolu şerit (içbükey üst kenar da olur).</summary>
        public VectorShape Band(IList<Vector2> top, float bottomY, Color topColor, Color bottomColor)
        {
            for (int i = 0; i < top.Count - 1; i++)
            {
                Vector2 a = top[i], b = top[i + 1];
                Quad(new Vector2(a.x, bottomY), a, b, new Vector2(b.x, bottomY), bottomColor, topColor, topColor, bottomColor);
            }
            SetVerticesDirty();
            return this;
        }

        /// <summary>Halka parçası; açılar derece, 0 = yukarı, saat yönünde artar.</summary>
        public VectorShape Arc(Vector2 center, float rIn, float rOut, float from, float to, Color c, int segments = 48)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(segments * Mathf.Abs(to - from) / 360f));
            for (int i = 0; i < n; i++)
            {
                float a0 = Mathf.Lerp(from, to, i / (float)n) * Mathf.Deg2Rad;
                float a1 = Mathf.Lerp(from, to, (i + 1) / (float)n) * Mathf.Deg2Rad;
                Vector2 d0 = new Vector2(Mathf.Sin(a0), Mathf.Cos(a0)), d1 = new Vector2(Mathf.Sin(a1), Mathf.Cos(a1));
                Quad(center + d0 * rIn, center + d0 * rOut, center + d1 * rOut, center + d1 * rIn, c, c, c, c);
            }
            SetVerticesDirty();
            return this;
        }

        public VectorShape Circle(Vector2 center, float r, Color c, int segments = 48) => Disc(center, r, c, c, segments);

        /// <summary>Ortası ile kenarı farklı renkte daire (hafif derinlik).</summary>
        public VectorShape Disc(Vector2 center, float r, Color inner, Color outer, int segments = 48)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                Add(center, center + new Vector2(Mathf.Sin(a0), Mathf.Cos(a0)) * r, center + new Vector2(Mathf.Sin(a1), Mathf.Cos(a1)) * r,
                    inner, outer, outer);
            }
            SetVerticesDirty();
            return this;
        }

        public VectorShape Line(Vector2 a, Vector2 b, float width, Color c)
        {
            Vector2 n = (b - a).normalized;
            n = new Vector2(-n.y, n.x) * (width * 0.5f);
            Quad(a - n, a + n, b + n, b - n, c, c, c, c);
            SetVerticesDirty();
            return this;
        }

        /// <summary>Köşeleri yuvarlatılmış dikdörtgen (dolu).</summary>
        public VectorShape RoundRect(Rect r, float radius, Color c)
        {
            radius = Mathf.Min(radius, r.width / 2f, r.height / 2f);
            var pts = new List<Vector2>();
            Vector2[] corners = { new Vector2(r.xMax - radius, r.yMax - radius), new Vector2(r.xMax - radius, r.yMin + radius),
                                  new Vector2(r.xMin + radius, r.yMin + radius), new Vector2(r.xMin + radius, r.yMax - radius) };
            for (int k = 0; k < 4; k++)
                for (int i = 0; i <= 6; i++)
                {
                    float a = (k * 90f + i * 15f) * Mathf.Deg2Rad;
                    pts.Add(corners[k] + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * radius);
                }
            return Polygon(pts, c);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Color tint = color;
            foreach (var t in tris)
            {
                int i = vh.currentVertCount;
                vh.AddVert(t.a, Mul(t.ca, tint), Vector2.zero);
                vh.AddVert(t.b, Mul(t.cb, tint), Vector2.zero);
                vh.AddVert(t.c, Mul(t.cc, tint), Vector2.zero);
                vh.AddTriangle(i, i + 1, i + 2);
            }
        }

        static Color32 Mul(Color32 a, Color b) => (Color)a * b;
    }
}
