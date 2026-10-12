using System.Threading;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

// Tek seferlik proje kurulumu: paketler (1. adım) ve temel ayarlar (2. adım).
// Komut satırından: Unity -batchmode -executeMethod ARaki.EditorTools.ProjectBootstrap.<Metot>
namespace ARaki.EditorTools
{
    public static class ProjectBootstrap
    {
        static readonly string[] Packages =
        {
            "com.unity.render-pipelines.universal",
            "com.unity.inputsystem",
            "com.unity.test-framework",
            "com.unity.ugui",
        };

        public static void InstallPackages()
        {
            foreach (var id in Packages)
            {
                AddRequest req = Client.Add(id);
                while (!req.IsCompleted) Thread.Sleep(100);
                Debug.Log(req.Status == StatusCode.Success
                    ? $"[Bootstrap] eklendi: {req.Result.packageId}"
                    : $"[Bootstrap] HATA {id}: {req.Error?.message}");
            }
        }

        public static void ApplySettings()
        {
            PlayerSettings.companyName = "ARaki";
            PlayerSettings.productName = GameInfo.Title;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            string[] folders =
            {
                "Scenes", "Prefabs", "Settings", "Art", "UI/Theme", "UI/Components", "UI/Screens",
                "Scripts/Core", "Scripts/Robot", "Scripts/Net", "Scripts/Video",
                "Scripts/AR", "Scripts/Game", "Scripts/UI", "Scripts/Dev",
            };
            const string root = "Assets/_Project";
            if (!AssetDatabase.IsValidFolder(root)) AssetDatabase.CreateFolder("Assets", "_Project");
            foreach (var f in folders)
            {
                string path = root;
                foreach (var part in f.Split('/'))
                {
                    string next = path + "/" + part;
                    if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(path, part);
                    path = next;
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Bootstrap] ayarlar ve klasörler tamam");
        }
    }
}
