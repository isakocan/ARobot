// Duvar ve tavanların temaya göre "gökyüzü / arka plan" olarak çizilmesi. Işıksız, yalnızca bakış yönüne bağlıdır
// (sonsuz uzaklıkta durur): duvar kapı gibi görünür, ardında dünya vardır.
//   _Mode 0 = Safari: yaprak tavan, ışık huzmeleri, sisli orman, ateş böcekleri
//   _Mode 1 = Uzay:   yıldızlar, bulutsu, samanyolu, gezegen ve ay
//   _Mode 2 = Gizem:  karanlık damask duvar kağıdı, mum ışığı, sis (duvara bağlı, bakış yönünden bağımsız)
// Gerçek robotta bu görünüm, görüntüde zemin/eşya dışında kalan piksellere bir bölütleme modeliyle uygulanır.
Shader "ARaki/Sky"
{
    Properties
    {
        _Mode ("Tema", Float) = 1
        _ColA ("Renk A", Color) = (0.1, 0.1, 0.3, 1)
        _ColB ("Renk B", Color) = (0.02, 0.02, 0.1, 1)
        _ColC ("Renk C", Color) = (0.6, 0.2, 0.7, 1)
        _Intensity ("Parlaklık", Range(0, 3)) = 1
        _Ceiling ("Tavan (1) / Duvar (0)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Sky"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Mode;
                float4 _ColA;
                float4 _ColB;
                float4 _ColC;
                float _Intensity;
                float _Ceiling;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            float hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float3 hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            float vnoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = hash13(i);
                float n100 = hash13(i + float3(1, 0, 0));
                float n010 = hash13(i + float3(0, 1, 0));
                float n110 = hash13(i + float3(1, 1, 0));
                float n001 = hash13(i + float3(0, 0, 1));
                float n101 = hash13(i + float3(1, 0, 1));
                float n011 = hash13(i + float3(0, 1, 1));
                float n111 = hash13(i + float3(1, 1, 1));
                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                            lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            float fbm(float3 p)
            {
                float v = 0.0, a = 0.5;
                for (int k = 0; k < 4; k++) { v += a * vnoise(p); p = p * 2.03 + 11.7; a *= 0.5; }
                return v;
            }

            // ---------------------------------------------------------------- uzay
            float3 SpaceSky(float3 d)
            {
                float t = _Time.y;
                float up = d.y * 0.5 + 0.5;
                float3 col = lerp(_ColB.rgb, _ColA.rgb, pow(up, 1.4));

                // bulutsu
                float neb = fbm(d * 2.4 + float3(0, 0, t * 0.01));
                float neb2 = fbm(d * 4.8 + 17.0);
                col += _ColC.rgb * smoothstep(0.45, 0.85, neb) * 0.55;
                col += _ColA.rgb * smoothstep(0.5, 0.9, neb2) * 0.4;

                // samanyolu şeridi
                float3 bandNormal = normalize(float3(0.35, 0.8, 0.45));
                float band = exp(-pow(dot(d, bandNormal) * 3.2, 2.0));
                col += float3(0.85, 0.8, 1.0) * band * (0.15 + 0.5 * fbm(d * 7.0 + 3.0)) * 0.6;

                // yıldızlar: 3 katman
                for (int layer = 0; layer < 3; layer++)
                {
                    float scale = 55.0 + layer * 55.0;
                    float3 cell = floor(d * scale);
                    float3 rnd = hash33(cell + layer * 7.1);
                    float3 local = frac(d * scale) - (0.2 + 0.6 * rnd);
                    float dist = length(local);
                    float present = step(0.93 - layer * 0.01, rnd.x);
                    float twinkle = 0.65 + 0.35 * sin(t * (1.5 + rnd.y * 3.0) + rnd.z * 40.0);
                    float size = 0.12 + 0.12 * rnd.y;
                    float s = smoothstep(size, 0.0, dist) * present * twinkle;
                    float3 tint = lerp(float3(0.8, 0.9, 1.0), float3(1.0, 0.85, 0.7), rnd.z);
                    col += tint * s * (1.2 + 1.5 * rnd.x);
                }

                // gezegen (halkalı) ve ay
                float3 pdir = normalize(float3(-0.55, 0.28, 0.78));
                float pa = dot(d, pdir);
                float pr = 0.22;
                if (pa > cos(pr))
                {
                    float3 side = normalize(cross(pdir, float3(0, 1, 0)));
                    float3 upv = cross(side, pdir);
                    float2 pc = float2(dot(d - pdir * pa, side), dot(d - pdir * pa, upv)) / sin(pr);
                    float rr = length(pc);
                    float3 nrm = float3(pc, sqrt(saturate(1.0 - rr * rr)));
                    float3 ldir = normalize(float3(0.6, 0.5, 0.65));
                    float lit = saturate(dot(nrm, ldir)) * 0.9 + 0.1;
                    float bands = 0.5 + 0.5 * sin(pc.y * 14.0 + fbm(float3(pc * 3.0, 1.0)) * 3.0);
                    float3 pcol = lerp(_ColC.rgb * 0.8, float3(0.95, 0.65, 0.4), bands);
                    float atmos = pow(1.0 - nrm.z, 3.0);
                    float3 planet = pcol * lit + _ColA.rgb * atmos * 1.5;
                    col = lerp(col, planet, smoothstep(1.0, 0.97, rr));
                }
                // halka
                {
                    float3 side = normalize(cross(pdir, float3(0, 1, 0)));
                    float3 upv = cross(side, pdir);
                    float3 rel = d - pdir * pa;
                    float2 rc = float2(dot(rel, side), dot(rel, upv)) / sin(pr);
                    rc.y *= 3.2;
                    float rl = length(rc);
                    float ring = smoothstep(1.35, 1.5, rl) * smoothstep(2.15, 2.0, rl) * step(0.0, pa);
                    float behind = step(0.0, rc.y) ; // üst yarı gezegenin arkasında
                    float occl = (pa > cos(pr) && length(float2(dot(rel, side), dot(rel, upv)) / sin(pr)) < 1.0) ? (rc.y > 0 ? 0.0 : 1.0) : 1.0;
                    col += float3(0.9, 0.8, 0.65) * ring * 0.55 * occl;
                }
                float3 mdir = normalize(float3(0.7, 0.4, -0.55));
                float ma = dot(d, mdir);
                col = lerp(col, float3(0.8, 0.82, 0.9) * (0.7 + 0.3 * vnoise(d * 80.0)), smoothstep(cos(0.035), cos(0.03), ma));
                return col;
            }

            // ---------------------------------------------------------------- orman
            float3 SafariSky(float3 d)
            {
                float t = _Time.y;
                float up = saturate(d.y * 0.5 + 0.5);
                // sisli orman: ufukta doğru açık, yukarı doğru koyu yeşil
                float3 fog = _ColB.rgb;
                float3 col = lerp(fog, _ColA.rgb, smoothstep(0.35, 0.9, up));

                // tavan yaprakları ve boşluklar
                float canopyAmt = smoothstep(0.55, 0.85, up);
                float2 uv = d.xz / (abs(d.y) + 0.55);
                float leaf = fbm(float3(uv * 3.2, t * 0.03));
                float leaf2 = fbm(float3(uv * 7.5 + 4.0, t * 0.05));
                float holes = smoothstep(0.52, 0.66, leaf + 0.15 * leaf2);
                float3 canopyCol = lerp(_ColA.rgb * 0.45, _ColC.rgb, leaf2);
                float3 skyHole = float3(1.0, 0.95, 0.75);
                col = lerp(col, lerp(canopyCol, skyHole, holes), canopyAmt);

                // yatay: gövdeler (azimuta göre dikey şeritler, iki katman)
                float az = atan2(d.x, d.z);
                for (int layer = 0; layer < 3; layer++)
                {
                    float freq = 7.0 + layer * 5.0;
                    float cell = floor(az * freq + layer * 3.1);
                    float r = frac(sin(cell * 91.7 + layer * 13.0) * 4375.5);
                    float width = 0.05 + 0.05 * r;
                    float center = (cell + 0.5 + (r - 0.5) * 0.4) / freq - layer * 3.1 / freq;
                    float dd = abs(az - center);
                    float trunk = smoothstep(width, width * 0.6, dd) * step(0.35, r) * smoothstep(0.62, 0.35, up);
                    float3 tc = lerp(_ColA.rgb * 0.25, fog, 0.25 + layer * 0.25);
                    col = lerp(col, tc, trunk * 0.9);
                }

                // ışık huzmeleri
                float shaft = pow(saturate(sin(az * 5.0 + t * 0.05) * 0.5 + 0.5), 6.0) * smoothstep(0.3, 0.8, up) * 0.35;
                col += float3(1.0, 0.9, 0.6) * shaft;

                // ateş böcekleri
                float3 cell = floor(d * 40.0);
                float3 rnd = hash33(cell);
                float3 loc = frac(d * 40.0) - (0.2 + 0.6 * rnd);
                float fly = smoothstep(0.1, 0.0, length(loc)) * step(0.985, rnd.x) * (0.5 + 0.5 * sin(t * 2.0 + rnd.y * 30.0));
                col += float3(1.0, 0.95, 0.4) * fly * 1.6;
                return col;
            }

            // ---------------------------------------------------------------- gizem (duvar kağıdı)
            float3 MysteryWall(float3 pos, float3 nrm, float3 viewDir)
            {
                float t = _Time.y;
                float3 an = abs(nrm);
                float2 uv = an.x > an.z ? pos.zy : pos.xy;
                if (an.y > 0.8) uv = pos.xz;          // tavan
                uv *= 3.4;
                float2 g = frac(uv) - 0.5;
                float d1 = abs(g.x) + abs(g.y);                       // elmas
                float lattice = smoothstep(0.03, 0.0, abs(d1 - 0.42));
                float dots = smoothstep(0.1, 0.06, length(g));
                float orn = smoothstep(0.06, 0.0, abs(length(g) - 0.18)) * 0.8;
                float3 paper = lerp(_ColB.rgb, _ColA.rgb, 0.5 + 0.5 * vnoise(float3(pos.xz * 3.0, pos.y * 2.0)));
                float3 gold = _ColC.rgb;
                float3 col = paper + gold * (lattice * 0.35 + dots * 0.22 + orn * 0.2);
                // mum ışığı: yükseklik ve titreme
                float flicker = 0.85 + 0.15 * sin(t * 9.0) * sin(t * 3.7);
                float glow = smoothstep(2.6, 0.4, pos.y) * flicker;
                col *= 0.35 + 0.9 * glow;
                // zeminde sis
                float fog = smoothstep(0.7, 0.0, pos.y) * (0.5 + 0.5 * fbm(float3(pos.xz * 1.6, t * 0.05)));
                col = lerp(col, float3(0.18, 0.14, 0.22), fog * 0.5);
                return col;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.positionWS - _WorldSpaceCameraPos);
                float3 col;
                if (_Mode < 0.5) col = SafariSky(d);
                else if (_Mode < 1.5) col = SpaceSky(d);
                else col = MysteryWall(i.positionWS, normalize(i.normalWS), d);
                return half4(col * _Intensity, 1.0);
            }
            ENDHLSL
        }
    }
}
