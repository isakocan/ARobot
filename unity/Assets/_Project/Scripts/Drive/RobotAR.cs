using System.Collections.Generic;
using ARaki.Net;
using ARaki.Robot;
using ARaki.Video;
using UnityEngine;

namespace ARaki.Drive
{
    /// <summary>
    /// Gerçek robotun 360° görüntüsüne evde duran AR yaratıkları bindirir. Robotun evdeki konumu köprüden gelir
    /// (durum paketindeki "konum": Jetson'da ArUco işareti + görsel pusula + anahtar kareler). Yaratıklar ev koordinatında
    /// durur; her karede robota göre yerleri hesaplanıp kürenin merkezine (kamera) göre çizilir. Küre robota göre sabit
    /// olduğundan robot dönünce görüntüyle birlikte dönerler.
    ///
    /// Konum 5 Hz ve ~0,2 sn gecikmeli gelir: aradaki dönüş motor komutundan tahmin edilir, gelen konuma yumuşakça geçilir.
    /// Yaratıklar konum ilk bulunduğunda robotun çevresine (0,7-1,3 m) konur, yuvalarının çevresinde yavaşça dolaşır.
    /// </summary>
    public class RobotAR : MonoBehaviour
    {
        const float CameraHeight = 0.17f;       // mercek yerden ~17 cm
        const float TurnRate = 127f;            // derece/sn, (sol-sag) = 510'da (Jetson'un öğrendiği değer, 21 Kasım)
        const int Count = 3;

        RealRobotLink link;
        Transform root;
        Material baseMat;
        readonly List<Creature> creatures = new List<Creature>();
        bool havePose;
        float poseX, poseZ, poseYaw;              // gösterilen (yumuşatılmış) robot konumu, ev koordinatı
        float lastYaw = float.NaN, lastX, lastZ;  // son gelen konum
        float predicted;                          // son konumdan beri komuttan tahmin edilen dönüş (derece)

        EnvironmentLight env;
        Material shadowMat;

        class Creature
        {
            public Transform t, body, shadow;
            public MaterialPropertyBlock shadowBlock;
            public Transform[] eyes;
            public Vector2 home, pos, target;
            public float heading, phase, nextTarget, size, wander = 0.45f;
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
            cr.home = cr.pos = cr.target = ev;
            cr.heading = poseYaw + 180f; cr.phase = Random.value * 10f; cr.wander = 0.2f;
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
            float dt = Time.deltaTime;
            // Editörde robotsuz demo: konum yok, yaratıklar robotun önünde sabit (ışık / gölge denemesi için)
            if (Application.isEditor && link.Demo)
            {
                if (!havePose) { havePose = true; poseX = poseZ = poseYaw = 0f; Spawn(); }
                k = new RobotStatus.Konum { var = true, x = 0f, z = 0f, yaw = 0f };
                lastYaw = 0f; lastX = 0f; lastZ = 0f; predicted = 0f;
            }

            // komuttan dönüş tahmini: konum 5 Hz geldiği için arada görüntüyle birlikte dönsün
            if (st?.motor != null)
                predicted += TurnRate * (st.motor.sol - st.motor.sag) / 510f * dt;

            if (k != null && k.var)
            {
                if (float.IsNaN(lastYaw) || k.yaw != lastYaw || k.x != lastX || k.z != lastZ)
                {
                    lastYaw = k.yaw; lastX = k.x; lastZ = k.z;
                    predicted = 0f;                     // yeni konum: tahmin sıfırdan
                    if (!havePose)
                    {
                        havePose = true;
                        poseX = k.x; poseZ = k.z; poseYaw = k.yaw;
                        Spawn();
                    }
                }
                // gelen konuma (+ tahmin) yumuşak geçiş; yön daha hızlı (görüntü anında döner)
                float a = 1f - Mathf.Exp(-dt / 0.25f), ay = 1f - Mathf.Exp(-dt / 0.08f);
                poseX = Mathf.Lerp(poseX, k.x, a);
                poseZ = Mathf.Lerp(poseZ, k.z, a);
                poseYaw += Mathf.DeltaAngle(poseYaw, k.yaw + predicted) * ay;
            }

            bool show = havePose && k != null && k.var;
            root.gameObject.SetActive(show);
            if (!show) return;

            float c = Mathf.Cos(poseYaw * Mathf.Deg2Rad), s = Mathf.Sin(poseYaw * Mathf.Deg2Rad);
            foreach (var cr in creatures)
            {
                Wander(cr, dt);
                // ev -> robot: ileri = (sin yaw, cos yaw), sağ = (cos yaw, -sin yaw)
                float dx = cr.pos.x - poseX, dz = cr.pos.y - poseZ;
                float right = dx * c - dz * s, fwd = dx * s + dz * c;
                cr.t.localPosition = new Vector3(right, -CameraHeight, fwd);
                cr.t.localRotation = Quaternion.Euler(0f, cr.heading - poseYaw, 0f);
                Animate(cr);
                Shadow(cr);
            }
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

        void Spawn()
        {
            var colors = new[] { new Color(1f, 0.62f, 0.25f), new Color(0.35f, 0.85f, 0.75f), new Color(0.75f, 0.5f, 1f) };
            for (int i = 0; i < Count; i++)
            {
                // robotun önünde, 100 derecelik yayda, 0,7-1,3 m
                float ang = poseYaw + (i - 1) * 50f + Random.Range(-10f, 10f);
                float r = Random.Range(0.7f, 1.3f);
                var home = new Vector2(poseX + Mathf.Sin(ang * Mathf.Deg2Rad) * r, poseZ + Mathf.Cos(ang * Mathf.Deg2Rad) * r);
                var cr = Build(colors[i % colors.Length], Random.Range(0.11f, 0.16f));
                cr.home = cr.pos = cr.target = home;
                cr.heading = ang + 180f;
                cr.phase = Random.value * 10f;
                creatures.Add(cr);
            }
        }

        void Wander(Creature cr, float dt)
        {
            if (Time.time > cr.nextTarget || Vector2.Distance(cr.pos, cr.target) < 0.03f)
            {
                cr.target = cr.home + Random.insideUnitCircle * cr.wander;
                cr.nextTarget = Time.time + Random.Range(4f, 9f);
            }
            var d = cr.target - cr.pos;
            float want = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
            cr.heading = Mathf.MoveTowardsAngle(cr.heading, want, 70f * dt);
            // yalnız yönü hedefe yakınken ilerle, yavaşça (6 cm/sn)
            if (Mathf.Abs(Mathf.DeltaAngle(cr.heading, want)) < 40f && d.magnitude > 0.03f)
            {
                float h = cr.heading * Mathf.Deg2Rad;
                cr.pos += new Vector2(Mathf.Sin(h), Mathf.Cos(h)) * Mathf.Min(0.06f * dt, d.magnitude);
            }
        }

        void Animate(Creature cr)
        {
            float t = Time.time + cr.phase;
            float hop = Mathf.Abs(Mathf.Sin(t * 3.2f));
            cr.body.localPosition = new Vector3(0f, cr.size * (0.55f + 0.18f * hop), 0f);
            float sq = 1f + 0.08f * (1f - hop);
            cr.body.localScale = new Vector3(cr.size * sq, cr.size / sq, cr.size * sq);
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

        void SetColor(Transform t, Color color, float glow)
        {
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
