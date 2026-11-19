Shader "ArTarama/Duzlem"
{
    Properties
    {
        _Color("Renk", Color) = (0.25, 0.85, 0.78, 0.2)
        _Izgara("Izgara sikligi", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            float _Izgara;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv * _Izgara;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float2 f = fwidth(i.uv);
                float2 g = abs(frac(i.uv - 0.5) - 0.5) / max(f, 0.0001);
                float cizgi = 1.0 - saturate(min(g.x, g.y));
                fixed4 c = _Color;
                c.rgb = lerp(c.rgb, fixed3(1,1,1), cizgi * 0.35);
                c.a = saturate(_Color.a * (0.55 + 2.2 * cizgi));
                return c;
            }
            ENDCG
        }
    }
}
