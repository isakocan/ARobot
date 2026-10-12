using System.Collections.Generic;
using ARaki.Theming;
using ARaki.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ARaki.EditorTools
{
    /// <summary>
    /// Çok odalı ev: HouseLayout'taki planı (zeminler, duvarlar, kapı açıklıkları, tavanlar) kurar, odaları mobilyayla döşer
    /// (Poly Haven CC0 modelleri) ve her odaya bir ışık koyar. Duvar ve tavanlar seçili temaya göre gökyüzü/orman/duvar kağıdı olur.
    /// </summary>
    public static class RoomBuilder
    {
        static Color C(string hex) => SceneBuilder.C(hex);
        static Transform house;
        static Material trim, white;
        static Material[] wallSky, ceilingSky, windowSky;

        public static void Build()
        {
            AssetPipeline.PrepareTextures();
            house = new GameObject("House").transform;
            trim = SceneBuilder.Mat("Trim", C("#F2EEE6"), smooth: 0.35f);
            white = SceneBuilder.Mat("Porcelain", C("#F4F6F6"), smooth: 0.85f);
            wallSky = SceneBuilder.SkyMaterials(SurfaceRole.Wall);
            ceilingSky = SceneBuilder.SkyMaterials(SurfaceRole.Ceiling);
            windowSky = SceneBuilder.SkyMaterials(SurfaceRole.Window);

            BuildShells();
            BuildDoorFrames();
            BuildLiving();
            BuildStudy();
            BuildKitchen();
            BuildBedroom();
            BuildBath();
            BuildHall();
            BuildLights();
            Debug.Log("[RoomBuilder] ThemeSurface sayısı (MarkStatic öncesi): " + house.GetComponentsInChildren<ThemeSurface>(true).Length);
            MarkStatic(house);
            Debug.Log("[RoomBuilder] ThemeSurface sayısı (MarkStatic sonrası): " + house.GetComponentsInChildren<ThemeSurface>(true).Length);
        }

        // ------------------------------------------------------------------ kabuk: zemin, duvar, tavan

        static Material FloorFor(RoomInfo r)
        {
            float w = r.Rect.width, d = r.Rect.height;
            switch (r.Kind)
            {
                case RoomKind.Kitchen: return AssetPipeline.TextureSetMaterial("kitchen_tiles", new Vector2(w / 1.4f, d / 1.4f), null, 1f);
                case RoomKind.Bath: return AssetPipeline.TextureSetMaterial("bath_floor", new Vector2(w / 1.2f, d / 1.2f), null, 1f);
                case RoomKind.Hall: return AssetPipeline.TextureSetMaterial("hall_parquet", new Vector2(w / 1.6f, d / 1.6f), null, 1f);
                case RoomKind.Study: return AssetPipeline.TextureSetMaterial("laminate_floor_02", new Vector2(w / 1.6f, d / 1.6f), C("#C9B8A4"), 1f);
                default: return AssetPipeline.TextureSetMaterial("laminate_floor_02", new Vector2(w / 1.6f, d / 1.6f), null, 1f);
            }
        }

        static void BuildShells()
        {
            const float h = HouseLayout.WallHeight, t = HouseLayout.WallThickness;
            foreach (var r in HouseLayout.Rooms)
            {
                var root = new GameObject("Room_" + r.Id).transform;
                root.SetParent(house, false);
                var rect = r.Rect;

                // zemin: oda büyüklüğünde bir kutu (çarpışma yok)
                var floor = Box("Floor", root, new Vector3(rect.center.x, -0.02f, rect.center.y), new Vector3(rect.width, 0.04f, rect.height), FloorFor(r), false);
                floor.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

                // tavan
                var ceiling = Box("Ceiling", root, new Vector3(rect.center.x, h + 0.025f, rect.center.y), new Vector3(rect.width, 0.05f, rect.height), ceilingSky[0], false);
                ceiling.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                ceiling.layer = 2;       // üstten kamera tavanı çizmez
                Themed(ceiling, SurfaceRole.Ceiling, ceilingSky);

                // dört duvar: kapı açıklıkları kesilir
                WallLine(root, true, rect.yMin + t * 0.5f, rect.xMin, rect.xMax, 1);       // güney
                WallLine(root, true, rect.yMax - t * 0.5f, rect.xMin, rect.xMax, -1);      // kuzey
                WallLine(root, false, rect.xMin + t * 0.5f, rect.yMin, rect.yMax, 1);      // batı
                WallLine(root, false, rect.xMax - t * 0.5f, rect.yMin, rect.yMax, -1);     // doğu
            }
        }

        /// <summary>
        /// Bir duvar çizgisi. alongX = duvar x ekseninde uzanır (z sabit). Kapı açıklıklarında yalnızca lento (üst parça) kalır.
        /// </summary>
        static void WallLine(Transform root, bool alongX, float fixedCoord, float from, float to, int inwardSign)
        {
            const float h = HouseLayout.WallHeight, t = HouseLayout.WallThickness, doorH = 2.1f;
            float edge = fixedCoord - inwardSign * t * 0.5f;       // kapının tanımlandığı gerçek kenar
            var doors = HouseLayout.DoorsOnWall(alongX, Mathf.Round(edge * 100f) / 100f);
            doors.Sort((a, b) => (alongX ? a.Center.x : a.Center.z).CompareTo(alongX ? b.Center.x : b.Center.z));

            float cursor = from;
            foreach (var d in doors)
            {
                float c = alongX ? d.Center.x : d.Center.z;
                float gapStart = c - d.Width * 0.5f, gapEnd = c + d.Width * 0.5f;
                if (gapStart > cursor) WallPiece(root, alongX, fixedCoord, cursor, gapStart, 0f, h);
                WallPiece(root, alongX, fixedCoord, gapStart, gapEnd, doorH, h);      // lento
                cursor = gapEnd;
            }
            if (to > cursor) WallPiece(root, alongX, fixedCoord, cursor, to, 0f, h);
        }

        static void WallPiece(Transform root, bool alongX, float fixedCoord, float a, float b, float y0, float y1)
        {
            float len = b - a, height = y1 - y0;
            if (len < 0.01f || height < 0.01f) return;
            Vector3 center = alongX
                ? new Vector3((a + b) * 0.5f, (y0 + y1) * 0.5f, fixedCoord)
                : new Vector3(fixedCoord, (y0 + y1) * 0.5f, (a + b) * 0.5f);
            Vector3 size = alongX
                ? new Vector3(len, height, HouseLayout.WallThickness)
                : new Vector3(HouseLayout.WallThickness, height, len);
            var wall = Box("Wall", root, center, size, wallSky[0], true);
            Themed(wall, SurfaceRole.Wall, wallSky);
        }

        static void Themed(GameObject go, SurfaceRole role, Material[] set)
        {
            var s = go.AddComponent<ThemeSurface>();
            s.Role = role;
            s.ThemeMaterials = set;
        }

        static void BuildDoorFrames()
        {
            foreach (var d in HouseLayout.Doors)
            {
                var frame = new GameObject("DoorFrame").transform;
                frame.SetParent(house, false);
                const float jamb = 0.05f, depth = 0.18f, height = 2.1f;
                float half = d.Width * 0.5f;
                if (d.AlongX)
                {
                    Box("JambL", frame, new Vector3(d.Center.x - half, height * 0.5f, d.Center.z), new Vector3(jamb, height, depth), trim, false);
                    Box("JambR", frame, new Vector3(d.Center.x + half, height * 0.5f, d.Center.z), new Vector3(jamb, height, depth), trim, false);
                    Box("Header", frame, new Vector3(d.Center.x, height, d.Center.z), new Vector3(d.Width + jamb, jamb, depth), trim, false);
                }
                else
                {
                    Box("JambL", frame, new Vector3(d.Center.x, height * 0.5f, d.Center.z - half), new Vector3(depth, height, jamb), trim, false);
                    Box("JambR", frame, new Vector3(d.Center.x, height * 0.5f, d.Center.z + half), new Vector3(depth, height, jamb), trim, false);
                    Box("Header", frame, new Vector3(d.Center.x, height, d.Center.z), new Vector3(depth, jamb, d.Width + jamb), trim, false);
                }
            }
        }

        // ------------------------------------------------------------------ pencere

        /// <summary>Duvara gömülü pencere: çerçeve + dışarıyı gösteren cam (temaya göre gökyüzü). side: "N","S","E","W".</summary>
        static void Window(string side, float along, float y, float width, float height)
        {
            const float t = HouseLayout.WallThickness;
            var bounds = HouseLayout.Bounds;
            Vector3 pos; Vector3 size; Vector3 glassSize;
            switch (side)
            {
                case "N": pos = new Vector3(along, y, bounds.yMax - t - 0.01f); size = new Vector3(width + 0.1f, height + 0.1f, 0.05f); glassSize = new Vector3(width, height, 0.02f); break;
                case "S": pos = new Vector3(along, y, bounds.yMin + t + 0.01f); size = new Vector3(width + 0.1f, height + 0.1f, 0.05f); glassSize = new Vector3(width, height, 0.02f); break;
                case "E": pos = new Vector3(bounds.xMax - t - 0.01f, y, along); size = new Vector3(0.05f, height + 0.1f, width + 0.1f); glassSize = new Vector3(0.02f, height, width); break;
                default: pos = new Vector3(bounds.xMin + t + 0.01f, y, along); size = new Vector3(0.05f, height + 0.1f, width + 0.1f); glassSize = new Vector3(0.02f, height, width); break;
            }
            Box("WindowFrame", house, pos, size, trim, false);
            Vector3 inward = side == "N" ? Vector3.back : side == "S" ? Vector3.forward : side == "E" ? Vector3.left : Vector3.right;
            var glass = Box("WindowGlass", house, pos + inward * 0.03f, glassSize, windowSky[0], false);
            glass.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            Themed(glass, SurfaceRole.Window, windowSky);
            if (side == "N" || side == "S")
                Box("Mullion", house, pos + inward * 0.045f, new Vector3(0.04f, height, 0.03f), trim, false);
            else
                Box("Mullion", house, pos + inward * 0.045f, new Vector3(0.03f, height, 0.04f), trim, false);
        }

        // ------------------------------------------------------------------ odalar

        static void P(string id, float x, float z, float yaw, float y = 0f, float scale = 1f, bool collider = true, bool shadow = true)
            => AssetPipeline.Place(id, house, new Vector3(x, y, z), yaw, scale, collider, shadow);

        static void Rug(string name, float x, float z, float w, float d, string tint = "#A9B4C2")
        {
            var mat = AssetPipeline.TextureSetMaterial("wool_boucle", new Vector2(w * 1.5f, d * 1.5f), C(tint), 0.8f);
            var rug = Box(name, house, new Vector3(x, 0.007f, z), new Vector3(w, 0.014f, d), mat, false);
            rug.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        static void BuildLiving()
        {
            Rug("RugLiving", -3.2f, -2.9f, 3.2f, 2.3f);
            P("sofa_03", -3.4f, -4.1f, 0f);
            P("throw_pillows_01", -3.9f, -4.0f, 0f, 0.36f, 0.8f, false, false);
            P("modern_coffee_table_01", -3.4f, -2.95f, 0f);
            P("modern_arm_chair_01", -4.78f, -3.0f, 85f);
            P("side_table_01", -5.1f, -4.3f, 0f);
            P("ceramic_vase_01", -5.1f, -4.3f, 0f, 0.55f, 0.8f, false, false);
            P("wooden_display_shelves_01", -5.3f, -1.5f, 90f);
            P("potted_plant_02", -4.9f, -0.95f, 0f);
            P("modern_wooden_cabinet", -0.3f, -4.4f, 0f);
            P("standing_picture_frame_01", -0.6f, -4.4f, 20f, 0.68f, 1f, false, false);
            P("mid_century_lounge_chair", 0.55f, -1.55f, 215f);
            P("wicker_basket_02", 0.9f, -4.3f, 30f);
            P("hanging_picture_frame_01", -3.4f, -4.665f, 0f, 1.55f, 1f, false, false);
            P("hanging_picture_frame_02", -5.465f, -2.7f, 90f, 1.5f, 1f, false, false);
            P("modern_ceiling_lamp_01", -2.6f, -2.8f, 0f, HouseLayout.WallHeight - 1.1f, 1f, false, false);
            Window("S", -0.3f, 1.55f, 1.2f, 1.0f);
        }

        static void BuildStudy()
        {
            P("painted_wooden_table", 3.9f, -4.05f, 0f);
            P("painted_wooden_chair_02", 3.7f, -3.3f, 180f);
            P("wooden_bookshelf_worn", 5.15f, -2.6f, 270f);
            P("vintage_grandfather_clock_01", 5.15f, -1.2f, 270f);
            P("GothicCommode_01", 2.25f, -4.05f, 90f);
            P("potted_plant_04", 2.25f, -4.05f, 0f, 0.58f, 1f, false, false);
            P("Ottoman_01", 3.1f, -1.7f, 15f);
            P("wicker_basket_01", 4.7f, -4.3f, 0f);
            P("hanging_picture_frame_02", 3.9f, -4.665f, 0f, 1.6f, 1f, false, false);
            Window("E", -3.6f, 1.5f, 1.2f, 1.0f);
        }

        static void BuildKitchen()
        {
            P("painted_wooden_cabinet", 1.85f, 3.85f, 180f);
            P("painted_wooden_cabinet", 3.05f, 3.85f, 180f);
            P("painted_wooden_cabinet", 4.25f, 3.85f, 180f);
            P("painted_wooden_cabinet_02", 5.1f, 2.9f, 270f);
            P("steel_frame_shelves_01", 1.3f, 2.3f, 90f);
            P("round_wooden_table_02", 3.1f, 2.2f, 0f);
            P("painted_wooden_chair_01", 2.35f, 2.2f, 90f);
            P("painted_wooden_chair_01", 3.85f, 2.2f, 270f);
            P("painted_wooden_stool", 3.1f, 1.45f, 0f);
            P("vintage_electric_kettle", 3.1f, 2.2f, 30f, 0.75f, 1f, false, false);
            P("jug_01", 2.95f, 2.3f, 0f, 0.75f, 1f, false, false);
            P("pot_enamel_01", 4.9f, 1.2f, 20f);
            P("wooden_bowl_02", 3.25f, 2.1f, 0f, 0.75f, 1f, false, false);
            P("hanging_picture_frame_01", 1.04f, 2.2f, 270f, 1.6f, 1f, false, false);
            Window("N", 3.05f, 1.75f, 1.3f, 0.9f);
        }

        static void BuildBedroom()
        {
            Rug("RugBedroom", -3.5f, 2.5f, 2.4f, 1.9f, "#C9A9A0");
            P("vintage_day_bed", -4.8f, 2.7f, 90f);
            P("throw_pillows_01", -5.1f, 2.6f, 90f, 0.5f, 0.9f, false, false);
            P("painted_wooden_nightstand", -5.05f, 1.45f, 90f);
            P("ClassicNightstand_01", -5.1f, 3.95f, 90f);
            P("GothicCommode_01", -3.2f, 3.9f, 180f);
            P("ornate_mirror_01", -3.2f, 4.14f, 180f, 1.35f, 1f, false, false);
            P("chinese_screen_panels", -2.35f, 1.4f, 35f);
            P("wicker_basket_01", -2.2f, 3.85f, 0f);
            P("hanging_picture_frame_02", -5.465f, 1.0f, 90f, 1.5f, 1f, false, false);
            Window("N", -4.8f, 1.6f, 1.1f, 1.0f);
        }

        static void BuildBath()
        {
            Box("Bathtub", house, new Vector3(-0.55f, 0.28f, 3.8f), new Vector3(1.7f, 0.56f, 0.75f), white, true);
            Box("TubInside", house, new Vector3(-0.55f, 0.57f, 3.8f), new Vector3(1.5f, 0.02f, 0.55f), SceneBuilder.Mat("TubInside", C("#BFD0D4"), smooth: 0.9f), false);
            Box("Faucet", house, new Vector3(0.22f, 0.7f, 3.8f), new Vector3(0.05f, 0.18f, 0.05f), SceneBuilder.Mat("Chrome", C("#C9CED2"), smooth: 0.95f), false);
            Box("VanityCabinet", house, new Vector3(0.72f, 0.38f, 2.2f), new Vector3(0.5f, 0.76f, 0.75f), SceneBuilder.Mat("VanityWood", C("#9B7B5C"), smooth: 0.5f), true);
            Box("Basin", house, new Vector3(0.72f, 0.79f, 2.2f), new Vector3(0.44f, 0.06f, 0.5f), white, false);
            P("ornate_mirror_01", 0.97f, 2.2f, 270f, 1.35f, 0.9f, false, false);
            Box("ToiletBase", house, new Vector3(-1.4f, 0.2f, 2.5f), new Vector3(0.62f, 0.4f, 0.38f), white, true);
            Box("ToiletTank", house, new Vector3(-1.55f, 0.55f, 2.5f), new Vector3(0.2f, 0.36f, 0.4f), white, false);
            P("wooden_stool_02", -1.35f, 1.35f, 0f);
            P("rubber_duck_toy", -0.95f, 3.8f, 200f, 0.57f, 1f, false, false);
            P("wicker_basket_01", 0.7f, 1.2f, 0f);
        }

        static void BuildHall()
        {
            P("painted_wooden_bench", -5.15f, 0f, 90f);
            P("ClassicConsole_01", 1.7f, -0.38f, 0f);
            P("potted_plant_04", 1.7f, -0.38f, 0f, 0.95f, 1f, false, false);
            P("Shelf_01", 1.2f, 0.58f, 180f);
            P("hanging_picture_frame_01", 3.0f, 0.665f, 180f, 1.5f, 1f, false, false);
            P("wall_clock", -3.0f, 0.665f, 180f, 1.9f, 1f, false, false);
            P("painted_wooden_bench", 5.15f, 0f, 270f);
        }

        // ------------------------------------------------------------------ ışık

        static void BuildLights()
        {
            var colors = new Dictionary<RoomKind, string>
            {
                { RoomKind.Living, "#FFD9A8" }, { RoomKind.Kitchen, "#FFF1D6" }, { RoomKind.Bedroom, "#FFC890" },
                { RoomKind.Bath, "#E6F2FF" }, { RoomKind.Study, "#FFD08A" }, { RoomKind.Hall, "#FFE2BC" },
            };
            foreach (var r in HouseLayout.Rooms)
            {
                var go = new GameObject("Light_" + r.Id);
                go.transform.SetParent(house, false);
                go.transform.position = new Vector3(r.Rect.center.x, HouseLayout.WallHeight - 0.35f, r.Rect.center.y);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = C(colors[r.Kind]);
                l.intensity = r.Kind == RoomKind.Hall ? 2.2f : 3.2f;
                l.range = Mathf.Max(r.Rect.width, r.Rect.height) * 0.85f + 1.2f;
                l.shadows = LightShadows.None;
            }
        }

        // ------------------------------------------------------------------ yardımcılar

        /// <summary>Hareket etmeyen oda öğelerini statik batching'e alır (tema yüzeyleri hariç: malzemeleri çalışırken değişir).</summary>
        static void MarkStatic(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.GetComponent<Light>() != null || t.GetComponent<ThemeSurface>() != null) continue;
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
            }
        }

        static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }
    }
}
