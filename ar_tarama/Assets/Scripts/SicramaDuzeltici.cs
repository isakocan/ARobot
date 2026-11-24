using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ArTarama
{
    /// <summary>
    /// XR Origin'e eklenir: ARCore'un tek karelik poz sicramasini (PozSureklilik) gorunce XR Origin'i o kadar tasir/dondurur.
    /// Boylece kamera, derinlik noktalari, isaretler ve cizilen harita hep ayni (surekli) dunya uzayinda kalir; harita
    /// sicramayla kaymaz. Diger betiklerden once calisir (kare okunmadan once duzeltilmis olsun).
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public class SicramaDuzeltici : MonoBehaviour
    {
        public Transform kamera;
        public readonly PozSureklilik poz = new PozSureklilik();
        public static int Sayi;

        public void Sifirla() { poz.Sifirla(); transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); }

        void Update() { Denetle(); }
        void LateUpdate() { Denetle(); }

        float sonZaman = -1f;
        void Denetle()
        {
            if (kamera == null) return;
            if (!(ARSession.state == ARSessionState.SessionTracking && ARSession.notTrackingReason == NotTrackingReason.None)) return;
            float t = Time.unscaledTime;
            if (t == sonZaman) return;        // karede bir kez (Update ve LateUpdate)
            sonZaman = t;
            // ham (oturum) poz: XR Origin'e gore
            Vector3 p = transform.InverseTransformPoint(kamera.position);
            Quaternion q = Quaternion.Inverse(transform.rotation) * kamera.rotation;
            if (!poz.Isle(p.x, p.y, p.z, q.x, q.y, q.z, q.w, t)) return;
            transform.SetPositionAndRotation(new Vector3((float)poz.Tx, (float)poz.Ty, (float)poz.Tz), Quaternion.AngleAxis((float)(poz.A * 180.0 / System.Math.PI), Vector3.up));
            Sayi = poz.Sayi;
            Gunluk.Yaz("poz", poz.Son);
            Debug.Log("[ArTarama] " + poz.Son);
        }
    }
}
