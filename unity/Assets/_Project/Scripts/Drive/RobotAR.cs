using System.Collections.Generic;
using ARaki.Net;
using ARaki.Robot;
using ARaki.Video;
using UnityEngine;
using UnityEngine.UI;

namespace ARaki.Drive
{
    /// <summary>
    /// Gerçek robotun 360° görüntüsüne evde duran AR nesneleri bindirir. Robotun evdeki konumu köprüden gelir
    /// (durum paketindeki "konum": Jetson'da ArUco işareti + görsel pusula + zemin izi). Nesneler ev koordinatında sabit
    /// durur; her karede robota göre yerleri hesaplanıp kürenin merkezine (kamera) göre çizilir.
    ///
    /// Yön ve yer robotun gönderdiği konumdan gelir. Konum 5 Hz ve ~0,3 sn geç geldiği için aradaki boşluk telefonda
    /// doldurulur: o andan beri ekrandaki görüntü pusulasının (VideoCompass) ölçtüğü dönüş (pusula sıfıra yapışmış ya da
    /// tutmuyorsa motor komutundan dönüş), ileri hareket motor komutundan. Boşluk en çok ~0,5 sn, hata birikmez.
    /// (Eski yol, 25 Kas: görüntü pusulası asıl kaynaktı, robotun yönü yalnız yavaşça düzeltiyordu; jiroskoplu sürüşte
    /// AR yönü %90 26 / en çok 82 derece şaşıyordu. Yeni yol aynı kayıtta %90 12 / en çok 28; tools/konum/ar_yon_olc.py.)
    /// Nesneler yalnız Dev modunda çift dokunulan yere konur ve yalnız Dev modunda görünür; yerlerinde kıpırdamadan durur
    /// (konumun doğru olup olmadığı gözle görülsün). Ayrı kamerayla yarım çözünürlükte çizilip hafif bulanık bindirilir.
    /// </summary>
    public class RobotAR : MonoBehaviour
    {
        const float CameraHeight = 0.17f;       // mercek yerden ~17 cm
        const float TurnRate = 127f;            // derece/sn, (sol-sag) = 510'da (Jetson'un öğrendiği değer, 21 Kasım)
        const float Speed = 0.45f;              // m/sn, (sol+sag) = 510'da (konum.py HIZ_TAM)
        const float PoseLag = 0.3f;             // robotun konumu ekrandaki görüntüden bu kadar eski (sn)

        RealRobotLink link;
        Transform root;
        Material baseMat;
        readonly List<Creature> creatures = new List<Creature>();
        bool havePose;
        float poseX, poseZ, poseYaw;              // gösterilen robot konumu, ev koordinatı
        float lastYaw = float.NaN, lastX, lastZ;  // son gelen konum

        // telefondaki kısa süreli hareket (keyfi çerçeve): dönüş görüntü pusulasından ya da komuttan, ileri komuttan
        struct Odo { public float t, x, z, yaw; public int kare; }   // kare: o an ekrandaki video karesinin numarası (-1 bilinmiyor)
        readonly List<Odo> hist = new List<Odo>();
        float odoX, odoZ, odoYaw, lastCompassYaw = float.NaN;
        Odo baseOdo;                              // son konumun ait olduğu andaki (PoseLag önce) kısa süreli hareket
        Vector2 corrP; float corrY;               // yeni konum gelince sıçramasın: fark yumuşakça söner
        readonly List<Vector2> route = new List<Vector2>();

        EnvironmentLight env;
        VideoCompass compass;
        Material shadowMat;

        // yumuşak bindirme: yaratıklar ayrı kamerayla düşük çözünürlükte çizilip hafif bulanık konur
        const int SoftLayer = 30;
        const float SoftScale = 0.5f;
        Camera softCam; RenderTexture softRt; RawImage softImage;

        /// <summary>Nesneler görünsün mü (yalnız Dev modunda).</summary>
        public bool Visible { get; set; }
        /// <summary>Konumlar arasındaki dönüşün kaynağı: görüntü (telefondaki görsel pusula) ya da motor komutu.</summary>
        public bool VisualYaw { get; private set; }
        public VideoCompass Compass => compass;
        /// <summary>Son konum ekrandaki video karesiyle numarasından eşlendi mi (true); yoksa PoseLag tahmini kullanıldı.</summary>
        public bool FrameSync { get; private set; }
        RobotVideo video;
        /// <summary>Robotun gönderdiği konumlardan gidilen yol (ev koordinatı, 3 cm'de bir).</summary>
        public IReadOnlyList<Vector2> Route => route;
        /// <summary>Gidilen yol her uzadığında artar (küçük harita yeniden çizsin diye).</summary>
        public int RouteVersion { get; private set; }

