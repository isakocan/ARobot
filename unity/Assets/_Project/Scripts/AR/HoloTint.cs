using System.Collections.Generic;
using UnityEngine;

namespace ARaki.AR
{
    /// <summary>
    /// Bir hologramın tüm parçalarının rengini birlikte yönetir (aktif, tamamlandı, kilitli...) ve ona canlılık verir:
    /// hafif nefes alma, ara sıra titreme (sinyal bozulması) ve dışarıdan verilen parlaklık çarpanı.
    /// </summary>
    public class HoloTint : MonoBehaviour
    {
        [SerializeField] float pulseAmount = 0.12f;
        [SerializeField] float pulseSpeed = 3f;
        [SerializeField] bool flicker = true;

        public float Intensity { get; set; } = 1f;

        readonly List<Material> materials = new List<Material>();
        readonly List<float> weights = new List<float>();
        Color baseColor;
        bool hasColor;
        float phase, nextFlicker, flickerUntil;

        void Awake() => phase = Random.value * 6.28f;

        public void SetColor(Color color)
        {
            Cache();
            baseColor = color;
            hasColor = true;
            Apply(1f);
        }

        void Cache()
        {
            if (materials.Count > 0) return;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.GetComponentInParent<HoloBurst>(true) != null) continue; // patlama halkası kendi rengini yönetir
                materials.Add(r.material);
                weights.Add(r.name.StartsWith("Glow") ? 0.22f : (r.name == "Edge" || r.name == "Arc") ? 0.45f : 1f);
            }
        }

        void Update()
        {
            if (!hasColor) return;
            float k = 1f + Mathf.Sin(Time.time * pulseSpeed + phase) * pulseAmount;
            if (flicker)
            {
                if (Time.time > nextFlicker)
                {
                    flickerUntil = Time.time + 0.06f;
                    nextFlicker = Time.time + Random.Range(2.5f, 7f);
                }
                if (Time.time < flickerUntil) k *= 0.5f;
            }
            Apply(k);
        }

        void Apply(float k)
        {
            for (int i = 0; i < materials.Count; i++)
            {
                Color c = baseColor * (Intensity * k * weights[i]);
                c.a = 1f;
                materials[i].SetColor("_BaseColor", c);
            }
        }
    }
}
