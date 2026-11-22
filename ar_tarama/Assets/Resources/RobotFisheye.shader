// Robotun Ricoh Theta S cift balik gozu karesi (1280x720) -> yon. Robot uygulamasindaki ARaki/DualFisheye ile ayni lens
// modeli (merkez, olcek px/radyan, 2. lensin donmesi, takilis acisi _Mount), yerlesik render hattina uyarlandi.
// Gecisler: 0 = ekranda perspektif gorus (Dev ekrani), 1 = ufuk bandi (gorsel pusula, enlem -5..+31, gri),
// 2 = kusbakisi zemin (2 m x 2 m, kamera yerden _Yukseklik; zemin izi, gri).
// Resources icinde: derlemede ayiklanmasin, RobotDev Shader.Find ile bulur.
Shader "ArTarama/RobotFisheye"
{
    Properties
    {
        _MainTex ("Cift balik gozu kare", 2D) = "black" {}
        _Lens1 ("Lens 1", Vector) = (322.42, 319.04, 180.87, 0)
        _Lens2 ("Lens 2", Vector) = (956.67, 320.15, 180.30, 0)
        _FrameSize ("Kare boyutu", Vector) = (1280, 720, 0, 0)
        _RMax ("En buyuk yaricap", Float) = 306
        _Band ("Karisim bandi", Float) = 6
        _Yukseklik ("Kamera yuksekligi (m)", Float) = 0.17
        _KusR ("Kusbakisi yari genislik (m)", Float) = 1.0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _Lens1, _Lens2, _FrameSize;
        float _RMax, _Band, _Yukseklik, _KusR;
        float4x4 _Mount, _Lens2Rotation, _ViewRot;
        float4 _TanHalf;

        struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
        struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
        v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

        float3 Ornek(float3 c, float3 lens)
        {
            float theta = acos(clamp(c.z, -1.0, 1.0));
            float2 side = c.xy / max(length(c.xy), 1e-6);
            float2 px = lens.xy + float2(side.x, -side.y) * theta * lens.z;
            return tex2Dlod(_MainTex, float4(px.x / _FrameSize.x, 1.0 - px.y / _FrameSize.y, 0, 0)).rgb;
        }

        float3 Renk(float3 dirRobot)
        {
            float3 d = normalize(mul((float3x3)_Mount, dirRobot));
            float3 c2 = mul((float3x3)_Lens2Rotation, float3(-d.x, d.y, -d.z));
            float t1 = acos(clamp(d.z, -1.0, 1.0));
            float t2 = acos(clamp(c2.z, -1.0, 1.0));
            float x = saturate((degrees(t1) - (90.0 - _Band)) / (2.0 * _Band));
            float w1 = t1 * _Lens1.z > _RMax ? 0.0 : 1.0 - x * x * (3.0 - 2.0 * x);
            float w2 = t2 * _Lens2.z > _RMax ? 0.0 : 1.0 - w1;
            float3 color = 0;
            if (w1 > 0.0) color += w1 * Ornek(d, _Lens1.xyz);
            if (w2 > 0.0) color += w2 * Ornek(c2, _Lens2.xyz);
            return color / max(w1 + w2, 1e-4);
        }
        float Gri(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }
        ENDCG

        Pass
        {
            Name "Gorus"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag(v2f i) : SV_Target
            {
                float3 v = normalize(float3((i.uv.x * 2 - 1) * _TanHalf.x, (i.uv.y * 2 - 1) * _TanHalf.y, 1));
                return fixed4(Renk(mul((float3x3)_ViewRot, v)), 1);
            }
            ENDCG
        }
        Pass
        {
            Name "Band"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag(v2f i) : SV_Target
            {
                float yaw = (i.uv.x - 0.5) * 6.2831853, lat = radians(-5.0 + i.uv.y * 36.0);
                float l = Gri(Renk(float3(sin(yaw) * cos(lat), sin(lat), cos(yaw) * cos(lat))));
                return fixed4(l, l, l, 1);
            }
            ENDCG
        }
        Pass
        {
            Name "Kus"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag(v2f i) : SV_Target
            {
                // dokunun alt satiri robotun arkasi (-z), ust satiri onu (+z); sol -x, sag +x
                float x = (i.uv.x * 2 - 1) * _KusR, z = (i.uv.y * 2 - 1) * _KusR;
                float l = Gri(Renk(normalize(float3(x, -_Yukseklik, z))));
                return fixed4(l, l, l, 1);
            }
            ENDCG
        }
    }
}