        class Creature
        {
            public Transform t, body, shadow;
            public MaterialPropertyBlock shadowBlock;
            public Transform[] eyes;
            public Vector2 pos;
            public float heading, phase, size;
        }

        PanoramaViewer view;

        /// <summary>Robotun gösterilen konumu (ev koordinatı, yaw derece: +z'den saat yönü).</summary>
        public bool HavePose => havePose;
        public float PoseX => poseX;
        public float PoseZ => poseZ;
        public float PoseYaw => poseYaw;
        /// <summary>AR nesnelerinin ev koordinatındaki yerleri (küçük harita için).</summary>
        public IEnumerable<Vector2> Objects { get { foreach (var c in creatures) yield return c.pos; } }

        /// <summary>Ekrana çift dokunulan yere (zemine) bir yaratık koyar. Konum bilinmiyorsa ya da yer ufkun üstündeyse / 4 m
        /// ötedeyse koymaz ve neden döner.</summary>
        public string PlaceAt(Vector2 screen)
        {
            if (!havePose) return "Konum henüz bulunmadı: önce işareti göster";
            var ray = view.ViewCamera.ScreenPointToRay(screen);
            Vector3 o = root.InverseTransformPoint(ray.origin), d = root.InverseTransformDirection(ray.direction);
            if (d.y > -0.03f) return "Zemine dokun";
            float t = (-CameraHeight - o.y) / d.y; var p = o + d * t;
            if (new Vector2(p.x, p.z).magnitude > 4f) return "Çok uzak (en çok 4 m)";
            // robot -> ev: sağ = dx*c - dz*s, ileri = dx*s + dz*c'nin tersi
            float c = Mathf.Cos(poseYaw * Mathf.Deg2Rad), sn = Mathf.Sin(poseYaw * Mathf.Deg2Rad);
            var ev = new Vector2(poseX + p.x * c + p.z * sn, poseZ - p.x * sn + p.z * c);
            var colors = new[] { new Color(1f, 0.62f, 0.25f), new Color(0.35f, 0.85f, 0.75f), new Color(0.75f, 0.5f, 1f), new Color(1f, 0.45f, 0.55f) };
            var cr = Build(colors[creatures.Count % colors.Length], Random.Range(0.11f, 0.16f));
            cr.pos = ev;
            cr.heading = poseYaw + 180f; cr.phase = Random.value * 10f;
            creatures.Add(cr);
            return null;
        }

        public void Init(RealRobotLink robotLink, PanoramaViewer viewer)
        {
            link = robotLink; view = viewer;
            root = new GameObject("AR (ev)").transform;
            root.SetParent(viewer.transform, false);
            root.localPosition = viewer.ViewCamera.transform.localPosition;
            baseMat = Resources.Load<Material>("ARToon");
            // ortamın ışığı 360 görüntüden (renk, yön, yaygın ışık); gölge o yöne göre
            env = gameObject.AddComponent<EnvironmentLight>(); env.Init(viewer);
            // dönüş ekrandaki görüntüden (görüntüyle aynı anda); robotun konumu yalnız yavaş mutlak düzeltme
            compass = gameObject.AddComponent<VideoCompass>(); compass.Init(viewer);
            SetupSoft(viewer);
            var ss = Shader.Find("ARaki/BlobShadow");
            if (ss != null) shadowMat = new Material(ss);
            UdpRobotConnection.RequestPose = true;
        }

        void OnDestroy() => UdpRobotConnection.RequestPose = false;

