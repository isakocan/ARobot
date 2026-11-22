// AR yaratiklarinin zemine dusen yumusak golgesi: dortgen uzerinde merkezden kenara solan koyu leke. Yonu ve uzunlugu
// OrtamIsigi'nin buldugu baskin isik yonune gore C#'ta (RobotAR) ayarlanir, koyulugu isigin yonluluguna gore.
// Resources icinde: derlemede ayiklanmasin, RobotAR Shader.Find ile bulur.
Shader "ARaki/BlobShadow"
{
    Properties
    {
        _BaseColor ("Renk (a = koyuluk)", Color) = (0, 0, 0, 0.4)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
            CBUFFER_END

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
                float r = length(i.uv - 0.5) * 2.0;
                float a = 1.0 - smoothstep(0.25, 1.0, r);
                return half4(_BaseColor.rgb, _BaseColor.a * a * a);
            }
            ENDHLSL
        }
    }
}
