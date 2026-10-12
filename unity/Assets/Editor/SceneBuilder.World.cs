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

        /// <summary>Duvar/tavan/pencere malzemesi (ThemeId sırasıyla; tek tema var).</summary>
        internal static Material[] SkyMaterials(SurfaceRole role)
        {
            return new[]
            {
                role == SurfaceRole.Wall ? HomeWall(RoomKind.Hall)
                : role == SurfaceRole.Ceiling ? Mat("Home_Ceiling", C("#F6F3EE"), smooth: 0.1f)
                : Mat("Home_Window", C("#CFE4F7"), smooth: 0.1f, emission: C("#A8CDF0") * 1.1f),
            };
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

        /// <summary>
        /// Üs: Ocak. Katı, gölgeli, oyuncak gibi bir lamba kulesi (hologram değil). Her yerden görünsün diye 0,9 m yüksekliğinde,
        /// ışığı sıcak; zeminde hafif bir ışık havuzu var. Gerçek robotta üs işaretçisinin (ArUco) üstüne oturur.
        /// </summary>
        static Transform BuildBaseMarker(Transform arRoot)
        {
            var root = new GameObject("AR_Base").transform;
            root.SetParent(arRoot, false);

            var stone = Toon("BaseStone", "#9A7A56", "#4E3A26", "#FFE0B0", rimStrength: 0.4f);
            var amber = Toon("BaseInlay", "#FFC46A", "#B8742A", "#FFFFFF", "#FFB050", 0.7f, rimStrength: 0.6f);
            var iron = Toon("BaseIron", "#3A3436", "#121011", "#FFD9A0", rimStrength: 0.7f);
            var lamp = Toon("BaseLamp", "#FFE7A8", "#FFC060", "#FFFFFF", "#FFD27A", 2.2f, rimStrength: 0.2f);
            var roof = Toon("BaseRoof", "#B5503A", "#5C2218", "#FFB8A0", rimStrength: 0.6f);

            var glow = AdditiveMat("BaseGlow", C("#FFC878") * 0.35f, GlowTexture());
            var pool = GameObject.CreatePrimitive(PrimitiveType.Plane);
            pool.name = "LightPool";
            Object.DestroyImmediate(pool.GetComponent<Collider>());
            pool.transform.SetParent(root, false);
            pool.transform.localPosition = new Vector3(0f, 0.003f, 0f);
            pool.transform.localScale = Vector3.one * 0.2f;
            pool.GetComponent<MeshRenderer>().sharedMaterial = glow;
            pool.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            Prim(root, PrimitiveType.Cylinder, "Platform", new Vector3(0f, 0.02f, 0f), new Vector3(1.0f, 0.02f, 1.0f), stone);
            Prim(root, PrimitiveType.Cylinder, "Inlay", new Vector3(0f, 0.0235f, 0f), new Vector3(0.84f, 0.0235f, 0.84f), amber);
            var tower = new GameObject("Tower").transform;      // kule platformun batı kenarında: robot ortada kenetlenir
            tower.SetParent(root, false);
            tower.localPosition = new Vector3(-0.36f, 0f, 0f);
            Prim(tower, PrimitiveType.Cylinder, "Post", new Vector3(0f, 0.315f, 0f), new Vector3(0.07f, 0.275f, 0.07f), iron);
            Prim(tower, PrimitiveType.Cylinder, "Collar", new Vector3(0f, 0.6f, 0f), new Vector3(0.14f, 0.015f, 0.14f), iron);
            Prim(tower, PrimitiveType.Cube, "Lantern", new Vector3(0f, 0.7f, 0f), new Vector3(0.15f, 0.18f, 0.15f), lamp);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    Prim(tower, PrimitiveType.Cylinder, "Frame", new Vector3(sx * 0.082f, 0.7f, sz * 0.082f), new Vector3(0.016f, 0.1f, 0.016f), iron);
            MeshPart(tower, "Roof", ARaki.AR.ProceduralMeshes.Cone(0.15f, 0.012f, 0.11f, 4), new Vector3(0f, 0.8f, 0f), new Vector3(0f, 45f, 0f), roof);
            Ball(tower, "Finial", new Vector3(0f, 0.925f, 0f), Vector3.one * 0.035f, amber);

            // dört köşede küçük taş fenerler: alan sınırı
            foreach (float a in new[] { 45f, 135f, 225f, 315f })
            {
                float r = a * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Sin(r) * 0.43f, 0f, Mathf.Cos(r) * 0.43f);
                Prim(root, PrimitiveType.Cylinder, "Stud", p + new Vector3(0f, 0.07f, 0f), new Vector3(0.05f, 0.03f, 0.05f), iron);
                Ball(root, "StudLamp", p + new Vector3(0f, 0.12f, 0f), Vector3.one * 0.05f, lamp);
            }
            return root;
        }
    }
}