        void Update()
        {
            if (link == null) return;
            var st = link.IsConnected && !link.Demo ? link.Status : null;
            var k = st?.konum;
            float dt = Time.deltaTime, now = Time.time;
            // Editörde robotsuz demo: konum yok, robot ev başlangıcında (ışık / gölge denemesi için)
            if (Application.isEditor && link.Demo) k = new RobotStatus.Konum { var = true, x = 0f, z = 0f, yaw = 0f };

            // 1) kısa süreli hareket. Dönüş: ekrandaki görüntü pusulası; pusula tutmuyorsa ya da robot komutla dönerken
            //    pusula duruyorsa (hızlı dönüşte sıfıra yapışıyor, 25 Kas jiroskop kaydı) motor komutu. İleri: motor komutu.
            float cmdTurn = 0f, cmdFwd = 0f;
            if (st?.motor != null)
            {
                cmdTurn = TurnRate * (st.motor.sol - st.motor.sag) / 510f;
                cmdFwd = Speed * (st.motor.sol + st.motor.sag) / 510f;
            }
            float step = cmdTurn * dt;
            VisualYaw = false;
            if (compass != null && compass.Healthy && !float.IsNaN(lastCompassYaw))
            {
                bool stuck = compass.Rate < 5f && Mathf.Abs(cmdTurn) > 40f;
                if (!stuck) { step = Mathf.DeltaAngle(lastCompassYaw, compass.Yaw); VisualYaw = true; }
            }
            if (compass != null) lastCompassYaw = compass.Yaw;
            odoYaw += step;
            float oy = odoYaw * Mathf.Deg2Rad;
            odoX += Mathf.Sin(oy) * cmdFwd * dt; odoZ += Mathf.Cos(oy) * cmdFwd * dt;
            if (video == null) video = FindFirstObjectByType<RobotVideo>();
            hist.Add(new Odo { t = now, x = odoX, z = odoZ, yaw = odoYaw, kare = video != null ? video.DisplayedKare : -1 });
            while (hist.Count > 2 && hist[1].t < now - 2f) hist.RemoveAt(0);

            // 2) yeni konum geldi: o konumun ait olduğu andaki (PoseLag önce) kısa süreli hareket başlangıç olur
            if (k != null && k.var && (float.IsNaN(lastYaw) || k.yaw != lastYaw || k.x != lastX || k.z != lastZ))
            {
                lastYaw = k.yaw; lastX = k.x; lastZ = k.z;
                var before = new Vector2(poseX, poseZ); float beforeYaw = poseYaw;
                // konum hangi kamera karesinde hesaplandıysa, o kare ekrandayken telefondaki kısa süreli hareket başlangıç
                // olur (gecikme tahmini yok; dönüşteki AR sıçramalarının sebebi 0,3-0,4 sn'lik bu tahmindi, 28 Kas).
                // Eşleşme yoksa (eski köprü, JPEG yolu, görüntü konumdan geride) eski tahmin.
                FrameSync = k.kare >= 0 && FrameMatch(k.kare, out baseOdo);
                if (!FrameSync) baseOdo = At(now - PoseLag);
                Target(out var tp, out var ty);
                corrP = Vector2.zero; corrY = 0f;
                if (havePose)
                {
                    // küçük fark yumuşakça söner; büyük düzeltme (işaret yeniden görüldü) hemen uygulanır
                    corrP = before - tp; corrY = Mathf.DeltaAngle(ty, beforeYaw);
                    if (corrP.magnitude > 0.5f || Mathf.Abs(corrY) > 30f) { corrP = Vector2.zero; corrY = 0f; }
                }
                havePose = true;
                var p = new Vector2(k.x, k.z);
                if (route.Count == 0 || Vector2.Distance(route[route.Count - 1], p) >= 0.03f)
                {
                    route.Add(p); RouteVersion++;
                    if (route.Count > 20000) route.RemoveRange(0, 1000);
                }
            }
            if (havePose)
            {
                Target(out var tp, out var ty);
                corrP *= Mathf.Exp(-dt / 0.25f); corrY *= Mathf.Exp(-dt / 0.15f);
                poseX = tp.x + corrP.x; poseZ = tp.y + corrP.y; poseYaw = ty + corrY;
            }

            bool show = havePose && k != null && k.var && Visible && creatures.Count > 0;
            root.gameObject.SetActive(show);
            if (!show) return;

            float c = Mathf.Cos(poseYaw * Mathf.Deg2Rad), s = Mathf.Sin(poseYaw * Mathf.Deg2Rad);
            foreach (var cr in creatures)
            {
                // ev -> robot: ileri = (sin yaw, cos yaw), sağ = (cos yaw, -sin yaw)
                float dx = cr.pos.x - poseX, dz = cr.pos.y - poseZ;
                float right = dx * c - dz * s, fwd = dx * s + dz * c;
                cr.t.localPosition = new Vector3(right, -CameraHeight, fwd);
                cr.t.localRotation = Quaternion.Euler(0f, cr.heading - poseYaw, 0f);
                Animate(cr);
                Shadow(cr);
            }
        }

        /// <summary>kare numaralı video karesinin ekrana geldiği andaki kısa süreli hareket. Kare atlanmışsa ondan sonra
        /// gelen ilk kare. Geçmiş o kareyi kapsamıyorsa (çok eski ya da henüz gösterilmedi) false.</summary>
        bool FrameMatch(int kare, out Odo o)
        {
            o = default;
            bool eskiVar = false;
            for (int i = 0; i < hist.Count; i++)
            {
                int h = hist[i].kare;
                if (h < 0) continue;
                if (h < kare) { eskiVar = true; continue; }
                if (h > kare + 30) return false;          // köprü yeniden başladı (sayaç sıfırlandı) ya da kopukluk
                if (!eskiVar && h != kare) return false;  // istenen kare geçmişten eski
                o = hist[i]; return true;
            }
            return false;                                 // görüntü konumdan geride: o kare henüz ekranda değil
        }

