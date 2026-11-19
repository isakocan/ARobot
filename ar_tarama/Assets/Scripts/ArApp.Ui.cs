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
    /// <summary>Arayuz: baslangic, tarama HUD'u, ozet, harita gorunumu, modallar.</summary>
    public partial class ArApp
    {
        static readonly string[] Odalar = { "Salon", "Mutfak", "Koridor", "Yatak odası", "Banyo", "Çalışma odası", "Toplantı salonu", "Diğer" };

        // kok
        RectTransform kok, guvenli, kaplama;
        Rect sonGuvenli;
        Yumusak ekranBaslangic, ekranHud, ekranOzet, ekranHarita, modalOnay, modalOda;
        // baslangic
        RectTransform listeIcerik; Text listeBos;
        // hud
        readonly Image[] segArka = new Image[TaramaAkisi.AdimSayisi], segDolu = new Image[TaramaAkisi.AdimSayisi];
        Text adimNoYazi, adimBaslikYazi, adimAciklamaYazi, atlaYazi, istatYazi, kocYazi, odaYazi, bitirYazi;
        Button geriBtn, atlaBtn, bittiBtn, bitirBtn;
        Image kocNokta, bitirArka;
        Yumusak kocGrup, pusulaGrup, chipGrup, yonGrup, taraGrup, bittiGrup, geriGrup;
        RectTransform yonKok, pusulaHalka;
        Image yonOk, yonDaire;
        Text yonYazi, taraYazi, pusulaMerkez;
        readonly Image[] pusArka = new Image[TaramaAkisi.Sektor], pusOn = new Image[TaramaAkisi.Sektor];
        readonly Image[] chipArka = new Image[6]; readonly Text[] chipYazi = new Text[6], chipSayi = new Text[6]; readonly Image[] chipTik = new Image[6];
        Text chipIpucu;
        OdaMini odaMini;
        // toast
        CanvasGroup toastGrup; Text toastYazi; float toastBitis;
        // basari
        RectTransform basariKok; CanvasGroup basariGrup;
        // modal
        Text onayBaslik, onayMetin, onayEvetYazi; Image onayEvetArka; Action onayEylem;
        // ozet
        Text ozetBaslik, ozetAlt; readonly Text[] ozetDeger = new Text[6];
        // harita
        PlanGorunumu buyuk; RectTransform bodyRoot, harita3bAlan; Text haritaBilgi; Image sekme2b, sekme3b; Text sekme2bYazi, sekme3bYazi;
        GameObject alt2b, alt3b; bool haritaIkiB = true;
        readonly HaritaDokunma dokunma2b = new HaritaDokunma();
        Image yukseklikArka, duzlemArka, noktaArka; Text yukseklikYazi, duzlemYazi, noktaYazi;
        // durum
        string kocMesaji = ""; float kocDegisim = -10f; Color kocRenk;
        float sonUiGuncelleme;
        Vector2 yonPoz; float yonAlfa; YonTuru oncekiYon; string oncekiYonYazi = "";
        float canvasOlcek = 1f;

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

        static void Golge(Text t, float a = 0.55f)
        {
            var s = t.gameObject.AddComponent<Shadow>(); s.effectColor = new Color(0, 0, 0, a); s.effectDistance = new Vector2(2f, -2f);
        }

        RectTransform Tamekran(string ad, Transform ebeveyn)
        {
            var r = UiKur.Obj(ad, ebeveyn); UiKur.Tam(r); return r;
        }

        static string Say(long n) { return n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", "."); }

        // ============================================================ kurulum
        void UiHazirla()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            var cgo = new GameObject("Arayuz", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
            var cs = cgo.GetComponent<CanvasScaler>();
            cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1080, 2400);
            cs.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            cs.matchWidthOrHeight = 0.5f;
            kok = (RectTransform)cgo.transform;

            kaplama = Tamekran("IsaretKaplama", kok);
            guvenli = Tamekran("GuvenliAlan", kok);

            // sira: harita, hud, baslangic, ozet, modallar, basari, toast
            ekranHarita = HaritaUi(guvenli);
            ekranHud = HudUi(guvenli);
            ekranBaslangic = BaslangicUi(kok);
            ekranOzet = OzetUi(kok);
            modalOnay = OnayUi(kok);
            modalOda = OdaUi(kok);

            // basari animasyonu
            basariKok = UiKur.Obj("Basari", kok);
            basariKok.anchorMin = basariKok.anchorMax = new Vector2(0.5f, 0.55f); basariKok.sizeDelta = new Vector2(220, 220);
            basariGrup = basariKok.gameObject.AddComponent<CanvasGroup>(); basariGrup.alpha = 0; basariGrup.blocksRaycasts = false;
            var bd = UiKur.Nokta(basariKok, "Daire", UiKur.Vurgu, 220f); UiKur.Tam(bd.rectTransform);
            var bt = UiKur.Simge(basariKok, "Tik", UiKur.Tik(), UiKur.Koyu, 130f); bt.rectTransform.anchoredPosition = Vector2.zero;

            // toast
            var tk = UiKur.Kutu(kok, "Toast", UiKur.PanelKoyu, true);
            var rt = tk.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.31f); rt.sizeDelta = new Vector2(860, 96);
            toastGrup = tk.gameObject.AddComponent<CanvasGroup>();
            toastGrup.alpha = 0; toastGrup.blocksRaycasts = false; toastGrup.interactable = false;
            toastYazi = UiKur.Metin(tk.transform, "Yazi", "", 30, UiKur.MetinRenk, TextAnchor.MiddleCenter);
            UiKur.Ankor(toastYazi.rectTransform, Vector2.zero, Vector2.one, new Vector2(20, 6), new Vector2(-20, -6));
        }

        // ============================================================ HUD
        Yumusak HudUi(Transform ebeveyn)
        {
            var hud = Tamekran("Hud", ebeveyn);
            var y = Yumusak.Ekle(hud.gameObject, false);

            // ---- ust kart ----
            var kart = UiKur.Kutu(hud, "AdimKarti", UiKur.Panel, true); kart.raycastTarget = true;
            var kr = kart.rectTransform;
            kr.anchorMin = new Vector2(0, 1); kr.anchorMax = new Vector2(1, 1); kr.pivot = new Vector2(0.5f, 1);
            kr.anchoredPosition = new Vector2(0, -12); kr.sizeDelta = new Vector2(-48, 236);
            var seg = UiKur.Obj("Segmentler", kr);
            UiKur.Ankor(seg, new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -30), new Vector2(-28, -22));
            var hg = seg.gameObject.AddComponent<HorizontalLayoutGroup>();
            hg.spacing = 10; hg.childControlWidth = hg.childControlHeight = true; hg.childForceExpandWidth = hg.childForceExpandHeight = true;
            for (int i = 0; i < TaramaAkisi.AdimSayisi; i++)
            {
                segArka[i] = UiKur.Kutu(seg, "S" + i, new Color(1, 1, 1, 0.14f));
                segDolu[i] = UiKur.Kutu(segArka[i].transform, "Dolu", UiKur.Vurgu);
                var r = segDolu[i].rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = new Vector2(0, 1); r.offsetMin = r.offsetMax = Vector2.zero;
            }
            adimNoYazi = UiKur.Metin(kr, "AdimNo", "ADIM 1 / 5", 24, UiKur.Vurgu, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKur.Yerles(adimNoYazi.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(360, 34), new Vector2(28, -46));
            adimBaslikYazi = UiKur.Metin(kr, "AdimBaslik", "", 44, UiKur.MetinRenk, TextAnchor.MiddleLeft, FontStyle.Bold);
            adimBaslikYazi.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKur.Yerles(adimBaslikYazi.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(560, 60), new Vector2(28, -80));
            adimAciklamaYazi = UiKur.Metin(kr, "AdimAciklama", "", 29, UiKur.Soluk, TextAnchor.UpperLeft);
            UiKur.Ankor(adimAciklamaYazi.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(28, 14), new Vector2(-28, 84));

            Image ark; Text yz;
            atlaBtn = UiKur.Dugme(kr, "Atla", "Atla", UiKur.DugmeStili.Ikincil, out ark, out atlaYazi, 26);
            UiKur.Yerles(ark.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(150, 58), new Vector2(-24, -46));
            atlaBtn.onClick.AddListener(AtlaTikla);
            var geri = UiKur.Dugme(kr, "Geri", "Geri", UiKur.DugmeStili.Ghost, out ark, out yz, 26);
            geriBtn = geri;
            UiKur.Yerles(ark.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(104, 58), new Vector2(-24 - 160 - 4, -46));
            geriGrup = Yumusak.Ekle(ark.gameObject, true);
            geri.onClick.AddListener(AdimaGeri);
            var bitti = UiKur.Dugme(kr, "Bitti", "Bitti", UiKur.DugmeStili.Birincil, out ark, out yz, 26);
            bittiBtn = bitti;
            UiKur.Yerles(ark.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(120, 58), new Vector2(-24 - 160 - 4 - 108 - 4, -46));
            bittiGrup = Yumusak.Ekle(ark.gameObject, false);
            bitti.onClick.AddListener(() => { isaretAdimiBitti = true; });

            // ---- yonlendirme cubugu (koçluk) ----
            var pill = UiKur.Kutu(hud, "KocPill", UiKur.Panel, true);
            var pr = pill.rectTransform;
            pr.anchorMin = new Vector2(0, 1); pr.anchorMax = new Vector2(1, 1); pr.pivot = new Vector2(0.5f, 1);
            pr.anchoredPosition = new Vector2(0, -12 - 236 - 14); pr.sizeDelta = new Vector2(-48, 76);
            kocGrup = Yumusak.Ekle(pill.gameObject, true); kocGrup.hiz = 9f;
            kocNokta = UiKur.Nokta(pr, "Nokta", UiKur.Vurgu, 20);
            UiKur.Yerles(kocNokta.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 20), new Vector2(30, 0));
            kocYazi = UiKur.Metin(pr, "Koc", "", 31, UiKur.MetinRenk, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKur.Ankor(kocYazi.rectTransform, Vector2.zero, Vector2.one, new Vector2(66, 4), new Vector2(-20, -4));
            kocYazi.horizontalOverflow = HorizontalWrapMode.Wrap;

            var istatPill = UiKur.Kutu(hud, "IstatPill", UiKur.Panel, true);
            UiKur.Yerles(istatPill.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(560, 50), new Vector2(-24, -12 - 236 - 14 - 76 - 10));
            istatYazi = UiKur.Metin(istatPill.transform, "Istat", "", 25, UiKur.MetinRenk, TextAnchor.MiddleCenter);
            UiKur.Tam(istatYazi.rectTransform, 6f);

            // ---- yon oku ----
            yonKok = UiKur.Obj("Yon", hud);
            yonKok.anchorMin = yonKok.anchorMax = new Vector2(0.5f, 0.5f); yonKok.pivot = new Vector2(0.5f, 0.5f);
            yonKok.sizeDelta = new Vector2(240, 240);
            yonGrup = Yumusak.Ekle(yonKok.gameObject, false); yonGrup.hiz = 8f;
            yonDaire = UiKur.Nokta(yonKok, "Daire", new Color(UiKur.Panel.r, UiKur.Panel.g, UiKur.Panel.b, 0.72f), 240); UiKur.Tam(yonDaire.rectTransform);
            var halka = UiKur.Simge(yonKok, "Halka", UiKur.Halka(), new Color(UiKur.Vurgu.r, UiKur.Vurgu.g, UiKur.Vurgu.b, 0.8f), 240); UiKur.Tam(halka.rectTransform);
            yonOk = UiKur.Simge(yonKok, "Ok", UiKur.Ok(), UiKur.Vurgu, 170);
            yonYazi = UiKur.Metin(hud, "YonYazi", "", 52, UiKur.MetinRenk, TextAnchor.MiddleCenter, FontStyle.Bold);
            yonYazi.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKur.Yerles(yonYazi.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(700, 80), Vector2.zero);
            Golge(yonYazi, 0.75f);
            taraYazi = yonYazi;

            // ---- pusula ----
            var pk = UiKur.Obj("Pusula", hud);
            UiKur.Yerles(pk, new Vector2(0, 0), new Vector2(0, 0), new Vector2(252, 252), new Vector2(24, OdaMini.AltBosluk + OdaMini.Yukseklik + 16));
            pusulaGrup = Yumusak.Ekle(pk.gameObject, false);
            var pd = UiKur.Nokta(pk, "Arka", new Color(UiKur.Panel.r, UiKur.Panel.g, UiKur.Panel.b, 0.80f), 252); UiKur.Tam(pd.rectTransform);
            pusulaHalka = UiKur.Obj("Halka", pk);
            pusulaHalka.anchorMin = pusulaHalka.anchorMax = new Vector2(0.5f, 0.5f); pusulaHalka.sizeDelta = new Vector2(214, 214);
            for (int i = 0; i < TaramaAkisi.Sektor; i++)
            {
                float a = -(30f * i + 3f);
                pusArka[i] = SegmentKur(pusulaHalka, "A" + i, new Color(1, 1, 1, 0.16f), a, 24f / 360f);
                pusOn[i] = SegmentKur(pusulaHalka, "D" + i, UiKur.Vurgu, a, 0f);
            }
            var tepe = UiKur.Simge(pk, "Tepe", UiKur.Ucgen(), UiKur.MetinRenk, 34f);
            UiKur.Yerles(tepe.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(34, 34), new Vector2(0, -4));
            pusulaMerkez = UiKur.Metin(pk, "Sayi", "0/12", 46, UiKur.MetinRenk, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKur.Tam(pusulaMerkez.rectTransform);

            // ---- isaret etiketleri ----
            var ck = UiKur.Obj("Chipler", hud);
            UiKur.Yerles(ck, new Vector2(0, 0), new Vector2(0, 0), new Vector2(420, 6 * 72), new Vector2(24, OdaMini.AltBosluk + OdaMini.Yukseklik + 16));
            chipGrup = Yumusak.Ekle(ck.gameObject, false);
            for (int i = 0; i < 6; i++)
            {
                var c = UiKur.Kutu(ck, "Chip" + i, UiKur.Panel, true);
                var cr = c.rectTransform; cr.anchorMin = cr.anchorMax = new Vector2(0, 0); cr.pivot = new Vector2(0, 0);
                cr.sizeDelta = new Vector2(380, 62); cr.anchoredPosition = new Vector2(0, i * 72);
                chipArka[i] = c;
                chipYazi[i] = UiKur.Metin(cr, "Y", "", 30, UiKur.MetinRenk, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKur.Ankor(chipYazi[i].rectTransform, Vector2.zero, Vector2.one, new Vector2(24, 0), new Vector2(-110, 0));
                chipSayi[i] = UiKur.Metin(cr, "S", "", 28, UiKur.Soluk, TextAnchor.MiddleRight);
                UiKur.Ankor(chipSayi[i].rectTransform, Vector2.zero, Vector2.one, new Vector2(0, 0), new Vector2(-22, 0));
                chipTik[i] = UiKur.Simge(cr, "Tik", UiKur.Tik(), UiKur.Vurgu, 44f);
                UiKur.Yerles(chipTik[i].rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(44, 44), new Vector2(-18, 0));
                c.gameObject.SetActive(false);
            }
            chipIpucu = UiKur.Metin(ck, "Ipucu", "İşaretleri sırayla kadraja al,\nher birine yaklaşık 2 sn bak", 28, UiKur.MetinRenk, TextAnchor.LowerLeft);
            UiKur.Yerles(chipIpucu.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(520, 110), new Vector2(4, 0));
            Golge(chipIpucu);

            // ---- canli oda modeli paneli (minimap yerine) ----
            odaMini = new OdaMini(hud, oda, arKamera, HaritayiAc);

            // ---- alt bar ----
            var bar = UiKur.Kutu(hud, "AltBar", UiKur.Panel, true); bar.raycastTarget = true;
            var rb = bar.rectTransform;
            rb.anchorMin = new Vector2(0, 0); rb.anchorMax = new Vector2(1, 0); rb.pivot = new Vector2(0.5f, 0);
            rb.anchoredPosition = new Vector2(0, 24); rb.sizeDelta = new Vector2(-48, 116);
            KenariDisla(bar);
            var yatay = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            yatay.padding = new RectOffset(14, 14, 14, 14); yatay.spacing = 12;
            yatay.childForceExpandWidth = true; yatay.childForceExpandHeight = true; yatay.childControlWidth = true; yatay.childControlHeight = true;

            var odaBtn = UiKur.Dugme(bar.transform, "Oda", "", UiKur.DugmeStili.Ikincil, out Image odaArka, out odaYazi, 31);
            odaBtn.GetComponent<Image>().raycastTarget = true;
            odaYazi.text = "Oda: " + KayitYoneticisi.DefaultOda; odaYazi.alignment = TextAnchor.MiddleLeft;
            UiKur.Ankor(odaYazi.rectTransform, Vector2.zero, Vector2.one, new Vector2(24, 4), new Vector2(-64, -4));
            var chev = UiKur.Simge(odaArka.transform, "Chevron", UiKur.ChevronAsagi(), UiKur.Soluk, 44f);
            UiKur.Yerles(chev.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(44, 44), new Vector2(-18, 0));
            odaBtn.onClick.AddListener(() => { OdaTikleriYenile(); ModalGoster(modalOda, true); });
            var le0 = odaBtn.gameObject.AddComponent<LayoutElement>(); le0.flexibleWidth = 1.7f;

            var sifBtn = UiKur.Dugme(bar.transform, "Sifirla", "Sıfırla", UiKur.DugmeStili.Ikincil, 31);
            sifBtn.onClick.AddListener(() => OnayGoster("Tarama silinsin mi?", "Bu taramanın kaydı ve haritası tamamen silinir, 1. adımdan başlanır.", "Sil", true, SifirlaTarama));
            var le1 = sifBtn.gameObject.AddComponent<LayoutElement>(); le1.flexibleWidth = 1f;

            bitirBtn = UiKur.Dugme(bar.transform, "Bitir", "Bitir", UiKur.DugmeStili.Ikincil, out bitirArka, out bitirYazi, 31);
            bitirBtn.onClick.AddListener(BitirTikla);
            var le2 = bitirBtn.gameObject.AddComponent<LayoutElement>(); le2.flexibleWidth = 1f;
            bitirYazi.color = UiKur.Vurgu;
            return y;
        }

        Image SegmentKur(RectTransform ebeveyn, string ad, Color renk, float aci, float dolum)
        {
            var im = UiKur.Simge(ebeveyn, ad, UiKur.Halka(), renk, 214f);
            UiKur.Tam(im.rectTransform);
            im.type = Image.Type.Filled; im.fillMethod = Image.FillMethod.Radial360; im.fillOrigin = (int)Image.Origin360.Top; im.fillClockwise = true;
            im.fillAmount = dolum;
            im.rectTransform.localRotation = Quaternion.Euler(0, 0, aci);
            return im;
        }

        void AtlaTikla()
        {
            if (akis.Mevcut >= TaramaAkisi.AdimSayisi) return;
            AdimiBitir(true);
        }

        void BitirTikla()
        {
            if (akis.Mevcut >= TaramaAkisi.AdimSayisi) { Bitir(true); return; }
            OnayGoster("Taramayı bitirelim mi?", "Tüm adımlar tamamlanmadı. Yine de kaydı bitirip özeti görebilirsin.", "Bitir", false, () => Bitir(false));
        }

        // ============================================================ adim arayuzu
        void AdimArayuzunuYenile(bool yeniTarama)
        {
            int m = akis.Mevcut;
            bool bitti = m >= TaramaAkisi.AdimSayisi;
            adimNoYazi.text = bitti ? "HEPSİ TAMAM" : "ADIM " + (m + 1) + " / " + TaramaAkisi.AdimSayisi;
            adimBaslikYazi.text = bitti ? "Tarama tamam" : TaramaAkisi.Baslik[m];
            adimAciklamaYazi.text = bitti ? "Taramayı bitirmek için Bitir'e dokun. Eksik bir yer varsa Geri ile dönebilirsin." : TaramaAkisi.Aciklama[m];
            atlaBtn.gameObject.SetActive(!bitti);
            atlaYazi.text = m == TaramaAkisi.Isaret ? "İşaret yok" : "Atla";
            var ar = atlaBtn.GetComponent<RectTransform>(); ar.sizeDelta = new Vector2(m == TaramaAkisi.Isaret ? 190 : 150, 58);
            geriGrup.Goster(m > 0);
            // Geri ve Bitti konumlari Atla genisligine gore kayar
            float x = -24 - ar.sizeDelta.x - 4;
            ((RectTransform)geriBtn.transform).anchoredPosition = new Vector2(x, -46);
            ((RectTransform)bittiBtn.transform).anchoredPosition = new Vector2(x - (geriBtn.gameObject.activeSelf && m > 0 ? 108 : 0), -46);
            bool kategori = m == TaramaAkisi.Goz || m == TaramaAkisi.Tavan || m == TaramaAkisi.Alcak;
            pusulaGrup.Goster(kategori);
            chipGrup.Goster(m == TaramaAkisi.Isaret);
            if (bitti) { bitirArka.color = UiKur.Vurgu; bitirYazi.color = UiKur.Koyu; }
            else { bitirArka.color = new Color(1, 1, 1, 0.10f); bitirYazi.color = UiKur.Vurgu; }
            if (yeniTarama) { kocMesaji = ""; kocDegisim = -10f; isaretAdimiBitti = false; }
        }

        void TaramaArayuzuGuncelle(float dt)
        {
            int m = akis.Mevcut;
            bool bitti = m >= TaramaAkisi.AdimSayisi;
            float isaretOrani = IsaretOrani();

            // ilerleme segmentleri
            for (int i = 0; i < TaramaAkisi.AdimSayisi; i++)
            {
                float p = akis.Tamamlandi[i] ? 1f : (i == m ? akis.AdimIlerleme(i, isaretOrani) : 0f);
                var r = segDolu[i].rectTransform; r.anchorMax = new Vector2(Mathf.Lerp(r.anchorMax.x, p, 0.25f), 1);
                var c = akis.Atlandi[i] ? UiKur.Uyari : UiKur.Vurgu;
                segDolu[i].color = c;
                segArka[i].color = i == m ? new Color(1, 1, 1, 0.22f) : new Color(1, 1, 1, 0.12f);
            }

            // yon oku ve etiketi
            Yonlendirme yon = bitti ? new Yonlendirme { tur = YonTuru.Yok } : akis.Yon(m);
            if (!Takipte) yon.tur = YonTuru.Yok;
            YonOkunuGuncelle(yon, dt);

            // pusula
            if (m >= TaramaAkisi.Goz && m <= TaramaAkisi.Alcak && pusulaGrup.Acik) PusulaGuncelle(m);

            if (Time.unscaledTime - sonUiGuncelleme > 0.2f)
            {
                sonUiGuncelleme = Time.unscaledTime;
                string sure = ""; if (kayit != null) { int s = (int)kayit.Sure; sure = (s / 60).ToString("00") + ":" + (s % 60).ToString("00"); }
                istatYazi.text = Say(bulut != null ? bulut.Sayi : 0) + " nokta   " + (kayit != null ? kayit.KareSayisi : 0) + " kare   " + sure;
                KocGuncelle(yon, m, bitti);
                ChipleriGuncelle(m);
                bool isaretBulundu = plan != null && plan.Isaretler.Count > 0;
                bittiGrup.Goster(m == TaramaAkisi.Isaret && isaretBulundu);
                if (bitti) { bitirArka.color = UiKur.Vurgu; bitirYazi.color = UiKur.Koyu; }
            }
            odaMini.Guncelle(dt, true);
        }

        float IsaretOrani()
        {
            if (plan == null || plan.Isaretler.Count == 0) return 0f;
            float t = 0; foreach (var b in plan.Isaretler.Values) t += Mathf.Clamp01(b.sayi / (float)IsaretYoneticisi.GerekliAlgilama);
            return t / plan.Isaretler.Count;
        }

        void YonOkunuGuncelle(Yonlendirme y, float dt)
        {
            Vector2 hedef = Vector2.zero; float aci = 0; float alfa = 1f; bool ok = true; Vector2 etiketOfs = new Vector2(0, -170);
            switch (y.tur)
            {
                case YonTuru.Sol: hedef = new Vector2(-405, 100); aci = 180; etiketOfs = new Vector2(135, -165); break;
                case YonTuru.Sag: hedef = new Vector2(405, 100); aci = 0; etiketOfs = new Vector2(-135, -165); break;
                case YonTuru.Yukari: hedef = new Vector2(0, 520); aci = 90; break;
                case YonTuru.Asagi: hedef = new Vector2(0, -330); aci = -90; break;
                case YonTuru.Tara: ok = false; hedef = new Vector2(0, 330); etiketOfs = Vector2.zero; break;
                default: alfa = 0f; ok = false; etiketOfs = Vector2.zero; hedef = yonPoz; break;
            }
            float t = Time.unscaledTime;
            // darbe: yon boyunca nabiz
            Vector2 dir = new Vector2(Mathf.Cos(aci * Mathf.Deg2Rad), Mathf.Sin(aci * Mathf.Deg2Rad));
            Vector2 nabiz = ok ? dir * Mathf.Sin(t * 5.5f) * 18f : Vector2.zero;
            float k = 1f - Mathf.Exp(-10f * dt);
            if (y.tur != YonTuru.Yok) yonPoz = Vector2.Lerp(yonPoz, hedef, y.tur != oncekiYon ? 0.6f : k);
            oncekiYon = y.tur;
            yonGrup.Goster(ok && alfa > 0);
            yonKok.anchoredPosition = yonPoz + nabiz;
            yonOk.rectTransform.localRotation = Quaternion.Euler(0, 0, aci);
            yonYazi.rectTransform.anchoredPosition = yonPoz + etiketOfs;
            if (y.tur == YonTuru.Tara && y.yazi.StartsWith("Yavaşça")) yonYazi.rectTransform.anchoredPosition = new Vector2(0, 330);
            yonAlfa = Mathf.MoveTowards(yonAlfa, alfa, 6f * dt);
            var c = yonYazi.color; c.a = yonAlfa; yonYazi.color = c;
            if (y.yazi != null && y.yazi != "" && y.tur != YonTuru.Yok) yonYazi.text = y.yazi;
        }

        void PusulaGuncelle(int m)
        {
            int kat = TaramaAkisi.KategoriDe(m);
            var ilr = akis.Ilerleme[kat];
            pusulaHalka.localRotation = Quaternion.Euler(0, 0, akis.Yaw);
            for (int i = 0; i < TaramaAkisi.Sektor; i++)
            {
                float p = Mathf.Clamp01(ilr[i]);
                pusOn[i].fillAmount = 24f / 360f * p;
                bool su = i == akis.SektorNo;
                pusArka[i].color = su ? new Color(1, 1, 1, 0.42f) : new Color(1, 1, 1, 0.16f);
                pusOn[i].color = p >= 1f ? UiKur.Vurgu : Color.Lerp(UiKur.Uyari, UiKur.Vurgu, p);
            }
            pusulaMerkez.text = akis.Say(kat) + "/" + TaramaAkisi.Gerekli[m];
        }

        void ChipleriGuncelle(int m)
        {
            if (!chipGrup.Acik || plan == null) return;
            var liste = new List<IsaretBilgi>(plan.Isaretler.Values);
            liste.Sort((a, b) => a.id.CompareTo(b.id));
            for (int i = 0; i < 6; i++)
            {
                bool var = i < liste.Count;
                chipArka[i].gameObject.SetActive(var);
                if (!var) continue;
                var b = liste[i]; bool tamam = b.sayi >= IsaretYoneticisi.GerekliAlgilama;
                chipYazi[i].text = "İşaret " + b.id;
                chipSayi[i].text = tamam ? "" : Mathf.Min(b.sayi, IsaretYoneticisi.GerekliAlgilama) + "/" + IsaretYoneticisi.GerekliAlgilama;
                chipTik[i].gameObject.SetActive(tamam);
                chipArka[i].color = tamam ? new Color(UiKur.Vurgu.r * 0.25f, UiKur.Vurgu.g * 0.35f, UiKur.Vurgu.b * 0.3f, 0.85f) : UiKur.Panel;
            }
            chipIpucu.gameObject.SetActive(liste.Count == 0);
            // ipucu yaziyi yigilmayan yere
            chipIpucu.rectTransform.anchoredPosition = new Vector2(4, 0);
        }

        // ============================================================ kocluk
        void KocGuncelle(Yonlendirme yon, int m, bool bitti)
        {
            string aday; Color renk = UiKur.MetinRenk; bool uyari = false;
            var st = ARSession.state; var neden = ARSession.notTrackingReason;
            if (bitti) { aday = "Her şey tamam. Bitir'e dokun."; }
            else if (!Takipte)
            {
                uyari = true;
                switch (neden)
                {
                    case NotTrackingReason.ExcessiveMotion: aday = "Çok hızlı: yavaşla"; break;
                    case NotTrackingReason.InsufficientLight: aday = "Işık az: aydınlık bir yere çevir"; break;
                    case NotTrackingReason.InsufficientFeatures: aday = "Dokusuz yüzey: biraz geri çekil / eşyalara bak"; break;
                    case NotTrackingReason.Relocalizing: aday = "Takip kayboldu: kamerayı az önceki yere çevir"; break;
                    case NotTrackingReason.Initializing:
                        aday = oncedenTakip ? "Takip kayboldu: kamerayı az önceki yere çevir" : "Takip başlıyor: telefonu yavaşça hareket ettir"; break;
                    default:
                        aday = oncedenTakip ? "Takip kayboldu: kamerayı az önceki yere çevir" : "Takip başlıyor: telefonu yavaşça hareket ettir"; break;
                }
                if (!oncedenTakip) uyari = false;
            }
            else if (AciHizi > 60f) { aday = "Daha yavaş çevir"; uyari = true; }
            else if (HizMs > 0.6f) { aday = "Daha yavaş yürü"; uyari = true; }
            else if (bulut != null && bulut.OrtancaDerinlik > 0f && bulut.OrtancaDerinlik < 0.35f) { aday = "Çok yakınsın, biraz geri çekil"; uyari = true; }
            else aday = AdimMesaji(yon, m);
            renk = uyari ? UiKur.Uyari : UiKur.MetinRenk;
            // 1 sn'den kisa araliklarla degistirme
            if (aday != kocMesaji && Time.unscaledTime - kocDegisim >= 1.0f)
            {
                kocMesaji = aday; kocDegisim = Time.unscaledTime;
                kocYazi.text = aday;
                kocNokta.color = uyari ? UiKur.Uyari : UiKur.Vurgu;
                kocYazi.color = uyari ? UiKur.Uyari : UiKur.MetinRenk;
            }
        }

        string AdimMesaji(Yonlendirme yon, int m)
        {
            switch (m)
            {
                case TaramaAkisi.Zemin: return ZeminVar ? "Zemin bulundu" : "Telefonu yere doğru tutup yavaşça gezdir";
                case TaramaAkisi.Isaret: return isaret != null && plan.Isaretler.Count > 0 ? "İşaretlere kısa süre bak" : "Basılı işaretlerden birini kadraja al";
                default:
                    if (yon.tur == YonTuru.Tara) return "Güzel, böyle yavaş tara";
                    if (yon.tur == YonTuru.Sol || yon.tur == YonTuru.Sag) return "Boş bölgeye doğru yavaşça dön";
                    if (yon.yazi != "") return yon.yazi;
                    return TaramaAkisi.Aciklama[m];
            }
        }

        // ============================================================ basari animasyonu
        void BasariGoster() { StopCoroutine("BasariAnim"); StartCoroutine("BasariAnim"); }

        IEnumerator BasariAnim()
        {
            float t = 0;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / 0.9f;
                float s = t < 0.25f ? Mathf.Lerp(0.5f, 1.15f, t / 0.25f) : Mathf.Lerp(1.15f, 1f, (t - 0.25f) / 0.2f);
                basariKok.localScale = Vector3.one * Mathf.Clamp(s, 0.5f, 1.15f);
                basariGrup.alpha = t < 0.15f ? t / 0.15f : Mathf.Clamp01((1f - t) / 0.4f);
                yield return null;
            }
            basariGrup.alpha = 0;
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
            onayBaslik = UiKur.Metin(kart.transform, "Baslik", "", 50, UiKur.MetinRenk, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKur.Ankor(onayBaslik.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(48, -110), new Vector2(-48, -36));
            onayMetin = UiKur.Metin(kart.transform, "Metin", "", 32, UiKur.Soluk, TextAnchor.UpperLeft);
            UiKur.Ankor(onayMetin.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(48, 170), new Vector2(-48, -124));
            var vaz = UiKur.Dugme(kart.transform, "Vazgec", "Vazgeç", UiKur.DugmeStili.Ikincil, 34);
            UiKur.Ankor(vaz.GetComponent<RectTransform>(), new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(40, 36), new Vector2(-10, 136));
            vaz.onClick.AddListener(() => { modalOnay.Goster(false); ModalAcik = false; });
            var ev = UiKur.Dugme(kart.transform, "Evet", "Evet", UiKur.DugmeStili.Birincil, out onayEvetArka, out onayEvetYazi, 34);
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
            var b = UiKur.Metin(kart.transform, "Baslik", "Hangi odadasın?", 44, UiKur.MetinRenk, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKur.Ankor(b.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(48, -ust + 10), new Vector2(-48, -26));
            var alt = UiKur.Metin(kart.transform, "Alt", "Sonraki kareler bu odayla etiketlenir", 26, UiKur.Soluk, TextAnchor.MiddleLeft);
            UiKur.Ankor(alt.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(48, -ust + 10 - 38), new Vector2(-48, -26 - 50));
            for (int i = 0; i < Odalar.Length; i++)
            {
                string ad = Odalar[i];
                var d = UiKur.Dugme(kart.transform, "O" + i, ad, UiKur.DugmeStili.Ghost, out Image ark, out Text yzz, 36);
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
                    kayit.OdaDegistir(ad); odaYazi.text = "Oda: " + ad;
                    modalOda.Goster(false); ModalAcik = false; Haptik(15); Toast("Oda: " + ad);
                });
            }
            return y;
        }
        readonly List<Image> odaTikleri = new List<Image>(); readonly List<string> odaAdlari = new List<string>();

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
            // guvenli alan boslugu: ust 120, alt 60
            UiKur.Ankor(ic, Vector2.zero, Vector2.one, new Vector2(56, 70), new Vector2(-56, -130));

            var rozet = UiKur.Kutu(ic, "Rozet", UiKur.VurguYumusak, true);
            UiKur.Yerles(rozet.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(112, 112), new Vector2(0, -20));
            var halka = UiKur.Simge(rozet.transform, "Halka", UiKur.Halka(), UiKur.Vurgu, 70f);
            var ok = UiKur.Simge(rozet.transform, "Nokta", UiKur.Daire(), UiKur.Vurgu, 20f);
            var baslik = UiKur.Metin(ic, "Baslik", "Ev Taraması", 92, UiKur.MetinRenk, TextAnchor.UpperLeft, FontStyle.Bold);
            UiKur.Yerles(baslik.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(900, 120), new Vector2(0, -170));
            var alt = UiKur.Metin(ic, "Alt", "Odaları gezerek 3B harita ve kayıt oluştur.", 36, UiKur.Soluk, TextAnchor.UpperLeft);
            UiKur.Yerles(alt.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(900, 100), new Vector2(0, -300));

            var yeni = UiKur.Dugme(ic, "YeniTarama", "Yeni tarama", UiKur.DugmeStili.Birincil, 48);
            UiKur.Yerles(yeni.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(0, 1), new Vector2(968, 150), new Vector2(0, -420));
            yeni.onClick.AddListener(YeniTarama);

            var etiket = UiKur.Metin(ic, "Etiket", "ÖNCEKİ TARAMALAR", 26, UiKur.Soluk, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKur.Yerles(etiket.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(700, 40), new Vector2(4, -640));

            var gor = UiKur.Obj("Liste", ic);
            UiKur.Ankor(gor, Vector2.zero, Vector2.one, new Vector2(0, 0), new Vector2(0, -700));
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
            listeBos = UiKur.Metin(gor, "Bos", "Henüz tarama yok. İlk taramanı başlat.", 32, UiKur.Soluk, TextAnchor.UpperLeft);
            UiKur.Yerles(listeBos.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(900, 60), new Vector2(4, -10));
            return y;
        }

        [Serializable] class OzetOku { public int frame_count, point_count, plane_count; public float duration_s; public string started_at; public List<OdaOku> rooms; public bool complete; public List<IsaretOku> markers_seen; }
        [Serializable] class OdaOku { public string name; public int frame_count; }
        [Serializable] class IsaretOku { public int id; public int detections; }

        void KayitlariListele()
        {
            for (int i = listeIcerik.childCount - 1; i >= 0; i--) Destroy(listeIcerik.GetChild(i).gameObject);
            var klasorler = new List<string>();
            try
            {
                if (Directory.Exists(KayitYoneticisi.Kok)) klasorler.AddRange(Directory.GetDirectories(KayitYoneticisi.Kok));
            }
            catch (Exception e) { Debug.LogWarning("[ArTarama] kayit listesi: " + e.Message); }
            klasorler.Sort((a, b) => string.CompareOrdinal(b, a));
            listeBos.gameObject.SetActive(klasorler.Count == 0);
            int n = 0;
            foreach (var d in klasorler)
            {
                if (n++ >= 25) break;
                string ad = Path.GetFileName(d);
                long bayt = 0; int jpg = 0; OzetOku oz = null;
                try
                {
                    foreach (var f in new DirectoryInfo(d).GetFiles()) { bayt += f.Length; if (f.Extension == ".jpg") jpg++; }
                    string oy = Path.Combine(d, "ozet.json");
                    if (File.Exists(oy)) oz = JsonUtility.FromJson<OzetOku>(File.ReadAllText(oy));
                }
                catch (Exception e) { Debug.LogWarning("[ArTarama] kayit okunamadi " + ad + ": " + e.Message); }
                string tarih = ad;
                if (ad.StartsWith("kayit_") && ad.Length >= 21)
                {
                    string s = ad.Substring(6);   // yyyyMMdd_HHmmss
                    tarih = s.Substring(6, 2) + "." + s.Substring(4, 2) + "." + s.Substring(0, 4) + "   " + s.Substring(9, 2) + ":" + s.Substring(11, 2);
                }
                string odalar = "";
                if (oz != null && oz.rooms != null) foreach (var o in oz.rooms) odalar += (odalar == "" ? "" : ", ") + o.name;
                string satir2 = (odalar != "" ? odalar + "  ·  " : "") + (oz != null ? oz.frame_count : jpg) + " kare  ·  " + (bayt / (1024f * 1024f)).ToString("F0") + " MB";
                if (oz == null) satir2 += "  ·  özet yok";
                var kart = UiKur.Kutu(listeIcerik, "K" + ad, UiKur.Panel, true);
                var le = kart.gameObject.AddComponent<LayoutElement>(); le.minHeight = 140; le.preferredHeight = 140;
                var t1 = UiKur.Metin(kart.transform, "Tarih", tarih, 36, UiKur.MetinRenk, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKur.Ankor(t1.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 1), new Vector2(32, 0), new Vector2(-32, -14));
                var t2 = UiKur.Metin(kart.transform, "Bilgi", satir2, 28, UiKur.Soluk, TextAnchor.MiddleLeft);
                UiKur.Ankor(t2.rectTransform, new Vector2(0, 0), new Vector2(1, 0.5f), new Vector2(32, 14), new Vector2(-32, 0));
            }
        }

        // ============================================================ ozet
        Yumusak OzetUi(Transform ebeveyn)
        {
            var y = ModalKok(ebeveyn, "Ozet", new Color(0.02f, 0.03f, 0.035f, 0.80f));
            var kart = UiKur.Kutu(y.transform, "Kart", UiKur.PanelKoyu, true); kart.raycastTarget = true;
            UiKur.Yerles(kart.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(960, 1120), Vector2.zero);
            ozetBaslik = UiKur.Metin(kart.transform, "Baslik", "Tarama tamamlandı", 58, UiKur.MetinRenk, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKur.Ankor(ozetBaslik.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(52, -150), new Vector2(-52, -48));
            ozetAlt = UiKur.Metin(kart.transform, "Alt", "", 28, UiKur.Soluk, TextAnchor.MiddleLeft);
            UiKur.Ankor(ozetAlt.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(52, -206), new Vector2(-52, -150));
            string[] etk = { "SÜRE", "KARE", "NOKTA", "DÜZLEM", "İŞARET", "ODALAR" };
            for (int i = 0; i < 6; i++)
            {
                int sutun = i % 2, satir = i / 2;
                var hucre = UiKur.Kutu(kart.transform, "H" + i, new Color(1, 1, 1, 0.05f), true);
                var hr = hucre.rectTransform; hr.anchorMin = hr.anchorMax = new Vector2(0, 1); hr.pivot = new Vector2(0, 1);
                hr.sizeDelta = new Vector2(412, 150); hr.anchoredPosition = new Vector2(52 + sutun * 444, -250 - satir * 172);
                var e = UiKur.Metin(hr, "E", etk[i], 24, UiKur.Soluk, TextAnchor.UpperLeft, FontStyle.Bold);
                UiKur.Ankor(e.rectTransform, Vector2.zero, Vector2.one, new Vector2(24, 0), new Vector2(-16, -20));
                ozetDeger[i] = UiKur.Metin(hr, "D", "-", i == 5 ? 30 : 46, UiKur.MetinRenk, TextAnchor.LowerLeft, FontStyle.Bold);
                UiKur.Ankor(ozetDeger[i].rectTransform, Vector2.zero, Vector2.one, new Vector2(24, 16), new Vector2(-16, -52));
                ozetDeger[i].verticalOverflow = VerticalWrapMode.Overflow;
            }
            var g = UiKur.Dugme(kart.transform, "Harita", "Haritayı gör", UiKur.DugmeStili.Birincil, 40);
            UiKur.Ankor(g.GetComponent<RectTransform>(), new Vector2(0, 0), new Vector2(1, 0), new Vector2(52, 188), new Vector2(-52, 312));
            g.onClick.AddListener(HaritayiAc);
            var yb = UiKur.Dugme(kart.transform, "Yeni", "Yeni tarama", UiKur.DugmeStili.Ikincil, 40);
            UiKur.Ankor(yb.GetComponent<RectTransform>(), new Vector2(0, 0), new Vector2(1, 0), new Vector2(52, 52), new Vector2(-52, 164));
            yb.onClick.AddListener(YeniTarama);
            return y;
        }

        void OzetiDoldur()
        {
            if (kayit == null) return;
            int s = (int)kayit.Sure;
            ozetBaslik.text = akis.Mevcut >= TaramaAkisi.AdimSayisi ? "Tarama tamamlandı" : "Tarama kaydedildi";
            ozetAlt.text = kayit.SonKlasor;
            ozetDeger[0].text = (s / 60) + " dk " + (s % 60).ToString("00") + " sn";
            ozetDeger[1].text = Say(kayit.KareSayisi);
            ozetDeger[2].text = Say(bulut != null ? bulut.Sayi : 0);
            ozetDeger[3].text = duzlem != null ? duzlem.trackables.count.ToString() : "0";
            string ik = ""; int n = 0;
            if (plan != null) foreach (var b in plan.Isaretler.Values) if (b.sayi > 0) { n++; ik += (ik == "" ? "" : ", ") + b.id; }
            ozetDeger[4].text = n == 0 ? "Yok" : n + "  (" + ik + ")";
            var od = ""; foreach (var o in OdaListesi()) od += (od == "" ? "" : ", ") + o;
            ozetDeger[5].text = od;
        }

        IEnumerable<string> OdaListesi()
        {
            var l = new List<string>(); if (kayit != null) l.AddRange(kayit.OdaAdlari()); return l;
        }

        // ============================================================ harita gorunumu
        Yumusak HaritaUi(Transform ebeveyn)
        {
            var kokH = Tamekran("Harita", ebeveyn);
            var y = Yumusak.Ekle(kokH.gameObject, false);
            bodyRoot = UiKur.Obj("Govde", kokH);
            UiKur.Ankor(bodyRoot, Vector2.zero, Vector2.one, new Vector2(0, 150), new Vector2(0, -150));
            harita3bAlan = bodyRoot;

            // 2B gorunum
            buyuk = PlanGorunumu.Olustur(bodyRoot, "Plan2B", plan, arKamera, isaret, true);
            UiKur.Tam(buyuk.kok);
            buyuk.basYukari = false; buyuk.kullaniciyiTakipEt = false; buyuk.ppm = 60f; buyuk.oda = oda;
            dokunma2b.alan = bodyRoot;

            // ust cubuk
            var ust = UiKur.Obj("Ust", kokH);
            UiKur.Ankor(ust, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -140), new Vector2(-24, -12));
            var geri = UiKur.Dugme(ust, "Don", "Kameraya dön", UiKur.DugmeStili.Ikincil, out Image ga, out Text gy, 30);
            UiKur.Yerles(ga.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(360, 96), Vector2.zero);
            ga.color = UiKur.Panel;
            var ch = UiKur.Simge(ga.transform, "Chev", UiKur.ChevronSag(), UiKur.MetinRenk, 44f); ch.rectTransform.localRotation = Quaternion.Euler(0, 0, 180);
            UiKur.Yerles(ch.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(44, 44), new Vector2(34, 0));
            UiKur.Ankor(gy.rectTransform, Vector2.zero, Vector2.one, new Vector2(70, 0), new Vector2(-12, 0));
            geri.onClick.AddListener(HaritadanDon);

            var seg = UiKur.Kutu(ust, "Sekmeler", UiKur.Panel, true);
            UiKur.Yerles(seg.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(300, 96), Vector2.zero);
            var b2 = UiKur.Dugme(seg.transform, "B2", "2B", UiKur.DugmeStili.Ghost, out sekme2b, out sekme2bYazi, 34);
            UiKur.Ankor(sekme2b.rectTransform, Vector2.zero, new Vector2(0.5f, 1), new Vector2(8, 8), new Vector2(-4, -8));
            b2.onClick.AddListener(() => SekmeSec(true));
            var b3 = UiKur.Dugme(seg.transform, "B3", "3B", UiKur.DugmeStili.Ghost, out sekme3b, out sekme3bYazi, 34);
            UiKur.Ankor(sekme3b.rectTransform, new Vector2(0.5f, 0), Vector2.one, new Vector2(4, 8), new Vector2(-8, -8));
            b3.onClick.AddListener(() => SekmeSec(false));

            haritaBilgi = UiKur.Metin(kokH, "Bilgi", "", 28, UiKur.MetinRenk, TextAnchor.MiddleLeft);
            UiKur.Yerles(haritaBilgi.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(700, 40), new Vector2(34, -166));
            Golge(haritaBilgi);

            // alt: 2B
            alt2b = AltCubuk(kokH, "Alt2B");
            var bul = UiKur.Dugme(alt2b.transform, "Bul", "Beni bul", UiKur.DugmeStili.Ikincil, 32);
            bul.onClick.AddListener(() => { buyuk.kullaniciyiTakipEt = true; });
            var ya = UiKur.Dugme(alt2b.transform, "Yaklas", "Tümünü sığdır", UiKur.DugmeStili.Ikincil, 32);
            ya.onClick.AddListener(Haritayisigdir);
            // alt: 3B
            alt3b = AltCubuk(kokH, "Alt3B");
            var yr = UiKur.Dugme(alt3b.transform, "Yukseklik", "Yükseklik rengi", UiKur.DugmeStili.Ikincil, out yukseklikArka, out yukseklikYazi, 30);
            yr.onClick.AddListener(() => { harita3b.YukseklikRengi = !harita3b.YukseklikRengi; harita3b.ModAyarla(); SekmeGorunumu(); });
            var dz = UiKur.Dugme(alt3b.transform, "Duzlem", "Düzlemler", UiKur.DugmeStili.Ikincil, out duzlemArka, out duzlemYazi, 30);
            dz.onClick.AddListener(() => { harita3b.DuzlemGoster = !harita3b.DuzlemGoster; PlaneStyle.Goster = harita3b.DuzlemGoster; SekmeGorunumu(); });
            var nk = UiKur.Dugme(alt3b.transform, "Nokta", "Nokta bulutu", UiKur.DugmeStili.Ikincil, out noktaArka, out noktaYazi, 30);
            nk.onClick.AddListener(() => { harita3b.NoktaGoster = !harita3b.NoktaGoster; if (bulut != null) bulut.Goster = harita3b.NoktaGoster; SekmeGorunumu(); });
            var mr = UiKur.Dugme(alt3b.transform, "Merkez", "Merkezle", UiKur.DugmeStili.Ikincil, 30);
            mr.onClick.AddListener(() => harita3b.Merkezle());
            alt3b.SetActive(false);
            return y;
        }

        /// <summary>Kutu'nun kenar cocugu yatay yerlesimde bos bir dugme yuvasi olusturuyordu: yerlesimden cikar.</summary>
        static void KenariDisla(Image kutu)
        {
            var k = kutu.transform.Find("Kenar");
            if (k != null) k.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        }

        GameObject AltCubuk(Transform ebeveyn, string ad)
        {
            var bar = UiKur.Kutu(ebeveyn, ad, UiKur.Panel, true); bar.raycastTarget = true;
            var rb = bar.rectTransform;
            rb.anchorMin = new Vector2(0, 0); rb.anchorMax = new Vector2(1, 0); rb.pivot = new Vector2(0.5f, 0);
            rb.anchoredPosition = new Vector2(0, 24); rb.sizeDelta = new Vector2(-48, 112);
            KenariDisla(bar);
            var yatay = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            yatay.padding = new RectOffset(14, 14, 14, 14); yatay.spacing = 12;
            yatay.childForceExpandWidth = true; yatay.childForceExpandHeight = true; yatay.childControlWidth = true; yatay.childControlHeight = true;
            return bar.gameObject;
        }

        void SekmeSec(bool ikiB)
        {
            haritaIkiB = ikiB;
            if (ikiB) Haritayisigdir();
            SekmeGorunumu();
        }

        void SekmeGorunumu()
        {
            bool b2 = haritaIkiB;
            buyuk.kok.gameObject.SetActive(b2);
            alt2b.SetActive(b2); alt3b.SetActive(!b2);
            if (harita3b != null) harita3b.MaskeUygula(b2);
            sekme2b.color = b2 ? UiKur.Vurgu : new Color(1, 1, 1, 0); sekme2bYazi.color = b2 ? UiKur.Koyu : UiKur.MetinRenk;
            sekme3b.color = !b2 ? UiKur.Vurgu : new Color(1, 1, 1, 0); sekme3bYazi.color = !b2 ? UiKur.Koyu : UiKur.MetinRenk;
            if (harita3b != null)
            {
                yukseklikArka.color = harita3b.YukseklikRengi ? UiKur.VurguYumusak : new Color(1, 1, 1, 0.10f);
                duzlemArka.color = harita3b.DuzlemGoster ? UiKur.VurguYumusak : new Color(1, 1, 1, 0.10f);
                noktaArka.color = harita3b.NoktaGoster ? UiKur.VurguYumusak : new Color(1, 1, 1, 0.10f);
                PlaneStyle.Goster = harita3b.DuzlemGoster;
            }
        }

        void Haritayisigdir()
        {
            var k = plan.Kapsam();
            var bs = bodyRoot.rect.size; if (bs.x < 10) bs = new Vector2(1080, 1900);
            buyuk.ppm = Mathf.Clamp(Mathf.Min(bs.x * 0.82f / Mathf.Max(k.width, 1f), bs.y * 0.82f / Mathf.Max(k.height, 1f)), 12f, 160f);
            buyuk.bakis = k.center; buyuk.kullaniciyiTakipEt = false;
        }

        void HaritaAcildi()
        {
            haritaIkiB = true;
            PlaneStyle.Goster = true;
            if (harita3b != null) { harita3b.DuzlemGoster = true; harita3b.NoktaGoster = false; harita3b.dokunmaAlani = bodyRoot; }
            if (bulut != null) bulut.Goster = false;   // 3B haritada ham noktalar varsayilan kapali, "Nokta bulutu" dugmesiyle acilir
            SekmeGorunumu();
            Haritayisigdir();
            canvasOlcek = kok.rect.width / Screen.width;
        }

        void HaritaGuncelle()
        {
            if (kok != null) canvasOlcek = kok.rect.width / Screen.width;
            if (haritaIkiB)
            {
                dokunma2b.Oku();
                if (dokunma2b.Parmak > 0)
                {
                    Vector2 d = dokunma2b.Parmak == 1 ? dokunma2b.tekDelta : dokunma2b.ciftDelta;
                    if (d.sqrMagnitude > 0f) { buyuk.kullaniciyiTakipEt = false; buyuk.bakis -= d * canvasOlcek / buyuk.ppm; }
                    if (dokunma2b.Parmak >= 2) buyuk.ppm = Mathf.Clamp(buyuk.ppm * Mathf.Clamp(dokunma2b.yakinlik, 0.8f, 1.25f), 8f, 500f);
                }
                buyuk.Guncelle();
            }
            if (Time.unscaledTime - sonUiGuncelleme > 0.3f)
            {
                sonUiGuncelleme = Time.unscaledTime;
                haritaBilgi.text = (oda != null ? oda.Duvarlar.Count + " duvar · " + oda.Kutular.Count + " eşya   ·   " : "") + Say(bulut != null ? bulut.Sayi : 0) + " nokta   ·   " + (haritaIkiB ? "sürükle, iki parmakla yakınlaştır" : "bir parmak döndür, iki parmak kaydır");
            }
        }
    }
}
