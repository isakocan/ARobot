using System.IO;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;
using UnityEngine.XR.ARFoundation;
using UnityEditor.Build.Reporting;

namespace ArTarama
{
    public static class Kurulum
    {
        const string SahneYolu = "Assets/Scenes/Tarama.unity";
        const string ApkYolu = "Build/ar_tarama.apk";

        [MenuItem("ArTarama/Hepsini hazirla ve derle")]
        public static void HepsiniYap() { Hazirla(); Derle(); }

        public static void Hazirla()
        {
            AssetDatabase.Refresh();
            AndroidAyarlari();
            XrAyarlari();
            GrafikAyarlari();
            MalzemeleriOlustur();
            DuzlemPrefabi();
            SahneKur();
            AssetDatabase.SaveAssets();
            Debug.Log("[Kurulum] Hazirla bitti");
        }

        static void AndroidAyarlari()
        {
            PlayerSettings.companyName = "Ekip";
            PlayerSettings.productName = "AR Tarama";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.ekip.artarama");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToPortrait = true;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        }

        static void XrAyarlari()
        {
            const string Anahtar = "com.unity.xr.management.loader_settings";
            EditorBuildSettings.TryGetConfigObject(Anahtar, out XRGeneralSettingsPerBuildTarget hedef);
            if (hedef == null)
            {
                if (!Directory.Exists("Assets/XR")) AssetDatabase.CreateFolder("Assets", "XR");
                hedef = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(hedef, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(Anahtar, hedef, true);
            }
            var grup = BuildTargetGroup.Android;
            if (!hedef.HasSettingsForBuildTarget(grup)) hedef.CreateDefaultSettingsForBuildTarget(grup);
            if (!hedef.HasManagerSettingsForBuildTarget(grup)) hedef.CreateDefaultManagerSettingsForBuildTarget(grup);
            var gs = hedef.SettingsForBuildTarget(grup);
            gs.InitManagerOnStart = true;
            var ms = hedef.ManagerSettingsForBuildTarget(grup);
            bool ok = XRPackageMetadataStore.AssignLoader(ms, "UnityEngine.XR.ARCore.ARCoreLoader", grup);
            Debug.Log("[Kurulum] ARCore yukleyici atandi: " + ok);
            EditorUtility.SetDirty(hedef); EditorUtility.SetDirty(gs); EditorUtility.SetDirty(ms);

            // ARCore: Required + Depth Optional
            var ayar = UnityEditor.XR.ARCore.ARCoreSettings.GetOrCreateSettings();
            ayar.requirement = UnityEditor.XR.ARCore.ARCoreSettings.Requirement.Required;
            ayar.depth = UnityEditor.XR.ARCore.ARCoreSettings.Requirement.Optional;
            EditorUtility.SetDirty(ayar);
        }

        static void GrafikAyarlari()
        {
            // golge alici seffaf yuzeyler icin ekran uzayi golge toplayicisi kapali: golge haritasi dogrudan ornekleniyor
            GraphicsSettings.SetShaderMode(BuiltinShaderType.ScreenSpaceShadows, BuiltinShaderMode.Disabled);
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowDistance = 6f;
            AssetDatabase.SaveAssets();
        }

        static Material Mal(string ad, Shader s)
        {
            string yol = "Assets/Resources/" + ad + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(yol);
            if (m == null) { m = new Material(s); AssetDatabase.CreateAsset(m, yol); }
            else m.shader = s;
            return m;
        }

        static void MalzemeleriOlustur()
        {
            Directory.CreateDirectory("Assets/Resources");
            var nokta = Mal("mat_nokta", Shader.Find("ArTarama/Nokta")); nokta.SetFloat("_Boyut", 7f);   // v4: ekranda nokta cizilmez; malzeme eski sahnelerle uyum icin duruyor
            var duz = Mal("mat_duzlem", Shader.Find("ArTarama/Duzlem"));
            var gol = Mal("mat_golge", Shader.Find("ArTarama/Golge"));
            var ga = Mal("mat_golgealici", Shader.Find("ArTarama/GolgeAlici")); ga.SetFloat("_Yogunluk", 0.5f);
            var cizgi = Mal("mat_cizgi", Shader.Find("Sprites/Default"));
            var oc = Mal("mat_oda_cizgi", Shader.Find("ArTarama/OdaCizgi"));
            var od = Mal("mat_oda_dolgu", Shader.Find("ArTarama/OdaDolgu"));
            var ok = Mal("mat_oda_kati", Shader.Find("ArTarama/OdaKati"));
            var kr = Mal("mat_kristal", Shader.Find("Standard"));
            // daha az doygun ve daha az parlak: sahneye oturur
            var renk = new Color(0.55f, 0.80f, 0.76f, 1f);
            kr.color = renk; kr.SetFloat("_Metallic", 0.25f); kr.SetFloat("_Glossiness", 0.72f);
            kr.EnableKeyword("_EMISSION"); kr.SetColor("_EmissionColor", renk * 0.07f);
            kr.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            foreach (var m in new[] { nokta, duz, gol, ga, cizgi, kr, oc, od, ok }) EditorUtility.SetDirty(m);
        }

        static void DuzlemPrefabi()
        {
            Directory.CreateDirectory("Assets/Prefabs");
            var go = new GameObject("Duzlem");
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/mat_duzlem.mat"), AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/mat_golgealici.mat") };
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = true;
            go.layer = NoktaBulutu.HaritaKatmani;
            go.AddComponent<ARPlane>();
            go.AddComponent<ARPlaneMeshVisualizer>();
            go.AddComponent<PlaneStyle>();
            PrefabUtility.SaveAsPrefabAsset(go, "Assets/Prefabs/Duzlem.prefab");
            Object.DestroyImmediate(go);
        }

