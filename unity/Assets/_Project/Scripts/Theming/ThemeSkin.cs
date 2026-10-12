using UnityEngine;
using UnityEngine.Rendering;

namespace ARaki.Theming
{
    /// <summary>Seçili temayı sahneye uygular: duvar/tavan görünümü, güneş ve ortam ışığı, görüntü sonrası işleme.</summary>
    public class ThemeSkin : MonoBehaviour
    {
        [SerializeField] Light sun;
        [SerializeField] Volume volume;
        [SerializeField] VolumeProfile[] profiles;     // ThemeId sırasıyla

        void Awake() => Apply();

        public void Apply() => Apply(ThemeCatalog.Current);

        public void Apply(ThemeId id)
        {
            var def = ThemeCatalog.Get(id);
            UiTheme.Apply(def.Palette);

            foreach (var s in FindObjectsByType<ThemeSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (s.ThemeMaterials == null || (int)id >= s.ThemeMaterials.Length) continue;
                var r = s.GetComponent<Renderer>();
                if (r != null && s.ThemeMaterials[(int)id] != null) r.sharedMaterial = s.ThemeMaterials[(int)id];
            }

            if (sun != null)
            {
                sun.color = def.SunColor;
                sun.intensity = def.SunIntensity;
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = def.AmbientSky;
            RenderSettings.ambientEquatorColor = def.AmbientEquator;
            RenderSettings.ambientGroundColor = def.AmbientGround;

            if (volume != null && profiles != null && (int)id < profiles.Length && profiles[(int)id] != null)
                volume.profile = profiles[(int)id];
        }
    }
}
