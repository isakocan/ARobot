Shader "ArTarama/Golge"
{
    Properties { _MainTex("Doku", 2D) = "white" {} _Color("Renk", Color) = (0,0,0,0.55) }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+5" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Offset -1, -1
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed4 _Color;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed a = tex2D(_MainTex, i.uv).a;
                return fixed4(_Color.rgb, _Color.a * a);
            }
            ENDCG
        }
    }
}
