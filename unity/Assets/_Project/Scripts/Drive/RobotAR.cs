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

        class Creature
        {
            public Transform t, body;
            public Transform[] eyes;
            public Vector2 home, pos, target;
            public float heading, phase, nextTarget, size;
        }

        public void Init(RealRobotLink robotLink, PanoramaViewer viewer)
        {
            link = robotLink;
            root = new GameObject("AR (ev)").transform;
            root.SetParent(viewer.transform, false);
            root.localPosition = viewer.ViewCamera.transform.localPosition;
            baseMat = Resources.Load<Material>("ARToon");
            UdpRobotConnection.RequestPose = true;
        }

        void OnDestroy() => UdpRobotConnection.RequestPose = false;

        void Update()
        {
            if (link == null) return;
            var st = link.IsConnected && !link.Demo ? link.Status : null;
            var k = st?.konum;
            float dt = Time.deltaTime;

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
            }
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
                cr.target = cr.home + Random.insideUnitCircle * 0.45f;
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

            var shadow = Prim(PrimitiveType.Cylinder, cr.t, new Color(0f, 0f, 0f, 1f), 0f);
            shadow.localScale = new Vector3(size * 1.1f, 0.002f, size * 1.1f);
            shadow.localPosition = new Vector3(0f, 0.002f, 0f);
            SetColor(shadow, new Color(0.05f, 0.05f, 0.07f), 0f);

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
