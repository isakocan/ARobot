using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ARaki.EditorTools
{
    /// <summary>
    /// Telefon (Samsung S21+, Android) için performans ayarları. Sahne yeniden kurulurken uygulanır:
    /// - Düşük kalite seviyeleri için hafif bir URP profili (MSAA kapalı, küçük gölge haritası, kısa gölge mesafesi).
    /// - Dokular ve modeller Android'de GPU'nun doğrudan okuyabildiği ASTC biçiminde ve sıkıştırılmış mesh ile.
    /// - Android oyuncu ayarları: IL2CPP + ARM64, Vulkan/GLES3, ASTC, çoklu iş parçacıklı çizim.
    /// </summary>
    public static class Optimizer
    {
        const string Root = "Assets/_Project";
        const string MobilePath = Root + "/Settings/ARaki_URP_Mobile.asset";

        /// <summary>URP profillerini ve kalite seviyelerini hazırlar (SetupPipeline'dan sonra çağrılır).</summary>
        public static void ConfigurePipelines()
        {
            var pc = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(Root + "/Settings/ARaki_URP.asset");
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Root + "/Settings/ARaki_Renderer.asset");
            if (pc == null || data == null) { Debug.LogWarning("[Optimizer] URP profili bulunamadı"); return; }

            var mobile = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobilePath);
            if (mobile == null)
            {
                mobile = UniversalRenderPipelineAsset.Create(data);
                AssetDatabase.CreateAsset(mobile, MobilePath);
            }

            var so = new SerializedObject(mobile);
            void SetInt(string name, int v) { var p = so.FindProperty(name); if (p != null) p.intValue = v; }
            void SetFloat(string name, float v) { var p = so.FindProperty(name); if (p != null) p.floatValue = v; }
            void SetBool(string name, bool v) { var p = so.FindProperty(name); if (p != null) p.boolValue = v; }
            SetInt("m_MSAA", 1);                          // kenar yumuşatmayı kamerada SMAA yapıyor; MSAA yinelenirdi
            SetFloat("m_RenderScale", 1f);
            SetInt("m_MainLightShadowmapResolution", 1024);
            SetFloat("m_ShadowDistance", 9f);
            SetInt("m_ShadowCascadeCount", 1);
            SetBool("m_SoftShadowsSupported", false);
            SetBool("m_SupportsTerrainHoles", false);
            SetInt("m_ColorGradingLutSize", 16);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(mobile);

            // 0-2 (Very Low, Low, Medium) hafif profil; Android varsayılanı 2 (Medium). 3-5 masaüstü.
            int original = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i);
                QualitySettings.renderPipeline = i <= 2 ? (RenderPipelineAsset)mobile : pc;
                QualitySettings.vSyncCount = i <= 2 ? 0 : 1;
            }
            QualitySettings.SetQualityLevel(original);
            AssetDatabase.SaveAssets();
        }

        /// <summary>İçe aktarılan doku ve modelleri Android için sıkıştırır. Yalnızca değişen varlıklar yeniden içe aktarılır.</summary>
        public static void ConfigureImports()
        {
            int textures = 0, models = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Art/Imported" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter imp)) continue;

                bool normal = imp.textureType == TextureImporterType.NormalMap;
                bool changed = false;
                if (imp.maxTextureSize > 1024) { imp.maxTextureSize = 1024; changed = true; }
                if (imp.crunchedCompression) { imp.crunchedCompression = false; changed = true; }
                if (!imp.mipmapEnabled) { imp.mipmapEnabled = true; changed = true; }

                var android = imp.GetPlatformTextureSettings("Android");
                var wanted = normal ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.ASTC_6x6;
                if (!android.overridden || android.format != wanted || android.maxTextureSize != 1024)
                {
                    android.overridden = true;
                    android.format = wanted;
                    android.maxTextureSize = 1024;
                    android.compressionQuality = 50;
                    imp.SetPlatformTextureSettings(android);
                    changed = true;
                }
                if (changed) { imp.SaveAndReimport(); textures++; }
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Root + "/Art/Imported" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is ModelImporter imp)) continue;

                bool changed = false;
                if (imp.meshCompression != ModelImporterMeshCompression.Medium) { imp.meshCompression = ModelImporterMeshCompression.Medium; changed = true; }
                if (!imp.optimizeMeshVertices || !imp.optimizeMeshPolygons) { imp.optimizeMeshVertices = true; imp.optimizeMeshPolygons = true; changed = true; }
                if (imp.isReadable) { imp.isReadable = false; changed = true; }
                if (imp.importCameras || imp.importLights) { imp.importCameras = false; imp.importLights = false; changed = true; }
                if (imp.importBlendShapes) { imp.importBlendShapes = false; changed = true; }
                if (imp.animationType != ModelImporterAnimationType.None) { imp.animationType = ModelImporterAnimationType.None; changed = true; }
                if (imp.importAnimation) { imp.importAnimation = false; changed = true; }
                if (changed) { imp.SaveAndReimport(); models++; }
            }
            Debug.Log($"[Optimizer] içe aktarma: {textures} doku, {models} model güncellendi");
        }

        /// <summary>Android derlemesi için oyuncu ayarları (Unity'nin Android modülü kurulu olmasa da kaydedilir).</summary>
        public static void ConfigureAndroid()
        {
            void Try(string what, Action action)
            {
                try { action(); }
                catch (Exception e) { Debug.LogWarning($"[Optimizer] {what} ayarlanamadı: {e.Message}"); }
            }

            Try("betik arka ucu", () => PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP));
            Try("mimari", () => PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64);
            Try("grafik API", () =>
            {
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
            });
            Try("en düşük sürüm", () => PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26);
            Try("kod ayıklama", () => PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Medium));
            Try("doku biçimi", () => EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC);
            Try("çoklu çizim", () => PlayerSettings.MTRendering = true);
            Try("çerçeve hızı", () => PlayerSettings.Android.optimizedFramePacing = true);
            Try("GPU deri", () => PlayerSettings.gpuSkinning = true);
        }
    }
}
