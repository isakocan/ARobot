using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ARaki.EditorTools
{
    /// <summary>İçe aktarılan modellerin boyutunu ve malzeme adlarını raporlar (kurulum kontrolü için).</summary>
    public static class AssetInspector
    {
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
