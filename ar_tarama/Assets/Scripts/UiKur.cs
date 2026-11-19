using System;
using UnityEngine;
using UnityEngine.UI;

namespace ArTarama
{
    /// <summary>Tasarim belirteclari ve kod ile uretilen uGUI parcalari (koyu cam paneller, tek vurgu rengi).</summary>
    public static class UiKur
    {
        // ---- renk belirteclari ----
        public const int Title = 46, Body = 32, Caption = 26;   // tek yazi olcegi (1080 genislikli referans)
        public static readonly Color Panel = new Color(16 / 255f, 18 / 255f, 22 / 255f, 0.85f);
        public static readonly Color PanelKoyu = new Color(16 / 255f, 18 / 255f, 22 / 255f, 0.92f);
        public static readonly Color Cam = new Color(1f, 1f, 1f, 0.07f);
        public static readonly Color Kenar = new Color(1f, 1f, 1f, 0.11f);
        public static readonly Color Vurgu = new Color(0x43 / 255f, 0xD9 / 255f, 0xBE / 255f, 1f);
        public static readonly Color VurguYumusak = new Color(0x43 / 255f, 0xD9 / 255f, 0xBE / 255f, 0.18f);
        public static readonly Color Uyari = new Color(0xF2 / 255f, 0xA5 / 255f, 0x41 / 255f, 1f);
        public static readonly Color Hata = new Color(0xEB / 255f, 0x57 / 255f, 0x57 / 255f, 1f);
        public static readonly Color MetinRenk = new Color(0xED / 255f, 0xEB / 255f, 0xE6 / 255f, 1f);
        public static readonly Color Soluk = new Color(0x9A / 255f, 0xA0 / 255f, 0xA6 / 255f, 1f);
        public static readonly Color Koyu = new Color(0.05f, 0.07f, 0.08f, 1f);
        public static readonly Color Siyah60 = new Color(0f, 0f, 0f, 0.60f);

        static Font font;
        static Sprite yuvarlak, yuvarlakKenar, daire, halka, ok, tik, chevronAsagi, chevronSag, carpi, ucgen, yenile, uyariS, bilgiS, bekleS;

