using UnityEngine;

namespace ARaki.AR
{
    /// <summary>Hologram parçalarını döndürür.</summary>
    public class Spin : MonoBehaviour
    {
        [SerializeField] Vector3 degreesPerSecond = new Vector3(0f, 60f, 0f);

        void Update() => transform.Rotate(degreesPerSecond * Time.deltaTime, Space.Self);
    }
}
