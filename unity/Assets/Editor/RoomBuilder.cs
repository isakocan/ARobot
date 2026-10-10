using ARaki.AR;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ARaki.EditorTools
{
    /// <summary>Oturma odası: gerçek dokulu zemin ve duvarlar, model mobilyalar, perde, pencere ışığı ve tavan lambası.</summary>
    public static class RoomBuilder
    {
        public const float Width = 6f, Depth = 5f, Height = 2.5f;

        public class PrintObjects
        {
            public Transform[] Towers;
            public Transform Fuel;
            public Transform BasePad;
        }

        static Color C(string hex) => SceneBuilder.C(hex);

        public static PrintObjects Build()
        {
            AssetPipeline.PrepareTextures();
            var room = new GameObject("Room").transform;
            const float w = Width, d = Depth, h = Height, t = 0.1f;

            // --- yüzeyler (gerçek PBR dokular)
            var floorMat = AssetPipeline.TextureSetMaterial("laminate_floor_02", new Vector2(w / 1.6f, d / 1.6f), null, 1f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(room);
            floor.transform.localScale = new Vector3(w / 10f, 1f, d / 10f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;
            Object.DestroyImmediate(floor.GetComponent<Collider>()); // zemin çarpma saymaz

            var wallEW = AssetPipeline.TextureSetMaterial("white_plaster_02", new Vector2(d / 2f, h / 2f), C("#FFFFFF"), 1f);
            var wallNS = AssetPipeline.TextureSetMaterial("white_plaster_02", new Vector2(w / 2f, h / 2f), C("#FFFFFF"), 1f);
            var ceilingMat = AssetPipeline.TextureSetMaterial("white_plaster_02", new Vector2(w / 2f, d / 2f), C("#F4F0E8"), 1f);
            Box("WallN", room, new Vector3(0f, h / 2f, d / 2f + t / 2f), new Vector3(w + 2 * t, h, t), wallNS);
            Box("WallS", room, new Vector3(0f, h / 2f, -d / 2f - t / 2f), new Vector3(w + 2 * t, h, t), wallNS);
            Box("WallE", room, new Vector3(w / 2f + t / 2f, h / 2f, 0f), new Vector3(t, h, d), wallEW);
            Box("WallW", room, new Vector3(-w / 2f - t / 2f, h / 2f, 0f), new Vector3(t, h, d), wallEW);
            var ceiling = Box("Ceiling", room, new Vector3(0f, h + 0.025f, 0f), new Vector3(w + 2 * t, 0.05f, d + 2 * t), ceilingMat, false);
            ceiling.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            ceiling.layer = 2; // üstten kamera tavanı çizmez

            // süpürgelik
            var trim = SceneBuilder.Mat("Trim", C("#F7F4EE"), smooth: 0.35f);
            Box("TrimN", room, new Vector3(0f, 0.06f, d / 2f - 0.012f), new Vector3(w, 0.12f, 0.024f), trim, false);
            Box("TrimS", room, new Vector3(0f, 0.06f, -d / 2f + 0.012f), new Vector3(w, 0.12f, 0.024f), trim, false);
            Box("TrimE", room, new Vector3(w / 2f - 0.012f, 0.06f, 0f), new Vector3(0.024f, 0.12f, d), trim, false);
            Box("TrimW", room, new Vector3(-w / 2f + 0.012f, 0.06f, 0f), new Vector3(0.024f, 0.12f, d), trim, false);

            BuildWindow(room);

            // --- halı
            var rugMat = AssetPipeline.TextureSetMaterial("wool_boucle", new Vector2(4.6f, 3.3f), C("#A9B4C2"), 0.8f);
            var rug = Box("Rug", room, new Vector3(-0.2f, 0.007f, 0.2f), new Vector3(3.0f, 0.014f, 2.1f), rugMat, false);
            rug.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            // --- mobilya
            Place("sofa_02", room, new Vector3(-1.2f, 0f, 2.05f), 180f);
            Place("throw_pillows_01", room, new Vector3(-1.2f, 0.34f, 2.18f), 180f, 0.8f, false, false);
            Place("modern_coffee_table_01", room, new Vector3(-1.2f, 0f, 0.9f), 0f);
            Place("modern_arm_chair_01", room, new Vector3(0.45f, 0f, 2.0f), 205f);
            Place("side_table_01", room, new Vector3(-2.55f, 0f, 2.1f), 0f);
            Place("ceramic_vase_01", room, new Vector3(-2.55f, 0.55f, 2.1f), 0f, 0.8f, false, false);
            Place("modern_wooden_cabinet", room, new Vector3(0.6f, 0f, -2.2f), 0f);
            Place("standing_picture_frame_01", room, new Vector3(0.2f, 0.68f, -2.2f), 20f, 1f, false, false);
            Place("wooden_display_shelves_01", room, new Vector3(-2.8f, 0f, -0.3f), 90f);
            Place("potted_plant_02", room, new Vector3(2.55f, 0f, 2.05f), 0f);
            Place("wicker_basket_02", room, new Vector3(-0.05f, 0f, 0.95f), 30f);
            Place("hanging_picture_frame_01", room, new Vector3(-1.2f, 1.55f, 2.485f), 180f, 1f, false, false);
            Place("hanging_picture_frame_02", room, new Vector3(-2.985f, 1.5f, -1.5f), 90f, 1f, false, false);
            Place("hanging_picture_frame_02", room, new Vector3(2.985f, 1.45f, -0.9f), 270f, 1f, false, false);
            Place("modern_ceiling_lamp_01", room, new Vector3(0f, h - 1.1f, 0.2f), 0f, 1f, false, false);

            return BuildPrints(room);
        }

        static void BuildWindow(Transform room)
        {
            const float x = Width / 2f;
            var frame = SceneBuilder.Mat("WindowFrame", C("#F4F1EA"), smooth: 0.4f);
            var glass = SceneBuilder.Mat("WindowPane", C("#FFE2B0"), smooth: 0.9f, emission: C("#FFC878") * 1.8f);
            var curtain = AssetPipeline.TextureSetMaterial("wool_boucle", new Vector2(0.5f, 2f), C("#CFC6B8"), 0.3f);

            Box("WindowPane", room, new Vector3(x - 0.015f, 1.5f, 0.2f), new Vector3(0.02f, 1.2f, 1.7f), glass, false);
            // çerçeve
            Box("FrameTop", room, new Vector3(x - 0.03f, 2.13f, 0.2f), new Vector3(0.05f, 0.06f, 1.82f), frame, false);
            Box("FrameBottom", room, new Vector3(x - 0.04f, 0.87f, 0.2f), new Vector3(0.09f, 0.05f, 1.86f), frame, false);
            Box("FrameL", room, new Vector3(x - 0.03f, 1.5f, -0.68f), new Vector3(0.05f, 1.26f, 0.05f), frame, false);
            Box("FrameR", room, new Vector3(x - 0.03f, 1.5f, 1.08f), new Vector3(0.05f, 1.26f, 0.05f), frame, false);
            Box("FrameMid", room, new Vector3(x - 0.03f, 1.5f, 0.2f), new Vector3(0.04f, 1.2f, 0.035f), frame, false);
            // perdeler
            Box("CurtainA", room, new Vector3(x - 0.1f, 1.45f, -0.92f), new Vector3(0.07f, 2.1f, 0.34f), curtain, false);
            Box("CurtainB", room, new Vector3(x - 0.1f, 1.45f, 1.32f), new Vector3(0.07f, 2.1f, 0.34f), curtain, false);
            Box("CurtainRod", room, new Vector3(x - 0.1f, 2.45f, 0.2f), new Vector3(0.025f, 0.025f, 2.4f), SceneBuilder.Mat("Rod", C("#2B2B2B"), smooth: 0.6f), false);

            // pencereden giren ışık huzmesi (gerçek ışığın görsel karşılığı)
            var shaft = GameObject.CreatePrimitive(PrimitiveType.Quad);
            shaft.name = "LightShaft";
            Object.DestroyImmediate(shaft.GetComponent<Collider>());
            shaft.transform.SetParent(room, false);
            var dir = Quaternion.Euler(30f, 262f, 0f) * Vector3.forward;       // güneşin gittiği yön
            Vector3 start = new Vector3(x - 0.1f, 1.5f, 0.2f);
            float length = 3.4f;
            shaft.transform.position = start + dir * (length * 0.5f);
            shaft.transform.rotation = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, 0f, 0f);
            shaft.transform.localScale = new Vector3(1.4f, length, 1f);
            // Quad'ın düzlemi XY; uzunluk ekseni Z olsun diye dönüştür
            shaft.transform.rotation = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up).normalized, dir);
            shaft.transform.localScale = new Vector3(1.4f, length, 1f);
            var mat = SceneBuilder.AdditiveMat("LightShaft", C("#FFC47A") * 0.22f, SceneBuilder.GlowTexture());
            var r = shaft.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            // havada uçuşan toz
            var dust = new GameObject("Dust");
            dust.transform.SetParent(room, false);
            dust.transform.position = start + dir * 1.6f;
            var ps = dust.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true; main.prewarm = true;
            main.startLifetime = 9f; main.startSpeed = 0.02f; main.startSize = 0.012f;
            main.maxParticles = 90; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 10f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(2.2f, 1.1f, 1.1f);
            var fade = ps.colorOverLifetime; fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.7f, 0.25f), new GradientAlphaKey(0.7f, 0.75f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(g);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.05f; noise.frequency = 0.4f;
            var pr = dust.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = SceneBuilder.AdditiveMat("DustMote", C("#FFE2B8") * 1.0f, SceneBuilder.GlowTexture(), true);
            pr.shadowCastingMode = ShadowCastingMode.Off;
        }

        // 3D baskı görev objeleri: oyuncunun odaya koyduğu gerçek nesneler. Konumları görev üreticisi belirler.
        static PrintObjects BuildPrints(Transform room)
        {
            var orange = SceneBuilder.Mat("PrintOrange", C("#FF5A1F"), smooth: 0.35f);
            var orangeDark = SceneBuilder.Mat("PrintOrangeDark", C("#D9470F"), smooth: 0.3f);
            var teal = SceneBuilder.Mat("PrintTeal", C("#19A99A"), smooth: 0.4f);
            var tealDark = SceneBuilder.Mat("PrintTealDark", C("#0F7A70"), smooth: 0.3f);
            var pad = SceneBuilder.Mat("PrintPad", C("#3A424B"), smooth: 0.25f);

            var result = new PrintObjects { Towers = new Transform[4] };
            for (int i = 0; i < 4; i++)
            {
                var tower = new GameObject("PrintTower" + (i + 1)).transform;
                tower.SetParent(room, false);
                tower.position = new Vector3(0.4f + i * 0.5f, 0f, 1.1f);
                // üst üste basılmış katmanlar: hafif daralan, hafif döndürülmüş halkalar
                for (int l = 0; l < 7; l++)
                {
                    var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    ring.name = "Layer" + l;
                    Object.DestroyImmediate(ring.GetComponent<Collider>());
                    ring.transform.SetParent(tower, false);
                    float radius = Mathf.Lerp(0.062f, 0.036f, l / 6f);
                    ring.transform.localScale = new Vector3(radius * 2f, 0.012f, radius * 2f);
                    ring.transform.localPosition = new Vector3(0f, 0.012f + l * 0.022f, 0f);
                    ring.transform.localRotation = Quaternion.Euler(0f, l * 11f, 0f);
                    ring.GetComponent<MeshRenderer>().sharedMaterial = l % 2 == 0 ? orange : orangeDark;
                }
                var cap = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                cap.name = "Cap";
                Object.DestroyImmediate(cap.GetComponent<Collider>());
                cap.transform.SetParent(tower, false);
                cap.transform.localScale = Vector3.one * 0.05f;
                cap.transform.localPosition = new Vector3(0f, 0.18f, 0f);
                cap.GetComponent<MeshRenderer>().sharedMaterial = orange;
                var col = tower.gameObject.AddComponent<CapsuleCollider>();
                col.radius = 0.06f; col.height = 0.2f; col.center = new Vector3(0f, 0.1f, 0f);
                AssetPipeline.ContactShadow(room, new Bounds(new Vector3(0f, 0f, 0f), new Vector3(0.14f, 0.1f, 0.14f)), 0.5f, 1.6f);
                result.Towers[i] = tower;
            }

            // yakıt tankı: turkuaz silindir, koyu bantlar ve kapak
            var fuel = new GameObject("PrintFuel").transform;
            fuel.SetParent(room, false);
            fuel.position = new Vector3(2.2f, 0f, -1.2f);
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(fuel, false);
            body.transform.localScale = new Vector3(0.13f, 0.1f, 0.13f);
            body.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            body.GetComponent<MeshRenderer>().sharedMaterial = teal;
            foreach (float y in new[] { 0.05f, 0.15f })
            {
                var band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                band.name = "Band";
                Object.DestroyImmediate(band.GetComponent<Collider>());
                band.transform.SetParent(fuel, false);
                band.transform.localScale = new Vector3(0.138f, 0.01f, 0.138f);
                band.transform.localPosition = new Vector3(0f, y, 0f);
                band.GetComponent<MeshRenderer>().sharedMaterial = tealDark;
            }
            var lid = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lid.name = "Lid";
            Object.DestroyImmediate(lid.GetComponent<Collider>());
            lid.transform.SetParent(fuel, false);
            lid.transform.localScale = new Vector3(0.07f, 0.012f, 0.07f);
            lid.transform.localPosition = new Vector3(0f, 0.212f, 0f);
            lid.GetComponent<MeshRenderer>().sharedMaterial = tealDark;
            var fuelCol = fuel.gameObject.AddComponent<CapsuleCollider>();
            fuelCol.radius = 0.07f; fuelCol.height = 0.22f; fuelCol.center = new Vector3(0f, 0.11f, 0f);
            AssetPipeline.ContactShadow(room, new Bounds(Vector3.zero, new Vector3(0.16f, 0.1f, 0.16f)), 0.5f, 1.6f);
            result.Fuel = fuel;

            // ana üs: yerdeki düz disk
            var basePad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            basePad.name = "PrintBasePad";
            Object.DestroyImmediate(basePad.GetComponent<Collider>());
            basePad.transform.SetParent(room, false);
            basePad.transform.localScale = new Vector3(0.56f, 0.006f, 0.56f);
            basePad.transform.position = new Vector3(-2.15f, 0.006f, -1.9f);
            basePad.GetComponent<MeshRenderer>().sharedMaterial = pad;
            basePad.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            result.BasePad = basePad.transform;
            return result;
        }

        static void Place(string id, Transform parent, Vector3 pos, float yaw, float scale = 1f, bool collider = true, bool shadow = true)
            => AssetPipeline.Place(id, parent, pos, yaw, scale, collider, shadow);

        static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.position = pos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }
    }
}
