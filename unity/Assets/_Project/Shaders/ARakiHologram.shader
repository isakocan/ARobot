// AR hologram yüzeyi: yarı saydam, kenarı (fresnel) parlayan, hareketli tarama çizgili ve ara sıra titreyen.
// Işık ekler (additive), bu yüzden gerçek görüntünün üstünde "ışıkla çizilmiş" gibi durur.
// Köşe rengi (vertex color) alfa'sı kenara doğru sönen konileri ve yelpazeleri çizmek için kullanılır.
Shader "ARaki/Hologram"
{
    Properties
    {
        [HDR] _BaseColor ("Renk", Color) = (0.37, 0.89, 1.0, 1.0)
        _BaseAlpha ("Taban saydamlık", Range(0, 1)) = 0.14
        _FresnelPower ("Kenar keskinliği", Range(0.5, 8)) = 2.4
        _FresnelBoost ("Kenar parlaklığı", Range(0, 4)) = 1.6
        _ScanDensity ("Tarama çizgisi sıklığı", Float) = 70
        _ScanSpeed ("Tarama hızı", Float) = 0.5
        _ScanStrength ("Tarama gücü", Range(0, 1)) = 0.4
        _Flicker ("Titreme", Range(0, 1)) = 0.12
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Hologram"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _BaseAlpha;
                float _FresnelPower;
                float _FresnelBoost;
                float _ScanDensity;
                float _ScanSpeed;
                float _ScanStrength;
                float _Flicker;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 normal = normalize(input.normalWS);
                float rim = pow(1.0 - saturate(abs(dot(normal, viewDir))), _FresnelPower);

                // yukarı kayan tarama çizgileri
                float scan = 0.5 + 0.5 * sin(input.positionWS.y * _ScanDensity - _Time.y * _ScanSpeed * 6.2831853);
                float scanMask = lerp(1.0, 0.35 + 0.65 * scan, _ScanStrength);

                // ara sıra kısa parlak/sönük titreme
                float tick = floor(_Time.y * 22.0);
                float noise = frac(sin(tick * 12.9898) * 43758.5453);
                float flicker = lerp(1.0, 0.55 + 0.45 * noise, _Flicker * step(0.93, frac(sin(tick * 3.1) * 9371.0)));

                float alpha = (_BaseAlpha + rim * _FresnelBoost) * scanMask * flicker * input.color.a;
                float3 rgb = _BaseColor.rgb * input.color.rgb * (0.55 + rim * _FresnelBoost);
                return half4(rgb, saturate(alpha) * _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
