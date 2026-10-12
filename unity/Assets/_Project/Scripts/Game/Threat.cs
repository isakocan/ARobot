using System;
using ARaki.Robot;
using UnityEngine;

namespace ARaki.Game
{
    public enum ThreatState { Patrol, Alert, Chase, Search, Scared }

    public enum ThreatKind { Sentinel, Hunter }

    /// <summary>
    /// İstasyonu koruyan otonom devriye sondası. Rotası boyunca devriye gezer; görüş açısına giren robotu önce
    /// fark eder (şüphelenir), sonra takip eder; robotu kaybedince son gördüğü yere gidip etrafı tarar.
    /// Temas ederse elektrik şokuyla yakıt yer. Robotun LED'i yüzüne tutulursa ışıktan kaçar.
    /// "Avcı" tipi daha hızlı ve daha dar bakar. Düşman tamamen oyun mantığıdır; fiziksel dünyada karşılığı
    /// yoktur, yalnızca AR'da görünür.
    /// </summary>
    public class Threat : MonoBehaviour
    {
        [SerializeField] Transform robot;
        [SerializeField] MockRobotLink link;
        [SerializeField] Vector2 areaMin = new Vector2(-2.6f, -2.1f);
        [SerializeField] Vector2 areaMax = new Vector2(2.6f, 2.1f);

        [Header("Hareket")]
        [SerializeField] float patrolSpeed = 0.15f;
        [SerializeField] float chaseSpeed = 0.26f;
        [SerializeField] float fleeSpeed = 0.45f;
        [SerializeField] float turnSpeed = 140f;       // derece/sn
        [SerializeField] float hoverHeight = 0.28f;

        [Header("Algı")]
        [SerializeField] float viewDistance = 1.5f;
        [SerializeField] float viewHalfAngle = 45f;
        [SerializeField] float alertSeconds = 0.9f;
        [SerializeField] float loseTrackDistance = 2.2f;
        [SerializeField] float loseTrackSeconds = 2.0f;
        [SerializeField] float searchSeconds = 3.6f;

        [Header("Etki")]
        [SerializeField] float engageDistance = 0.5f;
        [SerializeField] float drainPerSecond = 0.05f;
        [SerializeField] float ledRange = 2.2f;
        [SerializeField] float ledHalfAngle = 50f;
        [SerializeField] float scareSeconds = 3f;

        public event Action<Threat, ThreatState> StateChanged;

        /// <summary>Hız çarpanı (görev zorluğu). 1 = normal.</summary>
        public float Difficulty { get; private set; } = 1f;

        /// <summary>Kapalıyken temas yakıt yemez (menü ve sonuç ekranlarında).</summary>
        public bool DrainEnabled { get; set; } = true;

        public ThreatKind Kind { get; private set; } = ThreatKind.Sentinel;
        public ThreatState State { get; private set; } = ThreatState.Patrol;
        /// <summary>Robotun sensörü bu sondayı şu an görüyor mu? (arayüz etiketi ve harita noktası için)</summary>
        public bool Detected { get; set; } = true;
        public bool Engaged { get; private set; }
        public bool Chasing => State == ThreatState.Chase || State == ThreatState.Alert;
        public bool Scared => State == ThreatState.Scared;
        public float ViewDistance => viewDistance;
        public float ViewHalfAngle => viewHalfAngle;
        public float HoverHeight => hoverHeight;
        public Vector3 Heading => heading;
        public Vector3 Velocity { get; private set; }
        public float StateTime { get; private set; }

        Vector3 heading = Vector3.forward;
        Vector3[] route;
        int routeIndex;
        Vector3 lastKnown, searchBase;
        float phase, timer, lostTimer;
        float baseView = 1.5f, baseAngle = 45f, baseChase = 0.26f, baseTurn = 140f;
        bool baseCaptured;

        void Awake() => CaptureBase();

        void CaptureBase()
        {
            if (baseCaptured) return;
            baseCaptured = true;
            baseView = viewDistance;
            baseAngle = viewHalfAngle;
            baseChase = chaseSpeed;
            baseTurn = turnSpeed;
        }

        void Start()
        {
            phase = UnityEngine.Random.value * 6.28f;
            if (route == null) BuildRoute();
        }

        void Update() => Tick(Time.deltaTime);

        public void SetDifficulty(float scale) => Difficulty = Mathf.Max(0.1f, scale);

        /// <summary>Tipi seçer: Avcı daha hızlı, daha uzağı ama daha dar görür.</summary>
        public void SetKind(ThreatKind kind)
        {
            CaptureBase();
            Kind = kind;
            bool hunter = kind == ThreatKind.Hunter;
            viewDistance = baseView * (hunter ? 1.4f : 1f);
            viewHalfAngle = baseAngle * (hunter ? 0.65f : 1f);
            chaseSpeed = baseChase * (hunter ? 1.3f : 1f);
            turnSpeed = baseTurn * (hunter ? 1.25f : 1f);
        }

        /// <summary>Devriyeye yeniden başlar: konuma yerleşir, sakinleşir, yeni bir rota çizer.</summary>
        public void Respawn(Vector3 position)
        {
            transform.position = new Vector3(position.x, hoverHeight, position.z);
            heading = Quaternion.Euler(0f, UnityEngine.Random.value * 360f, 0f) * Vector3.forward;
            State = ThreatState.Patrol;
            Engaged = false;
            timer = lostTimer = StateTime = 0f;
            Velocity = Vector3.zero;
            BuildRoute();
        }

