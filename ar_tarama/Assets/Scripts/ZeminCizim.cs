using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArTarama
{
    /// <summary>
    /// AR goruntusunde acik zemin: birlesik, yumusak kenarli turkuaz alan + ince parlak kenar cizgisi; engel alanlari
    /// cok soluk. Duvar arkasi (GorusTablosu) ve 8 m otesi cizilmez: baska odanin zemini duvarin icinden gorunmez.
    /// Geometri yeni sonuc gelince (1 Hz) kurulur, gorunurluk 5 Hz'de koselerin rengine yazilir.
    /// </summary>
    public class ZeminCizim
    {
        readonly GameObject go; readonly Mesh mesh; readonly MeshRenderer mr;
        readonly List<Vector3> v = new List<Vector3>(); readonly List<Color> c = new List<Color>(); readonly List<float> taban = new List<float>();
        readonly List<int> t = new List<int>();
        Color[] renk = new Color[0];
        ZeminSonuc kurulan; float sonGorus = -1f;
        public readonly GorusTablosu Gorus = new GorusTablosu();

        static readonly Color Acik = new Color(0.26f, 0.85f, 0.75f, 0.20f), Kenar = new Color(0.62f, 1f, 0.92f, 0.85f), Engel = new Color(1f, 0.62f, 0.58f, 0.10f);
        const float KenarGen = 0.018f, Menzil = 8f, KurMenzil = 12f;

        public ZeminCizim(Material dolgu)
        {
            mesh = new Mesh { name = "AcikZemin", indexFormat = IndexFormat.UInt32 }; mesh.MarkDynamic();
            go = new GameObject("AcikZemin"); go.layer = OdaYoneticisi.KatmanAr;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = dolgu != null ? new Material(dolgu) : new Material(Shader.Find("ArTarama/OdaDolgu"));
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.allowOcclusionWhenDynamic = false;
        }

        public void Yok() { Object.Destroy(go); Object.Destroy(mesh); }
        public void Temizle() { mesh.Clear(); kurulan = null; Gorus.Hazir = false; }

        Vector3 kurKam;
        void Kur(ZeminSonuc s, float y, Vector3 kam)
        {
            kurKam = kam;
            v.Clear(); c.Clear(); t.Clear(); taban.Clear();
            Ucgenler(s.engelUcgen, y + 0.004f, Engel);
            Ucgenler(s.acikUcgen, y + 0.006f, Acik);
            var k = s.acikKenar;
            for (int i = 0; i + 3 < k.Length; i += 4)
            {
                Vector2 a = new Vector2(k[i], k[i + 1]), b = new Vector2(k[i + 2], k[i + 3]);
                if ((a - new Vector2(kam.x, kam.z)).sqrMagnitude > KurMenzil * KurMenzil) continue;
                Vector2 d = b - a; float L = d.magnitude; if (L < 1e-4f) continue;
                Vector2 n = new Vector2(-d.y, d.x) / L * (KenarGen * 0.5f);
                int o = v.Count; float yy = y + 0.008f;
                v.Add(new Vector3(a.x - n.x, yy, a.y - n.y)); v.Add(new Vector3(a.x + n.x, yy, a.y + n.y));
                v.Add(new Vector3(b.x + n.x, yy, b.y + n.y)); v.Add(new Vector3(b.x - n.x, yy, b.y - n.y));
                for (int q = 0; q < 4; q++) { c.Add(Kenar); taban.Add(Kenar.a); }
                t.Add(o); t.Add(o + 1); t.Add(o + 2); t.Add(o); t.Add(o + 2); t.Add(o + 3);
                t.Add(o); t.Add(o + 2); t.Add(o + 1); t.Add(o); t.Add(o + 3); t.Add(o + 2);   // iki yuzlu
            }
            mesh.Clear();
            mesh.SetVertices(v); mesh.SetColors(c); mesh.SetTriangles(t, 0, false);
            mesh.bounds = new Bounds(new Vector3(0, y, 0), Vector3.one * 400f);
            renk = c.ToArray();
            kurulan = s; sonGorus = -1f;
        }

        void Ucgenler(float[] u, float y, Color r)
        {
            for (int i = 0; i + 5 < u.Length; i += 6)
            {
                float ddx = u[i] - kurKam.x, ddz = u[i + 1] - kurKam.z; if (ddx * ddx + ddz * ddz > KurMenzil * KurMenzil) continue;
                int o = v.Count;
                v.Add(new Vector3(u[i], y, u[i + 1])); v.Add(new Vector3(u[i + 2], y, u[i + 3])); v.Add(new Vector3(u[i + 4], y, u[i + 5]));
                for (int q = 0; q < 3; q++) { c.Add(r); taban.Add(r.a); }
                t.Add(o); t.Add(o + 1); t.Add(o + 2); t.Add(o); t.Add(o + 2); t.Add(o + 1);
            }
        }

        public void Guncelle(ZeminSonuc s, float zeminY, Camera kam, bool goster)
        {
            mr.enabled = goster && s != null && s.zeminVar;
            if (s == null) return;
            var kp = kam.transform.position;
            if (s != kurulan || (new Vector2(kp.x - kurKam.x, kp.z - kurKam.z)).sqrMagnitude > 9f) Kur(s, s.zeminY, kp);
            float simdi = Time.unscaledTime;
            if (simdi - sonGorus < 0.25f) return;
            sonGorus = simdi;
            var p = kam.transform.position;
            Gorus.Kur(s.duvar, p.x, p.z);
            for (int i = 0; i < renk.Length; i++)
            {
                var q = v[i]; float dx = q.x - p.x, dz = q.z - p.z; float r = Mathf.Sqrt(dx * dx + dz * dz);
                float a = r > Menzil ? 0f : Mathf.Clamp01((Menzil - r) / 1.5f);
                if (a > 0f) a *= Gorus.Gorunur(q.x, q.z);
                renk[i].a = taban[i] * a;
            }
            mesh.SetColors(renk);
        }
    }
}
