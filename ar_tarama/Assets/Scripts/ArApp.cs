using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Unity.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ArTarama
{
    public enum Mod { Baslangic, Tarama, Gozden }

    /// <summary>Ana denetleyici: tarama akisi, yonlendirme kocu, nesne koyma, isik. Arayuz ArApp.Ui.cs, isik ArApp.Isik.cs.</summary>
    public partial class ArApp : MonoBehaviour
    {
        public ARSession oturum;
        public ARCameraManager kamera;
        public AROcclusionManager oklusyon;
        public ARPlaneManager duzlem;
        public ARRaycastManager isin;
        public ARPointCloudManager nokta;
        public AREnvironmentProbeManager probe;
        public Camera arKamera;
        public Light isik;
        public NoktaBulutu bulut;
        public KayitYoneticisi kayit;
        public PlanHaritasi plan;
        public IsaretYoneticisi isaret;
        public OdaGozden gozden;
        public OdaYoneticisi oda;

        public static ArApp Ornek { get; private set; }
        public static bool ZeminVar { get; private set; }
        public static float ZeminY { get; private set; }
        public bool Takipte { get; private set; }
        public bool OncedenTakip => oncedenTakip;
        public long SonZamanNs { get; private set; } = -1;
        public float AciHizi { get; private set; }
        public float HizMs { get; private set; }
        public Mod mod = Mod.Baslangic;

        readonly ZeminKoc koc = new ZeminKoc();
        readonly List<GameObject> nesneler = new List<GameObject>();
        readonly List<ARRaycastHit> vurus = new List<ARRaycastHit>();
        Quaternion oncekiRot; Vector3 oncekiKonum;
        float fpsSure; int fpsKare; float sonFps = 60f;
        bool oncedenTakip;
        float planBaslatBekle = -1f, sifirlaZamani = -100f;

        void Awake()
        {
            EvDeposu.Kok = System.IO.Path.Combine(Application.persistentDataPath, "evler");
            Ornek = this;
            Application.targetFrameRate = 30;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            QualitySettings.vSyncCount = 0;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.Medium;
            QualitySettings.shadowDistance = 6f;
            QualitySettings.shadowCascades = 1;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            QualitySettings.pixelLightCount = 2;
            PlaneStyle.Goster = false;   // v4: AR duzlem agi gosterilmez (golge alici kalir)
            HaptikIzniAnahtari();
            UiHazirla();
        }

        void Start()
        {
            if (oklusyon != null)
            {
                oklusyon.requestedEnvironmentDepthMode = EnvironmentDepthMode.Best;
                oklusyon.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.PreferEnvironmentOcclusion;
            }
            if (kamera != null)
            {
                kamera.requestedLightEstimation = LightEstimation.AmbientIntensity | LightEstimation.AmbientColor |
                    LightEstimation.AmbientSphericalHarmonics | LightEstimation.MainLightDirection | LightEstimation.MainLightIntensity;
                kamera.frameReceived += KareGeldi;
            }
            if (duzlem != null) duzlem.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            if (probe != null) probe.automaticPlacementRequested = true;
            if (bulut != null) { bulut.yonetici = nokta; bulut.oklusyon = oklusyon; bulut.kamera = kamera; bulut.arKamera = arKamera; }
            if (plan != null) { plan.duzlem = duzlem; plan.arKamera = arKamera; }
            if (kayit != null) { kayit.kamera = kamera; kayit.oklusyon = oklusyon; kayit.duzlem = duzlem; kayit.nokta = bulut; kayit.arKamera = arKamera; kayit.plan = plan; kayit.isaret = isaret; }
            if (isaret != null) { isaret.oklusyon = oklusyon; isaret.arKamera = arKamera; isaret.plan = plan; isaret.kayit = kayit; isaret.YeniIsaret += IsaretKaydedildi; }
            if (oda != null) { oda.duzlem = duzlem; oda.oklusyon = oklusyon; oda.kamera = kamera; oda.bulut = bulut; oda.arKamera = arKamera; oda.plan = plan; oda.kayit = kayit; }
            if (gozden != null) { gozden.katiMateryal = oda != null ? oda.KatiMateryal : null; GozdenUiBagla(); }
            arKamera.cullingMask &= ~((1 << OdaYoneticisi.KatmanMini) | (1 << OdaYoneticisi.KatmanHarita));
            oncekiRot = arKamera.transform.rotation; oncekiKonum = arKamera.transform.position;
            KayitlariListele();
            GosterMod(Mod.Baslangic);
            StartCoroutine(Tanilama());
        }

        void OnDestroy() { if (kamera != null) kamera.frameReceived -= KareGeldi; }

        // ---------------- tanilama / kamera ayari ----------------
        IEnumerator Tanilama()
        {
            float t0 = Time.realtimeSinceStartup;
            while (ARSession.state < ARSessionState.SessionInitializing && Time.realtimeSinceStartup - t0 < 20f) yield return null;
            yield return new WaitForSeconds(1.5f);
            KameraAyariSec();
            yield return new WaitForSeconds(2.5f);
            LogCokluDestek();
            while (true)
            {
                yield return new WaitForSeconds(4f);
                string duv = oda != null && oda.ZSon != null ? oda.ZSon.alan.ToString("F1") + "m2" : "-";
                string kut = oda != null && oda.ZSon != null ? oda.ZSon.engel.Count.ToString() : "-";
                Debug.Log("[ArTarama] durum: mod=" + mod + " oturum=" + ARSession.state + " neden=" + ARSession.notTrackingReason +
                    " zemin=" + (ZeminVar ? ZeminY.ToString("F2") : "yok") + " kare=" + (kayit != null ? kayit.KareSayisi : 0) +
                    " isaret=" + (isaret != null ? isaret.Toplam : 0) + " ortDerinlik=" + (bulut != null ? bulut.OrtancaDerinlik : 0).ToString("F2") +
                    " voksel=" + (oda != null ? oda.VokselSayisi : 0) + " isle=" + (oda != null ? oda.IsleMs : 0).ToString("F1") + "ms yapi=" + (oda != null ? oda.YapiMs : 0).ToString("F1") +
                    "ms acik=" + duv + " engel=" + kut + " koc=\"" + koc.Mevcut.yazi + "\"");
            }
        }

        void KameraAyariSec()
        {
            try
            {
                if (kamera == null || kamera.subsystem == null) { Debug.Log("[ArTarama] kamera altsistemi yok, ayar secilemedi"); return; }
                using (var cfgler = kamera.GetConfigurations(Allocator.Temp))
                {
                    if (!cfgler.IsCreated || cfgler.Length == 0) { Debug.Log("[ArTarama] kamera ayari listesi bos"); return; }
                    XRCameraConfiguration? en = null; long enAlan = -1;
                    foreach (var c in cfgler)
                    {
                        int fps = c.framerate ?? 0;
                        Debug.Log($"[ArTarama] kamera ayari: {c.width}x{c.height} @{fps}");
                        if (c.width > 1920) continue;
                        long alan = (long)c.width * c.height + (fps >= 30 ? 1000000000L : 0);
                        if (alan > enAlan) { enAlan = alan; en = c; }
                    }
                    if (en.HasValue)
                    {
                        kamera.currentConfiguration = en.Value;
                        Debug.Log($"[ArTarama] secilen kamera ayari: {en.Value.width}x{en.Value.height}");
                    }
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[ArTarama] kamera ayari secilemedi: " + e.Message); }
        }

        void LogCokluDestek()
        {
            if (oklusyon != null)
            {
                var d = oklusyon.descriptor;
                Debug.Log(d == null
                    ? "[ArTarama] OKLUZYON descriptor yok (altsistem olusmadi)"
                    : $"[ArTarama] OKLUZYON destek: environmentDepth={d.environmentDepthImageSupported} confidence={d.environmentDepthConfidenceImageSupported} smoothing={d.environmentDepthTemporalSmoothingSupported} insan={d.humanSegmentationDepthImageSupported}");
                Debug.Log($"[ArTarama] OKLUZYON istenen={oklusyon.requestedEnvironmentDepthMode} gecerli={oklusyon.currentEnvironmentDepthMode} tercih={oklusyon.currentOcclusionPreferenceMode}");
            }
            if (kamera != null)
            {
                var d = kamera.descriptor;
                Debug.Log($"[ArTarama] ISIK istenen={kamera.requestedLightEstimation} gecerli={kamera.currentLightEstimation}");
                if (d != null)
                    Debug.Log($"[ArTarama] ISIK destek: dunyaAmbient={d.supportsWorldTrackingAmbientIntensityLightEstimation} dunyaHDR={d.supportsWorldTrackingHDRLightEstimation} parlaklik={d.supportsAverageBrightness} renkSicakligi={d.supportsAverageColorTemperature} renkDuzeltme={d.supportsColorCorrection} lumen={d.supportsAverageIntensityInLumens} yapilandirma={d.supportsCameraConfigurations}");
            }
        }

        // ---------------- mod gecisleri ----------------
        public bool ModalAcik { get; private set; }

        void GosterMod(Mod m)
        {
            mod = m;
            if (ekranBaslangic != null) ekranBaslangic.Goster(m == Mod.Baslangic);
            if (ekranHud != null) ekranHud.Goster(m == Mod.Tarama);
            if (ekranGozden != null) ekranGozden.Goster(m == Mod.Gozden);
            bool gozdende = m == Mod.Gozden;
            if (arKamera != null) arKamera.enabled = !gozdende;
            if (kayit != null) kayit.Duraklat = gozdende;
            if (bulut != null) bulut.Aktif = m == Mod.Tarama && PuruzsuzBaslangicTamam();
            if (oda != null) oda.Aktif = m == Mod.Tarama;
            Application.targetFrameRate = gozdende ? 60 : 30;

        }

        bool PuruzsuzBaslangicTamam() { return Time.unscaledTime - sifirlaZamani > 1.0f; }

        // ---------------- ev akisi ----------------
        Ev aktifEv; bool duzenleme, hizaAbone;

        /// <summary>Yeni ev: bos haritayla tarama baslar; Bitir'de ilk isarete gore ev koordinatinda kaydedilir.</summary>
        public void YeniEv()
        {
            int n = EvDeposu.Listele().Count + 1;
            aktifEv = EvDeposu.Yeni("Ev " + n); duzenleme = false;
            YeniTarama();
            if (oda != null) oda.ev = null;
        }

        /// <summary>Duzenle: kayitli bir isaret gorulunce eski harita yerine oturur, yeni tarama eklenir.</summary>
        public void EvDuzenle()
        {
            if (aktifEv == null) return;
            duzenleme = true;
            YeniTarama();
            if (oda != null)
            {
                oda.ev = aktifEv;
                if (!hizaAbone) { hizaAbone = true; oda.EvHizalandi += id => { if (duzenleme) { Toast("İşaret " + id + " bulundu, kayıtlı zemin yüklendi"); Haptik(30); } }; }
            }
        }

        /// <summary>Oturum haritasini eve ekleyip kaydeder. Dondurur: kaydedildi mi.</summary>
        bool EvKaydet(out string not)
        {
            not = "";
            var s = oda != null ? oda.ZHam : null;
            if (aktifEv == null || s == null) { not = "Harita yok"; return false; }
            Poz2 oturumdanEve;
            if (duzenleme)
            {
                if (!oda.Hizali) { not = "Kayıtlı işaretlerden hiçbiri görülmedi, değişiklikler eve eklenemedi"; return false; }
                oturumdanEve = oda.EvOturum.Value.Ters();
            }
            else
            {
                IsaretBilgi refI = null;
                if (plan != null) foreach (var b in plan.Isaretler.Values) if (b.pozVar && b.pozSay >= 4 && (refI == null || b.id < refI.id)) refI = b;
                if (refI != null) oturumdanEve = new Poz2(refI.merkez.x, refI.merkez.z, refI.aci).Ters();
                else { oturumdanEve = Poz2.Birim; not = "İşaret görülmedi: ev kaydedildi ama sonra düzenlenemez"; }
            }
            aktifEv.Ekle(s, oturumdanEve);
            if (plan != null)
                foreach (var b in plan.Isaretler.Values)
                {
                    if (!b.pozVar || b.pozSay < 4 || aktifEv.isaretler.ContainsKey(b.id)) continue;
                    oturumdanEve.Uygula(b.merkez.x, b.merkez.z, out double x, out double z);
                    aktifEv.isaretler[b.id] = new EvIsaret { id = b.id, x = x, z = z, aci = b.aci + oturumdanEve.aci, yukseklik = b.merkez.y - s.zeminY, gozlem = b.pozSay };
                }
            try { aktifEv.Kaydet(EvDeposu.Klasor(aktifEv.id)); }
            catch (System.Exception e) { not = "Kaydedilemedi: " + e.Message; return false; }
            return true;
        }

        public void YeniTarama()
        {
            if (gozden != null && gozden.Acik) gozden.Kapat();
            if (kayit != null && kayit.Kayitta) kayit.Durdur(false);
            TemizleHepsi(true);
            kayit.Baslat();
            koc.Sifirla();
            GosterMod(Mod.Tarama);
        }

        /// <summary>Sifirla: oturum, kanit izgarasi ve kayit klasoru silinir, bastan baslanir.</summary>
        public void SifirlaTarama()
        {
            TemizleHepsi(true);
            if (kayit != null) { kayit.Sil(); kayit.Baslat(); }
            koc.Sifirla();
            Toast("Tarama silindi, baştan başlayabilirsin");
        }

        void TemizleHepsi(bool oturumuSifirla)
        {
            KalemTemizle();
            if (oturumuSifirla) { oturum.Reset(); }
            sifirlaZamani = Time.unscaledTime;
            oncedenTakip = false;
            if (bulut != null) bulut.Temizle();
            if (plan != null) plan.Temizle();
            if (oda != null) oda.Temizle();
            if (zeminMini != null) zeminMini.Sifirla();
            if (isaret != null) isaret.Sifirla();
            Temizle();
            planBaslatBekle = Time.unscaledTime;
            ZeminVar = false;
            if (bulut != null) bulut.Aktif = false;
            isaretChipGoruldu.Clear();
        }

        /// <summary>Bitir: kayit kapanir, oturum haritasi eve eklenip kaydedilir, ev ekrani acilir.</summary>
        public void Bitir()
        {
            KalemTemizle(); KalemModu(false);
            var s = oda != null ? oda.ZHam : null;
            if (kayit != null && kayit.Kayitta)
            {
                if (oda != null) oda.Kaydet();   // ham kayit klasorune de zemin.json (tekrar oynatma icin)
                kayit.Durdur(true);
            }
            if (aktifEv == null || ((s == null || s.alan < 0.3f) && !duzenleme))
            {
                GosterMod(Mod.Baslangic); KayitlariListele();
                Toast("Henüz açık zemin bulunamadı");
                return;
            }
            bool ok = EvKaydet(out string not);
            if (oda != null) oda.ev = null;
            if (!string.IsNullOrEmpty(not)) Toast(not);
            else if (ok) Toast(duzenleme ? "Ev güncellendi" : "Ev kaydedildi");
            // bitti dedikten sonra biter: ana menuye donulur; duzenle / silgi oradan evi acinca (kullanici istegi, 22 Kasim)
            GosterMod(Mod.Baslangic); KayitlariListele();
        }

        // ---------------- ana dongu ----------------
        void Update()
        {
            GuvenliAlan();
            var tr = arKamera.transform;
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);
            AciHizi = Mathf.Lerp(AciHizi, Quaternion.Angle(oncekiRot, tr.rotation) / dt, 0.25f);
            HizMs = Mathf.Lerp(HizMs, Vector3.Distance(oncekiKonum, tr.position) / dt, 0.2f);
            oncekiRot = tr.rotation; oncekiKonum = tr.position;

            Takipte = ARSession.state == ARSessionState.SessionTracking && ARSession.notTrackingReason == NotTrackingReason.None;
            if (Takipte) oncedenTakip = true;

            ZeminuGuncelle();
            if (oda != null) oda.ArGoster = mod == Mod.Tarama && Takipte && !PlaneStyle.ArcoreGorunum;
            IsikGuncelle();
            FpsOlc(dt);

            if (planBaslatBekle >= 0f && Takipte && Time.unscaledTime - planBaslatBekle > 0.6f)
            {
                planBaslatBekle = -1f; plan.Baslat(tr.position);
            }
            if (mod == Mod.Tarama && bulut != null) bulut.Aktif = PuruzsuzBaslangicTamam() && Takipte;

            if (mod == Mod.Tarama) TaramaGuncelle(dt);
            else if (zeminHedef != null) zeminHedef.Guncelle(false, 0f, 0f, 0f, dt);
            if (mod != Mod.Tarama && kenarGo != null && kenarGo.activeSelf) kenarGo.SetActive(false);
            else if (mod == Mod.Gozden) GozdenGuncelle();
            NesneGorunurlugu();

            ToastGuncelle();
            if (mod == Mod.Tarama && !ModalAcik) { if (kalemAcik) KalemIsle(); else DokunmaIsle(); }
        }

        void FpsOlc(float dt)
        {
            fpsSure += dt; fpsKare++;
            if (fpsSure >= 5f)
            {
                sonFps = fpsKare / fpsSure;
                Debug.Log($"[ArTarama] FPS ort={sonFps:F1} (5 sn) mod={mod} voksel={(oda != null ? oda.VokselSayisi : 0)}");
                fpsSure = 0; fpsKare = 0;
            }
        }

        public float Fps => sonFps;

        void ZeminuGuncelle()
        {
            bool var = false; float y = float.MaxValue;
            if (duzlem != null)
                foreach (var p in duzlem.trackables)
                    if (p.alignment == PlaneAlignment.HorizontalUp && p.trackingState != TrackingState.None)
                    { var = true; y = Mathf.Min(y, p.transform.position.y); }
            ZeminVar = var; ZeminY = var ? y : 0f;
        }

        // ---------------- tarama: yonlendirme kocu ----------------
        float sonKocZamani;
        KocMesaj kocMesaj = new KocMesaj();

        void TaramaGuncelle(float dt)
        {
            if (Time.unscaledTime - sonKocZamani >= 0.2f)
            {
                sonKocZamani = Time.unscaledTime;
                var tr = arKamera.transform; var f = tr.forward;
                float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg; if (yaw < 0) yaw += 360f;
                float pitch = Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
                if (new Vector2(f.x, f.z).magnitude < 0.35f)   // telefon cok dik: ust yonunden yaw
                {
                    Vector3 u = tr.up; yaw = Mathf.Atan2(u.x, u.z) * Mathf.Rad2Deg; if (f.y < 0) yaw += 180f; yaw = (yaw + 720f) % 360f;
                }
                int neden = 0;
                switch (ARSession.notTrackingReason)
                {
                    case NotTrackingReason.ExcessiveMotion: neden = 1; break;
                    case NotTrackingReason.InsufficientLight: neden = 2; break;
                    case NotTrackingReason.InsufficientFeatures: neden = 3; break;
                    case NotTrackingReason.Relocalizing: neden = 4; break;
                    case NotTrackingReason.Initializing: neden = 5; break;
                }
                var gi = new KocGirdi
                {
                    takipte = Takipte, oncedenTakip = oncedenTakip, neden = neden, hizli = AciHizi > 75f || HizMs > 0.7f,
                    ortancaDerinlik = bulut != null ? bulut.OrtancaDerinlik : -1f, guvenOrani = oda != null ? oda.GuvenOrani : -1f,
                    kx = tr.position.x, ky = tr.position.y, kz = tr.position.z, yaw = yaw, pitch = pitch, zaman = Time.unscaledTime, sonuc = null
                };
                kocMesaj = koc.Guncelle(gi, oda != null ? oda.ZSon : null);
                // gorunen ama henuz az gozlenen isaret uzaktaysa: yon dogrulugu icin yaklastir (1 m'de ~1 derece, 2,5 m'de ~7 derece)
                if (Takipte && plan != null)
                    foreach (var bi in plan.Isaretler.Values)
                    {
                        if (Time.unscaledTime - bi.sonGorulme > 1f || bi.pozSay >= 15) continue;
                        Vector3 mp = bi.pozVar ? bi.merkez : bi.konum; if (!bi.pozVar && !bi.konumVar) continue;
                        if (Vector3.Distance(tr.position, mp) > 1.6f) { kocMesaj = new KocMesaj { yazi = "İşaret " + bi.id + " görüldü, kaydetmek için 1 metre kadar yaklaş", simge = KocSimge.Yuru, kural = 10 }; break; }
                    }
                if (duzenleme && Takipte && oda != null && !oda.Hizali)
                {
                    // kayitli evi acinca: isaret gorulene kadar bekle, uyar; gorulunce kayitli zemin gelir
                    string ids = aktifEv != null && aktifEv.isaretler.Count > 0 ? string.Join(", ", aktifEv.isaretler.Keys) : "";
                    kocMesaj = new KocMesaj { yazi = (ids.Length > 0 ? "Kayıtlı işareti (" + ids + ") bul, " : "Kayıtlı işareti bul, ") + "1 metreden birkaç saniye tut. Kayıtlı zemin sonra gelir", simge = KocSimge.Bilgi, kural = 9 };
                }
            }
            TaramaArayuzuGuncelle(dt);
        }

        // ---------------- kalem: zemin siniri ----------------
        // Ciz: parmakla zemin siniri cekilir; 15 cm'den kisa / cok kisa sureli dokunus (yanlislikla nokta) atilir.
        // Sil: parmagin gectigi kalem cizgisi silinir. Kalem acikken haritanin kendi buldugu sinir sari cizgiyle gorunur;
        // cekilen cizgi o yerdeki otomatik siniri duzeltir (ZeminHaritasi.KalemHucreleri).
        Text kalemYazi, kSilYazi; bool kalemAcik, kSilAcik; GameObject kSilBtnGo;
        LineRenderer kalemCizgi; readonly Dictionary<int, LineRenderer> kalemParca = new Dictionary<int, LineRenderer>();
        readonly List<Vector3> kalemNokta = new List<Vector3>(); bool kalemBasili; Vector3 kalemSon; float kalemBas;
        GameObject kenarGo; Mesh kenarMesh; ZeminSonuc kenarSonuc; Vector3 kenarKam;

        void KalemTemizle()
        {
            foreach (var l in kalemParca.Values) if (l != null) Destroy(l.gameObject);
            kalemParca.Clear(); kalemNokta.Clear();
            if (kalemCizgi != null) Destroy(kalemCizgi.gameObject);
            kalemCizgi = null; kalemBasili = false; kSilAcik = false;
            if (kenarGo != null) kenarGo.SetActive(false);
        }

        void KalemModu(bool ac)
        {
            kalemAcik = ac; kSilAcik = false;
            if (kalemYazi != null) kalemYazi.color = ac ? UiKur.Vurgu : UiKur.MetinRenk;
            if (kSilYazi != null) kSilYazi.color = UiKur.MetinRenk;
            if (kSilBtnGo != null) kSilBtnGo.SetActive(ac);
            if (!ac && kenarGo != null) kenarGo.SetActive(false);
        }

        float KalemZeminY => oda != null && oda.ZSon != null && oda.ZSon.zeminVar ? oda.ZSon.zeminY : ZeminY;

        LineRenderer YeniKalemCizgisi()
        {
            var go = new GameObject("KalemCizgi"); go.layer = OdaYoneticisi.KatmanAr;
            var l = go.AddComponent<LineRenderer>();
            // OdaDolgu: tepe rengi, her seyin ustunde (Sprites/Default derlemede olmayabiliyor; cizgi gorunmuyordu)
            var km = Resources.Load<Material>("mat_oda_dolgu");
            l.sharedMaterial = km != null ? km : new Material(Shader.Find("ArTarama/OdaDolgu"));
            l.startColor = l.endColor = new Color(1f, 1f, 1f, 0.95f);
            l.widthMultiplier = 0.03f; l.numCapVertices = 4; l.numCornerVertices = 2;
            l.alignment = LineAlignment.TransformZ; go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            l.useWorldSpace = true; l.positionCount = 0;
            return l;
        }

        void KalemIsle()
        {
            KenarGoster();
            var ts = Touchscreen.current;
            if (ts == null || oda == null || !Takipte) return;
            var t = ts.primaryTouch;
            if (!t.press.isPressed) { if (kalemBasili) KalemBirak(); return; }
            Vector2 sp = t.position.ReadValue();
            if (t.press.wasPressedThisFrame && UiUstunde(sp)) return;
            if (!t.press.wasPressedThisFrame && !kalemBasili) return;
            // ekran -> zemin duzlemi
            float zy = KalemZeminY;
            var r = arKamera.ScreenPointToRay(sp);
            if (r.direction.y > -0.05f) return;
            float d = (zy - r.origin.y) / r.direction.y; if (d <= 0f || d > 6f) return;
            var p = r.origin + r.direction * d;
            if (kSilAcik)
            {
                kalemBasili = true;
                foreach (int id in oda.SinirSil(p.x, p.z, 0.15f))
                    if (kalemParca.TryGetValue(id, out var l)) { if (l != null) Destroy(l.gameObject); kalemParca.Remove(id); Haptik(15); }
                return;
            }
            if (!kalemBasili)
            {
                kalemNokta.Clear(); kalemSon = p; kalemBas = Time.unscaledTime;
                kalemCizgi = YeniKalemCizgisi(); kalemBasili = true;
                kalemNokta.Add(new Vector3(p.x, zy + 0.012f, p.z)); KalemCiz();
                return;
            }
            if (Vector3.Distance(p, kalemSon) < 0.03f) return;
            kalemSon = p;
            kalemNokta.Add(new Vector3(p.x, zy + 0.012f, p.z));
            KalemCiz();
        }

        void KalemBirak()
        {
            kalemBasili = false;
            if (kSilAcik || kalemCizgi == null) return;
            float L = 0f; for (int i = 1; i < kalemNokta.Count; i++) L += Vector3.Distance(kalemNokta[i - 1], kalemNokta[i]);
            if (L < 0.15f || Time.unscaledTime - kalemBas < 0.12f)
            {
                // nokta gibi kisa dokunus: yanlislikla, atilir
                Destroy(kalemCizgi.gameObject); kalemCizgi = null; kalemNokta.Clear(); return;
            }
            var xz = new float[kalemNokta.Count * 2];
            for (int i = 0; i < kalemNokta.Count; i++) { xz[i * 2] = kalemNokta[i].x; xz[i * 2 + 1] = kalemNokta[i].z; }
            var kp = arKamera.transform.position;
            int id = oda.SinirCizgisiEkle(xz, kp.x, kp.z);
            kalemParca[id] = kalemCizgi; kalemCizgi = null; kalemNokta.Clear();
            Haptik(10);
        }

        void KalemCiz()
        {
            kalemCizgi.positionCount = kalemNokta.Count;
            for (int i = 0; i < kalemNokta.Count; i++) kalemCizgi.SetPosition(i, kalemNokta[i]);
        }

        /// <summary>Kalem modunda haritanin kendi buldugu zemin siniri: sari ince cizgi (kamera cevresi 7 m).</summary>
        void KenarGoster()
        {
            var s = oda != null ? oda.ZSon : null;
            if (s == null || !s.zeminVar) { if (kenarGo != null) kenarGo.SetActive(false); return; }
            if (kenarGo == null)
            {
                kenarMesh = new Mesh { name = "OtomatikSinir", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                kenarGo = new GameObject("OtomatikSinir"); kenarGo.layer = OdaYoneticisi.KatmanAr;
                kenarGo.AddComponent<MeshFilter>().sharedMesh = kenarMesh;
                var mr = kenarGo.AddComponent<MeshRenderer>(); var km = Resources.Load<Material>("mat_oda_dolgu");
                mr.sharedMaterial = km != null ? km : new Material(Shader.Find("ArTarama/OdaDolgu"));
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            }
            kenarGo.SetActive(true);
            var kp = arKamera.transform.position;
            if (s == kenarSonuc && (new Vector2(kp.x - kenarKam.x, kp.z - kenarKam.z)).sqrMagnitude < 4f) return;
            kenarSonuc = s; kenarKam = kp;
            var e = s.acikKenar; float y = s.zeminY + 0.009f; const float yg = 0.009f, menzil = 7f;
            var v = new List<Vector3>(); var c = new List<Color>(); var tr = new List<int>();
            var renk = new Color(1f, 0.78f, 0.22f, 0.9f);
            for (int i = 0; i + 3 < e.Length; i += 4)
            {
                float ax = e[i], az = e[i + 1], bx = e[i + 2], bz = e[i + 3];
                float mx = (ax + bx) * 0.5f - kp.x, mz = (az + bz) * 0.5f - kp.z; if (mx * mx + mz * mz > menzil * menzil) continue;
                float dx = bx - ax, dz = bz - az, L = Mathf.Sqrt(dx * dx + dz * dz); if (L < 1e-5f) continue;
                float nx = -dz / L * yg, nz = dx / L * yg; int o = v.Count;
                v.Add(new Vector3(ax - nx, y, az - nz)); v.Add(new Vector3(ax + nx, y, az + nz));
                v.Add(new Vector3(bx + nx, y, bz + nz)); v.Add(new Vector3(bx - nx, y, bz - nz));
                for (int q = 0; q < 4; q++) c.Add(renk);
                tr.Add(o); tr.Add(o + 1); tr.Add(o + 2); tr.Add(o); tr.Add(o + 2); tr.Add(o + 3);
                tr.Add(o); tr.Add(o + 2); tr.Add(o + 1); tr.Add(o); tr.Add(o + 3); tr.Add(o + 2);
            }
            kenarMesh.Clear(); kenarMesh.SetVertices(v); kenarMesh.SetColors(c); kenarMesh.SetTriangles(tr, 0, false);
            kenarMesh.bounds = new Bounds(kp, Vector3.one * 40f);
        }

        // ---------------- dokunma / nesne koyma ----------------
        void DokunmaIsle()
        {
            var ts = Touchscreen.current;
            if (ts == null) return;
            var t = ts.primaryTouch;
            if (!t.press.wasPressedThisFrame) return;
            Vector2 sp = t.position.ReadValue();
            if (UiUstunde(sp)) return;
            if (TryYerlestir(sp, out Pose pose, out string neden)) Koy(pose);
            else Toast(neden);
        }

        static readonly List<RaycastResult> uiSonuc = new List<RaycastResult>();
        bool UiUstunde(Vector2 sp)
        {
            if (EventSystem.current == null) return false;
            var e = new PointerEventData(EventSystem.current) { position = sp };
            uiSonuc.Clear();
            EventSystem.current.RaycastAll(e, uiSonuc);
            return uiSonuc.Count > 0;
        }

        bool TryYerlestir(Vector2 sp, out Pose pose, out string neden)
        {
            pose = default;
            neden = "Buraya konmaz: açık zemin seç";
            if (isin == null || !ZeminVar) return false;
            vurus.Clear();
            if (!isin.Raycast(sp, vurus, TrackableType.PlaneWithinPolygon)) return false;
            foreach (var h in vurus)
            {
                var p = duzlem.GetPlane(h.trackableId);
                if (p == null || p.alignment != PlaneAlignment.HorizontalUp) continue;
                if (Mathf.Abs(h.pose.position.y - ZeminY) > 0.15f) continue;
                var yerel = p.transform.InverseTransformPoint(h.pose.position);
                if (KenarMesafesi(p.boundary, new Vector2(yerel.x, yerel.z)) < 0.25f) continue;
                if (oda != null && !oda.AcikZeminMi(h.pose.position)) continue;   // acik zemin haritasinda degil ya da engele 25 cm'den yakin
                if (DerinlikEngelli(sp, h.pose.position)) continue;
                pose = h.pose;
                return true;
            }
            return false;
        }

        /// <summary>Dokunulan noktada derinlik, zemin vurus noktasindan belirgin sekilde yakinsa (onde bir sey var) true.</summary>
        bool DerinlikEngelli(Vector2 sp, Vector3 dunyaNokta)
        {
            if (oklusyon == null || oklusyon.currentEnvironmentDepthMode == EnvironmentDepthMode.Disabled) return false;
            if (!oklusyon.TryAcquireEnvironmentDepthCpuImage(out XRCpuImage img)) return false;
            using (img)
            {
                var pl = img.GetPlane(0);
                float u = 1f - sp.y / Screen.height, v = 1f - sp.x / Screen.width;
                int ix = Mathf.Clamp((int)(u * img.width), 0, img.width - 1);
                int iy = Mathf.Clamp((int)(v * img.height), 0, img.height - 1);
                int off = iy * pl.rowStride + ix * pl.pixelStride;
                float z;
                var d = pl.data;
                if (img.format == XRCpuImage.Format.DepthFloat32)
                    z = System.BitConverter.ToSingle(new[] { d[off], d[off + 1], d[off + 2], d[off + 3] }, 0);
                else if (img.format == XRCpuImage.Format.DepthUint16) z = (d[off] | (d[off + 1] << 8)) / 1000f;
                else return false;
                if (z <= 0.01f || float.IsNaN(z)) return false;
                float hz = Vector3.Dot(dunyaNokta - arKamera.transform.position, arKamera.transform.forward);
                return hz - z > 0.35f && z < hz * 0.75f;
            }
        }

        static float KenarMesafesi(NativeArray<Vector2> poli, Vector2 pt)
        {
            float min = float.MaxValue;
            for (int i = 0; i < poli.Length; i++)
            {
                Vector2 a = poli[i], b = poli[(i + 1) % poli.Length];
                Vector2 ab = b - a; float l2 = ab.sqrMagnitude;
                float t = l2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(pt - a, ab) / l2) : 0f;
                min = Mathf.Min(min, Vector2.Distance(pt, a + ab * t));
            }
            return min;
        }

        void Koy(Pose pose)
        {
            var kok = new GameObject("Capa");
            kok.transform.SetPositionAndRotation(pose.position, Quaternion.Euler(0, arKamera.transform.eulerAngles.y, 0));
            try { kok.AddComponent<ARAnchor>(); } catch (System.Exception e) { Debug.LogWarning("[ArTarama] capa eklenemedi: " + e.Message); }
            Kristal.Olustur(kok.transform);
            nesneler.Add(kok);
            if (plan != null) plan.Kristaller.Add(pose.position);
            Haptik(15);
        }

        /// <summary>Konan nesneler duvar arkasinda (baska odada) ya da 9 m'den uzaktaysa cizilmez.</summary>
        void NesneGorunurlugu()
        {
            if (oda == null || arKamera == null) return;
            var kp = arKamera.transform.position;
            foreach (var g in nesneler)
            {
                if (g == null || g.transform.childCount == 0) continue;
                var p = g.transform.position;
                bool gor = (p - kp).sqrMagnitude < 81f && oda.Gorunurluk(p) > 0.5f;
                var c = g.transform.GetChild(0).gameObject;
                if (c.activeSelf != gor) c.SetActive(gor);
            }
        }

        void Temizle()
        {
            foreach (var g in nesneler) if (g != null) Destroy(g);
            nesneler.Clear();
            if (plan != null) plan.Kristaller.Clear();
        }

        // ---------------- haptik ----------------
        AndroidJavaObject titresim;
        bool titresimDenendi;

        /// <summary>Unity bildirimde VIBRATE iznini yalnizca Handheld.Vibrate kullanimini gorunce ekler; hic cagrilmaz.</summary>
        static void HaptikIzniAnahtari() { if (Time.timeScale < -1f) Handheld.Vibrate(); }

        public void Haptik(int ms = 30)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (!titresimDenendi)
                {
                    titresimDenendi = true;
                    using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
                        titresim = act.Call<AndroidJavaObject>("getSystemService", "vibrator");
                }
                if (titresim == null) return;
                using (var ef = new AndroidJavaClass("android.os.VibrationEffect"))
                using (var e = ef.CallStatic<AndroidJavaObject>("createOneShot", (long)ms, -1))
                    titresim.Call("vibrate", e);
            }
            catch (System.Exception ex) { Debug.LogWarning("[ArTarama] haptik: " + ex.Message); }
#endif
        }
    }
}
