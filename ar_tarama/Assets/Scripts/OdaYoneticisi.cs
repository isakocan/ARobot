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
        /// <summary>Taranan zemin (15 cm kaba hucre merkezleri, x,z ciftleri): AR'da hafifce boyanir ki kullanici neresinin tarandigini gorsun.</summary>
        public float[] ZeminBoya = new float[0];
        public const float BoyaHucre = 0.10f;
        /// <summary>Acik zemin / engel boyasi (10 cm kaba hucre merkezleri, x,z ciftleri): AR'da zemine boyanir.</summary>
        public float[] AcikBoya = new float[0], EngelBoya = new float[0];

        public void Temizle() { Duvarlar.Clear(); Kutular.Clear(); ZeminVar = false; ZeminPoligon = new float[0]; Alan = 0; ZeminBoya = new float[0]; AcikBoya = new float[0]; EngelBoya = new float[0]; }

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
    /// Canli acik zemin haritasi: derinlik karelerini (4 Hz, 80x45) arka plan is parcacigindaki zemin haritasina isler
    /// (ZeminHaritasi), 1 Hz'de acik zemin / engel siniflandirmasini cikarir, AR zemin boyasini ve alt 2B haritayi besler.
    /// Duvar / esya modeli (OdaModeli) artik calistirilmaz: hedef yalniz acik zemin.
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
        public Etiketleyici etiket;
        /// <summary>SegFormer etiketleri (insan noktalarini atar, zemin etiketini kullanir). Zemin icin sart degil; kapali (fps).</summary>
        public bool EtiketKullan = false;
        public const float EtiketAraligi = 0.5f;
        float sonEtiket;
        public int EtiketliKare { get; private set; }

        public const int KatmanAr = 10, KatmanMini = 11, KatmanHarita = 12;
        public const float KareAraligi = 0.25f, YapiAraligi = 1.0f, KayitAraligi = 10f;

        public bool Aktif;
        public bool ArGoster;
        public ZeminSonuc ZSon { get; private set; }
        /// <summary>Bu oturumun ham sonucu (eski ev haritasi katilmamis): eve eklenen budur.</summary>
        public ZeminSonuc ZHam { get; private set; }
        /// <summary>Duzenlenen ev (null = yeni ev). Kayitli bir isaret gorulunce eski harita oturuma oturtulur.</summary>
        public Ev ev;
        public Poz2? EvOturum { get; private set; }        // ev -> oturum
        public bool Hizali => EvOturum.HasValue;
        public event Action<int> EvHizalandi;
        public int HizaIsaret { get; private set; } = -1;
        volatile ZeminSonuc oncekiOturum, bekleyenHam;
        float sonHiza;
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

        ZeminHaritasi zemin = new ZeminHaritasi();
        volatile ZeminSonuc bekleyen;
        int nesil;
        float sonKare, sonYapi, sonKayit, oturumBasi;
        Thread is_; BlockingCollection<Action> kuyruk = new BlockingCollection<Action>(new ConcurrentQueue<Action>());
        volatile int bekleyenIs;
        volatile bool dur;
        OdaArCizim ar; ZeminCizim zc;
        Material matKati;
        public Material KatiMateryal { get { if (matKati == null) matKati = Resources.Load<Material>("mat_oda_kati"); return matKati; } }
        int hataLog;
        float[] oturumKopya = new float[0];

        void Awake()
        {
            if (EtiketKullan && etiket == null) { etiket = GetComponent<Etiketleyici>(); if (etiket == null) etiket = gameObject.AddComponent<Etiketleyici>(); }
            ar = new OdaArCizim(Resources.Load<Material>("mat_oda_cizgi"), Resources.Load<Material>("mat_oda_dolgu"));
            zc = new ZeminCizim(Resources.Load<Material>("mat_oda_dolgu"));
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

        void OnDestroy() { dur = true; try { kuyruk.CompleteAdding(); } catch { } if (ar != null) ar.Yok(); if (zc != null) zc.Yok(); }

        public void Temizle()
        {
            nesil++; zemin = new ZeminHaritasi(); bekleyen = null; ZSon = null; ZHam = null; Goruntu.Temizle(); oturumBasi = Time.unscaledTime;
            EvOturum = null; HizaIsaret = -1; oncekiOturum = null; bekleyenHam = null;
            sonKare = sonYapi = sonEtiket = 0f; KareSayisi = 0; EtiketliKare = 0; VokselSayisi = 0;
            if (ar != null) ar.Temizle();
            if (zc != null) zc.Temizle();
        }

        // ------------------------------------------------------------------------------------------ sorgular
        /// <summary>Zemindeki nokta acik zeminde ve en yakin engele en az 25 cm uzak mi? Harita yoksa false (bilinmeyen yere konmaz).</summary>
        /// <summary>Kalemle cekilen zemin siniri noktasi (dunya x, z). Bir sonraki yapida (1 Hz) haritaya islenir.</summary>
        public int SinirCizgisiEkle(float[] xz, float kx, float kz) => zemin.SinirCizgisiEkle(xz, kx, kz);
        public List<int> SinirSil(float x, float z, float r) => zemin.SinirSil(x, z, r);

        public bool AcikZeminMi(Vector3 p)
        {
            var s = ZSon;
            if (s == null || !s.zeminVar) return false;
            return s.AcikMi(p.x, p.z, 0.25f);
        }

        /// <summary>0..1: nokta kullanicinin bulundugu yerden gorunuyor mu (arada duvar yok)? Bilgi yoksa 1.</summary>
        public float Gorunurluk(Vector3 p) { return zc != null ? zc.Gorus.Gorunur(p.x, p.z) : 1f; }

        // ------------------------------------------------------------------------------------------ dongu
        void Update()
        {
            if (!Aktif) return;
            float t = Time.unscaledTime;
            bool takipte = ArApp.Ornek != null && ArApp.Ornek.Takipte;
            // etiketli kare (~2 Hz): derinlik ve kamera goruntusu ayni anda alinir; SegFormer bitince kare etiketleriyle islenir.
            // Ara karelerde (4 Hz) yalniz derinlik islenir.
            if (EtiketKullan && takipte && oklusyon != null && kamera != null && etiket != null && etiket.Hazir && !etiket.Mesgul && t - sonEtiket >= EtiketAraligi && bekleyenIs < 6)
            {
                sonEtiket = t;
                try
                {
                    var f = KareOku();
                    if (f != null)
                    {
                        int bu = nesil;
                        bool basladi = etiket.Baslat(kamera, r => { if (bu != nesil) return; f.e = r.Izgaraya(GW, GH); EtiketliKare++; Gonder(f); });
                        if (!basladi) Gonder(f);
                        sonKare = t;
                    }
                }
                catch (Exception e) { if (hataLog++ < 3) Debug.LogWarning("[ArTarama] etiketli kare hatasi: " + e.Message); }
            }
            else if (takipte && oklusyon != null && kamera != null && t - sonKare >= KareAraligi && bekleyenIs < 6)
            {
                sonKare = t;
                try { var f = KareOku(); if (f != null) Gonder(f); } catch (Exception e) { if (hataLog++ < 3) Debug.LogWarning("[ArTarama] derinlik karesi hatasi: " + e.Message); }
            }
            // harita buyudukce guncelleme uzar: aralik en az 1 sn, guncelleme suresinin 3 kati (is parcacigi tikanmasin)
            if (takipte && duzlem != null && t - sonYapi >= Mathf.Max(YapiAraligi, YapiMs * 0.003f) && bekleyenIs < 6)
            {
                sonYapi = t; YapiIsiGonder();
            }
            if (ev != null && t - sonHiza > 1f) { sonHiza = t; HizaKontrol(); }
            var b = bekleyen;
            if (b != null && b != ZSon) { bekleyen = null; ZHam = bekleyenHam ?? b; Uygula(b); }
            Canlandir(Time.unscaledDeltaTime);

            if (kayit != null && kayit.Kayitta && t - sonKayit >= KayitAraligi && ZSon != null) { sonKayit = t; Kaydet(); }
        }

        void LateUpdate()
        {
            if (ar == null || arKamera == null) return;
            ar.Guncelle(this, arKamera, Aktif && ArGoster && ZeminVar);
            // kayitli evi acinca: isaret gorulup eski harita yerine oturana kadar zemin cizilmez (sifirdan taraniyor gibi gorunmesin)
            if (zc != null) zc.Guncelle(ZSon, Goruntu.GZeminY, arKamera, Aktif && ArGoster && (ev == null || Hizali));
        }

        // ------------------------------------------------------------------------------------------ derinlik karesi
        const int GW = 160, GH = 90;     // ARCore derinliginin tam cozunurlugu (S21+: 160x90): her nokta kullanilir

        DerinlikKaresi KareOku()
        {
            if (!oklusyon.TryAcquireEnvironmentDepthCpuImage(out XRCpuImage img)) return null;
            XRCpuImage conf = default; bool confVar = false;
            try
            {
                if (oklusyon.TryAcquireEnvironmentDepthConfidenceCpuImage(out conf)) confVar = true;
                if (!kamera.TryGetIntrinsics(out XRCameraIntrinsics ki)) return null;
                bool u16 = img.format == XRCpuImage.Format.DepthUint16, f32 = img.format == XRCpuImage.Format.DepthFloat32;
                if (!u16 && !f32) return null;
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
                return f;
            }
            finally { img.Dispose(); if (confVar) conf.Dispose(); }
        }

        void Gonder(DerinlikKaresi f)
        {
            var m = zemin; int bu = nesil; KareSayisi++;
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
            var m = zemin; int bu = nesil; float zaman = OturumSure;
            Interlocked.Increment(ref bekleyenIs);
            kuyruk.Add(() =>
            {
                if (bu != nesil) return;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var r = m.Guncelle(planlar, zaman);
                var ham = r; var onc = oncekiOturum;
                if (onc != null) r = Ev.Birlestir(r, onc, null);   // duzenleme: eski ev haritasi + bu oturum
                bekleyenHam = ham;
                sw.Stop();
                YapiMs = YapiMs <= 0 ? (float)sw.Elapsed.TotalMilliseconds : Mathf.Lerp(YapiMs, (float)sw.Elapsed.TotalMilliseconds, 0.2f);
                VokselSayisi = m.HucreSayisi;
                if (bu == nesil) bekleyen = r;
            });
        }

        /// <summary>Duzenlemede: evde kayitli ve bu oturumda en cok gorulen isarete gore ev -> oturum donusumu.</summary>
        void HizaKontrol()
        {
            if (plan == null || ev == null) return;
            IsaretBilgi en = null;
            foreach (var bi in plan.Isaretler.Values)
                if (bi.pozVar && bi.pozSay >= 6 && ev.isaretler.ContainsKey(bi.id) && (en == null || bi.pozSay > en.pozSay)) en = bi;
            if (en == null) return;
            var T = Ev.Hizala(ev.isaretler[en.id], en.merkez.x, en.merkez.z, en.aci);
            if (EvOturum.HasValue)
            {
                var o = EvOturum.Value;
                double dd = System.Math.Sqrt((o.x - T.x) * (o.x - T.x) + (o.z - T.z) * (o.z - T.z)), da = System.Math.Abs(o.aci - T.aci) * 180.0 / System.Math.PI;
                if (dd < 0.03 && da < 1.0) return;
            }
            bool ilk = !EvOturum.HasValue;
            EvOturum = T; HizaIsaret = en.id;
            if (ilk) { Debug.Log("[ArTarama] ev hizalandi: isaret " + en.id + " (" + en.pozSay + " gozlem)"); EvHizalandi?.Invoke(en.id); }
            var e = ev; int bu = nesil; float zy = Goruntu.GZeminY;
            Interlocked.Increment(ref bekleyenIs);
            kuyruk.Add(() => { if (bu == nesil) oncekiOturum = e.Oturuma(T, zy); });
        }

        // ------------------------------------------------------------------------------------------ gosterim durumu
        void Uygula(ZeminSonuc s)
        {
            ZSon = s;
            var g = Goruntu;
            bool ilk = !g.ZeminVar;
            g.ZeminVar = s.zeminVar; g.Alan = s.alan; g.Duvarlar.Clear(); g.Kutular.Clear();
            if (!s.zeminVar) return;
            if (ilk) { g.GZeminY = s.zeminY; g.GTavanY = s.zeminY + 2.5f; }
            // zemin boyasi artik ZeminCizim'de (birlesik, yumusak kenarli); kare boya kapali
        }

        /// <summary>5 cm hucreleri 10 cm kaba hucrelere toplar (en az 'min' ince hucre), merkezleri x,z ciftleri olarak verir.</summary>
        static float[] Kaba(HashSet<long> h, int min)
        {
            var kaba = new Dictionary<long, int>();
            foreach (long k in h)
            {
                ZeminSonuc.Coz(k, out int ix, out int iz);
                long kk = OdaSonuc.Anah2(Mathf.FloorToInt(ix * ZeminSonuc.V / OdaGoruntu.BoyaHucre), Mathf.FloorToInt(iz * ZeminSonuc.V / OdaGoruntu.BoyaHucre));
                kaba.TryGetValue(kk, out int c0); kaba[kk] = c0 + 1;
            }
            var bl = new List<float>(kaba.Count * 2);
            foreach (var kv in kaba)
            {
                if (kv.Value < min) continue;
                ZeminSonuc.Coz(kv.Key, out int cx, out int cz);
                bl.Add((cx + 0.5f) * OdaGoruntu.BoyaHucre); bl.Add((cz + 0.5f) * OdaGoruntu.BoyaHucre);
            }
            return bl.ToArray();
        }

        void Canlandir(float dt)
        {
            var s = ZSon; var g = Goruntu;
            if (s == null || !s.zeminVar) return;
            float k = 1f - Mathf.Exp(-6f * dt);
            g.GZeminY += (s.zeminY - g.GZeminY) * k;
        }

        // ------------------------------------------------------------------------------------------ disa aktarim
        public void Kaydet()
        {
            try
            {
                var s = ZSon; if (s == null || kayit == null || string.IsNullOrEmpty(kayit.Klasor) || !Directory.Exists(kayit.Klasor)) return;
                File.WriteAllText(Path.Combine(kayit.Klasor, "zemin.json"), s.Json(kayit.Oda), new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogWarning("[ArTarama] zemin.json yazilamadi: " + e.Message); }
        }

        void OnApplicationPause(bool p) { if (p && kayit != null && kayit.Kayitta) Kaydet(); }
    }
}
