using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ARaki.EditorTools
{
    /// <summary>
    /// İçe aktarılan (Poly Haven, CC0) modelleri ve dokuları URP malzemelerine çevirir ve sahneye yerleştirir.
    /// Dokular "diffuse / normal / ARM" olarak gelir; ARM (AO, pürüzlülük, metal) URP'nin beklediği
    /// "metal + pürüzsüzlük" maskesine dönüştürülür.
    /// </summary>
    public static class AssetPipeline
    {
        const string Imported = "Assets/_Project/Art/Imported";
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        // ------------------------------------------------------------ dokular

        public static void PrepareTextures()
        {
            cache.Clear();
            if (!Directory.Exists(Imported)) return;
            bool changed = false;
            foreach (var path in Directory.GetFiles(Imported, "*.jpg", SearchOption.AllDirectories))
            {
                string p = path.Replace('\\', '/');
                string name = Path.GetFileNameWithoutExtension(p);
                var imp = AssetImporter.GetAtPath(p) as TextureImporter;
                if (imp == null) { AssetDatabase.ImportAsset(p); imp = AssetImporter.GetAtPath(p) as TextureImporter; }
                if (imp == null) continue;

                bool isNormal = name.Contains("_nor");
                bool isArm = name.Contains("_arm");
                if (isNormal && imp.textureType != TextureImporterType.NormalMap)
                {
                    imp.textureType = TextureImporterType.NormalMap;
                    imp.SaveAndReimport();
                    changed = true;
                }
                else if (isArm)
                {
                    string maskPath = p.Substring(0, p.Length - 4) + "_mask.png";
                    if (!File.Exists(maskPath)) { MakeMask(p, maskPath); changed = true; }
                }
                else if (!isNormal && imp.anisoLevel < 8)
                {
                    imp.anisoLevel = 8;
                    imp.SaveAndReimport();
                }
            }
            if (changed) AssetDatabase.Refresh();
            foreach (var path in Directory.GetFiles(Imported, "*_mask.png", SearchOption.AllDirectories))
            {
                string p = path.Replace('\\', '/');
                var imp = AssetImporter.GetAtPath(p) as TextureImporter;
                if (imp != null && imp.sRGBTexture)
                {
                    imp.sRGBTexture = false;
                    imp.anisoLevel = 8;
                    imp.SaveAndReimport();
                }
            }
        }

        // ARM: R = AO, G = pürüzlülük, B = metal  ->  maske: R = metal, G = AO, A = pürüzsüzlük
        static void MakeMask(string armPath, string maskPath)
        {
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            src.LoadImage(File.ReadAllBytes(armPath));
            var px = src.GetPixels32();
            var outPx = new Color32[px.Length];
            for (int i = 0; i < px.Length; i++)
                outPx[i] = new Color32(px[i].b, px[i].r, 0, (byte)(255 - px[i].g));
            var dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, true);
            dst.SetPixels32(outPx);
            File.WriteAllBytes(maskPath, dst.EncodeToPNG());
            Object.DestroyImmediate(src);
            Object.DestroyImmediate(dst);
            AssetDatabase.ImportAsset(maskPath);
        }

        static Texture2D Tex(string path)
        {
            if (!File.Exists(path)) return null;
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------ malzemeler

        /// <summary>Bir doku setinden (zemin, duvar, kumaş) URP Lit malzemesi.</summary>
        public static Material TextureSetMaterial(string setId, Vector2 tiling, Color? tint = null, float smoothScale = 1f)
        {
            string key = $"set:{setId}:{tiling}:{tint}";
            if (cache.TryGetValue(key, out var cached) && cached != null) return cached;

            string dir = $"{Imported}/textures/{setId}";
            var mat = SceneBuilder.LoadOrCreateMaterial("Set_" + setId + "_" + Mathf.RoundToInt(tiling.x * 10) + "x" + Mathf.RoundToInt(tiling.y * 10), "Universal Render Pipeline/Lit");
            Apply(mat, Tex($"{dir}/{setId}_diff.jpg"), Tex($"{dir}/{setId}_nor.jpg"), Tex($"{dir}/{setId}_arm_mask.png"), tint ?? Color.white, 0.25f);
            mat.SetTextureScale("_BaseMap", tiling);
            mat.SetTextureScale("_BumpMap", tiling);
            mat.SetTextureScale("_MetallicGlossMap", tiling);
            mat.SetTextureScale("_OcclusionMap", tiling);
            mat.SetFloat("_Smoothness", smoothScale);
            EditorUtility.SetDirty(mat);
            cache[key] = mat;
            return mat;
        }

        /// <summary>Modelin FBX malzeme adına karşılık gelen PBR malzemesi.</summary>
        public static Material ModelMaterial(string modelId, string materialName)
        {
            string key = $"model:{modelId}:{materialName}";
            if (cache.TryGetValue(key, out var cached) && cached != null) return cached;

            string dir = $"{Imported}/models/{modelId}";
            string lower = materialName.ToLowerInvariant();
            Material mat;

            if (lower.Contains("glass") && !lower.Contains("globe"))
            {
                mat = SceneBuilder.LoadOrCreateMaterial($"M_{modelId}_{materialName}", "Universal Render Pipeline/Lit");
                mat.SetColor("_BaseColor", new Color(0.9f, 0.95f, 1f, 0.1f));
                mat.SetFloat("_Smoothness", 0.95f);
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)RenderQueue.Transparent;
            }
            else if (lower.Contains("globe"))
            {
                mat = SceneBuilder.LoadOrCreateMaterial($"M_{modelId}_{materialName}", "Universal Render Pipeline/Lit");
                mat.SetColor("_BaseColor", new Color(1f, 0.95f, 0.85f));
                mat.SetFloat("_Smoothness", 0.7f);
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", SceneBuilder.C("#FFD9A0") * 1.6f);
            }
            else
            {
                var info = ReadMaterialInfo(dir, materialName);
                mat = SceneBuilder.LoadOrCreateMaterial($"M_{modelId}_{materialName}", "Universal Render Pipeline/Lit");
                if (info == null)
                {
                    mat.SetColor("_BaseColor", new Color(0.32f, 0.27f, 0.22f));
                    mat.SetFloat("_Smoothness", 0.3f);
                }
                else
                {
                    Apply(mat, Tex($"{dir}/{info.diff}"), Tex($"{dir}/{info.nor}"), Tex($"{dir}/{(info.arm == null ? null : info.arm.Replace(".jpg", "_mask.png"))}"),
                        info.baseColor, 0.3f);
                }
            }
            EditorUtility.SetDirty(mat);
            cache[key] = mat;
            return mat;
        }

        class Info { public string diff, arm, nor; public Color baseColor = Color.white; }

        static Info ReadMaterialInfo(string dir, string materialName)
        {
            string file = $"{dir}/materials.json";
            if (!File.Exists(file)) return null;
            var json = File.ReadAllText(file);
            // materials.json küçük: {"ad": {"diff": "...", "arm": "...", "nor": "...", "baseColor": [r,g,b,a]}}
            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            if (root == null || !root.TryGetValue(materialName, out var entryObj)) return null;
            var entry = entryObj as Dictionary<string, object>;
            if (entry == null) return null;
            var info = new Info
            {
                diff = entry["diff"] as string,
                arm = entry["arm"] as string,
                nor = entry["nor"] as string,
            };
            if (entry.TryGetValue("baseColor", out var bc) && bc is List<object> list && list.Count >= 3)
                info.baseColor = new Color(ToF(list[0]), ToF(list[1]), ToF(list[2]), 1f);
            return info;
        }

        static float ToF(object o) => System.Convert.ToSingle(o, System.Globalization.CultureInfo.InvariantCulture);

        static void Apply(Material mat, Texture2D diff, Texture2D nor, Texture2D mask, Color tint, float fallbackSmooth)
        {
            mat.SetColor("_BaseColor", tint);
            if (diff != null) mat.SetTexture("_BaseMap", diff);
            if (nor != null)
            {
                mat.SetTexture("_BumpMap", nor);
                mat.SetFloat("_BumpScale", 1f);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (mask != null)
            {
                mat.SetTexture("_MetallicGlossMap", mask);
                mat.SetTexture("_OcclusionMap", mask);
                mat.SetFloat("_Metallic", 1f);
                mat.SetFloat("_Smoothness", 1f);
                mat.SetFloat("_OcclusionStrength", 1f);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.EnableKeyword("_OCCLUSIONMAP");
            }
            else
            {
                mat.SetFloat("_Smoothness", fallbackSmooth);
                mat.SetFloat("_Metallic", 0f);
            }
            mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_ON");
        }

        // ------------------------------------------------------------ yerleştirme

        public struct Placed
        {
            public GameObject Go;
            public Bounds WorldBounds;
        }

        /// <summary>Modeli sahneye koyar, malzemelerini atar, kutu çarpışma ve zemin gölgesi ekler.</summary>
        public static Placed Place(string modelId, Transform parent, Vector3 position, float yaw, float scale = 1f, bool collider = true, bool contactShadow = true)
        {
            string path = $"{Imported}/models/{modelId}/{modelId}.fbx";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogWarning("[Assets] model yok: " + modelId); return default; }

            // Modelin kendi kök dönüşümü (FBX içe aktarma ölçeği ve dönüşü) korunur; konum, yön ve ölçeği bir sarmalayıcı taşır.
            var go = new GameObject(modelId);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;
            PrefabUtility.InstantiatePrefab(prefab, go.transform);

            var renderers = go.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    mats[i] = ModelMaterial(modelId, mats[i] != null ? mats[i].name : modelId);
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
            }

            // Sınırları mesh köşelerinden hesapla (Renderer.bounds editörde güncel olmayabiliyor)
            Vector3 lmin = Vector3.one * float.MaxValue, lmax = Vector3.one * float.MinValue;
            Vector3 wmin = Vector3.one * float.MaxValue, wmax = Vector3.one * float.MinValue;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f));
                    Vector3 world = mf.transform.TransformPoint(corner);
                    Vector3 local = go.transform.InverseTransformPoint(world);
                    lmin = Vector3.Min(lmin, local); lmax = Vector3.Max(lmax, local);
                    wmin = Vector3.Min(wmin, world); wmax = Vector3.Max(wmax, world);
                }
            }
            var bounds = new Bounds((wmin + wmax) * 0.5f, wmax - wmin);

            if (collider)
            {
                var box = go.AddComponent<BoxCollider>();
                box.center = (lmin + lmax) * 0.5f;
                box.size = lmax - lmin;
            }
            if (contactShadow) ContactShadow(parent, bounds);
            return new Placed { Go = go, WorldBounds = bounds };
        }

        /// <summary>Eşyanın altındaki yumuşak koyu leke: parçaların yere "oturmasını" sağlar.</summary>
        public static void ContactShadow(Transform parent, Bounds b, float strength = 0.55f, float spread = 1.35f)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Plane);
            quad.name = "ContactShadow";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(parent, true);
            quad.transform.position = new Vector3(b.center.x, 0.004f, b.center.z);
            quad.transform.localScale = new Vector3(b.size.x * spread / 10f, 1f, b.size.z * spread / 10f);
            var mat = SceneBuilder.LoadOrCreateMaterial("ContactShadow", "Universal Render Pipeline/Unlit");
            mat.SetColor("_BaseColor", new Color(0f, 0f, 0f, strength));
            mat.SetTexture("_BaseMap", SceneBuilder.GlowTexture());
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent - 10;
            mat.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(mat);
            var r = quad.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }

    /// <summary>materials.json için küçük bir JSON okuyucu (sözlük, liste, metin, sayı, null).</summary>
    static class MiniJson
    {
        public static object Parse(string text)
        {
            int i = 0;
            return Value(text, ref i);
        }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            char c = s[i];
            if (c == '{') return Obj(s, ref i);
            if (c == '[') return Arr(s, ref i);
            if (c == '"') return Str(s, ref i);
            if (s.Substring(i).StartsWith("null")) { i += 4; return null; }
            if (s.Substring(i).StartsWith("true")) { i += 4; return true; }
            if (s.Substring(i).StartsWith("false")) { i += 5; return false; }
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+')) i++;
            return double.Parse(s.Substring(start, i - start), System.Globalization.CultureInfo.InvariantCulture);
        }

        static Dictionary<string, object> Obj(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++; Ws(s, ref i);
            if (s[i] == '}') { i++; return d; }
            while (true)
            {
                Ws(s, ref i);
                string k = Str(s, ref i);
                Ws(s, ref i); i++; // :
                d[k] = Value(s, ref i);
                Ws(s, ref i);
                if (s[i] == ',') { i++; continue; }
                i++; return d; // }
            }
        }

        static List<object> Arr(string s, ref int i)
        {
            var l = new List<object>();
            i++; Ws(s, ref i);
            if (s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(Value(s, ref i));
                Ws(s, ref i);
                if (s[i] == ',') { i++; continue; }
                i++; return l; // ]
            }
        }

        static string Str(string s, ref int i)
        {
            i++; var sb = new System.Text.StringBuilder();
            while (s[i] != '"')
            {
                if (s[i] == '\\') { i++; sb.Append(s[i] == 'n' ? '\n' : s[i]); }
                else sb.Append(s[i]);
                i++;
            }
            i++;
            return sb.ToString();
        }
    }
}
