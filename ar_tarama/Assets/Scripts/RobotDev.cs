using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace ArTarama
{
    /// <summary>
    /// Robot Dev ekranı (23 Kasım, kullanıcı: dev modu tarama uygulamasında, yalnız yön tuşlarıyla sürüş). Robotun 360
    /// görüntüsü (köprü /goruntu.mjpg) ekranda öne bakış olarak çizilir, üstüne evdeki AR yaratıkları konur.
    ///
    /// Robotun konumu (24 Kasım): dönüş ve ileri/yan hareket telefonda, ekrandaki karenin kendisinden ölçülür (RobotGorselIz,
    /// her karede, gecikmesiz). Robotun kendi konumu (işaret + Kalman süzgeci) hangi kamera karesine ait olduğunu söyler
    /// (konum.kare; görüntü akışında her karenin numarası X-Kare başlığında). Telefon o karedeki kendi ölçümüyle robotun
    /// konumunu karşılaştırıp aradaki farkı düzeltir: ağ gecikmesi ne olursa olsun karşılaştırma aynı an içindir, bu yüzden
    /// düzeltme hareket ederken de yapılır (eskiden yalnız dururken ve yavaşça yapılıyordu, telefonun kaybettiği açı sürüş
    /// boyunca hiç düzelmiyordu). Ne kadar düzeltileceği iki tarafın belirsizliğinden (robot: sy/sp, telefon: son
    /// düzeltmeden beri ne kadar dönüp gittiği) hesaplanır.
    /// Ölçüm (geliştirici için): robot işareti gördüğünde, o karede telefonun gösterdiği konuma göre işaretin hangi açıda
    /// görünmesi gerektiği ile robotun gördüğü açı karşılaştırılır = "AR hatası" (ekranda nesnelerin yana kayması). Ekranda
    /// ve konum.csv'de. İşaretin yerine sanal çerçeve çizilir: gerçek işaretin üstünde kalmalı.
    ///
    /// Kayıt: files/dev/yyyyMMdd_HHmmss/ altına her kare (k00000.jpg) ve kare başına ölçümler (iz.csv): bilgisayarda
    /// aynen tekrar oynatılabilir. Robot da kendi tarafını ~/konum_kayit altına yazar (konum_kayit_ac açıkken).
    /// </summary>
    public class RobotDev : MonoBehaviour
    {
        const int UdpPort = 8081, HttpPort = 8080, Katman = 29;
        const float KameraYuksek = 0.17f, Hfov = 110f, Egim = 8f, Ileri = 200, Donus = 150;

        public Action Kapandi;

        // ---- bağlantı
        string ip; UdpClient udp; Thread alici, akis; volatile bool calis;
        readonly object kilit = new object();
        byte[] sonJpeg; int jpegNo, islenenNo;
        struct KareBilgi { public int kare; public double zamanMs; public int sol, sag; public bool motorVar; }
        KareBilgi sonBilgi, islenenBilgi, oncekiBilgi; float oncekiKareZamani = -1f;
        string sonDurumJson; int durumNo, islenenDurum;
        int paketNo; float sonPaket;
        int komutSol, komutSag; float sonHareketKomutu = -10f;

        [Serializable] class Konum { public bool var; public float x, z, yaw; public int isaret; public float isaret_sn = -1; public string kaynak = "", hareket = ""; public int nokta; public float olcek;
                                     public int kare = -1; public float sy = 5f, sp = 0.1f; public float[] hata; public int kilit; }
        [Serializable] class Motor { public int sol, sag; }
        [Serializable] class Durum { public Konum konum; public Motor motor; public float fps; }
        Durum durum;

        // ---- görüntü ve iz
        Camera temizCam, yumusakCam; RenderTexture yumusakRt;
        RawImage videoImg, arImg; Material balik, yumusakMat;
        Texture2D kareTex; RobotGorselIz iz; RenderTexture gorusRt;
        Matrix4x4 gorus;

        // ---- konum birleştirme (ev koordinatı; yaw derece, +z'den sağa)
        float gYaw, gX, gZ;                  // görüntüden: toplam
        bool farkVar; float fYaw, fX, fZ;     // ev = döndür(fYaw)(görüntü) + (fX, fZ)   (tahmin)
        float dYaw, dX, dZ;                  // ekrandaki fark (tahmine 0,25 sn'de yumuşakça yetişir: düzeltme sıçramasın)
        float vYaw = 25f, vPoz = 0.01f;      // telefon farkının belirsizliği (derece², m²): telefon dönüp gittikçe büyür
        float donusK = 140f;                 // derece/sn, (sol-sag)=510'da; ölçümden öğrenilir (tahminli arama için)
        struct Gecmis { public int kare; public double zamanMs; public float gYaw, gX, gZ, pX, pZ, pYaw; }
        readonly List<Gecmis> gecmis = new List<Gecmis>();
        // ölçüm
        readonly List<Vector2> arHata = new List<Vector2>();    // (zaman, derece): işaretin beklenen - görülen açısı
        float sonGecikmeMs = -1f; int sonGecikmeKare = -1; int eslesmeyen; float sonTahmin; int tahminKare;
        StreamWriter konumCsv;
        public bool PozVar { get; private set; }
        public float PozX { get; private set; }
        public float PozZ { get; private set; }
        public float PozYaw { get; private set; }
        float sonKonumX = float.NaN, sonKonumZ, sonKonumYaw;
        float sonGorselZaman = -10f;

        // ---- yaratıklar
        Transform kok;
        class Yaratik { public Transform t; public Vector2 ev; public float bas, faz, boy; public Transform govde; public Color renk; }
        readonly List<Yaratik> yaratiklar = new List<Yaratik>();

        // ---- arayüz
        Canvas arayuz; Text bilgi; RawImage haritaImg; RectTransform haritaPanel, harita, ok; Texture2D haritaTex;
        float hox, hoz; int hW, hH; float sonHarita = -100f;
        readonly List<RectTransform> isaretler = new List<RectTransform>(), haritaYaratik = new List<RectTransform>();
        [Serializable] class ZeminJson { public int[] acik; }
        [Serializable] class Isaret { public int id; public float x, z, aci, yukseklik; }
        [Serializable] class EvJson { public string ad; public Isaret[] isaretler; }
        EvJson ev;
        readonly Dictionary<string, bool> basili = new Dictionary<string, bool>();

        // ---- kayıt
        string kayitKlasor; StreamWriter csv; int kayitKare; float sonCsvFlush;
        ScreenOrientation oncekiYon;

        public static RobotDev Ac(Action kapandi)
        {
            var go = new GameObject("Robot Dev");
            var d = go.AddComponent<RobotDev>(); d.Kapandi = kapandi;
            return d;
        }

        void Start()
        {
            oncekiYon = Screen.orientation;
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            var sh = Shader.Find("ArTarama/RobotFisheye");
            balik = new Material(sh);
            Mount(0f, 0f, 90f);
            iz = new RobotGorselIz(balik);
            Kur();
            KayitAc();
            Gunluk.Yaz("dev", "acildi");
            EvSec();
        }

        // ------------------------------------------------------------------ kamera takılışı (robot uygulamasıyla aynı)
        /// <summary>duzIleri / duzYan: kameranın robota göre ölçülmüş eğimi (robot konum/teta.py EGIM; 24 Kasım: yana 2,5
        /// derece). Görüntü düzeltilmezse AR nesneleri zemine oturmaz, işaret çerçevesi yanında kalır.</summary>
        void Mount(float yon, float egim, float yatma, float duzIleri = 0f, float duzYan = 0f)
        {
            balik.SetMatrix("_Mount", Rz(yatma) * Rx(egim) * Ry(yon) * Rz(duzYan) * Rx(-duzIleri));
            balik.SetMatrix("_Lens2Rotation", Rz(0.683f) * Ry(0.984f) * Rx(-0.141f));
        }
        static Matrix4x4 Rx(float a) { float c = Mathf.Cos(a * Mathf.Deg2Rad), s = Mathf.Sin(a * Mathf.Deg2Rad); return Satir(1, 0, 0, 0, c, -s, 0, s, c); }
        static Matrix4x4 Ry(float a) { float c = Mathf.Cos(a * Mathf.Deg2Rad), s = Mathf.Sin(a * Mathf.Deg2Rad); return Satir(c, 0, s, 0, 1, 0, -s, 0, c); }
        static Matrix4x4 Rz(float a) { float c = Mathf.Cos(a * Mathf.Deg2Rad), s = Mathf.Sin(a * Mathf.Deg2Rad); return Satir(c, -s, 0, s, c, 0, 0, 0, 1); }
        static Matrix4x4 Satir(float a, float b, float c, float d, float e, float f, float g, float h, float i)
        {
            var m = Matrix4x4.identity;
            m.SetRow(0, new Vector4(a, b, c, 0)); m.SetRow(1, new Vector4(d, e, f, 0)); m.SetRow(2, new Vector4(g, h, i, 0));
            return m;
        }

        // ------------------------------------------------------------------ sahne ve arayüz
        void Kur()
        {
            temizCam = new GameObject("Dev kamera").AddComponent<Camera>();
            temizCam.transform.SetParent(transform, false);
            temizCam.clearFlags = CameraClearFlags.SolidColor; temizCam.backgroundColor = Color.black; temizCam.cullingMask = 0; temizCam.depth = 50;

            kok = new GameObject("Robot (kamera)").transform; kok.SetParent(transform, false); kok.position = new Vector3(0, -500, 0);
            yumusakCam = new GameObject("AR kamera").AddComponent<Camera>();
            yumusakCam.transform.SetParent(kok, false);
            yumusakCam.transform.localRotation = Quaternion.Euler(Egim, 0, 0);
            yumusakCam.cullingMask = 1 << Katman; yumusakCam.clearFlags = CameraClearFlags.SolidColor;
            yumusakCam.backgroundColor = new Color(0, 0, 0, 0); yumusakCam.nearClipPlane = 0.03f; yumusakCam.farClipPlane = 20f; yumusakCam.depth = 40;
            gorus = Matrix4x4.Rotate(Quaternion.Euler(Egim, 0, 0));

            var cg = new GameObject("Dev video", typeof(Canvas)); cg.transform.SetParent(transform, false);
            var cv = cg.GetComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 100;
            videoImg = TamEkran(cv.transform, "Video"); videoImg.color = Color.white;   // görüş dokusu Gorus()'te çizilir
            arImg = TamEkran(cv.transform, "AR");
            var ys = Shader.Find("ARaki/SoftComposite");
            if (ys != null) { yumusakMat = new Material(ys); yumusakMat.SetFloat("_Soft", 0.9f); arImg.material = yumusakMat; }

            var ug = new GameObject("Dev arayuz", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); ug.transform.SetParent(transform, false);
            arayuz = ug.GetComponent<Canvas>(); arayuz.renderMode = RenderMode.ScreenSpaceOverlay; arayuz.sortingOrder = 110;
            var sc = ug.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 1f;

            // çift dokunma: zemine yaratık
            var dokun = TamEkran(arayuz.transform, "Dokunma"); dokun.color = new Color(0, 0, 0, 0); dokun.raycastTarget = true;
            dokun.gameObject.AddComponent<CiftDokun>().dokundu = CiftDokunuldu;

            Tus("Ileri", "▲", new Vector2(1, 0), new Vector2(-170, 290));
            Tus("Geri", "▼", new Vector2(1, 0), new Vector2(-170, 110));
            Tus("Sol", "◀", new Vector2(0, 0), new Vector2(140, 170));
            Tus("Sag", "▶", new Vector2(0, 0), new Vector2(330, 170));
            var cik = Dugme("Cik", "Çık", new Vector2(1, 1), new Vector2(-110, -70), new Vector2(180, 90));
            cik.onClick.AddListener(Kapat);

            haritaPanel = Rect("Harita", arayuz.transform);
            haritaPanel.anchorMin = haritaPanel.anchorMax = haritaPanel.pivot = new Vector2(0, 1);
            haritaPanel.anchoredPosition = new Vector2(24, -24); haritaPanel.sizeDelta = new Vector2(360, 360);
            var hb = haritaPanel.gameObject.AddComponent<Image>(); hb.color = new Color(0.04f, 0.045f, 0.055f, 0.78f); hb.raycastTarget = false;
            haritaPanel.gameObject.AddComponent<RectMask2D>();
            var ic = Rect("Icerik", haritaPanel); ic.anchorMin = ic.anchorMax = new Vector2(0.5f, 0.5f); ic.sizeDelta = Vector2.zero;
            harita = Rect("Zemin", ic); harita.anchorMin = harita.anchorMax = new Vector2(0.5f, 0.5f);
            haritaImg = harita.gameObject.AddComponent<RawImage>(); haritaImg.raycastTarget = false; haritaImg.enabled = false;
            ok = Rect("Robot", haritaPanel); ok.anchorMin = ok.anchorMax = new Vector2(0.5f, 0.5f); ok.sizeDelta = new Vector2(22, 30);
            var oi = ok.gameObject.AddComponent<Image>(); oi.sprite = Ucgen(); oi.color = new Color(1f, 0.62f, 0.25f); oi.raycastTarget = false;

            var bt = Rect("Bilgi", arayuz.transform); bt.anchorMin = bt.anchorMax = bt.pivot = new Vector2(0, 1);
            bt.anchoredPosition = new Vector2(400, -24); bt.sizeDelta = new Vector2(1000, 260);
            bilgi = bt.gameObject.AddComponent<Text>(); bilgi.font = UiKur.Yazi; bilgi.fontSize = 26; bilgi.color = Color.white; bilgi.raycastTarget = false;
            var gl = bt.gameObject.AddComponent<Shadow>(); gl.effectColor = new Color(0, 0, 0, 0.8f); gl.effectDistance = new Vector2(2, -2);
        }

        static RawImage TamEkran(Transform p, string ad)
        {
            var r = Rect(ad, p); r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            var i = r.gameObject.AddComponent<RawImage>(); i.raycastTarget = false; return i;
        }
        static RectTransform Rect(string ad, Transform p) { var g = new GameObject(ad, typeof(RectTransform)); g.transform.SetParent(p, false); return (RectTransform)g.transform; }

        void Tus(string ad, string etiket, Vector2 ankor, Vector2 yer)
        {
            var r = Rect(ad, arayuz.transform); r.anchorMin = r.anchorMax = ankor; r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = yer; r.sizeDelta = new Vector2(170, 160);
            var im = r.gameObject.AddComponent<Image>(); im.color = new Color(1, 1, 1, 0.16f);
            var t = Rect("Yazi", r); t.anchorMin = Vector2.zero; t.anchorMax = Vector2.one; t.offsetMin = t.offsetMax = Vector2.zero;
            var tx = t.gameObject.AddComponent<Text>(); tx.font = UiKur.Yazi; tx.text = etiket; tx.fontSize = 70; tx.alignment = TextAnchor.MiddleCenter; tx.color = Color.white; tx.raycastTarget = false;
            var b = r.gameObject.AddComponent<BasiliTus>(); basili[ad] = false;
            b.degisti = v => { basili[ad] = v; im.color = new Color(1, 1, 1, v ? 0.42f : 0.16f); };
        }

        Button Dugme(string ad, string etiket, Vector2 ankor, Vector2 yer, Vector2 boy)
        {
            var r = Rect(ad, arayuz.transform); r.anchorMin = r.anchorMax = ankor; r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = yer; r.sizeDelta = boy;
            var im = r.gameObject.AddComponent<Image>(); im.color = new Color(0, 0, 0, 0.55f);
            var t = Rect("Yazi", r); t.anchorMin = Vector2.zero; t.anchorMax = Vector2.one; t.offsetMin = t.offsetMax = Vector2.zero;
            var tx = t.gameObject.AddComponent<Text>(); tx.font = UiKur.Yazi; tx.text = etiket; tx.fontSize = 36; tx.alignment = TextAnchor.MiddleCenter; tx.color = Color.white; tx.raycastTarget = false;
            return r.gameObject.AddComponent<Button>();
        }

        static Sprite daire;
        static Sprite Daire()
        {
            if (daire != null) return daire;
            const int S = 32; var t = new Texture2D(S, S, TextureFormat.RGBA32, false);
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) { float d = new Vector2(x - S / 2 + 0.5f, y - S / 2 + 0.5f).magnitude; t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(S / 2 - 1 - d))); }
            t.Apply(); return daire = Sprite.Create(t, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        }
        static Sprite ucgen;
        static Sprite Ucgen()
        {
            if (ucgen != null) return ucgen;
            const int S = 64; var t = new Texture2D(S, S, TextureFormat.RGBA32, false);
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) t.SetPixel(x, y, Mathf.Abs(x / (float)S - 0.5f) < 0.5f - y / (float)S * 0.5f ? Color.white : Color.clear);
            t.Apply(); return ucgen = Sprite.Create(t, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        }

        class BasiliTus : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
        {
            public Action<bool> degisti;
            public void OnPointerDown(PointerEventData e) => degisti?.Invoke(true);
            public void OnPointerUp(PointerEventData e) => degisti?.Invoke(false);
            public void OnPointerExit(PointerEventData e) => degisti?.Invoke(false);
        }

        class CiftDokun : MonoBehaviour, IPointerClickHandler
        {
            public Action<Vector2> dokundu; float son = -10f; Vector2 sonYer;
            public void OnPointerClick(PointerEventData e)
            {
                if (Time.unscaledTime - son < 0.35f && (e.position - sonYer).magnitude < 80f) { dokundu?.Invoke(e.position); son = -10f; }
                else { son = Time.unscaledTime; sonYer = e.position; }
            }
        }

        // ------------------------------------------------------------------ bağlantı
        // ------------------------------------------------------------------ ev seçimi
        GameObject secimPanel;

        /// <summary>Hangi evde sürülecek: seçilen evin haritası robota gönderilir (konum onun işaretleriyle bulunur).</summary>
        void EvSec()
        {
            var evler = EvDeposu.Listele();
            if (evler.Count == 0) { StartCoroutine(Baglan(null)); return; }
            int n = Mathf.Min(evler.Count, 6);
            var p = Rect("Ev secimi", arayuz.transform); p.anchorMin = p.anchorMax = p.pivot = new Vector2(0.5f, 0.5f);
            p.sizeDelta = new Vector2(900, 150 + 120 * n);
            p.gameObject.AddComponent<Image>().color = new Color(0.06f, 0.07f, 0.09f, 0.95f);
            secimPanel = p.gameObject;
            var bt = Rect("Baslik", p); bt.anchorMin = new Vector2(0, 1); bt.anchorMax = new Vector2(1, 1); bt.pivot = new Vector2(0.5f, 1);
            bt.anchoredPosition = new Vector2(0, -20); bt.sizeDelta = new Vector2(-60, 90);
            var bx = bt.gameObject.AddComponent<Text>(); bx.font = UiKur.Yazi; bx.fontSize = 44; bx.color = Color.white; bx.alignment = TextAnchor.MiddleLeft;
            bx.text = "Hangi evde süreceksin?";
            for (int i = 0; i < n; i++)
            {
                string klasor = EvDeposu.Klasor(evler[i]); string ad = evler[i];
                try { var e = Ev.Yukle(klasor); if (e != null) ad = e.ad + "   (" + evler[i].Replace("ev_", "") + ")"; } catch (Exception) { }
                var r = Rect("Ev", p); r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0.5f, 1);
                r.anchoredPosition = new Vector2(0, -130 - 120 * i); r.sizeDelta = new Vector2(-60, 104);
                r.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0.1f);
                var t = Rect("Yazi", r); t.anchorMin = Vector2.zero; t.anchorMax = Vector2.one; t.offsetMin = new Vector2(30, 0); t.offsetMax = Vector2.zero;
                var tx = t.gameObject.AddComponent<Text>(); tx.font = UiKur.Yazi; tx.fontSize = 38; tx.color = Color.white; tx.alignment = TextAnchor.MiddleLeft; tx.text = ad; tx.raycastTarget = false;
                r.gameObject.AddComponent<Button>().onClick.AddListener(() => { Destroy(secimPanel); secimPanel = null; StartCoroutine(Baglan(klasor)); });
            }
        }

        IEnumerator Baglan(string evKlasoru)
        {
            bilgi.text = "Robot aranıyor…";
            var t = RobotaGonder.RobotuBul(s => { });
            while (!t.IsCompleted) yield return null;
            ip = t.Result;
            if (ip == null) { bilgi.text = "Robot bulunamadı. Robot açık ve telefonla aynı ağda mı?"; Gunluk.Yaz("dev", "robot bulunamadi"); yield break; }
            Gunluk.Yaz("dev", "robot: " + ip + ", ev: " + (evKlasoru ?? "robottaki"));
            if (evKlasoru != null)
            {
                bilgi.text = "Ev haritası robota gönderiliyor…";
                var g = RobotaGonder.Gonder(evKlasoru, s => { });
                while (!g.IsCompleted) yield return null;
                Gunluk.Yaz("dev", "ev gonderildi: " + (g.Result ?? "tamam"));
            }
            using (var r = UnityWebRequest.Get("http://" + ip + ":" + HttpPort + "/api/kamera-ayari"))
            {
                r.timeout = 4; yield return r.SendWebRequest();
                if (r.result == UnityWebRequest.Result.Success && r.downloadHandler.text.Length > 3)
                {
                    var k = JsonUtility.FromJson<KameraAyari>(r.downloadHandler.text); Mount(k.yon, k.egim, k.yatma, k.duz_ileri, k.duz_yan);
                    Gunluk.Yaz("dev", "kamera ayari: " + r.downloadHandler.text);
                }
            }
            calis = true;
            udp = new UdpClient(0);
            alici = new Thread(UdpAl) { IsBackground = true }; alici.Start();
            akis = new Thread(MjpegOku) { IsBackground = true }; akis.Start();
            StartCoroutine(HaritaGetir(evKlasoru));
        }
        [Serializable] class KameraAyari { public float yon = 0f, egim = 0f, yatma = 90f, duz_ileri = 0f, duz_yan = 0f; }

        void UdpAl()
        {
            var uc = new IPEndPoint(IPAddress.Any, 0);
            while (calis)
            {
                try
                {
                    var b = udp.Receive(ref uc);
                    lock (kilit) { sonDurumJson = Encoding.UTF8.GetString(b); durumNo++; }
                }
                catch (Exception) { if (!calis) return; Thread.Sleep(50); }
            }
        }

        void MjpegOku()
        {
            while (calis)
            {
                try
                {
                    var istek = (HttpWebRequest)WebRequest.Create("http://" + ip + ":" + HttpPort + "/goruntu.mjpg?fps=15");
                    istek.Timeout = 6000; istek.ReadWriteTimeout = 6000;
                    using (var c = istek.GetResponse())
                    using (var s = new BufferedStream(c.GetResponseStream(), 1 << 16))
                    {
                        while (calis)
                        {
                            int uzunluk = -1; string satir; var kb = new KareBilgi { kare = -1 };
                            var ci = System.Globalization.CultureInfo.InvariantCulture;
                            while ((satir = Satir(s)) != null)
                            {
                                if (satir.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) int.TryParse(satir.Substring(15).Trim(), out uzunluk);
                                else if (satir.StartsWith("X-Kare:", StringComparison.OrdinalIgnoreCase)) int.TryParse(satir.Substring(7).Trim(), out kb.kare);
                                else if (satir.StartsWith("X-Zaman:", StringComparison.OrdinalIgnoreCase)) double.TryParse(satir.Substring(8).Trim(), System.Globalization.NumberStyles.Float, ci, out kb.zamanMs);
                                else if (satir.StartsWith("X-Motor:", StringComparison.OrdinalIgnoreCase))
                                {
                                    var m = satir.Substring(8).Split(',');
                                    kb.motorVar = m.Length == 2 && int.TryParse(m[0].Trim(), out kb.sol) && int.TryParse(m[1].Trim(), out kb.sag);
                                }
                                else if (satir.Length == 0 && uzunluk > 0) break;
                            }
                            if (satir == null) break;
                            var j = new byte[uzunluk]; int o = 0;
                            while (o < uzunluk) { int n = s.Read(j, o, uzunluk - o); if (n <= 0) throw new IOException("akis bitti"); o += n; }
                            lock (kilit) { sonJpeg = j; sonBilgi = kb; jpegNo++; }
                        }
                    }
                }
                catch (Exception e) { Gunluk.Yaz("dev", "goruntu akisi: " + e.Message); Thread.Sleep(1000); }
            }
        }

        static string Satir(Stream s)
        {
            var sb = new StringBuilder();
            while (true)
            {
                int c = s.ReadByte(); if (c < 0) return null;
                if (c == '\n') return sb.ToString().TrimEnd('\r');
                if (sb.Length < 256) sb.Append((char)c);
            }
        }

        void Gonder()
        {
            if (udp == null || ip == null || Time.unscaledTime - sonPaket < 0.05f) return;
            sonPaket = Time.unscaledTime; paketNo++;
            var j = "{\"t\":\"d\",\"n\":" + paketNo + ",\"sol\":" + komutSol + ",\"sag\":" + komutSag + ",\"konum\":1}";
            var b = Encoding.ASCII.GetBytes(j);
            try { udp.Send(b, b.Length, ip, UdpPort); } catch (Exception) { }
        }

        IEnumerator HaritaGetir(string evKlasoru)
        {
            if (evKlasoru != null)
            {
                // seçilen ev telefonda: harita doğrudan dosyadan (robota da aynısı gönderildi)
                try { ev = JsonUtility.FromJson<EvJson>(File.ReadAllText(Path.Combine(evKlasoru, "ev.json"))); } catch (Exception) { }
                ZeminJson yz = null;
                try { yz = JsonUtility.FromJson<ZeminJson>(File.ReadAllText(Path.Combine(evKlasoru, "zemin.json"))); } catch (Exception) { }
                if (yz?.acik != null && yz.acik.Length >= 2) { HaritaCiz(yz.acik); yield break; }
            }
            using (var r = UnityWebRequest.Get("http://" + ip + ":" + HttpPort + "/api/ev"))
            {
                r.timeout = 6; yield return r.SendWebRequest();
                if (r.result == UnityWebRequest.Result.Success) { try { ev = JsonUtility.FromJson<EvJson>(r.downloadHandler.text); } catch (Exception) { } }
            }
            using (var r = UnityWebRequest.Get("http://" + ip + ":" + HttpPort + "/api/ev/zemin"))
            {
                r.timeout = 8; yield return r.SendWebRequest();
                if (r.result != UnityWebRequest.Result.Success) yield break;
                ZeminJson z = null; try { z = JsonUtility.FromJson<ZeminJson>(r.downloadHandler.text); } catch (Exception) { }
                if (z?.acik == null || z.acik.Length < 2) yield break;
                HaritaCiz(z.acik);
            }
        }

        void HaritaCiz(int[] acik)
        {
            {
                int x0 = int.MaxValue, x1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue; var a = acik;
                for (int i = 0; i + 1 < a.Length; i += 2) { x0 = Math.Min(x0, a[i]); x1 = Math.Max(x1, a[i]); z0 = Math.Min(z0, a[i + 1]); z1 = Math.Max(z1, a[i + 1]); }
                x0 -= 2; z0 -= 2; hW = x1 - x0 + 3; hH = z1 - z0 + 3;
                if (hW * hH > 4000000) return;
                var dolu = new float[hW * hH];
                for (int i = 0; i + 1 < a.Length; i += 2) dolu[(a[i + 1] - z0) * hW + (a[i] - x0)] = 1f;
                // 3x3 ortalama (iki kez): kenar yumuşar, tek hücrelik girinti / çıkıntı silinir
                for (int tekrar = 0; tekrar < 2; tekrar++)
                {
                    var b = new float[hW * hH];
                    for (int z = 0; z < hH; z++) for (int x = 0; x < hW; x++)
                        {
                            float t = 0f; int c = 0;
                            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) { int xx = x + dx, zz = z + dz; if (xx < 0 || zz < 0 || xx >= hW || zz >= hH) continue; t += dolu[zz * hW + xx]; c++; }
                            b[z * hW + x] = t / c;
                        }
                    dolu = b;
                }
                const int U = 4; int W4 = hW * U, H4 = hH * U;
                var px = new Color32[W4 * H4];
                for (int z = 0; z < H4; z++)
                    for (int x = 0; x < W4; x++)
                    {
                        float fx = (x + 0.5f) / U - 0.5f, fz = (z + 0.5f) / U - 0.5f;
                        int ix = Mathf.Clamp((int)Mathf.Floor(fx), 0, hW - 2), iz = Mathf.Clamp((int)Mathf.Floor(fz), 0, hH - 2);
                        float tx = Mathf.Clamp01(fx - ix), tz = Mathf.Clamp01(fz - iz);
                        float v = Mathf.Lerp(Mathf.Lerp(dolu[iz * hW + ix], dolu[iz * hW + ix + 1], tx), Mathf.Lerp(dolu[(iz + 1) * hW + ix], dolu[(iz + 1) * hW + ix + 1], tx), tz);
                        float al = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.58f, v));
                        px[z * W4 + x] = new Color32(67, 217, 190, (byte)(210 * al));
                    }
                haritaTex = new Texture2D(W4, H4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                haritaTex.SetPixels32(px); haritaTex.Apply(false);
                haritaImg.texture = haritaTex; haritaImg.enabled = true; hox = x0 * 0.05f; hoz = z0 * 0.05f;
                Gunluk.Yaz("dev", "ev haritasi: " + (ev != null ? ev.ad : "?") + " " + a.Length / 2 + " hucre");
            }
        }

        // ------------------------------------------------------------------ her kare
        void Update()
        {
            // sürüş komutu
            int f = (basili.TryGetValue("Ileri", out bool i1) && i1 ? 1 : 0) - (basili.TryGetValue("Geri", out bool i2) && i2 ? 1 : 0);
            int d = (basili.TryGetValue("Sag", out bool i3) && i3 ? 1 : 0) - (basili.TryGetValue("Sol", out bool i4) && i4 ? 1 : 0);
            komutSol = Mathf.Clamp(Mathf.RoundToInt(f * Ileri + d * Donus), -255, 255);
            komutSag = Mathf.Clamp(Mathf.RoundToInt(f * Ileri - d * Donus), -255, 255);
            if (komutSol != 0 || komutSag != 0) sonHareketKomutu = Time.unscaledTime;
            Gonder();

            // robot durumu
            string dj = null;
            lock (kilit) { if (durumNo != islenenDurum) { islenenDurum = durumNo; dj = sonDurumJson; } }
            bool yeniKonum = false; string konumJson = null;
            if (dj != null)
            {
                try { durum = JsonUtility.FromJson<Durum>(dj); } catch (Exception) { }
                var k = durum?.konum;
                if (k != null && k.var && (k.x != sonKonumX || k.z != sonKonumZ || k.yaw != sonKonumYaw || (k.kare >= 0 && k.kare != sonKonumKare)))
                { sonKonumX = k.x; sonKonumZ = k.z; sonKonumYaw = k.yaw; sonKonumKare = k.kare; yeniKonum = true; konumJson = dj; }
            }

            // yeni kare: göster ve ölç
            byte[] jpeg = null; int no = 0; KareBilgi kb = default;
            lock (kilit) { if (jpegNo != islenenNo) { islenenNo = jpegNo; jpeg = sonJpeg; no = jpegNo; kb = sonBilgi; } }
            if (jpeg != null)
            {
                if (kareTex == null) kareTex = new Texture2D(2, 2, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                if (kareTex.LoadImage(jpeg))
                {
                    oncekiBilgi = islenenBilgi; islenenBilgi = kb;
                    sonTahmin = DonusTahmini(kb);
                    iz.Kare(kareTex, sonTahmin);
                    GorselAdim();
                    KareKaydet(jpeg);
                }
            }
            if (yeniKonum) KonumGeldi(durum.konum, konumJson);
            PozGuncelle();
            Isaretler();
            Gorus();
            Yaratiklar();
            Harita();
            Bilgi();
        }

        /// <summary>Bu karede beklenen dönüş (derece): karenin çekildiği andaki motorlar (köprü X-Motor) ve önceki işlenen
        /// kareden beri geçen süre (X-Zaman, robot saati: ağdaki takılma süreyi bozmaz). Görüntü pusulası bunun çevresinde arar.</summary>
        float DonusTahmini(KareBilgi kb)
        {
            float simdi = Time.realtimeSinceStartup;
            float dt = oncekiKareZamani < 0 ? 0f : simdi - oncekiKareZamani;
            oncekiKareZamani = simdi;
            if (kb.zamanMs > 0 && oncekiBilgi.zamanMs > 0 && kb.zamanMs > oncekiBilgi.zamanMs) dt = (float)((kb.zamanMs - oncekiBilgi.zamanMs) / 1000.0);
            dt = Mathf.Clamp(dt, 0f, 1.5f);
            int sol, sag;
            if (kb.motorVar) { sol = (kb.sol + (oncekiBilgi.motorVar ? oncekiBilgi.sol : kb.sol)) / 2; sag = (kb.sag + (oncekiBilgi.motorVar ? oncekiBilgi.sag : kb.sag)) / 2; }
            else if (durum?.motor != null) { sol = durum.motor.sol; sag = durum.motor.sag; }
            else { sol = komutSol; sag = komutSag; }
            tahminSol = sol; tahminSag = sag; tahminDt = dt;
            return donusK * (sol - sag) / 510f * dt;
        }
        int tahminSol, tahminSag; float tahminDt; int sonKonumKare = -1;

        void GorselAdim()
        {
            if (iz.PusulaTuttu || iz.TahminKullanildi)
            {
                gYaw += iz.AdimDonus;
                // telefonun yönü dönüş başına belirsizleşir (ölçüm %5, tahmin %30)
                // 24 Kasım kaydı: telefon pusulası hızlı dönüşte %30-50 eksik sayabiliyor (dev_olc.py): belirsizlik buna göre
                float s = (iz.TahminKullanildi ? 0.5f : 0.3f) * Mathf.Abs(iz.AdimDonus) + 0.1f;
                vYaw += s * s;
                if (iz.TahminKullanildi) tahminKare++;
                // dönüş hızı katsayısı (yalnız güvenilir ölçümle)
                if (iz.PusulaTuttu && tahminDt > 0.03f && Mathf.Abs(tahminSol - tahminSag) > 150 && Mathf.Abs(iz.AdimDonus) > 3f)
                {
                    float olc = iz.AdimDonus / tahminDt / ((tahminSol - tahminSag) / 510f);
                    if (olc > 40f && olc < 400f) donusK = donusK * 0.95f + olc * 0.05f;
                }
            }
            else vYaw += 4f;      // ölçülemedi: dönüş bilinmiyor
            if (iz.ZeminTuttu)
            {
                float s = Mathf.Sin(gYaw * Mathf.Deg2Rad), c = Mathf.Cos(gYaw * Mathf.Deg2Rad);
                gX += s * iz.AdimIleri + c * iz.AdimSag; gZ += c * iz.AdimIleri - s * iz.AdimSag;
                float yol = Mathf.Sqrt(iz.AdimIleri * iz.AdimIleri + iz.AdimSag * iz.AdimSag);
                vPoz += (0.3f * yol + 0.002f) * (0.3f * yol + 0.002f);
            }
            else if (komutSol != 0 || komutSag != 0) vPoz += 0.0004f;     // gidiyor ama ölçülemiyor
            if (iz.PusulaTuttu) sonGorselZaman = Time.unscaledTime;
            vYaw = Mathf.Min(vYaw, 90f * 90f); vPoz = Mathf.Min(vPoz, 4f);
            // bu karenin görüntü ölçümü ve ekranda gösterilen konum (robot konumu geldiğinde aynı kareyle karşılaştırılır)
            gecmis.Add(new Gecmis { kare = islenenBilgi.kare, zamanMs = islenenBilgi.zamanMs, gYaw = gYaw, gX = gX, gZ = gZ, pX = PozX, pZ = PozZ, pYaw = PozYaw });
            if (gecmis.Count > 400) gecmis.RemoveRange(0, gecmis.Count - 400);
        }

        bool GorselSaglam => Time.unscaledTime - sonGorselZaman < 0.6f;

        /// <summary>Robotun konumu geldi (kare numarasıyla). O karedeki telefon ölçümü bulunur, ikisi arasındaki fark
        /// belirsizliklere göre düzeltilir. Kare numarası yoksa (eski köprü) yalnız robot dururken yavaşça (eski yol).</summary>
        void KonumGeldi(Konum k, string json)
        {
            int i = -1;
            if (k.kare >= 0)
                for (int j = gecmis.Count - 1; j >= 0; j--) if (gecmis[j].kare >= 0 && gecmis[j].kare <= k.kare) { i = j; break; }
            Gecmis g;
            if (i >= 0)
            {
                g = gecmis[i];
                // robot o kareyi işlemiş, telefona gelmemiş olabilir (akış kareleri atlar): sonraki kareyle ara değer
                if (g.kare != k.kare && i + 1 < gecmis.Count && gecmis[i + 1].kare > k.kare)
                {
                    var h = gecmis[i + 1]; float t = (k.kare - g.kare) / (float)(h.kare - g.kare);
                    g.gYaw = g.gYaw + Mathf.DeltaAngle(g.gYaw, h.gYaw) * t; g.gX = Mathf.Lerp(g.gX, h.gX, t); g.gZ = Mathf.Lerp(g.gZ, h.gZ, t);
                    g.pYaw = g.pYaw + Mathf.DeltaAngle(g.pYaw, h.pYaw) * t; g.pX = Mathf.Lerp(g.pX, h.pX, t); g.pZ = Mathf.Lerp(g.pZ, h.pZ, t);
                }
                var son = gecmis[gecmis.Count - 1];
                sonGecikmeKare = son.kare - k.kare;
                sonGecikmeMs = son.zamanMs > 0 && g.zamanMs > 0 ? (float)(son.zamanMs - g.zamanMs) : -1f;
            }
            else
            {
                eslesmeyen++;
                if (k.kare >= 0 && gecmis.Count > 0 && gecmis[0].kare > k.kare) return;   // çok eski konum
                bool duruyor = Time.unscaledTime - sonHareketKomutu > 1.0f && (durum?.motor == null || (durum.motor.sol == 0 && durum.motor.sag == 0));
                if (farkVar && !duruyor) return;
                g = new Gecmis { kare = -1, gYaw = gYaw, gX = gX, gZ = gZ, pX = PozX, pZ = PozZ, pYaw = PozYaw };
            }

            // ölçüm: robot işareti bu karede gördüyse, telefonun o karede gösterdiği konuma göre işaret hangi açıda olmalıydı
            float arH = float.NaN; int arId = -1;
            if (PozVar && i >= 0 && ev?.isaretler != null)
                foreach (var gr in Gorulen(json))
                    foreach (var e in ev.isaretler)
                        if (e.id == gr.x && gr.y < 2.5f)
                        {
                            float c0 = Mathf.Cos(g.pYaw * Mathf.Deg2Rad), s0 = Mathf.Sin(g.pYaw * Mathf.Deg2Rad);
                            float dx = e.x - g.pX, dz = e.z - g.pZ;
                            float sag = dx * c0 - dz * s0, ileri = dx * s0 + dz * c0;
                            arH = Mathf.DeltaAngle(gr.z, Mathf.Atan2(sag, ileri) * Mathf.Rad2Deg); arId = e.id;
                            if (!float.IsNaN(gr.w) && gr.y < 1.8f)
                            {
                                isaretYuk.TryGetValue(e.id, out var ys);
                                ys = ys.y < 1f ? new Vector2(gr.w, 1f) : new Vector2(ys.x + (gr.w - ys.x) / Mathf.Min(ys.y + 1f, 30f), ys.y + 1f);
                                isaretYuk[e.id] = ys;
                            }
                            arHata.Add(new Vector2(Time.unscaledTime, arH));
                        }
            if (arHata.Count > 0 && Time.unscaledTime - arHata[0].x > 30f) arHata.RemoveAll(q => Time.unscaledTime - q.x > 30f);

            float hYaw = Mathf.DeltaAngle(0f, k.yaw - g.gYaw);
            float ry = Mathf.Max(k.sy, 0.8f), rp = Mathf.Max(k.sp, 0.015f);
            float ky, kp;
            if (!farkVar)
            {
                fYaw = hYaw; vYaw = ry * ry; ky = 1f;
                Dondur(fYaw, g.gX, g.gZ, out float rx0, out float rz0);
                fX = k.x - rx0; fZ = k.z - rz0; vPoz = rp * rp; kp = 1f;
                dYaw = fYaw; dX = fX; dZ = fZ; farkVar = true;
                Gunluk.Yaz("dev", "ilk konum: " + k.x.ToString("F2") + "," + k.z.ToString("F2") + " yon " + k.yaw.ToString("F0") + " kare " + k.kare + (i >= 0 ? " (eslesti)" : " (kare eslesmedi)"));
            }
            else
            {
                ky = vYaw / (vYaw + ry * ry);
                // robot emin olduğu sürece fark en geç ~1 sn'de kapansın (konum saniyede ~3-5 kez geliyor)
                if (k.sy < 6f) ky = Mathf.Max(ky, 0.5f);
                fYaw += Mathf.DeltaAngle(fYaw, hYaw) * ky; vYaw *= 1f - ky;
                // yön düzeltilince konum farkı yeni yöne göre (robotun o karedeki yeri sabit kalsın)
                Dondur(fYaw, g.gX, g.gZ, out float rx, out float rz);
                kp = vPoz / (vPoz + rp * rp);
                if (k.sp < 0.15f) kp = Mathf.Max(kp, 0.5f);
                fX += (k.x - rx - fX) * kp; fZ += (k.z - rz - fZ) * kp; vPoz *= 1f - kp;
            }
            // yaratıklar ancak robot işareti yeni görmüşken ve konumu kesinken (eski / tahmini konumla yanlış yere konuyordu)
            if (!yaratikKonuldu && k.kaynak == "isaret" && k.isaret_sn >= 0 && k.isaret_sn < 1.5f && k.sy < 5f && (i >= 0 || k.kare < 0))
            { yaratikKonuldu = true; koyBekle = 0.4f; }
            KonumKaydet(k, i >= 0, ky, kp, arH, arId);
        }
        bool yaratikKonuldu; float koyBekle = -1f;
        readonly Dictionary<int, Vector2> isaretYuk = new Dictionary<int, Vector2>();   // id -> (kameradan yükseklik ort., sayı)

        static void Dondur(float yaw, float x, float z, out float rx, out float rz)
        {
            float c = Mathf.Cos(yaw * Mathf.Deg2Rad), s = Mathf.Sin(yaw * Mathf.Deg2Rad);
            rx = x * c + z * s; rz = -x * s + z * c;
        }

        /// <summary>Durum JSON'undaki "gorulen": [[id, mesafe, yon], ...] (JsonUtility iç içe dizi okumuyor).</summary>
        static List<Vector4> Gorulen(string json)
        {
            var l = new List<Vector4>();
            if (json == null) return l;
            int a = json.IndexOf("\"gorulen\"", StringComparison.Ordinal); if (a < 0) return l;
            a = json.IndexOf('[', a); if (a < 0) return l;
            int d = 0, b = a;
            for (; b < json.Length; b++) { if (json[b] == '[') d++; else if (json[b] == ']') { d--; if (d == 0) break; } }
            var ic = json.Substring(a + 1, Math.Max(0, b - a - 1));
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var parca in ic.Split(new[] { ']' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = parca.Trim(' ', ',', '[').Split(',');
                float yk = float.NaN;
                if (t.Length >= 4) float.TryParse(t[3], System.Globalization.NumberStyles.Float, ci, out yk);
                if (t.Length >= 3 && float.TryParse(t[0], System.Globalization.NumberStyles.Float, ci, out float id) &&
                    float.TryParse(t[1], System.Globalization.NumberStyles.Float, ci, out float m) && float.TryParse(t[2], System.Globalization.NumberStyles.Float, ci, out float y))
                    l.Add(new Vector4(id, m, y, yk));
            }
            return l;
        }

        void PozGuncelle()
        {
            if (farkVar)
            {
                float a = 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.15f);
                dYaw += Mathf.DeltaAngle(dYaw, fYaw) * a; dX += (fX - dX) * a; dZ += (fZ - dZ) * a;
                Dondur(dYaw, gX, gZ, out float rx, out float rz);
                PozX = rx + dX; PozZ = rz + dZ; PozYaw = gYaw + dYaw;
                PozVar = true;
            }
            else if (!GorselSaglam && durum?.konum != null && durum.konum.var)
            {
                // görüntü ölçümü yoksa: robotun konumu (gecikmeli; yalnız harita için, yaratık bununla konmaz)
                PozX = durum.konum.x; PozZ = durum.konum.z; PozYaw = durum.konum.yaw;
                PozVar = true;
            }
            if (koyBekle > 0f && farkVar)
            {
                koyBekle -= Time.unscaledDeltaTime;
                if (koyBekle <= 0f) { YaratiklariKoy(); Gunluk.Yaz("dev", "yaratiklar kondu: isaret yeni goruldu, yon belirsizligi kucuk"); }
            }
        }

        // ------------------------------------------------------------------ sanal işaret çerçevesi (ölçüm)
        Canvas olcumKatmani; readonly List<RectTransform[]> cerceveler = new List<RectTransform[]>();

        /// <summary>Evdeki işaretlerin yerine, telefonun şu anki konumuna göre 18 cm'lik çerçeve: dönerken ve giderken gerçek
        /// işaretin üstünde kalıyorsa AR doğru. Çerçeve keskin çizilir (yaratıklar yumuşak), kayma göze batsın diye.</summary>
        void Isaretler()
        {
            if (ev?.isaretler == null || !PozVar) { foreach (var c in cerceveler) foreach (var r in c) r.gameObject.SetActive(false); return; }
            if (olcumKatmani == null)
            {
                var go = new GameObject("Dev olcum", typeof(Canvas)); go.transform.SetParent(transform, false);
                olcumKatmani = go.GetComponent<Canvas>(); olcumKatmani.renderMode = RenderMode.ScreenSpaceOverlay; olcumKatmani.sortingOrder = 105;
            }
            float c0 = Mathf.Cos(PozYaw * Mathf.Deg2Rad), s0 = Mathf.Sin(PozYaw * Mathf.Deg2Rad);
            for (int n = 0; n < ev.isaretler.Length; n++)
            {
                var e = ev.isaretler[n];
                if (n >= cerceveler.Count)
                {
                    var c = new RectTransform[4];
                    for (int j = 0; j < 4; j++)
                    {
                        var r = Rect("Cizgi", olcumKatmani.transform); r.pivot = new Vector2(0f, 0.5f); r.anchorMin = r.anchorMax = Vector2.zero;
                        var im = r.gameObject.AddComponent<Image>(); im.color = new Color(1f, 0.25f, 0.85f, 0.95f); im.raycastTarget = false;
                        c[j] = r;
                    }
                    cerceveler.Add(c);
                }
                // köşeler: ev -> robot (sağ, yukarı, ileri) -> ekran
                float yuk = isaretYuk.TryGetValue(e.id, out var ym) && ym.y >= 3f ? ym.x + KameraYuksek : (e.yukseklik > 0.02f ? e.yukseklik : 0.25f);
                float nx = Mathf.Sin(e.aci), nz = Mathf.Cos(e.aci), ux = nz, uz = -nx;   // işaretin sağı (ona bakana göre sol, önemsiz)
                var p = new Vector2[4]; bool gorunur = true;
                for (int j = 0; j < 4; j++)
                {
                    float su = (j == 0 || j == 3) ? -0.09f : 0.09f, sv = j < 2 ? 0.09f : -0.09f;
                    float wx = e.x + ux * su, wz = e.z + uz * su, wy = yuk + sv;
                    float dx = wx - PozX, dz = wz - PozZ;
                    float sag = dx * c0 - dz * s0, ileri = dx * s0 + dz * c0;
                    var v = yumusakCam.WorldToViewportPoint(kok.TransformPoint(new Vector3(sag, wy - KameraYuksek, ileri)));
                    if (v.z < 0.05f) gorunur = false;
                    p[j] = new Vector2(v.x * Screen.width, v.y * Screen.height);
                }
                var cz = cerceveler[n];
                for (int j = 0; j < 4; j++)
                {
                    cz[j].gameObject.SetActive(gorunur);
                    if (!gorunur) continue;
                    var a = p[j]; var b = p[(j + 1) % 4]; var d = b - a;
                    cz[j].anchoredPosition = a; cz[j].sizeDelta = new Vector2(d.magnitude, 4f);
                    cz[j].localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                }
            }
        }

        void Gorus()
        {
            float th = Mathf.Tan(Hfov * 0.5f * Mathf.Deg2Rad), tv = th * Screen.height / Mathf.Max(1f, Screen.width);
            balik.SetVector("_TanHalf", new Vector4(th, tv, 0, 0));
            balik.SetMatrix("_ViewRot", gorus);
            // görüş: çok geçişli gölgelendirici arayüzde doğrudan kullanılmaz (bütün geçişler çizilebilir), önce dokuya
            int gw = Screen.width, gh = Screen.height;
            if (gorusRt == null || gorusRt.width != gw || gorusRt.height != gh)
            {
                if (gorusRt != null) gorusRt.Release();
                gorusRt = new RenderTexture(gw, gh, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear };
                videoImg.texture = gorusRt;
            }
            if (kareTex != null) Graphics.Blit(kareTex, gorusRt, balik, 0);
            yumusakCam.fieldOfView = 2f * Mathf.Atan(tv) * Mathf.Rad2Deg;
            int w = Mathf.Max(64, Screen.width / 2), h = Mathf.Max(64, Screen.height / 2);
            if (yumusakRt == null || yumusakRt.width != w || yumusakRt.height != h)
            {
                if (yumusakRt != null) { yumusakCam.targetTexture = null; yumusakRt.Release(); }
                yumusakRt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, filterMode = FilterMode.Bilinear };
                yumusakCam.targetTexture = yumusakRt; arImg.texture = yumusakRt;
            }
        }

        // ------------------------------------------------------------------ yaratıklar (yerinde durur)
        void YaratiklariKoy()
        {
            var renk = new[] { new Color(1f, 0.62f, 0.25f), new Color(0.35f, 0.85f, 0.75f), new Color(0.75f, 0.5f, 1f) };
            for (int i = 0; i < 3; i++)
            {
                float a = PozYaw + (i - 1) * 40f, r = 0.9f + 0.2f * i;
                Ekle(new Vector2(PozX + Mathf.Sin(a * Mathf.Deg2Rad) * r, PozZ + Mathf.Cos(a * Mathf.Deg2Rad) * r), renk[i], PozYaw + 180f);
            }
        }

        void Ekle(Vector2 ev, Color renk, float bas)
        {
            var y = new Yaratik { ev = ev, bas = bas, faz = UnityEngine.Random.value * 10f, boy = 0.13f, renk = renk };
            y.t = new GameObject("Yaratik").transform; y.t.SetParent(kok, false);
            y.govde = Kure(y.t, renk, Vector3.zero, Vector3.one * y.boy);
            Golge(y.t, y.boy);
            foreach (float yan in new[] { -1f, 1f })
            {
                var g = Kure(y.govde, Color.white, new Vector3(yan * 0.2f, 0.12f, 0.42f), Vector3.one * 0.22f);
                Kure(g, new Color(0.08f, 0.08f, 0.12f), new Vector3(0, 0, 0.35f), Vector3.one * 0.55f);
            }
            yaratiklar.Add(y);
            Gunluk.Yaz("dev", "yaratik: " + ev.x.ToString("F2") + "," + ev.y.ToString("F2"));
        }

        // fizik modülü yok: CreatePrimitive her kürede "SphereCollider yok" hatası veriyordu (23 Kasım günlüğü); küre elle
        static Mesh kureMesh, golgeMesh; static Material yaratikMat, golgeMat;
        static Mesh KureMesh()
        {
            if (kureMesh != null) return kureMesh;
            const int B = 16, D = 24; var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= B; i++)
                for (int j = 0; j <= D; j++)
                {
                    float a = Mathf.PI * i / B, b = 2f * Mathf.PI * j / D;
                    v.Add(new Vector3(Mathf.Sin(a) * Mathf.Cos(b), Mathf.Cos(a), Mathf.Sin(a) * Mathf.Sin(b)) * 0.5f);
                }
            for (int i = 0; i < B; i++)
                for (int j = 0; j < D; j++)
                {
                    int q = i * (D + 1) + j;
                    t.Add(q); t.Add(q + 1); t.Add(q + D + 1); t.Add(q + 1); t.Add(q + D + 2); t.Add(q + D + 1);
                }
            kureMesh = new Mesh(); kureMesh.SetVertices(v); kureMesh.SetTriangles(t, 0); kureMesh.RecalculateNormals(); kureMesh.RecalculateBounds();
            return kureMesh;
        }
        static Mesh GolgeMesh()
        {
            if (golgeMesh != null) return golgeMesh;
            golgeMesh = new Mesh();
            golgeMesh.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) };
            golgeMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            golgeMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 }; golgeMesh.RecalculateBounds();
            return golgeMesh;
        }

        static Transform Kure(Transform p, Color c, Vector3 yer, Vector3 olcek)
        {
            if (yaratikMat == null) { var sh = Shader.Find("ArTarama/RobotYaratik"); yaratikMat = new Material(sh != null ? sh : Shader.Find("Unlit/Color")); }
            var g = new GameObject("Kure", typeof(MeshFilter), typeof(MeshRenderer));
            g.layer = Katman; g.transform.SetParent(p, false); g.transform.localPosition = yer; g.transform.localScale = olcek;
            g.GetComponent<MeshFilter>().sharedMesh = KureMesh();
            var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = yaratikMat;
            var b = new MaterialPropertyBlock(); c.a = 1f; b.SetColor("_Color", c); r.SetPropertyBlock(b);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return g.transform;
        }

        static void Golge(Transform p, float boy)
        {
            if (golgeMat == null) { var sh = Shader.Find("ArTarama/RobotYaratikGolge"); if (sh == null) return; golgeMat = new Material(sh); }
            var g = new GameObject("Golge", typeof(MeshFilter), typeof(MeshRenderer));
            g.layer = Katman; g.transform.SetParent(p, false); g.transform.localPosition = new Vector3(0, 0.003f, 0); g.transform.localScale = new Vector3(boy * 2.2f, 1f, boy * 2.2f);
            g.GetComponent<MeshFilter>().sharedMesh = GolgeMesh();
            var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = golgeMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        }

        void Yaratiklar()
        {
            bool goster = PozVar;
            float c = Mathf.Cos(PozYaw * Mathf.Deg2Rad), s = Mathf.Sin(PozYaw * Mathf.Deg2Rad);
            foreach (var y in yaratiklar)
            {
                y.t.gameObject.SetActive(goster);
                float dx = y.ev.x - PozX, dz = y.ev.y - PozZ;
                float sag = dx * c - dz * s, ileri = dx * s + dz * c;
                y.t.localPosition = new Vector3(sag, -KameraYuksek, ileri);
                y.t.localRotation = Quaternion.Euler(0, y.bas - PozYaw, 0);
                float hop = Mathf.Abs(Mathf.Sin((Time.time + y.faz) * 3.2f));
                y.govde.localPosition = new Vector3(0, y.boy * (0.55f + 0.12f * hop), 0);
            }
            arImg.enabled = goster;
        }

        void CiftDokunuldu(Vector2 ekran)
        {
            if (!PozVar) { Gunluk.Yaz("dev", "cift dokunma: konum yok"); return; }
            // ekran -> görüş yönü -> robot -> zemin (y = -KameraYuksek)
            float th = Mathf.Tan(Hfov * 0.5f * Mathf.Deg2Rad), tv = th * Screen.height / Mathf.Max(1f, Screen.width);
            var v = new Vector3((ekran.x / Screen.width * 2 - 1) * th, (ekran.y / Screen.height * 2 - 1) * tv, 1f).normalized;
            var d = gorus.MultiplyVector(v);
            if (d.y > -0.03f) return;
            float t = -KameraYuksek / d.y; if (t * new Vector2(d.x, d.z).magnitude > 4f) return;
            float sag = d.x * t, ileri = d.z * t;
            float c = Mathf.Cos(PozYaw * Mathf.Deg2Rad), s = Mathf.Sin(PozYaw * Mathf.Deg2Rad);
            Ekle(new Vector2(PozX + sag * c + ileri * s, PozZ - sag * s + ileri * c), new Color(1f, 0.85f, 0.3f), PozYaw + 180f);
        }

        // ------------------------------------------------------------------ küçük harita ve bilgi
        void Harita()
        {
            const float R = 3f;
            float k = haritaPanel.sizeDelta.x * 0.5f / R;
            harita.parent.localRotation = Quaternion.Euler(0, 0, PozYaw);
            ok.gameObject.SetActive(PozVar);
            if (haritaTex != null)
            {
                harita.sizeDelta = new Vector2(hW, hH) * 0.05f * k;
                harita.pivot = new Vector2((PozX - hox) / (hW * 0.05f), (PozZ - hoz) / (hH * 0.05f));
                harita.anchoredPosition = Vector2.zero;
            }
            int n = 0;
            if (ev?.isaretler != null)
                foreach (var e in ev.isaretler)
                {
                    if (n >= isaretler.Count) { var r = Rect("Isaret", harita.parent); r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.sizeDelta = new Vector2(18, 18); r.gameObject.AddComponent<Image>().raycastTarget = false; isaretler.Add(r); }
                    isaretler[n].anchoredPosition = new Vector2(e.x - PozX, e.z - PozZ) * k; n++;
                }
            // yaratıklar (evde sabit yerleri): ekranda kayıyorlarsa haritadaki yerleriyle karşılaştırılabilir
            for (int i = 0; i < yaratiklar.Count; i++)
            {
                if (i >= haritaYaratik.Count)
                {
                    var r = Rect("Yaratik", harita.parent); r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.sizeDelta = new Vector2(16, 16);
                    var im = r.gameObject.AddComponent<Image>(); im.raycastTarget = false; im.sprite = Daire(); im.color = yaratiklar[i].renk;
                    haritaYaratik.Add(r);
                }
                haritaYaratik[i].gameObject.SetActive(PozVar);
                haritaYaratik[i].anchoredPosition = new Vector2(yaratiklar[i].ev.x - PozX, yaratiklar[i].ev.y - PozZ) * k;
            }
        }

        void Bilgi()
        {
            var sb = new StringBuilder();
            if (ip == null) return;
            var k = durum?.konum;
            sb.Append(PozVar ? "konum: " + PozX.ToString("F2") + ", " + PozZ.ToString("F2") + "  yön " + PozYaw.ToString("F0") + "°" : "konum yok: robotu işarete göster").Append('\n');
            sb.Append("görüntü: dönüş ").Append(iz.PusulaTuttu ? "var (" + iz.PusulaGuven.ToString("F2") + ")" : "YOK")
              .Append(", zemin ").Append(iz.ZeminTuttu ? iz.ZeminNokta + " nokta" : "YOK").Append('\n');
            if (k != null && k.var)
            {
                sb.Append("robot: ").Append(k.kaynak == "hafiza" ? "anahtar kare" : "işaret " + k.isaret).Append(" ").Append(k.isaret_sn.ToString("F0")).Append(" sn önce, ilerleme ").Append(k.hareket == "goruntu" ? "zemin izi" : "motor")
                  .Append(", belirsizlik yön ±").Append(k.sy.ToString("F1")).Append("° yer ±").Append((k.sp * 100f).ToString("F0")).Append(" cm");
                if (k.kilit > 0) sb.Append(", yeniden kilit ").Append(k.kilit);
                sb.Append('\n');
            }
            // ölçüm: AR hatası (işaret görünürken), gecikme, dönüş tahmini
            int n = 0; float enCok = 0f; var son = new List<float>();
            foreach (var q in arHata) if (Time.unscaledTime - q.x < 10f) { n++; son.Add(Mathf.Abs(q.y)); enCok = Mathf.Max(enCok, Mathf.Abs(q.y)); }
            if (n > 0) { son.Sort(); sb.Append("AR hatası (son 10 sn, işarete göre): ortanca ").Append(son[son.Count / 2].ToString("F1")).Append("°, en çok ").Append(enCok.ToString("F1")).Append("°  (").Append(n).Append(" ölçüm)\n"); }
            else sb.Append("AR hatası: ölçüm yok (robot işareti görmüyor)\n");
            sb.Append("gecikme: ").Append(sonGecikmeKare >= 0 ? sonGecikmeKare + " kare" + (sonGecikmeMs >= 0 ? " (" + sonGecikmeMs.ToString("F0") + " ms)" : "") : "?")
              .Append(", telefon belirsizliği yön ±").Append(Mathf.Sqrt(vYaw).ToString("F1")).Append("°, dönüş tahminiyle geçilen ").Append(tahminKare)
              .Append(", reddedilen ").Append(iz.ReddedilenAdim).Append(eslesmeyen > 0 ? ", eşleşmeyen konum " + eslesmeyen : "").Append('\n');
            sb.Append(kayitKlasor != null ? "kayıt: " + Path.GetFileName(kayitKlasor) + " (" + kayitKare + " kare)" : "kayıt yok");
            bilgi.text = sb.ToString();
        }

        // ------------------------------------------------------------------ kayıt
        void KayitAc()
        {
            try
            {
                kayitKlasor = Path.Combine(Application.persistentDataPath, "dev", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(kayitKlasor);
                csv = new StreamWriter(Path.Combine(kayitKlasor, "iz.csv"), false, new UTF8Encoding(false));
                csv.WriteLine("t,kare,jpg,adim_donus,pusula_guven,pusula_tuttu,adim_ileri,adim_sag,zemin_nokta,zemin_tuttu,g_yaw,g_x,g_z,fark_yaw,fark_x,fark_z,poz_x,poz_z,poz_yaw,komut_sol,komut_sag,motor_sol,motor_sag,k_var,k_x,k_z,k_yaw,k_isaret,k_isaret_sn,k_kaynak,k_hareket,k_nokta,"
                              + "robot_kare,robot_zaman_ms,kare_motor_sol,kare_motor_sag,tahmin,tahmin_kullanildi,v_yaw,v_poz,donus_k,k_kare,k_sy,k_sp");
                konumCsv = new StreamWriter(Path.Combine(kayitKlasor, "konum.csv"), false, new UTF8Encoding(false));
                konumCsv.WriteLine("t,k_kare,eslesti,gecikme_kare,gecikme_ms,k_x,k_z,k_yaw,k_sy,k_sp,k_kaynak,k_isaret_sn,k_hata_yon,k_kilit,ky,kp,fark_yaw,fark_x,fark_z,v_yaw,v_poz,ar_hata,ar_isaret,poz_x,poz_z,poz_yaw");
            }
            catch (Exception e) { kayitKlasor = null; Gunluk.Yaz("dev", "kayit acilamadi: " + e.Message); }
        }

        void KareKaydet(byte[] jpeg)
        {
            if (csv == null) return;
            string ad = "k" + kayitKare.ToString("00000") + ".jpg"; kayitKare++;
            string yol = Path.Combine(kayitKlasor, ad);
            ThreadPool.QueueUserWorkItem(_ => { try { File.WriteAllBytes(yol, jpeg); } catch (Exception) { } });
            var k = durum?.konum; var m = durum?.motor;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            csv.WriteLine(string.Join(",", new[] {
                Time.realtimeSinceStartup.ToString("F3", ci), islenenNo.ToString(), ad,
                iz.AdimDonus.ToString("F2", ci), iz.PusulaGuven.ToString("F2", ci), iz.PusulaTuttu ? "1" : "0",
                iz.AdimIleri.ToString("F4", ci), iz.AdimSag.ToString("F4", ci), iz.ZeminNokta.ToString(), iz.ZeminTuttu ? "1" : "0",
                gYaw.ToString("F2", ci), gX.ToString("F3", ci), gZ.ToString("F3", ci), fYaw.ToString("F2", ci), fX.ToString("F3", ci), fZ.ToString("F3", ci),
                PozX.ToString("F3", ci), PozZ.ToString("F3", ci), PozYaw.ToString("F2", ci), komutSol.ToString(), komutSag.ToString(),
                (m != null ? m.sol : 0).ToString(), (m != null ? m.sag : 0).ToString(),
                k != null && k.var ? "1" : "0", (k != null ? k.x : 0).ToString("F3", ci), (k != null ? k.z : 0).ToString("F3", ci), (k != null ? k.yaw : 0).ToString("F1", ci),
                (k != null ? k.isaret : -1).ToString(), (k != null ? k.isaret_sn : -1).ToString("F1", ci), k?.kaynak ?? "", k?.hareket ?? "", (k != null ? k.nokta : 0).ToString(),
                islenenBilgi.kare.ToString(), islenenBilgi.zamanMs.ToString("F1", ci), (islenenBilgi.motorVar ? islenenBilgi.sol : -999).ToString(), (islenenBilgi.motorVar ? islenenBilgi.sag : -999).ToString(),
                sonTahmin.ToString("F2", ci), iz.TahminKullanildi ? "1" : "0", vYaw.ToString("F2", ci), vPoz.ToString("F5", ci), donusK.ToString("F0", ci),
                (k != null ? k.kare : -1).ToString(), (k != null ? k.sy : 0).ToString("F1", ci), (k != null ? k.sp : 0).ToString("F3", ci) }));
            if (Time.unscaledTime - sonCsvFlush > 1f) { sonCsvFlush = Time.unscaledTime; csv.Flush(); konumCsv?.Flush(); }
        }

        void KonumKaydet(Konum k, bool eslesti, float ky, float kp, float arH, int arId)
        {
            if (konumCsv == null) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            konumCsv.WriteLine(string.Join(",", new[] {
                Time.realtimeSinceStartup.ToString("F3", ci), k.kare.ToString(), eslesti ? "1" : "0", sonGecikmeKare.ToString(), sonGecikmeMs.ToString("F0", ci),
                k.x.ToString("F3", ci), k.z.ToString("F3", ci), k.yaw.ToString("F1", ci), k.sy.ToString("F1", ci), k.sp.ToString("F3", ci), k.kaynak ?? "", k.isaret_sn.ToString("F1", ci),
                k.hata != null && k.hata.Length > 0 ? k.hata[0].ToString("F2", ci) : "", k.kilit.ToString(), ky.ToString("F3", ci), kp.ToString("F3", ci),
                fYaw.ToString("F2", ci), fX.ToString("F3", ci), fZ.ToString("F3", ci), vYaw.ToString("F2", ci), vPoz.ToString("F5", ci),
                float.IsNaN(arH) ? "" : arH.ToString("F2", ci), arId.ToString(), PozX.ToString("F3", ci), PozZ.ToString("F3", ci), PozYaw.ToString("F2", ci) }));
        }

        // ------------------------------------------------------------------ kapanış
        public void Kapat()
        {
            Gunluk.Yaz("dev", "kapandi, " + kayitKare + " kare kaydedildi");
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            calis = false;
            komutSol = komutSag = 0;
            try { if (udp != null && ip != null) { var b = Encoding.ASCII.GetBytes("{\"t\":\"d\",\"n\":" + (paketNo + 1) + ",\"sol\":0,\"sag\":0}"); udp.Send(b, b.Length, ip, UdpPort); } } catch (Exception) { }
            try { udp?.Close(); } catch (Exception) { }
            try { csv?.Flush(); csv?.Dispose(); } catch (Exception) { }
            try { konumCsv?.Flush(); konumCsv?.Dispose(); } catch (Exception) { }
            iz?.Birak();
            if (yumusakRt != null) yumusakRt.Release();
            if (gorusRt != null) gorusRt.Release();
            Screen.orientation = oncekiYon == ScreenOrientation.LandscapeLeft ? ScreenOrientation.Portrait : oncekiYon;
            Kapandi?.Invoke();
        }
    }
}
