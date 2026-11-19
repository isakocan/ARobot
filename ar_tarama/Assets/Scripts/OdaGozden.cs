using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ArTarama
{
    /// <summary>Dokunma hareketleri: tek parmak surukle, iki parmak yakinlastir + kaydir, hizli dokunus ve cift dokunus.</summary>
    public class DokunmaOkuyucu
    {
        public RectTransform alan;
        public Vector2 tekDelta;       // piksel (bu kare)
        public float yakinlik = 1f;    // bu karedeki olcek carpani
        public Vector2 ciftDelta;      // iki parmak orta noktasi hareketi (piksel)
        public int Parmak;
        public bool Birakildi;         // bu karede tum parmaklar kalkti
        public bool Dokunus; public Vector2 DokunusPoz; public bool CiftDokunus;
        class T { public float zaman; public Vector2 bas; public float yol; public bool izinli; }
        readonly Dictionary<int, T> izler = new Dictionary<int, T>();
        float oncekiMesafe, sonDokunusZaman = -10f; Vector2 sonDokunusPoz; int maksParmak;

        public void Oku()
        {
            tekDelta = Vector2.zero; yakinlik = 1f; ciftDelta = Vector2.zero; Parmak = 0; Birakildi = false; Dokunus = false; CiftDokunus = false;
            var ts = Touchscreen.current;
            if (ts == null) return;
            var aktif = new List<UnityEngine.InputSystem.Controls.TouchControl>(2);
            foreach (var t in ts.touches)
            {
                var ph = t.phase.ReadValue(); int id = t.touchId.ReadValue();
                Vector2 pos = t.position.ReadValue();
                if (ph == UnityEngine.InputSystem.TouchPhase.Began)
                    izler[id] = new T { zaman = Time.unscaledTime, bas = pos, izinli = alan == null || RectTransformUtility.RectangleContainsScreenPoint(alan, pos) };
                if (!izler.TryGetValue(id, out var iz)) continue;
                if (ph == UnityEngine.InputSystem.TouchPhase.Moved) iz.yol += t.delta.ReadValue().magnitude;
                if (ph == UnityEngine.InputSystem.TouchPhase.Ended)
                {
                    if (iz.izinli && maksParmak <= 1 && Time.unscaledTime - iz.zaman < 0.28f && iz.yol < 24f)
                    {
                        Dokunus = true; DokunusPoz = pos;
                        if (Time.unscaledTime - sonDokunusZaman < 0.38f && (pos - sonDokunusPoz).magnitude < 90f) { CiftDokunus = true; sonDokunusZaman = -10f; }
                        else { sonDokunusZaman = Time.unscaledTime; sonDokunusPoz = pos; }
                    }
                    izler.Remove(id); continue;
                }
                if (ph == UnityEngine.InputSystem.TouchPhase.Canceled) { izler.Remove(id); continue; }
                if (ph == UnityEngine.InputSystem.TouchPhase.None) continue;
                if (iz.izinli) aktif.Add(t);
            }
            Parmak = aktif.Count;
            if (Parmak > maksParmak) maksParmak = Parmak;
            if (aktif.Count == 1) { tekDelta = aktif[0].delta.ReadValue(); oncekiMesafe = 0; }
            else if (aktif.Count >= 2)
            {
                Vector2 a = aktif[0].position.ReadValue(), b = aktif[1].position.ReadValue();
                Vector2 da = aktif[0].delta.ReadValue(), db = aktif[1].delta.ReadValue();
                float m = Vector2.Distance(a, b);
                if (oncekiMesafe > 1f) yakinlik = m / oncekiMesafe;
                oncekiMesafe = m; ciftDelta = (da + db) * 0.5f;
            }
            else oncekiMesafe = 0;
            if (aktif.Count == 0 && izler.Count == 0) { if (maksParmak > 0) Birakildi = true; maksParmak = 0; }
        }
    }

    /// <summary>
    /// Inceleme ekrani: tam ekran 3B oda gorunumu. Tek parmak yumusak yorunge (atalet), iki parmak yakinlastir + kaydir,
    /// cift dokunus sifirlar, "3B / Ustten" gecisi (ustten = ortografik, duvar uzunluklari yazili), kutuya dokununca olculeri.
    /// </summary>
    public class OdaGozden : MonoBehaviour
    {
        public Camera kam;
        public RectTransform alan;        // dokunma alani
        public RectTransform etiketKok;   // duvar/kutu etiketleri icin ekran kaplamasi
        public Material katiMateryal;
        public bool Acik { get; private set; }
        public bool Ust { get; private set; }
        public System.Action<bool> UstDegisti;

        OdaKatiModel model; OdaGoruntu goruntu;
        readonly DokunmaOkuyucu dokunma = new DokunmaOkuyucu();
        float yaw, pitch, mesafe, hYaw, hPitch, hMesafe; Vector3 hedef, hHedef;
        float yawHiz, pitchHiz;
        float hedefMesafe; Vector3 hedefMerkez; float hedefPitch, hedefYaw; bool hedefYawVar;
        const float Fov = 40f, UstPitch = 89.9f, AnaPitch = 52f, AnaYaw = 32f;
        readonly List<Text> duvarYazi = new List<Text>();
        Image kutuKapsul; Text kutuYazi; int secilenKutu = -1; Vector3 secilenNokta;

        void Awake()
        {
            kam.enabled = false;
            kam.clearFlags = CameraClearFlags.SolidColor; kam.backgroundColor = new Color(0.047f, 0.055f, 0.067f, 1f);
            kam.cullingMask = 1 << OdaYoneticisi.KatmanHarita;
            kam.fieldOfView = Fov; kam.depth = 20; kam.allowHDR = false; kam.allowMSAA = true; kam.useOcclusionCulling = false;
        }

        void OnDestroy() { if (model != null) model.Yok(); }

        public void Ac(OdaGoruntu g)
        {
            goruntu = g;
            if (model == null) model = new OdaKatiModel(OdaYoneticisi.KatmanHarita, katiMateryal, "OdaGozdenModel");
            Acik = true; kam.enabled = true; Ust = false; secilenKutu = -1; if (kutuKapsul != null) kutuKapsul.gameObject.SetActive(false);
            Sifirla(true);
            model.Guncelle(goruntu, kam, true, false, Vector3.zero, 0f, false);
            Sigdir(out hHedef, out hMesafe);
            hedef = hHedef; mesafe = hMesafe; hedefMesafe = hMesafe; hedefMerkez = hHedef;
            Sifirla(true);
        }

        public void Kapat()
        {
            Acik = false; kam.enabled = false;
            if (model != null) model.Guncelle(goruntu ?? new OdaGoruntu(), null, false, false, Vector3.zero, 0f, false);
            foreach (var t in duvarYazi) t.gameObject.SetActive(false);
            if (kutuKapsul != null) kutuKapsul.gameObject.SetActive(false);
        }

        void Sifirla(bool anlik)
        {
            if (anlik) { yaw = hYaw = AnaYaw; pitch = hPitch = AnaPitch; mesafe = hMesafe; hedef = hHedef; }
            hedefPitch = AnaPitch; hedefYaw = AnaYaw; hedefYawVar = true; hedefMesafe = hMesafe; hedefMerkez = hHedef; yawHiz = pitchHiz = 0f;
        }

        /// <summary>Modele sigdirma: merkez + mesafe (yatay gorus acisina gore).</summary>
        void Sigdir(out Vector3 merkez, out float dist)
        {
            var b = model.Sinir; float R = model.Dolu ? model.Yaricap : 2.5f;
            merkez = model.Dolu ? new Vector3(b.center.x, goruntu.GZeminY + 0.4f, b.center.z) : Vector3.zero;
            float asp = Mathf.Max(0.3f, kam.pixelWidth > 0 ? kam.pixelWidth / (float)kam.pixelHeight : Screen.width / (float)Screen.height);
            float vHalf = Fov * 0.5f * Mathf.Deg2Rad;
            dist = R * 0.92f / (Mathf.Min(1f, asp) * Mathf.Tan(vHalf));
        }

        public void UstGoster(bool ust)
        {
            if (Ust == ust) return;
            Ust = ust; secilenKutu = -1; if (kutuKapsul != null) kutuKapsul.gameObject.SetActive(false);
            if (ust)
            {
                hedefPitch = UstPitch;
                GDuvar en = null; foreach (var d in goruntu.Duvarlar) if (d.kesin && (en == null || d.Uzunluk > en.Uzunluk)) en = d;
                if (en != null)
                {
                    float th = Mathf.Atan2(en.bz - en.az, en.bx - en.ax) * Mathf.Rad2Deg, hy = -th;
                    hy += 90f * Mathf.Round((yaw - hy) / 90f);
                    hedefYaw = hy; hedefYawVar = true;
                }
            }
            else { hedefPitch = AnaPitch; }
            UstDegisti?.Invoke(ust);
        }

        void LateUpdate()
        {
            if (!Acik || goruntu == null) return;
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0005f);
            dokunma.alan = alan; dokunma.Oku();

            bool ortho = Ust && Mathf.Abs(pitch - UstPitch) < 0.2f;
            float pxBirim = ortho ? 2f * mesafe * Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad) / Screen.height : mesafe * 2f * Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad) / Screen.height;

            // ---- girdi ----
            if (dokunma.Parmak == 1)
            {
                if (Ust || ortho) PanUygula(dokunma.tekDelta, pxBirim);
                else
                {
                    float dy = dokunma.tekDelta.x * 0.22f, dp = -dokunma.tekDelta.y * 0.22f;
                    yaw += dy; pitch = Mathf.Clamp(pitch + dp, 6f, 89f);
                    yawHiz = Mathf.Lerp(yawHiz, dy / dt, 0.5f); pitchHiz = Mathf.Lerp(pitchHiz, dp / dt, 0.5f);
                    hedefPitch = pitch; hedefYawVar = false;
                }
            }
            else if (dokunma.Parmak >= 2)
            {
                hedefMesafe = Mathf.Clamp(hedefMesafe / Mathf.Clamp(dokunma.yakinlik, 0.85f, 1.18f), hMesafe * 0.12f, hMesafe * 3f);
                PanUygula(dokunma.ciftDelta, pxBirim);
                yawHiz = pitchHiz = 0f;
            }
            else if (!Ust)
            {
                // atalet: birakinca hiz ussel olarak sonumlenir
                if (Mathf.Abs(yawHiz) > 0.5f || Mathf.Abs(pitchHiz) > 0.5f)
                {
                    yaw += yawHiz * dt; pitch = Mathf.Clamp(pitch + pitchHiz * dt, 6f, 89f);
                    float s = Mathf.Exp(-3.2f * dt); yawHiz *= s; pitchHiz *= s; hedefPitch = pitch;
                }
                else { yawHiz = pitchHiz = 0f; }
            }
            if (dokunma.CiftDokunus) { Sifirla(false); if (Ust) hedefPitch = UstPitch; }
            else if (dokunma.Dokunus) Dokun(dokunma.DokunusPoz);

            // ---- yumusatma ----
            float k = 1f - Mathf.Exp(-9f * dt);
            mesafe = Mathf.Lerp(mesafe, hedefMesafe, 1f - Mathf.Exp(-12f * dt));
            hedef = Vector3.Lerp(hedef, hedefMerkez, k);
            if (dokunma.Parmak == 0 && Mathf.Abs(yawHiz) < 0.5f && Mathf.Abs(pitchHiz) < 0.5f) pitch = Mathf.Lerp(pitch, hedefPitch, 1f - Mathf.Exp(-8f * dt));
            if (hedefYawVar && dokunma.Parmak == 0) { yaw = Mathf.LerpAngle(yaw, hedefYaw, 1f - Mathf.Exp(-8f * dt)); if (Mathf.Abs(Mathf.DeltaAngle(yaw, hedefYaw)) < 0.05f) hedefYawVar = false; }

            // ---- kamera ----
            bool orto = Ust && Mathf.Abs(pitch - UstPitch) < 0.2f;
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            kam.orthographic = orto;
            if (orto) { kam.orthographicSize = mesafe * Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad); }
            kam.transform.SetPositionAndRotation(hedef - rot * Vector3.forward * mesafe, rot);
            kam.nearClipPlane = Mathf.Max(0.05f, mesafe * 0.04f); kam.farClipPlane = mesafe * 4f + 20f;

            model.Guncelle(goruntu, kam, true, false, Vector3.zero, 0f, !orto && !Ust);
            EtiketleriGuncelle(orto);
        }

        void PanUygula(Vector2 delta, float pxBirim)
        {
            var t = kam.transform;
            Vector3 sag = t.right; sag.y = 0; sag.Normalize();
            Vector3 ileri = t.up; ileri.y = 0; if (ileri.sqrMagnitude < 1e-4f) ileri = t.forward; ileri.y = 0; ileri.Normalize();
            Vector3 d = -(sag * delta.x + ileri * delta.y) * pxBirim;
            hedefMerkez += d; hedef += d;
        }

        // ---- dokunma: kutu secimi ----
        void Dokun(Vector2 sp)
        {
            var ray = kam.ScreenPointToRay(sp);
            int en = -1; float enT = float.MaxValue;
            for (int i = 0; i < goruntu.Kutular.Count; i++)
            {
                var b = goruntu.Kutular[i]; if (!b.kesin) continue;
                if (IsinKutu(ray, b, out float t) && t < enT) { enT = t; en = i; }
            }
            if (en < 0) { secilenKutu = -1; if (kutuKapsul != null) kutuKapsul.gameObject.SetActive(false); return; }
            secilenKutu = goruntu.Kutular[en].id;
        }

        static bool IsinKutu(Ray r, GKutu b, out float t)
        {
            t = 0;
            var q = Quaternion.Euler(0, -b.aci, 0);
            var inv = Quaternion.Inverse(q);
            Vector3 o = inv * (r.origin - new Vector3(b.cx, b.cy, b.cz)), d = inv * r.direction;
            Vector3 h = new Vector3(b.sx * 0.5f, b.sy * 0.5f, b.sz * 0.5f);
            float t0 = 0, t1 = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                float oi = o[i], di = d[i], hi = h[i];
                if (Mathf.Abs(di) < 1e-8f) { if (Mathf.Abs(oi) > hi) return false; continue; }
                float a = (-hi - oi) / di, c = (hi - oi) / di; if (a > c) { float x = a; a = c; c = x; }
                t0 = Mathf.Max(t0, a); t1 = Mathf.Min(t1, c); if (t0 > t1) return false;
            }
            t = t0; return true;
        }

        static string M(float v) { return v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ','); }

        void EtiketleriGuncelle(bool orto)
        {
            // duvar uzunluklari: yalniz ustten gorunumde
            int n = 0;
            if (orto)
                foreach (var d in goruntu.Duvarlar)
                {
                    if (!d.kesin || d.Uzunluk < 0.5f) continue;
                    while (duvarYazi.Count <= n) duvarYazi.Add(EtiketOlustur("DuvarEtiket" + duvarYazi.Count));
                    Vector3 mid = new Vector3((d.ax + d.bx) * 0.5f - d.nx * 0.32f, goruntu.GZeminY, (d.az + d.bz) * 0.5f - d.nz * 0.32f);
                    Vector3 sp = kam.WorldToScreenPoint(mid);
                    var yz = duvarYazi[n]; yz.gameObject.SetActive(sp.z > 0);
                    yz.text = M(d.Uzunluk) + " m";
                    PozisyonYaz(yz.transform.parent as RectTransform, sp);
                    n++;
                }
            for (int i = n; i < duvarYazi.Count; i++) duvarYazi[i].gameObject.SetActive(false);

            // secili kutu etiketi
            GKutu kb = null; foreach (var b in goruntu.Kutular) if (b.id == secilenKutu) kb = b;
            if (kb == null) { if (kutuKapsul != null) kutuKapsul.gameObject.SetActive(false); return; }
            if (kutuKapsul == null) KutuEtiketiOlustur();
            kutuKapsul.gameObject.SetActive(true);
            kutuYazi.text = M(kb.sx) + " × " + M(kb.sz) + " × " + M(kb.sy) + " m";
            Vector3 sp2 = kam.WorldToScreenPoint(new Vector3(kb.cx, kb.cy + kb.sy * 0.5f, kb.cz));
            PozisyonYaz(kutuKapsul.rectTransform, sp2 + new Vector3(0, 70f, 0));
        }

        void PozisyonYaz(RectTransform rt, Vector2 sp)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(etiketKok, sp, null, out Vector2 lp);
            rt.anchoredPosition = lp;
        }

        Text EtiketOlustur(string ad)
        {
            var kapsul = UiKur.Kutu(etiketKok, ad, new Color(0.063f, 0.07f, 0.086f, 0.72f), false);
            kapsul.raycastTarget = false;
            var rt = kapsul.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(150, 48);
            var yz = UiKur.Metin(rt, "Y", "", UiKur.Caption, UiKur.MetinRenk, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKur.Tam(yz.rectTransform); yz.horizontalOverflow = HorizontalWrapMode.Overflow;
            return yz;
        }

        void KutuEtiketiOlustur()
        {
            kutuKapsul = UiKur.Kutu(etiketKok, "KutuEtiket", UiKur.PanelKoyu, true);
            kutuKapsul.raycastTarget = false;
            var rt = kutuKapsul.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(380, 64);
            kutuYazi = UiKur.Metin(rt, "Y", "", UiKur.Body, UiKur.MetinRenk, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKur.Tam(kutuYazi.rectTransform); kutuYazi.horizontalOverflow = HorizontalWrapMode.Overflow;
        }
    }
}
