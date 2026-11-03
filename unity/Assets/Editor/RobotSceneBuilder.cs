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
            ConfigureH264Plugins();
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
            var stream = robotGo.AddComponent<RobotVideo>();
            SceneBuilder.SetRef(stream, "yuvShader", Shader.Find("ARaki/YuvToRgb"));
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
        /// Robotsuz H.264 denemesi: tools/robotsuz_deneme/kayit/goruntu.h264 kaydını OpenH264 ile çözer, son kareyi
        /// YuvToRgb ve DualFisheye'dan geçirip ARAKI_SHOT yoluna PNG olarak kaydeder.
        /// </summary>
        public static void VerifyH264()
        {
            string file = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "tools", "robotsuz_deneme", "kayit", "goruntu.h264"));
            string shot = System.Environment.GetEnvironmentVariable("ARAKI_SHOT") ?? "h264.png";
            byte[] data = File.ReadAllBytes(file);
            var starts = new List<int>();
            for (int i = 0; i + 4 < data.Length; i++)
                if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 0 && data[i + 3] == 1 && (data[i + 4] & 0x1F) == 9) starts.Add(i);
            starts.Add(data.Length);
            byte[] y = null, u = null, v = null;
            int frames = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using (var dec = new H264Decoder())
            {
                for (int k = 0; k + 1 < starts.Count; k++)
                    if (dec.Decode(data, starts[k], starts[k + 1] - starts[k], ref y, ref u, ref v)) frames++;
                Debug.Log($"[H264] {starts.Count - 1} erişim birimi, {frames} kare çözüldü, {dec.Width}x{dec.Height} (adım {dec.StrideY}/{dec.StrideUV}), " +
                          $"kare başına {sw.Elapsed.TotalMilliseconds / Mathf.Max(1, frames):0.0} ms");
                EditorSceneManager.OpenScene(ScenePath);
                Texture2D Plane(int w, int h, byte[] bytes)
                {
                    var t = new Texture2D(w, h, TextureFormat.R8, false, true);
                    t.LoadRawTextureData(bytes);
                    t.Apply();
                    return t;
                }
                var mat = new Material(Shader.Find("ARaki/YuvToRgb"));
                mat.SetTexture("_Y", Plane(dec.StrideY, dec.Height, y));
                mat.SetTexture("_U", Plane(dec.StrideUV, dec.Height / 2, u));
                mat.SetTexture("_V", Plane(dec.StrideUV, dec.Height / 2, v));
                mat.SetVector("_Scale", new Vector4(dec.Width / (float)dec.StrideY, 1f, dec.Width / 2f / dec.StrideUV, 1f));
                var rgb = new RenderTexture(dec.Width, dec.Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(null, rgb, mat);

                var viewer = Object.FindFirstObjectByType<PanoramaViewer>();
                var mount = Object.FindFirstObjectByType<RobotCameraMount>();
                var sphere = GameObject.Find("Panorama").GetComponent<MeshRenderer>();
                viewer.Bind();
                mount.Apply();
                sphere.sharedMaterial.SetTexture("_MainTex", rgb);
                viewer.SetLook(0f, 4f);
                var cam = viewer.ViewCamera;
                var rt = new RenderTexture(1280, 720, 24);
                cam.targetTexture = rt;
                for (int i = 0; i < 40; i++) { cam.Render(); System.Threading.Thread.Sleep(200); if (!ShaderUtil.anythingCompiling) break; }
                cam.Render();
                RenderTexture.active = rt;
                var outTex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                outTex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                outTex.Apply();
                File.WriteAllBytes(shot, outTex.EncodeToPNG());
                RenderTexture.active = null;
                cam.targetTexture = null;
                Debug.Log("[H264] kaydedildi: " + shot);
            }
        }

        /// <summary>OpenH264 kütüphaneleri: Windows (Editör + 64 bit oyun) ve Android ARM64.</summary>
        static void ConfigureH264Plugins()
        {
            void Set(string path, System.Action<PluginImporter> configure)
            {
                var imp = AssetImporter.GetAtPath(path) as PluginImporter;
                if (imp == null) { Debug.LogWarning("[RobotSceneBuilder] eklenti yok: " + path); return; }
                imp.SetCompatibleWithAnyPlatform(false);
                configure(imp);
                imp.SaveAndReimport();
            }
            Set("Assets/Plugins/x86_64/openh264.dll", imp =>
            {
                imp.SetCompatibleWithEditor(true);
                imp.SetEditorData("CPU", "x86_64");
                imp.SetEditorData("OS", "Windows");
                imp.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
            });
            Set("Assets/Plugins/Android/arm64-v8a/libopenh264.so", imp =>
            {
                imp.SetCompatibleWithPlatform(BuildTarget.Android, true);
                imp.SetPlatformData(BuildTarget.Android, "CPU", "ARM64");
            });
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
