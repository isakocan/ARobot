using System.Collections.Generic;
using ARaki.AR;
using ARaki.Game;
using ARaki.Theming;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ARaki.EditorTools
{
    /// <summary>
    /// Bulguların AR şablonları: 3 tema × 5 tür = 15 küçük "oyuncak" model (ARaki/Toon gölgelendiricisiyle, hologram değil).
    /// Tüm şekiller ilkel biçimlerden kodla kurulur; sahnede pasif durur, Expedition çıkış anında kopyalar.
    /// </summary>
    public static partial class SceneBuilder
    {
        static GameObject[] BuildFindTemplates(Transform arRoot)
        {
            var holder = new GameObject("FindTemplates").transform;
            holder.SetParent(arRoot, false);
            var result = new GameObject[5];
            for (int theme = 0; theme < 1; theme++)
                for (int kind = 0; kind < 5; kind++)
                {
                    var go = BuildFind((ThemeId)theme, (FindKind)kind, holder);
                    go.SetActive(false);
                    result[theme * 5 + kind] = go;
                }
            holder.gameObject.SetActive(true);
            return result;
        }

        /// <summary>Gölge: toplanmaz, kaçılır. Koyu, yarı saydam, kızıl gözlü.</summary>
        static GameObject BuildGhostTemplate(Transform parent)
        {
            var root = new GameObject("Ghost_Template");
            root.transform.SetParent(parent, false);
            Prim(root.transform, PrimitiveType.Quad, "Shadow", new Vector3(0f, 0.003f, 0f), Vector3.one * 0.5f, FindShadowMaterial(), new Vector3(90f, 0f, 0f));

            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);
            var cloth = Toon("GhostCloth", "#2A2440", "#0C0A18", "#7C86FF", "#3A3A90", 0.5f, 0.88f, 1f);
            var cloth2 = Toon("GhostClothDark", "#17132A", "#05040C", "#5A62E0", rimStrength: 1f);
            var eyeMat = Toon("GhostEye", "#FF4040", "#A01010", "#FFFFFF", "#FF2020", 1.6f, rimStrength: 0f);

            Ball(body, "Head", new Vector3(0f, 0.26f, 0f), new Vector3(0.17f, 0.2f, 0.16f), cloth);
            var skirt = new GameObject("Tail").transform;
            skirt.SetParent(body, false);
            MeshPart(skirt, "Skirt", ARaki.AR.ProceduralMeshes.Cone(0.15f, 0.03f, 0.3f, 16), new Vector3(0f, 0.02f, 0f), Vector3.zero, cloth2);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI * 2f / 6f;
                Ball(skirt, "Rag", new Vector3(Mathf.Sin(a) * 0.12f, 0.04f, Mathf.Cos(a) * 0.12f), new Vector3(0.06f, 0.1f, 0.06f), cloth);
            }
            foreach (float side in new[] { -1f, 1f })
                Prim(body, PrimitiveType.Sphere, "Arm", new Vector3(0.14f * side, 0.14f, 0.05f), new Vector3(0.045f, 0.16f, 0.045f), cloth2, new Vector3(0f, 0f, 18f * -side));

            var eyes = new List<Object>();
            foreach (float side in new[] { -1f, 1f })
            {
                var e = Ball(body, "Eye", new Vector3(0.05f * side, 0.285f, 0.12f), new Vector3(0.036f, 0.05f, 0.02f), eyeMat);
                eyes.Add(e.GetComponent<Renderer>());
            }

            var visual = root.AddComponent<GhostVisual>();
            var so = new SerializedObject(visual);
            so.FindProperty("body").objectReferenceValue = body;
            so.FindProperty("tail").objectReferenceValue = skirt;
            SetArray(so.FindProperty("eyes"), eyes);
            so.ApplyModifiedPropertiesWithoutUndo();
            root.transform.localScale = Vector3.one * 1.25f;
            root.SetActive(false);
            return root;
        }

        // ------------------------------------------------------------------ malzeme ve parça yardımcıları

        static Material Toon(string name, string baseHex, string shadeHex, string rimHex = "#FFFFFF", string emissionHex = null, float emission = 1f, float alpha = 1f, float rimStrength = 0.5f)
        {
            var mat = LoadOrCreateMaterial("Toon_" + name, "ARaki/Toon");
            var b = C(baseHex); b.a = alpha;
            mat.SetColor("_BaseColor", b);
            mat.SetColor("_ShadeColor", C(shadeHex));
            mat.SetColor("_RimColor", C(rimHex));
            mat.SetFloat("_RimStrength", rimStrength);
            mat.SetColor("_EmissionColor", emissionHex != null ? C(emissionHex) * emission : Color.black);
            bool transparent = alpha < 0.999f;
            mat.SetFloat("_SrcBlend", transparent ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
            mat.SetFloat("_DstBlend", transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            mat.SetFloat("_ZWrite", transparent ? 0f : 1f);
            mat.renderQueue = transparent ? 3000 : 2000;
            mat.SetOverrideTag("RenderType", transparent ? "Transparent" : "Opaque");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static GameObject Prim(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat, Vector3? euler = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        static GameObject MeshPart(Transform parent, string name, Mesh mesh, Vector3 pos, Vector3 euler, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        static Material FindShadowMaterial()
        {
            var mat = LoadOrCreateMaterial("FindShadow", "Universal Render Pipeline/Unlit");
            mat.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0.55f));
            mat.SetTexture("_BaseMap", GlowTexture());
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = 2990;
            mat.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Gövde için küre: ölçek + isteğe bağlı parlama.</summary>
        static GameObject Ball(Transform p, string name, Vector3 pos, Vector3 scale, Material m) => Prim(p, PrimitiveType.Sphere, name, pos, scale, m);

        /// <summary>İki gözlü yüz: beyaz küre + siyah göz bebeği (+Z yönüne bakar).</summary>
        static Transform[] Face(Transform body, Vector3 center, float spread, float size, Material eyeWhite, Material pupil)
        {
            var eyes = new List<Transform>();
            foreach (float side in new[] { -1f, 1f })
            {
                var e = Ball(body, "Eye", center + new Vector3(spread * side, 0f, 0f), Vector3.one * size, eyeWhite);
                var pu = Ball(e.transform, "Pupil", new Vector3(0f, 0f, 0.36f), Vector3.one * 0.55f, pupil);
                _ = pu;
                eyes.Add(e.transform);
            }
            return eyes.ToArray();
        }

        static Mesh quadMesh;
        static Mesh WingMesh()
        {
            if (quadMesh != null) return quadMesh;
            quadMesh = new Mesh { name = "Wing" };
            quadMesh.vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(0.11f, 0.05f, 0.03f), new Vector3(0.13f, -0.04f, -0.01f), new Vector3(0.02f, -0.06f, -0.02f) };
            quadMesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
            quadMesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            quadMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            return quadMesh;
        }

        // ------------------------------------------------------------------ şablon iskeleti

        static GameObject BuildFind(ThemeId theme, FindKind kind, Transform parent)
        {
            var root = new GameObject($"Find_{theme}_{kind}");
            root.transform.SetParent(parent, false);

            var shadow = Prim(root.transform, PrimitiveType.Quad, "Shadow", new Vector3(0f, 0.003f, 0f), Vector3.one * (kind == FindKind.Legend ? 0.55f : 0.34f), FindShadowMaterial(), new Vector3(90f, 0f, 0f));
            shadow.GetComponent<MeshRenderer>().sharedMaterial = FindShadowMaterial();

            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);

            var parts = new Parts();
            switch (theme)
            {
                default: BuildSafari(kind, body, parts); break;
            }

            var visual = root.AddComponent<FindVisual>();
            var so = new SerializedObject(visual);
            so.FindProperty("body").objectReferenceValue = body;
            so.FindProperty("kind").enumValueIndex = (int)kind;
            so.FindProperty("hoverHeight").floatValue = parts.Hover;
            so.FindProperty("bobAmount").floatValue = parts.Bob;
            SetArray(so.FindProperty("wings"), parts.Wings);
            SetArray(so.FindProperty("eyes"), parts.Eyes);
            SetArray(so.FindProperty("spinners"), parts.Spinners);
            SetArray(so.FindProperty("glowRenderers"), parts.Glow);

            // nadir bulgular için zemin halesi
            var aura = Prim(root.transform, PrimitiveType.Quad, "Aura", new Vector3(0f, 0.004f, 0f), Vector3.one * 0.6f,
                AdditiveMat("FindAura", C("#FFD98A") * 1.1f, GlowTexture()), new Vector3(90f, 0f, 0f));
            aura.SetActive(false);
            so.FindProperty("tierAura").objectReferenceValue = aura;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (kind == FindKind.Legend) root.transform.localScale = Vector3.one * 1.35f;
            return root;
        }

        class Parts
        {
            public float Hover = 0.02f, Bob = 0.012f;
            public readonly List<Object> Wings = new List<Object>(), Eyes = new List<Object>(), Spinners = new List<Object>(), Glow = new List<Object>();
        }

        static void SetArray(SerializedProperty prop, List<Object> items)
        {
            prop.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        static void AddRange(List<Object> list, IEnumerable<Transform> items) { foreach (var t in items) list.Add(t); }

        // ------------------------------------------------------------------ SAFARİ

        static void BuildSafari(FindKind kind, Transform body, Parts p)
        {
            var white = Toon("EyeWhite", "#FFFFFF", "#D8D8E0", rimStrength: 0f);
            var black = Toon("EyeBlack", "#1B1620", "#000000", rimStrength: 0f);
            switch (kind)
            {
                case FindKind.Shy:        // POFUDUK: tüylü, yosun yeşili top
                    {
                        var fur = Toon("PofudukFur", "#8FD16A", "#3F7D4A", "#EAFF9E", "#5CFF80", 0.25f);
                        var fur2 = Toon("PofudukTuft", "#B7E88A", "#5E9E4E", "#F4FFC0", rimStrength: 0.7f);
                        var ear = Toon("PofudukEar", "#5FBF5A", "#2E6B3A", "#C8FF9A");
                        var cheek = Toon("PofudukCheek", "#F59EB5", "#B8667F");
                        var foot = Toon("PofudukFoot", "#8B6B43", "#4A3622");
                        var b = Ball(body, "Fur", new Vector3(0f, 0.095f, 0f), new Vector3(0.2f, 0.17f, 0.19f), fur);
                        p.Glow.Add(b.GetComponent<Renderer>());
                        for (int i = 0; i < 9; i++)
                        {
                            float a = i * 2.4f;
                            var t = Ball(body, "Tuft", new Vector3(Mathf.Sin(a) * 0.075f, 0.14f + (i % 3) * 0.02f, Mathf.Cos(a) * 0.065f - 0.01f), Vector3.one * 0.06f, fur2);
                            _ = t;
                        }
                        foreach (float side in new[] { -1f, 1f })
                        {
                            MeshPart(body, "Ear", ARaki.AR.ProceduralMeshes.Cone(0.032f, 0.004f, 0.09f, 8), new Vector3(0.06f * side, 0.17f, 0f), new Vector3(0f, 0f, -22f * side), ear);
                            Ball(body, "Cheek", new Vector3(0.075f * side, 0.075f, 0.082f), new Vector3(0.035f, 0.022f, 0.012f), cheek);
                            Ball(body, "Foot", new Vector3(0.05f * side, 0.012f, 0.05f), new Vector3(0.06f, 0.035f, 0.08f), foot);
                        }
                        AddRange(p.Eyes, Face(body, new Vector3(0f, 0.115f, 0.085f), 0.05f, 0.055f, white, black));
                        p.Hover = 0.0f; p.Bob = 0.006f;
                        break;
                    }
                case FindKind.Lure:       // ATEŞBÖCEĞİ
                    {
                        var shell = Toon("FireflyBody", "#6A4B2E", "#2E1F12", "#FFE9B0");
                        var glow = Toon("FireflyGlow", "#FFE55C", "#FFB22E", "#FFFFFF", "#FFE066", 2.4f, rimStrength: 1f);
                        var wing = Toon("FireflyWing", "#FFFFFF", "#CFE8FF", "#FFFFFF", "#CFEFFF", 0.4f, 0.55f);
                        Ball(body, "Thorax", new Vector3(0f, 0f, 0.025f), new Vector3(0.05f, 0.045f, 0.07f), shell);
                        var abd = Ball(body, "Abdomen", new Vector3(0f, -0.005f, -0.045f), new Vector3(0.07f, 0.065f, 0.085f), glow);
                        p.Glow.Add(abd.GetComponent<Renderer>());
                        Ball(body, "Head", new Vector3(0f, 0.005f, 0.075f), Vector3.one * 0.042f, shell);
                        AddRange(p.Eyes, Face(body, new Vector3(0f, 0.012f, 0.1f), 0.014f, 0.016f, white, black));
                        foreach (float side in new[] { -1f, 1f })
                        {
                            var w = MeshPart(body, "Wing", WingMesh(), new Vector3(0.02f * side, 0.025f, 0.01f), new Vector3(0f, side < 0 ? 180f : 0f, 0f), wing);
                            p.Wings.Add(w.transform);
                        }
                        p.Hover = 0.22f; p.Bob = 0.025f;
                        break;
                    }
                case FindKind.Hidden:     // GİZLİ YUVA: dallardan yuva ve yumurtalar
                    {
                        var twig = Toon("NestTwig", "#8B6B43", "#4A3622");
                        var egg1 = Toon("NestEggA", "#BFE3F2", "#6FA0B8", "#FFFFFF", "#9FE0FF", 0.5f);
                        var egg2 = Toon("NestEggB", "#F7D9E6", "#C58BA6", "#FFFFFF", "#FFC9E6", 0.5f);
                        var egg3 = Toon("NestEggC", "#F5F0B8", "#B8B06A", "#FFFFFF", "#FFF59A", 0.5f);
                        for (int i = 0; i < 12; i++)
                        {
                            float a = i * Mathf.PI * 2f / 12f;
                            var tw = Prim(body, PrimitiveType.Cylinder, "Twig", new Vector3(Mathf.Sin(a) * 0.1f, 0.025f + (i % 2) * 0.012f, Mathf.Cos(a) * 0.1f), new Vector3(0.012f, 0.06f, 0.012f), twig,
                                new Vector3(90f, a * Mathf.Rad2Deg + 90f, 0f));
                            _ = tw;
                        }
                        var e1 = Ball(body, "Egg", new Vector3(0.03f, 0.05f, 0.015f), new Vector3(0.055f, 0.07f, 0.055f), egg1);
                        var e2 = Ball(body, "Egg", new Vector3(-0.035f, 0.045f, 0.0f), new Vector3(0.05f, 0.065f, 0.05f), egg2);
                        var e3 = Ball(body, "Egg", new Vector3(0.0f, 0.045f, -0.04f), new Vector3(0.05f, 0.065f, 0.05f), egg3);
                        p.Glow.Add(e1.GetComponent<Renderer>()); p.Glow.Add(e2.GetComponent<Renderer>()); p.Glow.Add(e3.GetComponent<Renderer>());
                        p.Hover = 0f; p.Bob = 0.004f;
                        break;
                    }
                case FindKind.Relic:      // ESKİ TOHUM
                    {
                        var seed = Toon("SeedShell", "#C79A5B", "#6B4620", "#FFE9B0", "#9CFF6A", 0.3f);
                        var groove = Toon("SeedGroove", "#7A4F24", "#3A210C");
                        var leaf = Toon("SeedLeaf", "#6FD05A", "#2F7A3A", "#E8FFA0", "#8CFF70", 0.4f);
                        var s = Ball(body, "Seed", new Vector3(0f, 0.085f, 0f), new Vector3(0.12f, 0.17f, 0.1f), seed);
                        p.Glow.Add(s.GetComponent<Renderer>());
                        Ball(body, "Groove", new Vector3(0f, 0.085f, 0.04f), new Vector3(0.012f, 0.15f, 0.012f), groove);
                        Prim(body, PrimitiveType.Cylinder, "Sprout", new Vector3(0f, 0.2f, 0f), new Vector3(0.012f, 0.04f, 0.012f), leaf);
                        foreach (float side in new[] { -1f, 1f })
                            p.Wings.Add(MeshPart(body, "Leaf", WingMesh(), new Vector3(0.006f * side, 0.235f, 0f), new Vector3(0f, side < 0 ? 180f : 0f, 35f), leaf).transform);
                        p.Hover = 0.0f; p.Bob = 0.008f;
                        break;
                    }
                default:                  // KÖK ANA
                    {
                        var core = Toon("RootCore", "#F2D96A", "#9A7A2A", "#FFFFFF", "#FFE88A", 0.45f, rimStrength: 0.6f);
                        var ring = Toon("RootRing", "#8FE35A", "#2E7A3A", "#EAFF9E", "#9CFF6A", 1.0f);
                        var leaf = Toon("RootLeaf", "#5FC05A", "#2A6A34", "#C8FF9A", "#7CFF6A", 0.5f);
                        var c = Ball(body, "Core", new Vector3(0f, 0.2f, 0f), Vector3.one * 0.22f, core);
                        p.Glow.Add(c.GetComponent<Renderer>());
                        var orbit = new GameObject("Orbit").transform;
                        orbit.SetParent(body, false);
                        orbit.localPosition = new Vector3(0f, 0.2f, 0f);
                        for (int i = 0; i < 6; i++)
                        {
                            float a = i * Mathf.PI * 2f / 6f;
                            Ball(orbit, "Orb", new Vector3(Mathf.Sin(a) * 0.2f, Mathf.Sin(a * 2f) * 0.03f, Mathf.Cos(a) * 0.2f), Vector3.one * 0.04f, ring);
                        }
                        p.Spinners.Add(orbit);
                        var leaves = new GameObject("Leaves").transform;
                        leaves.SetParent(body, false);
                        leaves.localPosition = new Vector3(0f, 0.07f, 0f);
                        for (int i = 0; i < 5; i++)
                            MeshPart(leaves, "Leaf", WingMesh(), Vector3.zero, new Vector3(-40f, i * 72f, 0f), leaf).transform.localScale = Vector3.one * 1.6f;
                        AddRange(p.Eyes, Face(body, new Vector3(0f, 0.2f, 0.105f), 0.05f, 0.05f, white, black));
                        p.Hover = 0.1f; p.Bob = 0.02f;
                        break;
                    }
            }
        }
    }
}
