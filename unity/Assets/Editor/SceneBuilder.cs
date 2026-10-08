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
    /// <summary>Sürüş simülasyonu sahnesini ve URP ayarını koddan üretir; tekrar çalıştırılabilir.</summary>
    public static class SceneBuilder
    {
        const string Root = "Assets/_Project";
        const string ScenePath = Root + "/Scenes/DriveSim.unity";

        static readonly Color Floor = new Color32(0x0E, 0x14, 0x1B, 255);
        static readonly Color GridLine = new Color32(0x1C, 0x29, 0x35, 255);
        static readonly Color Body = new Color32(0xE6, 0xEC, 0xF1, 255);
        static readonly Color Dark = new Color32(0x3A, 0x44, 0x50, 255);
        static readonly Color Accent = new Color32(0xFF, 0x5A, 0x1F, 255);

        [MenuItem("ARaki/Kur/Sürüş sahnesi")]
        public static void BuildDriveSim()
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            SetupPipeline();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Işık
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.5f, 0.56f);

            // Zemin (1 m'lik ızgara)
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(10f, 1f, 10f); // 100 m x 100 m
            var floorMat = MakeMaterial("Floor", Floor, GridTexture(), 100f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

            // Robot
            var robot = new GameObject("Robot");
            var link = robot.AddComponent<MockRobotLink>();
            robot.AddComponent<KeyboardDriveInput>();
            BuildRobotVisual(robot.transform);

            // Kamera
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Floor;
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.05f;
            camGo.AddComponent<UniversalAdditionalCameraData>();
            camGo.transform.position = new Vector3(0f, 1.5f, 0f);
            camGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var follow = camGo.AddComponent<FollowCamera>();
            SetRef(follow, "target", robot.transform);

            // Geliştirici paneli
            var overlay = new GameObject("DebugOverlay").AddComponent<ARaki.Dev.DebugOverlay>();
            SetRef(overlay, "robot", link);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[SceneBuilder] sahne hazır: " + ScenePath);
        }

        // Robot ~20 cm: gövde, iki tekerlek, önü gösteren turuncu şerit, üstte 360 kamera kubbesi.
        static void BuildRobotVisual(Transform parent)
        {
            var bodyMat = MakeMaterial("RobotBody", Body);
            var darkMat = MakeMaterial("RobotDark", Dark);
            var accentMat = MakeMaterial("RobotAccent", Accent);

            Part(PrimitiveType.Cube, "Body", parent, new Vector3(0f, 0.05f, 0f), new Vector3(0.18f, 0.05f, 0.22f), Quaternion.identity, bodyMat);
            Part(PrimitiveType.Cube, "Front", parent, new Vector3(0f, 0.052f, 0.105f), new Vector3(0.14f, 0.045f, 0.03f), Quaternion.identity, accentMat);
            foreach (float side in new[] { -1f, 1f })
                Part(PrimitiveType.Cylinder, side < 0 ? "WheelL" : "WheelR", parent,
                    new Vector3(0.105f * side, 0.04f, -0.01f), new Vector3(0.08f, 0.015f, 0.08f),
                    Quaternion.Euler(0f, 0f, 90f), darkMat);
            Part(PrimitiveType.Sphere, "CameraDome", parent, new Vector3(0f, 0.115f, 0f), new Vector3(0.05f, 0.05f, 0.05f), Quaternion.identity, darkMat);
            Part(PrimitiveType.Cylinder, "CameraMast", parent, new Vector3(0f, 0.09f, 0f), new Vector3(0.012f, 0.025f, 0.012f), Quaternion.identity, darkMat);
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
            GraphicsSettings.defaultRenderPipeline = rp;
            int original = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i);
                QualitySettings.renderPipeline = rp;
            }
            QualitySettings.SetQualityLevel(original);
        }

        static Texture2D GridTexture()
        {
            const string path = Root + "/Art/Grid.png";
            const int size = 256, line = 3;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = (x < line || y < line) ? GridLine : Floor;
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

        static Material MakeMaterial(string name, Color color, Texture2D map = null, float tiling = 1f)
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
            mat.SetFloat("_Smoothness", 0.2f);
            mat.SetFloat("_EnvironmentReflections", 0f);
            mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            mat.SetFloat("_SpecularHighlights", 0f);
            mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            if (map != null)
            {
                mat.SetTexture("_BaseMap", map);
                mat.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
                mat.SetColor("_BaseColor", Color.white);
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

        /// <summary>Doğrulama için: sahnenin ana kamerasından PNG alır (ARAKI_SHOT yoluna). ARAKI_ISO=1 yan açı verir.</summary>
        public static void Screenshot()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var cam = Camera.main;
            if (System.Environment.GetEnvironmentVariable("ARAKI_ISO") == "1")
            {
                cam.transform.position = new Vector3(0.5f, 0.5f, -0.6f);
                cam.transform.LookAt(new Vector3(0f, 0.05f, 0f));
            }
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            // Shader varyantları arka planda derlenir; hazır olana kadar turuncu yer tutucu çizilir.
            for (int i = 0; i < 60; i++)
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
