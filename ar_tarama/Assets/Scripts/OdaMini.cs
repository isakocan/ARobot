using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ArTarama
{
    /// <summary>
    /// Tarama ekraninin altindaki canli 3B oda modeli paneli (RoomPlan tarzi): ayri kamera RenderTexture'a cizer,
    /// kamera ~45 derece egimle kullanicinin yonunu yavasca izler ve modele otomatik sigdirilir. Dokununca tam ekran harita acilir.
    /// </summary>
    public class OdaMini
    {
        public const float Yukseklik = 640f, AltBosluk = 154f;
        readonly OdaYoneticisi oda; readonly Camera arKam;
        readonly Camera cam; readonly RenderTexture rt; readonly OdaKatiModel model;
        readonly Text bilgi, bos, ipucu;
        public readonly RectTransform kok;
        Vector3 merkez; float mesafe = 8f, yaw; bool basladi; float sonYazi;
        const float Fov = 30f, Egim = 45f;

        public OdaMini(Transform ebeveyn, OdaYoneticisi oda, Camera arKam, UnityAction tikla)
        {
            this.oda = oda; this.arKam = arKam;
            rt = new RenderTexture(1024, 640, 24, RenderTextureFormat.ARGB32) { name = "OdaMiniRT", antiAliasing = 4, filterMode = FilterMode.Bilinear };
            rt.Create();
            var go = new GameObject("OdaMiniKamera");
            cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.cullingMask = 1 << OdaYoneticisi.KatmanMini;
            cam.fieldOfView = Fov; cam.nearClipPlane = 0.3f; cam.farClipPlane = 120f; cam.depth = -20;
            cam.allowHDR = false; cam.allowMSAA = true; cam.useOcclusionCulling = false;
            cam.targetTexture = rt;
            model = new OdaKatiModel(OdaYoneticisi.KatmanMini, oda.KatiMateryal, "OdaMiniModel");

            // arayuz: yuvarlak cam panel + maske + RenderTexture
            var panel = UiKur.Kutu(ebeveyn, "OdaPanel", new Color(14 / 255f, 16 / 255f, 19 / 255f, 0.90f), true);
            panel.raycastTarget = true;
            kok = panel.rectTransform;
            kok.anchorMin = new Vector2(0, 0); kok.anchorMax = new Vector2(1, 0); kok.pivot = new Vector2(0.5f, 0);
            kok.anchoredPosition = new Vector2(0, AltBosluk); kok.sizeDelta = new Vector2(-48, Yukseklik);
            var m = panel.gameObject.AddComponent<Mask>(); m.showMaskGraphic = true;
            var ri = UiKur.Obj("Model", kok).gameObject.AddComponent<RawImage>();
            ri.texture = rt; ri.raycastTarget = false;
            UiKur.Tam(ri.rectTransform);
            var kenar = UiKur.Kutu(kok, "KenarUst", UiKur.Kenar, false, false); kenar.sprite = UiKur.YuvarlakKenar(); kenar.type = Image.Type.Sliced; UiKur.Tam(kenar.rectTransform);
            var b = panel.gameObject.AddComponent<Button>(); b.targetGraphic = panel; b.transition = Selectable.Transition.None;
            b.onClick.AddListener(tikla);

            var etiket = UiKur.Metin(kok, "Etiket", "ODA MODELİ", 24, UiKur.Soluk, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKur.Yerles(etiket.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(360, 34), new Vector2(30, -34));
            bilgi = UiKur.Metin(kok, "Bilgi", "", 26, UiKur.MetinRenk, TextAnchor.MiddleRight, FontStyle.Bold);
            UiKur.Yerles(bilgi.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(520, 34), new Vector2(-30, -34));
            bos = UiKur.Metin(kok, "Bos", "Odayı gezdikçe duvarlar ve eşyalar\nburada belirir", 30, UiKur.Soluk, TextAnchor.MiddleCenter);
            UiKur.Tam(bos.rectTransform, 40f);
            ipucu = UiKur.Metin(kok, "Ipucu", "Büyütmek için dokun", 22, new Color(UiKur.Soluk.r, UiKur.Soluk.g, UiKur.Soluk.b, 0.85f), TextAnchor.MiddleRight);
            UiKur.Yerles(ipucu.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(360, 30), new Vector2(-30, 26));
        }

        public void Yok()
        {
            if (rt != null) { cam.targetTexture = null; rt.Release(); Object.Destroy(rt); }
            if (cam != null) Object.Destroy(cam.gameObject);
            model.Yok();
        }

        public void Sifirla() { basladi = false; }

        public void Guncelle(float dt, bool goster)
        {
            cam.enabled = goster;
            kok.gameObject.SetActive(true);
            if (!goster) { model.Guncelle(oda, null, false, false, Vector3.zero, 0f, true); return; }

            Vector3 kp = arKam.transform.position, f = arKam.transform.forward; f.y = 0;
            float uyaw = f.sqrMagnitude > 1e-4f ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : yaw;
            bool var = oda.ZeminVar && (oda.Duvarlar.Count > 0 || oda.Kutular.Count > 0 || oda.ZeminPoligon.Length >= 6);
            if (!oda.ZeminVar) { kp.y = ArApp.ZeminVar ? ArApp.ZeminY : kp.y - 1.3f; }
            model.Guncelle(oda, cam, var, true, kp, uyaw, true);

            Vector3 hedef = model.Dolu ? model.Merkez : kp;
            float R = model.Dolu ? model.Yaricap : 2f;
            float hedefMesafe = R / Mathf.Sin(Fov * 0.5f * Mathf.Deg2Rad) * 1.06f;
            if (!basladi) { merkez = hedef; mesafe = hedefMesafe; yaw = uyaw; basladi = true; }
            float k = 1f - Mathf.Exp(-3f * dt), ky = 1f - Mathf.Exp(-dt / 1.6f);
            merkez += (hedef - merkez) * k; mesafe += (hedefMesafe - mesafe) * k; yaw = Mathf.LerpAngle(yaw, uyaw, ky);
            var rot = Quaternion.Euler(Egim, yaw, 0);
            cam.transform.SetPositionAndRotation(merkez - rot * Vector3.forward * mesafe, rot);
            cam.nearClipPlane = Mathf.Max(0.3f, mesafe - R * 1.5f); cam.farClipPlane = mesafe + R * 2.5f + 5f;
            Rect r = kok.rect; if (r.height > 10f) cam.aspect = r.width / r.height;

            bos.gameObject.SetActive(!var);
            if (Time.unscaledTime - sonYazi > 0.3f)
            {
                sonYazi = Time.unscaledTime;
                bilgi.text = oda.Duvarlar.Count == 0 && oda.Kutular.Count == 0 ? "" : oda.Duvarlar.Count + " duvar · " + oda.Kutular.Count + " eşya";
            }
        }
    }
}
