using UnityEngine;

namespace ARaki.AR
{
    /// <summary>Bir hedef tamamlanınca yayılıp sönen halka (tek seferlik efekt).</summary>
    public class HoloBurst : MonoBehaviour
    {
        [SerializeField] LineRenderer ring;
        [SerializeField] Color color = new Color(0.45f, 1f, 0.6f) * 1.8f;
        [SerializeField] float seconds = 0.9f;
        [SerializeField] float endScale = 3.2f;

        float t = -1f;
        Material material;

        public void Play()
        {
            gameObject.SetActive(true);
            material ??= ring.material;
            t = 0f;
        }

        void Update()
        {
            if (t < 0f) return;
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / seconds);
            float eased = 1f - (1f - k) * (1f - k);
            transform.localScale = Vector3.one * Mathf.Lerp(0.4f, endScale, eased);
            Color c = color * (1f - k);
            c.a = 1f;
            material.SetColor("_BaseColor", c);
            if (k >= 1f)
            {
                t = -1f;
                gameObject.SetActive(false);
            }
        }
    }
}
