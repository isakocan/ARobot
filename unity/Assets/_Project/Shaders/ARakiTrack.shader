// AR yarış pisti: zemine serilmiş yarı saydam asfalt şeridi. Renkler köşe rengiyle gelir (asfalt, kerb, çizgiler).
// uv.x = pist boyunca metre, uv.y = 1 ise asfalt (üzerinde yarış yönünü gösteren ışık dalgası akar).
Shader "ARaki/Track"
{
    Properties
    {
        [HDR] _PulseColor ("Dalga rengi", Color) = (1.0, 0.72, 0.2, 1.0)
        _PulseSpeed ("Dalga hızı", Float) = 0.9
        _PulsePeriod ("Dalga aralığı (m)", Float) = 1.1
        _PulseStrength ("Dalga gücü", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Track"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _PulseColor;
                float _PulseSpeed;
                float _PulsePeriod;
                float _PulseStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // pist yönünde akan ışık dalgası: önü keskin, arkası yumuşakça sönen
                float phase = frac(input.uv.x / _PulsePeriod - _Time.y * _PulseSpeed);
                float wave = smoothstep(0.0, 0.04, phase) * (1.0 - smoothstep(0.04, 0.3, phase));
                float mask = input.uv.y * wave * _PulseStrength;
                float3 rgb = input.color.rgb + _PulseColor.rgb * mask;
                float alpha = saturate(input.color.a + mask * 0.35);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
