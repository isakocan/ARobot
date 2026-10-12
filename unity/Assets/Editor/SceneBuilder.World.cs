using ARaki.AR;
using ARaki.Game;
using ARaki.Robot;
using ARaki.Sensing;
using ARaki.Theming;
using ARaki.UI;
using ARaki.Video;
using ARaki.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ARaki.EditorTools
{
    /// <summary>Tema yüzeyleri (gökyüzü malzemeleri), görüntü sonrası işleme profilleri, sensör katmanı ve üs işareti.</summary>
    public static partial class SceneBuilder
    {
        // ------------------------------------------------------------------ gökyüzü malzemeleri

        /// <summary>Duvar/tavan/pencere için her tema için bir malzeme (ThemeId sırasıyla).</summary>
        internal static Material[] SkyMaterials(SurfaceRole role)
        {
            var result = new Material[4];
            string r = role.ToString();
            bool ceiling = role == SurfaceRole.Ceiling;
            bool window = role == SurfaceRole.Window;

            // Ev: sıradan boyalı duvar, beyaz tavan, gündüz ışığı gelen pencere
            result[0] = role == SurfaceRole.Wall ? HomeWall(RoomKind.Hall)
                : ceiling ? Mat("Home_Ceiling", C("#F6F3EE"), smooth: 0.1f)
                : Mat("Home_Window", C("#CFE4F7"), smooth: 0.1f, emission: C("#A8CDF0") * 1.1f);
            result[1] = SkyMat("Sky_Safari_" + r, 0, "#4F9A4A", "#C8DDB4", "#F6F06A", window ? 1.5f : ceiling ? 1.1f : 1.0f, ceiling);
            result[2] = SkyMat("Sky_Space_" + r, 1, "#3A4FA8", "#04051A", "#8A3FD0", window ? 1.2f : 1.0f, ceiling);
            result[3] = window
                ? SkyMat("Sky_Mystery_" + r, 1, "#1A2450", "#05060F", "#2A3A7A", 0.9f, false)
                : SkyMat("Sky_Mystery_" + r, 2, "#3A2030", "#150A14", "#B8924A", ceiling ? 0.7f : 1.0f, ceiling);
            return result;
        }

        /// <summary>Ev temasında her odanın kendi duvar rengi var.</summary>
        internal static Material HomeWall(RoomKind kind)
        {
            string hex;
            switch (kind)
            {
                case RoomKind.Living: hex = "#E6D3B8"; break;
                case RoomKind.Kitchen: hex = "#F0E2B0"; break;
                case RoomKind.Bedroom: hex = "#CFD9C8"; break;
                case RoomKind.Bath: hex = "#C9DDE3"; break;
                case RoomKind.Study: hex = "#C7CEDC"; break;
                default: hex = "#EADFCF"; break;
            }
            return Mat("Home_Wall_" + kind, C(hex), smooth: 0.12f);
        }

        static Material SkyMat(string name, int mode, string a, string b, string c, float intensity, bool ceiling)
        {
            var mat = LoadOrCreateMaterial(name, "ARaki/Sky");
            mat.SetFloat("_Mode", mode);
            mat.SetColor("_ColA", C(a));
            mat.SetColor("_ColB", C(b));
            mat.SetColor("_ColC", C(c));
            mat.SetFloat("_Intensity", intensity);
            mat.SetFloat("_Ceiling", ceiling ? 1f : 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ görüntü sonrası işleme

        static VolumeProfile MakeProfile(string file, float exposure, float saturation, float contrast, string filter, float vignette, float grain, float bloom, float aberration)
        {
            string path = Root + "/Settings/" + file + ".asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            var b = profile.Add<Bloom>(true);
            b.intensity.Override(bloom);
            b.threshold.Override(1.0f);
            b.scatter.Override(0.65f);
            var v = profile.Add<Vignette>(true);
            v.intensity.Override(vignette);
            v.smoothness.Override(0.5f);
            var g = profile.Add<FilmGrain>(true);
            g.type.Override(FilmGrainLookup.Thin1);
            g.intensity.Override(grain);
            g.response.Override(0.8f);
            profile.Add<ChromaticAberration>(true).intensity.Override(aberration);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(exposure);
            ca.saturation.Override(saturation);
            ca.contrast.Override(contrast);
            ca.colorFilter.Override(C(filter));

            foreach (var comp in profile.components)
            {
                AssetDatabase.AddObjectToAsset(comp, profile);
                EditorUtility.SetDirty(comp);
            }
            EditorUtility.SetDirty(profile);
            return profile;
        }

        static ThemeSkin BuildThemeSkin(Light sun)
        {
            var profiles = new[]
            {
                MakeProfile("ARaki_Volume_Home", 0.05f, 6f, 5f, "#FFF8EC", 0.2f, 0.05f, 0.3f, 0.0f),
                MakeProfile("ARaki_Volume_Safari", 0.15f, 14f, 6f, "#FFF6DD", 0.26f, 0.15f, 0.5f, 0.1f),
                MakeProfile("ARaki_Volume_Space", 0.05f, 6f, 14f, "#E6EEFF", 0.32f, 0.2f, 0.85f, 0.18f),
                MakeProfile("ARaki_Volume_Mystery", -0.1f, -22f, 12f, "#FFE9D6", 0.42f, 0.32f, 0.5f, 0.2f),
            };
            AssetDatabase.SaveAssets();

            var vol = new GameObject("PostProcessing").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profiles[0];

            var skin = new GameObject("ThemeSkin").AddComponent<ThemeSkin>();
            SetRef(skin, "sun", sun);
            SetRef(skin, "volume", vol);
            SetRefs(skin, "profiles", profiles);
            return skin;
        }

        // ------------------------------------------------------------------ sensör katmanı ve üs

        static ExplorationMap BuildMap(Transform robot)
        {
            var go = new GameObject("ExplorationMap");
            var map = go.AddComponent<ExplorationMap>();
            var b = HouseLayout.Bounds;
            var so = new SerializedObject(map);
            so.FindProperty("origin").vector2Value = new Vector2(b.xMin - 0.2f, b.yMin - 0.2f);
            so.FindProperty("size").vector2Value = new Vector2(b.width + 0.4f, b.height + 0.4f);
            so.FindProperty("robot").objectReferenceValue = robot;
            so.ApplyModifiedPropertiesWithoutUndo();
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

        /// <summary>Üs: yerde parlayan halka, ortada ışık sütunu. Gerçek robotta bu, üs işaretçisinin (ArUco) üstüne oturur.</summary>
        static Transform BuildBaseMarker(Transform arRoot)
        {
            var root = new GameObject("AR_Base").transform;
            root.SetParent(arRoot, false);
            var line = AdditiveMat("BaseLine", C("#CFFFE0") * 0.6f);
            var glow = AdditiveMat("BaseGlow", C("#8FFFB0") * 0.45f, GlowTexture());

            var quad = GameObject.CreatePrimitive(PrimitiveType.Plane);
            quad.name = "Glow";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(root, false);
            quad.transform.localPosition = new Vector3(0f, 0.003f, 0f);
            quad.transform.localScale = Vector3.one * 0.14f;
            quad.GetComponent<MeshRenderer>().sharedMaterial = glow;
            quad.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            Ring(root, "Outer", 0.55f, 0.005f, 64, 0.005f, line);
            Ring(root, "Inner", 0.42f, 0.005f, 48, 0.003f, line);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                Line(root, "Tick", new[] { new Vector3(Mathf.Cos(a) * 0.42f, 0.005f, Mathf.Sin(a) * 0.42f), new Vector3(Mathf.Cos(a) * 0.6f, 0.005f, Mathf.Sin(a) * 0.6f) }, 0.004f, line);
            }
            Line(root, "Beam", new[] { new Vector3(0f, 0.01f, 0f), new Vector3(0f, 0.9f, 0f) }, 0.004f, AdditiveMat("BaseBeam", C("#8FFFB0") * 0.45f));
            return root;
        }
    }
}
