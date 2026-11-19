using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ArTarama
{
    /// <summary>Ekranda gosterilen (yumusatilmis) duvar.</summary>
    public class GDuvar
    {
        public int id; public float ax, az, bx, bz;         // gosterilen uclar
        public float tax, taz, tbx, tbz;                    // hedef uclar
        public float nx, nz; public float dogum;
        /// <summary>0..1 aciliş (cizgi cizilme / yukselme) ilerlemesi.</summary>
        public float Acilis(float simdi) { float t = Mathf.Clamp01((simdi - dogum) / 0.9f); return 1f - (1f - t) * (1f - t) * (1f - t); }
        public float Uzunluk { get { return Mathf.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az)); } }
    }

    public class GKutu
    {
        public int id; public float cx, cy, cz, sx, sy, sz, aci;
        public float tcx, tcy, tcz, tsx, tsy, tsz, taci; public float dogum;
        /// <summary>Belirme animasyonu: 0.85 -> 1 (geri tepmeli) ve saydamlik.</summary>
        public float Olcek(float simdi) { float t = Mathf.Clamp01((simdi - dogum) / 0.4f); const float c1 = 1.70158f, c3 = c1 + 1f; float e = 1f + c3 * Mathf.Pow(t - 1f, 3) + c1 * Mathf.Pow(t - 1f, 2); return Mathf.LerpUnclamped(0.82f, 1f, e); }
        public float Gorunurluk(float simdi) { return Mathf.Clamp01((simdi - dogum) / 0.25f); }
    }

    /// <summary>
    /// Canli oda modeli: ARCore duzlemleri ve nokta bulutundan (OdaModeli cekirdegi, arka plan is parcaciginda ~1 Hz)
    /// duvar + esya kutusu uretir, ekranda yumusatir/canlandirir, AR cizgilerini, alt mini modeli ve haritalari besler,
    /// "acik zemin" sorgusunu ve oda.json disa aktarimini yapar.
    /// </summary>
    public class OdaYoneticisi : MonoBehaviour
    {
        public ARPlaneManager duzlem;
        public NoktaBulutu bulut;
        public Camera arKamera;
        public PlanHaritasi plan;
        public KayitYoneticisi kayit;
        public Harita3B harita3b;

        public const int KatmanAr = 10, KatmanMini = 11, KatmanHarita = 12;
        public const float Aralik = 1.0f, KayitAraligi = 10f;

        /// <summary>Hesaplama ve AR cizgileri yalniz tarama modunda calisir.</summary>
        public bool Aktif;
        public bool ArGoster;
        public OdaSonuc Son { get; private set; }
        public readonly List<GDuvar> Duvarlar = new List<GDuvar>();
        public readonly List<GKutu> Kutular = new List<GKutu>();
        public float GZeminY, GTavanY; public bool ZeminVar;
        public float[] ZeminPoligon = new float[0];
        public bool Hesapliyor => mesgul;

        OdaModeli model = new OdaModeli();
        volatile OdaSonuc bekleyen;
        volatile bool mesgul;
        int nesil;
        float sonHesap, sonKayit, sonSurum = -1;
        OdaArCizim ar;
        public OdaKatiModel haritaModeli;
        Material matKati;
        int hataLog;

        void Awake()
        {
            ar = new OdaArCizim(Resources.Load<Material>("mat_oda_cizgi"), Resources.Load<Material>("mat_oda_dolgu"));
            haritaModeli = new OdaKatiModel(KatmanHarita, KatiMateryal, "OdaHarita3B");
        }

        public Material KatiMateryal { get { if (matKati == null) matKati = Resources.Load<Material>("mat_oda_kati"); return matKati; } }

        void OnDestroy() { if (ar != null) ar.Yok(); if (haritaModeli != null) haritaModeli.Yok(); }

        public void Temizle()
        {
            nesil++; model = new OdaModeli(); bekleyen = null; Son = null; sonSurum = -1;
            Duvarlar.Clear(); Kutular.Clear(); ZeminVar = false; ZeminPoligon = new float[0];
            if (ar != null) ar.Temizle();
        }

        // ------------------------------------------------------------------------------------------ sorgular
        /// <summary>Zemindeki nokta, kutulara ve duvarlara en az 25 cm uzakta mi? Model yoksa true (bilgi yok).</summary>
        public bool AcikZeminMi(Vector3 p)
        {
            var s = Son;
            if (s == null || !s.zeminVar) return true;
            return s.AcikZeminMi(p.x, p.z, 0.25f);
        }

        // ------------------------------------------------------------------------------------------ dongu
        void Update()
        {
            if (!Aktif) return;
            float t = Time.unscaledTime;
            if (!mesgul && t - sonHesap >= Aralik && duzlem != null && bulut != null && ArApp.Ornek != null && ArApp.Ornek.Takipte)
            {
                sonHesap = t;
                Baslat();
            }
            var b = bekleyen;
            if (b != null && b != Son) { bekleyen = null; Uygula(b); }
            Canlandir(Time.unscaledDeltaTime);

            if (kayit != null && kayit.Kayitta && t - sonKayit >= KayitAraligi && Son != null)
            {
                sonKayit = t; Kaydet();
            }
        }

        void LateUpdate()
        {
            if (ar == null || arKamera == null) return;
            ar.Guncelle(this, arKamera, Aktif && ArGoster && ZeminVar);
            if (haritaModeli != null)
            {
                bool ac = harita3b != null && harita3b.Acik;
                haritaModeli.Guncelle(this, harita3b != null ? harita3b.kam : null, ac, false, Vector3.zero, 0f, true);
            }
        }

        void Baslat()
        {
            var planlar = new List<OdaDuzlem>();
            foreach (var p in duzlem.trackables)
            {
                if (p.trackingState == TrackingState.None) continue;
                int tur = p.alignment == PlaneAlignment.HorizontalUp ? 0 : p.alignment == PlaneAlignment.HorizontalDown ? 1 : p.alignment == PlaneAlignment.Vertical ? 2 : -1;
                if (tur < 0) continue;
                var bnd = p.boundary; if (bnd.Length < 3) continue;
                var sinir = new float[bnd.Length * 3];
                for (int i = 0; i < bnd.Length; i++)
                {
                    Vector3 w = p.transform.TransformPoint(new Vector3(bnd[i].x, 0, bnd[i].y));
                    sinir[i * 3] = w.x; sinir[i * 3 + 1] = w.y; sinir[i * 3 + 2] = w.z;
                }
                Vector3 n = p.transform.up;
                planlar.Add(new OdaDuzlem { id = p.trackableId.ToString(), tur = tur, sinir = sinir, nx = n.x, ny = n.y, nz = n.z });
            }
            Vector3[] pts = bulut.Kopya();
            var m = model; int bu = nesil; mesgul = true;
            Task.Run(() =>
            {
                try
                {
                    var flat = new float[pts.Length * 3];
                    for (int i = 0; i < pts.Length; i++) { flat[i * 3] = pts[i].x; flat[i * 3 + 1] = pts[i].y; flat[i * 3 + 2] = pts[i].z; }
                    var r = m.Guncelle(planlar, flat, pts.Length);
                    if (bu == nesil) bekleyen = r;
                }
                catch (Exception e) { if (hataLog++ < 3) Debug.LogWarning("[ArTarama] oda modeli hatasi: " + e.Message + "\n" + e.StackTrace); }
                finally { mesgul = false; }
            });
        }

        // ------------------------------------------------------------------------------------------ gosterim durumu
        void Uygula(OdaSonuc s)
        {
            Son = s;
            float simdi = Time.unscaledTime;
            ZeminVar = s.zeminVar; ZeminPoligon = s.zeminPoligon;
            if (!s.zeminVar) return;
            if (Duvarlar.Count == 0 && Kutular.Count == 0) { GZeminY = s.zeminY; GTavanY = s.tavanY; }

            // duvarlar
            for (int i = Duvarlar.Count - 1; i >= 0; i--)
            {
                bool var = false; foreach (var d in s.duvarlar) if (d.id == Duvarlar[i].id) { var = true; break; }
                if (!var) Duvarlar.RemoveAt(i);
            }
            foreach (var d in s.duvarlar)
            {
                GDuvar g = null; foreach (var x in Duvarlar) if (x.id == d.id) { g = x; break; }
                if (g == null)
                {
                    g = new GDuvar { id = d.id, dogum = simdi };
                    float mx = (d.ax + d.bx) * 0.5f, mz = (d.az + d.bz) * 0.5f;
                    g.ax = g.bx = mx; g.az = g.bz = mz;   // ortadan iki yana cizilerek acilir
                    Duvarlar.Add(g);
                }
                g.tax = d.ax; g.taz = d.az; g.tbx = d.bx; g.tbz = d.bz; g.nx = d.nx; g.nz = d.nz;
            }
            // kutular
            for (int i = Kutular.Count - 1; i >= 0; i--)
            {
                bool var = false; foreach (var k in s.kutular) if (k.id == Kutular[i].id) { var = true; break; }
                if (!var) Kutular.RemoveAt(i);
            }
            foreach (var k in s.kutular)
            {
                GKutu g = null; foreach (var x in Kutular) if (x.id == k.id) { g = x; break; }
                if (g == null)
                {
                    g = new GKutu { id = k.id, dogum = simdi, cx = k.cx, cy = k.cy, cz = k.cz, sx = k.sx, sy = k.sy, sz = k.sz, aci = k.aci };
                    Kutular.Add(g);
                }
                g.tcx = k.cx; g.tcy = k.cy; g.tcz = k.cz; g.tsx = k.sx; g.tsy = k.sy; g.tsz = k.sz; g.taci = k.aci;
            }
        }

        void Canlandir(float dt)
        {
            if (Son == null || !Son.zeminVar) return;
            float k = 1f - Mathf.Exp(-6f * dt), kd = 1f - Mathf.Exp(-4f * dt);
            GZeminY += (Son.zeminY - GZeminY) * k; GTavanY += (Son.tavanY - GTavanY) * k;
            foreach (var d in Duvarlar)
            {
                d.ax += (d.tax - d.ax) * kd; d.az += (d.taz - d.az) * kd; d.bx += (d.tbx - d.bx) * kd; d.bz += (d.tbz - d.bz) * kd;
            }
            foreach (var b in Kutular)
            {
                b.cx += (b.tcx - b.cx) * k; b.cy += (b.tcy - b.cy) * k; b.cz += (b.tcz - b.cz) * k;
                b.sx += (b.tsx - b.sx) * k; b.sy += (b.tsy - b.sy) * k; b.sz += (b.tsz - b.sz) * k;
                float da = Mathf.DeltaAngle(b.aci, b.taci);
                if (Mathf.Abs(b.sx - b.sz) < 0.08f && Mathf.Abs(da) > 45f) da -= Mathf.Sign(da) * 90f;
                b.aci += da * k;
            }
        }

        // ------------------------------------------------------------------------------------------ disa aktarim
        public void Kaydet()
        {
            try
            {
                var s = Son; if (s == null || kayit == null || string.IsNullOrEmpty(kayit.Klasor) || !Directory.Exists(kayit.Klasor)) return;
                List<float[]> isr = null;
                if (plan != null)
                {
                    isr = new List<float[]>();
                    foreach (var b in plan.Isaretler.Values) if (b.konumVar) isr.Add(new[] { (float)b.id, b.konum.x, b.konum.y, b.konum.z });
                }
                File.WriteAllText(Path.Combine(kayit.Klasor, "oda.json"), OdaJson.Yaz(s, isr), new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogWarning("[ArTarama] oda.json yazilamadi: " + e.Message); }
        }

        void OnApplicationPause(bool p) { if (p && kayit != null && kayit.Kayitta) Kaydet(); }
    }
}
