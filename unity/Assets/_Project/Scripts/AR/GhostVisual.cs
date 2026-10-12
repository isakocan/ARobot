using ARaki.Game;
using UnityEngine;

namespace ARaki.AR
{
    /// <summary>Gölgenin AR görünümü: süzülür, sallanır; kovalarken gözleri kızarır ve büyür.</summary>
    public class GhostVisual : MonoBehaviour
    {
        [SerializeField] Transform body;
        [SerializeField] Transform tail;
        [SerializeField] Renderer[] eyes;
        [SerializeField] float hover = 0.16f;

        MaterialPropertyBlock block;
        static readonly int GlowId = Shader.PropertyToID("_Glow");
        float phase;

        void Awake() => phase = Random.value * 10f;

        public void Refresh(float dt, Ghost g)
        {
            float t = Time.time + phase;
            float chase = g.State == GhostState.Chase ? 1f : 0f;
            if (body != null)
            {
                body.localPosition = new Vector3(0f, hover + Mathf.Sin(t * (1.6f + chase)) * 0.025f, 0f);
                body.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.1f) * 5f, 0f, Mathf.Sin(t * 1.4f) * 8f);
                float s = 1f + chase * 0.18f + Mathf.Sin(t * 6f) * 0.03f * chase;
                body.localScale = Vector3.one * s;
            }
            if (tail != null) tail.localRotation = Quaternion.Euler(0f, t * 40f, Mathf.Sin(t * 2f) * 10f);

            if (eyes != null && eyes.Length > 0)
            {
                block ??= new MaterialPropertyBlock();
                float glow = 0.6f + chase * 1.8f + g.Alert * 0.8f;
                foreach (var e in eyes)
                {
                    if (e == null) continue;
                    e.GetPropertyBlock(block);
                    block.SetFloat(GlowId, glow);
                    e.SetPropertyBlock(block);
                }
            }
        }
    }
}
