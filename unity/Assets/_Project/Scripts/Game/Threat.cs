using System;
using ARaki.Robot;
using UnityEngine;

namespace ARaki.Game
{
    public enum ThreatState { Patrol, Alert, Chase, Scared }

    /// <summary>
    /// Demo düşmanı: odada devriye gezen sanal bir sonda. Görüş açısına giren robotu fark eder, kısa bir süre
    /// şüphelenir, sonra takip eder; temas ederse yakıt yer. Robotun LED'i yüzüne tutulursa ışıktan kaçar.
    /// Robotu sürekli kovalamaz: yeterince uzaklaşınca takibi bırakıp devriyeye döner.
    /// Düşman tamamen oyun mantığıdır; fiziksel dünyada karşılığı yoktur, yalnızca AR'da görünür.
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
        [SerializeField] float loseTrackSeconds = 2.5f;

        [Header("Etki")]
        [SerializeField] float engageDistance = 0.5f;
        [SerializeField] float drainPerSecond = 0.05f;
        [SerializeField] float ledRange = 2.2f;
        [SerializeField] float ledHalfAngle = 50f;
        [SerializeField] float scareSeconds = 3f;

        public event Action<Threat, ThreatState> StateChanged;

        public ThreatState State { get; private set; } = ThreatState.Patrol;
        public bool Engaged { get; private set; }
        public bool Chasing => State == ThreatState.Chase || State == ThreatState.Alert;
        public bool Scared => State == ThreatState.Scared;
        public float ViewDistance => viewDistance;
        public float ViewHalfAngle => viewHalfAngle;
        public float HoverHeight => hoverHeight;

        Vector3 heading = Vector3.forward;
        Vector3 target;
        float phase, timer, lostTimer;

        void Start()
        {
            phase = UnityEngine.Random.value * 6.28f;
            heading = Quaternion.Euler(0f, UnityEngine.Random.value * 360f, 0f) * Vector3.forward;
            PickTarget();
        }

        void Update() => Tick(Time.deltaTime);

        public void Tick(float dt)
        {
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

            if (lit && State != ThreatState.Scared)
            {
                timer = scareSeconds;
                SetState(ThreatState.Scared);
            }

            switch (State)
            {
                case ThreatState.Patrol:
                    move = Steer(flat, target, patrolSpeed, dt);
                    if ((flat - target).sqrMagnitude < 0.04f) PickTarget();
                    if (sees)
                    {
                        timer = alertSeconds;
                        SetState(ThreatState.Alert);
                    }
                    break;

                case ThreatState.Alert:
                    Face(toRobotDir, dt);
                    timer -= dt;
                    if (!sees && dist > viewDistance * 1.2f) SetState(ThreatState.Patrol);
                    else if (timer <= 0f) { lostTimer = 0f; SetState(ThreatState.Chase); }
                    break;

                case ThreatState.Chase:
                    move = Steer(flat, robotFlat, chaseSpeed, dt);
                    Engaged = dist < engageDistance;
                    lostTimer = dist > loseTrackDistance ? lostTimer + dt : 0f;
                    if (lostTimer > loseTrackSeconds) { PickTarget(); SetState(ThreatState.Patrol); }
                    break;

                case ThreatState.Scared:
                    Vector3 away = flat - robotFlat;
                    Vector3 fleeGoal = flat + (away.sqrMagnitude > 0.0001f ? away.normalized : -heading) * 2f;
                    move = Steer(flat, ClampToArea(fleeGoal), fleeSpeed, dt);
                    if (lit) timer = scareSeconds;      // ışık sürdükçe kaçar
                    timer -= dt;
                    if (timer <= 0f) { PickTarget(); SetState(ThreatState.Patrol); }
                    break;
            }

            Vector3 next = ClampToArea(flat + move);
            phase += dt * 2.2f;
            transform.position = new Vector3(next.x, hoverHeight + Mathf.Sin(phase) * 0.03f, next.z);
            transform.rotation = Quaternion.LookRotation(heading, Vector3.up);

            if (Engaged) link.AddBattery(-drainPerSecond * dt);
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
            StateChanged?.Invoke(this, next);
        }

        void PickTarget() =>
            target = new Vector3(UnityEngine.Random.Range(areaMin.x, areaMax.x), 0f, UnityEngine.Random.Range(areaMin.y, areaMax.y));
    }
}
