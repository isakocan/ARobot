// H.264'ten çözülen YUV 4:2:0 kareyi (üç ayrı düzlem: Y tam boy, U ve V yarım boy) RGB'ye çevirir.
// Renk dönüşümü BT.601, sınırlı aralık (Y 16-235): Jetson'un kodlayıcısının varsayılanı.
// Çıktı sRGB render dokusuna yazıldığı için sonuç doğrusal (linear) renge çevrilip verilir.
// Düzlem dokularının genişliği satır adımıdır (stride); _Scale.x / _Scale.z kullanılan kısmı seçer.
Shader "ARaki/YuvToRgb"
{
    Properties
    {
        _Y ("Y", 2D) = "black" {}
        _U ("U", 2D) = "gray" {}
        _V ("V", 2D) = "gray" {}
        _Scale ("Kullanılan oran (Y x, -, UV x, -)", Vector) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Y); SAMPLER(sampler_Y);
            TEXTURE2D(_U); SAMPLER(sampler_U);
            TEXTURE2D(_V); SAMPLER(sampler_V);
            float4 _Scale;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Çözücünün satırları yukarıdan aşağıdır, Unity dokusu aşağıdan yukarı: dikey çevir
                float2 uv = float2(i.uv.x, 1.0 - i.uv.y);
                float y = SAMPLE_TEXTURE2D(_Y, sampler_Y, float2(uv.x * _Scale.x, uv.y)).r;
                float u = SAMPLE_TEXTURE2D(_U, sampler_U, float2(uv.x * _Scale.z, uv.y)).r - 0.5;
                float v = SAMPLE_TEXTURE2D(_V, sampler_V, float2(uv.x * _Scale.z, uv.y)).r - 0.5;
                y = (y - 16.0 / 255.0) * (255.0 / 219.0);
                float3 c = float3(y + 1.596 * v, y - 0.392 * u - 0.813 * v, y + 2.017 * u);
                c = saturate(c);
                // sRGB → doğrusal (sRGB render dokusu yazarken geri çevirir)
                c = c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4);
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }
}
