using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace ARaki.Drive
{
    /// <summary>
    /// Dev modunda sol üstte küçük harita: tarama uygulamasının robota gönderdiği ev haritası (köprü GET /api/ev/zemin,
    /// 5 cm açık zemin hücreleri, ev koordinatı: ArUco işaretinin önündeki zemin, +z işaretin baktığı yön) + robotun
    /// canlı konumu (ok) + AR yaratıkları (noktalar) + işaretler (kare). Harita robotun bakış yönüne göre döner, robot ortada.
    /// Konum bulmanın içi de görünür: robotun anahtar kareleri (mavi), son izi (sarı), sağda zemin izinin kuşbakışı
    /// görüntüsü ve izlenen noktaları (köprü /api/konum/zemin.jpg; yeşil = ortak harekete uyan), altta kaynaklar.
    /// </summary>
    public class DevMap : MonoBehaviour
    {
        const float Cell = 0.05f, Radius = 3.5f;     // ekranda robotun çevresi 3,5 m

        DriveController drive;
        Canvas canvas;
        RectTransform panel, content, map, arrow;
        RawImage mapImage;
        Text info;
        Texture2D tex;
        float ox, oz; int W, H;
        readonly System.Collections.Generic.List<RectTransform> dots = new System.Collections.Generic.List<RectTransform>();
        readonly System.Collections.Generic.List<RectTransform> markers = new System.Collections.Generic.List<RectTransform>();
        float nextFetch, nextMem, nextFloorCam;
        Color32[] basePx;
        RawImage floorCam; Texture2D floorCamTex;
        float[][] memKeys = new float[0][], memTrail = new float[0][];
        string lastHost, mapText = "harita yok";

        [System.Serializable] class ZeminJson { public int[] acik; }
        [System.Serializable] class Isaret { public int id; public float x, z, aci; }
        [System.Serializable] class EvJson { public string ad; public Isaret[] isaretler; }
        EvJson ev;

        public void Init(DriveController d)
        {
            drive = d;
            var go = new GameObject("Dev Harita", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 40;
            var sc = go.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 1f;

            panel = NewRect("Panel", go.transform);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0, 1);
            panel.anchoredPosition = new Vector2(24, -130); panel.sizeDelta = new Vector2(400, 400);
            var bg = panel.gameObject.AddComponent<Image>(); bg.color = new Color(0.04f, 0.045f, 0.055f, 0.82f); bg.raycastTarget = false;
            panel.gameObject.AddComponent<RectMask2D>();

            content = NewRect("Icerik", panel); content.anchorMin = content.anchorMax = new Vector2(0.5f, 0.5f); content.sizeDelta = Vector2.zero;
            map = NewRect("Harita", content); map.anchorMin = map.anchorMax = new Vector2(0.5f, 0.5f);
            mapImage = map.gameObject.AddComponent<RawImage>(); mapImage.raycastTarget = false; mapImage.enabled = false;

            arrow = NewRect("Robot", panel); arrow.anchorMin = arrow.anchorMax = new Vector2(0.5f, 0.5f); arrow.sizeDelta = new Vector2(26, 34);
            var ai = arrow.gameObject.AddComponent<Image>(); ai.sprite = Triangle(); ai.color = new Color(1f, 0.62f, 0.25f); ai.raycastTarget = false;

            var t = NewRect("Bilgi", panel); t.anchorMin = new Vector2(0, 0); t.anchorMax = new Vector2(1, 0); t.pivot = new Vector2(0.5f, 0);
            t.anchoredPosition = new Vector2(0, 6); t.sizeDelta = new Vector2(-16, 30);
            info = t.gameObject.AddComponent<Text>(); info.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            info.fontSize = 20; info.color = new Color(0.85f, 0.88f, 0.92f); info.alignment = TextAnchor.MiddleLeft; info.raycastTarget = false;
            // sağda zemin izi görüntüsü
            var fc = NewRect("Zemin izi", go.transform);
            fc.anchorMin = fc.anchorMax = fc.pivot = new Vector2(0, 1);
            fc.anchoredPosition = new Vector2(24 + 400 + 12, -130); fc.sizeDelta = new Vector2(220, 220);
            floorCam = fc.gameObject.AddComponent<RawImage>(); floorCam.raycastTarget = false; floorCam.enabled = false;
            go.SetActive(false);
        }

        public void Show(bool on)
        {
            if (canvas != null) canvas.gameObject.SetActive(on);
            if (on) nextFetch = 0f;
        }

        static RectTransform NewRect(string n, Transform p)
        {
            var g = new GameObject(n, typeof(RectTransform)); g.transform.SetParent(p, false); return (RectTransform)g.transform;
        }

        static Sprite tri;
        static Sprite Triangle()
        {
            if (tri != null) return tri;
            const int S = 64; var t = new Texture2D(S, S, TextureFormat.RGBA32, false);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float half = (y / (float)S) * 0.5f;   // tepe yukarıda
                    bool inside = Mathf.Abs(x / (float)S - 0.5f) < 0.5f - half;
                    t.SetPixel(x, y, inside ? Color.white : Color.clear);
                }
            t.Apply();
            return tri = Sprite.Create(t, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        }

        void Update()
        {
            if (canvas == null || !canvas.gameObject.activeSelf) return;
            var link = drive.Link;
            string host = link != null && link.IsConnected && !link.Demo ? link.Host : null;
            if (host != null && (host != lastHost || Time.unscaledTime > nextFetch))
            {
                lastHost = host; nextFetch = Time.unscaledTime + 30f;
                StartCoroutine(Fetch(host));
            }
            if (host != null && Time.unscaledTime > nextMem) { nextMem = Time.unscaledTime + 3f; StartCoroutine(FetchMem(host)); }
            if (host != null && Time.unscaledTime > nextFloorCam) { nextFloorCam = Time.unscaledTime + 0.5f; StartCoroutine(FetchFloorCam(host)); }
            Layout();
        }

        IEnumerator Fetch(string host)
        {
            using (var r = UnityWebRequest.Get("http://" + host + ":8080/api/ev"))
            {
                r.timeout = 6; yield return r.SendWebRequest();
                if (r.result == UnityWebRequest.Result.Success) { try { ev = JsonUtility.FromJson<EvJson>(r.downloadHandler.text); } catch { } }
            }
            using (var r = UnityWebRequest.Get("http://" + host + ":8080/api/ev/zemin"))
            {
                r.timeout = 8; yield return r.SendWebRequest();
                if (r.result != UnityWebRequest.Result.Success) { mapText = "harita yok (tarama uygulamasından Robota gönder)"; yield break; }
                ZeminJson z = null;
                try { z = JsonUtility.FromJson<ZeminJson>(r.downloadHandler.text); } catch { }
                if (z?.acik == null || z.acik.Length < 2) { mapText = "harita boş"; yield break; }
                Build(z.acik);
                mapText = (ev != null && !string.IsNullOrEmpty(ev.ad) ? ev.ad : "ev") + "  ·  " + (z.acik.Length / 2 * Cell * Cell).ToString("F1") + " m²";
            }
        }

        IEnumerator FetchMem(string host)
        {
            using (var r = UnityWebRequest.Get("http://" + host + ":8080/api/konum/hafiza"))
            {
                r.timeout = 4; yield return r.SendWebRequest();
                if (r.result != UnityWebRequest.Result.Success) yield break;
                // {"kareler": [[x, z, yaw], ...], "iz": [[x, z], ...]}: JsonUtility iç içe diziyi okumaz, elle ayrıştır
                memKeys = Pairs(r.downloadHandler.text, "kareler");
                memTrail = Pairs(r.downloadHandler.text, "iz");
                Compose();
            }
        }

        static float[][] Pairs(string j, string key)
        {
            var list = new System.Collections.Generic.List<float[]>();
            int i = j.IndexOf('"' + key + '"', System.StringComparison.Ordinal); if (i < 0) return list.ToArray();
            i = j.IndexOf('[', i); if (i < 0) return list.ToArray();
            int depth = 0, start = -1;
            for (int k = i; k < j.Length; k++)
            {
                char ch = j[k];
                if (ch == '[') { depth++; if (depth == 2) start = k + 1; }
                else if (ch == ']')
                {
                    if (depth == 2 && start >= 0)
                    {
                        var parts = j.Substring(start, k - start).Split(',');
                        var v = new float[parts.Length];
                        for (int q = 0; q < parts.Length; q++) float.TryParse(parts[q], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[q]);
                        list.Add(v);
                    }
                    depth--; if (depth == 0) break;
                }
            }
            return list.ToArray();
        }

        IEnumerator FetchFloorCam(string host)
        {
            using (var r = UnityWebRequest.Get("http://" + host + ":8080/api/konum/zemin.jpg"))
            {
                r.timeout = 3; yield return r.SendWebRequest();
                if (r.result != UnityWebRequest.Result.Success) { floorCam.enabled = false; yield break; }
                if (floorCamTex == null) floorCamTex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (floorCamTex.LoadImage(r.downloadHandler.data)) { floorCam.texture = floorCamTex; floorCam.enabled = true; }
            }
        }

        /// <summary>Zemin + anahtar kareler (mavi) + robotun izi (sarı) aynı dokuda.</summary>
        void Compose()
        {
            if (basePx == null || tex == null) return;
            var px = (Color32[])basePx.Clone();
            void Put(float x, float z, Color32 c)
            {
                int ix = Mathf.FloorToInt((x - ox) / Cell), iz = Mathf.FloorToInt((z - oz) / Cell);
                if (ix >= 0 && iz >= 0 && ix < W && iz < H) px[iz * W + ix] = c;
            }
            foreach (var t in memTrail) if (t.Length >= 2) Put(t[0], t[1], new Color32(255, 214, 90, 255));
            foreach (var k in memKeys) if (k.Length >= 2) Put(k[0], k[1], new Color32(110, 170, 255, 255));
            tex.SetPixels32(px); tex.Apply(false);
        }

        void Build(int[] a)
        {
            int x0 = int.MaxValue, x1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue;
            for (int i = 0; i + 1 < a.Length; i += 2) { x0 = Mathf.Min(x0, a[i]); x1 = Mathf.Max(x1, a[i]); z0 = Mathf.Min(z0, a[i + 1]); z1 = Mathf.Max(z1, a[i + 1]); }
            x0 -= 2; z0 -= 2; W = x1 - x0 + 3; H = z1 - z0 + 3;
            if (W * H > 4000000) return;
            var px = new Color32[W * H];
            var c = new Color32(67, 217, 190, 210);
            for (int i = 0; i + 1 < a.Length; i += 2) px[(a[i + 1] - z0) * W + (a[i] - x0)] = c;
            if (tex == null || tex.width != W || tex.height != H)
            {
                if (tex != null) Destroy(tex);
                tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            }
            tex.SetPixels32(px); tex.Apply(false);
            mapImage.texture = tex; mapImage.enabled = true;
            ox = x0 * Cell; oz = z0 * Cell;
            basePx = px;
            Compose();
        }

        void Layout()
        {
            var ar = drive.Ar;
            bool pose = ar != null && ar.HavePose;
            float px = pose ? ar.PoseX : 0f, pz = pose ? ar.PoseZ : 0f, yaw = pose ? ar.PoseYaw : 0f;
            float k = panel.sizeDelta.x * 0.5f / Radius;     // piksel / metre
            // robot ortada, bakış yönü yukarı: içerik -yaw kadar değil +yaw kadar döner (UI saat yönünün tersi pozitif)
            content.localRotation = Quaternion.Euler(0, 0, yaw);
            arrow.gameObject.SetActive(pose);
            if (tex != null && mapImage.enabled)
            {
                map.sizeDelta = new Vector2(W * Cell, H * Cell) * k;
                map.pivot = new Vector2((px - ox) / (W * Cell), (pz - oz) / (H * Cell));
                map.anchoredPosition = Vector2.zero;
            }
            // yaratıklar ve konulan nesneler
            int n = 0;
            if (ar != null)
                foreach (var p in ar.Objects)
                {
                    if (n >= dots.Count) dots.Add(Dot(new Color(0.75f, 0.5f, 1f), 16));
                    dots[n].anchoredPosition = new Vector2(p.x - px, p.y - pz) * k; dots[n].gameObject.SetActive(true); n++;
                }
            for (int i = n; i < dots.Count; i++) dots[i].gameObject.SetActive(false);
            int m = 0;
            if (ev?.isaretler != null)
                foreach (var e in ev.isaretler)
                {
                    if (m >= markers.Count) markers.Add(Dot(Color.white, 22));
                    markers[m].anchoredPosition = new Vector2(e.x - px, e.z - pz) * k; markers[m].gameObject.SetActive(true); m++;
                }
            for (int i = m; i < markers.Count; i++) markers[i].gameObject.SetActive(false);

            string konum = "konum yok";
            var st = drive.Link != null ? drive.Link.Status : null;
            if (pose && st?.konum != null)
            {
                var kk = st.konum;
                konum = "düzeltme: " + (kk.kaynak == "hafiza" ? "anahtar kare" : "işaret " + kk.isaret) + " (" + kk.isaret_sn.ToString("F0") + " sn önce)"
                    + "\nilerleme: " + (kk.hareket == "goruntu" ? "zemin izi, " + kk.nokta + " nokta, ölçek " + kk.olcek.ToString("F2") : "motor komutu")
                    + "\ndönüş (ekranda): " + (ar.VisualYaw ? "görüntü, güven " + ar.Compass.Confidence.ToString("F2") : "robotun konumu")
                    + "  ·  anahtar kare " + kk.hafiza;
            }
            else if (drive.Link != null && drive.Link.Demo) konum = "demo: konum yok";
            info.text = konum + "\n" + mapText;
            info.rectTransform.sizeDelta = new Vector2(-16, 110);
        }

        RectTransform Dot(Color c, float size)
        {
            var r = NewRect("Nokta", content); r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.sizeDelta = new Vector2(size, size);
            var im = r.gameObject.AddComponent<Image>(); im.color = c; im.raycastTarget = false;
            return r;
        }
    }
}
