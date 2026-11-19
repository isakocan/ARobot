using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ArTarama
{
    public class IsaretBilgi
    {
        public int id; public Vector3 konum; public bool konumVar; public int sayi; public float sonGorulme;
    }

    /// <summary>
    /// Ust gorunum plan haritasi: 5 cm hucre, 640x640 (32 m). Zemin duzlemleri, engel noktalari (zeminden 0.10-1.80 m),
    /// yol izi tek bir dokuda tutulur; minimap ve tam ekran 2B gorunum ayni dokuyu kullanir.
    /// </summary>
    public class PlanHaritasi : MonoBehaviour
    {
        public const int N = 640;
        public const float Hucre = 0.05f;
        public ARPlaneManager duzlem;
        public Camera arKamera;
        public Texture2D doku { get; private set; }
        public Vector2 Merkez { get; private set; }
        public bool Hazir { get; private set; }
        public readonly List<Vector3> Iz = new List<Vector3>();
        public readonly Dictionary<int, IsaretBilgi> Isaretler = new Dictionary<int, IsaretBilgi>();
        public readonly List<Vector3> Kristaller = new List<Vector3>();
        public int MinX = N, MaxX = -1, MinZ = N, MaxZ = -1;

        readonly byte[] zemin = new byte[N * N], engel = new byte[N * N], iz = new byte[N * N];
        bool kirli; float sonYapi, sonDuzlem, sonIz;
        Vector3 sonIzKonum; bool izVar;
        readonly List<float> kes = new List<float>();

        public static readonly Color32 ArkaPlan = new Color32(46, 52, 59, 214);
        static readonly Color32 ZeminRenk = new Color32(67, 217, 190, 120);
        static readonly Color32 EngelRenk = new Color32(16, 18, 22, 245);
        static readonly Color32 IzRenk = new Color32(237, 235, 230, 235);

        void Awake()
        {
            doku = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "PlanDoku" };
            var bos = doku.GetRawTextureData<Color32>();
            for (int i = 0; i < bos.Length; i++) bos[i] = default;
            doku.Apply(false);
        }

        public void Temizle()
        {
            System.Array.Clear(zemin, 0, zemin.Length); System.Array.Clear(engel, 0, engel.Length); System.Array.Clear(iz, 0, iz.Length);
            Iz.Clear(); Isaretler.Clear(); Kristaller.Clear();
            MinX = N; MaxX = -1; MinZ = N; MaxZ = -1; Hazir = false; izVar = false;
            var raw = doku.GetRawTextureData<Color32>();
            for (int i = 0; i < raw.Length; i++) raw[i] = default;
            doku.Apply(false);
        }

        public void Baslat(Vector3 konum)
        {
            Merkez = new Vector2(konum.x, konum.z);
            Hazir = true;
        }

        public bool Hucreye(float x, float z, out int cx, out int cz)
        {
            cx = Mathf.FloorToInt((x - Merkez.x) / Hucre) + N / 2;
            cz = Mathf.FloorToInt((z - Merkez.y) / Hucre) + N / 2;
            return cx >= 0 && cz >= 0 && cx < N && cz < N;
        }

        void Sinir(int cx, int cz)
        {
            if (cx < MinX) MinX = cx; if (cx > MaxX) MaxX = cx; if (cz < MinZ) MinZ = cz; if (cz > MaxZ) MaxZ = cz;
        }

        public void NoktaEkle(Vector3 w)
        {
            if (!Hazir || !ArApp.ZeminVar) return;
            float h = w.y - ArApp.ZeminY;
            if (h < 0.10f || h > 1.80f) return;
            if (!Hucreye(w.x, w.z, out int cx, out int cz)) return;
            int i = cz * N + cx;
            if (engel[i] < 255) engel[i]++;
            Sinir(cx, cz); kirli = true;
        }

        void Update()
        {
            if (!Hazir || arKamera == null) return;
            float t = Time.unscaledTime;
            var kp = arKamera.transform.position;
            if (ArApp.Ornek != null && ArApp.Ornek.Takipte && (!izVar || (kp - sonIzKonum).sqrMagnitude > 0.08f * 0.08f))
                IzEkle(kp);
            if (t - sonDuzlem > 1.0f) { sonDuzlem = t; ZeminiCiz(); }
            if (kirli && t - sonYapi > 0.5f) { sonYapi = t; kirli = false; Yeniden(); }
        }

        void IzEkle(Vector3 kp)
        {
            if (!izVar) { sonIzKonum = kp; izVar = true; }
            Vector3 a = sonIzKonum, b = kp; sonIzKonum = kp;
            Iz.Add(kp);
            int adim = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / (Hucre * 0.5f)));
            for (int s = 0; s <= adim; s++)
            {
                Vector3 p = Vector3.Lerp(a, b, s / (float)adim);
                if (!Hucreye(p.x, p.z, out int cx, out int cz)) continue;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = cx + dx, z = cz + dz;
                        if (x < 0 || z < 0 || x >= N || z >= N) continue;
                        iz[z * N + x] = 1; Sinir(x, z);
                    }
            }
            kirli = true;
        }

        void ZeminiCiz()
        {
            if (duzlem == null || !ArApp.ZeminVar) return;
            System.Array.Clear(zemin, 0, zemin.Length);
            foreach (var p in duzlem.trackables)
            {
                if (p.alignment != PlaneAlignment.HorizontalUp || p.trackingState == TrackingState.None) continue;
                if (Mathf.Abs(p.transform.position.y - ArApp.ZeminY) > 0.2f) continue;
                var bnd = p.boundary;
                int m = bnd.Length; if (m < 3) continue;
                var px = new float[m]; var pz = new float[m];
                float mn = float.MaxValue, mx = float.MinValue;
                for (int i = 0; i < m; i++)
                {
                    Vector3 w = p.transform.TransformPoint(new Vector3(bnd[i].x, 0, bnd[i].y));
                    px[i] = (w.x - Merkez.x) / Hucre + N / 2f; pz[i] = (w.z - Merkez.y) / Hucre + N / 2f;
                    if (pz[i] < mn) mn = pz[i]; if (pz[i] > mx) mx = pz[i];
                }
                int z0 = Mathf.Max(0, Mathf.FloorToInt(mn)), z1 = Mathf.Min(N - 1, Mathf.CeilToInt(mx));
                for (int z = z0; z <= z1; z++)
                {
                    float y = z + 0.5f; kes.Clear();
                    for (int i = 0; i < m; i++)
                    {
                        int j = (i + 1) % m;
                        float ya = pz[i], yb = pz[j];
                        if ((ya <= y && yb > y) || (yb <= y && ya > y)) kes.Add(px[i] + (y - ya) / (yb - ya) * (px[j] - px[i]));
                    }
                    kes.Sort();
                    for (int k = 0; k + 1 < kes.Count; k += 2)
                    {
                        int xa = Mathf.Max(0, Mathf.CeilToInt(kes[k] - 0.5f)), xb = Mathf.Min(N - 1, Mathf.FloorToInt(kes[k + 1] - 0.5f));
                        for (int x = xa; x <= xb; x++) { zemin[z * N + x] = 1; Sinir(x, z); }
                    }
                }
            }
            kirli = true;
        }

        void Yeniden()
        {
            if (MaxX < 0) return;
            var raw = doku.GetRawTextureData<Color32>();
            for (int z = MinZ; z <= MaxZ; z++)
            {
                int o = z * N;
                for (int x = MinX; x <= MaxX; x++)
                {
                    int i = o + x;
                    Color32 c;
                    if (iz[i] != 0) c = IzRenk;
                    else if (engel[i] >= 3) c = EngelRenk;
                    else if (zemin[i] != 0) c = ZeminRenk;
                    else c = default;
                    raw[i] = c;
                }
            }
            doku.Apply(false);
        }

        /// <summary>Haritanin kapsadigi dunya dikdortgeni (x,z), veri yoksa kamera cevresi.</summary>
        public Rect Kapsam()
        {
            if (MaxX < 0) return new Rect(Merkez.x - 3, Merkez.y - 3, 6, 6);
            float x0 = (MinX - N / 2) * Hucre + Merkez.x, z0 = (MinZ - N / 2) * Hucre + Merkez.y;
            return new Rect(x0, z0, (MaxX - MinX + 1) * Hucre, (MaxZ - MinZ + 1) * Hucre);
        }
    }

    /// <summary>Plan dokusunu gosteren ve ustune isaret/kristal/kullanici katmanlari ekleyen arayuz parcasi.</summary>
    public class PlanGorunumu : MonoBehaviour
    {
        public PlanHaritasi plan;
        public Camera kam;
        public IsaretYoneticisi isaretler;
        public RectTransform kok, icerik;
        public bool basYukari;
        public float ppm = 40f;          // piksel / metre
        public Vector2 bakis;            // dunya x,z merkez (basYukari=false iken)
        public bool kullaniciyiTakipEt = true;
        public OdaYoneticisi oda;
        readonly List<Image> duvarCizgi = new List<Image>(), kutuResim = new List<Image>();
        readonly List<Text> duvarYazi = new List<Text>();

        RawImage resim; Image kullanici, koni, iz2;
        readonly List<Image> kutular = new List<Image>(); readonly List<Text> etiketler = new List<Text>();
        readonly List<Image> kristalNoktalar = new List<Image>();

        public static PlanGorunumu Olustur(Transform ebeveyn, string ad, PlanHaritasi plan, Camera kam, IsaretYoneticisi isr, bool maskeli)
        {
            var kok = UiKur.Kutu(ebeveyn, ad, PlanHaritasi.ArkaPlan, true);
            var g = kok.gameObject.AddComponent<PlanGorunumu>();
            g.kok = kok.rectTransform; g.plan = plan; g.kam = kam; g.isaretler = isr;
            if (maskeli) { var m = kok.gameObject.AddComponent<Mask>(); m.showMaskGraphic = true; }
            // kenar cizgisi maskenin ustunde gorunsun
            var ic = UiKur.Obj("Icerik", kok.transform);
            ic.anchorMin = ic.anchorMax = new Vector2(0.5f, 0.5f); ic.pivot = new Vector2(0.5f, 0.5f);
            g.icerik = ic;
            g.resim = ic.gameObject.AddComponent<RawImage>();
            g.resim.texture = plan.doku; g.resim.raycastTarget = false;
            g.kullanici = null;
            // izgara cizgileri yerine sade: 1 m ince nokta deseni yok
            // kullanici: dolgu koni + nokta
            g.koni = UiKur.Simge(ic, "Koni", UiKur.Ucgen(), new Color(UiKur.Vurgu.r, UiKur.Vurgu.g, UiKur.Vurgu.b, 0.45f), 90f);
            g.koni.rectTransform.pivot = new Vector2(0.5f, 0.0f);
            g.kullanici = UiKur.Nokta(ic, "Kullanici", UiKur.Vurgu, 22f);
            var halo = UiKur.Nokta(g.kullanici.transform, "Halo", new Color(1, 1, 1, 0.95f), 30f);
            halo.transform.SetAsFirstSibling();
            return g;
        }

        public void Guncelle()
        {
            if (plan == null || !plan.Hazir || kam == null) return;
            var tr = kam.transform;
            Vector3 f = tr.forward; f.y = 0;
            float yaw = f.sqrMagnitude > 1e-4f ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : 0f;
            float boyut = PlanHaritasi.N * PlanHaritasi.Hucre * ppm;
            icerik.sizeDelta = new Vector2(boyut, boyut);

            Vector2 kullaniciDunya = new Vector2(tr.position.x, tr.position.z);
            Vector2 gorus = kullaniciyiTakipEt ? kullaniciDunya : bakis;
            Vector2 c = (gorus - plan.Merkez) * ppm;
            float rot = basYukari ? yaw : 0f;
            icerik.localRotation = Quaternion.Euler(0, 0, rot);
            float cs = Mathf.Cos(rot * Mathf.Deg2Rad), sn = Mathf.Sin(rot * Mathf.Deg2Rad);
            icerik.anchoredPosition = -new Vector2(c.x * cs - c.y * sn, c.x * sn + c.y * cs);

            Vector2 ku = (kullaniciDunya - plan.Merkez) * ppm;
            kullanici.rectTransform.anchoredPosition = ku;
            koni.rectTransform.anchoredPosition = ku;
            koni.rectTransform.localRotation = Quaternion.Euler(0, 0, -yaw);
            koni.rectTransform.sizeDelta = new Vector2(80f, 110f);
            Quaternion karsi = Quaternion.Euler(0, 0, -rot);

            // isaretler
            int k = 0;
            foreach (var kv in plan.Isaretler)
            {
                var b = kv.Value; if (!b.konumVar) continue;
                if (k >= kutular.Count)
                {
                    var kt = UiKur.Kutu(icerik, "Isaret" + k, new Color(UiKur.Uyari.r, UiKur.Uyari.g, UiKur.Uyari.b, 1f), false, false);
                    kt.rectTransform.sizeDelta = new Vector2(20, 20);
                    var et = UiKur.Metin(kt.transform, "Etiket", "", 22, UiKur.MetinRenk, TextAnchor.MiddleCenter, FontStyle.Bold);
                    et.rectTransform.sizeDelta = new Vector2(60, 28); et.rectTransform.anchoredPosition = new Vector2(0, 26);
                    et.horizontalOverflow = HorizontalWrapMode.Overflow;
                    kutular.Add(kt); etiketler.Add(et);
                }
                kutular[k].gameObject.SetActive(true);
                kutular[k].rectTransform.anchoredPosition = (new Vector2(b.konum.x, b.konum.z) - plan.Merkez) * ppm;
                kutular[k].rectTransform.localRotation = karsi;
                etiketler[k].text = b.id.ToString();
                k++;
            }
            for (int i = k; i < kutular.Count; i++) kutular[i].gameObject.SetActive(false);

            // kristaller
            for (int i = 0; i < plan.Kristaller.Count; i++)
            {
                if (i >= kristalNoktalar.Count) kristalNoktalar.Add(UiKur.Nokta(icerik, "Kristal" + i, new Color(0.56f, 0.9f, 1f, 1f), 14f));
                kristalNoktalar[i].gameObject.SetActive(true);
                kristalNoktalar[i].rectTransform.anchoredPosition = (new Vector2(plan.Kristaller[i].x, plan.Kristaller[i].z) - plan.Merkez) * ppm;
            }
            for (int i = plan.Kristaller.Count; i < kristalNoktalar.Count; i++) kristalNoktalar[i].gameObject.SetActive(false);

            OdaKatmani(karsi);
        }

        static string Metre(float m) { return m.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ','); }

        /// <summary>Oda modeli: duvarlar kalin cizgi + olcu yazisi, esyalar dolu yuvarlak dikdortgen.</summary>
        void OdaKatmani(Quaternion karsi)
        {
            int nd = oda != null ? oda.Duvarlar.Count : 0, nk = oda != null ? oda.Kutular.Count : 0;
            while (kutuResim.Count < nk)
            {
                var kt = UiKur.Kutu(icerik, "OdaKutu" + kutuResim.Count, new Color(1f, 1f, 1f, 0.55f), true);
                kutuResim.Add(kt);
            }
            for (int i = 0; i < kutuResim.Count; i++)
            {
                kutuResim[i].gameObject.SetActive(i < nk);
                if (i >= nk) continue;
                var b = oda.Kutular[i];
                var rt = kutuResim[i].rectTransform;
                rt.sizeDelta = new Vector2(b.sx * ppm, b.sz * ppm);
                rt.anchoredPosition = (new Vector2(b.cx, b.cz) - plan.Merkez) * ppm;
                rt.localRotation = Quaternion.Euler(0, 0, b.aci);
            }
            while (duvarCizgi.Count < nd)
            {
                var w = UiKur.Kutu(icerik, "OdaDuvar" + duvarCizgi.Count, Color.white, false, false);
                duvarCizgi.Add(w);
                var et = UiKur.Metin(icerik, "OdaOlcu" + duvarYazi.Count, "", 26, UiKur.MetinRenk, TextAnchor.MiddleCenter, FontStyle.Bold);
                et.rectTransform.sizeDelta = new Vector2(180, 36); et.horizontalOverflow = HorizontalWrapMode.Overflow;
                var sh = et.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.7f); sh.effectDistance = new Vector2(2, -2);
                duvarYazi.Add(et);
            }
            for (int i = 0; i < duvarCizgi.Count; i++)
            {
                bool var = i < nd;
                duvarCizgi[i].gameObject.SetActive(var); duvarYazi[i].gameObject.SetActive(var);
                if (!var) continue;
                var d = oda.Duvarlar[i];
                float len = d.Uzunluk;
                Vector2 mid = new Vector2((d.ax + d.bx) * 0.5f, (d.az + d.bz) * 0.5f);
                float ang = Mathf.Atan2(d.bz - d.az, d.bx - d.ax) * Mathf.Rad2Deg;
                var rt = duvarCizgi[i].rectTransform;
                rt.sizeDelta = new Vector2(len * ppm, 9f);
                rt.anchoredPosition = (mid - plan.Merkez) * ppm;
                rt.localRotation = Quaternion.Euler(0, 0, ang);
                var yz = duvarYazi[i];
                yz.text = len >= 0.7f ? Metre(len) + " m" : "";
                // yazi duvarin dis tarafinda (ic normalin tersi), okunur kalsin diye ters donduruluyor
                Vector2 ofs = new Vector2(-d.nx, -d.nz) * (30f / ppm);
                yz.rectTransform.anchoredPosition = (mid + ofs - plan.Merkez) * ppm;
                yz.rectTransform.localRotation = karsi;
            }
            kullanici.transform.SetAsLastSibling();
        }
    }
}
