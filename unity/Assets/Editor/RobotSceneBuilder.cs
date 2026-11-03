using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ARaki.Drive;
using ARaki.Robot;
using ARaki.Video;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ARaki.EditorTools
{
    /// <summary>
    /// Gerçek robot sahnesi (RealRobot): robotu telefondan / PC'den sürme uygulaması. 360° görüntü içinden
    /// bakılan küreye kaplanır; üstünde araç içi kokpit. Robot yoksa "robotsuz demo" kayıtlı görüntü ve
    /// buzzer'ın kopyasıyla çalışır. Oyun (DriveSim) simülasyonda kalır.
    /// Kurarken Arduino kodundan sayılar ve şarkılar Resources/RobotFirmware.txt'e yazılır.
    /// </summary>
    public static class RobotSceneBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/RealRobot.unity";
        const string FirmwareOut = "Assets/_Project/Resources/RobotFirmware.txt";
        static readonly string ArduinoDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "robot", "arduino", "robot_kontrol"));

        [MenuItem("ARaki/Kur/Gerçek robot sahnesi")]
        public static void Build()
        {
            WriteFirmwareData();
            // Robotla düz HTTP (kamera ayarı) ve ağ erişimi; telefonda gerekli
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.Android.forceInternetPermission = true;
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
            cam.fieldOfView = 75f;
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
            so.FindProperty("defaultPitch").floatValue = 4f;   // hafif aşağı: kaput hizası
            so.ApplyModifiedPropertiesWithoutUndo();

            var robotGo = new GameObject("Robot", typeof(AudioSource));
            var buzzer = robotGo.AddComponent<BuzzerSynth>();
            var link = robotGo.AddComponent<RealRobotLink>();
            SceneBuilder.SetRef(link, "buzzer", buzzer);
            var stream = robotGo.AddComponent<MjpegStream>();
            SceneBuilder.SetRef(stream, "robot", link);
            SceneBuilder.SetRef(stream, "target", sphereRenderer);
            var mount = robotGo.AddComponent<RobotCameraMount>();
            SceneBuilder.SetRef(mount, "robot", link);
            SceneBuilder.SetRef(mount, "sphere", sphereRenderer);

            var driveGo = new GameObject("Sürüş");
            var recorder = driveGo.AddComponent<ScreenRecorder>();
            var drive = driveGo.AddComponent<DriveController>();
            SceneBuilder.SetRef(drive, "link", link);
            SceneBuilder.SetRef(drive, "viewer", viewer);
            SceneBuilder.SetRef(drive, "recorder", recorder);
            var cockpit = driveGo.AddComponent<DriveCockpit>();
            SceneBuilder.SetRef(cockpit, "drive", drive);
            SceneBuilder.SetRef(cockpit, "video", stream);
            SceneBuilder.SetRef(cockpit, "font", AssetDatabase.LoadAssetAtPath<Font>("Assets/_Project/UI/Fonts/Rajdhani-Medium.ttf"));
            SceneBuilder.SetRef(cockpit, "mono", AssetDatabase.LoadAssetAtPath<Font>("Assets/_Project/UI/Fonts/ShareTechMono-Regular.ttf"));

            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(s => s.path == ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[RobotSceneBuilder] sahne hazır: " + ScenePath);
        }

        /// <summary>
        /// robot_kontrol.ino ve sarkilar*.h'den sabitleri ve MELODILER listesini (satır sonu "// grup|ad"
        /// yorumlarıyla) okuyup Resources/RobotFirmware.txt'e yazar.
        /// </summary>
        [MenuItem("ARaki/Kur/Arduino şarkılarını oku")]
        public static void WriteFirmwareData()
        {
            string ino = File.ReadAllText(Path.Combine(ArduinoDir, "robot_kontrol.ino"));
            var text = new StringBuilder(ino);
            foreach (var h in Directory.GetFiles(ArduinoDir, "*.h")) text.Append('\n').Append(File.ReadAllText(h));
            string all = text.ToString();

            // MELODILER satırları: "  M_AD,   // grup|ad"
            var table = Regex.Match(ino, @"MELODILER\[\]\s*=\s*\{(.*?)\};", RegexOptions.Singleline).Groups[1].Value;
            var entries = Regex.Matches(table, @"(M_\w+)\s*,?\s*(?://\s*([^\n]*))?").Cast<Match>()
                .Select(m => (name: m.Groups[1].Value, label: m.Groups[2].Value.Trim())).ToList();

            string code = Regex.Replace(all, @"//[^\n]*", "");
            var constants = new Dictionary<string, int>();
            foreach (Match m in Regex.Matches(code, @"\b([A-Z][A-Z0-9_]+)\s*=\s*(\d+)\b"))
                constants[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
            var arrays = new Dictionary<string, List<int>>();
            foreach (Match m in Regex.Matches(code, @"const uint16_t (M_\w+)\[\] PROGMEM = \{(.*?)\};", RegexOptions.Singleline))
            {
                var values = new List<int>();
                foreach (var tok in m.Groups[2].Value.Split(','))
                {
                    string t = tok.Trim();
                    if (t.Length == 0) continue;
                    values.Add(constants.TryGetValue(t, out int v) ? v : int.Parse(t));
                }
                arrays[m.Groups[1].Value] = values;
            }

            var o = new StringBuilder("# Arduino kodundan üretildi (RobotSceneBuilder.WriteFirmwareData); elle düzenleme.\n");
            foreach (var kv in constants.OrderBy(k => k.Key)) o.Append("SABIT ").Append(kv.Key).Append(' ').Append(kv.Value).Append('\n');
            foreach (var (name, label) in entries)
            {
                if (!arrays.TryGetValue(name, out var notes)) { Debug.LogWarning("[RobotSceneBuilder] melodi yok: " + name); continue; }
                o.Append("MELODI ").Append(string.IsNullOrEmpty(label) ? "|" + name : label).Append('\n');
                o.Append("NOTA ").Append(string.Join(" ", notes)).Append('\n');
            }
            Directory.CreateDirectory(Path.GetDirectoryName(FirmwareOut)!);
            File.WriteAllText(FirmwareOut, o.ToString());
            AssetDatabase.ImportAsset(FirmwareOut);
            Debug.Log($"[RobotSceneBuilder] Arduino verisi: {constants.Count} sabit, {entries.Count} melodi");
        }
    }
}
