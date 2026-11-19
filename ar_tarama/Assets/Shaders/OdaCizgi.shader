Shader "ArTarama/OdaCizgi"
{
    // Kamera yonlu cizgi seridi: ekran uzayinda sabit kalinlik + yumusak parilti, uzaklikla solar.
    // Girdi: POSITION = bu ucun konumu, TEXCOORD1 = diger ucun konumu, TEXCOORD0.x = genislik carpani, TEXCOORD0.y = serit tarafi (-1/+1), COLOR = renk*alfa.
    Properties
    {
        _Genislik("Cizgi genisligi (px)", Float) = 3
        _Parilti("Parilti genisligi (px)", Float) = 11
        _Yakin("Solma baslangici (m)", Float) = 3
        _Uzak("Solma bitisi (m)", Float) = 10
    }
    SubShader
    {
        Tags { "Queue"="Transparent+6" "RenderType"="Transparent" "IgnoreProjector"="True" }
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
            float _Genislik, _Parilti, _Yakin, _Uzak;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float3 diger : TEXCOORD1; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float lp : TEXCOORD0; float cek : TEXCOORD1; float4 color : COLOR; };
            v2f vert(appdata v)
            {
                v2f o;
                float4 c0 = UnityObjectToClipPos(float4(v.vertex.xyz, 1));
                float4 c1 = UnityObjectToClipPos(float4(v.diger, 1));
                float2 px = _ScreenParams.xy;
                float2 d = (c1.xy / c1.w - c0.xy / c0.w) * px;
                float l = length(d);
                d = l > 1e-4 ? d / l : float2(1, 0);
                float2 nrm = float2(-d.y, d.x);
                float cek = _Genislik * 0.5 * v.uv.x;
                float yari = cek + _Parilti;
                float2 ndc = c0.xy / c0.w + (nrm * v.uv.y * yari - d * yari) * 2.0 / px;
                o.pos = float4(ndc * c0.w, c0.z, c0.w);
                o.lp = v.uv.y * yari; o.cek = cek;
                float uzak = lerp(1.0, 0.3, saturate((c0.w - _Yakin) / max(_Uzak - _Yakin, 0.01)));
                o.color = float4(v.color.rgb, v.color.a * uzak);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float dist = abs(i.lp);
                float yari = i.cek;
                float cekirdek = 1.0 - smoothstep(yari - 0.8, yari + 0.8, dist);
                float g = saturate(1.0 - (dist - yari) / _Parilti);
                float parilti = 0.30 * g * g;
                float a = max(cekirdek, parilti) * i.color.a;
                return fixed4(i.color.rgb, a);
            }
            ENDCG
        }
    }
}
