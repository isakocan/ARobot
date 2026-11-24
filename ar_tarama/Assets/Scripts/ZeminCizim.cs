using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArTarama
{
    /// <summary>
    /// AR goruntusunde acik zemin, ARCore duzlem gorunumune benzer (ArTarama/ZeminBoya: yari saydam renk + izgara):
    /// birlesik alan, kenara dogru yumusakca solar (kenar cizgisi yok). Engeller cizilmez: yalniz kesin zemin, temiz. Duvar arkasi (GorusTablosu) ve 8 m otesi cizilmez: baska odanin zemini duvarin icinden gorunmez.
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

        static readonly Color Acik = new Color(0.20f, 0.88f, 0.66f, 0.40f);
        // oyun alani (24 Kasim): guveni yuksek, esyadan payli, duz hatli; acik zeminin ustunde koyu mavi
        static readonly Color Oyun = new Color(0.10f, 0.28f, 0.92f, 0.55f);
        // kesinlesmemis acik zemin (24 Kasim): soluk; hucre birkac kez gorulup durumu oturunca dolgun. "Burasi bitti mi"
        // boyanin dolgunlugundan anlasilir.
        const float KesinDegil = 0.45f;
        const float Menzil = 8f, KurMenzil = 12f;

        public ZeminCizim(Material dolgu)
        {
            mesh = new Mesh { name = "AcikZemin", indexFormat = IndexFormat.UInt32 }; mesh.MarkDynamic();
            go = new GameObject("AcikZemin"); go.layer = OdaYoneticisi.KatmanAr;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            mr = go.AddComponent<MeshRenderer>(); var boya = Resources.Load<Material>("mat_zemin_boya");
            mr.sharedMaterial = boya != null ? new Material(boya) : dolgu != null ? new Material(dolgu) : new Material(Shader.Find("ArTarama/OdaDolgu"));
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.allowOcclusionWhenDynamic = false;
        }

        public void Yok() { Object.Destroy(go); Object.Destroy(mesh); }
        public void Temizle() { mesh.Clear(); kurulan = null; Gorus.Hazir = false; }

        Vector3 kurKam; ZeminSonuc kurSonuc;
        void Kur(ZeminSonuc s, float y, Vector3 kam)
        {
            kurKam = kam;
            v.Clear(); c.Clear(); t.Clear(); taban.Clear();
            kurSonuc = s;
            Ucgenler(s.acikUcgen, y + 0.006f, Acik, true);
            Ucgenler(s.oyunUcgen, y + 0.011f, Oyun, false);
            mesh.Clear();
            mesh.SetVertices(v); mesh.SetColors(c); mesh.SetTriangles(t, 0, false);
            mesh.bounds = new Bounds(new Vector3(0, y, 0), Vector3.one * 400f);
            renk = c.ToArray();
            kurulan = s; sonGorus = -1f;
        }

        void Ucgenler(float[] u, float y, Color r, bool sol)
        {
            for (int i = 0; i + 5 < u.Length; i += 6)
            {
                float ddx = u[i] - kurKam.x, ddz = u[i + 1] - kurKam.z; if (ddx * ddx + ddz * ddz > KurMenzil * KurMenzil) continue;
                int o = v.Count;
                v.Add(new Vector3(u[i], y, u[i + 1])); v.Add(new Vector3(u[i + 2], y, u[i + 3])); v.Add(new Vector3(u[i + 4], y, u[i + 5]));
                for (int q = 0; q < 3; q++)
                {
                    // kenara dogru solma: yumusatilmis alan 0,5 (kenar) -> 0, 0,85+ (ic) -> 1
                    float al = kurSonuc.AlanDeger(u[i + q * 2], u[i + q * 2 + 1]);
                    float k = sol ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.85f, al)) : 1f;
                    if (sol && kurSonuc.kesin != null && kurSonuc.kesin.Count > 0 &&
                        !kurSonuc.kesin.Contains(OdaSonuc.Anah2(ZeminSonuc.Ind(u[i + q * 2]), ZeminSonuc.Ind(u[i + q * 2 + 1])))) k *= KesinDegil;
                    var rr = r; rr.a = r.a * k; c.Add(rr); taban.Add(rr.a);
                }
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
            Gorus.Kur(s.engel, p.x, p.z);          // duvar + esya: masa / sandalye arkasindaki zemin ve nesneler de gizlenir
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
