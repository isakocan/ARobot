using UnityEngine;

namespace ARaki.Video
{
    /// <summary>
    /// Equirectangular (360°) görüntüyü içten kaplamak için küre ağı. UV'ler görüntü doğrudan eşlenecek şekilde
    /// üretilir: merkezde "ileri" (+Z), sağa doğru +X, yukarı doğru yükseklik.
    /// </summary>
    public static class EquirectSphere
    {
        public static Mesh Create(float radius = 50f, int columns = 96, int rows = 48)
        {
            var vertices = new Vector3[(columns + 1) * (rows + 1)];
            var uvs = new Vector2[vertices.Length];
            for (int i = 0; i <= rows; i++)
            {
                float v = (float)i / rows;
                float lat = (v - 0.5f) * Mathf.PI;
                for (int j = 0; j <= columns; j++)
                {
                    float u = (float)j / columns;
                    float yaw = (u - 0.5f) * Mathf.PI * 2f;
                    int k = i * (columns + 1) + j;
                    vertices[k] = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(lat), Mathf.Sin(lat), Mathf.Cos(yaw) * Mathf.Cos(lat)) * radius;
                    uvs[k] = new Vector2(u, v);
                }
            }

            var triangles = new int[columns * rows * 6];
            int t = 0;
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < columns; j++)
                {
                    int a = i * (columns + 1) + j;
                    int b = a + columns + 1;
                    triangles[t++] = a; triangles[t++] = b; triangles[t++] = a + 1;
                    triangles[t++] = a + 1; triangles[t++] = b; triangles[t++] = b + 1;
                }

            var mesh = new Mesh { name = "EquirectSphere", vertices = vertices, uv = uvs, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
