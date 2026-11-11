using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ARaki.EditorTools
{
    /// <summary>
    /// Robot sürüş uygulamasının derlemeleri (yalnız RealRobot sahnesi). Oyun (DriveSim) ayrı derlenir.
    ///   Windows: build/RobotSurus/RobotSurus.exe (pencereli 1600×900, boyutu değişir; F11 tam ekran)
    ///   Android: build/RobotSurus.apk (IL2CPP, ARM64; OpenH264 ARM64 kütüphanesi bunu ister)
    ///   Mac: build/RobotSurus.app (Apple Silicon + Intel; Mac'te Unity Hub'dan "Mac Build Support" modülüyle derlenir)
    /// Komut satırı: Unity -batchmode -quit -projectPath unity -executeMethod ARaki.EditorTools.RobotBuilds.Windows
    /// </summary>
    public static class RobotBuilds
    {
        const string Scene = "Assets/_Project/Scenes/RealRobot.unity";
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "build"));

        [MenuItem("ARaki/Derle/Robot sürüş: Windows")]
        public static void Windows()
        {
            Build(BuildTarget.StandaloneWindows64, Path.Combine(Root, "RobotSurus", "RobotSurus.exe"), () =>
            {
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.defaultScreenWidth = 1600;
                PlayerSettings.defaultScreenHeight = 900;
                PlayerSettings.resizableWindow = true;
                PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            });
        }

        [MenuItem("ARaki/Derle/Robot sürüş: Mac")]
        public static void Mac()
        {
            Build(BuildTarget.StandaloneOSX, Path.Combine(Root, "RobotSurus.app"), () =>
            {
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.defaultScreenWidth = 1600;
                PlayerSettings.defaultScreenHeight = 900;
                PlayerSettings.resizableWindow = true;
                PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
                PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, "com.bitirme.robotsurus");
                // Mac modülü yalnız Mac'te kurulu olabilir: ayarlar adla (yansımayla) yapılır, Windows'ta derleme bozulmaz
                SetStatic(typeof(PlayerSettings).GetNestedType("macOS"), "microphoneUsageDescription", "Sürüş videosuna ses kaydetmek için");
                var osx = System.Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor.OSXStandalone.Extensions");
                var arch = System.Type.GetType("UnityEditor.Build.OSArchitecture, UnityEditor.CoreModule");
                if (osx != null && arch != null) SetStatic(osx, "architecture", System.Enum.Parse(arch, "x64ARM64"));
            });
        }

        [MenuItem("ARaki/Derle/Robot sürüş: Android")]
        public static void Android()
        {
            Build(BuildTarget.Android, Path.Combine(Root, "RobotSurus.apk"), () =>
            {
                PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.bitirme.robotsurus");
                PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
                PlayerSettings.Android.forceInternetPermission = true;
                PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
                EditorUserBuildSettings.buildAppBundle = false;
            });
        }

        static void SetStatic(System.Type type, string name, object value)
        {
            var p = type?.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (p != null && p.CanWrite) p.SetValue(null, value);
        }

        static void Build(BuildTarget target, string output, System.Action configure)
        {
            RobotSceneBuilder.WriteFirmwareData();
            string productName = PlayerSettings.productName;
            PlayerSettings.productName = "Robot Sürüş";
            try
            {
                configure();
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { Scene },
                    locationPathName = output,
                    target = target,
                    options = BuildOptions.None,
                });
                var s = report.summary;
                Debug.Log($"[RobotBuilds] {target}: {s.result}, {s.totalSize / (1024 * 1024)} MB, {s.totalTime.TotalSeconds:0} sn → {output}");
                if (s.result != BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
            }
            finally
            {
                PlayerSettings.productName = productName;
            }
        }
    }
}
