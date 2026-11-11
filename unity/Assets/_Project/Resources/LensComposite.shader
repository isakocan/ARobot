// "Görünen kısım" görüntüsünü birleştirir: tüm çift balık gözü karenin küçüğü (_Base, 640x360) büyütülüp zemine
// konur, baktığın lensin keskin karesi (_Win) kendi yerine (_Rect: u0, v0, u1, v1) yapıştırılır. Çıktı 1280x720
// tam kare gibidir; küreyi kaplayan DualFisheye gölgelendiricisi farkı bilmez. _Rect boşsa (u1 <= u0) yalnızca zemin.
// Resources içinde: derlemede ayıklanmasın, RobotVideo Resources.Load ile bulur.
Shader "ARaki/LensComposite"
{
    Properties
    {
        _Base ("Küçük tam kare", 2D) = "black" {}
        _Win ("Lens karesi", 2D) = "black" {}
        _Rect ("Lensin yeri (u0, v0, u1, v1)", Vector) = (0, 0, 0, 0)
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

            TEXTURE2D(_Base); SAMPLER(sampler_Base);
            TEXTURE2D(_Win); SAMPLER(sampler_Win);
            float4 _Rect;

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
                float2 uv = i.uv;
                bool inside = _Rect.z > _Rect.x && uv.x >= _Rect.x && uv.x <= _Rect.z && uv.y >= _Rect.y && uv.y <= _Rect.w;
                if (inside)
                    return SAMPLE_TEXTURE2D(_Win, sampler_Win, (uv - _Rect.xy) / (_Rect.zw - _Rect.xy));
                return SAMPLE_TEXTURE2D(_Base, sampler_Base, uv);
            }
            ENDHLSL
        }
    }
}
