using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using ARaki.Dev;
using ARaki.Net;
using ARaki.Robot;
using ARaki.Video;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ARaki.EditorTools
{
    /// <summary>
    /// Gerçek robot sahnesi: telefondaki/PC'deki Unity uygulaması robotun köprüsüne bağlanır, 360° görüntüyü
    /// içinden bakılan küreye kaplar ve robotu sürer. Oyun (DriveSim) simülasyonda kalır; bu sahne
    /// gerçek robotla uçtan uca hattın (komut + görüntü + kalibrasyon) Unity'de çalıştığını gösterir.
    /// </summary>
    public static class RobotSceneBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/RealRobot.unity";

        [MenuItem("ARaki/Kur/Gerçek robot sahnesi")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.fog = false;
            RenderSettings.skybox = null;

            var mat = SceneBuilder.LoadOrCreateMaterial("Robot360", "ARaki/DualFisheye");

            var viewerGo = new GameObject("Viewer");
            var sphere = new GameObject("Panorama", typeof(MeshFilter), typeof(MeshRenderer));
            sphere.transform.SetParent(viewerGo.transform, false);
            var sphereRenderer = sphere.GetComponent<MeshRenderer>();
            sphereRenderer.sharedMaterial = mat;
            sphereRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sphereRenderer.receiveShadows = false;

            var cam = new GameObject("ViewCam", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            cam.transform.SetParent(viewerGo.transform, false);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.fieldOfView = 80f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.tag = "MainCamera";
            var camData = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = false;
            camData.renderShadows = false;

            var viewer = viewerGo.AddComponent<PanoramaViewer>();
            SceneBuilder.SetRef(viewer, "viewCamera", cam);
            SceneBuilder.SetRef(viewer, "sphere", sphereRenderer);
            var so = new SerializedObject(viewer);
            so.FindProperty("defaultPitch").floatValue = 0f;
            so.ApplyModifiedPropertiesWithoutUndo();

            var robotGo = new GameObject("Robot");
            var link = robotGo.AddComponent<RealRobotLink>();
            robotGo.AddComponent<RealDriveInput>();
            var stream = robotGo.AddComponent<MjpegStream>();
            SceneBuilder.SetRef(stream, "robot", link);
            SceneBuilder.SetRef(stream, "target", sphereRenderer);
            var mount = robotGo.AddComponent<RobotCameraMount>();
            SceneBuilder.SetRef(mount, "robot", link);
            SceneBuilder.SetRef(mount, "sphere", sphereRenderer);
            var overlay = robotGo.AddComponent<RealRobotOverlay>();
            SceneBuilder.SetRef(overlay, "robot", link);
            SceneBuilder.SetRef(overlay, "video", stream);

            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(s => s.path == ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[RobotSceneBuilder] sahne hazır: " + ScenePath);
        }

        /// <summary>
        /// Komut satırından deneme (motorları oynatmaz): ARAKI_ROBOT adresindeki köprüye bağlanır, durum mesajı ve
        /// ping bekler, bir kare alıp sahnedeki küreyle çizer, ARAKI_SHOT yoluna PNG kaydeder.
        /// ARAKI_LOOK = bakış yaw'ı (derece).
        /// </summary>
        public static void Verify()
        {
            string host = System.Environment.GetEnvironmentVariable("ARAKI_ROBOT") ?? "jetson.local";
            string shot = System.Environment.GetEnvironmentVariable("ARAKI_SHOT") ?? "robot360.png";
            float.TryParse(System.Environment.GetEnvironmentVariable("ARAKI_LOOK") ?? "0", out float look);

            using (var bridge = new RobotBridge(host))
            {
                double start = RobotBridge.Now();
                string status = null;
                while (RobotBridge.Now() - start < 8000 && (status == null || bridge.RoundTripMs < 0))
                {
                    Thread.Sleep(100);
                    while (bridge.Incoming.TryDequeue(out var m)) status = m;
                }
                Debug.Log($"[Verify] bağlı={bridge.Connected} ping={bridge.RoundTripMs:0} ms hata='{bridge.LastError}'");
                Debug.Log("[Verify] durum: " + status);
                if (status != null)
                {
                    var s = JsonUtility.FromJson<BridgeStatus>(status);
                    Debug.Log($"[Verify] çözülen: arduino={s.arduino} kamera={s.kamera} sinyal={s.sinyal} motor={(s.motor != null ? s.motor.sol + "/" + s.motor.sag : "yok")}");
                }
            }

            byte[] jpeg;
            using (var web = new WebClient()) jpeg = web.DownloadData("http://" + host + ":" + RobotBridge.Port + "/kare.jpg");
            // Sahne açılışı kullanılmayan nesneleri boşaltır; doku ondan sonra yaratılmalı
            EditorSceneManager.OpenScene(ScenePath);
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            tex.LoadImage(jpeg);
            Debug.Log($"[Verify] kare {tex.width}x{tex.height}, {jpeg.Length / 1024} KB");
            var viewer = Object.FindFirstObjectByType<PanoramaViewer>();
            var mount = Object.FindFirstObjectByType<RobotCameraMount>();
            var sphere = GameObject.Find("Panorama").GetComponent<MeshRenderer>();
            viewer.Bind();
            mount.Apply();
            sphere.sharedMaterial.SetTexture("_MainTex", tex);
            viewer.SetLook(look, 0f);
            File.WriteAllBytes(Path.ChangeExtension(shot, ".jpg"), jpeg);
            var mesh = sphere.GetComponent<MeshFilter>().sharedMesh;
            Debug.Log($"[Verify] küre {(mesh != null ? mesh.vertexCount : 0)} köşe, gölgelendirici {sphere.sharedMaterial.shader.name} " +
                      $"destek={sphere.sharedMaterial.shader.isSupported} boru={UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline?.name} " +
                      $"takılış={sphere.sharedMaterial.GetMatrix("_Mount").GetRow(0)}");

            var cam = viewer.ViewCamera;
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;
            // Gölgelendirici arka planda derlenir; hazır olana kadar yer tutucu çizilir
            for (int i = 0; i < 80; i++)
            {
                cam.Render();
                Thread.Sleep(250);
                if (!ShaderUtil.anythingCompiling) break;
            }
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var outTex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            outTex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            outTex.Apply();
            File.WriteAllBytes(shot, outTex.EncodeToPNG());
            RenderTexture.active = prev;
            cam.targetTexture = null;
            Debug.Log("[Verify] kaydedildi: " + shot);
        }
    }
}
