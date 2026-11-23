// Robot Dev yaratiklari (24 Kasim): yumusak, hafif isikli opak govde + zemine temas golgesi. Resources icinde: derlemede
// ayiklanmasin (RobotDev Shader.Find ile bulur). Cikti on carpimli alfa (ARaki/SoftComposite boyle bekliyor):
// govde alfa 1, golge (0,0,0,a).
Shader "ArTarama/RobotYaratik"
{
    Properties
    {
        _Color ("Renk", Color) = (1, 0.6, 0.25, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 v : TEXCOORD1; };
            v2f vert(appdata i)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(i.vertex);
                o.n = UnityObjectToWorldNormal(i.normal);
                o.v = normalize(_WorldSpaceCameraPos - mul(unity_ObjectToWorld, i.vertex).xyz);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.n);
                // oda isigi: ustten, hafif onden; ortam isigi yuksek (360 goruntu genelde yumusak aydinlik)
                float l = saturate(dot(n, normalize(float3(0.3, 1.0, -0.4)))) * 0.55 + 0.5;
                float kenar = pow(1.0 - saturate(dot(n, normalize(i.v))), 3.0) * 0.25;
                return fixed4(_Color.rgb * l + kenar, 1);
            }
            ENDCG
        }
    }
}
