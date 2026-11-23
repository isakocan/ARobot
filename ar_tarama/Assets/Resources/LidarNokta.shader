// LIDAR gorunumu (24 Kasim): doluluk haritasinin kesin dolu kupleri kamera goruntusunun ustunde nokta olarak.
// Renk tepe renginden (LidarCizim: zemin yesil, engel turuncu, uzun mor, zayif gri); nokta boyu uzakliga gore.
Shader "ArTarama/LidarNokta"
{
    SubShader
    {
        Tags { "Queue" = "Transparent+5" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float psize : PSIZE; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 w = mul(unity_ObjectToWorld, v.vertex).xyz;
                float d = max(0.2, distance(w, _WorldSpaceCameraPos));
                o.psize = clamp(22.0 / d, 3.0, 16.0);
                o.color = v.color;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target { return i.color; }
            ENDCG
        }
    }
}
