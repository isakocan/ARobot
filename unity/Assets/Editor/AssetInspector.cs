using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ARaki.EditorTools
{
    /// <summary>İçe aktarılan modellerin boyutunu ve malzeme adlarını raporlar (kurulum kontrolü için).</summary>
    public static class AssetInspector
    {
        public static void Diagnose()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/_Project/Scenes/DriveSim.unity");
            var sb = new StringBuilder();
            foreach (var id in new[] { "sofa_02", "modern_arm_chair_01", "modern_coffee_table_01", "potted_plant_02" })
            {
                var go = GameObject.Find(id);
                if (go == null) { sb.AppendLine(id + ": YOK"); continue; }
                sb.AppendLine($"{id}: root konum={go.transform.position} yerel={go.transform.localPosition} dönüş={go.transform.eulerAngles} ölçek={go.transform.localScale} ebeveyn={go.transform.parent?.name}");
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                    sb.AppendLine($"    {r.name} dünya-merkez={r.bounds.center} boyut={r.bounds.size} konum={r.transform.position}");
                var col = go.GetComponent<BoxCollider>();
                if (col != null) sb.AppendLine($"    collider merkez(yerel)={col.center} boyut={col.size}");
            }
            Debug.Log("[Diag] " + sb);
            File.WriteAllText(System.Environment.GetEnvironmentVariable("ARAKI_REPORT") ?? "diag.txt", sb.ToString());
        }

        public static void Report()
        {
            var sb = new StringBuilder();
            foreach (var dir in Directory.GetDirectories("Assets/_Project/Art/Imported/models"))
            {
                string id = Path.GetFileName(dir);
                string path = $"Assets/_Project/Art/Imported/models/{id}/{id}.fbx";
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) { sb.AppendLine(id + ": YÜKLENEMEDİ"); continue; }

                var b = new Bounds();
                bool first = true;
                int tris = 0;
                var mats = new System.Collections.Generic.HashSet<string>();
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
                    foreach (var m in r.sharedMaterials) mats.Add(m != null ? m.name : "null");
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
                }
                sb.AppendLine($"{id}: size=({b.size.x:0.00},{b.size.y:0.00},{b.size.z:0.00}) center=({b.center.x:0.00},{b.center.y:0.00},{b.center.z:0.00}) tris={tris} mats=[{string.Join(", ", mats)}]");
            }
            Debug.Log("[Inspector]\n" + sb);
            File.WriteAllText(System.Environment.GetEnvironmentVariable("ARAKI_REPORT") ?? "inspect.txt", sb.ToString());
        }
    }
}
