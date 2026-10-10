using ARaki.Robot;
using UnityEngine;

namespace ARaki.Game
{
    /// <summary>
    /// Demo düşmanı: odada dolaşan bir sanal sonda. Robot yaklaşırsa üzerine gelir ve temas ederse bataryayı (yakıtı) yer.
    /// Düşman tamamen oyun mantığıdır; fiziksel dünyada karşılığı yoktur, yalnızca AR'da görünür.
    /// </summary>
    public class Threat : MonoBehaviour
    {
        [SerializeField] Transform robot;
        [SerializeField] MockRobotLink link;
        [SerializeField] Vector2 areaMin = new Vector2(-2.6f, -2.1f);
        [SerializeField] Vector2 areaMax = new Vector2(2.6f, 2.1f);
        [SerializeField] float wanderSpeed = 0.25f;
        [SerializeField] float chaseSpeed = 0.42f;
        [SerializeField] float noticeDistance = 1.6f;
        [SerializeField] float engageDistance = 0.55f;
        [SerializeField] float drainPerSecond = 0.06f;
        [SerializeField] float hoverHeight = 0.28f;

        public bool Engaged { get; private set; }
        public bool Chasing { get; private set; }

        Vector3 target;
        float phase;

        void Start()
        {
            phase = Random.value * 6.28f;
            PickTarget();
        }

        void Update() => Tick(Time.deltaTime);

        public void Tick(float dt)
        {
            Vector3 pos = transform.position;
            float toRobot = MissionController.FlatDistance(pos, robot.position);
            Chasing = toRobot < noticeDistance;
            Engaged = toRobot < engageDistance;

            Vector3 goal = Chasing ? new Vector3(robot.position.x, 0f, robot.position.z) : target;
            Vector3 flat = new Vector3(pos.x, 0f, pos.z);
            Vector3 step = Vector3.MoveTowards(flat, goal, (Chasing ? chaseSpeed : wanderSpeed) * dt);
            if (!Chasing && (step - goal).sqrMagnitude < 0.01f) PickTarget();

            phase += dt * 2.2f;
            transform.position = new Vector3(step.x, hoverHeight + Mathf.Sin(phase) * 0.03f, step.z);

            if (Engaged) link.AddBattery(-drainPerSecond * dt);
        }

        void PickTarget() =>
            target = new Vector3(Random.Range(areaMin.x, areaMax.x), 0f, Random.Range(areaMin.y, areaMax.y));
    }
}
