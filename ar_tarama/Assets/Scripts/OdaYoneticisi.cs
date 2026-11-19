using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ArTarama
{
    /// <summary>Ekranda gosterilen (yumusatilmis) duvar.</summary>
    public class GDuvar
    {
        public int id; public bool kesin; public float ilerleme;
        public float ax, az, bx, bz;         // gosterilen uclar
        public float tax, taz, tbx, tbz;     // hedef uclar
        public bool aKose, bKose; public float nx, nz;
        public float kesinAn = -100f;        // kesinlestigi an (animasyon basi)
        /// <summary>0..1 cizilme ilerlemesi (zemin cizgisi, dikey kose cizgileri, tavan cizgisi sirayla).</summary>
        public float Acilis(float simdi) { if (!kesin) return 0f; float t = Mathf.Clamp01((simdi - kesinAn) / 1.1f); return 1f - (1f - t) * (1f - t) * (1f - t); }
        public float Uzunluk { get { return Mathf.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az)); } }
    }

    public class GKutu
    {
        public int id; public bool kesin; public float ilerleme;
        public float cx, cy, cz, sx, sy, sz, aci;
        public float tcx, tcy, tcz, tsx, tsy, tsz, taci;
        public float kesinAn = -100f;
        public float Olcek(float simdi) { if (!kesin) return 1f; float t = Mathf.Clamp01((simdi - kesinAn) / 0.45f); const float c1 = 1.70158f, c3 = c1 + 1f; float e = 1f + c3 * Mathf.Pow(t - 1f, 3) + c1 * Mathf.Pow(t - 1f, 2); return Mathf.LerpUnclamped(0.88f, 1f, e); }
        public float Gorunurluk(float simdi) { return kesin ? Mathf.Clamp01((simdi - kesinAn) / 0.35f) : 1f; }
    }

    /// <summary>Gosterim durumu: canli (OdaYoneticisi) veya kayittan (inceleme ekrani) kurulur.</summary>
    public class OdaGoruntu
    {
        public readonly List<GDuvar> Duvarlar = new List<GDuvar>();
        public readonly List<GKutu> Kutular = new List<GKutu>();
        public float GZeminY, GTavanY; public bool TavanTahmin; public bool ZeminVar;
        public float[] ZeminPoligon = new float[0];
        public float Alan;

        public void Temizle() { Duvarlar.Clear(); Kutular.Clear(); ZeminVar = false; ZeminPoligon = new float[0]; Alan = 0; }

        /// <summary>Sonuctan animasyonsuz (tamamlanmis) gosterim; yalniz kesin nesneler.</summary>
        public static OdaGoruntu Sonuctan(OdaSonuc s)
        {
            var g = new OdaGoruntu { ZeminVar = true, GZeminY = s.zeminY, GTavanY = s.tavanY, TavanTahmin = s.TavanTahmin, ZeminPoligon = s.zeminPoligon, Alan = s.alan };
            foreach (var d in s.duvarlar)
            {
                if (!d.kesin) continue;
                g.Duvarlar.Add(new GDuvar { id = d.id, kesin = true, ilerleme = 1f, ax = d.ax, az = d.az, bx = d.bx, bz = d.bz, tax = d.ax, taz = d.az, tbx = d.bx, tbz = d.bz, aKose = d.aKose, bKose = d.bKose, nx = d.nx, nz = d.nz });
            }
            foreach (var k in s.kutular)
            {
                if (!k.kesin) continue;
                g.Kutular.Add(new GKutu { id = k.id, kesin = true, ilerleme = 1f, cx = k.cx, cy = k.cy, cz = k.cz, sx = k.sx, sy = k.sy, sz = k.sz, aci = k.aci, tcx = k.cx, tcy = k.cy, tcz = k.cz, tsx = k.sx, tsy = k.sy, tsz = k.sz, taci = k.aci });
            }
            return g;
        }
    }

    /// <summary>
    /// Canli oda modeli: derinlik karelerini (4 Hz, 80x45) arka plan is parcacigindaki kanit izgarasina isler, 1 Hz'de
    /// duvar + esya yapisini cikarir (OdaModeli cekirdegi), ekranda yumusatir ve AR cizgilerini, alt mini modeli besler.
    /// </summary>
    public class OdaYoneticisi : MonoBehaviour
    {
        public ARPlaneManager duzlem;
        public AROcclusionManager oklusyon;
        public ARCameraManager kamera;
        public NoktaBulutu bulut;
        public Camera arKamera;
        public PlanHaritasi plan;
        public KayitYoneticisi kayit;

        public const int KatmanAr = 10, KatmanMini = 11, KatmanHarita = 12;
        public const float KareAraligi = 0.25f, YapiAraligi = 1.0f, KayitAraligi = 10f;

        public bool Aktif;
        public bool ArGoster;
        public OdaSonuc Son { get; private set; }
        public readonly OdaGoruntu Goruntu = new OdaGoruntu();
        public List<GDuvar> Duvarlar => Goruntu.Duvarlar;
        public List<GKutu> Kutular => Goruntu.Kutular;
        public bool ZeminVar => Goruntu.ZeminVar;
        public float GuvenOrani { get; private set; } = -1f;
        public float IsleMs { get; private set; }
        public float YapiMs { get; private set; }
        public int VokselSayisi { get; private set; }
        public int KareSayisi { get; private set; }
        public float OturumSure => Time.unscaledTime - oturumBasi;

        OdaModeli model = new OdaModeli();
        volatile OdaSonuc bekleyen;
        int nesil;
        float sonKare, sonYapi, sonKayit, oturumBasi;
        Thread is_; BlockingCollection<Action> kuyruk = new BlockingCollection<Action>(new ConcurrentQueue<Action>());
        volatile int bekleyenIs;
        volatile bool dur;
        OdaArCizim ar;
        Material matKati;
        public Material KatiMateryal { get { if (matKati == null) matKati = Resources.Load<Material>("mat_oda_kati"); return matKati; } }
        int hataLog;
        float[] oturumKopya = new float[0];

        void Awake()
        {
            ar = new OdaArCizim(Resources.Load<Material>("mat_oda_cizgi"), Resources.Load<Material>("mat_oda_dolgu"));
            oturumBasi = Time.unscaledTime;
            is_ = new Thread(Calis) { IsBackground = true, Name = "OdaYapisi" };
            is_.Start();
        }

        void Calis()
        {
            foreach (var a in kuyruk.GetConsumingEnumerable())
            {
                try { a(); } catch (Exception e) { if (hataLog++ < 3) Debug.LogWarning("[ArTarama] oda modeli hatasi: " + e.Message + "\n" + e.StackTrace); }
                finally { Interlocked.Decrement(ref bekleyenIs); }
                if (dur) break;
            }
        }

        void OnDestroy() { dur = true; try { kuyruk.CompleteAdding(); } catch { } if (ar != null) ar.Yok(); }

        public void Temizle()
        {
            nesil++; model = new OdaModeli(); bekleyen = null; Son = null; Goruntu.Temizle(); oturumBasi = Time.unscaledTime;
            sonKare = sonYapi = 0f; KareSayisi = 0; VokselSayisi = 0;
            if (ar != null) ar.Temizle();
        }

        // ------------------------------------------------------------------------------------------ sorgular
        /// <summary>Zemindeki nokta: bos zemin kaniti var ve kutu/duvara en az 25 cm uzak mi? Model yoksa true (bilgi yok).</summary>
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
            bool takipte = ArApp.Ornek != null && ArApp.Ornek.Takipte;
            if (takipte && oklusyon != null && kamera != null && t - sonKare >= KareAraligi && bekleyenIs < 6)
            {
                sonKare = t;
                try { KareAl(); } catch (Exception e) { if (hataLog++ < 3) Debug.LogWarning("[ArTarama] derinlik karesi hatasi: " + e.Message); }
            }
            if (takipte && duzlem != null && t - sonYapi >= YapiAraligi && bekleyenIs < 6)
            {
                sonYapi = t; YapiIsiGonder();
            }
            var b = bekleyen;
            if (b != null && b != Son) { bekleyen = null; Uygula(b); }
            Canlandir(Time.unscaledDeltaTime);

            if (kayit != null && kayit.Kayitta && t - sonKayit >= KayitAraligi && Son != null) { sonKayit = t; Kaydet(); }
        }

        void LateUpdate()
        {
            if (ar == null || arKamera == null) return;
            ar.Guncelle(this, arKamera, Aktif && ArGoster && ZeminVar);
        }

        // ------------------------------------------------------------------------------------------ derinlik karesi
        const int GW = 80, GH = 45;

        void KareAl()
        {
            if (!oklusyon.TryAcquireEnvironmentDepthCpuImage(out XRCpuImage img)) return;
            XRCpuImage conf = default; bool confVar = false;
            try
            {
                if (oklusyon.TryAcquireEnvironmentDepthConfidenceCpuImage(out conf)) confVar = true;
                if (!kamera.TryGetIntrinsics(out XRCameraIntrinsics ki)) return;
                bool u16 = img.format == XRCpuImage.Format.DepthUint16, f32 = img.format == XRCpuImage.Format.DepthFloat32;
                if (!u16 && !f32) return;
                int dw = img.width, dh = img.height;
                var pl = img.GetPlane(0); var d = pl.data; int rs = pl.rowStride, ps = pl.pixelStride;
                Unity.Collections.NativeArray<byte> cd = default; int crs = 0, cps = 1;
                if (confVar) { var cp = conf.GetPlane(0); cd = cp.data; crs = cp.rowStride; cps = cp.pixelStride; }
                var f = new DerinlikKaresi
                {
                    w = GW, h = GH, d = new float[GW * GH], g = new byte[GW * GH],
                    fx = ki.focalLength.x * GW / ki.resolution.x, fy = ki.focalLength.y * GH / ki.resolution.y,
                    cx = ki.principalPoint.x * GW / ki.resolution.x, cy = ki.principalPoint.y * GH / ki.resolution.y
                };
                var tr = arKamera.transform; var p = tr.position; var q = tr.rotation;
                f.px = p.x; f.py = p.y; f.pz = p.z; f.qx = q.x; f.qy = q.y; f.qz = q.z; f.qw = q.w; f.zaman = OturumSure;
                int gecerli = 0, guvenli = 0;
                for (int v = 0; v < GH; v++)
                {
                    int j = Mathf.Min(dh - 1, (int)((v + 0.5f) * dh / GH));
                    for (int u = 0; u < GW; u++)
                    {
                        int i = Mathf.Min(dw - 1, (int)((u + 0.5f) * dw / GW));
                        int off = j * rs + i * ps; float z;
                        if (u16) z = (d[off] | (d[off + 1] << 8)) * 0.001f;
                        else { z = BitConverter.ToSingle(new[] { d[off], d[off + 1], d[off + 2], d[off + 3] }, 0); if (float.IsNaN(z)) z = 0f; }
                        f.d[v * GW + u] = z;
                        byte gv = confVar ? cd[Mathf.Min(img.height - 1, j) * crs + i * cps] : (byte)255;
                        f.g[v * GW + u] = gv;
                        if (z > 0.3f && z < 4f) { gecerli++; if (gv >= 128) guvenli++; }
                    }
                }
                GuvenOrani = gecerli > 50 ? guvenli / (float)gecerli : -1f;
                var m = model; int bu = nesil; KareSayisi++;
                Interlocked.Increment(ref bekleyenIs);
                kuyruk.Add(() =>
                {
                    if (bu != nesil) return;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    m.Isle(f);
                    sw.Stop();
                    IsleMs = IsleMs <= 0 ? (float)sw.Elapsed.TotalMilliseconds : Mathf.Lerp(IsleMs, (float)sw.Elapsed.TotalMilliseconds, 0.1f);
                });
            }
            finally { img.Dispose(); if (confVar) conf.Dispose(); }
        }

        void YapiIsiGonder()
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
            var m = model; int bu = nesil; float zaman = OturumSure;
            Interlocked.Increment(ref bekleyenIs);
            kuyruk.Add(() =>
            {
                if (bu != nesil) return;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var r = m.Guncelle(planlar, zaman);
                sw.Stop();
                YapiMs = YapiMs <= 0 ? (float)sw.Elapsed.TotalMilliseconds : Mathf.Lerp(YapiMs, (float)sw.Elapsed.TotalMilliseconds, 0.2f);
                VokselSayisi = r.voksel;
                if (bu == nesil) bekleyen = r;
            });
        }

        // ------------------------------------------------------------------------------------------ gosterim durumu
        void Uygula(OdaSonuc s)
        {
            Son = s;
            float simdi = Time.unscaledTime;
            var g = Goruntu;
            bool ilk = !g.ZeminVar;
            g.ZeminVar = s.zeminVar; g.ZeminPoligon = s.zeminPoligon; g.Alan = s.alan; g.TavanTahmin = s.TavanTahmin;
            if (!s.zeminVar) return;
            if (ilk) { g.GZeminY = s.zeminY; g.GTavanY = s.tavanY; }

            for (int i = g.Duvarlar.Count - 1; i >= 0; i--)
            {
                bool var = false; foreach (var d in s.duvarlar) if (d.id == g.Duvarlar[i].id) { var = true; break; }
                if (!var) g.Duvarlar.RemoveAt(i);
            }
            foreach (var d in s.duvarlar)
            {
                GDuvar x = null; foreach (var q in g.Duvarlar) if (q.id == d.id) { x = q; break; }
                if (x == null) { x = new GDuvar { id = d.id, ax = d.ax, az = d.az, bx = d.bx, bz = d.bz }; g.Duvarlar.Add(x); }
                if (d.kesin && !x.kesin) x.kesinAn = simdi;
                x.kesin = d.kesin; x.ilerleme = d.ilerleme;
                x.tax = d.ax; x.taz = d.az; x.tbx = d.bx; x.tbz = d.bz; x.nx = d.nx; x.nz = d.nz; x.aKose = d.aKose; x.bKose = d.bKose;
            }
            for (int i = g.Kutular.Count - 1; i >= 0; i--)
            {
                bool var = false; foreach (var k in s.kutular) if (k.id == g.Kutular[i].id) { var = true; break; }
                if (!var) g.Kutular.RemoveAt(i);
            }
            foreach (var k in s.kutular)
            {
                GKutu x = null; foreach (var q in g.Kutular) if (q.id == k.id) { x = q; break; }
                if (x == null) { x = new GKutu { id = k.id, cx = k.cx, cy = k.cy, cz = k.cz, sx = k.sx, sy = k.sy, sz = k.sz, aci = k.aci }; g.Kutular.Add(x); }
                if (k.kesin && !x.kesin) x.kesinAn = simdi;
                x.kesin = k.kesin; x.ilerleme = k.ilerleme;
                x.tcx = k.cx; x.tcy = k.cy; x.tcz = k.cz; x.tsx = k.sx; x.tsy = k.sy; x.tsz = k.sz; x.taci = k.aci;
            }
        }

        void Canlandir(float dt)
        {
            var s = Son; var g = Goruntu;
            if (s == null || !s.zeminVar) return;
            float k = 1f - Mathf.Exp(-6f * dt), kd = 1f - Mathf.Exp(-5f * dt);
            g.GZeminY += (s.zeminY - g.GZeminY) * k; g.GTavanY += (s.tavanY - g.GTavanY) * k;
            foreach (var d in g.Duvarlar)
            { d.ax += (d.tax - d.ax) * kd; d.az += (d.taz - d.az) * kd; d.bx += (d.tbx - d.bx) * kd; d.bz += (d.tbz - d.bz) * kd; }
            foreach (var b in g.Kutular)
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
                File.WriteAllText(Path.Combine(kayit.Klasor, "oda.json"), OdaJson.Yaz(s, isr, kayit.Oda), new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogWarning("[ArTarama] oda.json yazilamadi: " + e.Message); }
        }

        void OnApplicationPause(bool p) { if (p && kayit != null && kayit.Kayitta) Kaydet(); }
    }
}
