using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ArTarama
{
    /// <summary>Duzlem gorsellestirmesi: zemin turkuaz, diger yuzeyler daha soluk. Golge alici ikinci malzeme her zaman aktif kalir.</summary>
    [RequireComponent(typeof(ARPlane))]
    public class PlaneStyle : MonoBehaviour
    {
        public static bool Goster = true;
        /// <summary>ARCore gorunumu: oda modeli gizlenir, ARCore'un buldugu duzlemler belirgin renklerle gosterilir.</summary>
        public static bool ArcoreGorunum;
        MeshRenderer r; ARPlane p; MaterialPropertyBlock mpb;
        static readonly int RenkId = Shader.PropertyToID("_Color");

        void Awake() { r = GetComponent<MeshRenderer>(); p = GetComponent<ARPlane>(); mpb = new MaterialPropertyBlock(); }

        void Update()
        {
            if (r == null) return;
            Color c;
            bool zemin = p.alignment == PlaneAlignment.HorizontalUp && ArApp.ZeminVar &&
                         Mathf.Abs(transform.position.y - ArApp.ZeminY) < 0.15f;
            if (zemin) c = new Color(0.25f, 0.85f, 0.78f, 0.10f);
            else if (p.alignment == PlaneAlignment.HorizontalUp) c = new Color(0.45f, 0.65f, 1f, 0.05f);
            else if (p.alignment == PlaneAlignment.HorizontalDown) c = new Color(0.8f, 0.7f, 1f, 0.03f);
            else c = new Color(0.7f, 0.85f, 1f, 0.02f);   // dikey: duvar tonunu oda modeli cizer
            if (ArcoreGorunum)
            {
                if (p.alignment == PlaneAlignment.HorizontalUp) c = zemin ? new Color(0.2f, 0.9f, 0.6f, 0.40f) : new Color(0.3f, 0.6f, 1f, 0.45f);
                else if (p.alignment == PlaneAlignment.HorizontalDown) c = new Color(0.75f, 0.5f, 1f, 0.40f);
                else c = new Color(1f, 0.6f, 0.25f, 0.40f);
            }
            else if (!Goster) c.a = 0f;
            r.GetPropertyBlock(mpb);
            mpb.SetColor(RenkId, c);
            r.SetPropertyBlock(mpb);
        }
    }
}
