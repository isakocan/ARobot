using UnityEngine;

namespace ARaki.AR
{
    /// <summary>
    /// Hologram şekilleri için küçük mesh üreticileri. Köşe rengi alfa'sı kenara doğru söner;
    /// ARaki/Hologram shader'ı bunu okuyarak yumuşak ışık hüzmeleri çizer.
    /// </summary>
    public static class ProceduralMeshes
    {
        /// <summary>Yerde bir yelpaze (görüş sektörü): merkezden uzağa doğru sönen dilim. +Z yönüne bakar.</summary>
        public static Mesh Fan(float radius, float halfAngleDegrees, int segments = 24, float height = 0.004f)
        {
            var vertices = new Vector3[segments + 2];
            var colors = new Color[vertices.Length];
            var normals = new Vector3[vertices.Length];
            vertices[0] = new Vector3(0f, height, 0f);
            colors[0] = new Color(1f, 1f, 1f, 1f);
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.Lerp(-halfAngleDegrees, halfAngleDegrees, i / (float)segments) * Mathf.Deg2Rad;
                vertices[i + 1] = new Vector3(Mathf.Sin(a) * radius, height, Mathf.Cos(a) * radius);
                colors[i + 1] = new Color(1f, 1f, 1f, 0f);
            }
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;

            var triangles = new int[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 2;
                triangles[i * 3 + 2] = i + 1;
            }
            return Build("Fan", vertices, normals, colors, triangles);
        }

        /// <summary>Alttan üste yumuşakça sönen koni: bir objeden yukarı yansıyan ışık hüzmesi.</summary>
        public static Mesh Cone(float bottomRadius, float topRadius, float height, int segments = 28)
        {
            var vertices = new Vector3[(segments + 1) * 2];
            var colors = new Color[vertices.Length];
            var normals = new Vector3[vertices.Length];
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                vertices[i] = dir * bottomRadius;
                vertices[i + segments + 1] = dir * topRadius + Vector3.up * height;
                colors[i] = new Color(1f, 1f, 1f, 1f);
                colors[i + segments + 1] = new Color(1f, 1f, 1f, 0f);
                normals[i] = normals[i + segments + 1] = dir;
            }
            var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                int a = i, b = i + 1, c = i + segments + 1, d = i + segments + 2;
                triangles[i * 6] = a; triangles[i * 6 + 1] = c; triangles[i * 6 + 2] = b;
                triangles[i * 6 + 3] = b; triangles[i * 6 + 4] = c; triangles[i * 6 + 5] = d;
            }
            return Build("Cone", vertices, normals, colors, triangles);
        }

        /// <summary>Yerde yassı bir halka dilimi (sektör göstergesi): iç ve dış yarıçap arasında, açı aralığında.</summary>
        public static Mesh Wedge(float innerRadius, float outerRadius, float fromDegrees, float toDegrees, int segments = 20, float height = 0.005f)
        {
            var vertices = new Vector3[(segments + 1) * 2];
            var colors = new Color[vertices.Length];
            var normals = new Vector3[vertices.Length];
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.Lerp(fromDegrees, toDegrees, i / (float)segments) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                vertices[i * 2] = dir * innerRadius + Vector3.up * height;
                vertices[i * 2 + 1] = dir * outerRadius + Vector3.up * height;
                colors[i * 2] = new Color(1f, 1f, 1f, 0.15f);
                colors[i * 2 + 1] = new Color(1f, 1f, 1f, 0.9f);
                normals[i * 2] = normals[i * 2 + 1] = Vector3.up;
            }
            var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2, b = i * 2 + 1, c = i * 2 + 2, d = i * 2 + 3;
                triangles[i * 6] = a; triangles[i * 6 + 1] = c; triangles[i * 6 + 2] = b;
                triangles[i * 6 + 3] = b; triangles[i * 6 + 4] = c; triangles[i * 6 + 5] = d;
            }
            return Build("Wedge", vertices, normals, colors, triangles);
        }

        /// <summary>Ortası boş, kalın halka (üst yüz + dış ve iç yan yüzler). Üst yüz y = height, taban y = 0.</summary>
        public static Mesh Annulus(float innerRadius, float outerRadius, float height, int segments = 40)
        {
            var v = new System.Collections.Generic.List<Vector3>();
            var n = new System.Collections.Generic.List<Vector3>();
            var c = new System.Collections.Generic.List<Color>();
            var t = new System.Collections.Generic.List<int>();

            void Strip(float r0, float y0, float r1, float y1, bool radialNormal, float sign)
            {
                int start = v.Count;
                for (int i = 0; i <= segments; i++)
                {
                    float a = i / (float)segments * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    v.Add(dir * r0 + Vector3.up * y0);
                    v.Add(dir * r1 + Vector3.up * y1);
                    Vector3 nm = radialNormal ? dir * sign : Vector3.up;
                    n.Add(nm); n.Add(nm);
                    c.Add(Color.white); c.Add(Color.white);
                }
                for (int i = 0; i < segments; i++)
                {
                    int a = start + i * 2, b = a + 1, cc = a + 2, d = a + 3;
                    if (sign >= 0f) { t.Add(a); t.Add(cc); t.Add(b); t.Add(b); t.Add(cc); t.Add(d); }
                    else { t.Add(a); t.Add(b); t.Add(cc); t.Add(b); t.Add(d); t.Add(cc); }
                }
            }

            Strip(innerRadius, height, outerRadius, height, false, 1f);     // üst
            Strip(outerRadius, 0f, outerRadius, height, true, 1f);          // dış duvar
            Strip(innerRadius, 0f, innerRadius, height, true, -1f);         // iç duvar (içe bakar)
            return Build("Annulus", v.ToArray(), n.ToArray(), c.ToArray(), t.ToArray());
        }

        static Mesh Build(string name, Vector3[] vertices, Vector3[] normals, Color[] colors, int[] triangles)
        {
            var mesh = new Mesh { name = name, vertices = vertices, normals = normals, colors = colors, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
