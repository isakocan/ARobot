using UnityEngine;

namespace ARaki.AR
{
    /// <summary>
    /// AR sahnesinin kökü. Robot kamerasının gördüğü dünya, kameranın etrafındaki bu küçük alana
    /// "robota göre" konumlarla aktarılır; böylece AR objeleri gerçek görüntüyle aynı perspektifte çizilir.
    /// Gerçek sistemde konumlar ArUco algılamalarından gelecek, buradaki dünya konumlarının yerini alacak.
    /// </summary>
    public class ARRig : MonoBehaviour
    {
        [SerializeField] Transform robot;
        [SerializeField] Vector3 cameraOffset = new Vector3(0f, 0.25f, -0.07f); // 360 kameranın robota göre yeri

        public Transform Robot => robot;

        public Vector3 ToRig(Vector3 worldPosition) =>
            transform.position + robot.InverseTransformPoint(worldPosition) - cameraOffset;

        public Quaternion ToRig(Quaternion worldRotation) => Quaternion.Inverse(robot.rotation) * worldRotation;
    }
}