        static void SahneKur()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var sahne = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Isik
            var isikGo = new GameObject("Directional Light");
            var isik = isikGo.AddComponent<Light>();
            isik.type = LightType.Directional; isik.intensity = 0.9f; isik.shadows = LightShadows.Soft; isik.shadowStrength = 0.8f; isik.shadowBias = 0.03f; isik.shadowNormalBias = 0.4f;
            isikGo.transform.rotation = Quaternion.Euler(50, -30, 0);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.5f);
            RenderSettings.skybox = null;

            // AR oturumu
            var oturumGo = new GameObject("AR Session");
            var oturum = oturumGo.AddComponent<ARSession>();
            oturumGo.AddComponent<ARInputManager>();

            // XR Origin
            var originGo = new GameObject("XR Origin");
            var origin = originGo.AddComponent<XROrigin>();
            var ofset = new GameObject("Camera Offset");
            ofset.transform.SetParent(originGo.transform, false);
            var camGo = new GameObject("AR Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(ofset.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 30f;
            cam.allowHDR = false; cam.allowMSAA = false;
            camGo.AddComponent<AudioListener>();
            var kamYon = camGo.AddComponent<ARCameraManager>();
            camGo.AddComponent<ARCameraBackground>();
            var okl = camGo.AddComponent<AROcclusionManager>();

            var tpd = camGo.AddComponent<TrackedPoseDriver>();
            tpd.positionInput = new InputActionProperty(new InputAction("Pozisyon", binding: "<HandheldARInputDevice>/devicePosition", expectedControlType: "Vector3"));
            tpd.rotationInput = new InputActionProperty(new InputAction("Donus", binding: "<HandheldARInputDevice>/deviceRotation", expectedControlType: "Quaternion"));
            tpd.trackingStateInput = new InputActionProperty(new InputAction("Takip", binding: "<HandheldARInputDevice>/trackingState", expectedControlType: "Integer"));

            origin.Origin = originGo;
            origin.CameraFloorOffsetObject = ofset;
            origin.Camera = cam;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;

            var duzlem = originGo.AddComponent<ARPlaneManager>();
            duzlem.planePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Duzlem.prefab");
            duzlem.requestedDetectionMode = UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Horizontal | UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Vertical;
            var pc = originGo.AddComponent<ARPointCloudManager>();
            var isin = originGo.AddComponent<ARRaycastManager>();
            originGo.AddComponent<ARAnchorManager>();
            var probe = originGo.AddComponent<AREnvironmentProbeManager>();
            probe.automaticPlacementRequested = true;

            // Uygulama
            var uyg = new GameObject("Uygulama");
            var app = uyg.AddComponent<ArApp>();
            var bulut = uyg.AddComponent<NoktaBulutu>();
            var kayit = uyg.AddComponent<KayitYoneticisi>();
            var planH = uyg.AddComponent<PlanHaritasi>();
            var isaret = uyg.AddComponent<IsaretYoneticisi>();
            var odaY = uyg.AddComponent<OdaYoneticisi>();

            // Inceleme ekrani kamerasi (ayri nesne, baslangicta kapali)
            var hGo = new GameObject("Gozden");
            var hkGo = new GameObject("GozdenKamera"); hkGo.transform.SetParent(hGo.transform, false);
            var hkam = hkGo.AddComponent<Camera>(); hkam.enabled = false;
            var h3 = hGo.AddComponent<OdaGozden>(); h3.kam = hkam;
            app.oturum = oturum; app.kamera = kamYon; app.oklusyon = okl; app.duzlem = duzlem;
            app.isin = isin; app.nokta = pc; app.arKamera = cam; app.isik = isik;
            app.bulut = bulut; app.kayit = kayit; app.plan = planH; app.isaret = isaret; app.gozden = h3; app.probe = probe; app.oda = odaY;

            EditorSceneManager.SaveScene(sahne, SahneYolu);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(SahneYolu, true) };
        }

        public static void Derle()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Directory.CreateDirectory("Build");
            var opt = new BuildPlayerOptions
            {
                scenes = new[] { SahneYolu },
                locationPathName = ApkYolu,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };
            var rapor = BuildPipeline.BuildPlayer(opt);
            Debug.Log("[Kurulum] Derleme sonucu: " + rapor.summary.result + " sure=" + sw.Elapsed + " boyut=" + rapor.summary.totalSize);
            EditorApplication.Exit(rapor.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
