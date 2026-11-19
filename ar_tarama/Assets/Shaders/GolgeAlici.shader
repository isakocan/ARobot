Shader "ArTarama/GolgeAlici"
{
    // Yalnizca golge alan seffaf yuzey: golge alanlarda siyah, alfa = (1-atten)*0.5
    Properties { _Yogunluk("Yogunluk", Range(0,1)) = 0.5 }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Geometry+2" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"
            float _Yogunluk;
            struct v2f { float4 pos : SV_POSITION; SHADOW_COORDS(0) };
            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                TRANSFER_SHADOW(o);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed atten = SHADOW_ATTENUATION(i);
                return fixed4(0, 0, 0, (1.0 - atten) * _Yogunluk);
            }
            ENDCG
        }
    }
    Fallback Off
}
