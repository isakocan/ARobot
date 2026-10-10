using UnityEngine;

namespace ARaki.Robot
{
    /// <summary>Tekerlek görsellerini robotun gerçek tekerlek hızına göre döndürür (yalnızca görsel).</summary>
    public class WheelSpinner : MonoBehaviour
    {
        [SerializeField] MockRobotLink link;
        [SerializeField] Transform[] leftWheels;
        [SerializeField] Transform[] rightWheels;
        [SerializeField] float wheelRadius = 0.045f;

        void Update()
        {
            var t = link.Telemetry;
            Spin(leftWheels, t.LeftSpeed);
            Spin(rightWheels, t.RightSpeed);
        }

        void Spin(Transform[] wheels, float speed)
        {
            float degrees = speed * Time.deltaTime / wheelRadius * Mathf.Rad2Deg;
            foreach (var w in wheels) w.Rotate(degrees, 0f, 0f, Space.Self);
        }
    }
}
