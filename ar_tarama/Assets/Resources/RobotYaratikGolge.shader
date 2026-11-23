// Yaratigin zemine bastigini gosteren yumusak temas golgesi (yatay dortgen, merkezden kenara solan). On carpimli alfa.
Shader "ArTarama/RobotYaratikGolge"
{
    Properties
    {
        _Koyu ("Koyuluk", Float) = 0.45
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        ZWrite Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Koyu;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata i) { v2f o; o.pos = UnityObjectToClipPos(i.vertex); o.uv = i.uv; return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                float r = length(i.uv * 2 - 1);
                float a = _Koyu * saturate(1 - r) * saturate(1 - r);
                return fixed4(0, 0, 0, a);
            }
            ENDCG
        }
    }
}
