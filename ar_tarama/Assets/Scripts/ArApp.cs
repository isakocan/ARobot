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
    public enum Mod { Baslangic, Tarama, Ozet, Harita }

    /// <summary>Ana denetleyici: tarama akisi, durum/yonlendirme, nesne koyma, isik. Arayuz ArApp.Ui.cs, isik ArApp.Isik.cs.</summary>
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
        public Harita3B harita3b;
        public OdaYoneticisi oda;

        public static ArApp Ornek { get; private set; }
        public static bool ZeminVar { get; private set; }
        public static float ZeminY { get; private set; }
        public bool Takipte { get; private set; }
        public long SonZamanNs { get; private set; } = -1;
        public float AciHizi { get; private set; }
        public float HizMs { get; private set; }
        public Mod mod = Mod.Baslangic;

        readonly TaramaAkisi akis = new TaramaAkisi();
        readonly List<GameObject> nesneler = new List<GameObject>();
        readonly List<ARRaycastHit> vurus = new List<ARRaycastHit>();
        Quaternion oncekiRot; Vector3 oncekiKonum;
        float sonLog, fpsSure; int fpsKare; float sonFps = 60f;
        bool oncedenTakip;
        float planBaslatBekle = -1f, sifirlaZamani = -100f;
        float sonAdimZamani = -10f;
        string adimLog = "";

        void Awake()
        {
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
            if (bulut != null) { bulut.yonetici = nokta; bulut.oklusyon = oklusyon; bulut.kamera = kamera; bulut.arKamera = arKamera; bulut.plan = plan; }
            if (plan != null) { plan.duzlem = duzlem; plan.arKamera = arKamera; }
            if (kayit != null) { kayit.kamera = kamera; kayit.oklusyon = oklusyon; kayit.duzlem = duzlem; kayit.nokta = bulut; kayit.arKamera = arKamera; kayit.plan = plan; kayit.isaret = isaret; }
            if (isaret != null) { isaret.oklusyon = oklusyon; isaret.arKamera = arKamera; isaret.plan = plan; isaret.kayit = kayit; isaret.kaplama = kaplama; }
            if (harita3b != null) { harita3b.bulut = bulut; harita3b.plan = plan; harita3b.arKamera = arKamera; }
            if (oda != null) { oda.duzlem = duzlem; oda.bulut = bulut; oda.arKamera = arKamera; oda.plan = plan; oda.kayit = kayit; oda.harita3b = harita3b; }
            arKamera.cullingMask &= ~((1 << Harita3B.OzelKatman) | (1 << OdaYoneticisi.KatmanMini) | (1 << OdaYoneticisi.KatmanHarita));
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
                Debug.Log($"[ArTarama] durum: mod={mod} oturum={ARSession.state} neden={ARSession.notTrackingReason} kamera=({arKamera.transform.position.x:F2},{arKamera.transform.position.y:F2},{arKamera.transform.position.z:F2}) duzlem={(duzlem != null ? duzlem.trackables.count : 0)} nokta={(bulut != null ? bulut.Sayi : 0)} zemin={(ZeminVar ? ZeminY.ToString("F2") : "yok")} adim={adimLog} kare={(kayit != null ? kayit.KareSayisi : 0)} isaret={(isaret != null ? isaret.Toplam : 0)}({(isaret != null ? isaret.OrtMs : 0):F0}ms) ortDerinlik={(bulut != null ? bulut.OrtancaDerinlik : 0):F2}");
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
            if (probe != null)
            {
                var d = probe.descriptor;
                Debug.Log(d == null || probe.subsystem == null
                    ? "[ArTarama] ORTAM PROBU: altsistem yok (bu cihazda/ARCore'da desteklenmiyor); yansima SH'den uretilen kupten alinacak"
                    : $"[ArTarama] ORTAM PROBU destek: otomatik={d.supportsAutomaticPlacement} doku={d.supportsEnvironmentTexture} hdr={d.supportsEnvironmentTextureHDR}");
            }
        }

        // ---------------- mod gecisleri ----------------
        public bool ModalAcik { get; private set; }

        void GosterMod(Mod m)
        {
            mod = m;
            if (ekranBaslangic != null) ekranBaslangic.Goster(m == Mod.Baslangic);
            if (ekranHud != null) ekranHud.Goster(m == Mod.Tarama);
            if (ekranOzet != null) ekranOzet.Goster(m == Mod.Ozet);
            if (ekranHarita != null) ekranHarita.Goster(m == Mod.Harita);
            bool haritada = m == Mod.Harita;
            if (arKamera != null) arKamera.enabled = !haritada;
            if (harita3b != null) { if (haritada) harita3b.Ac(); else harita3b.Kapat(); }
            if (kayit != null) kayit.Duraklat = haritada;
            if (bulut != null) bulut.Aktif = m == Mod.Tarama && PuruzsuzBaslangicTamam();
            if (isaret != null && kaplama != null) kaplama.gameObject.SetActive(m == Mod.Tarama);
            if (oda != null) oda.Aktif = m == Mod.Tarama;
            if (bulut != null && !haritada) bulut.Goster = true;
            if (odaMini != null && m != Mod.Tarama) odaMini.Guncelle(0.016f, false);
        }

        bool PuruzsuzBaslangicTamam() { return Time.unscaledTime - sifirlaZamani > 1.0f; }

        public void YeniTarama()
        {
            if (kayit != null && kayit.Kayitta) kayit.Durdur(false);
            TemizleHepsi(true);
            kayit.AdimNotu = "";
            kayit.Baslat();
            akis.Sifirla();
            adimLog = "0";
            GosterMod(Mod.Tarama);
            sonAdimZamani = Time.unscaledTime;
            AdimArayuzunuYenile(true);
            Toast("Tarama başladı, kayıt otomatik");
        }

        /// <summary>Sifirla: oturum, bulut, harita ve kayit klasoru silinir, 1. adima donulur.</summary>
        public void SifirlaTarama()
        {
            TemizleHepsi(true);
            if (kayit != null) { kayit.Sil(); kayit.Baslat(); }
            akis.Sifirla();
            adimLog = "0";
            sonAdimZamani = Time.unscaledTime;
            AdimArayuzunuYenile(true);
            Toast("Tarama silindi, baştan başla");
        }

        void TemizleHepsi(bool oturumuSifirla)
        {
            if (oturumuSifirla) { oturum.Reset(); }
            sifirlaZamani = Time.unscaledTime;
            oncedenTakip = false;
            if (bulut != null) bulut.Temizle();
            if (plan != null) plan.Temizle();
            if (harita3b != null) harita3b.Temizle();
            if (oda != null) oda.Temizle();
            if (odaMini != null) odaMini.Sifirla();
            if (isaret != null) isaret.Sifirla();
            Temizle();
            planBaslatBekle = Time.unscaledTime;
            ZeminVar = false;
            if (bulut != null) bulut.Aktif = false;
        }

        public void Bitir(bool tamam)
        {
            if (kayit != null && kayit.Kayitta)
            {
                kayit.AdimNotu = adimLog;
                if (oda != null) oda.Kaydet();   // oda.json son hali (klasor hala acik)
                kayit.Durdur(tamam);
            }
            OzetiDoldur();
            GosterMod(Mod.Ozet);
        }

        public void HaritayiAc()
        {
            haritaOncesi = mod == Mod.Harita ? haritaOncesi : mod;
            GosterMod(Mod.Harita);
            HaritaAcildi();
        }

        Mod haritaOncesi = Mod.Tarama;
        public void HaritadanDon()
        {
            GosterMod(haritaOncesi == Mod.Harita ? Mod.Tarama : haritaOncesi);
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
            if (oda != null) oda.ArGoster = mod == Mod.Tarama && Takipte;
            IsikGuncelle();
            FpsOlc(dt);

            if (planBaslatBekle >= 0f && Takipte && Time.unscaledTime - planBaslatBekle > 0.6f)
            {
                planBaslatBekle = -1f; plan.Baslat(tr.position);
            }
            if (mod == Mod.Tarama && bulut != null) bulut.Aktif = PuruzsuzBaslangicTamam() && Takipte;

            if (mod == Mod.Tarama) TaramaGuncelle(dt);
            else if (mod == Mod.Harita) HaritaGuncelle();
            else if (mod == Mod.Baslangic || mod == Mod.Ozet) { /* arka planda sadece AR */ }

            ToastGuncelle();
            if (mod == Mod.Tarama && !ModalAcik) DokunmaIsle();
        }

        void FpsOlc(float dt)
        {
            fpsSure += dt; fpsKare++;
            if (fpsSure >= 5f)
            {
                sonFps = fpsKare / fpsSure;
                Debug.Log($"[ArTarama] FPS ort={sonFps:F1} (5 sn) mod={mod} nokta={(bulut != null ? bulut.Sayi : 0)}");
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

        // ---------------- tarama adimlari ----------------
        void TaramaGuncelle(float dt)
        {
            bool isaretBitti = isaretAdimiBitti || (isaret != null && isaret.HepsiTamam);
            akis.Isle(dt, arKamera.transform, ZeminVar, ZeminY, AciHizi, Takipte, 0f, isaretBitti);
            // adim tamamlama
            if (akis.Mevcut < TaramaAkisi.AdimSayisi && Time.unscaledTime - sonAdimZamani > 1.0f && !ModalAcik)
            {
                if (akis.AdimKosuluSaglandi(akis.Mevcut, isaretBitti))
                    AdimiBitir(false);
            }
            adimLog = akis.Mevcut.ToString();
            TaramaArayuzuGuncelle(dt);
        }

        bool isaretAdimiBitti;

        public void AdimiBitir(bool atlandi)
        {
            int m = akis.Mevcut;
            if (m >= TaramaAkisi.AdimSayisi) return;
            akis.Tamamlandi[m] = true; akis.Atlandi[m] = atlandi;
            if (!atlandi) { Haptik(); BasariGoster(); }
            akis.Mevcut = m + 1;
            sonAdimZamani = Time.unscaledTime;
            AdimArayuzunuYenile(false);
            if (akis.Mevcut >= TaramaAkisi.AdimSayisi) Toast("Tüm adımlar tamam, taramayı bitirebilirsin");
        }

        public void AdimaGeri()
        {
            if (akis.Mevcut <= 0) return;
            if (akis.Mevcut >= TaramaAkisi.AdimSayisi) akis.Mevcut = TaramaAkisi.AdimSayisi;
            akis.Mevcut = Mathf.Max(0, akis.Mevcut - 1);
            int m = akis.Mevcut;
            akis.Tamamlandi[m] = false; akis.Atlandi[m] = false;
            int k = TaramaAkisi.KategoriDe(m);
            if (k >= 0) System.Array.Clear(akis.Ilerleme[k], 0, TaramaAkisi.Sektor);
            if (m == TaramaAkisi.Isaret) isaretAdimiBitti = false;
            sonAdimZamani = Time.unscaledTime;
            AdimArayuzunuYenile(false);
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
                if (oda != null && !oda.AcikZeminMi(h.pose.position)) continue;   // kutu/duvar yakininda konmaz
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
