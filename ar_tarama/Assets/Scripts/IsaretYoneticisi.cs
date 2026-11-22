using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ArTarama
{
    /// <summary>Arka planda ArUco algilar, sessizce sayar (ekrana cerceve cizilmez), harita konumunu tahmin eder.</summary>
    public class IsaretYoneticisi : MonoBehaviour
    {
        public AROcclusionManager oklusyon;
        public Camera arKamera;
        public PlanHaritasi plan;
        public KayitYoneticisi kayit;
        public const int GerekliAlgilama = 6;
        /// <summary>Basili isaretin siyah kare kenari (tools/aruco/yazdir.py, 18 cm).</summary>
        public const float IsaretKenar = 0.18f;

        public struct Istek
        {
            public int kareIdx; public int w, h, satir;
            public double zaman;
            public Vector3 poz; public Quaternion rot;
            public float fx, fy, cx, cy; public int rw, rh;
        }

        public class Sonuc
        {
            public Istek istek; public List<ArucoSonuc> bulunan; public float ms;
        }

        byte[] tampon = new byte[0];
        volatile bool mesgul;
        BlockingCollection<Istek> kuyruk = new BlockingCollection<Istek>(new ConcurrentQueue<Istek>(), 1);
        readonly ConcurrentQueue<Sonuc> sonuclar = new ConcurrentQueue<Sonuc>();
        Thread is_;
        volatile bool dur;
        readonly HashSet<int> kesin = new HashSet<int>();
        public int Toplam { get; private set; }
        public float OrtMs { get; private set; }
        public bool Mesgul => mesgul;
        public event Action<int> YeniIsaret;

        void Awake()
        {
            is_ = new Thread(Calis) { IsBackground = true, Name = "ArucoAlgila" };
            is_.Start();
        }

        void OnDestroy() { dur = true; try { kuyruk.CompleteAdding(); } catch { } }

        public byte[] TamponAl(int n)
        {
            if (tampon.Length < n) tampon = new byte[n];
            return tampon;
        }

        public void Calistir(Istek i)
        {
            mesgul = true;
            if (!kuyruk.TryAdd(i)) mesgul = false;
        }

        void Calis()
        {
            var det = new ArucoAlgilayici { MaksHamming = 1 };
            var yarim = new byte[0];
            foreach (var i in kuyruk.GetConsumingEnumerable())
            {
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    int w2 = i.w / 2, h2 = i.h / 2;
                    if (yarim.Length != w2 * h2) yarim = new byte[w2 * h2];
                    var b = tampon;
                    for (int y = 0; y < h2; y++)
                    {
                        int o0 = (2 * y) * i.satir, o1 = o0 + i.satir, d = y * w2;
                        for (int x = 0; x < w2; x++)
                        {
                            int s = 2 * x;
                            yarim[d + x] = (byte)((b[o0 + s] + b[o0 + s + 1] + b[o1 + s] + b[o1 + s + 1] + 2) >> 2);
                        }
                    }
                    var bul = det.Algila(yarim, w2, h2, 2f);
                    sw.Stop();
                    sonuclar.Enqueue(new Sonuc { istek = i, bulunan = bul, ms = (float)sw.Elapsed.TotalMilliseconds });
                }
                catch (Exception e) { sonuclar.Enqueue(new Sonuc { istek = i, bulunan = new List<ArucoSonuc>(), ms = -1 }); Debug.LogWarning("[ArTarama] aruco hatasi: " + e.Message); }
                finally { mesgul = false; }
                if (dur) break;
            }
        }

        public void Sifirla()
        {
            kesin.Clear(); aday.Clear(); Toplam = 0;
            while (sonuclar.TryDequeue(out _)) { }
        }

        public int Gorulen { get { int n = 0; foreach (var b in plan.Isaretler.Values) if (b.sayi > 0) n++; return n; } }
        public bool HepsiTamam
        {
            get
            {
                if (plan.Isaretler.Count == 0) return false;
                foreach (var b in plan.Isaretler.Values) if (b.sayi < GerekliAlgilama) return false;
                return true;
            }
        }

        void Update()
        {
            while (sonuclar.TryDequeue(out var s))
            {
                if (s.ms > 0) OrtMs = OrtMs <= 0 ? s.ms : Mathf.Lerp(OrtMs, s.ms, 0.1f);
                var kabul = new List<ArucoSonuc>();
                foreach (var a in s.bulunan)
                {
                    // goruntu kenarina degen dortgen: yarim kalan bir sekil isaret sanilabiliyor (22 Kasim: odada tek isaret
                    // varken kenarda iki kez 'isaret 17' bulundu, kosesi x = -1 ve 0'daydi)
                    if (KenaraDegiyor(a, s.istek.w, s.istek.h)) continue;
                    if (!kesin.Contains(a.id))
                    {
                        // yeni kimlik: 3 sn icinde en az 3 karede tam (hatasiz) gorulmeden kabul edilmez
                        if (a.hamming != 0) continue;
                        float t = Time.unscaledTime;
                        if (!aday.TryGetValue(a.id, out var ad) || t - ad.Value > 3f) ad = new KeyValuePair<int, float>(0, t);
                        ad = new KeyValuePair<int, float>(ad.Key + 1, ad.Value); aday[a.id] = ad;
                        if (ad.Key < 3) continue;
                        kesin.Add(a.id); aday.Remove(a.id);
                    }
                    kabul.Add(a);
                }
                if (kayit != null) kayit.IsaretSonucu(s.istek.kareIdx, kabul, s.ms);
                foreach (var a in kabul) Isle(s.istek, a);
            }
        }

        readonly Dictionary<int, KeyValuePair<int, float>> aday = new Dictionary<int, KeyValuePair<int, float>>();

        static bool KenaraDegiyor(ArucoSonuc a, int w, int h)
        {
            const float pay = 4f;
            for (int q = 0; q < 4; q++)
            {
                float x = a.kose[q * 2], y = a.kose[q * 2 + 1];
                if (x < pay || y < pay || x > w - 1 - pay || y > h - 1 - pay) return true;
            }
            return false;
        }

        void Isle(Istek i, ArucoSonuc a)
        {
            Toplam++;
            if (!plan.Isaretler.TryGetValue(a.id, out var b)) { b = new IsaretBilgi { id = a.id }; plan.Isaretler[a.id] = b; }
            bool yeni = b.sayi == 0;
            b.sayi++; b.sonGorulme = Time.unscaledTime;
            if (yeni) YeniIsaret?.Invoke(a.id);

            // poz: 4 koseden homografi (isaret 18 cm), agirlikli ortalama; yon disi gozlemler atilir
            if (ArApp.Ornek != null && ArApp.Ornek.AciHizi < 40f)
            {
                var aa = new double[4]; var bb = new double[4];
                for (int q = 0; q < 4; q++)
                {
                    float px = a.kose[q * 2] * (i.rw / (float)i.w), py = a.kose[q * 2 + 1] * (i.rh / (float)i.h);
                    aa[q] = (i.cy - py) / i.fy; bb[q] = (i.cx - px) / i.fx;
                }
                if (IsaretPoz.Coz(aa, bb, IsaretKenar, out var t, out var r1, out _))
                {
                    Vector3 mw = i.poz + i.rot * new Vector3((float)t[0], (float)t[1], (float)t[2]);
                    Vector3 sag = i.rot * new Vector3((float)r1[0], (float)r1[1], (float)r1[2]);
                    Vector3 n = new Vector3(-sag.z, 0, sag.x);   // yatay, sag eksenine dik
                    if (n.sqrMagnitude > 1e-4f)
                    {
                        n.Normalize();
                        Vector3 kamYon = i.poz - mw; kamYon.y = 0;
                        if (Vector3.Dot(n, kamYon) < 0) n = -n;   // isaretten kameraya (odaya) bakar
                        float uz = Vector3.Distance(i.poz, mw);
                        bool uygun = uz < 2.5f;   // 2,5 m otesinde tek gozlem yon hatasi %90 durumda 17 dereceyi asiyor (EvTest)
                        if (uygun && b.pozSay >= 5)
                        {
                            var ort = new Vector3((float)b.snx, 0, (float)b.snz).normalized;
                            if (Vector3.Angle(ort, n) > 25f || Vector3.Distance(new Vector3((float)(b.sx / b.sw), (float)(b.sy / b.sw), (float)(b.sz / b.sw)), mw) > 0.4f) uygun = false;
                        }
                        if (uygun)
                        {
                            double w = 1.0 / Mathf.Max(0.3f, uz * uz);
                            b.sx += mw.x * w; b.sy += mw.y * w; b.sz += mw.z * w; b.snx += n.x * w; b.snz += n.z * w; b.sw += w; b.pozSay++;
                            b.merkez = new Vector3((float)(b.sx / b.sw), (float)(b.sy / b.sw), (float)(b.sz / b.sw));
                            b.aci = Mathf.Atan2((float)b.snx, (float)b.snz); b.pozVar = b.pozSay >= 3;
                        }
                    }
                }
            }

            // harita konumu (derinlikten)
            if (Time.unscaledTime - b.sonGorulme < 0.5f && ArApp.Ornek != null && ArApp.Ornek.AciHizi < 40f)
            {
                float cxp = (a.kose[0] + a.kose[2] + a.kose[4] + a.kose[6]) * 0.25f, cyp = (a.kose[1] + a.kose[3] + a.kose[5] + a.kose[7]) * 0.25f;
                if (DerinlikAl(cxp / i.w, cyp / i.h, out float z))
                {
                    float px = cxp * (i.rw / (float)i.w), py = cyp * (i.rh / (float)i.h);
                    Vector3 yerel = new Vector3((i.cy - py) / i.fy * z, (i.cx - px) / i.fx * z, z);
                    Vector3 w = i.poz + i.rot * yerel;
                    if (!b.konumVar) { b.konum = w; b.konumVar = true; }
                    else b.konum = Vector3.Lerp(b.konum, w, 0.2f);
                }
            }
        }

        bool DerinlikAl(float u, float v, out float z)
        {
            z = 0;
            if (oklusyon == null || !oklusyon.TryAcquireEnvironmentDepthCpuImage(out XRCpuImage img)) return false;
            using (img)
            {
                var pl = img.GetPlane(0);
                int ix = Mathf.Clamp((int)(u * img.width), 1, img.width - 2), iy = Mathf.Clamp((int)(v * img.height), 1, img.height - 2);
                if (img.format != XRCpuImage.Format.DepthUint16) return false;
                var vals = new List<float>(9);
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int off = (iy + dy) * pl.rowStride + (ix + dx) * pl.pixelStride;
                        float m = (pl.data[off] | (pl.data[off + 1] << 8)) * 0.001f;
                        if (m > 0.2f && m < 6f) vals.Add(m);
                    }
                if (vals.Count < 3) return false;
                vals.Sort(); z = vals[vals.Count / 2];
                return true;
            }
        }
    }
}
