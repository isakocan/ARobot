using UnityEngine;

namespace ARaki.Theming
{
    public enum SurfaceRole { Wall, Ceiling, Window }

    /// <summary>
    /// Bir duvar/tavan yüzeyi: seçili temaya göre malzemesi değişir (uzayda yıldızlı gökyüzü, safaride orman, gizemde duvar kağıdı).
    /// Gerçek robotta aynı etki, kameranın zemin/eşya dışında gördüğü piksellere bölütleme ile uygulanır.
    /// </summary>
    public class ThemeSurface : MonoBehaviour
    {
        public SurfaceRole Role;
        public Material[] ThemeMaterials;     // ThemeId sırasıyla
    }
}
