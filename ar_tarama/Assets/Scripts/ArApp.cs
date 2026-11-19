using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ArTarama
{
    /// <summary>Ana denetleyici: durum/rehber, kapsama, nesne koyma, isik tahmini, arayuz.</summary>
    public class ArApp : MonoBehaviour
    {
        public ARSession oturum;
        public ARCameraManager kamera;
        public AROcclusionManager oklusyon;
        public ARPlaneManager duzlem;
        public ARRaycastManager isin;
        public ARPointCloudManager nokta;
        public Camera arKamera;
        public Light isik;
        public NoktaBulutu bulut;
        public KayitYoneticisi kayit;

        public static ArApp Ornek { get; private set; }
        public static bool ZeminVar { get; private set; }
        public static float ZeminY { get; private set; }
        public bool Takipte { get; private set; }
        public long SonZamanNs { get; private set; } = -1;

        // arayuz
        Image durumNokta; Text durumYazi, rehberYazi, sayacYazi, kayitYazi, ipucuYazi, toastYazi;
        CanvasGroup toastGrup; float toastBitis;
        readonly Image[][] hucre = new Image[3][];
        readonly Text[] yuzdeYazi = new Text[3];
        Image kayitDugmeArka, noktaDugmeArka, duzlemDugmeArka; Text kayitDugmeYazi;
        RectTransform guvenliKok;
        Rect sonGuvenli;

        readonly KapsamaTakip kapsama = new KapsamaTakip();
        readonly List<GameObject> nesneler = new List<GameObject>();
        readonly List<ARRaycastHit> vurus = new List<ARRaycastHit>();
        Quaternion oncekiRot; float aciHizi;
        float sonDurum;
        float sonLog;

        static readonly string[] KatEtiket = { "Göz hizası", "Alçak tur", "Tavan" };

        void Awake()
        {
            Ornek = this;
            Application.targetFrameRate = 30;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
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
            if (bulut != null) bulut.yonetici = nokta;
            if (kayit != null) { kayit.kamera = kamera; kayit.oklusyon = oklusyon; kayit.duzlem = duzlem; kayit.nokta = bulut; kayit.arKamera = arKamera; }
            oncekiRot = arKamera.transform.rotation;
            StartCoroutine(Tanilama());
        }

        void OnDestroy() { if (kamera != null) kamera.frameReceived -= KareGeldi; }

        // ---------------- tanilama / kamera ayari ----------------
        IEnumerator Tanilama()
        {
            // oturum calisana kadar bekle
            float t0 = Time.realtimeSinceStartup;
            while (ARSession.state < ARSessionState.SessionInitializing && Time.realtimeSinceStartup - t0 < 20f) yield return null;
            yield return new WaitForSeconds(1.5f);
            KameraAyariSec();
            yield return new WaitForSeconds(2.5f);
            LogCokluDestek();
            while (true)
            {
                yield return new WaitForSeconds(4f);
                Debug.Log($"[ArTarama] durum: oturum={ARSession.state} neden={ARSession.notTrackingReason} kamera=({arKamera.transform.position.x:F2},{arKamera.transform.position.y:F2},{arKamera.transform.position.z:F2}) duzlem={(duzlem != null ? duzlem.trackables.count : 0)} nokta={(bulut != null ? bulut.Sayi : 0)} zemin={(ZeminVar ? ZeminY.ToString("F2") : "yok")} derinlikModu={(oklusyon != null ? oklusyon.currentEnvironmentDepthMode.ToString() : "-")} depth={(oklusyon != null && oklusyon.subsystem != null ? oklusyon.subsystem.running.ToString() : "-")}");
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

        // ---------------- isik tahmini ----------------
        bool isikLogEdildi;
        void KareGeldi(ARCameraFrameEventArgs a)
        {
            if (a.timestampNs.HasValue) SonZamanNs = a.timestampNs.Value;
            var le = a.lightEstimation;
            if (isik == null) return;
            if (!isikLogEdildi && (le.mainLightDirection.HasValue || le.averageBrightness.HasValue || le.ambientSphericalHarmonics.HasValue))
            {
                isikLogEdildi = true;
                Debug.Log($"[ArTarama] ISIK ilk veri: parlaklik={le.averageBrightness} lumen={le.mainLightIntensityLumens} yon={le.mainLightDirection} renk={le.mainLightColor} SH={le.ambientSphericalHarmonics.HasValue} duzeltme={le.colorCorrection}");
            }
            if (le.mainLightDirection.HasValue && le.mainLightDirection.Value.sqrMagnitude > 0.0001f)
                isik.transform.rotation = Quaternion.LookRotation(le.mainLightDirection.Value);
            if (le.mainLightColor.HasValue) isik.color = le.mainLightColor.Value;
            else if (le.colorCorrection.HasValue) isik.color = le.colorCorrection.Value;
            if (le.mainLightIntensityLumens.HasValue)
            {
                float l = le.mainLightIntensityLumens.Value;
                isik.intensity = Mathf.Clamp(l < 20f ? l : l / 2000f, 0.15f, 2f);
            }
            else if (le.averageBrightness.HasValue) isik.intensity = Mathf.Clamp(le.averageBrightness.Value * 1.3f, 0.15f, 1.5f);
            if (le.ambientSphericalHarmonics.HasValue)
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Custom;
                RenderSettings.ambientProbe = le.ambientSphericalHarmonics.Value;
            }
            else if (le.averageBrightness.HasValue)
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = Color.white * Mathf.Clamp(le.averageBrightness.Value, 0.1f, 1f);
            }
        }

        // ---------------- ana dongu ----------------
        void Update()
        {
            GuvenliAlan();
            var tr = arKamera.transform;
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);
            aciHizi = Mathf.Lerp(aciHizi, Quaternion.Angle(oncekiRot, tr.rotation) / dt, 0.2f);
            oncekiRot = tr.rotation;

            Takipte = ARSession.state == ARSessionState.SessionTracking && ARSession.notTrackingReason == NotTrackingReason.None;

            ZeminuGuncelle();
            if (Takipte && aciHizi < 80f) kapsama.Isle(tr.position, tr.forward, ZeminVar, ZeminY);

            if (Time.unscaledTime - sonDurum > 0.2f) { sonDurum = Time.unscaledTime; DurumYaz(); KapsamaYaz(); }
            ToastGuncelle();
            DokunmaIsle();
        }

        void ZeminuGuncelle()
        {
            bool var = false; float y = float.MaxValue;
            if (duzlem != null)
                foreach (var p in duzlem.trackables)
                    if (p.alignment == PlaneAlignment.HorizontalUp && p.trackingState != TrackingState.None)
                    { var = true; y = Mathf.Min(y, p.transform.position.y); }
            ZeminVar = var; ZeminY = var ? y : 0f;
        }

        // ---------------- durum / rehber ----------------
        void DurumYaz()
        {
            string durum; Color renk;
            var st = ARSession.state; var neden = ARSession.notTrackingReason;
            if (st == ARSessionState.SessionTracking && neden == NotTrackingReason.None) { durum = "Takip iyi"; renk = UiKur.Vurgu; }
            else if (st == ARSessionState.SessionTracking || st == ARSessionState.SessionInitializing && neden != NotTrackingReason.None)
            {
                renk = UiKur.Uyari;
                switch (neden)
                {
                    case NotTrackingReason.ExcessiveMotion: durum = "Takip sınırlı: yavaş hareket et"; break;
                    case NotTrackingReason.InsufficientLight: durum = "Takip sınırlı: daha çok ışık"; break;
                    case NotTrackingReason.InsufficientFeatures: durum = "Takip sınırlı: dokulu yere bak"; break;
                    case NotTrackingReason.Initializing: durum = "Takip başlıyor…"; break;
                    case NotTrackingReason.Relocalizing: durum = "Takip sınırlı: konum yeniden aranıyor"; break;
                    default: durum = "Takip sınırlı"; break;
                }
            }
            else { durum = "Takip yok"; renk = UiKur.Kirmizi; }
            durumYazi.text = durum; durumNokta.color = renk;

            string rehber;
            if (aciHizi > 90f) rehber = "Biraz yavaşla";
            else if (duzlem == null || duzlem.trackables.count == 0) rehber = "Telefonu yere doğru tutup yavaşça gezdir";
            else if (!ZeminVar) rehber = "Zemini bul: telefonu yere doğru yönelt";
            else rehber = "Dokunarak zemine nesne koyabilirsin";
            rehberYazi.text = rehber;

            int np = duzlem != null ? duzlem.trackables.count : 0;
            int nn = bulut != null ? bulut.Sayi : 0;
            int nk = kayit != null ? kayit.KareSayisi : 0;
            sayacYazi.text = $"Düzlem {np}    Nokta {nn}    Kare {nk}";

            if (kayit != null && kayit.Kayitta)
            {
                int s = (int)kayit.Sure;
                kayitYazi.text = $"● KAYIT {s / 60:00}:{s % 60:00}";
                kayitYazi.color = UiKur.Kirmizi;
                kayitDugmeYazi.text = "Kaydı durdur"; kayitDugmeArka.color = new Color(0.9f, 0.25f, 0.25f, 0.55f);
            }
            else
            {
                kayitYazi.text = "";
                kayitDugmeYazi.text = "Kaydı başlat"; kayitDugmeArka.color = UiKur.PanelAcik;
            }
        }

        void KapsamaYaz()
        {
            for (int k = 0; k < 3; k++)
            {
                for (int i = 0; i < KapsamaTakip.Sektor; i++)
                {
                    bool d = kapsama.Dolu[k][i];
                    Color c = d ? UiKur.Vurgu : new Color(1f, 1f, 1f, 0.12f);
                    if (i == kapsama.SonSektor && !d) c = new Color(1f, 1f, 1f, 0.35f);
                    hucre[k][i].color = c;
                }
                yuzdeYazi[k].text = Mathf.RoundToInt(kapsama.Oran(k) * 100f) + "%";
            }
            string ip;
            if (!ZeminVar) ip = "Önce zemini bul";
            else
            {
                int en = 0; float eo = 2f;
                for (int k = 0; k < 3; k++) if (kapsama.Oran(k) < eo) { eo = kapsama.Oran(k); en = k; }
                if (eo >= 0.9f) ip = "Kapsama tamam, kaydı durdurabilirsin";
                else if (en == KapsamaTakip.Goz) ip = "Telefonu göz hizasında tut, yavaşça bir tur dön";
                else if (en == KapsamaTakip.Alcak) ip = "Şimdi telefonu yere yaklaştır, bir tur daha";
                else ip = "Tavanı da tara";
            }
            ipucuYazi.text = ip;
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
                // dikey ekran: sensor goruntusu 90 derece donuk (varsayim)
                float u = 1f - sp.y / Screen.height, v = 1f - sp.x / Screen.width;
                int ix = Mathf.Clamp((int)(u * img.width), 0, img.width - 1);
                int iy = Mathf.Clamp((int)(v * img.height), 0, img.height - 1);
                int off = iy * pl.rowStride + ix * pl.pixelStride;
                float z;
                var d = pl.data;
                if (img.format == XRCpuImage.Format.DepthFloat32)
                {
                    z = System.BitConverter.ToSingle(new[] { d[off], d[off + 1], d[off + 2], d[off + 3] }, 0);
                }
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
        }

        void Temizle()
        {
            foreach (var g in nesneler) if (g != null) Destroy(g);
            nesneler.Clear();
        }

        // ---------------- arayuz ----------------
        void Toast(string s) { toastYazi.text = s; toastBitis = Time.unscaledTime + 2.2f; }

        void ToastGuncelle()
        {
            float kalan = toastBitis - Time.unscaledTime;
            toastGrup.alpha = Mathf.Clamp01(kalan * 2.5f);
        }

        void GuvenliAlan()
        {
            var sa = Screen.safeArea;
            if (sa == sonGuvenli) return;
            sonGuvenli = sa;
            guvenliKok.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
            guvenliKok.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
            guvenliKok.offsetMin = guvenliKok.offsetMax = Vector2.zero;
        }

        void UiHazirla()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            var cgo = new GameObject("Arayuz", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var cs = cgo.GetComponent<CanvasScaler>();
            cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1080, 2400);
            cs.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            cs.matchWidthOrHeight = 0.5f;

            guvenliKok = UiKur.Obj("GuvenliAlan", cgo.transform);
            UiKur.Ankor(guvenliKok, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // ---- ust: durum paneli ----
            var p1 = UiKur.Kutu(guvenliKok, "DurumPanel", UiKur.Panel);
            p1.raycastTarget = true; var r1 = p1.rectTransform;
            r1.anchorMin = new Vector2(0, 1); r1.anchorMax = new Vector2(1, 1); r1.pivot = new Vector2(0.5f, 1);
            r1.anchoredPosition = new Vector2(0, -20); r1.sizeDelta = new Vector2(-40, 196);

            durumNokta = UiKur.Nokta(p1.transform, "Nokta", UiKur.Uyari, 24);
            durumNokta.rectTransform.anchorMin = durumNokta.rectTransform.anchorMax = new Vector2(0, 1);
            durumNokta.rectTransform.anchoredPosition = new Vector2(38, -40);
            durumYazi = UiKur.Metin(p1.transform, "Durum", "Takip yok", 32, UiKur.Beyaz, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKur.Ankor(durumYazi.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(66, -66), new Vector2(-250, -14));
            kayitYazi = UiKur.Metin(p1.transform, "KayitSayac", "", 26, UiKur.Kirmizi, TextAnchor.MiddleRight, FontStyle.Bold);
            UiKur.Ankor(kayitYazi.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-250, -66), new Vector2(-24, -14));
            rehberYazi = UiKur.Metin(p1.transform, "Rehber", "", 27, UiKur.Soluk);
            UiKur.Ankor(rehberYazi.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -118), new Vector2(-24, -70));
            sayacYazi = UiKur.Metin(p1.transform, "Sayac", "", 24, UiKur.Vurgu);
            UiKur.Ankor(sayacYazi.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(28, 12), new Vector2(-24, 62));

            // ---- kapsama paneli ----
            var p2 = UiKur.Kutu(guvenliKok, "KapsamaPanel", UiKur.Panel);
            p2.raycastTarget = true; var r2 = p2.rectTransform;
            r2.anchorMin = new Vector2(0, 1); r2.anchorMax = new Vector2(1, 1); r2.pivot = new Vector2(0.5f, 1);
            r2.anchoredPosition = new Vector2(0, -230); r2.sizeDelta = new Vector2(-40, 262);
            var bas = UiKur.Metin(p2.transform, "Baslik", "KAPSAMA", 22, UiKur.Soluk, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKur.Ankor(bas.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -50), new Vector2(-24, -10));
            for (int k = 0; k < 3; k++)
            {
                float y = -58 - k * 54;
                var et = UiKur.Metin(p2.transform, "Etiket" + k, KatEtiket[k], 25, UiKur.Beyaz);
                UiKur.Ankor(et.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, y - 44), new Vector2(228, y));
                var sira = UiKur.Obj("Hucreler" + k, p2.transform);
                UiKur.Ankor(sira, new Vector2(0, 1), new Vector2(1, 1), new Vector2(236, y - 40), new Vector2(-110, y - 4));
                var hg = sira.gameObject.AddComponent<HorizontalLayoutGroup>();
                hg.spacing = 6; hg.childForceExpandWidth = true; hg.childForceExpandHeight = true;
                hg.childControlWidth = true; hg.childControlHeight = true;
                hucre[k] = new Image[KapsamaTakip.Sektor];
                for (int i = 0; i < KapsamaTakip.Sektor; i++)
                    hucre[k][i] = UiKur.Kutu(sira, "H" + i, new Color(1, 1, 1, 0.12f));
                yuzdeYazi[k] = UiKur.Metin(p2.transform, "Yuzde" + k, "0%", 24, UiKur.Vurgu, TextAnchor.MiddleRight);
                UiKur.Ankor(yuzdeYazi[k].rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-100, y - 44), new Vector2(-24, y));
            }
            ipucuYazi = UiKur.Metin(p2.transform, "Ipucu", "", 25, UiKur.Uyari);
            UiKur.Ankor(ipucuYazi.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(28, 10), new Vector2(-24, 52));

            // ---- alt: dugmeler ----
            var bar = UiKur.Kutu(guvenliKok, "AltBar", UiKur.Panel);
            bar.raycastTarget = true; var rb = bar.rectTransform;
            rb.anchorMin = new Vector2(0, 0); rb.anchorMax = new Vector2(1, 0); rb.pivot = new Vector2(0.5f, 0);
            rb.anchoredPosition = new Vector2(0, 20); rb.sizeDelta = new Vector2(-40, 132);
            var yatay = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            yatay.padding = new RectOffset(14, 14, 14, 14); yatay.spacing = 12;
            yatay.childForceExpandWidth = true; yatay.childForceExpandHeight = true;
            yatay.childControlWidth = true; yatay.childControlHeight = true;

            Text yz;
            var b1 = UiKur.Dugme(bar.transform, "KayitDugme", "Kaydı başlat", out kayitDugmeArka, out kayitDugmeYazi);
            b1.onClick.AddListener(() => { if (kayit == null) return; if (kayit.Kayitta) { kayit.Durdur(); Toast("Kayıt tamam: " + kayit.SonKlasor); } else { kapsama.Sifirla(); kayit.Baslat(); } });
            var b2 = UiKur.Dugme(bar.transform, "NoktaDugme", "Noktalar", out noktaDugmeArka, out yz);
            b2.onClick.AddListener(() => { bulut.Goster = !bulut.Goster; noktaDugmeArka.color = bulut.Goster ? new Color(0.27f, 0.87f, 0.79f, 0.30f) : UiKur.PanelAcik; });
            noktaDugmeArka.color = new Color(0.27f, 0.87f, 0.79f, 0.30f);
            var b3 = UiKur.Dugme(bar.transform, "DuzlemDugme", "Düzlemler", out duzlemDugmeArka, out yz);
            b3.onClick.AddListener(() => { PlaneStyle.Goster = !PlaneStyle.Goster; duzlemDugmeArka.color = PlaneStyle.Goster ? new Color(0.27f, 0.87f, 0.79f, 0.30f) : UiKur.PanelAcik; });
            duzlemDugmeArka.color = new Color(0.27f, 0.87f, 0.79f, 0.30f);
            Image ta;
            var b4 = UiKur.Dugme(bar.transform, "TemizleDugme", "Temizle", out ta, out yz);
            b4.onClick.AddListener(Temizle);

            // ---- toast ----
            var tk = UiKur.Kutu(guvenliKok, "Toast", new Color(0.035f, 0.055f, 0.075f, 0.86f));
            var rt = tk.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.28f);
            rt.sizeDelta = new Vector2(820, 96);
            toastGrup = tk.gameObject.AddComponent<CanvasGroup>();
            toastGrup.alpha = 0; toastGrup.blocksRaycasts = false; toastGrup.interactable = false;
            toastYazi = UiKur.Metin(tk.transform, "Yazi", "", 28, UiKur.Beyaz, TextAnchor.MiddleCenter);
            UiKur.Ankor(toastYazi.rectTransform, Vector2.zero, Vector2.one, new Vector2(16, 6), new Vector2(-16, -6));
        }
    }
}
