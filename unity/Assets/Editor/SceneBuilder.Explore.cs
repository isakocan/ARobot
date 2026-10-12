using ARaki.AR;
using ARaki.Game;
using ARaki.Sensing;
using UnityEditor;
using UnityEngine;

namespace ARaki.EditorTools
{
    /// <summary>Yüzey keşfi: mesafe sensörü haritası, sensör noktaları/halkası (AR) ve mineral numuneleri.</summary>
    public static partial class SceneBuilder
    {
        static ExplorationMap BuildMap(Transform robot)
        {
            var go = new GameObject("ExplorationMap");
            var map = go.AddComponent<ExplorationMap>();
            SetRef(map, "robot", robot);
            return map;
        }

        static LidarOverlay BuildLidar(ExplorationMap map, ARRig rig, Transform arRoot)
        {
            var go = new GameObject("LidarOverlay");
            go.transform.SetParent(arRoot, false);
            var overlay = go.AddComponent<LidarOverlay>();

            var dots = LoadOrCreateMaterial("LidarDots", "ARaki/Track");
            dots.SetFloat("_PulseStrength", 0f);
            dots.renderQueue = 2600;
            EditorUtility.SetDirty(dots);

            var ring = Ring(go.transform, "PulseRing", 1f, 0f, 72, 0.012f, AdditiveMat("SensorPulse", C("#5FE3FF") * 1.0f));
            SetRef(overlay, "map", map);
            SetRef(overlay, "rig", rig);
            SetRef(overlay, "dotMaterial", dots);
            SetRef(overlay, "pulseRing", ring);
            return overlay;
        }

        /// <summary>Mineral numunesi: gerçek bir 3D baskı karşılığı yok, sanal bir keşif noktası (zemine oturan kristal kümesi).</summary>
        static MissionPoint MakeSamplePoint(int index, ARRig rig, Transform arRoot)
        {
            var go = new GameObject("Point_Sample" + index);

            var vis = new GameObject("AR_Sample" + index).transform;
            vis.SetParent(arRoot, false);
            HoloSample(vis);
            var tint = vis.gameObject.AddComponent<HoloTint>();

            var mirror = go.AddComponent<ARMirror>();
            SetRef(mirror, "rig", rig);
            SetRef(mirror, "visual", vis);

            var mp = go.AddComponent<MissionPoint>();
            mp.Label = "NUMUNE";
            mp.Kind = PointKind.Sample;
            mp.Radius = 0.45f;
            mp.ScanSeconds = 1.6f;
            mp.SectorCount = 1;
            mp.Optional = true;
            SetRef(mp, "holo", tint);
            SetRef(mp, "burst", MakeBurst(vis));
            return mp;
        }

        static void HoloSample(Transform p)
        {
            var crystal = HoloSurface("HoloCrystal", Color.white, 0.5f, 2.2f, 1.4f, 0.45f, 0.06f);
            var line = HoloLineMat();

            Glow(p, "Glow", 0.75f, 0.003f);
            Ring(p, "Outer", 0.26f, 0.004f, 40, 0.004f, line);
            Ring(p, "Inner", 0.17f, 0.004f, 28, 0.003f, line);

            // zeminden çıkan kristal kümesi
            float[,] shards = { { 0f, 0f, 0.26f, 0f, 0f }, { 0.06f, 0.03f, 0.17f, 14f, 20f }, { -0.055f, 0.02f, 0.15f, -16f, 200f }, { 0.01f, -0.06f, 0.12f, 12f, 120f }, { -0.03f, -0.045f, 0.1f, -10f, 300f } };
            for (int i = 0; i < shards.GetLength(0); i++)
            {
                var go = HoloMesh("Shard" + i, p, new Vector3(shards[i, 0], 0f, shards[i, 1]), ProceduralMeshes.Cone(0.04f, 0.003f, shards[i, 2], 6), crystal);
                go.transform.localRotation = Quaternion.Euler(shards[i, 3], shards[i, 4], shards[i, 3] * 0.6f);
            }
            Motes(p, 0.12f, 0.03f, 8);
        }
    }
}
