using ARaki.AR;
using ARaki.Game;
using ARaki.Robot;
using ARaki.UI;
using ARaki.Video;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ARaki.EditorTools
{
    /// <summary>Yarış modu: AR kapı şablonu, pist malzemesi, yarış denetleyicisi ve yarış arayüzü.</summary>
    public static partial class SceneBuilder
    {
        static RaceController BuildRace(Transform robot, MockRobotLink link, ARRig rig, Transform arRoot)
        {
            var go = new GameObject("Race");
            var race = go.AddComponent<RaceController>();
            SetRef(race, "robot", robot);
            SetRef(race, "link", link);
            SetRef(race, "rig", rig);
            SetRef(race, "arRoot", arRoot);
            SetRef(race, "gateTemplate", BuildGateTemplate(arRoot));
            SetRef(race, "trackMaterial", TrackMaterial());
            return race;
        }

        static Material TrackMaterial()
        {
            var mat = LoadOrCreateMaterial("RaceTrack", "ARaki/Track");
            mat.SetColor("_PulseColor", C("#FFB020") * 1.4f);
            mat.SetFloat("_PulseSpeed", 0.9f);
            mat.SetFloat("_PulsePeriod", 1.3f);
            mat.SetFloat("_PulseStrength", 0.32f);
            mat.renderQueue = 2600;          // kapılardan (3000, ışık ekleyen) önce çizilir
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Yarış kapısı: iki direk, üst kiriş, aradan akan ışık perdesi, yerde geçiş çizgisi ve yön okları.
        /// Gerçekte bu, kameranın tanıdığı bir işaretçinin üstüne oturan 3D baskı kapıdır.
        /// </summary>
        static GameObject BuildGateTemplate(Transform arRoot)
        {
            var root = new GameObject("RaceGateTemplate");
            root.transform.SetParent(arRoot, false);

            var surface = HoloSurface("HoloGate", Color.white, 0.55f, 2.0f, 1.2f, 0.35f, 0.08f);
            var curtain = HoloSurface("HoloGateCurtain", Color.white, 0.3f, 1f, 0.2f, 0.55f, 0.1f);
            var banner = HoloSurface("HoloGateBanner", Color.white, 0.95f, 1f, 0.2f, 0.2f, 0.05f);
            var line = HoloLineMat();

            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -0.4f : 0.4f;
                HoloPrim(PrimitiveType.Cylinder, "Post", root.transform, new Vector3(x, 0.26f, 0f), new Vector3(0.05f, 0.26f, 0.05f), surface);
                HoloPrim(PrimitiveType.Sphere, "Cap", root.transform, new Vector3(x, 0.54f, 0f), new Vector3(0.075f, 0.075f, 0.075f), surface);
            }
            HoloPrim(PrimitiveType.Cube, "Beam", root.transform, new Vector3(0f, 0.52f, 0f), new Vector3(0.84f, 0.03f, 0.035f), surface);
            HoloMesh("Fan", root.transform, Vector3.zero, TrackMesh.Curtain(0.8f, 0.5f), curtain);
            Line(root.transform, "Threshold", new[] { new Vector3(-0.4f, 0.004f, 0f), new Vector3(0.4f, 0.004f, 0f) }, 0.014f, line);

            // yön okları: geçilecek yöne bakar (+Z)
            for (int i = 0; i < 2; i++)
            {
                float z = -0.2f - i * 0.17f;
                Line(root.transform, "Arc", new[]
                {
                    new Vector3(-0.13f, 0.004f, z - 0.12f), new Vector3(0f, 0.004f, z), new Vector3(0.13f, 0.004f, z - 0.12f),
                }, 0.012f, line);
            }
            Glow(root.transform, "Glow", 1.1f, 0.003f);

            // başlangıç/bitiş kapısında dama bayrağı
            var flag = HoloMesh("StartBanner", root.transform, new Vector3(0f, 0.58f, 0f), TrackMesh.CheckerBanner(0.84f, 0.16f, 12), banner);
            flag.SetActive(false);

            MakeBurst(root.transform);
            root.AddComponent<HoloTint>();
            root.SetActive(false);
            return root;
        }

        static RaceHud BuildRaceHud(MockRobotLink link, RaceController race, PanoramaViewer viewer, GameSession session)
        {
            var go = new GameObject("RaceHud");
            var hud = go.AddComponent<RaceHud>();
            SetRef(hud, "robot", link);
            SetRef(hud, "race", race);
            SetRef(hud, "viewer", viewer);
            SetRef(hud, "session", session);
            SetRef(hud, "titleFont", LoadFont("Rajdhani-Bold"));
            SetRef(hud, "bodyFont", LoadFont("Rajdhani-Medium"));
            SetRef(hud, "monoFont", LoadFont("ShareTechMono-Regular"));
            SetRef(hud, "bracket", MakeBracketSprite());
            SetRef(hud, "ring", MakeRingSprite());
            return hud;
        }
    }
}
