using UnityEngine;

namespace ARaki.AR
{
    /// <summary>Bir hologramın tüm parçalarının rengini birlikte değiştirir (aktif, tamamlandı, kilitli...).</summary>
    public class HoloTint : MonoBehaviour
    {
        Renderer[] renderers;

        public void SetColor(Color color)
        {
            renderers ??= GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers) r.material.SetColor("_BaseColor", color);
        }
    }
}
