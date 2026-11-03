// Ricoh Theta S'in çift balık gözü karesini (1280x720) içinden bakılan küreye kaplar.
// Kürenin her noktası robota göre bir yöndür (+Z ileri, +X sağ, +Y yukarı); o yön kameranın takılış
// açısıyla kamera eksenine çevrilir ve iki lensten hangisi görüyorsa oradan örneklenir.
// Lens ölçüleri sürüş sayfasındakiyle aynıdır (robot/jetson/surus_koprusu/surus.html, tools/lens_ayarla.py):
// merkez, ölçek (piksel/radyan) ve 2. lensin küçük dönmesi karelerden bulundu. İki lens 90° çizgisinde
// ±_Band derece içinde yumuşakça karışır, lens kenarındaki karanlık halka (_RMax ötesi) kullanılmaz.
Shader "ARaki/DualFisheye"
{
    Properties
    {
        _MainTex ("Çift balık gözü kare", 2D) = "black" {}
        _Lens1 ("Lens 1: merkez x, y (px), ölçek (px/rad)", Vector) = (322.42, 319.04, 180.87, 0)
        _Lens2 ("Lens 2: merkez x, y (px), ölçek (px/rad)", Vector) = (956.67, 320.15, 180.30, 0)
        _FrameSize ("Kare boyutu (px)", Vector) = (1280, 720, 0, 0)
        _RMax ("Kullanılan en büyük yarıçap (px)", Float) = 306
        _Band ("Karışım bandı (derece)", Float) = 6
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Background" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _Lens1;
            float4 _Lens2;
            float4 _FrameSize;
            float _RMax;
            float _Band;
            CBUFFER_END

            // C# tarafından verilir: robot → kamera (takılış açısı) ve 2. lensin dönmesi
            float4x4 _Mount;
            float4x4 _Lens2Rotation;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float3 Sample(float3 c, float3 lens)
            {
                float theta = acos(clamp(c.z, -1.0, 1.0));
                float2 side = c.xy / max(length(c.xy), 1e-6);
                float2 px = lens.xy + float2(side.x, -side.y) * theta * lens.z;
                // Görüntü pikselleri yukarıdan aşağı sayılır, Unity dokusu aşağıdan yukarı
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(px.x / _FrameSize.x, 1.0 - px.y / _FrameSize.y)).rgb;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(mul((float3x3)_Mount, normalize(i.dir)));
                float3 c2 = mul((float3x3)_Lens2Rotation, float3(-d.x, d.y, -d.z));
                float t1 = acos(clamp(d.z, -1.0, 1.0));
                float t2 = acos(clamp(c2.z, -1.0, 1.0));
                float x = saturate((degrees(t1) - (90.0 - _Band)) / (2.0 * _Band));
                float w1 = t1 * _Lens1.z > _RMax ? 0.0 : 1.0 - x * x * (3.0 - 2.0 * x);
                float w2 = t2 * _Lens2.z > _RMax ? 0.0 : 1.0 - w1;
                float3 color = 0;
                if (w1 > 0.0) color += w1 * Sample(d, _Lens1.xyz);
                if (w2 > 0.0) color += w2 * Sample(c2, _Lens2.xyz);
                return half4(color / max(w1 + w2, 1e-4), 1.0);
            }
            ENDHLSL
        }
    }
}
