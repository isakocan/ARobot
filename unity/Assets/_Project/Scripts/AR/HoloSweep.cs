using UnityEngine;

namespace ARaki.AR
{
    /// <summary>Bir hologram parçasını yukarı aşağı süpürür (tarama çizgisi hissi).</summary>
    public class HoloSweep : MonoBehaviour
    {
        [SerializeField] float minY = 0.01f;
        [SerializeField] float maxY = 0.4f;
        [SerializeField] float cyclesPerSecond = 0.45f;

        void Update()
        {
            float t = Mathf.PingPong(Time.time * cyclesPerSecond * 2f, 1f);
            var p = transform.localPosition;
            p.y = Mathf.Lerp(minY, maxY, t);
            transform.localPosition = p;
        }
    }
}
