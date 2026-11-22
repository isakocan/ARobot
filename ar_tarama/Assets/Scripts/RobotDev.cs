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
    /// Robotun konumu: dönüş ve ileri/yan hareket telefonda, ekrandaki karenin kendisinden ölçülür (RobotGorselIz); robotun
    /// kendi konumu (işaret + zemin izi, UDP durum cevabında) yalnızca robot dururken farkı yavaşça kapatır. Böylece nesne
    /// görüntüyle aynı anda hareket eder, geç gelen konum yüzünden "önce uzaklaşıp sonra yakınlaşma" ve sıçrama olmaz.
    /// Yaratıklar yerinde durur (dolaşmaz), yarım çözünürlükte çizilip hafif bulanık bindirilir.
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
        string sonDurumJson; int durumNo, islenenDurum;
        int paketNo; float sonPaket;
        int komutSol, komutSag; float sonHareketKomutu = -10f;

        [Serializable] class Konum { public bool var; public float x, z, yaw; public int isaret; public float isaret_sn = -1; public string kaynak = "", hareket = ""; public int nokta; public float olcek; }
        [Serializable] class Motor { public int sol, sag; }
        [Serializable] class Durum { public Konum konum; public Motor motor; }
        Durum durum;

        // ---- görüntü ve iz
        Camera temizCam, yumusakCam; RenderTexture yumusakRt;
        RawImage videoImg, arImg; Material balik, yumusakMat;
        Texture2D kareTex; RobotGorselIz iz; RenderTexture gorusRt;
        Matrix4x4 gorus;

        // ---- konum birleştirme (ev koordinatı; yaw derece, +z'den sağa)
        float gYaw, gX, gZ;                  // görüntüden: toplam
        bool farkVar; float fYaw, fX, fZ;     // ev = döndür(fYaw)(görüntü) + (fX, fZ)
        public bool PozVar { get; private set; }
        public float PozX { get; private set; }
        public float PozZ { get; private set; }
        public float PozYaw { get; private set; }
        float sonKonumX = float.NaN, sonKonumZ, sonKonumYaw;
        float sonGorselZaman = -10f;

        // ---- yaratıklar
        Transform kok;
        class Yaratik { public Transform t; public Vector2 ev; public float bas, faz, boy; public Transform govde; }
        readonly List<Yaratik> yaratiklar = new List<Yaratik>();

        // ---- arayüz
        Canvas arayuz; Text bilgi; RawImage haritaImg; RectTransform haritaPanel, harita, ok; Texture2D haritaTex;
        float hox, hoz; int hW, hH; float sonHarita = -100f;
        readonly List<RectTransform> isaretler = new List<RectTransform>();
        [Serializable] class ZeminJson { public int[] acik; }
        [Serializable] class Isaret { public int id; public float x, z, aci; }
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
            StartCoroutine(Baglan());
        }

        // ------------------------------------------------------------------ kamera takılışı (robot uygulamasıyla aynı)
        void Mount(float yon, float egim, float yatma)
        {
            balik.SetMatrix("_Mount", Rz(yatma) * Rx(egim) * Ry(yon));
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
        IEnumerator Baglan()
        {
            bilgi.text = "Robot aranıyor…";
            var t = RobotaGonder.RobotuBul(s => { });
            while (!t.IsCompleted) yield return null;
            ip = t.Result;
            if (ip == null) { bilgi.text = "Robot bulunamadı. Robot açık ve telefonla aynı ağda mı?"; Gunluk.Yaz("dev", "robot bulunamadi"); yield break; }
            Gunluk.Yaz("dev", "robot: " + ip);
            using (var r = UnityWebRequest.Get("http://" + ip + ":" + HttpPort + "/api/kamera-ayari"))
            {
                r.timeout = 4; yield return r.SendWebRequest();
                if (r.result == UnityWebRequest.Result.Success && r.downloadHandler.text.Length > 3)
                {
                    var k = JsonUtility.FromJson<KameraAyari>(r.downloadHandler.text); Mount(k.yon, k.egim, k.yatma);
                    Gunluk.Yaz("dev", "kamera ayari: " + r.downloadHandler.text);
                }
            }
            calis = true;
            udp = new UdpClient(0);
            alici = new Thread(UdpAl) { IsBackground = true }; alici.Start();
            akis = new Thread(MjpegOku) { IsBackground = true }; akis.Start();
            StartCoroutine(HaritaGetir());
        }
        [Serializable] class KameraAyari { public float yon = 0f, egim = 0f, yatma = 90f; }

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
                            int uzunluk = -1; string satir;
                            while ((satir = Satir(s)) != null)
                            {
                                if (satir.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) int.TryParse(satir.Substring(15).Trim(), out uzunluk);
                                else if (satir.Length == 0 && uzunluk > 0) break;
                            }
                            if (satir == null) break;
                            var j = new byte[uzunluk]; int o = 0;
                            while (o < uzunluk) { int n = s.Read(j, o, uzunluk - o); if (n <= 0) throw new IOException("akis bitti"); o += n; }
                            lock (kilit) { sonJpeg = j; jpegNo++; }
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

        IEnumerator HaritaGetir()
        {
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
                int x0 = int.MaxValue, x1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue; var a = z.acik;
                for (int i = 0; i + 1 < a.Length; i += 2) { x0 = Math.Min(x0, a[i]); x1 = Math.Max(x1, a[i]); z0 = Math.Min(z0, a[i + 1]); z1 = Math.Max(z1, a[i + 1]); }
                x0 -= 2; z0 -= 2; hW = x1 - x0 + 3; hH = z1 - z0 + 3;
                if (hW * hH > 4000000) yield break;
                var px = new Color32[hW * hH];
                for (int i = 0; i + 1 < a.Length; i += 2) px[(a[i + 1] - z0) * hW + (a[i] - x0)] = new Color32(67, 217, 190, 210);
                haritaTex = new Texture2D(hW, hH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
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
            bool yeniKonum = false;
            if (dj != null)
            {
                try { durum = JsonUtility.FromJson<Durum>(dj); } catch (Exception) { }
                var k = durum?.konum;
                if (k != null && k.var && (k.x != sonKonumX || k.z != sonKonumZ || k.yaw != sonKonumYaw)) { sonKonumX = k.x; sonKonumZ = k.z; sonKonumYaw = k.yaw; yeniKonum = true; }
            }

            // yeni kare: göster ve ölç
            byte[] jpeg = null; int no = 0;
            lock (kilit) { if (jpegNo != islenenNo) { islenenNo = jpegNo; jpeg = sonJpeg; no = jpegNo; } }
            if (jpeg != null)
            {
                if (kareTex == null) kareTex = new Texture2D(2, 2, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                if (kareTex.LoadImage(jpeg))
                {
                    iz.Kare(kareTex);
                    GorselAdim();
                    KareKaydet(jpeg);
                }
            }
            if (yeniKonum) KonumGeldi(durum.konum);
            PozGuncelle();
            Gorus();
            Yaratiklar();
            Harita();
            Bilgi();
        }

        void GorselAdim()
        {
            if (iz.PusulaTuttu) gYaw += iz.AdimDonus;
            if (iz.ZeminTuttu)
            {
                float s = Mathf.Sin(gYaw * Mathf.Deg2Rad), c = Mathf.Cos(gYaw * Mathf.Deg2Rad);
                gX += s * iz.AdimIleri + c * iz.AdimSag; gZ += c * iz.AdimIleri - s * iz.AdimSag;
            }
            if (iz.PusulaTuttu) sonGorselZaman = Time.unscaledTime;
        }

        bool GorselSaglam => Time.unscaledTime - sonGorselZaman < 0.6f;

        /// <summary>Robotun konumu geldi: görüntü ile ev arasındaki farkı ilk seferde koyar, sonra yalnız robot dururken
        /// (konum ~0,3 sn geç geldiği için hareket ederken karşılaştırma yanlış olur) yavaşça kapatır.</summary>
        void KonumGeldi(Konum k)
        {
            if (!GorselSaglam) return;
            float hYaw = Mathf.DeltaAngle(0f, k.yaw - gYaw);
            float c = Mathf.Cos(hYaw * Mathf.Deg2Rad), s = Mathf.Sin(hYaw * Mathf.Deg2Rad);
            // ev = R(hYaw) * görüntü + fark  (R: +z'den sağa dönüş)
            float rx = gX * c + gZ * s, rz = -gX * s + gZ * c;
            float hX = k.x - rx, hZ = k.z - rz;
            bool duruyor = Time.unscaledTime - sonHareketKomutu > 1.0f && (durum?.motor == null || (durum.motor.sol == 0 && durum.motor.sag == 0));
            if (!farkVar) { fYaw = hYaw; fX = hX; fZ = hZ; farkVar = true; Gunluk.Yaz("dev", "ilk konum: " + k.x.ToString("F2") + "," + k.z.ToString("F2") + " yon " + k.yaw.ToString("F0")); return; }
            if (!duruyor) return;
            fYaw += Mathf.DeltaAngle(fYaw, hYaw) * 0.15f;
            // yön farkı değişince konum farkı da yeni yöne göre hesaplanır
            c = Mathf.Cos(fYaw * Mathf.Deg2Rad); s = Mathf.Sin(fYaw * Mathf.Deg2Rad);
            rx = gX * c + gZ * s; rz = -gX * s + gZ * c;
            fX += (k.x - rx - fX) * 0.15f; fZ += (k.z - rz - fZ) * 0.15f;
        }

        void PozGuncelle()
        {
            if (farkVar)
            {
                float c = Mathf.Cos(fYaw * Mathf.Deg2Rad), s = Mathf.Sin(fYaw * Mathf.Deg2Rad);
                PozX = gX * c + gZ * s + fX; PozZ = -gX * s + gZ * c + fZ; PozYaw = gYaw + fYaw;
                if (!PozVar) { PozVar = true; YaratiklariKoy(); }
            }
            else if (!GorselSaglam && durum?.konum != null && durum.konum.var)
            {
                // görüntü ölçümü yoksa: robotun konumu (gecikmeli)
                PozX = durum.konum.x; PozZ = durum.konum.z; PozYaw = durum.konum.yaw;
                if (!PozVar) { PozVar = true; YaratiklariKoy(); }
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
            var y = new Yaratik { ev = ev, bas = bas, faz = UnityEngine.Random.value * 10f, boy = 0.13f };
            y.t = new GameObject("Yaratik").transform; y.t.SetParent(kok, false);
            y.govde = Kure(y.t, renk, Vector3.zero, Vector3.one * y.boy);
            foreach (float yan in new[] { -1f, 1f })
            {
                var g = Kure(y.govde, Color.white, new Vector3(yan * 0.2f, 0.12f, 0.42f), Vector3.one * 0.22f);
                Kure(g, new Color(0.08f, 0.08f, 0.12f), new Vector3(0, 0, 0.35f), Vector3.one * 0.55f);
            }
            yaratiklar.Add(y);
            Gunluk.Yaz("dev", "yaratik: " + ev.x.ToString("F2") + "," + ev.y.ToString("F2"));
        }

        static Transform Kure(Transform p, Color c, Vector3 yer, Vector3 olcek)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(g.GetComponent<Collider>());
            g.layer = Katman; g.transform.SetParent(p, false); g.transform.localPosition = yer; g.transform.localScale = olcek;
            var r = g.GetComponent<Renderer>(); var b = new MaterialPropertyBlock(); c.a = 1f; b.SetColor("_Color", c); r.SetPropertyBlock(b);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return g.transform;
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
                sb.Append("robot: ").Append(k.kaynak == "hafiza" ? "anahtar kare" : "işaret " + k.isaret).Append(" ").Append(k.isaret_sn.ToString("F0")).Append(" sn önce, ilerleme ").Append(k.hareket == "goruntu" ? "zemin izi" : "motor").Append('\n');
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
                csv.WriteLine("t,kare,jpg,adim_donus,pusula_guven,pusula_tuttu,adim_ileri,adim_sag,zemin_nokta,zemin_tuttu,g_yaw,g_x,g_z,fark_yaw,fark_x,fark_z,poz_x,poz_z,poz_yaw,komut_sol,komut_sag,motor_sol,motor_sag,k_var,k_x,k_z,k_yaw,k_isaret,k_isaret_sn,k_kaynak,k_hareket,k_nokta");
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
                (k != null ? k.isaret : -1).ToString(), (k != null ? k.isaret_sn : -1).ToString("F1", ci), k?.kaynak ?? "", k?.hareket ?? "", (k != null ? k.nokta : 0).ToString() }));
            if (Time.unscaledTime - sonCsvFlush > 1f) { sonCsvFlush = Time.unscaledTime; csv.Flush(); }
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
            iz?.Birak();
            if (yumusakRt != null) yumusakRt.Release();
            if (gorusRt != null) gorusRt.Release();
            Screen.orientation = oncekiYon == ScreenOrientation.LandscapeLeft ? ScreenOrientation.Portrait : oncekiYon;
            Kapandi?.Invoke();
        }
    }
}
