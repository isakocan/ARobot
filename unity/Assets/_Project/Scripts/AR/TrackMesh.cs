using System.Collections.Generic;
using UnityEngine;

namespace ARaki.AR
{
    /// <summary>
    /// Yarış pistinin geometrisi: kapılardan geçen kapalı bir eğri (Catmull-Rom) ve onun üstüne serilen
    /// şerit mesh'i (asfalt, kırmızı-beyaz kerb, kesik orta çizgi, kenar çizgileri, başlangıç çizgisi).
    /// Mesh dünya koordinatlarında kurulur; AR kökünde robota göre kaydırılır.
    /// </summary>
    public static class TrackMesh
    {
        public const float HalfWidth = 0.27f;
        const float KerbWidth = 0.06f;

        /// <summary>Kapı noktalarından geçen kapalı eğriyi örnekler. gateIndex[i] = i. kapının örnek sırası.</summary>
        public static Vector3[] SamplePath(Vector3[] gates, out int[] gateIndex)
        {
            int n = gates.Length;
            var list = new List<Vector3>();
            gateIndex = new int[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 p0 = gates[(i - 1 + n) % n], p1 = gates[i], p2 = gates[(i + 1) % n], p3 = gates[(i + 2) % n];
                int steps = Mathf.Max(8, Mathf.CeilToInt(Vector3.Distance(p1, p2) / 0.08f));
                gateIndex[i] = list.Count;
                for (int s = 0; s < steps; s++)
                {
                    var p = CatmullRom(p0, p1, p2, p3, s / (float)steps);
                    p.y = 0f;
                    list.Add(p);
                }
            }
            return list.ToArray();
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        public static float Length(Vector3[] path)
        {
            float len = 0f;
            for (int i = 0; i < path.Length; i++) len += Vector3.Distance(path[i], path[(i + 1) % path.Length]);
            return len;
        }

        public static Vector3 Tangent(Vector3[] path, int index)
        {
            int n = path.Length;
            var t = path[(index + 1) % n] - path[(index - 1 + n) % n];
            t.y = 0f;
            return t.sqrMagnitude < 1e-6f ? Vector3.forward : t.normalized;
        }

        /// <summary>Soldan sağa: (tangent'e dik) sağ yön.</summary>
        public static Vector3 Right(Vector3 tangent) => new Vector3(tangent.z, 0f, -tangent.x);

        public static Mesh Build(Vector3[] path, int startIndex)
        {
            var v = new List<Vector3>(path.Length * 24);
            var c = new List<Color>(path.Length * 24);
            var uv = new List<Vector2>(path.Length * 24);
            var tri = new List<int>(path.Length * 36);

            int n = path.Length;
            var dist = new float[n + 1];
            for (int i = 0; i < n; i++) dist[i + 1] = dist[i] + Vector3.Distance(path[i], path[(i + 1) % n]);

            var asphalt = new Color(0.035f, 0.04f, 0.05f, 0.62f);
            var red = new Color(0.95f, 0.1f, 0.08f, 0.9f);
            var white = new Color(0.95f, 0.95f, 0.92f, 0.9f);
            var line = new Color(0.98f, 0.9f, 0.55f, 0.85f);

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector3 ti = Tangent(path, i), tj = Tangent(path, j);
                Vector3 ri = Right(ti), rj = Right(tj);
                float d0 = dist[i], d1 = dist[i + 1];

                Strip(v, c, uv, tri, path[i], path[j], ri, rj, -HalfWidth, HalfWidth, 0.003f, asphalt, d0, d1, 1f);
                bool kerbRed = ((int)(d0 / 0.14f) & 1) == 0;
                var kerb = kerbRed ? red : white;
                Strip(v, c, uv, tri, path[i], path[j], ri, rj, HalfWidth, HalfWidth + KerbWidth, 0.004f, kerb, d0, d1, 0f);
                Strip(v, c, uv, tri, path[i], path[j], ri, rj, -HalfWidth - KerbWidth, -HalfWidth, 0.004f, kerb, d0, d1, 0f);
                // kenar çizgileri
                Strip(v, c, uv, tri, path[i], path[j], ri, rj, HalfWidth - 0.03f, HalfWidth - 0.014f, 0.005f, line, d0, d1, 0f);
                Strip(v, c, uv, tri, path[i], path[j], ri, rj, -HalfWidth + 0.014f, -HalfWidth + 0.03f, 0.005f, line, d0, d1, 0f);
                // kesik orta çizgi
                if (((int)(d0 / 0.15f) & 1) == 0)
                    Strip(v, c, uv, tri, path[i], path[j], ri, rj, -0.011f, 0.011f, 0.005f, line, d0, d1, 0f);
            }

            // başlangıç / bitiş çizgisi: dama deseni
            Vector3 sp = path[startIndex];
            Vector3 st = Tangent(path, startIndex);
            Vector3 sr = Right(st);
            const int cols = 10;
            float cell = HalfWidth * 2f / cols;
            for (int row = 0; row < 2; row++)
                for (int col = 0; col < cols; col++)
                {
                    var color = ((row + col) & 1) == 0 ? new Color(0.97f, 0.97f, 0.95f, 0.95f) : new Color(0.02f, 0.02f, 0.02f, 0.95f);
                    float x0 = -HalfWidth + col * cell, x1 = x0 + cell;
                    float z0 = (row - 1) * cell, z1 = z0 + cell;
                    Quad(v, c, uv, tri,
                        sp + sr * x0 + st * z0 + Vector3.up * 0.006f, sp + sr * x1 + st * z0 + Vector3.up * 0.006f,
                        sp + sr * x1 + st * z1 + Vector3.up * 0.006f, sp + sr * x0 + st * z1 + Vector3.up * 0.006f,
                        color, dist[startIndex], dist[startIndex], 0f);
                }

            var mesh = new Mesh { name = "RaceTrack" };
            if (v.Count > 60000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(v);
            mesh.SetColors(c);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void Strip(List<Vector3> v, List<Color> c, List<Vector2> uv, List<int> tri,
                          Vector3 a, Vector3 b, Vector3 ra, Vector3 rb, float x0, float x1, float y, Color color, float d0, float d1, float mask)
        {
            Quad(v, c, uv, tri,
                a + ra * x0 + Vector3.up * y, a + ra * x1 + Vector3.up * y,
                b + rb * x1 + Vector3.up * y, b + rb * x0 + Vector3.up * y,
                color, d0, d1, mask);
        }

        static void Quad(List<Vector3> v, List<Color> c, List<Vector2> uv, List<int> tri,
                         Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Color color, float d0, float d1, float mask)
        {
            int k = v.Count;
            v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
            for (int i = 0; i < 4; i++) c.Add(color);
            uv.Add(new Vector2(d0, mask)); uv.Add(new Vector2(d0, mask)); uv.Add(new Vector2(d1, mask)); uv.Add(new Vector2(d1, mask));
            // çift yüzlü çizim için iki yönde de üçgen gerekmez (Cull Off), tek yön yeter
            tri.Add(k); tri.Add(k + 2); tri.Add(k + 1);
            tri.Add(k); tri.Add(k + 3); tri.Add(k + 2);
        }

        /// <summary>Dikey kapı perdesi (altı saydam, üstü parlak) ve başlangıç kapısı için dama bayrağı.</summary>
        public static Mesh Curtain(float width, float height)
        {
            var mesh = new Mesh { name = "GateCurtain" };
            float h = width * 0.5f;
            mesh.vertices = new[] { new Vector3(-h, 0f, 0f), new Vector3(h, 0f, 0f), new Vector3(h, height, 0f), new Vector3(-h, height, 0f) };
            mesh.colors = new[] { new Color(1, 1, 1, 0.0f), new Color(1, 1, 1, 0.0f), new Color(1, 1, 1, 0.9f), new Color(1, 1, 1, 0.9f) };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            return mesh;
        }

        public static Mesh CheckerBanner(float width, float height, int cols)
        {
            var v = new List<Vector3>();
            var c = new List<Color>();
            var n = new List<Vector3>();
            var tri = new List<int>();
            float cw = width / cols, ch = height * 0.5f;
            for (int row = 0; row < 2; row++)
                for (int col = 0; col < cols; col++)
                {
                    int k = v.Count;
                    float x0 = -width * 0.5f + col * cw, y0 = row * ch;
                    v.Add(new Vector3(x0, y0, 0f)); v.Add(new Vector3(x0 + cw, y0, 0f));
                    v.Add(new Vector3(x0 + cw, y0 + ch, 0f)); v.Add(new Vector3(x0, y0 + ch, 0f));
                    var color = ((row + col) & 1) == 0 ? Color.white : new Color(0f, 0f, 0f, 1f);
                    for (int i = 0; i < 4; i++) { c.Add(color); n.Add(Vector3.back); }
                    tri.Add(k); tri.Add(k + 2); tri.Add(k + 1);
                    tri.Add(k); tri.Add(k + 3); tri.Add(k + 2);
                }
            var mesh = new Mesh { name = "CheckerBanner" };
            mesh.SetVertices(v);
            mesh.SetColors(c);
            mesh.SetNormals(n);
            mesh.SetTriangles(tri, 0);
            return mesh;
        }
    }
}
