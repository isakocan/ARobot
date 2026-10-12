// AR nesneleri için ışıksız "oyuncak" gölgelendirici: sabit bir ışık yönüne göre 3 bantlı gölge, kenar parlaması ve
// dışarıdan sürülen parlama (_Glow). AR katmanı odanın gerçek ışıklarının dışında durduğu için kendi ışığını taşır.
// Hologram'dan farkı: opak (ya da isteğe bağlı yarı saydam), hacimli ve zemine "oturmuş" görünür.
Shader "ARaki/Toon"
{
    Properties
    {
        _BaseColor ("Ana renk", Color) = (1, 1, 1, 1)
        _ShadeColor ("Gölge rengi", Color) = (0.35, 0.35, 0.5, 1)
        [HDR] _RimColor ("Kenar rengi", Color) = (1, 1, 1, 1)
        _RimPower ("Kenar keskinliği", Range(0.5, 8)) = 3
        _RimStrength ("Kenar gücü", Range(0, 2)) = 0.5
        [HDR] _EmissionColor ("Işık rengi", Color) = (0, 0, 0, 1)
        _Glow ("Parlama", Float) = 0.3
        _LightDir ("Işık yönü", Vector) = (-0.45, 0.8, -0.35, 0)
        [HideInInspector] _SrcBlend ("Src", Float) = 1
        [HideInInspector] _DstBlend ("Dst", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Toon"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _ShadeColor;
                float4 _RimColor;
                float _RimPower;
                float _RimStrength;
                float4 _EmissionColor;
                float _Glow;
                float4 _LightDir;
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
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.color = input.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float ndl = dot(n, normalize(_LightDir.xyz));
                float band = smoothstep(-0.15, 0.1, ndl) * 0.6 + smoothstep(0.25, 0.5, ndl) * 0.4;
                float3 baseCol = _BaseColor.rgb * i.color.rgb;
                float3 col = lerp(_ShadeColor.rgb * baseCol * 1.4, baseCol, band);
                float rim = pow(1.0 - saturate(dot(n, v)), _RimPower) * _RimStrength;
                col += _RimColor.rgb * rim;
                col += _EmissionColor.rgb * _Glow;
                return half4(col, _BaseColor.a * i.color.a);
            }
            ENDHLSL
        }
    }
}
