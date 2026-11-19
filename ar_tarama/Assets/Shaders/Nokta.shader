Shader "ArTarama/Nokta"
{
    Properties { _Boyut("Nokta boyu", Float) = 7 }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Boyut;
            // genel: Harita3B ve ArApp tarafindan ayarlanir
            float _ArZeminY;     // zemin yuksekligi (dunya y)
            float _NoktaMod;     // 0 = AR gorunumu (ince renk), 1 = harita (yukseklik rengi)
            float _NoktaBoyut;   // harita modunda nokta boyu
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float psize : PSIZE; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 w = mul(unity_ObjectToWorld, v.vertex).xyz;
                float t = saturate((w.y - _ArZeminY) / 2.8);
                // AR: zeminde turkuaz, yukarda kirik beyaz
                fixed3 a = lerp(fixed3(0.27, 0.85, 0.75), fixed3(0.93, 0.95, 0.92), t);
                // harita: mavi - turkuaz - kehribar
                fixed3 c0 = fixed3(0.25, 0.45, 1.0), c1 = fixed3(0.26, 0.85, 0.75), c2 = fixed3(1.0, 0.76, 0.30);
                fixed3 b = t < 0.5 ? lerp(c0, c1, t * 2.0) : lerp(c1, c2, (t - 0.5) * 2.0);
                o.color = fixed4(lerp(a, b, _NoktaMod), 1);
                o.psize = lerp(_Boyut, _NoktaBoyut, _NoktaMod);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target { return i.color; }
            ENDCG
        }
    }
}
