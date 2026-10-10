using UnityEngine;

namespace ARaki.Robot
{
    /// <summary>
    /// Robotun önündeki LED'in sahnedeki karşılığı: gerçek bir ışık yayar, yani 360° kamera görüntüsünde de görünür
    /// ve odayı aydınlatır. Açılıp kapanması yumuşak.
    /// </summary>
    public class RobotLed : MonoBehaviour
    {
        [SerializeField] MockRobotLink link;
        [SerializeField] Light spot;
        [SerializeField] Renderer lens;
        [SerializeField] float maxIntensity = 22f;
        [SerializeField] float fadeSpeed = 9f;

        float level;

        void Update() => Tick(Time.deltaTime);

        public void Tick(float dt)
        {
            level = Mathf.MoveTowards(level, link.LedOn ? 1f : 0f, fadeSpeed * dt);
            if (spot != null)
            {
                spot.enabled = level > 0.01f;
                spot.intensity = maxIntensity * level;
            }
            if (lens != null)
                lens.material.SetColor("_BaseColor", Color.Lerp(new Color(0.25f, 0.28f, 0.3f), new Color(1f, 0.97f, 0.9f) * 4f, level));
        }
    }
}
