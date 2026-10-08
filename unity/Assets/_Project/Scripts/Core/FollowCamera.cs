using UnityEngine;

namespace ARaki.Core
{
    /// <summary>Hedefi yukarıdan, yumuşak şekilde takip eden kamera (simülasyon sahnesi için).</summary>
    public class FollowCamera : MonoBehaviour
    {
        [SerializeField] Transform target;
        [SerializeField] float height = 1.5f;
        [SerializeField] float smoothTime = 0.25f;

        Vector3 velocity;

        void LateUpdate()
        {
            if (target == null) return;
            Vector3 goal = target.position + Vector3.up * height;
            transform.position = Vector3.SmoothDamp(transform.position, goal, ref velocity, smoothTime);
        }
    }
}
