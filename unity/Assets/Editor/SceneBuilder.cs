using System.IO;
using ARaki.AR;
using ARaki.Audio;
using ARaki.Core;
using ARaki.Dev;
using ARaki.Game;
using ARaki.Theming;
using ARaki.World;
using ARaki.Robot;
using ARaki.Sensing;
using ARaki.UI;
using ARaki.Video;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ARaki.EditorTools
{
    /// <summary>
    /// Sürüş simülasyonu sahnesini koddan üretir: ev odası, uzay gezgini robotu, 360° kamera hattı,
    /// AR hologramları, demo görevi ve arayüz. Tekrar çalıştırılabilir.
    /// </summary>
    public static partial class SceneBuilder
    {
        const string Root = "Assets/_Project";
        const string ScenePath = Root + "/Scenes/DriveSim.unity";
        static readonly Vector3 RigOrigin = new Vector3(2000f, 0f, 2000f);

        internal static Color C(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        [MenuItem("ARaki/Kur/Sürüş sahnesi")]
        public static void BuildDriveSim()
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            SetupPipeline();
            Optimizer.ConfigurePipelines();
            Optimizer.ConfigureImports();
            Optimizer.ConfigureAndroid();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.fog = false;

            var sun = BuildLighting();
            RoomBuilder.Build();
            var robot = BuildRover();
            var link = robot.GetComponent<MockRobotLink>();

            var cam360 = BuildCam360(robot.transform);
            var rigGo = BuildViewerRig(robot.transform, cam360, out var viewer, out var viewCam);
            var rig = rigGo.GetComponent<ARRig>();

            var arRoot = new GameObject("AR Objects").transform;
            arRoot.SetParent(rigGo.transform, false);

            var map = BuildMap(robot.transform);
            var lidar = BuildLidar(map, rig, arRoot);
            var baseMarker = BuildBaseMarker(arRoot);
            var templates = BuildFindTemplates(arRoot);

            var expedition = new GameObject("Expedition").AddComponent<Expedition>();
            SetRef(expedition, "robot", robot.transform);
            SetRef(expedition, "link", link);
            SetRef(expedition, "map", map);
            SetRef(expedition, "rig", rig);
            SetRef(expedition, "arRoot", arRoot);
            SetRef(expedition, "lidar", lidar);
            SetRef(expedition, "baseMarker", baseMarker);
            SetRefs(expedition, "templates", templates);

            var session = new GameObject("GameSession").AddComponent<GameSession>();
            SetRef(session, "robot", link);
            SetRef(session, "driveInput", robot.GetComponent<KeyboardDriveInput>());
            SetRef(session, "expedition", expedition);
            SetRef(session, "viewer", viewer);
            SetRef(session, "map", map);

            var sfx = new GameObject("Sfx").AddComponent<SfxPlayer>();
            SetRef(sfx, "link", link);
            SetRef(sfx, "expedition", expedition);

            var hud = new GameObject("ExpeditionHud").AddComponent<ExpeditionHud>();
            SetRef(hud, "robot", link);
            SetRef(hud, "expedition", expedition);
            SetRef(hud, "map", map);
            SetRef(hud, "viewer", viewer);
            SetRef(hud, "rig", rig);
            SetRef(hud, "titleFont", LoadFont("Rajdhani-Bold"));
            SetRef(hud, "bodyFont", LoadFont("Rajdhani-Medium"));
            SetRef(hud, "monoFont", LoadFont("ShareTechMono-Regular"));
            SetRef(hud, "bracket", MakeBracketSprite());
            SetRef(hud, "ring", MakeRingSprite());

            var screens = new GameObject("ScreenUi").AddComponent<ScreenUi>();
            SetRef(screens, "session", session);
            SetRef(screens, "hud", hud);
            SetRef(screens, "titleFont", LoadFont("Rajdhani-Bold"));
            SetRef(screens, "bodyFont", LoadFont("Rajdhani-Medium"));
            SetRef(screens, "monoFont", LoadFont("ShareTechMono-Regular"));
            SetRef(screens, "bracket", MakeBracketSprite());
            SetRef(screens, "ring", MakeRingSprite());

            var runtime = new GameObject("RuntimeSettings").AddComponent<RuntimeSettings>();
            SetRef(runtime, "video", cam360);
            SetRef(runtime, "viewer", viewer);

            BuildTopCamera(robot.transform, viewCam);
            BuildThemeSkin(sun);

            var overlay = new GameObject("DebugOverlay").AddComponent<ARaki.Dev.DebugOverlay>();
            SetRef(overlay, "robot", link);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[SceneBuilder] sahne hazır: " + ScenePath);
        }

        // ---------------------------------------------------------------- ışık

        static Light BuildLighting()
        {
            // Yansımalar için yumuşak bir gökyüzü; ortam ışığını çalışma anında ThemeSkin temaya göre ayarlar.
            var skyMat = LoadOrCreateMaterial("EveningSky", "Skybox/Procedural");
            skyMat.SetFloat("_SunSize", 0.02f);
            skyMat.SetFloat("_AtmosphereThickness", 0.9f);
            skyMat.SetColor("_SkyTint", C("#8E9BB8"));
            skyMat.SetColor("_GroundColor", C("#8A7C70"));
            skyMat.SetFloat("_Exposure", 0.85f);
            EditorUtility.SetDirty(skyMat);
            RenderSettings.skybox = skyMat;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.0f;
            RenderSettings.reflectionIntensity = 0.5f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.subtractiveShadowColor = C("#42404A");

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = C("#FFD6A8");
            sun.intensity = 1.9f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.9f;
            sun.shadowNormalBias = 0.6f;
            sun.transform.rotation = Quaternion.Euler(48f, 320f, 0f);
            return sun;
        }

        // ---------------------------------------------------------------- uzay gezgini

        static readonly Vector3 DomeLocal = new Vector3(0f, 0.255f, -0.07f);

        static GameObject BuildRover()
        {
            var robot = new GameObject("Robot");
            robot.transform.position = HouseLayout.BasePosition;
            robot.transform.rotation = Quaternion.Euler(0f, HouseLayout.BaseYaw, 0f);
            var link = robot.AddComponent<MockRobotLink>();
            robot.AddComponent<KeyboardDriveInput>();

            var body = Mat("RoverBody", C("#DDE4EB"), smooth: 0.45f);
            var dark = Mat("RoverDark", C("#242A33"), smooth: 0.35f);
            var tire = Mat("RoverTire", C("#101215"), smooth: 0.1f);
            var hub = Mat("RoverHub", C("#9AA5B1"), smooth: 0.6f);
            var accent = Mat("RoverAccent", C("#FF5A1F"), smooth: 0.4f);
            var solar = Mat("RoverSolar", C("#0D1C3A"), smooth: 0.85f);
            var glow = UnlitMat("RoverGlow", C("#5FE3FF") * 1.5f);
            var id = Quaternion.identity;
            var t = robot.transform;

            Part(PrimitiveType.Cube, "Chassis", t, new Vector3(0f, 0.06f, 0f), new Vector3(0.17f, 0.05f, 0.26f), id, body);
            Part(PrimitiveType.Cube, "Belly", t, new Vector3(0f, 0.035f, 0f), new Vector3(0.15f, 0.03f, 0.24f), id, dark);
            Part(PrimitiveType.Cube, "SolarRim", t, new Vector3(0f, 0.087f, -0.02f), new Vector3(0.16f, 0.006f, 0.18f), id, dark);
            Part(PrimitiveType.Cube, "SolarPanel", t, new Vector3(0f, 0.092f, -0.02f), new Vector3(0.145f, 0.006f, 0.165f), id, solar);
            Part(PrimitiveType.Cube, "SensorBar", t, new Vector3(0f, 0.066f, 0.133f), new Vector3(0.12f, 0.016f, 0.012f), id, glow);
            Part(PrimitiveType.Cube, "Bumper", t, new Vector3(0f, 0.04f, 0.142f), new Vector3(0.17f, 0.03f, 0.02f), id, accent);
            foreach (float side in new[] { -1f, 1f })
                Part(PrimitiveType.Cube, "Rocker", t, new Vector3(0.1f * side, 0.055f, 0f), new Vector3(0.02f, 0.018f, 0.2f), id, dark);

            var left = new Transform[2];
            var right = new Transform[2];
            int li = 0, ri = 0;
            foreach (float side in new[] { -1f, 1f })
                foreach (float z in new[] { 0.085f, -0.085f })
                {
                    var pivot = new GameObject(side < 0 ? "WheelL" : "WheelR").transform;
                    pivot.SetParent(t, false);
                    pivot.localPosition = new Vector3(0.125f * side, 0.045f, z);
                    Part(PrimitiveType.Cylinder, "Tire", pivot, Vector3.zero, new Vector3(0.09f, 0.0175f, 0.09f), Quaternion.Euler(0f, 0f, 90f), tire);
                    Part(PrimitiveType.Cylinder, "Hub", pivot, Vector3.zero, new Vector3(0.048f, 0.0192f, 0.048f), Quaternion.Euler(0f, 0f, 90f), hub);
                    if (side < 0) left[li++] = pivot; else right[ri++] = pivot;
                }

            Part(PrimitiveType.Cylinder, "Mast", t, new Vector3(DomeLocal.x, 0.175f, DomeLocal.z), new Vector3(0.012f, 0.07f, 0.012f), id, dark);
            Part(PrimitiveType.Cylinder, "DomeRing", t, new Vector3(DomeLocal.x, DomeLocal.y - 0.018f, DomeLocal.z), new Vector3(0.056f, 0.004f, 0.056f), id, dark);
            Part(PrimitiveType.Sphere, "CameraDome", t, new Vector3(DomeLocal.x, DomeLocal.y, DomeLocal.z), new Vector3(0.05f, 0.06f, 0.05f), id, body);
            Part(PrimitiveType.Cylinder, "Antenna", t, new Vector3(0.06f, 0.165f, -0.12f), new Vector3(0.004f, 0.065f, 0.004f), id, accent);
            Part(PrimitiveType.Sphere, "AntennaTip", t, new Vector3(0.06f, 0.232f, -0.12f), new Vector3(0.012f, 0.012f, 0.012f), id, glow);

            var ledMat = UnlitMat("RoverLed", C("#40484D"));
            Part(PrimitiveType.Cube, "LedLens", t, new Vector3(0f, 0.074f, 0.137f), new Vector3(0.07f, 0.014f, 0.006f), id, ledMat);
            var ledGo = new GameObject("LedSpot");
            ledGo.transform.SetParent(t, false);
            ledGo.transform.localPosition = new Vector3(0f, 0.075f, 0.14f);
            ledGo.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
            var spot = ledGo.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.range = 4.5f;
            spot.spotAngle = 100f;
            spot.innerSpotAngle = 55f;
            spot.color = C("#FFF2D6");
            spot.intensity = 0f;
            spot.shadows = LightShadows.None;
            spot.enabled = false;
            var led = robot.AddComponent<RobotLed>();
            SetRef(led, "link", link);
            SetRef(led, "spot", spot);
            SetRef(led, "lens", t.Find("LedLens").GetComponent<MeshRenderer>());

            var spinner = robot.AddComponent<WheelSpinner>();
            SetRef(spinner, "link", link);
            SetRefs(spinner, "leftWheels", left);
            SetRefs(spinner, "rightWheels", right);
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

        // ---------------------------------------------------------------- 360° hattı

        static Sim360Camera BuildCam360(Transform robot)
        {
            var go = new GameObject("Cam360");
            go.transform.SetParent(robot, false);
            go.transform.localPosition = DomeLocal;
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 14f;           // oda küçük: uzağı çizmeye gerek yok
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = C("#14171B");
            cam.enabled = false;
            return go.AddComponent<Sim360Camera>();
        }

        static GameObject BuildViewerRig(Transform robot, Sim360Camera source, out PanoramaViewer viewer, out Camera viewCam)
        {
            var rigGo = new GameObject("ViewerRig");
            rigGo.transform.position = RigOrigin;
            var rig = rigGo.AddComponent<ARRig>();
            SetRef(rig, "robot", robot);

            var sphere = new GameObject("PanoramaSphere");
            sphere.transform.SetParent(rigGo.transform, false);
            sphere.AddComponent<MeshFilter>();
            var mr = sphere.AddComponent<MeshRenderer>();
            mr.sharedMaterial = PanoMaterial();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var camGo = new GameObject("ViewCam") { tag = "MainCamera" };
            camGo.transform.SetParent(rigGo.transform, false);
            viewCam = camGo.AddComponent<Camera>();
            viewCam.fieldOfView = 70f; // dikey; 16:9'da yaklaşık 102° yatay görüş
            viewCam.nearClipPlane = 0.03f;
            viewCam.farClipPlane = 200f;     // panorama küresi büyük; yakın kesme siyah alan bırakır
            viewCam.clearFlags = CameraClearFlags.SolidColor;
            viewCam.backgroundColor = Color.black;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;

            viewer = rigGo.AddComponent<PanoramaViewer>();
            SetRef(viewer, "source", source);
            SetRef(viewer, "viewCamera", viewCam);
            SetRef(viewer, "sphere", mr);
            SetRef(viewer, "worldAlignedRobot", robot);
            SetRef(viewer, "motion", robot.GetComponent<MockRobotLink>());
            camGo.AddComponent<AudioListener>();
            return rigGo;
        }

        static Material PanoMaterial()
        {
            var mat = UnlitMat("PanoSphere", Color.white);
            mat.SetFloat("_Cull", 0f); // içten bakılacak: iki yüzü de çiz
            return mat;
        }

        static void BuildTopCamera(Transform robot, Camera viewCam)
        {
            var topGo = new GameObject("TopCam");
            var topCam = topGo.AddComponent<Camera>();
            topCam.fieldOfView = 50f;
            topCam.clearFlags = CameraClearFlags.SolidColor;
            topCam.backgroundColor = C("#14171B");
            topCam.enabled = false;
            topCam.cullingMask = ~(1 << 2);
            var data = topGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            topGo.transform.position = new Vector3(0f, 4.4f, 0f);
            topGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var follow = topGo.AddComponent<FollowCamera>();
            SetRef(follow, "target", robot);
            var so = new SerializedObject(follow);
            so.FindProperty("height").floatValue = 4.4f;
            so.ApplyModifiedPropertiesWithoutUndo();

            var switcher = new GameObject("CameraViewSwitcher").AddComponent<CameraViewSwitcher>();
            SetRef(switcher, "robotCamera", viewCam);
            SetRef(switcher, "topCamera", topCam);
        }

        // Yerde yumuşak ışık hâlesi: hologram gerçek zemine "oturuyormuş" gibi görünür.
        static void Glow(Transform parent, string name, float size, float y)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Plane);
            quad.name = name;
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = new Vector3(0f, y, 0f);
            quad.transform.localScale = Vector3.one * (size / 10f);
            var r = quad.GetComponent<MeshRenderer>();
            r.sharedMaterial = AdditiveMat("HoloGlow", C("#5FE3FF") * 1.2f, GlowTexture());
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        internal static Texture2D GlowTexture()
        {
            const string path = Root + "/Art/Glow.png";
            if (!File.Exists(path))
            {
                const int size = 128;
                var px = new Color[size * size];
                float c = (size - 1) * 0.5f;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                        float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f);
                        px[y * size + x] = new Color(1f, 1f, 1f, a);
                    }
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tex.SetPixels(px);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = false;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        internal static LineRenderer Ring(Transform parent, string name, float radius, float y, int segments, float width, Material mat)
        {
            var pts = new Vector3[segments];
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                pts[i] = new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius);
            }
            var lr = Line(parent, name, pts, width, mat);
            lr.loop = true;
            return lr;
        }

        internal static LineRenderer Line(Transform parent, string name, Vector3[] points, float width, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = points.Length;
            lr.SetPositions(points);
            lr.widthMultiplier = width;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 2;
            lr.alignment = LineAlignment.View;
            lr.sharedMaterial = mat;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
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

        static Font LoadFont(string name)
        {
            string path = $"{Root}/UI/Fonts/{name}.ttf";
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Font>(path);
        }

        // Yalnızca köşeleri çizilen 9-dilimli çerçeve: paneller ve AR etiketleri için.
        static Sprite MakeBracketSprite()
        {
            const int s = 48, len = 16, th = 2;
            var px = new Color[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    bool edgeX = x < th || x >= s - th, edgeY = y < th || y >= s - th;
                    bool inLenX = x < len || x >= s - len, inLenY = y < len || y >= s - len;
                    bool on = (edgeX && inLenY) || (edgeY && inLenX);
                    px[y * s + x] = on ? Color.white : new Color(1f, 1f, 1f, 0f);
                }
            return SaveSprite("Bracket", s, s, px, new Vector4(len, len, len, len));
        }

        static Sprite MakeRingSprite()
        {
            const int s = 128;
            var px = new Color[s * s];
            float c = (s - 1) * 0.5f, r = s * 0.5f - 2f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = Mathf.Clamp01(1.1f - Mathf.Abs(d - r) / 1.0f);
                    px[y * s + x] = new Color(1f, 1f, 1f, a);
                }
            return SaveSprite("Ring", s, s, px, Vector4.zero);
        }

        static Sprite SaveSprite(string name, int w, int h, Color[] pixels, Vector4 border)
        {
            string path = $"{Root}/UI/Components/{name}.png";
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.filterMode = FilterMode.Bilinear;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.spriteBorder = border;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
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

        internal static Material Mat(string name, Color color, Texture2D map = null, float tilingX = 1f, float tilingY = 1f,
                            float smooth = 0.2f, Color? emission = null)
        {
            var mat = LoadOrCreateMaterial(name, "Universal Render Pipeline/Lit");
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

        internal static Material UnlitMat(string name, Color color)
        {
            var mat = LoadOrCreateMaterial(name, "Universal Render Pipeline/Unlit");
            mat.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // Yarı saydam, ışık ekleyen malzeme: hologramlar altındaki gerçek görüntüyle karışır, söndüğünde kaybolur.
        internal static Material AdditiveMat(string name, Color color, Texture2D map = null, bool particle = false)
        {
            var mat = LoadOrCreateMaterial(name, particle ? "Universal Render Pipeline/Particles/Unlit" : "Universal Render Pipeline/Unlit");
            mat.SetColor("_BaseColor", color);
            if (map != null) mat.SetTexture("_BaseMap", map);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 2f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.One);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        internal static Material LoadOrCreateMaterial(string name, string shader)
        {
            string dir = Root + "/Art/Materials";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(Root + "/Art", "Materials");
            string path = $"{dir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader.name != shader)
            {
                if (mat != null) AssetDatabase.DeleteAsset(path);
                mat = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(mat, path);
            }
            return mat;
        }

        internal static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetRefs(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------------------------------------------------------- doğrulama

        /// <summary>
        /// Sahneyi PNG olarak kaydeder (ARAKI_SHOT yoluna).
        /// ARAKI_VIEW = pano | top | equirect. ARAKI_STATE = menu | playing | pause | lab | ending.
        /// ARAKI_THEME = safari | space | mystery. ARAKI_POSE = "x,z,yaw" robotu yerleştirir. ARAKI_LOOK = bakış yaw'ı (pitch: ARAKI_PITCH).
        /// ARAKI_EXPLORE = 1: robot evi gezmiş gibi harita dolar. ARAKI_SHOWCASE = 1: robotun önüne her türden bir bulgu koyar.
        /// ARAKI_SPAWN = "kind:dx:dz": robota göre tek bir bulgu (kind 0-4). ARAKI_ESSENCE = n.
        /// </summary>
        public static void Screenshot()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string Env(string k) => System.Environment.GetEnvironmentVariable(k);
            string view = Env("ARAKI_VIEW") ?? "pano";
            string state = Env("ARAKI_STATE") ?? "playing";
            string pose = Env("ARAKI_POSE");
            string look = Env("ARAKI_LOOK");

            string themeName = Env("ARAKI_THEME");
            if (!string.IsNullOrEmpty(themeName))
                ThemeCatalog.Set(themeName == "space" ? ThemeId.Space : themeName == "mystery" ? ThemeId.Mystery : ThemeId.Safari, false);
            else UiTheme.EnsureApplied();
            Object.FindFirstObjectByType<ThemeSkin>().Apply();
            {
                int n = 0; string first = "";
                foreach (var surf in Object.FindObjectsByType<ThemeSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    n++;
                    if (surf.Role == SurfaceRole.Wall && first == "") first = surf.GetComponent<Renderer>().sharedMaterial.name + " / " + (surf.ThemeMaterials != null ? surf.ThemeMaterials.Length.ToString() : "null");
                }
                int walls = 0, ceil = 0, win = 0;
                foreach (var surf in Object.FindObjectsByType<ThemeSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                { if (surf.Role == SurfaceRole.Wall) walls++; else if (surf.Role == SurfaceRole.Ceiling) ceil++; else win++; }
                int wallObjects = 0;
                foreach (var go in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (go.name == "Wall") wallObjects++;
                Debug.Log("[SceneBuilder] duvar " + walls + " tavan " + ceil + " pencere " + win + " duvar nesnesi " + wallObjects);
                Debug.Log("[SceneBuilder] tema yüzeyi: " + n + "  ilk duvar: " + first + "  tema: " + ThemeCatalog.Current);
            }

            var robot = GameObject.Find("Robot");
            var link = robot.GetComponent<MockRobotLink>();
            link.Initialize();
            var cam360 = Object.FindFirstObjectByType<Sim360Camera>();
            var viewer = Object.FindFirstObjectByType<PanoramaViewer>();
            var hud = Object.FindFirstObjectByType<ExpeditionHud>();
            var screens = Object.FindFirstObjectByType<ScreenUi>();
            var expedition = Object.FindFirstObjectByType<Expedition>();
            var session = Object.FindFirstObjectByType<GameSession>();
            var map = Object.FindFirstObjectByType<ExplorationMap>();
            var topCam = GameObject.Find("TopCam").GetComponent<Camera>();
            var cam = view == "top" ? topCam : viewer.ViewCamera;
            viewer.ViewCamera.enabled = view != "top";
            topCam.enabled = view == "top";
            if (view == "top")
            {
                var follow = topCam.GetComponent<FollowCamera>();
                if (follow != null) follow.enabled = false;
                topCam.orthographic = true;
                topCam.orthographicSize = 5.1f;
                topCam.transform.position = new Vector3(0f, 14f, -0.25f);
                topCam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }

            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            viewer.Bind();
            Physics.SyncTransforms();

            hud.Build();
            screens.Build();
            map.Init();
            expedition.Seed(Env("ARAKI_SEED") != null ? int.Parse(Env("ARAKI_SEED"), inv) : 1234);

            if (state == "menu") session.ShowMenu();
            else
            {
                expedition.Begin(true);
                map.Running = true;
                session.ForceState(GameState.Playing);
            }

            if (!string.IsNullOrEmpty(pose))
            {
                var v = pose.Split(',');
                link.Teleport(new Vector3(float.Parse(v[0], inv), 0f, float.Parse(v[1], inv)), float.Parse(v[2], inv));
                Physics.SyncTransforms();
            }
            if (Env("ARAKI_LED") == "1") link.SetLed(true);

            // harita: robot evi gezmiş gibi (koridor ve odaların ortalarından tarar)
            if (Env("ARAKI_EXPLORE") == "1")
            {
                var spots = new System.Collections.Generic.List<Vector3>();
                foreach (var r in HouseLayout.Rooms)
                {
                    spots.Add(r.Center);
                    spots.Add(new Vector3(r.Rect.xMin + 0.8f, 0f, r.Rect.center.y));
                    spots.Add(new Vector3(r.Rect.xMax - 0.8f, 0f, r.Rect.center.y));
                }
                for (float x = -5f; x <= 5f; x += 1.2f) spots.Add(new Vector3(x, 0f, 0f));
                map.SurveyFrom(spots.ToArray(), 2);
                for (int i = 0; i < 40; i++) expedition.Tick(0.5f);
            }

            if (!string.IsNullOrEmpty(Env("ARAKI_ESSENCE")))
                expedition.DebugSetEssence(int.Parse(Env("ARAKI_ESSENCE"), inv));

            // tek bir bulgu: "tür:ileri:yan" (robota göre metre)
            string spawn = Env("ARAKI_SPAWN");
            if (!string.IsNullOrEmpty(spawn))
            {
                var sp = spawn.Split(':');
                var f = SpawnNearRobot(expedition, link.transform, int.Parse(sp[0], inv), float.Parse(sp[1], inv), float.Parse(sp[2], inv));
                f.Progress = sp.Length > 3 ? float.Parse(sp[3], inv) : 0f;
            }
            if (Env("ARAKI_SHOWCASE") == "1")
                for (int k = 0; k < 5; k++) SpawnNearRobot(expedition, link.transform, k, 1.35f + 0.05f * k, (k - 2) * 0.42f);

            Physics.SyncTransforms();
            screens.Show(state == "pause" ? GameState.Paused : state == "lab" ? GameState.Lab : state == "ending" ? GameState.Ending : session.State);
            if (state == "lab" || state == "ending" || state == "pause") session.ForceState(state == "lab" ? GameState.Lab : state == "ending" ? GameState.Ending : GameState.Paused);
            if (view == "top") hud.SetVisible(false);

            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) ps.Simulate(2.2f, true, true);
            foreach (var led in Object.FindObjectsByType<RobotLed>(FindObjectsSortMode.None)) led.Tick(1f);
            string pitch = Env("ARAKI_PITCH");
            float pitchValue = pitch != null ? float.Parse(pitch, inv) : viewer.DefaultPitch * (state == "menu" ? 0.6f : 1f);
            viewer.SetLook(!string.IsNullOrEmpty(look) ? float.Parse(look, inv) : (state == "menu" ? 35f : 0f), pitchValue);

            // Shader varyantları arka planda derlenir; hazır olana kadar turuncu yer tutucu çizilir.
            for (int i = 0; i < 80; i++)
            {
                RenderFrame(cam, cam360, viewer, hud, expedition, view);
                System.Threading.Thread.Sleep(250);
                if (!ShaderUtil.anythingCompiling) break;
            }
            RenderFrame(cam, cam360, viewer, hud, expedition, view);

            string outPath = Env("ARAKI_SHOT") ?? "shot.png";
            if (view == "equirect") SavePng(cam360.Equirect, outPath);
            else SavePng(rt, outPath);
            cam.targetTexture = null;
            Time.timeScale = 1f;
            Debug.Log("[SceneBuilder] ekran görüntüsü: " + outPath);
        }

        static Find SpawnNearRobot(Expedition expedition, Transform robot, int kind, float forward, float side)
        {
            var pos = robot.position + robot.forward * forward + robot.right * side;
            pos.y = 0f;
            var f = expedition.Spawn((FindKind)kind, ARaki.World.HouseLayout.RoomAt(pos), pos, kind == 4 ? 3 : 1);
            f.Revealed = true;
            f.PingUntil = float.MaxValue;
            return f;
        }

        static void RenderFrame(Camera cam, Sim360Camera cam360, PanoramaViewer viewer, ExpeditionHud hud, Expedition expedition, string view)
        {
            cam360.Capture();
            expedition.SyncVisuals();
            Object.FindFirstObjectByType<LidarOverlay>()?.Tick(0f);
            Object.FindFirstObjectByType<ExplorationMap>()?.FlushTexture();
            if (view != "top") viewer.Apply();
            if (view != "top")
            {
                hud.Tick(0.016f);
                Canvas.ForceUpdateCanvases();
            }
            cam.Render();
        }

        static void SavePng(RenderTexture rt, string path)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = prev;
        }
    }
}
