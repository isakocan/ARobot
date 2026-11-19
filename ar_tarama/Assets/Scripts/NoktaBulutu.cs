using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

namespace ArTarama
{
    /// <summary>ARCore ozellik noktalarini biriktirir (2 cm vokselde tekillestirir), nokta olarak cizer.</summary>
    public class NoktaBulutu : MonoBehaviour
    {
        public ARPointCloudManager yonetici;
        public const int Tavan = 60000;
        public const float Voksel = 0.02f;

        readonly List<Vector3> poz = new List<Vector3>(Tavan);
        readonly List<Color> renk = new List<Color>(Tavan);
        readonly HashSet<long> gorulen = new HashSet<long>();
        int[] indis;
        Mesh mesh; MeshRenderer mr;
        float sonTarama, sonMesh; bool kirli;

        public int Sayi => poz.Count;
        public bool Goster { get => mr != null && mr.enabled; set { if (mr != null) mr.enabled = value; } }

        void Awake()
        {
            indis = new int[Tavan];
            for (int i = 0; i < Tavan; i++) indis[i] = i;
            mesh = new Mesh { name = "NoktaBulutu", indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            var go = new GameObject("NoktaCizim");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Resources.Load<Material>("mat_nokta");
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        public Vector3[] Kopya() { return poz.ToArray(); }

        void Update()
        {
            if (yonetici == null) return;
            if (Time.unscaledTime - sonTarama > 0.2f && poz.Count < Tavan)
            {
                sonTarama = Time.unscaledTime;
                foreach (var pc in yonetici.trackables)
                {
                    var ps = pc.positions;
                    if (!ps.HasValue) continue;
                    var arr = ps.Value;
                    for (int i = 0; i < arr.Length && poz.Count < Tavan; i++)
                    {
                        Vector3 w = pc.transform.TransformPoint(arr[i]);
                        long a = (long)Mathf.Floor(w.x / Voksel) + 1048576;
                        long b = (long)Mathf.Floor(w.y / Voksel) + 1048576;
                        long c = (long)Mathf.Floor(w.z / Voksel) + 1048576;
                        long key = (a << 42) | (b << 21) | c;
                        if (!gorulen.Add(key)) continue;
                        poz.Add(w);
                        float t = Mathf.InverseLerp(-1.2f, 2.0f, w.y);
                        renk.Add(Color.Lerp(UiKur.Vurgu, Color.white, t));
                        kirli = true;
                    }
                }
            }
            if (kirli && Time.unscaledTime - sonMesh > 0.5f)
            {
                sonMesh = Time.unscaledTime; kirli = false;
                mesh.Clear();
                mesh.SetVertices(poz);
                mesh.SetColors(renk);
                mesh.SetIndices(indis, 0, poz.Count, MeshTopology.Points, 0, false);
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 200f);
            }
        }
    }
}
