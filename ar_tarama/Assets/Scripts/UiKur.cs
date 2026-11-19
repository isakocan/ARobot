using UnityEngine;
using UnityEngine.UI;

namespace ArTarama
{
    /// <summary>Kod ile uGUI parcalari uretir: koyu, yari saydam, sade.</summary>
    public static class UiKur
    {
        public static readonly Color Panel = new Color(0.035f, 0.055f, 0.075f, 0.68f);
        public static readonly Color PanelAcik = new Color(1f, 1f, 1f, 0.08f);
        public static readonly Color Vurgu = new Color(0.27f, 0.87f, 0.79f, 1f);
        public static readonly Color Uyari = new Color(1f, 0.73f, 0.30f, 1f);
        public static readonly Color Kirmizi = new Color(1f, 0.34f, 0.34f, 1f);
        public static readonly Color Soluk = new Color(0.78f, 0.84f, 0.88f, 0.85f);
        public static readonly Color Beyaz = new Color(0.95f, 0.97f, 0.98f, 1f);

        static Font font;
        static Sprite yuvarlak, daire;

        public static Font Yazi
        {
            get
            {
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        public static Sprite Yuvarlak()
        {
            if (yuvarlak != null) return yuvarlak;
            const int n = 64; const float r = 20f;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = Mathf.Max(r - x - 0.5f, x + 0.5f - (n - r), 0f);
                    float dy = Mathf.Max(r - y - 0.5f, y + 0.5f - (n - r), 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(r - d + 0.5f)));
                }
            t.Apply();
            yuvarlak = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
            return yuvarlak;
        }

        public static Sprite Daire()
        {
            if (daire != null) return daire;
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f));
                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(n / 2f - d)));
                }
            t.Apply();
            daire = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            return daire;
        }

        public static RectTransform Obj(string ad, Transform ebeveyn)
        {
            var go = new GameObject(ad, typeof(RectTransform));
            go.transform.SetParent(ebeveyn, false);
            return (RectTransform)go.transform;
        }

        public static Image Kutu(Transform ebeveyn, string ad, Color renk, bool yuvarlakKose = true)
        {
            var rt = Obj(ad, ebeveyn);
            var im = rt.gameObject.AddComponent<Image>();
            im.color = renk;
            im.raycastTarget = false;
            if (yuvarlakKose) { im.sprite = Yuvarlak(); im.type = Image.Type.Sliced; }
            return im;
        }

        public static Image Nokta(Transform ebeveyn, string ad, Color renk, float boyut)
        {
            var im = Kutu(ebeveyn, ad, renk, false);
            im.sprite = Daire();
            im.rectTransform.sizeDelta = new Vector2(boyut, boyut);
            return im;
        }

        public static Text Metin(Transform ebeveyn, string ad, string s, int boyut, Color renk,
            TextAnchor hiza = TextAnchor.MiddleLeft, FontStyle stil = FontStyle.Normal)
        {
            var rt = Obj(ad, ebeveyn);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Yazi; t.text = s; t.fontSize = boyut; t.color = renk;
            t.alignment = hiza; t.fontStyle = stil;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            return t;
        }

        public static void Ankor(RectTransform rt, Vector2 min, Vector2 max, Vector2 ofsMin, Vector2 ofsMax)
        {
            rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = ofsMin; rt.offsetMax = ofsMax;
        }

        public static Button Dugme(Transform ebeveyn, string ad, string etiket, out Image arka, out Text yazi)
        {
            arka = Kutu(ebeveyn, ad, PanelAcik);
            arka.raycastTarget = true;
            var b = arka.gameObject.AddComponent<Button>();
            b.targetGraphic = arka;
            var cb = b.colors;
            cb.normalColor = Color.white; cb.highlightedColor = Color.white;
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f); cb.selectedColor = Color.white;
            b.colors = cb;
            yazi = Metin(arka.transform, "Yazi", etiket, 30, Beyaz, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ankor(yazi.rectTransform, Vector2.zero, Vector2.one, new Vector2(8, 4), new Vector2(-8, -4));
            return b;
        }
    }
}
