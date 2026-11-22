// AR yaratiklarinin yumusak bindirilmesi: yaratiklar ayri bir kamerayla dusuk cozunurluklu, saydam arka planli bir dokuya
// cizilir (RobotAR), bu gölgelendirici o dokuyu ekrana hafif bulaniklastirarak (5 ornek) on carpimli alfa ile koyar.
// 360 goruntu yumusak, nesneler keskin olunca yapistirilmis gibi duruyordu (22 Kasim denemesi).
// Resources icinde: derlemede ayiklanmasin, RobotAR Shader.Find ile bulur.
Shader "ARaki/SoftComposite"
{
    Properties
    {
        _MainTex ("Doku", 2D) = "black" {}
        _Soft ("Yumusaklik (doku pikseli)", Float) = 1.0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Soft;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 d = _MainTex_TexelSize.xy * _Soft;
                // dokuda arka plan (0,0,0,0): renkler zaten alfa ile carpilmis kabul edilir
                fixed4 c = tex2D(_MainTex, i.uv) * 0.4;
                c += tex2D(_MainTex, i.uv + float2(d.x, d.y)) * 0.15;
                c += tex2D(_MainTex, i.uv + float2(-d.x, d.y)) * 0.15;
                c += tex2D(_MainTex, i.uv + float2(d.x, -d.y)) * 0.15;
                c += tex2D(_MainTex, i.uv + float2(-d.x, -d.y)) * 0.15;
                return c * i.color.a;
            }
            ENDCG
        }
    }
}
