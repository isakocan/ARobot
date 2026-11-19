using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

namespace ArTarama
{
    /// <summary>Isik tahmini: ana isik yonu/siddeti, ortam SH, SH'den uretilen yansima kupu, gunluk degerler.</summary>
    public partial class ArApp
    {
        bool isikLogEdildi;
        Quaternion hedefIsikRot = Quaternion.Euler(50, -30, 0);
        float hedefSiddet = 0.9f; Color hedefRenk = Color.white;
        bool shVar; SphericalHarmonicsL2 sonSh; float sonKup = -10f, sonIsikLog = -10f;
        Cubemap yansimaKupu;
        string isikOzet = "";

        void KareGeldi(ARCameraFrameEventArgs a)
        {
            if (a.timestampNs.HasValue) SonZamanNs = a.timestampNs.Value;
            var le = a.lightEstimation;
            if (isik == null) return;
            if (!isikLogEdildi && (le.mainLightDirection.HasValue || le.averageBrightness.HasValue || le.ambientSphericalHarmonics.HasValue))
            {
                isikLogEdildi = true;
                Debug.Log($"[ArTarama] ISIK ilk veri: parlaklik={le.averageBrightness} lumen={le.mainLightIntensityLumens} yon={le.mainLightDirection} renk={le.mainLightColor} SH={le.ambientSphericalHarmonics.HasValue} duzeltme={le.colorCorrection}");
            }
            if (le.mainLightDirection.HasValue && le.mainLightDirection.Value.sqrMagnitude > 0.0001f)
            {
                Vector3 d = le.mainLightDirection.Value.normalized;
                // golge goruneceksin: isik her zaman yeterince asagi bakmali
                if (d.y > -0.35f) { d.y = -0.35f; d = d.normalized; }
                hedefIsikRot = Quaternion.LookRotation(d);
            }
            if (le.mainLightColor.HasValue) hedefRenk = Color.Lerp(Color.white, le.mainLightColor.Value, 0.7f);
            else if (le.colorCorrection.HasValue) hedefRenk = Color.Lerp(Color.white, le.colorCorrection.Value, 0.6f);
            if (le.mainLightIntensityLumens.HasValue)
            {
                float l = le.mainLightIntensityLumens.Value;
                hedefSiddet = Mathf.Clamp(l < 20f ? l : l / 2000f, 0.25f, 1.6f);
            }
            else if (le.averageBrightness.HasValue) hedefSiddet = Mathf.Clamp(le.averageBrightness.Value * 1.3f, 0.25f, 1.4f);
            if (le.ambientSphericalHarmonics.HasValue)
            {
                sonSh = le.ambientSphericalHarmonics.Value; shVar = true;
                RenderSettings.ambientMode = AmbientMode.Custom;
                RenderSettings.ambientProbe = sonSh;
            }
            else if (le.averageBrightness.HasValue)
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.white * Mathf.Clamp(le.averageBrightness.Value, 0.1f, 1f);
            }
            isikOzet = $"yon=({d3(hedefIsikRot * Vector3.forward)}) siddet={hedefSiddet:F2} renk=({hedefRenk.r:F2},{hedefRenk.g:F2},{hedefRenk.b:F2}) SH={(shVar ? "var" : "yok")} parlaklik={(le.averageBrightness.HasValue ? le.averageBrightness.Value.ToString("F2") : "-")}";
        }

        static string d3(Vector3 v) { return v.x.ToString("F2") + "," + v.y.ToString("F2") + "," + v.z.ToString("F2"); }

        void IsikGuncelle()
        {
            if (isik == null) return;
            float k = 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime);
            isik.transform.rotation = Quaternion.Slerp(isik.transform.rotation, hedefIsikRot, k);
            isik.intensity = Mathf.Lerp(isik.intensity, hedefSiddet, k);
            isik.color = Color.Lerp(isik.color, hedefRenk, k);
            isik.shadows = LightShadows.Soft;
            isik.shadowStrength = 0.8f;
            if (shVar && Time.unscaledTime - sonKup > 2f) { sonKup = Time.unscaledTime; KupGuncelle(); }
            if (Time.unscaledTime - sonIsikLog > 4f)
            {
                sonIsikLog = Time.unscaledTime;
                if (isikLogEdildi) Debug.Log("[ArTarama] ISIK " + isikOzet + $" | uygulanan siddet={isik.intensity:F2} golge={isik.shadows} probe={(probe != null && probe.subsystem != null ? "var" : "yok")}");
            }
        }

        static readonly Vector3[][] YuzYon = new Vector3[6][];

        /// <summary>ARCore ortam SH'sinden 16x16'lik kup uretir; Standard shader yansimasi icin ozel yansima dokusu olur.</summary>
        void KupGuncelle()
        {
            const int n = 16;
            if (yansimaKupu == null)
            {
                yansimaKupu = new Cubemap(n, TextureFormat.RGBA32, false) { name = "SHYansima" };
                for (int f = 0; f < 6; f++)
                {
                    var d = new Vector3[n * n];
                    for (int y = 0; y < n; y++)
                        for (int x = 0; x < n; x++)
                        {
                            float u = 2f * (x + 0.5f) / n - 1f, v = 2f * (y + 0.5f) / n - 1f;
                            Vector3 w;
                            switch (f)
                            {
                                case 0: w = new Vector3(1, -v, -u); break;
                                case 1: w = new Vector3(-1, -v, u); break;
                                case 2: w = new Vector3(u, 1, v); break;
                                case 3: w = new Vector3(u, -1, -v); break;
                                case 4: w = new Vector3(u, -v, 1); break;
                                default: w = new Vector3(-u, -v, -1); break;
                            }
                            d[y * n + x] = w.normalized;
                        }
                    YuzYon[f] = d;
                }
            }
            var renk = new Color[n * n];
            for (int f = 0; f < 6; f++)
            {
                sonSh.Evaluate(YuzYon[f], renk);
                for (int i = 0; i < renk.Length; i++)
                {
                    var c = renk[i]; c.r = Mathf.Clamp01(c.r * 0.9f); c.g = Mathf.Clamp01(c.g * 0.9f); c.b = Mathf.Clamp01(c.b * 0.9f); c.a = 1f;
                    renk[i] = c;
                }
                yansimaKupu.SetPixels(renk, (CubemapFace)f);
            }
            yansimaKupu.Apply(false);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = yansimaKupu;
            RenderSettings.reflectionIntensity = 1f;
        }
    }
}
