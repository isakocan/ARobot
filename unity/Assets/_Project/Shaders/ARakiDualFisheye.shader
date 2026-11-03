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
        _Sharpen ("Keskinleştirme", Range(0, 1)) = 0.35
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
            float _Sharpen;
            float4 _MainTex_TexelSize;
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

            // Catmull-Rom bicubic, 9 çift doğrusal (bilinear) örnekle. Kaynak kare 3-4 kat büyütüldüğü için düz
            // çift doğrusal örnekleme bulanık görünür; bu, kenarları korur. _Sharpen ek bir hafif keskinleştirme.
            float3 Bicubic(float2 uv)
            {
                float2 size = _MainTex_TexelSize.zw;
                float2 p = uv * size;
                float2 t1 = floor(p - 0.5) + 0.5;
                float2 f = p - t1;
                float2 w0 = f * (-0.5 + f * (1.0 - 0.5 * f));
                float2 w1 = 1.0 + f * f * (-2.5 + 1.5 * f);
                float2 w2 = f * (0.5 + f * (2.0 - 1.5 * f));
                float2 w3 = f * f * (-0.5 + 0.5 * f);
                float2 w12 = w1 + w2;
                float2 o12 = w2 / w12;
                float2 t0 = (t1 - 1.0) / size, t3 = (t1 + 2.0) / size, t12 = (t1 + o12) / size;
                float3 c = 0;
                c += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(t0.x, t0.y)).rgb * w0.x * w0.y;
                c += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(t12.x, t0.y)).rgb * w12.x * w0.y;
                c += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(t3.x, t0.y)).rgb * w3.x * w0.y;
                c += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(t0.x, t12.y)).rgb * w0.x * w12.y;
                float3 mid = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(t12.x, t12.y)).rgb;
                c += mid * w12.x * w12.y;
                c += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(t3.x, t12.y)).rgb * w3.x * w12.y;
                c += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(t0.x, t3.y)).rgb * w0.x * w3.y;
                c += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(t12.x, t3.y)).rgb * w12.x * w3.y;
                c += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(t3.x, t3.y)).rgb * w3.x * w3.y;
                // hafif keskinleştirme: bicubic sonucu ile çift doğrusalın farkını biraz büyüt
                float3 lin = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
                return max(0, c + (c - lin) * _Sharpen * 2.0);
            }

            float3 Sample(float3 c, float3 lens)
            {
                float theta = acos(clamp(c.z, -1.0, 1.0));
                float2 side = c.xy / max(length(c.xy), 1e-6);
                float2 px = lens.xy + float2(side.x, -side.y) * theta * lens.z;
                // Görüntü pikselleri yukarıdan aşağı sayılır, Unity dokusu aşağıdan yukarı
                return Bicubic(float2(px.x / _FrameSize.x, 1.0 - px.y / _FrameSize.y));
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
