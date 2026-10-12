using ARaki.Game;
using UnityEngine;

namespace ARaki.AR
{
    /// <summary>
    /// Bir bulgunun AR görünümünü canlandırır: süzülme/zıplama, kanat çırpma, göz kırpma, ürkme titremesi,
    /// yakalama ilerledikçe parlama, belirme ve yakalanma animasyonları. Şekil ve renkler sahne kurulurken şablona işlenir.
    /// </summary>
    public class FindVisual : MonoBehaviour
    {
        [SerializeField] Transform body;                 // süzülen/ezilen gövde
        [SerializeField] Transform[] wings;
        [SerializeField] Transform[] eyes;
        [SerializeField] Transform[] spinners;          // sürekli dönen halkalar/uydular (efsane bulgular)
        [SerializeField] float spinSpeed = 70f;
        [SerializeField] Renderer[] glowRenderers;
        [SerializeField] GameObject tierAura;
        [SerializeField] float hoverHeight = 0.02f;
        [SerializeField] float bobAmount = 0.012f;
        [SerializeField] float flapSpeed = 22f;
        [SerializeField] FindKind kind;

        static readonly int GlowId = Shader.PropertyToID("_Glow");

        MaterialPropertyBlock block;
        float age;
        float revealT = 1f;
        float captureT = -1f;
        float nextBlink = 2f;
        float blinkT = -1f;
        Vector3 bodyBase = Vector3.zero, bodyScaleBase = Vector3.one;
        bool cached;

        public bool Capturing => captureT >= 0f;

        void Cache()
        {
            if (cached) return;
            cached = true;
            age = Random.value * 10f;
            block = new MaterialPropertyBlock();
            if (body != null) { bodyBase = body.localPosition; bodyScaleBase = body.localScale; }
        }

        public void SetTier(int tier)
        {
            if (tierAura == null) return;
            tierAura.SetActive(tier >= 2);
            if (tier >= 3) tierAura.transform.localScale *= 1.4f;
        }

        public void PlayReveal() { Cache(); revealT = 0f; }

        public void PlayCapture()
        {
            Cache();
            captureT = 0f;
            gameObject.SetActive(true);
        }

        public void Refresh(float dt, Find f)
        {
            Cache();
            age += dt;
            if (body == null) return;

            float reveal = 1f;
            if (!Application.isPlaying) revealT = 1f;       // editör görüntülerinde zaman akmaz
            if (revealT < 1f)
            {
                revealT = Mathf.Min(1f, revealT + dt / 0.4f);
                float x = revealT - 1f;
                reveal = 1f + 2.2f * x * x * x + 1.2f * x * x;      // hafif aşma (overshoot) ile belirme
            }

            float bob = Mathf.Sin(age * 2.6f) * bobAmount;
            float hop = 0f;
            Vector3 shake = Vector3.zero;
            float squash = 1f;
            if (f.State == FindState.Fleeing)
            {
                hop = Mathf.Abs(Mathf.Sin(age * 12f)) * 0.07f;
                squash = 1f + Mathf.Sin(age * 24f) * 0.08f;
            }
            else if (f.Progress > 0.05f && (kind == FindKind.Shy || kind == FindKind.Legend))
            {
                shake = new Vector3(Mathf.Sin(age * 60f), 0f, Mathf.Cos(age * 53f)) * (0.004f * f.Progress);
            }

            body.localPosition = bodyBase + new Vector3(0f, hoverHeight + bob + hop, 0f) + shake;
            float progressScale = 1f - 0.25f * f.Progress;                           // yakalanırken küçülür
            body.localScale = Vector3.Scale(bodyScaleBase, new Vector3(1f / Mathf.Sqrt(squash), squash, 1f / Mathf.Sqrt(squash))) * (reveal * progressScale);

            if (wings != null)
                for (int i = 0; i < wings.Length; i++)
                {
                    float side = i % 2 == 0 ? 1f : -1f;
                    wings[i].localRotation = Quaternion.Euler(0f, 0f, side * (25f + Mathf.Sin(age * flapSpeed + i) * 28f));
                }

            if (spinners != null)
                for (int i = 0; i < spinners.Length; i++)
                    spinners[i].Rotate(0f, spinSpeed * (i % 2 == 0 ? 1f : -1.4f) * dt, 0f, Space.Self);

            if (eyes != null && eyes.Length > 0)
            {
                nextBlink -= dt;
                if (nextBlink <= 0f) { blinkT = 0f; nextBlink = Random.Range(2.5f, 5f); }
                float open = 1f;
                if (blinkT >= 0f)
                {
                    blinkT += dt;
                    open = Mathf.Abs(blinkT - 0.06f) / 0.06f;
                    if (blinkT > 0.12f) blinkT = -1f;
                }
                foreach (var e in eyes) { var s = e.localScale; s.y = Mathf.Max(0.12f, open) * Mathf.Abs(s.x); e.localScale = s; }
            }

            SetGlow(0.35f + 0.25f * Mathf.Sin(age * 3f) + 1.4f * f.Progress);
        }

        void SetGlow(float value)
        {
            if (glowRenderers == null) return;
            block.SetFloat(GlowId, value);
            foreach (var r in glowRenderers) if (r != null) r.SetPropertyBlock(block);
        }

        void Update()
        {
            if (captureT < 0f) return;
            captureT += Time.deltaTime;
            float k = Mathf.Clamp01(captureT / 0.55f);
            if (body != null)
            {
                body.localScale = bodyScaleBase * Mathf.Lerp(0.75f, 0.05f, k * k);
                body.localPosition = bodyBase + Vector3.up * (hoverHeight + k * 0.35f);
            }
            SetGlow(2f + 3f * k);
            if (k >= 1f)
            {
                captureT = -1f;
                gameObject.SetActive(false);
            }
        }
    }
}
