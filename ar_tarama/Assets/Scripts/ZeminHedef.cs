using UnityEngine;
using UnityEngine.Rendering;

namespace ArTarama
{
    /// <summary>
    /// Taramada tek yonlendirme: taranmamis zeminin basladigi yere zeminde kucuk, nefes alan bir halka (12 cm).
    /// Yazi yok; kullanici boyanan zeminden neyin tarandigini zaten goruyor (kullanici istegi, 21 Kasim).
    /// </summary>
    public class ZeminHedef
    {
        readonly GameObject go; readonly Mesh mesh; readonly MeshRenderer mr;
        Vector3 hedef; bool varMi; float gorunur, ilerlemeG;

        public ZeminHedef(Material dolgu)
        {
            mesh = new Mesh { name = "ZeminHedef" };
            const int N = 40; const float ic = 0.085f, dis = 0.12f, nokta = 0.025f;
            var v = new Vector3[N * 2 + N + 1]; var c = new Color[v.Length]; var t = new int[N * 6 + N * 3];
            var beyaz = new Color(1f, 1f, 1f, 0.9f);
            for (int i = 0; i < N; i++)
            {
                float a = i * Mathf.PI * 2f / N;
                v[i * 2] = new Vector3(Mathf.Sin(a) * ic, 0, Mathf.Cos(a) * ic); v[i * 2 + 1] = new Vector3(Mathf.Sin(a) * dis, 0, Mathf.Cos(a) * dis);
                c[i * 2] = beyaz; c[i * 2 + 1] = beyaz;
                int j = (i + 1) % N;
                t[i * 6] = i * 2; t[i * 6 + 1] = i * 2 + 1; t[i * 6 + 2] = j * 2 + 1; t[i * 6 + 3] = i * 2; t[i * 6 + 4] = j * 2 + 1; t[i * 6 + 5] = j * 2;
                v[N * 2 + i] = new Vector3(Mathf.Sin(a) * nokta, 0, Mathf.Cos(a) * nokta); c[N * 2 + i] = beyaz;
            }
            int m = N * 3; v[m] = Vector3.zero; c[m] = beyaz;
            for (int i = 0; i < N; i++) { int o = N * 6 + i * 3; t[o] = m; t[o + 1] = N * 2 + i; t[o + 2] = N * 2 + (i + 1) % N; }
            mesh.vertices = v; mesh.colors = c; mesh.triangles = t;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
            go = new GameObject("ZeminHedef"); go.layer = OdaYoneticisi.KatmanAr;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = dolgu != null ? new Material(dolgu) : new Material(Shader.Find("ArTarama/OdaDolgu"));
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            go.SetActive(false);
        }

        public void Guncelle(bool goster, float x, float z, float zeminY, float dt, float ilerleme = 0f)
        {
            if (goster)
            {
                var p = new Vector3(x, zeminY + 0.012f, z);
                if (!varMi || (p - hedef).sqrMagnitude > 0.04f) { if (gorunur > 0.05f && varMi) gorunur = 0f; hedef = p; ilerlemeG = 0f; }   // yeni hedef: belirerek gelir
                varMi = true;
            }
            gorunur = Mathf.MoveTowards(gorunur, goster ? 1f : 0f, dt * 3f);
            go.SetActive(gorunur > 0.01f);
            if (!go.activeSelf) { if (!goster) varMi = false; return; }
            float n = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 3.2f);
            go.transform.position = hedef;
            go.transform.localScale = Vector3.one * n * Mathf.Lerp(0.6f, 1f, gorunur);
            var col = new Color(1f, 1f, 1f, gorunur);
            var b = new MaterialPropertyBlock(); mr.GetPropertyBlock(b); b.SetColor("_Color", col); mr.SetPropertyBlock(b);
            // OdaDolgu tepe rengini kullanir: alfayi tepe renklerine yaz
            // ilerleme: halka saat yonunde turkuaza dolar (tuttukca zemin teyit edilir)
            ilerlemeG = Mathf.MoveTowards(ilerlemeG, Mathf.Clamp01(ilerleme), dt * 1.5f);
            var cs = mesh.colors; int N = (cs.Length - 1) / 3;
            for (int i = 0; i < cs.Length; i++)
            {
                bool dolu = i < N * 2 && (i / 2) < ilerlemeG * N;
                var r = dolu ? new Color(0.20f, 0.88f, 0.66f) : Color.white; r.a = 0.9f * gorunur; cs[i] = r;
            }
            mesh.colors = cs;
        }

        public void Yok() { Object.Destroy(go); Object.Destroy(mesh); }
    }
}
