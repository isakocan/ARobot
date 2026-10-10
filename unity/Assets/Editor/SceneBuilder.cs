using System.IO;
using ARaki.AR;
using ARaki.Core;
using ARaki.Dev;
using ARaki.Game;
using ARaki.Robot;
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
    public static class SceneBuilder
    {
        const string Root = "Assets/_Project";
        const string ScenePath = Root + "/Scenes/DriveSim.unity";
        static readonly Vector3 RigOrigin = new Vector3(2000f, 0f, 2000f);

        static Color C(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        [MenuItem("ARaki/Kur/Sürüş sahnesi")]
        public static void BuildDriveSim()
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            SetupPipeline();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = C("#4A5873");
            RenderSettings.ambientEquatorColor = C("#5E5A62");
            RenderSettings.ambientGroundColor = C("#2E2A2C");
            RenderSettings.fog = false;

            BuildLighting();
            BuildRoom();
            var robot = BuildRover();
            var link = robot.GetComponent<MockRobotLink>();

            var cam360 = BuildCam360(robot.transform);
            var rigGo = BuildViewerRig(robot.transform, cam360, out var viewer, out var viewCam);
            var rig = rigGo.GetComponent<ARRig>();

            var arRoot = new GameObject("AR Objects").transform;
            arRoot.SetParent(rigGo.transform, false);

            var points = new[]
            {
                MakePoint("TowerA", "KULE ALFA", PointKind.Tower, new Vector3(0.4f, 0f, 1.1f), 0.6f, rig, arRoot, HoloTower),
                MakePoint("TowerB", "KULE BETA", PointKind.Tower, new Vector3(2.2f, 0f, 1.7f), 0.6f, rig, arRoot, HoloTower),
                MakePoint("Fuel", "YAKIT İSTASYONU", PointKind.Fuel, new Vector3(1.9f, 0f, -1.7f), 0.55f, rig, arRoot, HoloFuel),
                MakePoint("Base", "ANA ÜS", PointKind.Base, robot.transform.position, 0.45f, rig, arRoot, HoloBase),
            };
            var threats = new[]
            {
                MakeThreat("A", new Vector3(1.0f, 0.28f, 0.6f), robot.transform, link, rig, arRoot),
                MakeThreat("B", new Vector3(-0.8f, 0.28f, 1.2f), robot.transform, link, rig, arRoot),
            };

            var missionGo = new GameObject("Mission");
            var mission = missionGo.AddComponent<MissionController>();
            SetRef(mission, "robot", robot.transform);
            SetRef(mission, "link", link);
            SetRefs(mission, "points", points);
            SetRefs(mission, "threats", threats);

            var hudGo = new GameObject("SpaceHud");
            var hud = hudGo.AddComponent<SpaceHud>();
            SetRef(hud, "robot", link);
            SetRef(hud, "mission", mission);
            SetRef(hud, "viewer", viewer);
            SetRef(hud, "rig", rig);
            SetRef(hud, "titleFont", LoadFont("Rajdhani-Bold"));
            SetRef(hud, "bodyFont", LoadFont("Rajdhani-Medium"));
            SetRef(hud, "monoFont", LoadFont("ShareTechMono-Regular"));
            SetRef(hud, "bracket", MakeBracketSprite());
            SetRef(hud, "ring", MakeRingSprite());

            BuildTopCamera(robot.transform, viewCam);
            BuildPostProcessing();

            var overlay = new GameObject("DebugOverlay").AddComponent<ARaki.Dev.DebugOverlay>();
            SetRef(overlay, "robot", link);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[SceneBuilder] sahne hazır: " + ScenePath);
        }

        // ---------------------------------------------------------------- ışık

        static void BuildLighting()
        {
            // Akşam: pencereden düşük, sıcak bir güneş ışığı
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = C("#FFC48A");
            sun.intensity = 1.0f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.8f;
            sun.transform.rotation = Quaternion.Euler(30f, 262f, 0f);

            var lamp = new GameObject("RoomLamp").AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = C("#FFD2A0");
            lamp.intensity = 2.2f;
            lamp.range = 9f;
            lamp.shadows = LightShadows.None;
            lamp.transform.position = new Vector3(0f, 1.9f, 0f);
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
            var window = Mat("Window", C("#FFD9A0"), emission: C("#FFB866") * 1.6f);

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

            Box("TrimN", room, new Vector3(0f, 0.05f, d / 2f - 0.01f), new Vector3(w, 0.1f, 0.02f), trim, false);
            Box("TrimS", room, new Vector3(0f, 0.05f, -d / 2f + 0.01f), new Vector3(w, 0.1f, 0.02f), trim, false);
            Box("TrimE", room, new Vector3(w / 2f - 0.01f, 0.05f, 0f), new Vector3(0.02f, 0.1f, d), trim, false);
            Box("TrimW", room, new Vector3(-w / 2f + 0.01f, 0.05f, 0f), new Vector3(0.02f, 0.1f, d), trim, false);

            Box("Ceiling", room, new Vector3(0f, h + 0.025f, 0f), new Vector3(w + 2 * t, 0.05f, d + 2 * t), Mat("Ceiling", C("#EDE8DD"), smooth: 0.05f), false);
            room.Find("Ceiling").GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            room.Find("Ceiling").gameObject.layer = 2; // üstten kamera tavanı çizmez
            Box("Window", room, new Vector3(w / 2f - 0.02f, 1.5f, 0.2f), new Vector3(0.02f, 1.1f, 1.6f), window, false);
            Box("Rug", room, new Vector3(0.2f, 0.006f, 0f), new Vector3(2.6f, 0.012f, 1.8f), rug, false);

            Box("SofaBase", room, new Vector3(-1.6f, 0.2f, 2.0f), new Vector3(2.0f, 0.4f, 0.85f), sofa);
            Box("SofaBack", room, new Vector3(-1.6f, 0.6f, 2.35f), new Vector3(2.0f, 0.5f, 0.2f), sofaDark);
            Box("SofaArmL", room, new Vector3(-2.55f, 0.35f, 2.0f), new Vector3(0.18f, 0.7f, 0.85f), sofaDark);
            Box("SofaArmR", room, new Vector3(-0.65f, 0.35f, 2.0f), new Vector3(0.18f, 0.7f, 0.85f), sofaDark);

            Box("TableTop", room, new Vector3(1.2f, 0.42f, -0.2f), new Vector3(1.0f, 0.04f, 0.6f), tableWood);
            foreach (var sx in new[] { -0.44f, 0.44f })
                foreach (var sz in new[] { -0.24f, 0.24f })
                    Box("TableLeg", room, new Vector3(1.2f + sx, 0.2f, -0.2f + sz), new Vector3(0.05f, 0.4f, 0.05f), tableWood);

            Box("Shelf", room, new Vector3(-2.7f, 0.9f, -1.0f), new Vector3(0.3f, 1.8f, 1.2f), shelf);
            Box("BookA", room, new Vector3(-2.52f, 1.45f, -0.85f), new Vector3(0.1f, 0.28f, 0.5f), book1, false);
            Box("BookB", room, new Vector3(-2.52f, 1.43f, -1.15f), new Vector3(0.1f, 0.24f, 0.4f), book2, false);
            Box("BookC", room, new Vector3(-2.52f, 0.95f, -1.0f), new Vector3(0.1f, 0.3f, 0.6f), book3, false);

            // Gerçek 3D baskı objeleri (kamera bunları görüp rol atayacak)
            Cylinder("PrintTower1", room, new Vector3(0.4f, 0.09f, 1.1f), new Vector3(0.12f, 0.09f, 0.12f), orange);
            Cylinder("PrintTower2", room, new Vector3(2.2f, 0.09f, 1.7f), new Vector3(0.12f, 0.09f, 0.12f), orange);
            Cylinder("PrintFuel", room, new Vector3(1.9f, 0.12f, -1.7f), new Vector3(0.14f, 0.12f, 0.14f), teal);
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

        // ---------------------------------------------------------------- uzay gezgini

        static readonly Vector3 DomeLocal = new Vector3(0f, 0.255f, -0.07f);

        static GameObject BuildRover()
        {
            var robot = new GameObject("Robot");
            robot.transform.position = new Vector3(-1.6f, 0f, -1.4f);
            robot.transform.rotation = Quaternion.Euler(0f, 40f, 0f);
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
            cam.farClipPlane = 40f;
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
            viewCam.farClipPlane = 200f;
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

        // ---------------------------------------------------------------- AR hologramları

        delegate void HoloBuilder(Transform parent);

        static MissionPoint MakePoint(string id, string label, PointKind kind, Vector3 pos, float radius, ARRig rig, Transform arRoot, HoloBuilder build)
        {
            var go = new GameObject("Point_" + id);
            go.transform.position = new Vector3(pos.x, 0f, pos.z);

            var vis = new GameObject("AR_" + id).transform;
            vis.SetParent(arRoot, false);
            build(vis);
            var tint = vis.gameObject.AddComponent<HoloTint>();

            var mirror = go.AddComponent<ARMirror>();
            SetRef(mirror, "rig", rig);
            SetRef(mirror, "visual", vis);

            var mp = go.AddComponent<MissionPoint>();
            mp.Label = label;
            mp.Kind = kind;
            mp.Radius = radius;
            SetRef(mp, "holo", tint);
            return mp;
        }

        static Threat MakeThreat(string id, Vector3 pos, Transform robot, MockRobotLink link, ARRig rig, Transform arRoot)
        {
            var go = new GameObject("Threat_" + id);
            go.transform.position = pos;
            var vis = new GameObject("AR_Threat" + id).transform;
            vis.SetParent(arRoot, false);
            HoloThreat(vis);

            var mirror = go.AddComponent<ARMirror>();
            SetRef(mirror, "rig", rig);
            SetRef(mirror, "visual", vis);

            var th = go.AddComponent<Threat>();
            SetRef(th, "robot", robot);
            SetRef(th, "link", link);
            return th;
        }

        static void HoloTower(Transform p)
        {
            var mat = UnlitMat("HoloLine", C("#5FE3FF") * 1.3f);
            Ring(p, "RingBase", 0.15f, 0.004f, 40, 0.006f, mat);
            Ring(p, "RingMid", 0.11f, 0.17f, 40, 0.005f, mat);
            Ring(p, "RingTop", 0.07f, 0.34f, 40, 0.005f, mat);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2f;
                Line(p, "Strut", new[]
                {
                    new Vector3(Mathf.Cos(a) * 0.15f, 0.004f, Mathf.Sin(a) * 0.15f),
                    new Vector3(Mathf.Cos(a) * 0.07f, 0.34f, Mathf.Sin(a) * 0.07f),
                }, 0.003f, mat);
            }
            Line(p, "Beam", new[] { new Vector3(0f, 0.34f, 0f), new Vector3(0f, 1.0f, 0f) }, 0.004f, mat);
            var gem = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gem.name = "Gem";
            Object.DestroyImmediate(gem.GetComponent<Collider>());
            gem.transform.SetParent(p, false);
            gem.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            gem.transform.localScale = Vector3.one * 0.055f;
            gem.transform.localRotation = Quaternion.Euler(35f, 0f, 45f);
            gem.GetComponent<MeshRenderer>().sharedMaterial = mat;
            gem.AddComponent<Spin>();
        }

        static void HoloFuel(Transform p)
        {
            var mat = UnlitMat("HoloLine", C("#5FE3FF") * 1.3f);
            Ring(p, "HexBase", 0.17f, 0.004f, 6, 0.006f, mat);
            Ring(p, "HexTop", 0.13f, 0.22f, 6, 0.005f, mat);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                Line(p, "Strut", new[]
                {
                    new Vector3(Mathf.Cos(a) * 0.17f, 0.004f, Mathf.Sin(a) * 0.17f),
                    new Vector3(Mathf.Cos(a) * 0.13f, 0.22f, Mathf.Sin(a) * 0.13f),
                }, 0.003f, mat);
            }
            Line(p, "Beam", new[] { new Vector3(0f, 0.22f, 0f), new Vector3(0f, 0.8f, 0f) }, 0.004f, mat);
            Line(p, "PlusV", new[] { new Vector3(0f, 0.5f, 0f), new Vector3(0f, 0.62f, 0f) }, 0.016f, mat);
            Line(p, "PlusH", new[] { new Vector3(-0.06f, 0.56f, 0f), new Vector3(0.06f, 0.56f, 0f) }, 0.016f, mat);
        }

        static void HoloBase(Transform p)
        {
            var mat = UnlitMat("HoloLine", C("#5FE3FF") * 1.3f);
            Ring(p, "Outer", 0.26f, 0.004f, 48, 0.003f, mat);
            Ring(p, "Inner", 0.2f, 0.004f, 48, 0.002f, mat);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2f + Mathf.PI / 4f;
                Line(p, "Tick", new[]
                {
                    new Vector3(Mathf.Cos(a) * 0.2f, 0.004f, Mathf.Sin(a) * 0.2f),
                    new Vector3(Mathf.Cos(a) * 0.3f, 0.004f, Mathf.Sin(a) * 0.3f),
                }, 0.003f, mat);
            }
        }

        static void HoloThreat(Transform p)
        {
            var red = UnlitMat("HoloThreat", C("#FF5C5C") * 1.6f);
            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "Core";
            Object.DestroyImmediate(core.GetComponent<Collider>());
            core.transform.SetParent(p, false);
            core.transform.localScale = Vector3.one * 0.075f;
            core.GetComponent<MeshRenderer>().sharedMaterial = red;

            for (int i = 0; i < 2; i++)
            {
                var pivot = new GameObject("Orbit" + i).transform;
                pivot.SetParent(p, false);
                pivot.localRotation = Quaternion.Euler(i * 55f, 0f, i * 30f);
                var ring = Ring(pivot, "Ring", 0.1f + i * 0.025f, 0f, 32, 0.005f, red);
                var spin = pivot.gameObject.AddComponent<Spin>();
                var so = new SerializedObject(spin);
                so.FindProperty("degreesPerSecond").vector3Value = i == 0 ? new Vector3(0f, 140f, 0f) : new Vector3(60f, -110f, 0f);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static LineRenderer Ring(Transform parent, string name, float radius, float y, int segments, float width, Material mat)
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

        static LineRenderer Line(Transform parent, string name, Vector3[] points, float width, Material mat)
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
            bloom.intensity.Override(0.55f);
            bloom.threshold.Override(1.0f);
            bloom.scatter.Override(0.65f);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.28f);
            vignette.smoothness.Override(0.5f);

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.1f);
            color.saturation.Override(4f);
            color.contrast.Override(8f);

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

        static Material Mat(string name, Color color, Texture2D map = null, float tilingX = 1f, float tilingY = 1f,
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

        static Material UnlitMat(string name, Color color)
        {
            var mat = LoadOrCreateMaterial(name, "Universal Render Pipeline/Unlit");
            mat.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material LoadOrCreateMaterial(string name, string shader)
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

        static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetRefs(Object target, string field, Object[] values)
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
        /// ARAKI_VIEW = pano | top | equirect (varsayılan pano). ARAKI_POSE = "x,z,yaw" robotu yerleştirir.
        /// ARAKI_LOOK = bakış yaw'ı (derece).
        /// </summary>
        public static void Screenshot()
        {
            EditorSceneManager.OpenScene(ScenePath);
            string view = System.Environment.GetEnvironmentVariable("ARAKI_VIEW") ?? "pano";
            string pose = System.Environment.GetEnvironmentVariable("ARAKI_POSE");
            string look = System.Environment.GetEnvironmentVariable("ARAKI_LOOK");

            var robot = GameObject.Find("Robot");
            if (!string.IsNullOrEmpty(pose))
            {
                var v = pose.Split(',');
                robot.transform.position = new Vector3(float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture), 0f, float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture));
                robot.transform.rotation = Quaternion.Euler(0f, float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture), 0f);
            }

            robot.GetComponent<MockRobotLink>().Initialize();
            var cam360 = Object.FindFirstObjectByType<Sim360Camera>();
            var viewer = Object.FindFirstObjectByType<PanoramaViewer>();
            var hud = Object.FindFirstObjectByType<SpaceHud>();
            var mission = Object.FindFirstObjectByType<MissionController>();
            var topCam = GameObject.Find("TopCam").GetComponent<Camera>();
            var cam = view == "top" ? topCam : viewer.ViewCamera;
            viewer.ViewCamera.enabled = view != "top";
            topCam.enabled = view == "top";

            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            viewer.Bind();
            if (!string.IsNullOrEmpty(look)) viewer.SetLook(float.Parse(look, System.Globalization.CultureInfo.InvariantCulture), viewer.DefaultPitch);
            else viewer.SetLook(0f, viewer.DefaultPitch);

            // Shader varyantları arka planda derlenir; hazır olana kadar turuncu yer tutucu çizilir.
            for (int i = 0; i < 80; i++)
            {
                RenderFrame(cam, cam360, viewer, hud, mission, view);
                System.Threading.Thread.Sleep(250);
                if (!ShaderUtil.anythingCompiling) break;
            }
            RenderFrame(cam, cam360, viewer, hud, mission, view);

            string outPath = System.Environment.GetEnvironmentVariable("ARAKI_SHOT") ?? "shot.png";
            if (view == "equirect") SavePng(cam360.Equirect, outPath);
            else SavePng(rt, outPath);
            cam.targetTexture = null;
            Debug.Log("[SceneBuilder] ekran görüntüsü: " + outPath);
        }

        static void RenderFrame(Camera cam, Sim360Camera cam360, PanoramaViewer viewer, SpaceHud hud, MissionController mission, string view)
        {
            cam360.Capture();
            foreach (var m in Object.FindObjectsByType<ARMirror>(FindObjectsSortMode.None)) m.Sync();
            viewer.Apply();
            if (view != "top")
            {
                hud.Build();
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