        /// <summary>t anındaki kısa süreli hareket (geçmişten, doğrusal ara değer).</summary>
        Odo At(float t)
        {
            if (hist.Count == 0) return new Odo { t = t, x = odoX, z = odoZ, yaw = odoYaw };
            for (int i = hist.Count - 1; i > 0; i--)
                if (hist[i - 1].t <= t)
                {
                    var a = hist[i - 1]; var b = hist[i];
                    float u = b.t > a.t ? Mathf.Clamp01((t - a.t) / (b.t - a.t)) : 1f;
                    return new Odo { t = t, x = Mathf.Lerp(a.x, b.x, u), z = Mathf.Lerp(a.z, b.z, u), yaw = Mathf.Lerp(a.yaw, b.yaw, u) };
                }
            return hist[0];
        }

        /// <summary>Son gelen konum + o andan beri telefonda ölçülen kısa süreli hareket (ev koordinatına döndürülmüş).</summary>
        void Target(out Vector2 p, out float yaw)
        {
            float th = (lastYaw - baseOdo.yaw) * Mathf.Deg2Rad, ct = Mathf.Cos(th), sn = Mathf.Sin(th);
            float vx = odoX - baseOdo.x, vz = odoZ - baseOdo.z;
            p = new Vector2(lastX + vx * ct + vz * sn, lastZ - vx * sn + vz * ct);
            yaw = lastYaw + (odoYaw - baseOdo.yaw);
        }