        void BuildRoute()
        {
            int n = 3 + UnityEngine.Random.Range(0, 2);
            route = new Vector3[n];
            for (int i = 0; i < n; i++)
                route[i] = new Vector3(UnityEngine.Random.Range(areaMin.x, areaMax.x), 0f, UnityEngine.Random.Range(areaMin.y, areaMax.y));
            routeIndex = 0;
        }

        public void Tick(float dt)
        {
            if (route == null) BuildRoute();
            Vector3 pos = transform.position;
            Vector3 flat = new Vector3(pos.x, 0f, pos.z);
            Vector3 robotFlat = new Vector3(robot.position.x, 0f, robot.position.z);
            Vector3 toRobot = robotFlat - flat;
            float dist = toRobot.magnitude;
            Vector3 toRobotDir = dist > 0.001f ? toRobot / dist : heading;

            bool lit = link.LedOn && dist < ledRange &&
                       Vector3.Angle(robot.forward, -toRobotDir) < ledHalfAngle;
            bool sees = dist < viewDistance && Vector3.Angle(heading, toRobotDir) < viewHalfAngle;

            Vector3 move = Vector3.zero;
            Engaged = false;
            StateTime += dt;

            if (lit && State != ThreatState.Scared)
            {
                timer = scareSeconds;
                SetState(ThreatState.Scared);
            }

            switch (State)
            {
                case ThreatState.Patrol:
                {
                    Vector3 target = route[routeIndex];
                    move = Steer(flat, target, patrolSpeed * Difficulty, dt);
                    if ((flat - target).sqrMagnitude < 0.05f) routeIndex = (routeIndex + 1) % route.Length;
                    if (sees)
                    {
                        timer = alertSeconds / Mathf.Max(0.6f, Difficulty);
                        SetState(ThreatState.Alert);
                    }
                    break;
                }

                case ThreatState.Alert:
                    Face(toRobotDir, dt);
                    lastKnown = robotFlat;
                    timer -= dt;
                    if (!sees && dist > viewDistance * 1.2f) SetState(ThreatState.Patrol);
                    else if (timer <= 0f) { lostTimer = 0f; SetState(ThreatState.Chase); }
                    break;

                case ThreatState.Chase:
                    move = Steer(flat, robotFlat, chaseSpeed * Difficulty, dt);
                    lastKnown = robotFlat;
                    Engaged = dist < engageDistance;
                    lostTimer = dist > loseTrackDistance || !sees && dist > viewDistance ? lostTimer + dt : 0f;
                    if (lostTimer > loseTrackSeconds)
                    {
                        timer = searchSeconds;
                        searchBase = Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg * Vector3.up;
                        SetState(ThreatState.Search);
                    }
                    break;

                case ThreatState.Search:
                {
                    // son görülen yere git, sonra yavaşça etrafı tara
                    if ((flat - lastKnown).sqrMagnitude > 0.08f && timer > searchSeconds * 0.7f)
                        move = Steer(flat, lastKnown, patrolSpeed * 1.6f * Difficulty, dt);
                    else
                    {
                        timer -= dt;
                        float yaw = searchBase.y + Mathf.Sin(StateTime * 1.7f) * 75f;
                        Face(Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, dt);
                    }
                    if (sees) { timer = alertSeconds * 0.6f; SetState(ThreatState.Alert); }
                    else if (timer <= 0f) SetState(ThreatState.Patrol);
                    break;
                }

                case ThreatState.Scared:
                {
                    Vector3 away = flat - robotFlat;
                    Vector3 fleeGoal = flat + (away.sqrMagnitude > 0.0001f ? away.normalized : -heading) * 2f;
                    move = Steer(flat, ClampToArea(fleeGoal), fleeSpeed, dt);
                    if (lit) timer = scareSeconds;      // ışık sürdükçe kaçar
                    timer -= dt;
                    if (timer <= 0f) SetState(ThreatState.Patrol);
                    break;
                }
            }

            Vector3 next = ClampToArea(flat + move);
            phase += dt * 2.2f;
            Velocity = dt > 0.0001f ? (next - flat) / dt : Vector3.zero;
            transform.position = new Vector3(next.x, hoverHeight + Mathf.Sin(phase) * 0.03f, next.z);
            transform.rotation = Quaternion.LookRotation(heading, Vector3.up);

            if (Engaged && DrainEnabled) link.AddBattery(-drainPerSecond * dt);
        }

        Vector3 Steer(Vector3 from, Vector3 to, float speed, float dt)
        {
            Vector3 dir = to - from;
            if (dir.sqrMagnitude < 0.0001f) return Vector3.zero;
            Face(dir.normalized, dt);
            // Dönerken yavaşlar: hareket yalnızca baktığı yöne doğru ilerler.
            float align = Mathf.Clamp01(Vector3.Dot(heading, dir.normalized) * 1.5f);
            return heading * (speed * align * dt);
        }

        void Face(Vector3 dir, float dt)
        {
            if (dir.sqrMagnitude < 0.0001f) return;
            heading = Vector3.RotateTowards(heading, dir, turnSpeed * Mathf.Deg2Rad * dt, 0f);
            heading.y = 0f;
            heading.Normalize();
        }

        Vector3 ClampToArea(Vector3 p) =>
            new Vector3(Mathf.Clamp(p.x, areaMin.x, areaMax.x), 0f, Mathf.Clamp(p.z, areaMin.y, areaMax.y));

        void SetState(ThreatState next)
        {
            if (State == next) return;
            State = next;
            StateTime = 0f;
            StateChanged?.Invoke(this, next);
        }
    }
}
