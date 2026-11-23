using UnityEngine;
using UnityEngine.Rendering;

namespace ArTarama
{
    /// <summary>
    /// LIDAR görünümü (24 Kasım, kullanıcı: "derinlik lidar gibi ama bunu hissedemiyorum"). Uygulamanın derinlikten kurduğu
    /// 3B doluluk haritası kameranın üstünde gösterilir: zemin (yeşil), engel (turuncu), 1 m üstü / duvar (mor), tek görülmüş
    /// zayıf küp (gri). Boyanan zeminin neden orada bittiği, masanın altının neden kapalı olduğu doğrudan görülür.
    /// Noktalar arka planda (ZeminHaritasi.Guncelle, 1 Hz) kameranın 4 m çevresinden kopyalanır.
    /// </summary>
    public class LidarCizim
    {
        readonly GameObject go; readonly Mesh mesh; readonly MeshRenderer mr;
        float[] kurulan;
        static readonly Color32[] Renk =
        {
            new Color32(70, 230, 150, 200), new Color32(255, 130, 50, 235), new Color32(185, 120, 255, 235), new Color32(200, 200, 200, 90)
        };

        public LidarCizim()
        {
            mesh = new Mesh { name = "Lidar", indexFormat = IndexFormat.UInt32 }; mesh.MarkDynamic();
            go = new GameObject("Lidar"); go.layer = OdaYoneticisi.KatmanAr;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            mr = go.AddComponent<MeshRenderer>();
            var sh = Shader.Find("ArTarama/LidarNokta");
            mr.sharedMaterial = new Material(sh != null ? sh : Shader.Find("ArTarama/Nokta"));
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.allowOcclusionWhenDynamic = false;
            mr.enabled = false;
        }

        public void Yok() { Object.Destroy(go); Object.Destroy(mesh); }

        public void Guncelle(ZeminSonuc s, bool goster)
        {
            mr.enabled = goster && s != null && s.voksel != null && s.voksel.Length > 0;
            if (!mr.enabled || s.voksel == kurulan) return;
            kurulan = s.voksel;
            int n = s.voksel.Length / 4;
            var v = new Vector3[n]; var c = new Color32[n]; var ind = new int[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = new Vector3(s.voksel[i * 4], s.voksel[i * 4 + 1], s.voksel[i * 4 + 2]);
                c[i] = Renk[Mathf.Clamp((int)s.voksel[i * 4 + 3], 0, 3)]; ind[i] = i;
            }
            mesh.Clear();
            mesh.vertices = v; mesh.colors32 = c; mesh.SetIndices(ind, MeshTopology.Points, 0, false);
            mesh.bounds = new Bounds(new Vector3(s.kameraX, s.zeminY, s.kameraZ), Vector3.one * 20f);
        }
    }
}