        // ---------------------------------------------------------------- gölge
        // Baskın ışığın tersine düşen yumuşak leke: ışık alçaldıkça uzar, ortam yönlü değilse (her yerden eşit ışık)
        // kısa ve soluk kalır, yalnız temas gölgesi.
        void Shadow(Creature cr)
        {
            if (cr.shadow == null) return;
            float dirness = env != null && env.Ready ? env.Directionality : 0.3f;
            Vector3 ld = env != null ? root.InverseTransformDirection(env.DirWorld) : new Vector3(-0.3f, 0.85f, -0.3f);
            var h = new Vector2(ld.x, ld.z);
            float up = Mathf.Max(ld.y, 0.35f);
            float len = Mathf.Min(h.magnitude / up, 2f) * dirness;           // gölgenin uzama oranı
            Vector2 dirH = h.sqrMagnitude > 1e-6f ? -h.normalized : Vector2.up;
            float s = cr.size;
            var p = cr.t.localPosition;
            cr.shadow.localPosition = new Vector3(p.x + dirH.x * s * 0.55f * len, p.y + 0.003f, p.z + dirH.y * s * 0.55f * len);
            cr.shadow.localRotation = Quaternion.Euler(90f, Mathf.Atan2(dirH.x, dirH.y) * Mathf.Rad2Deg, 0f);
            float hop = cr.body.localPosition.y / (s * 0.55f);                  // zıplayınca gölge küçülür, solar
            cr.shadow.localScale = new Vector3(s * 1.25f, s * 1.25f * (1f + len * 0.9f), 1f) / Mathf.Lerp(1f, hop, 0.5f);
            cr.shadowBlock.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.03f, (0.32f + 0.3f * dirness) / hop));
            cr.shadow.GetComponent<Renderer>().SetPropertyBlock(cr.shadowBlock);
        }

        // ---------------------------------------------------------------- yaratıklar

        void Animate(Creature cr)
        {
            // kıpırdamaz (konumun doğruluğu gözle görülsün); yalnız göz kırpar
            float t = Time.time + cr.phase;
            cr.body.localPosition = new Vector3(0f, cr.size * 0.55f, 0f);
            cr.body.localScale = new Vector3(cr.size, cr.size, cr.size);
            float blink = Mathf.Repeat(t, 3.7f) < 0.12f ? 0.15f : 1f;
            foreach (var e in cr.eyes) e.localScale = new Vector3(0.22f, 0.22f * blink, 0.12f);
        }

        Creature Build(Color color, float size)
        {
            var cr = new Creature { size = size };
            cr.t = new GameObject("Yaratik").transform;
            cr.t.SetParent(root, false);

            if (shadowMat != null)
            {
                var sg = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(sg.GetComponent<Collider>());
                sg.name = "Golge";
                sg.transform.SetParent(root, false);
                var sr = sg.GetComponent<Renderer>(); sr.sharedMaterial = shadowMat;
                sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; sr.receiveShadows = false;
                cr.shadow = sg.transform; cr.shadowBlock = new MaterialPropertyBlock();
            }

            cr.body = Prim(PrimitiveType.Sphere, cr.t, color, 0.35f);
            var eyes = new List<Transform>();
            foreach (float side in new[] { -1f, 1f })
            {
                var e = Prim(PrimitiveType.Sphere, cr.body, Color.white, 0.1f);
                e.localPosition = new Vector3(side * 0.2f, 0.12f, 0.42f);
                var pupil = Prim(PrimitiveType.Sphere, e, new Color(0.08f, 0.08f, 0.12f), 0f);
                pupil.localScale = Vector3.one * 0.55f;
                pupil.localPosition = new Vector3(0f, 0f, 0.35f);
                eyes.Add(e);
            }
            foreach (float side in new[] { -1f, 1f })
            {
                var ear = Prim(PrimitiveType.Capsule, cr.body, color * 0.85f, 0.3f);
                ear.localScale = new Vector3(0.14f, 0.28f, 0.14f);
                ear.localPosition = new Vector3(side * 0.28f, 0.5f, -0.05f);
                ear.localRotation = Quaternion.Euler(0f, 0f, -side * 22f);
            }
            cr.eyes = eyes.ToArray();
            if (softCam != null) SetLayer(cr.t, SoftLayer);
            return cr;
        }

        Transform Prim(PrimitiveType type, Transform parent, Color color, float glow)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            SetColor(go.transform, color, glow);
            return go.transform;
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }

        void SetupSoft(PanoramaViewer viewer)
        {
            var sh = Shader.Find("ARaki/SoftComposite");
            var main = viewer.ViewCamera;
            if (sh == null || main == null) return;
            main.cullingMask &= ~(1 << SoftLayer);
            var go = new GameObject("AR Kamera (yumuşak)");
            go.transform.SetParent(main.transform, false);
            softCam = go.AddComponent<Camera>();
            softCam.cullingMask = 1 << SoftLayer;
            softCam.clearFlags = CameraClearFlags.SolidColor;
            softCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            softCam.nearClipPlane = main.nearClipPlane; softCam.farClipPlane = 30f;
            softCam.allowHDR = false; softCam.allowMSAA = true;
            var cv = new GameObject("AR Bindirme", typeof(RectTransform));
            cv.transform.SetParent(transform, false);
            var canvas = cv.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = -100;
            var img = new GameObject("Doku", typeof(RectTransform)); img.transform.SetParent(cv.transform, false);
            var rtf = (RectTransform)img.transform; rtf.anchorMin = Vector2.zero; rtf.anchorMax = Vector2.one; rtf.offsetMin = rtf.offsetMax = Vector2.zero;
            softImage = img.AddComponent<RawImage>(); softImage.raycastTarget = false;
            softImage.material = new Material(sh);
            softImage.material.SetFloat("_Soft", 0.9f);
        }

        void LateUpdate()
        {
            if (softCam == null) return;
            var main = view.ViewCamera;
            softCam.fieldOfView = main.fieldOfView;
            int w = Mathf.Max(64, Mathf.RoundToInt(Screen.width * SoftScale)), h = Mathf.Max(64, Mathf.RoundToInt(Screen.height * SoftScale));
            if (softRt == null || softRt.width != w || softRt.height != h)
            {
                if (softRt != null) { softCam.targetTexture = null; softRt.Release(); }
                softRt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, filterMode = FilterMode.Bilinear };
                softCam.targetTexture = softRt; softImage.texture = softRt;
            }
            bool on = root != null && root.gameObject.activeInHierarchy;
            softCam.enabled = on; softImage.enabled = on;
        }

        void SetColor(Transform t, Color color, float glow)
        {
            color.a = 1f;                       // yumuşak bindirmede alfa = örtme; 0,85 kulak yarı saydam kalıyordu
            var r = t.GetComponent<Renderer>();
            if (baseMat != null) r.sharedMaterial = baseMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var b = new MaterialPropertyBlock();
            b.SetColor("_BaseColor", color);
            b.SetColor("_ShadeColor", Color.Lerp(color, new Color(0.2f, 0.2f, 0.35f), 0.55f));
            b.SetColor("_EmissionColor", color * glow);
            b.SetFloat("_Glow", glow);
            r.SetPropertyBlock(b);
        }
    }
}
