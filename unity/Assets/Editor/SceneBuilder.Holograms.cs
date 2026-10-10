using ARaki.AR;
using ARaki.Game;
using ARaki.Robot;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ARaki.EditorTools
{
    /// <summary>ORBİT-9 istasyonu, devriye sondaları, sığınak işaretleri ve parlama efektleri (AR katmanı).</summary>
    public static partial class SceneBuilder
    {
        // ---------------------------------------------------------------- malzemeler

        /// <summary>ARaki/Hologram shader'lı yarı saydam, kenarı parlayan yüzey malzemesi.</summary>
        static Material HoloSurface(string name, Color color, float baseAlpha = 0.16f, float fresnelPower = 2.4f,
                                    float fresnelBoost = 1.6f, float scanStrength = 0.4f, float flicker = 0.12f)
        {
            var mat = LoadOrCreateMaterial(name, "ARaki/Hologram");
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_BaseAlpha", baseAlpha);
            mat.SetFloat("_FresnelPower", fresnelPower);
            mat.SetFloat("_FresnelBoost", fresnelBoost);
            mat.SetFloat("_ScanStrength", scanStrength);
            mat.SetFloat("_Flicker", flicker);
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material HoloLineMat() => AdditiveMat("HoloLine", C("#5FE3FF") * 1.3f);

        static GameObject HoloPrim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        static GameObject HoloMesh(string name, Transform parent, Vector3 pos, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        // ---------------------------------------------------------------- ORBİT-9

        static MissionPoint MakeStationPoint(string label, Transform physical, ARRig rig, Transform arRoot)
        {
            var go = new GameObject("Point_Station");
            go.transform.position = new Vector3(physical.position.x, 0f, physical.position.z);

            var vis = new GameObject("AR_Station").transform;
            vis.SetParent(arRoot, false);
            var station = BuildStationHolo(vis);
            var tint = vis.gameObject.AddComponent<HoloTint>();

            var mirror = go.AddComponent<ARMirror>();
            SetRef(mirror, "rig", rig);
            SetRef(mirror, "visual", vis);

            var mp = go.AddComponent<MissionPoint>();
            mp.Label = label;
            mp.Kind = PointKind.Station;
            mp.Radius = 1.15f;
            mp.MinRadius = 0.5f;
            mp.ScanSeconds = 2.4f;
            mp.SectorCount = 3;
            SetRef(mp, "holo", tint);
            SetRef(mp, "burst", MakeBurst(vis));
            SetRef(mp, "physical", physical);
            SetRef(mp, "station", station);
            return mp;
        }

        static StationVisual BuildStationHolo(Transform p)
        {
            var cyan = C("#5FE3FF");
            var surface = HoloSurface("HoloStation", cyan * 0.8f, 0.1f, 2.4f, 1.3f, 0.5f, 0.1f);
            var frame = HoloSurface("HoloStationFrame", cyan * 1.0f, 0.06f, 1.8f, 1.8f, 0.35f, 0.1f);
            var projector = HoloSurface("HoloProjector", cyan * 0.6f, 0.22f, 1f, 0.3f, 0.4f, 0.05f);
            var wedgeMat = HoloSurface("HoloSector", cyan, 0.4f, 1f, 0f, 0.15f, 0f);
            var lineMat = HoloLineMat();

            Glow(p, "Glow", 1.5f, 0.003f);
            Ring(p, "PadA", 0.5f, 0.004f, 56, 0.004f, lineMat);
            Ring(p, "PadB", 0.58f, 0.004f, 56, 0.0025f, lineMat);
            Ring(p, "RingInner", 0.6f, 0.004f, 72, 0.003f, lineMat);
            Ring(p, "RingOuter", 1.12f, 0.004f, 72, 0.003f, lineMat);
            var wedges = new GameObject("Wedges").transform;
            wedges.SetParent(p, false);

            // gerçek maketten yukarı yansıyan ışık hüzmesi
            HoloMesh("Projector", p, new Vector3(0f, 0.27f, 0f), ProceduralMeshes.Cone(0.1f, 0.3f, 0.34f), projector);

            // çekirdek ve kapaklar
            var core = HoloPrim(PrimitiveType.Cylinder, "Core", p, new Vector3(0f, 0.84f, 0f), new Vector3(0.15f, 0.32f, 0.15f), surface);
            HoloPrim(PrimitiveType.Sphere, "CoreCapTop", p, new Vector3(0f, 1.16f, 0f), new Vector3(0.17f, 0.1f, 0.17f), surface);
            HoloPrim(PrimitiveType.Sphere, "CoreCapBottom", p, new Vector3(0f, 0.52f, 0f), new Vector3(0.17f, 0.08f, 0.17f), surface);
            Ring(p, "CoreBandA", 0.082f, 0.74f, 28, 0.004f, lineMat);
            Ring(p, "CoreBandB", 0.082f, 0.94f, 28, 0.004f, lineMat);

            // yaşam alanı halkası: parçalı, dönen
            var habitat = new GameObject("Habitat").transform;
            habitat.SetParent(p, false);
            habitat.localPosition = new Vector3(0f, 0.8f, 0f);
            const int segs = 22;
            for (int i = 0; i < segs; i++)
            {
                float a = i * Mathf.PI * 2f / segs;
                var seg = HoloPrim(PrimitiveType.Cube, "Seg" + i, habitat, new Vector3(Mathf.Sin(a) * 0.34f, 0f, Mathf.Cos(a) * 0.34f),
                    new Vector3(0.1f, 0.055f, 0.042f), frame, Quaternion.Euler(0f, a * Mathf.Rad2Deg + 90f, 0f));
            }
            Ring(habitat, "HabRingA", 0.355f, 0.032f, 44, 0.003f, lineMat);
            Ring(habitat, "HabRingB", 0.315f, -0.032f, 44, 0.003f, lineMat);
            for (int i = 0; i < 4; i++)
                HoloPrim(PrimitiveType.Cube, "Spoke" + i, habitat, Vector3.zero, new Vector3(0.68f, 0.012f, 0.018f), surface, Quaternion.Euler(0f, i * 45f, 0f));

            // güneş kanatları
            var wings = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                var pivot = new GameObject(i == 0 ? "WingR" : "WingL").transform;
                pivot.SetParent(p, false);
                pivot.localPosition = new Vector3(side * 0.08f, 1.06f, 0f);
                HoloPrim(PrimitiveType.Cube, "Panel", pivot, new Vector3(side * 0.27f, 0f, 0f), new Vector3(0.46f, 0.01f, 0.2f), frame);
                for (int g = 0; g < 4; g++)
                    Line(pivot, "Grid", new[] { new Vector3(side * (0.07f + g * 0.1f), 0.008f, -0.1f), new Vector3(side * (0.07f + g * 0.1f), 0.008f, 0.1f) }, 0.002f, lineMat);
                wings[i] = pivot;
            }

            // anten, çanak, işaret ışığı
            HoloPrim(PrimitiveType.Cylinder, "Mast", p, new Vector3(0f, 1.3f, 0f), new Vector3(0.012f, 0.1f, 0.012f), surface);
            HoloPrim(PrimitiveType.Sphere, "Dish", p, new Vector3(0.03f, 1.4f, 0f), new Vector3(0.16f, 0.034f, 0.16f), surface, Quaternion.Euler(0f, 0f, 24f));
            var beacon = HoloPrim(PrimitiveType.Sphere, "Beacon", p, new Vector3(0f, 1.46f, 0f), Vector3.one * 0.02f, lineMat);

            // alt bağlantı kolları
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f + 0.5f;
                HoloPrim(PrimitiveType.Cylinder, "Arm" + i, p, new Vector3(Mathf.Sin(a) * 0.13f, 0.55f, Mathf.Cos(a) * 0.13f),
                    new Vector3(0.02f, 0.09f, 0.02f), frame, Quaternion.Euler(Mathf.Cos(a) * 38f, 0f, -Mathf.Sin(a) * 38f));
            }

            Motes(p, 0.45f, 0.1f, 24);

            var sv = p.gameObject.AddComponent<StationVisual>();
            SetRef(sv, "wedgeMaterial", wedgeMat);
            SetRef(sv, "wedgeRoot", wedges);
            SetRef(sv, "habitat", habitat);
            SetRefs(sv, "wings", wings);
            SetRef(sv, "core", core.transform);
            SetRef(sv, "beacon", beacon.transform);
            return sv;
        }

        // ---------------------------------------------------------------- devriye sondası

        static Threat MakeThreat(string id, Vector3 pos, Transform robot, MockRobotLink link, ARRig rig, Transform arRoot)
        {
            var go = new GameObject("Threat_" + id);
            go.transform.position = pos;
            var th = go.AddComponent<Threat>();
            SetRef(th, "robot", robot);
            SetRef(th, "link", link);

            var vis = new GameObject("AR_Threat" + id).transform;
            vis.SetParent(arRoot, false);
            BuildDrone(vis, th, out var model, out var core, out var sectorRoot, out var fanMat, out var arc);
            var tint = vis.gameObject.AddComponent<HoloTint>();

            var mirror = go.AddComponent<ARMirror>();
            SetRef(mirror, "rig", rig);
            SetRef(mirror, "visual", vis);

            var tv = vis.gameObject.AddComponent<ThreatVisual>();
            SetRef(tv, "threat", th);
            SetRef(tv, "holo", tint);
            SetRef(tv, "sectorRoot", sectorRoot);
            SetRef(tv, "fanMaterial", fanMat);
            SetRef(tv, "model", model);
            SetRef(tv, "core", core);
            SetRef(tv, "arc", arc);
            SetRef(tv, "rig", rig);
            SetRef(tv, "robot", robot);
            return th;
        }

        static void BuildDrone(Transform p, Threat threat, out Transform model, out Transform core, out Transform sectorRoot, out Material fanMat, out LineRenderer arc)
        {
            var red = C("#FF5C5C");
            var surface = HoloSurface("HoloDrone", red * 1.3f, 0.3f, 2.0f, 1.9f, 0.3f, 0.1f);
            var frame = HoloSurface("HoloDroneFrame", red * 1.6f, 0.18f, 1.5f, 2.4f, 0.2f, 0.12f);
            var lineMat = AdditiveMat("HoloThreat", red * 1.6f);
            var lens = AdditiveMat("HoloDroneCore", red * 2.4f);
            fanMat = HoloSurface("HoloFan", red, 0.42f, 1f, 0f, 0.25f, 0.05f);

            var m = new GameObject("Model").transform;
            m.SetParent(p, false);
            model = m;

            // gövde: yassı disk + kubbe
            HoloPrim(PrimitiveType.Cylinder, "Hull", m, Vector3.zero, new Vector3(0.15f, 0.018f, 0.15f), surface);
            HoloPrim(PrimitiveType.Sphere, "Dome", m, new Vector3(0f, 0.012f, 0f), new Vector3(0.1f, 0.062f, 0.1f), surface);
            HoloPrim(PrimitiveType.Sphere, "Belly", m, new Vector3(0f, -0.016f, 0f), new Vector3(0.09f, 0.04f, 0.09f), frame);
            Ring(m, "HullRim", 0.075f, 0f, 36, 0.004f, lineMat);

            // ön mercek (çekirdek): bakış yönünü gösterir
            var coreGo = HoloPrim(PrimitiveType.Sphere, "Lens", m, new Vector3(0f, 0.006f, 0.058f), Vector3.one * 0.06f, lens);
            core = coreGo.transform;
            Line(m, "Visor", new[] { new Vector3(-0.04f, 0.012f, 0.05f), new Vector3(0f, 0.016f, 0.066f), new Vector3(0.04f, 0.012f, 0.05f) }, 0.004f, lineMat);

            // dört kol ve itici halkaları
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                HoloPrim(PrimitiveType.Cube, "Arm" + i, m, dir * 0.075f, new Vector3(0.1f, 0.008f, 0.012f), frame, Quaternion.Euler(0f, a * Mathf.Rad2Deg + 90f, 0f));
                var pivot = new GameObject("Thruster" + i).transform;
                pivot.SetParent(m, false);
                pivot.localPosition = dir * 0.128f;
                Ring(pivot, "Ring", 0.034f, 0f, 20, 0.004f, lineMat);
                var spin = pivot.gameObject.AddComponent<Spin>();
                var so = new SerializedObject(spin);
                so.FindProperty("degreesPerSecond").vector3Value = new Vector3(0f, i % 2 == 0 ? 360f : -360f, 0f);
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // yavaş dönen dış halka
            var halo = new GameObject("Halo").transform;
            halo.SetParent(m, false);
            Ring(halo, "Ring", 0.17f, 0f, 48, 0.003f, lineMat);
            var haloSpin = halo.gameObject.AddComponent<Spin>();
            var hso = new SerializedObject(haloSpin);
            hso.FindProperty("degreesPerSecond").vector3Value = new Vector3(0f, 55f, 0f);
            hso.ApplyModifiedPropertiesWithoutUndo();

            // elektrik arkı (yalnızca temas halinde görünür)
            var arcGo = new GameObject("Arc");
            arcGo.transform.SetParent(p, false);
            arc = arcGo.AddComponent<LineRenderer>();
            arc.sharedMaterial = AdditiveMat("HoloArc", C("#9FD8FF") * 2.6f);
            arc.widthMultiplier = 0.008f;
            arc.alignment = LineAlignment.View;
            arc.shadowCastingMode = ShadowCastingMode.Off;
            arc.enabled = false;

            // yerdeki izler: ışık hâlesi ve görüş yelpazesi
            var floor = new GameObject("Floor").transform;
            floor.SetParent(p, false);
            floor.localPosition = new Vector3(0f, -threat.HoverHeight, 0f);
            Glow(floor, "Glow", 0.4f, 0.003f);
            sectorRoot = new GameObject("SectorRoot").transform;
            sectorRoot.SetParent(floor, false);
        }

        // ---------------------------------------------------------------- sığınak işaretleri

        static Transform MakeShelter(int index, ARRig rig, Transform arRoot)
        {
            var green = C("#73FF9A");
            var go = new GameObject("Shelter_" + index);
            var vis = new GameObject("AR_Shelter" + index).transform;
            vis.SetParent(arRoot, false);

            var surface = HoloSurface("HoloShelter", green * 1.2f, 0.12f, 2.0f, 1.7f, 0.5f, 0.08f);
            var line = AdditiveMat("HoloShelterLine", green * 1.3f);
            HoloPrim(PrimitiveType.Sphere, "Dome", vis, Vector3.zero, new Vector3(0.62f, 0.34f, 0.62f), surface);
            Ring(vis, "RingA", 0.31f, 0.004f, 48, 0.004f, line);
            Ring(vis, "RingB", 0.4f, 0.004f, 48, 0.002f, line);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2f;
                Line(vis, "Mark", new[] { new Vector3(Mathf.Sin(a) * 0.4f, 0.004f, Mathf.Cos(a) * 0.4f), new Vector3(Mathf.Sin(a) * 0.5f, 0.004f, Mathf.Cos(a) * 0.5f) }, 0.004f, line);
            }
            Glow(vis, "Glow", 0.9f, 0.003f);

            var tint = vis.gameObject.AddComponent<HoloTint>();
            var so = new SerializedObject(tint);
            so.FindProperty("useInitialColor").boolValue = true;
            so.FindProperty("initialColor").colorValue = green * 1.1f;
            so.ApplyModifiedPropertiesWithoutUndo();

            var mirror = go.AddComponent<ARMirror>();
            SetRef(mirror, "rig", rig);
            SetRef(mirror, "visual", vis);
            go.SetActive(false);
            return go.transform;
        }

        // ---------------------------------------------------------------- güneş parlaması

        static Volume BuildStormVolume()
        {
            const string path = Root + "/Settings/ARaki_StormVolume.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin2);
            grain.intensity.Override(1f);
            grain.response.Override(1f);

            var aberration = profile.Add<ChromaticAberration>(true);
            aberration.intensity.Override(1f);

            var lens = profile.Add<LensDistortion>(true);
            lens.intensity.Override(-0.22f);

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.8f);
            color.saturation.Override(-28f);
            color.contrast.Override(16f);
            color.colorFilter.Override(new Color(1f, 0.84f, 0.68f));

            foreach (var comp in profile.components)
            {
                AssetDatabase.AddObjectToAsset(comp, profile);
                EditorUtility.SetDirty(comp);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var vol = new GameObject("StormVolume").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;
            vol.weight = 0f;
            vol.sharedProfile = profile;
            return vol;
        }
    }
}
