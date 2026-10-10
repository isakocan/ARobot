using System.IO;
using ARaki.AR;
using ARaki.Audio;
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

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.fog = false;

            BuildLighting();
            var prints = RoomBuilder.Build();
            var robot = BuildRover();
            var link = robot.GetComponent<MockRobotLink>();

            var cam360 = BuildCam360(robot.transform);
            var rigGo = BuildViewerRig(robot.transform, cam360, out var viewer, out var viewCam);
            var rig = rigGo.GetComponent<ARRig>();

            var arRoot = new GameObject("AR Objects").transform;
            arRoot.SetParent(rigGo.transform, false);

            var points = new[]
            {
                MakeStationPoint("ORBİT-9 · TARAMA", prints.Station, rig, arRoot),
                MakePoint("Fuel", "ENERJİ HÜCRESİ", PointKind.Fuel, prints.Fuel, 0.55f, rig, arRoot, HoloFuel),
                MakePoint("Base", "ANA ÜS", PointKind.Base, prints.BasePad, 0.45f, rig, arRoot, HoloBase),
            };
            var threats = new[]
            {
                MakeThreat("A", new Vector3(1.0f, 0.28f, 0.6f), robot.transform, link, rig, arRoot),
                MakeThreat("B", new Vector3(-0.8f, 0.28f, 1.2f), robot.transform, link, rig, arRoot),
                MakeThreat("C", new Vector3(1.8f, 0.28f, -0.4f), robot.transform, link, rig, arRoot),
                MakeThreat("D", new Vector3(-1.8f, 0.28f, -0.6f), robot.transform, link, rig, arRoot),
            };

            var missionGo = new GameObject("Mission");
            var mission = missionGo.AddComponent<MissionController>();
            SetRef(mission, "robot", robot.transform);
            SetRef(mission, "link", link);
            SetRefs(mission, "points", points);
            SetRefs(mission, "threats", threats);

            var beamGo = new GameObject("ScanBeam");
            beamGo.transform.SetParent(arRoot, false);
            var beamLine = beamGo.AddComponent<LineRenderer>();
            beamLine.sharedMaterial = AdditiveMat("ScanBeam", C("#FFB454") * 1.8f);
            beamLine.widthMultiplier = 0.008f;
            beamLine.numCapVertices = 2;
            beamLine.alignment = LineAlignment.View;
            beamLine.shadowCastingMode = ShadowCastingMode.Off;
            var beam = beamGo.AddComponent<ScanBeam>();
            SetRef(beam, "rig", rig);
            SetRef(beam, "mission", mission);
            SetRef(beam, "robot", robot.transform);

            var shelters = new[] { MakeShelter(0, rig, arRoot), MakeShelter(1, rig, arRoot) };
            var hazard = new GameObject("Hazard").AddComponent<HazardController>();
            SetRef(hazard, "robot", robot.transform);
            SetRef(hazard, "link", link);
            SetRef(hazard, "viewer", viewer);
            SetRef(hazard, "stormVolume", BuildStormVolume());
            SetRefs(hazard, "shelterProxies", shelters);

            var generator = new GameObject("MissionGenerator").AddComponent<MissionGenerator>();
            var ignore = new System.Collections.Generic.List<Object> { prints.Station, prints.Fuel, prints.BasePad };
            SetRefs(generator, "ignoreRoots", ignore.ToArray());

            var session = new GameObject("GameSession").AddComponent<GameSession>();
            SetRef(session, "robot", link);
            SetRef(session, "driveInput", robot.GetComponent<KeyboardDriveInput>());
            SetRef(session, "mission", mission);
            SetRef(session, "generator", generator);
            SetRef(session, "viewer", viewer);
            SetRef(session, "hazard", hazard);

            var sfx = new GameObject("Sfx").AddComponent<SfxPlayer>();
            SetRef(sfx, "link", link);
            SetRef(sfx, "mission", mission);
            SetRef(sfx, "hazard", hazard);

            var hudGo = new GameObject("SpaceHud");
            var hud = hudGo.AddComponent<SpaceHud>();
            SetRef(hud, "robot", link);
            SetRef(hud, "mission", mission);
            SetRef(hud, "viewer", viewer);
            SetRef(hud, "rig", rig);
            SetRef(hud, "session", session);
            SetRef(hud, "hazard", hazard);
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
            // Ortam ışığı ve yansımalar için yumuşak bir akşam gökyüzü
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

            // Akşam güneşi pencereden içeri girer (doğu duvarından)
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = C("#FFD6A8");
            sun.intensity = 1.9f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.9f;
            sun.shadowNormalBias = 0.6f;
            sun.transform.rotation = Quaternion.Euler(30f, 262f, 0f);

            // Tavan lambası: sıcak, yumuşak dolgu
            var lamp = new GameObject("RoomLamp").AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = C("#FFD6A0");
            lamp.intensity = 3.6f;
            lamp.range = 8f;
            lamp.shadows = LightShadows.None;
            lamp.transform.position = new Vector3(0f, 2.0f, 0.2f);

            // Pencere tarafından gelen soğuk gökyüzü ışığı (karşı yöne hafif mavi dolgu)
            var skyFill = new GameObject("SkyFill").AddComponent<Light>();
            skyFill.type = LightType.Directional;
            skyFill.color = C("#8FA5D0");
            skyFill.intensity = 0.35f;
            skyFill.shadows = LightShadows.None;
            skyFill.transform.rotation = Quaternion.Euler(50f, 82f, 0f);
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

        // ---------------------------------------------------------------- AR hologramları

        delegate void HoloBuilder(Transform parent);

        static MissionPoint MakePoint(string id, string label, PointKind kind, Transform physical, float radius, ARRig rig, Transform arRoot, HoloBuilder build)
        {
            var go = new GameObject("Point_" + id);
            go.transform.position = new Vector3(physical.position.x, 0f, physical.position.z);

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
            SetRef(mp, "burst", MakeBurst(vis));
            SetRef(mp, "physical", physical);
            return mp;
        }

        static HoloBurst MakeBurst(Transform parent)
        {
            var go = new GameObject("Burst");
            go.transform.SetParent(parent, false);
            var ring = Ring(go.transform, "Ring", 0.2f, 0.01f, 48, 0.008f, AdditiveMat("HoloBurst", C("#73FF9A") * 1.8f));
            var burst = go.AddComponent<HoloBurst>();
            SetRef(burst, "ring", ring);
            go.SetActive(false);
            return burst;
        }


        static void HoloFuel(Transform p)
        {
            var mat = HoloLineMat();
            Glow(p, "Glow", 0.6f, 0.003f);
            Ring(p, "HexBase", 0.17f, 0.004f, 6, 0.006f, mat);
            Ring(p, "HexTop", 0.13f, 0.22f, 6, 0.005f, mat);
            var sweep = Ring(p, "Sweep", 0.15f, 0.01f, 6, 0.003f, mat);
            var sw = sweep.gameObject.AddComponent<HoloSweep>();
            var so = new SerializedObject(sw);
            so.FindProperty("maxY").floatValue = 0.22f;
            so.ApplyModifiedPropertiesWithoutUndo();
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
            Motes(p, 0.1f, 0.1f, 12);
        }

        static void HoloBase(Transform p)
        {
            var mat = HoloLineMat();
            Glow(p, "Glow", 0.9f, 0.003f);
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

        // Yükselen toz parçacıkları: hologramın "canlı" durmasını sağlar.
        static void Motes(Transform parent, float radius, float startY, int rate)
        {
            var go = new GameObject("Motes");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, startY, 0f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = 2.4f;
            main.startSpeed = 0.1f;
            main.startSize = 0.013f;
            main.maxParticles = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var emission = ps.emission;
            emission.rateOverTime = rate;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 3f;
            shape.radius = radius;
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(gradient);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = AdditiveMat("HoloMote", C("#5FE3FF") * 1.4f, GlowTexture(), true);
            r.shadowCastingMode = ShadowCastingMode.Off;
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

            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.2f);
            grain.response.Override(0.8f);

            var aberration = profile.Add<ChromaticAberration>(true);
            aberration.intensity.Override(0.14f);

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
        /// ARAKI_VIEW = pano | top | equirect. ARAKI_STATE = menu | playing | pause | win | lose | upgrade.
        /// ARAKI_SEED, ARAKI_LEVEL görevi seçer. ARAKI_POSE = "x,z,yaw" robotu yerleştirir. ARAKI_LOOK = bakış yaw'ı.
        /// ARAKI_TICK = oyun mantığını ilerletilecek saniye. ARAKI_LED=1 LED'i yakar.
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

            var robot = GameObject.Find("Robot");
            var link = robot.GetComponent<MockRobotLink>();
            link.Initialize();
            var cam360 = Object.FindFirstObjectByType<Sim360Camera>();
            var viewer = Object.FindFirstObjectByType<PanoramaViewer>();
            var hud = Object.FindFirstObjectByType<SpaceHud>();
            var screens = Object.FindFirstObjectByType<ScreenUi>();
            var mission = Object.FindFirstObjectByType<MissionController>();
            var session = Object.FindFirstObjectByType<GameSession>();
            var topCam = GameObject.Find("TopCam").GetComponent<Camera>();
            var cam = view == "top" ? topCam : viewer.ViewCamera;
            viewer.ViewCamera.enabled = view != "top";
            topCam.enabled = view == "top";

            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            viewer.Bind();
            bool arOnly = view == "ar";   // yalnızca AR sahnesini dışarıdan göster (kürenin içinden değil)
            Physics.SyncTransforms();

            hud.Build();
            screens.Build();
            session.SetLevel(Env("ARAKI_LEVEL") != null ? int.Parse(Env("ARAKI_LEVEL"), inv) : 1);
            int seed = Env("ARAKI_SEED") != null ? int.Parse(Env("ARAKI_SEED"), inv) : 1234;
            if (state == "menu") session.ShowMenu();
            else session.StartMission(seed);

            if (!string.IsNullOrEmpty(pose))
            {
                var v = pose.Split(',');
                link.Teleport(new Vector3(float.Parse(v[0], inv), 0f, float.Parse(v[1], inv)), float.Parse(v[2], inv));
            }
            // ARAKI_FACE = "station:2.0" ya da "threat:1.5": robotu hedefe verilen uzaklıkta, ona bakar şekilde koyar
            string face = Env("ARAKI_FACE");
            if (!string.IsNullOrEmpty(face))
            {
                var parts = face.Split(':');
                float d = float.Parse(parts[1], inv);
                Vector3 target;
                if (parts[0] == "station") target = mission.Points[0].transform.position;
                else target = System.Linq.Enumerable.First(Object.FindObjectsByType<Threat>(FindObjectsSortMode.None)).transform.position;
                Vector3 dir = link.transform.position - target;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.01f) dir = Vector3.back;
                dir.Normalize();
                Vector3 pos = target + dir * d;
                link.Teleport(new Vector3(pos.x, 0f, pos.z), Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg);
            }
            if (Env("ARAKI_LED") == "1") link.SetLed(true);

            string tick = Env("ARAKI_TICK");
            if (!string.IsNullOrEmpty(tick))
            {
                float seconds = float.Parse(tick, inv);
                for (float t = 0f; t < seconds; t += 0.1f)
                {
                    mission.Tick(0.1f);
                    foreach (var th in Object.FindObjectsByType<Threat>(FindObjectsSortMode.None)) th.Tick(0.1f);
                }
            }

            string hazardEnv = Env("ARAKI_HAZARD");
            var hazardObj = Object.FindFirstObjectByType<HazardController>();
            if (!string.IsNullOrEmpty(hazardEnv))
            {
                hazardObj.ForcePhase(hazardEnv == "storm" ? HazardPhase.Storm : HazardPhase.Warning);
                for (int i = 0; i < 12; i++) hazardObj.Tick(0.2f);
            }

            switch (state)
            {
                case "win": session.ForceWin(); break;
                case "lose": session.ForceLose(); break;
                case "upgrade": session.ForceWin(); session.ShowUpgrades(); break;
                case "pause": break;
            }
            screens.Show(state == "pause" ? GameState.Paused : session.State);
            if (view == "ar") hud.SetVisible(false);

            foreach (var mp in Object.FindObjectsByType<MissionPoint>(FindObjectsSortMode.None)) mp.RefreshVisual();
            foreach (var tv in Object.FindObjectsByType<ThreatVisual>(FindObjectsSortMode.None)) tv.Refresh();
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) ps.Simulate(2.2f, true, true);
            foreach (var led in Object.FindObjectsByType<RobotLed>(FindObjectsSortMode.None)) led.Tick(1f);
            Object.FindFirstObjectByType<ScanBeam>().Tick();
            if (!string.IsNullOrEmpty(look)) viewer.SetLook(float.Parse(look, inv), viewer.DefaultPitch);
            else viewer.SetLook(state == "menu" ? 35f : 0f, viewer.DefaultPitch * (state == "menu" ? 0.6f : 1f));

            if (arOnly)
            {
                var rigObj = Object.FindFirstObjectByType<ARRig>();
                foreach (var r in rigObj.GetComponentsInChildren<MeshRenderer>())
                    if (r.name == "PanoramaSphere") r.enabled = false;
                viewer.ViewCamera.backgroundColor = new Color(0.04f, 0.05f, 0.07f);
                viewer.ViewCamera.clearFlags = CameraClearFlags.SolidColor;
                string[] a = (Env("ARAKI_AR") ?? "station").Split(':');
                Vector3 focus = a[0] == "threat"
                    ? System.Linq.Enumerable.First(Object.FindObjectsByType<Threat>(FindObjectsSortMode.None)).transform.position
                    : mission.Points[0].transform.position;
                float dist = a.Length > 1 ? float.Parse(a[1], inv) : 2.4f;
                float height = a.Length > 2 ? float.Parse(a[2], inv) : 0.8f;
                Vector3 focusRig = rigObj.ToRig(focus + Vector3.up * (a[0] == "threat" ? 0f : 0.7f));
                viewer.ViewCamera.transform.position = focusRig + new Vector3(0f, height - 0.3f, -dist);
                viewer.ViewCamera.transform.LookAt(focusRig);
            }

            // Shader varyantları arka planda derlenir; hazır olana kadar turuncu yer tutucu çizilir.
            for (int i = 0; i < 80; i++)
            {
                RenderFrame(cam, cam360, viewer, hud, view);
                System.Threading.Thread.Sleep(250);
                if (!ShaderUtil.anythingCompiling) break;
            }
            RenderFrame(cam, cam360, viewer, hud, view);

            string outPath = Env("ARAKI_SHOT") ?? "shot.png";
            if (view == "equirect") SavePng(cam360.Equirect, outPath);
            else SavePng(rt, outPath);
            cam.targetTexture = null;
            Time.timeScale = 1f;
            Debug.Log("[SceneBuilder] ekran görüntüsü: " + outPath);
        }

        static void RenderFrame(Camera cam, Sim360Camera cam360, PanoramaViewer viewer, SpaceHud hud, string view)
        {
            cam360.Capture();
            // Editörde OnEnable/OnDisable çalışmaz: pasif noktaların görselini elle gizle
            foreach (var m in Object.FindObjectsByType<ARMirror>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                bool live = m.gameObject.activeInHierarchy;
                if (m.Visual != null) m.Visual.gameObject.SetActive(live);
                if (live) m.Sync();
            }
            if (view != "ar") viewer.Apply();
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
