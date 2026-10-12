using ARaki.AR;
using ARaki.World;
using UnityEngine;

namespace ARaki.Game
{
    public enum GhostState { Drift, Chase, Gone }

    /// <summary>
    /// Evde dolaşan gölge: toplanmaz, kaçılır. Duvarlardan geçer, gürültüye ve ışığa gelir, robottan biraz yavaştır,
    /// üssün güvenli alanına giremez. Dokunursa pil harcatır ve taşınan bir kaydı alır. Davranışı <see cref="Expedition"/> yürütür.
    /// </summary>
    public class Ghost : MonoBehaviour
    {
        public GhostState State;
        public GhostVisual Visual;
        public Vector3 WanderTarget;
        public float WanderTimer;
        public float Age;
        public float LostTimer;             // robotu fark etmediği süre
        public float Alert;                 // 0..1 görsel/işitsel uyarı yoğunluğu

        public Vector3 Position => new Vector3(transform.position.x, 0f, transform.position.z);
        public bool Alive => State != GhostState.Gone;
    }
}