        public static Font Yazi
        {
            get
            {
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        // ---------------- sprite uretimi ----------------
        delegate float Alfa(float x, float y); // 0..1 koordinat, 0..1 alfa

        static float Seg(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        static Sprite Uret(int n, Func<float, float, float> f, Vector4 sinir = default, float ppu = 100f)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float a = Mathf.Clamp01(f((x + 0.5f), (y + 0.5f)));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f + 0.5f));
                }
            t.SetPixels32(px); t.Apply(false, true);
            return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect, sinir);
        }

        public static Sprite Yuvarlak()
        {
            if (yuvarlak != null) return yuvarlak;
            const int n = 64; const float r = 20f;
            yuvarlak = Uret(n, (x, y) =>
            {
                float dx = Mathf.Max(r - x, x - (n - r), 0f), dy = Mathf.Max(r - y, y - (n - r), 0f);
                return r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f;
            }, new Vector4(r, r, r, r));
            return yuvarlak;
        }

        public static Sprite YuvarlakKenar()
        {
            if (yuvarlakKenar != null) return yuvarlakKenar;
            const int n = 64; const float r = 20f;
            yuvarlakKenar = Uret(n, (x, y) =>
            {
                float dx = Mathf.Max(r - x, x - (n - r), 0f), dy = Mathf.Max(r - y, y - (n - r), 0f);
                float d = r - Mathf.Sqrt(dx * dx + dy * dy); // ic mesafe
                float dis = Mathf.Clamp01(d + 0.5f);
                float ic = Mathf.Clamp01(d - 1.5f + 0.5f);
                return dis - ic;
            }, new Vector4(r, r, r, r));
            return yuvarlakKenar;
        }

        public static Sprite Daire()
        {
            if (daire != null) return daire;
            const int n = 64;
            daire = Uret(n, (x, y) => n / 2f - Vector2.Distance(new Vector2(x, y), new Vector2(n / 2f, n / 2f)) + 0.5f);
            return daire;
        }

        public static Sprite Halka()
        {
            if (halka != null) return halka;
            const int n = 192;
            halka = Uret(n, (x, y) =>
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(n / 2f, n / 2f));
                float dis = Mathf.Clamp01(n / 2f - d + 0.5f);
                float ic = Mathf.Clamp01(n / 2f - 22f - d + 0.5f);
                return dis - ic;
            });
            return halka;
        }

        /// <summary>Saga bakan kalin ok (govde + uc).</summary>
        public static Sprite Ok()
        {
            if (ok != null) return ok;
            const int n = 128;
            ok = Uret(n, (x, y) =>
            {
                var p = new Vector2(x, y);
                float k = 9f;
                float d = Mathf.Min(Seg(p, new Vector2(18, 64), new Vector2(106, 64)),
                    Mathf.Min(Seg(p, new Vector2(64, 106), new Vector2(108, 64)), Seg(p, new Vector2(64, 22), new Vector2(108, 64))));
                return k - d + 0.5f;
            });
            return ok;
        }

        public static Sprite Tik()
        {
            if (tik != null) return tik;
            const int n = 96;
            tik = Uret(n, (x, y) =>
            {
                var p = new Vector2(x, y);
                float d = Mathf.Min(Seg(p, new Vector2(18, 50), new Vector2(40, 28)), Seg(p, new Vector2(40, 28), new Vector2(80, 72)));
                return 8f - d + 0.5f;
            });
            return tik;
        }

        public static Sprite ChevronAsagi()
        {
            if (chevronAsagi != null) return chevronAsagi;
            const int n = 64;
            chevronAsagi = Uret(n, (x, y) =>
            {
                var p = new Vector2(x, y);
                float d = Mathf.Min(Seg(p, new Vector2(14, 40), new Vector2(32, 22)), Seg(p, new Vector2(32, 22), new Vector2(50, 40)));
                return 4.2f - d + 0.5f;
            });
            return chevronAsagi;
        }

        public static Sprite ChevronSag()
        {
            if (chevronSag != null) return chevronSag;
            const int n = 64;
            chevronSag = Uret(n, (x, y) =>
            {
                var p = new Vector2(x, y);
                float d = Mathf.Min(Seg(p, new Vector2(24, 14), new Vector2(42, 32)), Seg(p, new Vector2(42, 32), new Vector2(24, 50)));
                return 4.2f - d + 0.5f;
            });
            return chevronSag;
        }

        public static Sprite Carpi()
        {
            if (carpi != null) return carpi;
            const int n = 64;
            carpi = Uret(n, (x, y) =>
            {
                var p = new Vector2(x, y);
                float d = Mathf.Min(Seg(p, new Vector2(16, 16), new Vector2(48, 48)), Seg(p, new Vector2(16, 48), new Vector2(48, 16)));
                return 4f - d + 0.5f;
            });
            return carpi;
        }

        /// <summary>Yukari bakan dolu ucgen (pusula isaretcisi, yon oku).</summary>
        public static Sprite Ucgen()
        {
            if (ucgen != null) return ucgen;
            const int n = 64;
            ucgen = Uret(n, (x, y) =>
            {
                // tepe (32,58), taban (10,8)-(54,8)
                float ty = (y - 8f) / 50f; if (ty < 0f || ty > 1f) return 0f;
                float yarim = (1f - ty) * 22f;
                return yarim - Mathf.Abs(x - 32f) + 0.5f;
            });
            return ucgen;
        }


        /// <summary>Uyari ucgeni (icinde unlem oyugu).</summary>
        public static Sprite UyariIkon()
        {
            if (uyariS != null) return uyariS;
            const int n = 96;
            uyariS = Uret(n, (x, y) =>
            {
                // tepe (48,84), taban (10,16)-(86,16); yuvarlatilmis kenar yaklasigi icin mesafe tabanli
                float ty = (y - 16f) / 68f; if (ty < 0f || ty > 1f) return 0f;
                float yarim = (1f - ty) * 38f;
                float tri = Mathf.Clamp01(yarim - Mathf.Abs(x - 48f) - 1f + 0.5f);
                float cubuk = Mathf.Clamp01(5f - Seg(new Vector2(x, y), new Vector2(48, 34), new Vector2(48, 56)) + 0.5f);
                float nokta = Mathf.Clamp01(5.5f - Vector2.Distance(new Vector2(x, y), new Vector2(48, 25)) + 0.5f);
                return tri * (1f - Mathf.Max(cubuk, nokta));
            });
            return uyariS;
        }

        /// <summary>Bilgi dairesi (icinde 'i' oyugu).</summary>
        public static Sprite BilgiIkon()
        {
            if (bilgiS != null) return bilgiS;
            const int n = 96;
            bilgiS = Uret(n, (x, y) =>
            {
                float d = Mathf.Clamp01(46f - Vector2.Distance(new Vector2(x, y), new Vector2(48, 48)) + 0.5f);
                float cubuk = Mathf.Clamp01(5.5f - Seg(new Vector2(x, y), new Vector2(48, 28), new Vector2(48, 50)) + 0.5f);
                float nokta = Mathf.Clamp01(6f - Vector2.Distance(new Vector2(x, y), new Vector2(48, 68)) + 0.5f);
                return d * (1f - Mathf.Max(cubuk, nokta));
            });
            return bilgiS;
        }

        /// <summary>Saat (bekle): halka + iki ibre.</summary>
        public static Sprite BekleIkon()
        {
            if (bekleS != null) return bekleS;
            const int n = 96;
            bekleS = Uret(n, (x, y) =>
            {
                var p = new Vector2(x, y); float d = Vector2.Distance(p, new Vector2(48, 48));
                float halka2 = Mathf.Clamp01(5f - Mathf.Abs(d - 38f) + 0.5f);
                float ib = Mathf.Max(Mathf.Clamp01(4.5f - Seg(p, new Vector2(48, 48), new Vector2(48, 72)) + 0.5f), Mathf.Clamp01(4.5f - Seg(p, new Vector2(48, 48), new Vector2(64, 40)) + 0.5f));
                return Mathf.Max(halka2, ib);
            });
            return bekleS;
        }

        public static Sprite Yenile()
        {
            if (yenile != null) return yenile;
            const int n = 96;
            yenile = Uret(n, (x, y) =>
            {
                var p = new Vector2(x, y); var c = new Vector2(48, 48);
                float d = Vector2.Distance(p, c);
                float ang = Mathf.Atan2(y - 48f, x - 48f) * Mathf.Rad2Deg; // -180..180
                float halka2 = 6f - Mathf.Abs(d - 28f) + 0.5f;
                if (ang > 20f && ang < 70f) halka2 = 0f; // bosluk
                float ucD = 6f - Seg(p, new Vector2(66, 82 - 22), new Vector2(76, 58)) + 0.5f; // ok ucu
                float ucD2 = 6f - Seg(p, new Vector2(76, 58), new Vector2(50, 62)) + 0.5f;
                return Mathf.Max(halka2, Mathf.Max(ucD, ucD2));
            });
            return yenile;
        }

        // ---------------- uGUI yardimcilari ----------------
        public static RectTransform Obj(string ad, Transform ebeveyn)
        {
            var go = new GameObject(ad, typeof(RectTransform));
            go.transform.SetParent(ebeveyn, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Yuvarlak koseli kutu; kenar=true ise ince acik kenar cizgisi ekler.</summary>
        public static Image Kutu(Transform ebeveyn, string ad, Color renk, bool kenar = false, bool yuvarlakKose = true)
        {
            var rt = Obj(ad, ebeveyn);
            var im = rt.gameObject.AddComponent<Image>();
            im.color = renk; im.raycastTarget = false;
            if (yuvarlakKose) { im.sprite = Yuvarlak(); im.type = Image.Type.Sliced; }
            if (kenar)
            {
                var k = Kutu(rt, "Kenar", Kenar, false, false);
                k.sprite = YuvarlakKenar(); k.type = Image.Type.Sliced;
                Tam(k.rectTransform);
            }
            return im;
        }

        public static Image Simge(Transform ebeveyn, string ad, Sprite sp, Color renk, float boyut)
        {
            var im = Kutu(ebeveyn, ad, renk, false, false);
            im.sprite = sp; im.preserveAspect = true;
            im.rectTransform.sizeDelta = new Vector2(boyut, boyut);
            return im;
        }

        public static Image Nokta(Transform ebeveyn, string ad, Color renk, float boyut)
        {
            return Simge(ebeveyn, ad, Daire(), renk, boyut);
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
            t.lineSpacing = 1.05f;
            t.raycastTarget = false;
            return t;
        }

        public static void Ankor(RectTransform rt, Vector2 min, Vector2 max, Vector2 ofsMin, Vector2 ofsMax)
        {
            rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = ofsMin; rt.offsetMax = ofsMax;
        }

        public static void Tam(RectTransform rt, float pay = 0f)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pay, pay); rt.offsetMax = new Vector2(-pay, -pay);
        }

        /// <summary>Sabit boyutlu oge: ankor noktasi + pivot + boyut + konum.</summary>
        public static void Yerles(RectTransform rt, Vector2 ankor, Vector2 pivot, Vector2 boyut, Vector2 konum)
        {
            rt.anchorMin = rt.anchorMax = ankor; rt.pivot = pivot; rt.sizeDelta = boyut; rt.anchoredPosition = konum;
        }

        public enum DugmeStili { Birincil, Ikincil, Ghost, Tehlike }

        public static Button Dugme(Transform ebeveyn, string ad, string etiket, DugmeStili stil, out Image arka, out Text yazi, int punto = 34)
        {
            Color bg, fg;
            switch (stil)
            {
                case DugmeStili.Birincil: bg = Vurgu; fg = Koyu; break;
                case DugmeStili.Tehlike: bg = new Color(Hata.r, Hata.g, Hata.b, 0.22f); fg = new Color(1f, 0.62f, 0.62f); break;
                case DugmeStili.Ghost: bg = new Color(1f, 1f, 1f, 0.0f); fg = MetinRenk; break;
                default: bg = new Color(1f, 1f, 1f, 0.10f); fg = MetinRenk; break;
            }
            arka = Kutu(ebeveyn, ad, bg, stil == DugmeStili.Ikincil || stil == DugmeStili.Tehlike);
            arka.raycastTarget = true;
            var b = arka.gameObject.AddComponent<Button>();
            b.targetGraphic = arka;
            var cb = b.colors;
            cb.normalColor = Color.white; cb.highlightedColor = Color.white; cb.selectedColor = Color.white;
            cb.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f); cb.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            cb.fadeDuration = 0.08f;
            b.colors = cb;
            yazi = Metin(arka.transform, "Yazi", etiket, punto, fg, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ankor(yazi.rectTransform, Vector2.zero, Vector2.one, new Vector2(10, 4), new Vector2(-10, -4));
            return b;
        }

        public static Button Dugme(Transform ebeveyn, string ad, string etiket, DugmeStili stil, int punto = 34)
        {
            return Dugme(ebeveyn, ad, etiket, stil, out _, out _, punto);
        }
    }

    /// <summary>CanvasGroup'u yumusakca gosterir/gizler; istege bagli kayma ile.</summary>
    public class Yumusak : MonoBehaviour
    {
        public CanvasGroup grup;
        public float hedef = 1f, hiz = 7f;
        public Vector2 kayma;
        RectTransform rt; Vector2 baz; bool bazAlindi;

        public static Yumusak Ekle(GameObject go, bool baslangicAcik, Vector2 kayma = default)
        {
            var y = go.GetComponent<Yumusak>(); if (y == null) y = go.AddComponent<Yumusak>();
            y.grup = go.GetComponent<CanvasGroup>(); if (y.grup == null) y.grup = go.AddComponent<CanvasGroup>();
            y.kayma = kayma;
            y.hedef = baslangicAcik ? 1f : 0f;
            y.grup.alpha = y.hedef; y.grup.blocksRaycasts = baslangicAcik; y.grup.interactable = baslangicAcik;
            return y;
        }

        public bool Acik => hedef > 0.5f;
        public void Goster(bool ac) { hedef = ac ? 1f : 0f; if (ac) grup.blocksRaycasts = true; }

        void Update()
        {
            if (rt == null) { rt = (RectTransform)transform; }
            if (!bazAlindi) { baz = rt.anchoredPosition; bazAlindi = true; }
            float a = Mathf.MoveTowards(grup.alpha, hedef, hiz * Time.unscaledDeltaTime);
            grup.alpha = a;
            bool ac = hedef > 0.5f;
            grup.interactable = ac; grup.blocksRaycasts = ac || a > 0.05f && hedef > 0.5f;
            if (kayma != Vector2.zero) rt.anchoredPosition = baz + kayma * (1f - Mathf.SmoothStep(0, 1, a));
        }
    }
}
