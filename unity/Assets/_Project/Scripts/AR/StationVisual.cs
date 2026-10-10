using System.Collections.Generic;
using ARaki.Game;
using UnityEngine;

namespace ARaki.AR
{
    /// <summary>
    /// ORBİT-9 hologramının canlanması: yerdeki tarama sektörleri (bekleyen, taranan, tamamlanan), dönen halka,
    /// açılıp kapanan güneş kanatları, sönük başlayıp tarama ilerledikçe parlayan çekirdek ve yanıp sönen işaret ışığı.
    /// </summary>
    public class StationVisual : MonoBehaviour
    {
        [SerializeField] Material wedgeMaterial;
        [SerializeField] Transform wedgeRoot;
        [SerializeField] Transform habitat;
        [SerializeField] Transform[] wings;
        [SerializeField] Transform core;
        [SerializeField] Transform beacon;
        [SerializeField] float innerRadius = 0.62f;
        [SerializeField] float outerRadius = 1.1f;

        static readonly Color Pending = new Color(0.37f, 0.89f, 1f) * 0.85f;
        static readonly Color Scanning = new Color(1f, 0.71f, 0.33f) * 1.7f;
        static readonly Color Done = new Color(0.45f, 1f, 0.6f) * 1.3f;

        readonly List<Material> wedgeMaterials = new List<Material>();
        readonly List<GameObject> wedges = new List<GameObject>();
        MissionPoint point;
        Vector3 coreBase;
        bool coreCached;

        public void Configure(MissionPoint p)
        {
            point = p;
            foreach (var w in wedges) if (w != null) Destroy(w);
            wedges.Clear();
            wedgeMaterials.Clear();

            int n = p.SectorCount;
            float step = 360f / n;
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("Sector" + i);
                go.transform.SetParent(wedgeRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshes.Wedge(innerRadius, outerRadius, i * step + 2.5f, (i + 1) * step - 2.5f, 18);
                var r = go.AddComponent<MeshRenderer>();
                var mat = new Material(wedgeMaterial);
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                wedges.Add(go);
                wedgeMaterials.Add(mat);
            }
            Refresh();
        }

        void Update() => Refresh();

        public void Refresh()
        {
            if (point == null) return;
            float t = Time.time;
            for (int i = 0; i < wedgeMaterials.Count && i < point.SectorDone.Length; i++)
            {
                Color c;
                if (point.SectorDone[i]) c = Done;
                else if (point.ActiveSector == i) c = Scanning * (0.8f + 0.25f * Mathf.Sin(t * 9f));
                else c = Pending * (0.7f + 0.35f * point.SectorProgress[i]);
                if (point.Done) c = Done * 1.1f;
                wedgeMaterials[i].SetColor("_BaseColor", c);
            }

            float progress = point.Done ? 1f : point.Progress;
            if (habitat != null) habitat.Rotate(0f, (14f + 90f * progress) * Time.deltaTime, 0f, Space.Self);
            if (core != null)
            {
                if (!coreCached) { coreBase = core.localScale; coreCached = true; }
                float pulse = 1f + 0.05f * Mathf.Sin(t * (2f + 4f * progress));
                core.localScale = new Vector3(coreBase.x * pulse, coreBase.y, coreBase.z * pulse);
            }
            if (wings != null)
                for (int i = 0; i < wings.Length; i++)
                {
                    float open = Mathf.Lerp(-35f, 0f, progress);
                    wings[i].localRotation = Quaternion.Euler(0f, 0f, (i == 0 ? 1f : -1f) * open);
                }
            if (beacon != null) beacon.localScale = Vector3.one * (0.018f * (Mathf.Sin(t * 5f) > 0.4f ? 1.6f : 0.7f));
        }
    }
}
