using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ArTarama
{
    /// <summary>Arka planda ArUco algilar, ekranda cerceve cizer, sayar, harita konumunu tahmin eder.</summary>
    public class IsaretYoneticisi : MonoBehaviour
    {
        public AROcclusionManager oklusyon;
        public Camera arKamera;
        public PlanHaritasi plan;
        public KayitYoneticisi kayit;
        public RectTransform kaplama;
        public const int GerekliAlgilama = 6;

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

        // ekran cercevesi
        class Cerceve { public int id; public Image[] kenar = new Image[4]; public Text yazi; public float son; public CanvasGroup grup; public RectTransform kok; }
        readonly Dictionary<int, Cerceve> cerceveler = new Dictionary<int, Cerceve>();

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
            kesin.Clear(); Toplam = 0;
            while (sonuclar.TryDequeue(out _)) { }
            foreach (var c in cerceveler.Values) c.grup.alpha = 0;
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
                    if (a.hamming == 0) kesin.Add(a.id);
                    else if (!kesin.Contains(a.id)) continue;
                    kabul.Add(a);
                }
                if (kayit != null) kayit.IsaretSonucu(s.istek.kareIdx, kabul, s.ms);
                foreach (var a in kabul) Isle(s.istek, a);
            }
            foreach (var c in cerceveler.Values)
            {
                float yas = Time.unscaledTime - c.son;
                c.grup.alpha = Mathf.Clamp01(1f - (yas - 0.35f) * 3f);
            }
        }

        void Isle(Istek i, ArucoSonuc a)
        {
            Toplam++;
            if (!plan.Isaretler.TryGetValue(a.id, out var b)) { b = new IsaretBilgi { id = a.id }; plan.Isaretler[a.id] = b; }
            bool yeni = b.sayi == 0;
            b.sayi++; b.sonGorulme = Time.unscaledTime;
            if (yeni) YeniIsaret?.Invoke(a.id);

            // ekran cercevesi
            var ekran = new Vector2[4]; bool ok = true;
            for (int k = 0; k < 4; k++)
            {
                float px = a.kose[k * 2] * (i.rw / (float)i.w), py = a.kose[k * 2 + 1] * (i.rh / (float)i.h);
                Vector3 yerel = new Vector3((i.cy - py) / i.fy, (i.cx - px) / i.fx, 1f);
                Vector3 dunya = arKamera.transform.position + (i.rot * yerel).normalized * 3f;
                Vector3 sp = arKamera.WorldToScreenPoint(dunya);
                if (sp.z <= 0) { ok = false; break; }
                RectTransformUtility.ScreenPointToLocalPointInRectangle(kaplama, sp, null, out ekran[k]);
            }
            if (ok) CerceveCiz(a.id, ekran);

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

        void CerceveCiz(int id, Vector2[] p)
        {
            if (!cerceveler.TryGetValue(id, out var c))
            {
                c = new Cerceve { id = id };
                c.kok = UiKur.Obj("IsaretCerceve" + id, kaplama);
                UiKur.Tam(c.kok);
                c.grup = c.kok.gameObject.AddComponent<CanvasGroup>(); c.grup.blocksRaycasts = false; c.grup.interactable = false;
                for (int k = 0; k < 4; k++)
                {
                    var im = UiKur.Kutu(c.kok, "K" + k, UiKur.Vurgu, false, false);
                    im.rectTransform.pivot = new Vector2(0, 0.5f);
                    c.kenar[k] = im;
                }
                c.yazi = UiKur.Metin(c.kok, "Etiket", "", 32, UiKur.Vurgu, TextAnchor.MiddleCenter, FontStyle.Bold);
                c.yazi.horizontalOverflow = HorizontalWrapMode.Overflow;
                c.yazi.rectTransform.sizeDelta = new Vector2(240, 44);
                c.yazi.rectTransform.anchorMin = c.yazi.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                cerceveler[id] = c;
            }
            c.son = Time.unscaledTime;
            Vector2 mrk = Vector2.zero;
            for (int k = 0; k < 4; k++)
            {
                Vector2 a = p[k], b = p[(k + 1) % 4]; mrk += a * 0.25f;
                var rt = c.kenar[k].rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = a; Vector2 d = b - a;
                rt.sizeDelta = new Vector2(d.magnitude, 6f);
                rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            }
            float ust = Mathf.Max(p[0].y, p[1].y, p[2].y, p[3].y);
            c.yazi.rectTransform.anchoredPosition = new Vector2(mrk.x, ust + 36f);
            plan.Isaretler.TryGetValue(id, out var bb);
            c.yazi.text = "İşaret " + id + (bb != null && bb.sayi >= GerekliAlgilama ? "  tamam" : "");
        }
    }
}
