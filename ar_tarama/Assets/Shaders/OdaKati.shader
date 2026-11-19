Shader "ArTarama/OdaKati"
{
    // Mini oda modeli icin isiksiz duz golgeli katı yuzey: tepe rengi x sabit isik yonune gore yumusak golgelendirme.
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        ZWrite On
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; fixed3 color : COLOR; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 n = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                float3 L = normalize(float3(0.45, 0.80, -0.40));
                float d = dot(n, L) * 0.5 + 0.5;
                o.color = v.color.rgb * (0.60 + 0.40 * d);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target { return fixed4(i.color, 1); }
            ENDCG
        }
    }
}
