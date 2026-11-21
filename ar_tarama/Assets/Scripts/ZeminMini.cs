using UnityEngine;
using UnityEngine.UI;

namespace ArTarama
{
    /// <summary>
    /// 2B acik zemin haritasi paneli. Canli (tarama): kullanici ortada, harita bakis yonune gore doner (yukari = onum),
    /// olcek gezilen alana gore 3-8 m yaricap. Inceleme: tum harita kuzey yukari sigdirilir, yurunen yol cizilir.
    /// Renkler: acik zemin turkuaz, engel acik gri, gorulmus ama acik sayilmayan zemin koyu gri, bilinmeyen bos.
    /// </summary>
    public class ZeminMini
    {
        public readonly RectTransform kok;
        readonly RectTransform icerik, harita, kullanici;
        readonly RawImage resim;
        readonly Text bilgi, bos;
        Texture2D tex; ZeminSonuc sonCizilen;
        float ox, oz; int W, H;
        float olcek = 50f, yaw;            // piksel / metre
        bool canli;

        public ZeminMini(Transform ebeveyn, bool canli, string baslik)
        {
            this.canli = canli;
            var panel = UiKur.Kutu(ebeveyn, "ZeminPanel", UiKur.PanelKoyu, true);
            panel.raycastTarget = false;
            kok = panel.rectTransform;
            var m = panel.gameObject.AddComponent<Mask>(); m.showMaskGraphic = true;
            icerik = UiKur.Obj("Icerik", kok);
            icerik.anchorMin = icerik.anchorMax = new Vector2(0.5f, 0.5f); icerik.pivot = new Vector2(0.5f, 0.5f); icerik.sizeDelta = Vector2.zero;
            harita = UiKur.Obj("Harita", icerik);
            harita.anchorMin = harita.anchorMax = new Vector2(0.5f, 0.5f);
            resim = harita.gameObject.AddComponent<RawImage>(); resim.raycastTarget = false; resim.color = Color.white;
            var ok = UiKur.Simge(kok, "Sen", UiKur.Ucgen(), UiKur.Vurgu, 40f);
            kullanici = ok.rectTransform; kullanici.anchorMin = kullanici.anchorMax = new Vector2(0.5f, 0.5f); kullanici.pivot = new Vector2(0.5f, 0.45f);
            var hale = UiKur.Simge(kullanici, "Hale", UiKur.Daire(), new Color(1, 1, 1, 0.9f), 14f);
            hale.rectTransform.anchorMin = hale.rectTransform.anchorMax = new Vector2(0.5f, 0.45f); hale.rectTransform.anchoredPosition = Vector2.zero;
            var kenar = UiKur.Kutu(kok, "Kenar", UiKur.Kenar, false, false); kenar.sprite = UiKur.YuvarlakKenar(); kenar.type = Image.Type.Sliced; UiKur.Tam(kenar.rectTransform);
            kenar.raycastTarget = false;

            var etiket = UiKur.Metin(kok, "Etiket", baslik, UiKur.Caption, UiKur.Soluk, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKur.Yerles(etiket.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(360, 34), new Vector2(30, -34));
            bilgi = UiKur.Metin(kok, "Bilgi", "", UiKur.Body, UiKur.MetinRenk, TextAnchor.MiddleRight, FontStyle.Bold);
            UiKur.Yerles(bilgi.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(520, 34), new Vector2(-30, -34));
            bos = UiKur.Metin(kok, "Bos", "Telefonu zemine doğru eğ,\naçık zemin burada belirir", 30, UiKur.Soluk, TextAnchor.MiddleCenter);
            UiKur.Tam(bos.rectTransform, 40f);
        }

        public void Yok() { if (tex != null) Object.Destroy(tex); }

        /// <summary>Inceleme (kuzey yukari): ekran noktasi harita uzerindeyse dunya (ev) x,z.</summary>
        public bool EkrandanHaritaya(Vector2 sp, out float x, out float z)
        {
            x = z = 0;
            if (canli || sonCizilen == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(harita, sp, null, out Vector2 lp)) return false;
            var sz = harita.rect.size; if (sz.x < 1f || sz.y < 1f) return false;
            float nx = (lp.x - harita.rect.xMin) / sz.x, ny = (lp.y - harita.rect.yMin) / sz.y;
            if (nx < 0 || ny < 0 || nx > 1 || ny > 1) return false;
            x = ox + nx * W * ZeminSonuc.V; z = oz + ny * H * ZeminSonuc.V;
            return true;
        }
        public void Sifirla() { sonCizilen = null; resim.texture = null; bos.gameObject.SetActive(true); bilgi.text = ""; }

        void Ciz(ZeminSonuc s)
        {
            var p = s.Resim(out W, out H, out ox, out oz, canli ? 8 : 12);
            if (!canli && s.iz.Length >= 4)
            {
                // inceleme: yurunen yol (turuncu, ince)
                for (int i = 0; i + 3 < s.iz.Length; i += 2)
                {
                    float ax = (s.iz[i] - ox) / ZeminSonuc.V, az = (s.iz[i + 1] - oz) / ZeminSonuc.V, bx = (s.iz[i + 2] - ox) / ZeminSonuc.V, bz = (s.iz[i + 3] - oz) / ZeminSonuc.V;
                    int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Mathf.Abs(bx - ax), Mathf.Abs(bz - az))));
                    for (int q = 0; q <= n; q++)
                    {
                        int x = Mathf.RoundToInt(Mathf.Lerp(ax, bx, q / (float)n)), y = Mathf.RoundToInt(Mathf.Lerp(az, bz, q / (float)n));
                        if (x < 0 || y < 0 || x >= W || y >= H) continue;
                        int o = (y * W + x) * 4; p[o] = 255; p[o + 1] = 170; p[o + 2] = 70; p[o + 3] = 255;
                    }
                }
            }
            if (tex == null || tex.width != W || tex.height != H)
            {
                if (tex != null) Object.Destroy(tex);
                tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "ZeminHarita" };
            }
            tex.LoadRawTextureData(p); tex.Apply(false);
            resim.texture = tex;
            sonCizilen = s;
        }

        /// <summary>Canli: kamera konumu ve yaw (derece, +z'den saat yonu) ile. Inceleme: kamera yok sayilir.</summary>
        public void Guncelle(ZeminSonuc s, Vector3 kam, float kamYaw, float dt)
        {
            bool var = s != null && s.zeminVar && (s.acik.Count + s.engel.Count) > 20;
            bos.gameObject.SetActive(!var);
            resim.enabled = var; kullanici.gameObject.SetActive(var && canli);
            if (!var) return;
            if (s != sonCizilen) { Ciz(s); bilgi.text = Metre(s.alan) + " m² açık zemin"; }
            Rect r = kok.rect; if (r.width < 10f) return;
            float mw = W * ZeminSonuc.V, mh = H * ZeminSonuc.V;
            if (canli)
            {
                // yaricap: kullanicidan haritanin en uzak kosesine, 3-8 m
                float R = 0f;
                foreach (var c in new[] { new Vector2(ox, oz), new Vector2(ox + mw, oz), new Vector2(ox, oz + mh), new Vector2(ox + mw, oz + mh) })
                    R = Mathf.Max(R, Vector2.Distance(c, new Vector2(kam.x, kam.z)));
                R = Mathf.Clamp(R * 0.8f, 3f, 8f);
                float hedef = Mathf.Min(r.width, r.height) * 0.5f / R;
                olcek = Mathf.Lerp(olcek, hedef, 1f - Mathf.Exp(-2f * dt));
                yaw = Mathf.LerpAngle(yaw, kamYaw, 1f - Mathf.Exp(-8f * dt));
                harita.sizeDelta = new Vector2(mw, mh) * olcek;
                harita.pivot = new Vector2((kam.x - ox) / mw, (kam.z - oz) / mh);
                harita.anchoredPosition = Vector2.zero;
                icerik.localRotation = Quaternion.Euler(0, 0, yaw);   // bakis yonu yukari
                kullanici.localRotation = Quaternion.identity;
            }
            else
            {
                float k = Mathf.Min(r.width / mw, r.height / mh) * 0.94f;
                harita.sizeDelta = new Vector2(mw, mh) * k; harita.pivot = new Vector2(0.5f, 0.5f); harita.anchoredPosition = Vector2.zero;
                icerik.localRotation = Quaternion.identity;
            }
        }

        static string Metre(float v) { return v.ToString("F1", System.Globalization.CultureInfo.GetCultureInfo("tr-TR")); }
    }
}
