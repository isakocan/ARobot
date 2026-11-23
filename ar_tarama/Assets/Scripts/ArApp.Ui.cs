using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ArTarama
{
    /// <summary>Basili tutmayi algilar (oda etiketi uzerinde gelistirici paneli).</summary>
    public class UzunBasma : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public Action uzun; public float sure = 0.8f; public bool tetiklendi;
        bool basili; float bas;
        public void OnPointerDown(PointerEventData e) { basili = true; bas = Time.unscaledTime; tetiklendi = false; }
        public void OnPointerUp(PointerEventData e) { basili = false; }
        void Update() { if (basili && !tetiklendi && Time.unscaledTime - bas >= sure) { tetiklendi = true; uzun?.Invoke(); } }
    }

    /// <summary>Arayuz: baslangic, tarama ekrani (yonlendirme kocu + mini model), inceleme ekrani, modallar.</summary>
    public partial class ArApp
    {
        static readonly string[] Odalar = { "Salon", "Mutfak", "Koridor", "Yatak odası", "Banyo", "Çalışma odası", "Toplantı salonu", "Diğer" };

        // kok
        RectTransform kok, guvenli;
        Rect sonGuvenli;
        Yumusak ekranBaslangic, ekranHud, ekranGozden, modalOnay, modalOda;
        // baslangic
        RectTransform listeIcerik; Text listeBos;
        // hud
        Text kocYazi, odaYazi, bitirYazi, debugYazi, chipYazi;
        Image kocIkon;
        ZeminHedef zeminHedef;
        CanvasGroup kocIcerik, kocPillGrup, chipGrup, kenarSol, kenarSag, ilerleGrup, debugGrup;
        RectTransform ilerleOk;
        string gosterilenKoc = ""; float kocGecis = 1f; bool debugAcik;
        float sonMiniIsaret; readonly List<Vector4> miniIs = new List<Vector4>();
        ZeminMini zeminMini, zeminGozden; ZeminSonuc gozdenZemin; GameObject gozdenSekmeler;
        // toast
        CanvasGroup toastGrup; Text toastYazi; float toastBitis;
        // modal
        Text onayBaslik, onayMetin, onayEvetYazi; Image onayEvetArka; Action onayEylem;
        // inceleme
        Text gozdenBaslik, gozdenAlt, gozdenNot, gozdenIpucu; Image sekme3b, sekmeUst; Text sekme3bYazi, sekmeUstYazi;
        RectTransform gozdenAlan, gozdenEtiketler;
        // durum
        float sonUiGuncelleme; float chipBitis;
        readonly HashSet<int> isaretChipGoruldu = new HashSet<int>();
        readonly List<Image> odaTikleri = new List<Image>(); readonly List<string> odaAdlari = new List<string>();

        // ============================================================ yardimcilar
        void Toast(string s) { toastYazi.text = s; toastBitis = Time.unscaledTime + 2.4f; }

        void ToastGuncelle()
        {
            float kalan = toastBitis - Time.unscaledTime;
            toastGrup.alpha = Mathf.Clamp01(kalan * 3f);
        }

        void GuvenliAlan()
        {
            var sa = Screen.safeArea;
            if (sa == sonGuvenli) return;
            sonGuvenli = sa;
            guvenli.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
            guvenli.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
            guvenli.offsetMin = guvenli.offsetMax = Vector2.zero;
        }

        RectTransform Tamekran(string ad, Transform ebeveyn)
        {
            var r = UiKur.Obj(ad, ebeveyn); UiKur.Tam(r); return r;
        }

        static string Say(long n) { return n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", "."); }
        static string Metre(float m) { return m.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ','); }

        static void KenariDisla(Image kutu)
        {
            var k = kutu.transform.Find("Kenar");
            if (k != null) k.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        }

        // ============================================================ kurulum
        void UiHazirla()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            var cgo = new GameObject("Arayuz", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            arayuzKok = cgo;
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
            var cs = cgo.GetComponent<CanvasScaler>();
            cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1080, 2400);
            cs.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            cs.matchWidthOrHeight = 0.5f;
            kok = (RectTransform)cgo.transform;
            guvenli = Tamekran("GuvenliAlan", kok);

            ekranGozden = GozdenUi(guvenli);
            ekranHud = HudUi(guvenli);
            ekranBaslangic = BaslangicUi(kok);
            modalOnay = OnayUi(kok);
            modalOda = OdaUi(kok);

            var tk = UiKur.Kutu(kok, "Toast", UiKur.PanelKoyu, true);
            var rt = tk.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.31f); rt.sizeDelta = new Vector2(860, 96);
            toastGrup = tk.gameObject.AddComponent<CanvasGroup>();
            toastGrup.alpha = 0; toastGrup.blocksRaycasts = false; toastGrup.interactable = false;
            toastYazi = UiKur.Metin(tk.transform, "Yazi", "", UiKur.Body, UiKur.MetinRenk, TextAnchor.MiddleCenter);
            UiKur.Ankor(toastYazi.rectTransform, Vector2.zero, Vector2.one, new Vector2(20, 6), new Vector2(-20, -6));
        }

        // ============================================================ HUD
        Yumusak HudUi(Transform ebeveyn)
        {
            var hud = Tamekran("Hud", ebeveyn);
            var y = Yumusak.Ekle(hud.gameObject, false);

            // ---- kenar oklari (yavasca cevir) ----
            kenarSol = KenarOku(hud, "KenarSol", true);
            kenarSag = KenarOku(hud, "KenarSag", false);

            // ---- ilerleme oku (odanin diger tarafina) ----
            var ok = UiKur.Obj("IlerleOk", hud);
            UiKur.Yerles(ok, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(190, 190), new Vector2(0, 40));
            ilerleGrup = ok.gameObject.AddComponent<CanvasGroup>(); ilerleGrup.alpha = 0; ilerleGrup.blocksRaycasts = false;
            var dai = UiKur.Nokta(ok, "Daire", new Color(UiKur.Panel.r, UiKur.Panel.g, UiKur.Panel.b, 0.72f), 190f); UiKur.Tam(dai.rectTransform);
            var oi = UiKur.Simge(ok, "Ok", UiKur.Ok(), UiKur.MetinRenk, 120f);
            ilerleOk = oi.rectTransform;

            // ---- koc: tek satir, cam hap ----
            var pill = UiKur.Kutu(hud, "KocPill", UiKur.Panel, true);
            var pr = pill.rectTransform;
            kocPillGrup = pill.gameObject.AddComponent<CanvasGroup>(); kocPillGrup.blocksRaycasts = false;
            pr.anchorMin = new Vector2(0, 1); pr.anchorMax = new Vector2(1, 1); pr.pivot = new Vector2(0.5f, 1);
            pr.anchoredPosition = new Vector2(0, -16); pr.sizeDelta = new Vector2(-72, 108);
            var ic = UiKur.Obj("Icerik", pr); UiKur.Tam(ic);
            kocIcerik = ic.gameObject.AddComponent<CanvasGroup>();
            kocIkon = UiKur.Simge(ic, "Ikon", UiKur.BilgiIkon(), UiKur.Vurgu, 56f);
            UiKur.Yerles(kocIkon.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(56, 56), new Vector2(38, 0));
            kocYazi = UiKur.Metin(ic, "Yazi", "", UiKur.Body, UiKur.MetinRenk, TextAnchor.MiddleLeft);
            UiKur.Ankor(kocYazi.rectTransform, Vector2.zero, Vector2.one, new Vector2(116, 6), new Vector2(-30, -6));
            kocYazi.resizeTextForBestFit = true; kocYazi.resizeTextMinSize = 26; kocYazi.resizeTextMaxSize = UiKur.Body;

            // ---- isaret rozeti ----
            var chip = UiKur.Kutu(hud, "IsaretChip", UiKur.Panel, true);
            var cr = chip.rectTransform;
            cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 1); cr.pivot = new Vector2(0.5f, 1);
            cr.anchoredPosition = new Vector2(0, -16 - 108 - 14); cr.sizeDelta = new Vector2(430, 70);
            chipGrup = chip.gameObject.AddComponent<CanvasGroup>(); chipGrup.alpha = 0; chipGrup.blocksRaycasts = false;
            var ct = UiKur.Simge(cr, "Tik", UiKur.Tik(), UiKur.Vurgu, 40f);
            UiKur.Yerles(ct.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(40, 40), new Vector2(30, 0));
            chipYazi = UiKur.Metin(cr, "Yazi", "", UiKur.Caption + 2, UiKur.MetinRenk, TextAnchor.MiddleLeft);
            UiKur.Ankor(chipYazi.rectTransform, Vector2.zero, Vector2.one, new Vector2(84, 0), new Vector2(-20, 0));

            // ---- gelistirici paneli (gizli) ----
            var dbg = UiKur.Kutu(hud, "Debug", new Color(0, 0, 0, 0.6f), false);
            var dr = dbg.rectTransform;
            dr.anchorMin = dr.anchorMax = new Vector2(0, 1); dr.pivot = new Vector2(0, 1); dr.anchoredPosition = new Vector2(36, -16 - 108 - 14 - 84); dr.sizeDelta = new Vector2(700, 150);
            debugGrup = dbg.gameObject.AddComponent<CanvasGroup>(); debugGrup.alpha = 0; debugGrup.blocksRaycasts = false;
            debugYazi = UiKur.Metin(dr, "Yazi", "", 22, UiKur.MetinRenk, TextAnchor.UpperLeft);
            UiKur.Tam(debugYazi.rectTransform, 12f);

            // ---- canli 2B acik zemin haritasi (kullanici ortada, bakis yonu yukari) ----
            zeminMini = new ZeminMini(hud, true, "AÇIK ZEMİN");
            {
                var k = zeminMini.kok;
                k.anchorMin = new Vector2(0, 0); k.anchorMax = new Vector2(1, 0); k.pivot = new Vector2(0.5f, 0);
                k.anchoredPosition = new Vector2(0, 150); k.sizeDelta = new Vector2(-48, 420);
            }

            // ---- alt bar ----
            var bar = UiKur.Kutu(hud, "AltBar", UiKur.Panel, true); bar.raycastTarget = true;
            var rb = bar.rectTransform;
            rb.anchorMin = new Vector2(0, 0); rb.anchorMax = new Vector2(1, 0); rb.pivot = new Vector2(0.5f, 0);
            rb.anchoredPosition = new Vector2(0, 24); rb.sizeDelta = new Vector2(-48, 110);
            KenariDisla(bar);
            var yatay = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            yatay.padding = new RectOffset(14, 14, 14, 14); yatay.spacing = 12;
            yatay.childForceExpandWidth = true; yatay.childForceExpandHeight = true; yatay.childControlWidth = true; yatay.childControlHeight = true;

            var odaBtn = UiKur.Dugme(bar.transform, "Oda", "", UiKur.DugmeStili.Ikincil, out Image odaArka, out odaYazi, UiKur.Body);
            odaBtn.GetComponent<Image>().raycastTarget = true;
            odaYazi.text = KayitYoneticisi.DefaultOda; odaYazi.alignment = TextAnchor.MiddleLeft;
            UiKur.Ankor(odaYazi.rectTransform, Vector2.zero, Vector2.one, new Vector2(24, 4), new Vector2(-64, -4));
            var chev = UiKur.Simge(odaArka.transform, "Chevron", UiKur.ChevronAsagi(), UiKur.Soluk, 44f);
            UiKur.Yerles(chev.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(44, 44), new Vector2(-18, 0));
            var ub = odaBtn.gameObject.AddComponent<UzunBasma>();
            ub.uzun = () => { debugAcik = !debugAcik; Haptik(20); };
            odaBtn.onClick.AddListener(() => { if (ub.tetiklendi) { ub.tetiklendi = false; return; } OdaTikleriYenile(); ModalGoster(modalOda, true); });
            var le0 = odaBtn.gameObject.AddComponent<LayoutElement>(); le0.flexibleWidth = 1.7f;

            // kalem: tararken zemin sinirini parmakla cek (zemin boyasi cizgiden gecmez)
            var arcBtn = UiKur.Dugme(bar.transform, "Kalem", "Kalem", UiKur.DugmeStili.Ikincil, out _, out kalemYazi, UiKur.Body);
            arcBtn.onClick.AddListener(() => { KalemModu(!kalemAcik); Haptik(10);
                if (kalemAcik) Toast("Sarı çizgi haritanın sınırı. Düzeltmek için parmağınla çiz"); });
            var leA = arcBtn.gameObject.AddComponent<LayoutElement>(); leA.flexibleWidth = 1f;
            // LIDAR: uygulamanın derinlikten kurduğu 3B haritayı (zemin / engel küpleri) kameranın üstünde göster
            var lidBtn = UiKur.Dugme(bar.transform, "Lidar", "LIDAR", UiKur.DugmeStili.Ikincil, out _, out Text lidYazi, UiKur.Body);
            lidBtn.onClick.AddListener(() => {
                if (oda == null) return;
                oda.LidarGoster = !oda.LidarGoster; lidYazi.color = oda.LidarGoster ? UiKur.Vurgu : UiKur.MetinRenk; Haptik(10);
                Gunluk.Yaz("tarama", "lidar gorunumu " + (oda.LidarGoster ? "acik" : "kapali"));
                if (oda.LidarGoster) Toast("Yeşil zemin, turuncu engel, mor duvar: uygulamanın derinlikten gördüğü");
            });
            var leL = lidBtn.gameObject.AddComponent<LayoutElement>(); leL.flexibleWidth = 1f;
            // silgi: yalniz kalem acikken gorunur; parmagin gectigi kalem cizgisi silinir
            var silBtn = UiKur.Dugme(bar.transform, "Silgi", "Silgi", UiKur.DugmeStili.Ikincil, out _, out kSilYazi, UiKur.Body);
            silBtn.onClick.AddListener(() => { kSilAcik = !kSilAcik; kSilYazi.color = kSilAcik ? UiKur.Vurgu : UiKur.MetinRenk; Haptik(10);
                if (kSilAcik) Toast("Silmek için çizginin üstünden parmağını geçir"); });
            var leS = silBtn.gameObject.AddComponent<LayoutElement>(); leS.flexibleWidth = 1f;
            kSilBtnGo = silBtn.gameObject; kSilBtnGo.SetActive(false);

            var sifBtn = UiKur.Dugme(bar.transform, "Sifirla", "Sıfırla", UiKur.DugmeStili.Ikincil, UiKur.Body);
            sifBtn.onClick.AddListener(() => OnayGoster("Tarama silinsin mi?", "Bu taramanın kaydı ve oda modeli silinir, baştan başlarsın.", "Sil", true, SifirlaTarama));
            var le1 = sifBtn.gameObject.AddComponent<LayoutElement>(); le1.flexibleWidth = 1f;

            var bitirBtn = UiKur.Dugme(bar.transform, "Bitir", "Bitir", UiKur.DugmeStili.Ikincil, out _, out bitirYazi, UiKur.Body);
            bitirBtn.onClick.AddListener(BitirTikla);
            var le2 = bitirBtn.gameObject.AddComponent<LayoutElement>(); le2.flexibleWidth = 1f;
            bitirYazi.color = UiKur.Vurgu;
            return y;
        }

        CanvasGroup KenarOku(Transform ebeveyn, string ad, bool sol)
        {
            var r = UiKur.Obj(ad, ebeveyn);
            r.anchorMin = r.anchorMax = new Vector2(sol ? 0f : 1f, 0.56f); r.pivot = new Vector2(sol ? 0f : 1f, 0.5f);
            r.sizeDelta = new Vector2(120, 240); r.anchoredPosition = Vector2.zero;
            var g = r.gameObject.AddComponent<CanvasGroup>(); g.alpha = 0; g.blocksRaycasts = false;
            for (int i = 0; i < 3; i++)
            {
                var im = UiKur.Simge(r, "C" + i, UiKur.ChevronSag(), UiKur.MetinRenk, 84f);
                UiKur.Yerles(im.rectTransform, new Vector2(sol ? 0f : 1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(84, 84), new Vector2((sol ? 1 : -1) * (46 + i * 26), 0));
                if (sol) im.rectTransform.localRotation = Quaternion.Euler(0, 0, 180);
            }
            return g;
        }

        void BitirTikla()
        {
            var s = oda != null ? oda.ZSon : null;
            Gunluk.Yaz("tarama", "bitir: alan " + (s != null ? s.alan.ToString("F2") : "-") + " m2");
            if (s == null || s.alan < 2f)
                OnayGoster("Taramayı bitirelim mi?", "Henüz az zemin tarandı. Yine de bitirip haritayı görebilirsin.", "Bitir", false, Bitir);
            else Bitir();
        }

        void IsaretKaydedildi(int id)
        {
            Gunluk.Yaz("isaret", "kaydedildi " + id);
            if (mod != Mod.Tarama || !isaretChipGoruldu.Add(id)) return;
            chipYazi.text = "İşaret " + id + " kaydedildi"; chipBitis = Time.unscaledTime + 2.6f;
        }

        // ---- tarama ekrani guncelleme ----
        void TaramaArayuzuGuncelle(float dt)
        {
            var m = kocMesaj;
            // koc cumlesi: degisince kisa solma
            if (m.yazi != gosterilenKoc)
            {
                kocGecis -= dt * 9f;
                if (kocGecis <= 0f)
                {
                    gosterilenKoc = m.yazi; kocYazi.text = m.yazi; kocIkon.sprite = IkonSec(m);
                    kocIkon.color = m.uyari ? UiKur.Uyari : UiKur.Vurgu;
                    kocIkon.rectTransform.localRotation = Quaternion.Euler(0, 0, m.simge == KocSimge.Egim ? (m.yazi.Contains("tavan") ? 180f : 0f) : m.simge == KocSimge.Yuru ? 90f : 0f);
                }
            }
            else kocGecis = Mathf.Min(1f, kocGecis + dt * 6f);
            kocIcerik.alpha = Mathf.Clamp01(kocGecis);
            // yazi yokken (yalniz sorun varken yazi cikar) kutu tamamen gizlenir; yonlendirme zemindeki kucuk halka
            kocPillGrup.alpha = Mathf.MoveTowards(kocPillGrup.alpha, string.IsNullOrEmpty(gosterilenKoc) && string.IsNullOrEmpty(m.yazi) ? 0f : 1f, 4f * dt);
            if (zeminHedef == null) zeminHedef = new ZeminHedef(Resources.Load<Material>("mat_oda_dolgu"));
            zeminHedef.Guncelle(m.hedefVar && Takipte && oda != null && oda.ZSon != null, m.hx, m.hz, oda != null && oda.ZSon != null ? oda.ZSon.zeminY : 0f, dt, m.ilerleme);

            // kenar oklari
            float t = Time.unscaledTime;
            float nabiz = 0.55f + 0.45f * Mathf.Sin(t * 5f);
            kenarSag.alpha = Mathf.MoveTowards(kenarSag.alpha, m.donus > 0 ? nabiz : 0f, 6f * dt);
            kenarSol.alpha = Mathf.MoveTowards(kenarSol.alpha, m.donus < 0 ? nabiz : 0f, 6f * dt);
            var yR = kenarSag.GetComponent<RectTransform>(); var yL = kenarSol.GetComponent<RectTransform>();
            yR.anchoredPosition = new Vector2(-10f * (0.5f + 0.5f * Mathf.Sin(t * 5f)), 0); yL.anchoredPosition = new Vector2(10f * (0.5f + 0.5f * Mathf.Sin(t * 5f)), 0);

            // ilerleme oku
            ilerleGrup.alpha = Mathf.MoveTowards(ilerleGrup.alpha, m.okVar ? 0.95f : 0f, 5f * dt);
            if (m.okVar)
            {
                var f = arKamera.transform.forward; float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                float rel = Mathf.DeltaAngle(yaw, m.okYaw);
                ilerleOk.localRotation = Quaternion.Euler(0, 0, 90f - rel);
            }

            // rozet
            float kalan = chipBitis - t;
            chipGrup.alpha = Mathf.Clamp01(kalan * 3f) * Mathf.Clamp01((2.6f - kalan) * 6f + 0.2f);

            // gelistirici paneli
            debugGrup.alpha = debugAcik ? 1f : 0f;
            if (debugAcik && t - sonUiGuncelleme > 0.3f)
            {
                sonUiGuncelleme = t;
                int sn = kayit != null ? (int)kayit.Sure : 0;
                debugYazi.text = "kare " + (kayit != null ? kayit.KareSayisi : 0) + "   nokta " + Say(bulut != null ? bulut.Sayi : 0) + "   voksel " + Say(oda != null ? oda.VokselSayisi : 0) +
                    "\nbutunleştirme " + (oda != null ? oda.IsleMs : 0).ToString("F1") + " ms   yapı " + (oda != null ? oda.YapiMs : 0).ToString("F1") + " ms   fps " + sonFps.ToString("F0") +
                    "\nsüre " + (sn / 60).ToString("00") + ":" + (sn % 60).ToString("00") + "   kural " + m.kural;
            }
            {
                var ktr = arKamera.transform; var kf = ktr.forward; float kyaw = Mathf.Atan2(kf.x, kf.z) * Mathf.Rad2Deg;
                if (new Vector2(kf.x, kf.z).magnitude < 0.35f) { var ku = ktr.up; kyaw = Mathf.Atan2(ku.x, ku.z) * Mathf.Rad2Deg; if (kf.y < 0) kyaw += 180f; }
                if (t - sonMiniIsaret > 0.5f && plan != null)
                {
                    sonMiniIsaret = t; miniIs.Clear();
                    foreach (var bi in plan.Isaretler.Values)
                    {
                        if (bi.pozVar) miniIs.Add(new Vector4(bi.merkez.x, bi.merkez.z, bi.aci, bi.id));
                        else if (bi.konumVar) miniIs.Add(new Vector4(bi.konum.x, bi.konum.z, 0f, bi.id));
                    }
                    zeminMini.IsaretAyarla(miniIs);
                }
                zeminMini.Guncelle(oda != null ? oda.ZSon : null, ktr.position, kyaw, dt);
            }
        }

        static Sprite IkonSec(KocMesaj m)
        {
            switch (m.simge)
            {
                case KocSimge.Uyari: return UiKur.UyariIkon();
                case KocSimge.Egim: return UiKur.ChevronAsagi();
                case KocSimge.Don: return UiKur.Yenile();
                case KocSimge.Yuru: return UiKur.Ok();
                case KocSimge.Tamam: return UiKur.Tik();
                case KocSimge.Bekle: return UiKur.BekleIkon();
                default: return UiKur.BilgiIkon();
            }
        }

        // ============================================================ modal: genel
        Yumusak ModalKok(Transform ebeveyn, string ad, Color scrim)
        {
            var r = Tamekran(ad, ebeveyn);
            var sc = UiKur.Kutu(r, "Perde", scrim, false, false); sc.raycastTarget = true; UiKur.Tam(sc.rectTransform);
            return Yumusak.Ekle(r.gameObject, false);
        }

        void ModalGoster(Yumusak m, bool ac)
        {
            m.Goster(ac);
            ModalAcik = (modalOnay != null && modalOnay.Acik) || (modalOda != null && modalOda.Acik);
            if (ac) ModalAcik = true;
        }

        Yumusak OnayUi(Transform ebeveyn)
        {
            var y = ModalKok(ebeveyn, "OnayModal", new Color(0, 0, 0, 0.62f));
            var kart = UiKur.Kutu(y.transform, "Kart", UiKur.PanelKoyu, true); kart.raycastTarget = true;
            UiKur.Yerles(kart.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(900, 470), Vector2.zero);
            onayBaslik = UiKur.Metin(kart.transform, "Baslik", "", UiKur.Title, UiKur.MetinRenk, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKur.Ankor(onayBaslik.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(48, -110), new Vector2(-48, -36));
            onayMetin = UiKur.Metin(kart.transform, "Metin", "", UiKur.Body, UiKur.Soluk, TextAnchor.UpperLeft);
            UiKur.Ankor(onayMetin.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(48, 170), new Vector2(-48, -124));
            var vaz = UiKur.Dugme(kart.transform, "Vazgec", "Vazgeç", UiKur.DugmeStili.Ikincil, UiKur.Body);
            UiKur.Ankor(vaz.GetComponent<RectTransform>(), new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(40, 36), new Vector2(-10, 136));
            vaz.onClick.AddListener(() => { modalOnay.Goster(false); ModalAcik = false; });
            var ev = UiKur.Dugme(kart.transform, "Evet", "Evet", UiKur.DugmeStili.Birincil, out onayEvetArka, out onayEvetYazi, UiKur.Body);
            UiKur.Ankor(ev.GetComponent<RectTransform>(), new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(10, 36), new Vector2(-40, 136));
            ev.onClick.AddListener(() => { modalOnay.Goster(false); ModalAcik = false; var e = onayEylem; onayEylem = null; e?.Invoke(); });
            return y;
        }

        void OnayGoster(string baslik, string metin, string evet, bool tehlike, Action eylem)
        {
            onayBaslik.text = baslik; onayMetin.text = metin; onayEvetYazi.text = evet; onayEylem = eylem;
            onayEvetArka.color = tehlike ? UiKur.Hata : UiKur.Vurgu;
            onayEvetYazi.color = tehlike ? Color.white : UiKur.Koyu;
            ModalGoster(modalOnay, true);
        }

        Yumusak OdaUi(Transform ebeveyn)
        {
            var y = ModalKok(ebeveyn, "OdaModal", new Color(0, 0, 0, 0.55f));
            var perde = y.transform.GetChild(0).gameObject.AddComponent<Button>();
            perde.targetGraphic = perde.GetComponent<Image>(); perde.transition = Selectable.Transition.None;
            perde.onClick.AddListener(() => { modalOda.Goster(false); ModalAcik = false; });
            float satir = 110f, ust = 150f;
            float yuk = ust + Odalar.Length * satir + 36f;
            var kart = UiKur.Kutu(y.transform, "Kart", UiKur.PanelKoyu, true); kart.raycastTarget = true;
            UiKur.Yerles(kart.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(1032, yuk), new Vector2(0, 24));
            var b = UiKur.Metin(kart.transform, "Baslik", "Hangi odadasın?", UiKur.Title, UiKur.MetinRenk, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKur.Ankor(b.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(48, -ust + 10), new Vector2(-48, -26));
            var alt = UiKur.Metin(kart.transform, "Alt", "Sonraki kareler bu odayla etiketlenir", UiKur.Caption, UiKur.Soluk, TextAnchor.MiddleLeft);
            UiKur.Ankor(alt.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(48, -ust + 10 - 38), new Vector2(-48, -26 - 50));
            for (int i = 0; i < Odalar.Length; i++)
            {
                string ad = Odalar[i];
                var d = UiKur.Dugme(kart.transform, "O" + i, ad, UiKur.DugmeStili.Ghost, out Image ark, out Text yzz, UiKur.Body);
                var r = ark.rectTransform; r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0.5f, 1);
                r.anchoredPosition = new Vector2(0, -ust - i * satir); r.sizeDelta = new Vector2(-48, satir - 8);
                yzz.alignment = TextAnchor.MiddleLeft; UiKur.Ankor(yzz.rectTransform, Vector2.zero, Vector2.one, new Vector2(28, 0), new Vector2(-90, 0));
                var cizgi = UiKur.Kutu(r, "Cizgi", new Color(1, 1, 1, 0.07f), false, false);
                UiKur.Ankor(cizgi.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(24, 0), new Vector2(-24, 1.5f));
                var tk = UiKur.Simge(r, "Tik", UiKur.Tik(), UiKur.Vurgu, 46f);
                UiKur.Yerles(tk.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(46, 46), new Vector2(-24, 0));
                tk.name = "Tik"; odaTikleri.Add(tk); odaAdlari.Add(ad);
                d.onClick.AddListener(() =>
                {
                    kayit.OdaDegistir(ad); odaYazi.text = ad;
                    modalOda.Goster(false); ModalAcik = false; Haptik(15);
                });
            }
            return y;
        }

        void OdaTikleriYenile()
        {
            for (int i = 0; i < odaTikleri.Count; i++) odaTikleri[i].gameObject.SetActive(odaAdlari[i] == kayit.Oda);
        }

        // ============================================================ baslangic
        Yumusak BaslangicUi(Transform ebeveyn)
        {
            var r = Tamekran("Baslangic", ebeveyn);
            var bg = UiKur.Kutu(r, "Zemin", new Color(0.047f, 0.055f, 0.067f, 0.97f), false, false); bg.raycastTarget = true; UiKur.Tam(bg.rectTransform);
            var y = Yumusak.Ekle(r.gameObject, true);
            var ic = UiKur.Obj("Icerik", r);
            UiKur.Ankor(ic, Vector2.zero, Vector2.one, new Vector2(56, 70), new Vector2(-56, -130));

            var rozet = UiKur.Kutu(ic, "Rozet", UiKur.VurguYumusak, true);
            UiKur.Yerles(rozet.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(112, 112), new Vector2(0, -20));
            UiKur.Simge(rozet.transform, "Halka", UiKur.Halka(), UiKur.Vurgu, 70f);
            UiKur.Simge(rozet.transform, "Nokta", UiKur.Daire(), UiKur.Vurgu, 20f);
            var baslik = UiKur.Metin(ic, "Baslik", "Evlerim", 92, UiKur.MetinRenk, TextAnchor.UpperLeft, FontStyle.Bold);
            UiKur.Yerles(baslik.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(900, 120), new Vector2(0, -170));
            var alt = UiKur.Metin(ic, "Alt", "Evini bir kez gezerek açık zemin haritasını çıkar. Oyun bu haritayı kullanır.", UiKur.Body + 2, UiKur.Soluk, TextAnchor.UpperLeft);
            UiKur.Yerles(alt.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(900, 100), new Vector2(0, -300));

            var yeni = UiKur.Dugme(ic, "YeniEv", "Yeni ev ekle", UiKur.DugmeStili.Birincil, 44);
            UiKur.Yerles(yeni.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(0, 1), new Vector2(640, 140), new Vector2(0, -410));
            yeni.onClick.AddListener(YeniEv);
            // robotu sür (dev): 360 görüntü, yön tuşları, AR nesneleri, kayıt
            var robot = UiKur.Dugme(ic, "RobotDev", "Robot", UiKur.DugmeStili.Ikincil, 40);
            UiKur.Yerles(robot.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(0, 1), new Vector2(308, 140), new Vector2(660, -410));
            robot.onClick.AddListener(RobotDevAc);

            var etiket = UiKur.Metin(ic, "Etiket", "EVLER", UiKur.Caption, UiKur.Soluk, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKur.Yerles(etiket.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(700, 40), new Vector2(4, -620));

            var gor = UiKur.Obj("Liste", ic);
            UiKur.Ankor(gor, Vector2.zero, Vector2.one, new Vector2(0, 0), new Vector2(0, -680));
            gor.gameObject.AddComponent<RectMask2D>();
            var sr = gor.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Elastic; sr.scrollSensitivity = 40;
            listeIcerik = UiKur.Obj("Icerik", gor);
            listeIcerik.anchorMin = new Vector2(0, 1); listeIcerik.anchorMax = new Vector2(1, 1); listeIcerik.pivot = new Vector2(0.5f, 1);
            listeIcerik.anchoredPosition = Vector2.zero; listeIcerik.sizeDelta = Vector2.zero;
            var vl = listeIcerik.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 16; vl.childControlWidth = vl.childControlHeight = true; vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            vl.padding = new RectOffset(0, 0, 4, 40);
            listeIcerik.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = listeIcerik; sr.viewport = gor;
            listeBos = UiKur.Metin(gor, "Bos", "Henüz ev yok. İşaretleri duvara yapıştır, sonra ilk evini ekle.", UiKur.Body, UiKur.Soluk, TextAnchor.UpperLeft);
            UiKur.Yerles(listeBos.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(900, 60), new Vector2(4, -10));
            return y;
        }

        [Serializable] class OzetOku { public int frame_count; public List<OdaOku> rooms; }
        [Serializable] class OdaOku { public string name; public int frame_count; }

        void KayitlariListele() { EvleriListele(); }

        void EvleriListele()
        {
            for (int i = listeIcerik.childCount - 1; i >= 0; i--) Destroy(listeIcerik.GetChild(i).gameObject);
            var evler = EvDeposu.Listele();
            listeBos.gameObject.SetActive(evler.Count == 0);
            foreach (var id in evler)
            {
                string ad = id, satir2 = "";
                try
                {
                    var ev = Ev.Yukle(EvDeposu.Klasor(id));
                    ad = ev.ad;
                    satir2 = Metre(ev.Alan) + " m² açık zemin  ·  " + ev.isaretler.Count + " işaret  ·  " + ev.taramaSayisi + " tarama";
                }
                catch (Exception e) { Debug.LogWarning("[ArTarama] ev okunamadi " + id + ": " + e.Message); satir2 = "Okunamadı"; }
                var kart = UiKur.Kutu(listeIcerik, "E" + id, UiKur.Panel, true); kart.raycastTarget = true;
                var le = kart.gameObject.AddComponent<LayoutElement>(); le.minHeight = 140; le.preferredHeight = 140;
                var t1 = UiKur.Metin(kart.transform, "Ad", ad, UiKur.Body + 2, UiKur.MetinRenk, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKur.Ankor(t1.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 1), new Vector2(32, 0), new Vector2(-250, -14));
                var t2 = UiKur.Metin(kart.transform, "Bilgi", satir2, UiKur.Caption + 2, UiKur.Soluk, TextAnchor.MiddleLeft);
                UiKur.Ankor(t2.rectTransform, new Vector2(0, 0), new Vector2(1, 0.5f), new Vector2(32, 14), new Vector2(-250, 0));
                var ch = UiKur.Simge(kart.transform, "Ok", UiKur.ChevronSag(), UiKur.Soluk, 48f);
                UiKur.Yerles(ch.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(48, 48), new Vector2(-34, 0));
                var btn = kart.gameObject.AddComponent<Button>(); btn.targetGraphic = kart; btn.transition = Selectable.Transition.None;
                string evId = id;
                btn.onClick.AddListener(() =>
                {
                    try { EvGoster(Ev.Yukle(EvDeposu.Klasor(evId))); }
                    catch (Exception e) { Debug.LogWarning("[ArTarama] ev acilamadi: " + e.Message); Toast("Ev açılamadı"); }
                });
                // sil: eski evleri temizleyip yeniden kaydetmek icin (onayla, geri alinamaz)
                var silB = UiKur.Dugme(kart.transform, "Sil", "Sil", UiKur.DugmeStili.Tehlike, UiKur.Body);
                UiKur.Yerles(silB.GetComponent<RectTransform>(), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(130, 84), new Vector2(-170, 0));
                string evAd = ad;
                silB.onClick.AddListener(() => OnayGoster(evAd + " silinsin mi?", "Bu evin haritası ve işaretleri kalıcı olarak silinir, geri alınamaz.", "Sil", true, () =>
                {
                    try
                    {
                        Directory.Delete(EvDeposu.Klasor(evId), true);
                        if (aktifEv != null && aktifEv.id == evId) aktifEv = null;
                        Toast(evAd + " silindi");
                    }
                    catch (Exception e) { Debug.LogWarning("[ArTarama] ev silinemedi: " + e.Message); Toast("Ev silinemedi"); }
                    EvleriListele();
                }));
            }
        }

        void EskiKayitlariListele()
        {
            for (int i = listeIcerik.childCount - 1; i >= 0; i--) Destroy(listeIcerik.GetChild(i).gameObject);
            var klasorler = new List<string>();
            try { if (Directory.Exists(KayitYoneticisi.Kok)) klasorler.AddRange(Directory.GetDirectories(KayitYoneticisi.Kok)); }
            catch (Exception e) { Debug.LogWarning("[ArTarama] kayit listesi: " + e.Message); }
            klasorler.Sort((a, b) => string.CompareOrdinal(b, a));
            listeBos.gameObject.SetActive(klasorler.Count == 0);
            int n = 0;
            foreach (var d in klasorler)
            {
                if (n++ >= 25) break;
                string ad = Path.GetFileName(d), klasor = d;
                string tarih = ad;
                if (ad.StartsWith("kayit_") && ad.Length >= 21)
                {
                    string s = ad.Substring(6);   // yyyyMMdd_HHmmss
                    tarih = s.Substring(6, 2) + "." + s.Substring(4, 2) + "." + s.Substring(0, 4) + "   " + s.Substring(9, 2) + ":" + s.Substring(11, 2);
                }
                string odaAd = "", satir2 = ""; bool modelVar = false;
                try
                {
                    string zj = Path.Combine(d, "zemin.json");
                    string oj = Path.Combine(d, "oda.json");
                    if (File.Exists(zj))
                    {
                        var zs = ZeminSonuc.JsonOku(File.ReadAllText(zj), out string zad);
                        odaAd = zad; modelVar = zs.alan > 0.3f;
                        satir2 = modelVar ? Metre(zs.alan) + " m² açık zemin" : "Açık zemin bulunamadı";
                    }
                    else if (File.Exists(oj))
                    {
                        var s = OdaJson.Oku(File.ReadAllText(oj), out string jad);
                        odaAd = jad; int nd = 0, nk = 0; foreach (var x in s.duvarlar) if (x.kesin) nd++; foreach (var x in s.kutular) if (x.kesin) nk++;
                        modelVar = nd + nk > 0;
                        satir2 = (modelVar ? Metre(s.alan) + " m²  ·  " + nd + " duvar  ·  " + nk + " eşya" : "Oda modeli boş");
                    }
                    else
                    {
                        string oy = Path.Combine(d, "ozet.json");
                        var oz = File.Exists(oy) ? JsonUtility.FromJson<OzetOku>(File.ReadAllText(oy)) : null;
                        if (oz != null && oz.rooms != null && oz.rooms.Count > 0) odaAd = oz.rooms[0].name;
                        satir2 = "Oda modeli yok";
                    }
                }
                catch (Exception e) { Debug.LogWarning("[ArTarama] kayit okunamadi " + ad + ": " + e.Message); }
                var kart = UiKur.Kutu(listeIcerik, "K" + ad, UiKur.Panel, true); kart.raycastTarget = true;
                var le = kart.gameObject.AddComponent<LayoutElement>(); le.minHeight = 140; le.preferredHeight = 140;
                var t1 = UiKur.Metin(kart.transform, "Tarih", (odaAd != "" ? odaAd + "  ·  " : "") + tarih, UiKur.Body + 2, UiKur.MetinRenk, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKur.Ankor(t1.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 1), new Vector2(32, 0), new Vector2(-250, -14));
                var t2 = UiKur.Metin(kart.transform, "Bilgi", satir2, UiKur.Caption + 2, UiKur.Soluk, TextAnchor.MiddleLeft);
                UiKur.Ankor(t2.rectTransform, new Vector2(0, 0), new Vector2(1, 0.5f), new Vector2(32, 14), new Vector2(-250, 0));
                var ch = UiKur.Simge(kart.transform, "Ok", UiKur.ChevronSag(), UiKur.Soluk, 48f);
                UiKur.Yerles(ch.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(48, 48), new Vector2(-34, 0));
                var btn = kart.gameObject.AddComponent<Button>(); btn.targetGraphic = kart; btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => GozdenAcKayit(klasor, odaAd));
            }
        }

        // ============================================================ inceleme
        Yumusak GozdenUi(Transform ebeveyn)
        {
            var kokG = Tamekran("Gozden", ebeveyn);
            var y = Yumusak.Ekle(kokG.gameObject, false);
            gozdenAlan = UiKur.Obj("Alan", kokG);
            UiKur.Ankor(gozdenAlan, Vector2.zero, Vector2.one, new Vector2(0, 150), new Vector2(0, -230));
            gozdenEtiketler = Tamekran("Etiketler", kokG);

            // ust kart: oda adi + istatistik + sekmeler
            var kart = UiKur.Kutu(kokG, "Baslik", UiKur.Panel, true); kart.raycastTarget = true;
            var kr = kart.rectTransform;
            kr.anchorMin = new Vector2(0, 1); kr.anchorMax = new Vector2(1, 1); kr.pivot = new Vector2(0.5f, 1);
            kr.anchoredPosition = new Vector2(0, -16); kr.sizeDelta = new Vector2(-48, 196);
            gozdenBaslik = UiKur.Metin(kr, "Ad", "", UiKur.Title, UiKur.MetinRenk, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKur.Ankor(gozdenBaslik.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(32, -86), new Vector2(-380, -20));
            gozdenAlt = UiKur.Metin(kr, "Alt", "", UiKur.Body, UiKur.Soluk, TextAnchor.MiddleLeft);
            UiKur.Ankor(gozdenAlt.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(32, -140), new Vector2(-32, -88));
            gozdenNot = UiKur.Metin(kr, "Not", "", UiKur.Caption, UiKur.Soluk, TextAnchor.MiddleLeft);
            UiKur.Ankor(gozdenNot.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(32, 10), new Vector2(-32, 52));

            var seg = UiKur.Kutu(kr, "Sekmeler", new Color(1, 1, 1, 0.07f), true);
            gozdenSekmeler = seg.gameObject;
            UiKur.Yerles(seg.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(340, 72), new Vector2(-24, -20));
            var b3 = UiKur.Dugme(seg.transform, "B3", "3B", UiKur.DugmeStili.Ghost, out sekme3b, out sekme3bYazi, UiKur.Body - 2);
            UiKur.Ankor(sekme3b.rectTransform, Vector2.zero, new Vector2(0.5f, 1), new Vector2(6, 6), new Vector2(-3, -6));
            b3.onClick.AddListener(() => gozden.UstGoster(false));
            var bu = UiKur.Dugme(seg.transform, "BU", "Üstten", UiKur.DugmeStili.Ghost, out sekmeUst, out sekmeUstYazi, UiKur.Body - 2);
            UiKur.Ankor(sekmeUst.rectTransform, new Vector2(0.5f, 0), Vector2.one, new Vector2(3, 6), new Vector2(-6, -6));
            bu.onClick.AddListener(() => gozden.UstGoster(true));

            gozdenIpucu = UiKur.Metin(kokG, "Ipucu", "", UiKur.Caption, UiKur.Soluk, TextAnchor.MiddleCenter);
            gozdenIpucu.rectTransform.anchorMin = new Vector2(0, 0); gozdenIpucu.rectTransform.anchorMax = new Vector2(1, 0);
            gozdenIpucu.rectTransform.pivot = new Vector2(0.5f, 0); gozdenIpucu.rectTransform.anchoredPosition = new Vector2(0, 160); gozdenIpucu.rectTransform.sizeDelta = new Vector2(-48, 44);
            Golge(gozdenIpucu);

            var bar = UiKur.Kutu(kokG, "AltBar", UiKur.Panel, true); bar.raycastTarget = true;
            var rb = bar.rectTransform;
            rb.anchorMin = new Vector2(0, 0); rb.anchorMax = new Vector2(1, 0); rb.pivot = new Vector2(0.5f, 0);
            rb.anchoredPosition = new Vector2(0, 24); rb.sizeDelta = new Vector2(-48, 110);
            KenariDisla(bar);
            var yatay = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            yatay.padding = new RectOffset(14, 14, 14, 14); yatay.spacing = 12;
            yatay.childForceExpandWidth = true; yatay.childForceExpandHeight = true; yatay.childControlWidth = true; yatay.childControlHeight = true;
            var kapat = UiKur.Dugme(bar.transform, "Kapat", "Kapat", UiKur.DugmeStili.Ikincil, UiKur.Body);
            kapat.onClick.AddListener(GozdenKapat);
            kapat.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            // robota gonder: robot uygulamasi kucuk haritayi ve konum bulma isaretleri buradan alir
            var robota = UiKur.Dugme(bar.transform, "Robota", "Robota", UiKur.DugmeStili.Ikincil, out _, out robotaYazi, UiKur.Body);
            robota.onClick.AddListener(RobotaGonderTikla);
            robota.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var silgi = UiKur.Dugme(bar.transform, "Silgi", "Silgi", UiKur.DugmeStili.Ikincil, out _, out silgiYazi, UiKur.Body);
            silgi.onClick.AddListener(() => { silgiAcik = !silgiAcik; SilgiGorunumu(); Haptik(10); });
            silgi.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var yeni = UiKur.Dugme(bar.transform, "Duzenle", "Düzenle", UiKur.DugmeStili.Birincil, UiKur.Body);
            yeni.onClick.AddListener(() => { silgiAcik = false; EvDuzenle(); });
            yeni.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1.4f;
            return y;
        }

        static void Golge(Text t, float a = 0.55f)
        {
            var s = t.gameObject.AddComponent<Shadow>(); s.effectColor = new Color(0, 0, 0, a); s.effectDistance = new Vector2(2f, -2f);
        }

        void GozdenUiBagla()
        {
            gozden.alan = gozdenAlan; gozden.etiketKok = gozdenEtiketler;
            gozden.UstDegisti += _ => SekmeGorunumu();
        }

        void SekmeGorunumu()
        {
            bool ust = gozden.Ust;
            sekme3b.color = !ust ? UiKur.Vurgu : new Color(1, 1, 1, 0); sekme3bYazi.color = !ust ? UiKur.Koyu : UiKur.MetinRenk;
            sekmeUst.color = ust ? UiKur.Vurgu : new Color(1, 1, 1, 0); sekmeUstYazi.color = ust ? UiKur.Koyu : UiKur.MetinRenk;
            gozdenIpucu.text = ust ? "Sürükle · iki parmakla yakınlaştır · çift dokun: sıfırla" : "Döndür · iki parmakla kaydır ve yakınlaştır · çift dokun: sıfırla";
        }

        // ---- ev ekrani ----
        Text silgiYazi; bool silgiAcik, silgiDegisti; float silgiSonCizim;

        void SilgiGorunumu()
        {
            if (silgiYazi != null) silgiYazi.color = silgiAcik ? UiKur.Vurgu : UiKur.MetinRenk;
            gozdenIpucu.text = silgiAcik ? "Silmek istediğin yanlış zeminin üstünden parmağını gezdir" : "Düzenle: evi gezip haritayı büyüt · Silgi: yanlış yerleri sil";
        }

        Text robotaYazi; bool robotaGidiyor;
        async void RobotaGonderTikla()
        {
            if (aktifEv == null || robotaGidiyor) return;
            robotaGidiyor = true; Haptik(10);
            try
            {
                string hata = await RobotaGonder.Gonder(EvDeposu.Klasor(aktifEv.id), d => { if (robotaYazi != null) robotaYazi.text = "…"; });
                Toast(hata ?? (aktifEv.ad + " robota gönderildi"));
                if (hata == null) Haptik(30);
            }
            catch (Exception e) { Toast("Gönderilemedi: " + e.Message); }
            finally { robotaGidiyor = false; if (robotaYazi != null) robotaYazi.text = "Robota"; }
        }

        void EvGoster(Ev ev)
        {
            aktifEv = ev; silgiAcik = false;
            GozdenAcZemin(ev.Sonuc(), ev.ad);
            var evIs = new List<Vector4>(); foreach (var m in ev.isaretler.Values) evIs.Add(new Vector4((float)m.x, (float)m.z, (float)m.aci, m.id));
            zeminGozden.IsaretAyarla(evIs);
            gozdenAlt.text = Metre(ev.Alan) + " m² açık zemin   ·   " + ev.isaretler.Count + " işaret";
            SilgiGorunumu();
        }

        /// <summary>Ev ekraninda silgi: parmagin gectigi yerde 20 cm yaricapla acik zemin silinir; birakinca kaydedilir.</summary>
        void SilgiIsle()
        {
            if (!silgiAcik || aktifEv == null || zeminGozden == null) return;
            var ts = UnityEngine.InputSystem.Touchscreen.current; if (ts == null) return;
            var t = ts.primaryTouch;
            if (t.press.isPressed)
            {
                if (zeminGozden.EkrandanHaritaya(t.position.ReadValue(), out float x, out float z) && aktifEv.Sil(x, z, 0.2) > 0) silgiDegisti = true;
                if (silgiDegisti && Time.unscaledTime - silgiSonCizim > 0.3f) { silgiSonCizim = Time.unscaledTime; gozdenZemin = aktifEv.Sonuc(); }
            }
            else if (silgiDegisti)
            {
                silgiDegisti = false; gozdenZemin = aktifEv.Sonuc();
                gozdenAlt.text = Metre(aktifEv.Alan) + " m² açık zemin   ·   " + aktifEv.isaretler.Count + " işaret";
                try { aktifEv.Kaydet(EvDeposu.Klasor(aktifEv.id)); } catch (Exception e) { Toast("Kaydedilemedi: " + e.Message); }
            }
        }

        /// <summary>Inceleme: 2B acik zemin haritasi (kuzey yukari, yurunen yol turuncu).</summary>
        void GozdenAcZemin(ZeminSonuc s, string ad)
        {
            if (zeminGozden == null)
            {
                zeminGozden = new ZeminMini(gozdenAlan, false, "AÇIK ZEMİN HARİTASI");
                UiKur.Ankor(zeminGozden.kok, Vector2.zero, Vector2.one, new Vector2(24, 0), new Vector2(-24, 0));
            }
            zeminGozden.kok.gameObject.SetActive(true); zeminGozden.Sifirla(); zeminGozden.IsaretAyarla(null);
            if (gozdenSekmeler != null) gozdenSekmeler.SetActive(false);
            gozdenZemin = s;
            gozdenBaslik.text = string.IsNullOrEmpty(ad) ? "Tarama" : ad;
            gozdenAlt.text = Metre(s.alan) + " m² açık zemin";
            gozdenNot.text = "Turkuaz: açık zemin · gri: üstünde eşya olan yer · turuncu: yürüdüğün yol";
            gozdenIpucu.text = "";
            GosterMod(Mod.Gozden);
        }

        void GozdenAc(OdaGoruntu g, string ad)
        {
            if (zeminGozden != null) zeminGozden.kok.gameObject.SetActive(false);
            if (gozdenSekmeler != null) gozdenSekmeler.SetActive(true);
            gozdenZemin = null;
            int nd = g.Duvarlar.Count, nk = g.Kutular.Count;
            gozdenBaslik.text = string.IsNullOrEmpty(ad) ? "Oda" : ad;
            gozdenAlt.text = Metre(g.Alan) + " m²   ·   " + nd + " duvar   ·   " + nk + " eşya";
            gozdenNot.text = g.TavanTahmin ? "Tavan yüksekliği tahmini: " + Metre(g.GTavanY - g.GZeminY) + " m" : "Tavan yüksekliği: " + Metre(g.GTavanY - g.GZeminY) + " m";
            gozden.Ac(g); SekmeGorunumu();
            GosterMod(Mod.Gozden);
        }

        void GozdenAcKayit(string klasor, string ad)
        {
            try
            {
                string zj = Path.Combine(klasor, "zemin.json");
                if (File.Exists(zj))
                {
                    var zs = ZeminSonuc.JsonOku(File.ReadAllText(zj), out string zad);
                    if (zs.alan < 0.3f) { Toast("Bu kayıtta açık zemin yok"); return; }
                    GozdenAcZemin(zs, !string.IsNullOrEmpty(zad) ? zad : (!string.IsNullOrEmpty(ad) ? ad : "Tarama"));
                    return;
                }
                string oj = Path.Combine(klasor, "oda.json");
                if (!File.Exists(oj)) { Toast("Bu kayıtta harita yok"); return; }
                var s = OdaJson.Oku(File.ReadAllText(oj), out string jad);
                var g = OdaGoruntu.Sonuctan(s);
                if (g.Duvarlar.Count + g.Kutular.Count == 0) { Toast("Bu kayıtta oda modeli boş"); return; }
                GozdenAc(g, !string.IsNullOrEmpty(jad) ? jad : (!string.IsNullOrEmpty(ad) ? ad : "Tarama"));
            }
            catch (Exception e) { Debug.LogWarning("[ArTarama] oda.json okunamadi: " + e.Message); Toast("Oda modeli okunamadı"); }
        }

        void GozdenKapat()
        {
            if (gozden != null) gozden.Kapat();
            gozdenZemin = null;
            GosterMod(Mod.Baslangic);
            KayitlariListele();
        }

        void GozdenGuncelle()
        {
            SilgiIsle();
            if (zeminGozden != null && gozdenZemin != null) zeminGozden.Guncelle(gozdenZemin, Vector3.zero, 0f, Time.unscaledDeltaTime);
        }
    }
}
