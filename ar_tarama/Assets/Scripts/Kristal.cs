using System.Collections.Generic;
using UnityEngine;

namespace ArTarama
{
    /// <summary>Dusuk poligonlu kristal + yumusak temas golgesi. Yavasca doner, hafifce yuzer.</summary>
    public class Kristal : MonoBehaviour
    {
        public const float Yukseklik = 0.12f, Havada = 0.08f;
        Transform govde;
        float faz;
        static Texture2D golgeDoku;
        static Mesh kristalMesh, golgeMesh;

        public static GameObject Olustur(Transform ebeveyn)
        {
            var kok = new GameObject("Kristal");
            kok.transform.SetParent(ebeveyn, false);
            var k = kok.AddComponent<Kristal>();

            var g = new GameObject("Govde");
            g.transform.SetParent(kok.transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = KristalMesh();
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Resources.Load<Material>("mat_kristal");
            k.govde = g.transform;
            g.transform.localPosition = new Vector3(0, Havada + Yukseklik * 0.5f, 0);

            var s = new GameObject("Golge");
            s.transform.SetParent(kok.transform, false);
            s.transform.localPosition = new Vector3(0, 0.002f, 0);
            s.AddComponent<MeshFilter>().sharedMesh = GolgeMesh();
            var smr = s.AddComponent<MeshRenderer>();
            var m = new Material(Resources.Load<Material>("mat_golge"));
            m.mainTexture = GolgeDoku();
            smr.sharedMaterial = m;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            smr.receiveShadows = false;
            k.faz = Random.value * 6.28f;
            return kok;
        }

        void Update()
        {
            if (govde == null) return;
            govde.Rotate(0, 30f * Time.deltaTime, 0, Space.Self);
            float y = Havada + Yukseklik * 0.5f + Mathf.Sin(Time.time * 1.6f + faz) * 0.008f;
            govde.localPosition = new Vector3(0, y, 0);
        }

        static Mesh KristalMesh()
        {
            if (kristalMesh != null) return kristalMesh;
            const int n = 6;
            float h = Yukseklik * 0.5f, r = 0.04f, kh = 0.02f;
            var ust = new Vector3(0, h, 0); var alt = new Vector3(0, -h, 0);
            var ru = new Vector3[n]; var rb = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                float rr = r * (i % 2 == 0 ? 1f : 0.86f);
                ru[i] = new Vector3(Mathf.Cos(a) * rr, kh, Mathf.Sin(a) * rr);
                rb[i] = new Vector3(Mathf.Cos(a) * rr, -kh, Mathf.Sin(a) * rr);
            }
            var v = new List<Vector3>();
            var t = new List<int>();
            void Uc(Vector3 a, Vector3 b, Vector3 c)
            {
                int i0 = v.Count; v.Add(a); v.Add(b); v.Add(c);
                t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2);
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Uc(ust, ru[j], ru[i]);
                Uc(ru[i], ru[j], rb[j]); Uc(ru[i], rb[j], rb[i]);
                Uc(alt, rb[i], rb[j]);
            }
            kristalMesh = new Mesh { name = "KristalMesh" };
            kristalMesh.SetVertices(v); kristalMesh.SetTriangles(t, 0);
            kristalMesh.RecalculateNormals(); kristalMesh.RecalculateBounds();
            return kristalMesh;
        }

        static Mesh GolgeMesh()
        {
            if (golgeMesh != null) return golgeMesh;
            float s = 0.13f;
            golgeMesh = new Mesh { name = "GolgeMesh" };
            golgeMesh.vertices = new[] { new Vector3(-s, 0, -s), new Vector3(-s, 0, s), new Vector3(s, 0, s), new Vector3(s, 0, -s) };
            golgeMesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            golgeMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            golgeMesh.RecalculateBounds();
            return golgeMesh;
        }

        static Texture2D GolgeDoku()
        {
            if (golgeDoku != null) return golgeDoku;
            const int n = 64;
            golgeDoku = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a);
                    golgeDoku.SetPixel(x, y, new Color(0, 0, 0, a));
                }
            golgeDoku.Apply();
            return golgeDoku;
        }
    }
}
