using System.IO;
using ARaki.Core;
using ARaki.Dev;
using ARaki.Robot;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ARaki.EditorTools
{
    /// <summary>Sürüş simülasyonu sahnesini (oda + robot + kameralar) ve URP ayarını koddan üretir; tekrar çalıştırılabilir.</summary>
    public static class SceneBuilder
    {
        const string Root = "Assets/_Project";
        const string ScenePath = Root + "/Scenes/DriveSim.unity";

        static Color C(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        [MenuItem("ARaki/Kur/Sürüş sahnesi")]
        public static void BuildDriveSim()
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            SetupPipeline();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = C("#B9C4D0");
            RenderSettings.ambientEquatorColor = C("#8F8A84");
            RenderSettings.ambientGroundColor = C("#4A4540");
            RenderSettings.fog = false;

            BuildLighting();
            BuildRoom();
            var robot = BuildRobot();
            BuildCameras(robot);
            BuildPostProcessing();

            var overlay = new GameObject("DebugOverlay").AddComponent<ARaki.Dev.DebugOverlay>();
            SetRef(overlay, "robot", robot.GetComponent<MockRobotLink>());

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[SceneBuilder] sahne hazır: " + ScenePath);
        }

        // ---------------------------------------------------------------- ışık

        static void BuildLighting()
        {
            // Pencereden giren sıcak güneş
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = C("#FFF1DC");
            sun.intensity = 1.35f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.transform.rotation = Quaternion.Euler(48f, 115f, 0f);

            // Tavan lambası: odayı yumuşatan dolgu ışığı
            var fill = new GameObject("RoomLamp").AddComponent<Light>();
            fill.type = LightType.Point;
            fill.color = C("#FFE6C7");
            fill.intensity = 5f;
            fill.range = 9f;
            fill.shadows = LightShadows.None;
            fill.transform.position = new Vector3(0f, 2.2f, 0f);
        }

        // ---------------------------------------------------------------- oda

        static void BuildRoom()
        {
            var wall = Mat("Wall", C("#E7E2D8"), smooth: 0.05f);
            var trim = Mat("Trim", C("#F6F3EC"), smooth: 0.15f);
            var wood = Mat("FloorWood", Color.white, WoodTexture(), 6f, 5f, smooth: 0.35f);
            var rug = Mat("Rug", C("#3F5D75"), smooth: 0.02f);
            var sofa = Mat("Sofa", C("#7E9C97"), smooth: 0.05f);
            var sofaDark = Mat("SofaDark", C("#5F7B77"), smooth: 0.05f);
            var tableWood = Mat("TableWood", C("#B98A5C"), smooth: 0.3f);
            var shelf = Mat("Shelf", C("#4F3E32"), smooth: 0.25f);
            var book1 = Mat("BookA", C("#C94F3D"), smooth: 0.1f);
            var book2 = Mat("BookB", C("#E3B341"), smooth: 0.1f);
            var book3 = Mat("BookC", C("#3E6F8F"), smooth: 0.1f);
            var orange = Mat("PrintOrange", C("#FF5A1F"), smooth: 0.4f);
            var teal = Mat("PrintTeal", C("#19A99A"), smooth: 0.4f);
            var window = Mat("Window", C("#FFF6E0"), emission: C("#FFE9B8") * 2.2f);

            var room = new GameObject("Room").transform;
            const float w = 6f, d = 5f, h = 2.5f, t = 0.1f;

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(room);
            floor.transform.localScale = new Vector3(w / 10f, 1f, d / 10f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = wood;
            Object.DestroyImmediate(floor.GetComponent<Collider>()); // zemin çarpma saymaz

            Box("WallN", room, new Vector3(0f, h / 2f, d / 2f + t / 2f), new Vector3(w + 2 * t, h, t), wall);
            Box("WallS", room, new Vector3(0f, h / 2f, -d / 2f - t / 2f), new Vector3(w + 2 * t, h, t), wall);
            Box("WallE", room, new Vector3(w / 2f + t / 2f, h / 2f, 0f), new Vector3(t, h, d), wall);
            Box("WallW", room, new Vector3(-w / 2f - t / 2f, h / 2f, 0f), new Vector3(t, h, d), wall);

            // Süpürgelikler
            Box("TrimN", room, new Vector3(0f, 0.05f, d / 2f - 0.01f), new Vector3(w, 0.1f, 0.02f), trim, false);
            Box("TrimS", room, new Vector3(0f, 0.05f, -d / 2f + 0.01f), new Vector3(w, 0.1f, 0.02f), trim, false);
            Box("TrimE", room, new Vector3(w / 2f - 0.01f, 0.05f, 0f), new Vector3(0.02f, 0.1f, d), trim, false);
            Box("TrimW", room, new Vector3(-w / 2f + 0.01f, 0.05f, 0f), new Vector3(0.02f, 0.1f, d), trim, false);

            // Pencere (ışığın geldiği duvar)
            Box("Window", room, new Vector3(w / 2f - 0.02f, 1.5f, 0.2f), new Vector3(0.02f, 1.1f, 1.6f), window, false);

            // Halı
            Box("Rug", room, new Vector3(0.2f, 0.006f, 0f), new Vector3(2.6f, 0.012f, 1.8f), rug, false);

            // Koltuk
            Box("SofaBase", room, new Vector3(-1.6f, 0.2f, 2.0f), new Vector3(2.0f, 0.4f, 0.85f), sofa);
            Box("SofaBack", room, new Vector3(-1.6f, 0.6f, 2.35f), new Vector3(2.0f, 0.5f, 0.2f), sofaDark);
            Box("SofaArmL", room, new Vector3(-2.55f, 0.35f, 2.0f), new Vector3(0.18f, 0.7f, 0.85f), sofaDark);
            Box("SofaArmR", room, new Vector3(-0.65f, 0.35f, 2.0f), new Vector3(0.18f, 0.7f, 0.85f), sofaDark);

            // Sehpa
            Box("TableTop", room, new Vector3(1.2f, 0.42f, -0.2f), new Vector3(1.0f, 0.04f, 0.6f), tableWood);
            foreach (var sx in new[] { -0.44f, 0.44f })
                foreach (var sz in new[] { -0.24f, 0.24f })
                    Box("TableLeg", room, new Vector3(1.2f + sx, 0.2f, -0.2f + sz), new Vector3(0.05f, 0.4f, 0.05f), tableWood);

            // Kitaplık
            Box("Shelf", room, new Vector3(-2.7f, 0.9f, -1.0f), new Vector3(0.3f, 1.8f, 1.2f), shelf);
            Box("BookA", room, new Vector3(-2.52f, 1.45f, -0.85f), new Vector3(0.1f, 0.28f, 0.5f), book1, false);
            Box("BookB", room, new Vector3(-2.52f, 1.43f, -1.15f), new Vector3(0.1f, 0.24f, 0.4f), book2, false);
            Box("BookC", room, new Vector3(-2.52f, 0.95f, -1.0f), new Vector3(0.1f, 0.3f, 0.6f), book3, false);

            // 3D baskı görev objeleri (oyunda kule / yakıt olacak yerlerin yer tutucuları)
            Cylinder("PrintTower1", room, new Vector3(0.4f, 0.09f, 1.1f), new Vector3(0.12f, 0.09f, 0.12f), orange);
            Cylinder("PrintTower2", room, new Vector3(2.2f, 0.09f, 1.7f), new Vector3(0.12f, 0.09f, 0.12f), orange);
            Cylinder("PrintFuel", room, new Vector3(1.9f, 0.12f, -1.7f), new Vector3(0.14f, 0.12f, 0.14f), teal);
            Box("PrintBlock", room, new Vector3(-0.3f, 0.06f, -1.3f), new Vector3(0.16f, 0.12f, 0.16f), orange);
        }

        static void Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.position = pos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        static void Cylinder(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // ---------------------------------------------------------------- robot

        static GameObject BuildRobot()
        {
            var robot = new GameObject("Robot");
            robot.transform.position = new Vector3(-1.6f, 0f, -1.4f);
            robot.transform.rotation = Quaternion.Euler(0f, 40f, 0f);
            robot.AddComponent<MockRobotLink>();
            robot.AddComponent<KeyboardDriveInput>();

            var body = Mat("RobotBody", C("#E6ECF1"), smooth: 0.35f);
            var dark = Mat("RobotDark", C("#2B323B"), smooth: 0.3f);
            var tire = Mat("RobotTire", C("#15181C"), smooth: 0.1f);
            var accent = Mat("RobotAccent", C("#FF5A1F"), smooth: 0.4f);
            var id = Quaternion.identity;

            Part(PrimitiveType.Cube, "Chassis", robot.transform, new Vector3(0f, 0.045f, 0f), new Vector3(0.17f, 0.045f, 0.22f), id, body);
            Part(PrimitiveType.Cube, "Plow", robot.transform, new Vector3(0f, 0.032f, 0.125f), new Vector3(0.17f, 0.03f, 0.03f), id, accent);
            Part(PrimitiveType.Cube, "Deck", robot.transform, new Vector3(0f, 0.075f, -0.02f), new Vector3(0.13f, 0.02f, 0.14f), id, dark);
            foreach (float side in new[] { -1f, 1f })
                Part(PrimitiveType.Cylinder, side < 0 ? "WheelL" : "WheelR", robot.transform,
                    new Vector3(0.105f * side, 0.035f, -0.015f), new Vector3(0.07f, 0.014f, 0.07f),
                    Quaternion.Euler(0f, 0f, 90f), tire);
            Part(PrimitiveType.Sphere, "Caster", robot.transform, new Vector3(0f, 0.012f, -0.095f), new Vector3(0.024f, 0.024f, 0.024f), id, dark);
            Part(PrimitiveType.Cylinder, "CameraMast", robot.transform, new Vector3(0f, 0.105f, 0f), new Vector3(0.012f, 0.03f, 0.012f), id, dark);
            Part(PrimitiveType.Sphere, "CameraDome", robot.transform, new Vector3(0f, 0.14f, 0f), new Vector3(0.045f, 0.06f, 0.045f), id, body);
            return robot;
        }

        static void Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Quaternion rot, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // ---------------------------------------------------------------- kameralar

        static void BuildCameras(GameObject robot)
        {
            // Robot kamerası: Theta S kubbesinin yeri, hafif aşağı bakar; robotun önü görüntünün altında görünür.
            var robotCamGo = new GameObject("RobotCam") { tag = "MainCamera" };
            robotCamGo.transform.SetParent(robot.transform, false);
            robotCamGo.transform.localPosition = new Vector3(0f, 0.15f, 0.02f);
            robotCamGo.transform.localRotation = Quaternion.Euler(14f, 0f, 0f);
            var robotCam = robotCamGo.AddComponent<Camera>();
            robotCam.fieldOfView = 68f; // dikey; 16:9'da yaklaşık 100° yatay görüş
            robotCam.nearClipPlane = 0.02f;
            robotCam.farClipPlane = 40f;
            robotCam.clearFlags = CameraClearFlags.SolidColor;
            robotCam.backgroundColor = C("#202428");
            var d1 = robotCamGo.AddComponent<UniversalAdditionalCameraData>();
            d1.renderPostProcessing = true;
            d1.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            d1.antialiasingQuality = AntialiasingQuality.High;

            // Üstten kamera (geliştirme için)
            var topGo = new GameObject("TopCam");
            var topCam = topGo.AddComponent<Camera>();
            topCam.fieldOfView = 50f;
            topCam.clearFlags = CameraClearFlags.SolidColor;
            topCam.backgroundColor = C("#202428");
            topCam.enabled = false;
            var d2 = topGo.AddComponent<UniversalAdditionalCameraData>();
            d2.renderPostProcessing = true;
            topGo.transform.position = new Vector3(0f, 4.2f, 0f);
            topGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var follow = topGo.AddComponent<FollowCamera>();
            SetRef(follow, "target", robot.transform);
            var so = new SerializedObject(follow);
            so.FindProperty("height").floatValue = 4.2f;
            so.ApplyModifiedPropertiesWithoutUndo();

            var switcher = new GameObject("CameraViewSwitcher").AddComponent<CameraViewSwitcher>();
            SetRef(switcher, "robotCamera", robotCam);
            SetRef(switcher, "topCamera", topCam);
        }

        // ---------------------------------------------------------------- görüntü sonrası işleme

        static void BuildPostProcessing()
        {
            const string path = Root + "/Settings/ARaki_Volume.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.25f);
            bloom.threshold.Override(1.0f);
            bloom.scatter.Override(0.6f);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.22f);
            vignette.smoothness.Override(0.5f);

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.15f);
            color.saturation.Override(6f);
            color.contrast.Override(6f);

            foreach (var comp in profile.components)
            {
                AssetDatabase.AddObjectToAsset(comp, profile);
                EditorUtility.SetDirty(comp);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var vol = new GameObject("PostProcessing").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
        }

        // ---------------------------------------------------------------- boru hattı, doku, malzeme

        static void SetupPipeline()
        {
            const string rendererPath = Root + "/Settings/ARaki_Renderer.asset";
            const string assetPath = Root + "/Settings/ARaki_URP.asset";
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
            if (rp == null)
            {
                var data = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(data, rendererPath);
                rp = UniversalRenderPipelineAsset.Create(data);
                AssetDatabase.CreateAsset(rp, assetPath);
            }
            rp.msaaSampleCount = 4;
            rp.shadowDistance = 12f;
            EditorUtility.SetDirty(rp);

            GraphicsSettings.defaultRenderPipeline = rp;
            int original = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i);
                QualitySettings.renderPipeline = rp;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            }
            QualitySettings.SetQualityLevel(original);
        }

        // 1 m x 1 m'lik parke dokusu: 8 tahta sırası, rastgele ton farkı ve derz çizgileri.
        static Texture2D WoodTexture()
        {
            const string path = Root + "/Art/Wood.png";
            const int size = 512, rows = 8;
            var rng = new System.Random(7);
            var px = new Color[size * size];
            int rowH = size / rows;
            for (int r = 0; r < rows; r++)
            {
                float tone = 0.88f + (float)rng.NextDouble() * 0.22f;
                int joint = rng.Next(size / 4, size * 3 / 4);
                float tone2 = 0.88f + (float)rng.NextDouble() * 0.22f;
                for (int y = r * rowH; y < (r + 1) * rowH; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float tt = x < joint ? tone : tone2;
                        float grain = 0.96f + 0.04f * Mathf.Sin(x * 0.07f + r * 3.1f + Mathf.Sin(y * 0.2f));
                        var baseCol = new Color(0.62f, 0.45f, 0.30f) * (tt * grain);
                        bool groove = (y - r * rowH) < 2 || Mathf.Abs(x - joint) < 1;
                        px[y * size + x] = groove ? baseCol * 0.55f : baseCol;
                    }
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.anisoLevel = 8;
            imp.mipmapEnabled = true;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material Mat(string name, Color color, Texture2D map = null, float tilingX = 1f, float tilingY = 1f,
                            float smooth = 0.2f, Color? emission = null)
        {
            string dir = Root + "/Art/Materials";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(Root + "/Art", "Materials");
            string path = $"{dir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smooth);
            mat.SetFloat("_EnvironmentReflections", 0f);
            mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            if (map != null)
            {
                mat.SetTexture("_BaseMap", map);
                mat.SetTextureScale("_BaseMap", new Vector2(tilingX, tilingY));
            }
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Doğrulama için: sahneyi PNG olarak kaydeder (ARAKI_SHOT yoluna).
        /// ARAKI_VIEW = robot | top | iso (varsayılan robot).
        /// </summary>
        public static void Screenshot()
        {
            EditorSceneManager.OpenScene(ScenePath);
            string view = System.Environment.GetEnvironmentVariable("ARAKI_VIEW") ?? "robot";
            var robotCam = GameObject.Find("RobotCam").GetComponent<Camera>();
            var topCam = GameObject.Find("TopCam").GetComponent<Camera>();
            var cam = view == "robot" ? robotCam : topCam;
            robotCam.enabled = false;
            topCam.enabled = false;
            cam.enabled = true;
            if (view == "iso")
            {
                cam.transform.position = new Vector3(0.6f, 1.6f, -2.2f);
                cam.transform.LookAt(new Vector3(-0.5f, 0.2f, -0.5f));
            }
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            // Shader varyantları arka planda derlenir; hazır olana kadar turuncu yer tutucu çizilir.
            for (int i = 0; i < 80; i++)
            {
                cam.Render();
                System.Threading.Thread.Sleep(250);
                if (!ShaderUtil.anythingCompiling) break;
            }
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            string outPath = System.Environment.GetEnvironmentVariable("ARAKI_SHOT") ?? "shot.png";
            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Debug.Log("[SceneBuilder] ekran görüntüsü: " + outPath);
        }
    }
}
