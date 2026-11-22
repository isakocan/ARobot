Shader "ArTarama/ZeminBoya"
{
    // Acik zemin boyasi, ARCore duzlem gorunumune benzer: yari saydam renk + dunyaya sabit ince izgara ve noktalar.
    // Tepe rengi alfasi kenara dogru solar (ZeminCizim yumusatilmis alandan verir), boylece kenar yumusak ve temiz.
    Properties
    {
        _Izgara("Izgara araligi (m)", Float) = 0.25
    }
    SubShader
    {
        Tags { "Queue"="Transparent+3" "RenderType"="Transparent" "IgnoreProjector"="True" }
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
            float _Izgara;
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 w : TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.color = v.color;
                o.w = mul(unity_ObjectToWorld, v.vertex).xz / _Izgara;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float2 f = max(fwidth(i.w), 1e-4);
                float2 g = abs(frac(i.w - 0.5) - 0.5) / f;              // cizgiye piksel uzakligi
                float cizgi = 1.0 - saturate(min(g.x, g.y) - 0.3);
                float2 d = (frac(i.w) - 0.5) / f;                        // hucre ortasinda nokta
                float nokta = 1.0 - saturate(length(d) - 1.6);
                // uzakta izgara titremesin: piksel basina birden fazla hucre dusunce sonumlenir
                float uzak = saturate(1.6 - max(f.x, f.y) * 6.0);
                fixed4 c = i.color;
                float vurgu = max(cizgi * 0.55, nokta * 0.8) * uzak;
                c.rgb = lerp(c.rgb, fixed3(1, 1, 1), vurgu);
                c.a = i.color.a * (0.55 + 0.45 * vurgu);
                return c;
            }
            ENDCG
        }
    }
}
