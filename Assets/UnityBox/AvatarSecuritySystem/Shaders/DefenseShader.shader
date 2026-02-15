Shader "UnityBox/ASS_DefenseShader"
{
    Properties
    {
        _xA0 ("", 2D) = "white" {}
        _xA1 ("", 2D) = "bump" {}
        _xA2 ("", 2D) = "white" {}
        _xA3 ("", 2D) = "white" {}
        _xA4 ("", 2D) = "white" {}
        _xA5 ("", 2D) = "white" {}
        _xA6 ("", 2D) = "white" {}
        _xA7 ("", 2D) = "white" {}
        _xA8 ("", 2D) = "white" {}
        _xA9 ("", 2D) = "white" {}
        _xAA ("", 2D) = "white" {}
        _xAB ("", 2D) = "white" {}
        _xAC ("", 2D) = "white" {}
        _xAD ("", 2D) = "white" {}
        _xAE ("", 2D) = "white" {}
        _xAF ("", 2D) = "white" {}

        _xB0 ("", Float) = 32768
        _xB1 ("", Float) = 2000.0
        _xB2 ("", Float) = 20000.0
        _xB3 ("", Float) = 200.0
        _xB4 ("", Float) = 128.0
        _xB5 ("", Float) = 512.0
        _xB6 ("", Float) = 500.0
        _xB7 ("", Float) = 64.0
        _xB8 ("", Float) = 64.0
        _xB9 ("", Float) = 32.0
        _xBA ("", Color) = (1, 1, 1, 1)

        _xC0 ("", Float) = 1.0
        _xC1 ("", Float) = 1.0
        _xC2 ("", Float) = 5.0
        _xC3 ("", Float) = 5.0
        _xC4 ("", Float) = 32.0
        _xC5 ("", Float) = 100.0
        _xC6 ("", Float) = 50.0
        _xC7 ("", Float) = 512.0
        _xC8 ("", Float) = 256.0
        _xC9 ("", Float) = 1.0
        _xCA ("", Float) = 0.5
        _xCB ("", Float) = 1.0
        _xCC ("", Float) = 1.0
        _xCD ("", Float) = 1.0
        _xCE ("", Color) = (1, 0.5, 0.5, 1)
        _xCF ("", Float) = 2.0

        _xD0 ("", Float) = 1.0
        _xD1 ("", Float) = 1.0
        _xD2 ("", Float) = 128.0
        _xD3 ("", Float) = 0.001
        _xD4 ("", Float) = 1000.0
        _xD5 ("", Float) = 5.0
        _xD6 ("", Float) = 128.0
        _xD7 ("", Float) = 16.0
        _xD8 ("", Float) = 2000.0
        _xD9 ("", Float) = 32.0
        _xDA ("", Float) = 8.0
        _xDB ("", Float) = 32.0
        _xDC ("", Float) = 1.0
        _xDD ("", Float) = 1.0
        _xDE ("", Float) = 0.5
        _xDF ("", Float) = 20.0

        _xE0 ("", Float) = 1.0
        _xE1 ("", Float) = 1.0
        _xE2 ("", Float) = 5.0
        _xE3 ("", Float) = 1.0
        _xE4 ("", Float) = 0.001
        _xE5 ("", Float) = 5.0
        _xE6 ("", Float) = 5.0
        _xE7 ("", Float) = 5.0
        _xE8 ("", Float) = 10.0
        _xE9 ("", Float) = 5.0
        _xEA ("", Float) = 5.0
        _xEB ("", Float) = 5.0

        _xF0 ("", Float) = 1000.0
        _xF1 ("", Float) = 128.0
        _xF2 ("", Float) = 256.0
        _xF3 ("", Float) = 128.0
        _xF4 ("", Float) = 512.0
        _xF5 ("", Float) = 1024.0
        _xF6 ("", Float) = 128.0
        _xF7 ("", Float) = 512.0
        _xF8 ("", Float) = 64.0
        _xF9 ("", Float) = 128.0
        _xFA ("", Float) = 64.0
        _xFB ("", Float) = 256.0
        _xFC ("", Float) = 128.0
        _xFD ("", Float) = 32.0
        _xFE ("", Float) = 512.0
        _xFF ("", Float) = 4096.0

        _y00 ("", Float) = 2048.0
        _y01 ("", Float) = 1024.0
        _y02 ("", Float) = 512.0
        _y03 ("", Float) = 256.0
        _y04 ("", Float) = 128.0
        _y05 ("", Float) = 64.0
        _y06 ("", Float) = 1.0
        _y07 ("", Float) = 1.0
        _y08 ("", Float) = 1.0
        _y09 ("", Float) = 1.0
        _y0A ("", Float) = 4096.0
        _y0B ("", Float) = 1024.0
        _y0C ("", Float) = 512.0
        _y0D ("", Float) = 256.0
        _y0E ("", Color) = (0.2, 0.4, 0.8, 1)
        _y0F ("", Color) = (0.8, 0.2, 0.1, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode"="ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma target 5.0

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            Texture2D _xA0, _xA1, _xA2, _xA3, _xA4, _xA5, _xA6, _xA7;
            Texture2D _xA8, _xA9, _xAA, _xAB, _xAC, _xAD, _xAE, _xAF;
            SamplerState sampler_xA0;
            float4 _xA0_ST, _xA1_ST, _xA2_ST, _xA3_ST, _xA4_ST;
            float _xB0, _xB1, _xB2, _xB3;
            float _xB4, _xB5, _xB6;
            float _xB7, _xB8, _xB9;
            float4 _xBA;

            float _xC0, _xC1, _xC2;
            float _xC3, _xC4;
            float _xC5, _xC6;
            float _xC7, _xC8;
            float _xC9, _xCA;
            float _xCB, _xCC, _xCD;
            float4 _xCE;
            float _xCF;

            float _xD0, _xD1;
            float _xD2, _xD3, _xD4;
            float _xD5, _xD6;
            float _xD7, _xD8;
            float _xD9, _xDA;
            float _xDB, _xDC;
            float _xDD, _xDE;
            float _xDF;

            float _xE0, _xE1;
            float _xE2, _xE3, _xE4;
            float _xE5, _xE6, _xE7;
            float _xE8, _xE9, _xEA, _xEB;

            float _xF0, _xF1, _xF2, _xF3;
            float _xF4, _xF5, _xF6;
            float _xF7, _xF8, _xF9;
            float _xFA, _xFB, _xFC, _xFD;
            float _xFE, _xFF;

            float _y00, _y01, _y02, _y03, _y04, _y05;
            float _y06, _y07, _y08, _y09;
            float _y0A, _y0B, _y0C, _y0D;
            float4 _y0E, _y0F;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float3 tangent : TEXCOORD2;
                float3 binormal : TEXCOORD3;
                float3 worldPos : TEXCOORD4;
                float3 viewDir : TEXCOORD5;
                float2 uv2 : TEXCOORD6;
                float4 vertColor : TEXCOORD7;
                SHADOW_COORDS(8)
                UNITY_FOG_COORDS(9)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float _h(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float _h2(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(443.8975, 397.2973, 491.1871));
                p3 += dot(p3, p3.yzx + 19.19);
                return frac((p3.x + p3.y) * p3.z);
            }

            float _n(float3 x)
            {
                float3 p = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(lerp(_h(p), _h(p + float3(1,0,0)), f.x),
                         lerp(_h(p + float3(0,1,0)), _h(p + float3(1,1,0)), f.x), f.y),
                    lerp(lerp(_h(p + float3(0,0,1)), _h(p + float3(1,0,1)), f.x),
                         lerp(_h(p + float3(0,1,1)), _h(p + float3(1,1,1)), f.x), f.y),
                    f.z);
            }

            float _fb(float3 p, int o)
            {
                float v = 0.0;
                float a = 0.5;
                float f = 1.0;
                for (int i = 0; i < o; i++)
                {
                    v += a * _n(p * f);
                    f *= 2.17;
                    a *= 0.49;
                }
                return v;
            }

            float3 _r2h(float3 c)
            {
                float4 K = float4(0.0, -1.0/3.0, 2.0/3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            float3 _h2r(float3 c)
            {
                float4 K = float4(1.0, 2.0/3.0, 1.0/3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            float2 _cm(float2 a, float2 b) { return float2(a.x*b.x - a.y*b.y, a.x*b.y + a.y*b.x); }

            float _mb(float2 c, int mi)
            {
                float2 z = float2(0, 0);
                int i;
                for (i = 0; i < mi; i++)
                {
                    z = _cm(z, z) + c;
                    if (dot(z, z) > 4.0) break;
                }
                return float(i) / float(mi);
            }

            float _jl(float2 z, float2 c, int mi)
            {
                int i;
                for (i = 0; i < mi; i++)
                {
                    z = _cm(z, z) + c;
                    if (dot(z, z) > 4.0) break;
                }
                return float(i) / float(mi);
            }

            float _bs(float2 z, int mi)
            {
                float2 c = z;
                int i;
                for (i = 0; i < mi; i++)
                {
                    z = float2(z.x*z.x*z.x - 3.0*z.x*z.y*z.y, 3.0*z.x*z.x*z.y - z.y*z.y*z.y) + c;
                    if (dot(z, z) > 4.0) break;
                }
                return float(i) / float(mi);
            }

            float _tc(float2 z, int mi)
            {
                float2 c = z;
                int i;
                for (i = 0; i < mi; i++)
                {
                    float x2 = z.x * z.x;
                    float y2 = z.y * z.y;
                    float xy = z.x * z.y;
                    z = float2(x2*x2 - 6.0*x2*y2 + y2*y2, 4.0*z.x*z.y*(x2 - y2)) + c;
                    if (dot(z, z) > 256.0) break;
                }
                return float(i) / float(mi);
            }

            float3 _fn(float3 p, int it)
            {
                float3 v = float3(0, 0, 0);
                float a = 0.5;
                float f = 1.0;
                for (int i = 0; i < it; i++)
                {
                    float3 q = p * f + float3(sin(_Time.y * 0.1 + i * 0.2), cos(_Time.y * 0.15 + i * 0.3), sin(_Time.y * 0.2 + i * 0.4));
                    float n1 = sin(q.x) * cos(q.y) + sin(q.z) * cos(q.x);
                    float n2 = sin(q.y * 1.3) * cos(q.z * 0.7) + sin(q.x * 1.7);
                    float n3 = cos(q.z * 2.1) * sin(q.x * 0.9) + cos(q.y * 1.1);
                    v += a * float3(n1, n2, n3);
                    f *= 1.73;
                    a *= 0.47;
                }
                return v;
            }

            float3 _td(float2 uv, float t, int l)
            {
                float3 d = float3(0, 0, 0);
                for (int i = 0; i < l; i++)
                {
                    float f = pow(2.0, i);
                    float a = pow(0.5, i);
                    float3 np = float3(uv * f * _xD3, t * 0.1 + i * 0.2);
                    d += _fn(np, 16).x * a * float3(1, 1, 1);
                }
                return d * _xD4;
            }

            float3 _vc(float3 wp, float3 vd, int st, int cl, int fo)
            {
                float3 c = float3(0, 0, 0);
                for (int i = 0; i < st; i++)
                {
                    float3 sp = wp + vd * (float(i) / float(st)) * 10.0;
                    float dn = 0.0;
                    for (int j = 0; j < cl; j++)
                    {
                        dn += _fb(sp * 0.1 + float3(0, j * 0.5, _Time.y * 0.01), fo) * 0.1;
                    }
                    float3 ls = float3(0, 0, 0);
                    for (int k = 0; k < 8; k++)
                    {
                        float3 lp = sp + normalize(_WorldSpaceLightPos0.xyz) * float(k) * 0.5;
                        ls.x += _fb(lp, fo) * 0.02;
                    }
                    c += float3(dn, dn, dn) * exp(-ls.x) * 0.01;
                }
                return c;
            }

            float3 _ps(float2 uv, int pc)
            {
                float3 c = float3(0, 0, 0);
                for (int i = 0; i < pc; i++)
                {
                    float fi = float(i);
                    float3 pp = float3(sin(fi * 12345.6789 + _Time.y * 0.5) * 0.5, cos(fi * 98765.4321 + _Time.y * 0.7) * 0.5, sin(fi * 54321.9876 + _Time.y * 0.9) * 0.5);
                    float d = length(pp.xy - uv);
                    float r = 0.01 * (1.0 + sin(fi * 0.1 + _Time.y) * 0.5);
                    float in1 = exp(-d * d / (r * r + 0.0001));
                    c += float3(sin(fi * 0.3) * 0.5 + 0.5, cos(fi * 0.7) * 0.5 + 0.5, sin(fi * 1.1) * 0.5 + 0.5) * in1;
                }
                return c;
            }

            float3 _gi(float3 wp, float3 nm, int sa, int fo)
            {
                float3 c = float3(0, 0, 0);
                for (int i = 0; i < sa; i++)
                {
                    float fi = float(i);
                    float th = fi * 2.399963;
                    float ph = acos(1.0 - 2.0 * fi / float(sa));
                    float3 rd = float3(sin(ph)*cos(th), sin(ph)*sin(th), cos(ph));
                    rd = rd * sign(dot(rd, nm));
                    float3 sp = wp + rd * 2.0;
                    c += _fb(sp, fo) * saturate(dot(rd, nm)) * float3(0.15, 0.12, 0.1);
                }
                return c / float(max(sa, 1));
            }

            float3 _ce(float3 wp, int sa, int fo)
            {
                float3 c = float3(0, 0, 0);
                float3 ld = normalize(_WorldSpaceLightPos0.xyz);
                for (int i = 0; i < sa; i++)
                {
                    float fi = float(i);
                    float3 rd = refract(-ld, float3(sin(fi*0.1), 1, cos(fi*0.1)), 1.33);
                    float3 sp = wp + rd * fi * 0.02;
                    float nn = _fb(sp * 3.0 + _Time.y * 0.1, fo);
                    c += pow(abs(nn), 4.0) * float3(0.2, 0.4, 0.6);
                }
                return c / float(max(sa, 1));
            }

            float _ao(float3 wp, float3 nm, int sa, int fo)
            {
                float ao = 0.0;
                for (int i = 0; i < sa; i++)
                {
                    float fi = float(i);
                    float th = fi * 2.399963;
                    float ph = acos(1.0 - 2.0 * fi / float(sa));
                    float3 sd = float3(sin(ph)*cos(th), sin(ph)*sin(th), cos(ph));
                    sd = sd * sign(dot(sd, nm));
                    float3 sp = wp + sd * 0.5;
                    float hh = _fb(sp, fo);
                    ao += step(hh, 0.3) * saturate(dot(sd, nm));
                }
                return 1.0 - ao / float(max(sa, 1));
            }

            float3 _ssr(float3 wp, float3 vd, float3 nm, float2 uv, int st)
            {
                float3 rd = reflect(vd, nm);
                float3 c = float3(0, 0, 0);
                for (int i = 0; i < st; i++)
                {
                    float fi = float(i) / float(st);
                    float3 sp = wp + rd * fi * 5.0;
                    float2 su = uv + rd.xy * fi * 0.1;
                    c += _xA0.SampleLevel(sampler_xA0, frac(su), fi * 4.0).rgb * exp(-fi * 2.0);
                    c += _xA4.SampleLevel(sampler_xA0, frac(su * 2.0), fi * 4.0).rgb * exp(-fi * 3.0) * 0.5;
                    c += _xA8.SampleLevel(sampler_xA0, frac(su * 1.5), fi * 3.0).rgb * exp(-fi * 2.5) * 0.3;
                    c += _xAC.SampleLevel(sampler_xA0, frac(su * 0.8), fi * 5.0).rgb * exp(-fi * 4.0) * 0.2;
                }
                return c / float(max(st, 1));
            }

            float3 _fl(float2 uv, int it)
            {
                float2 p = uv * 4.0 - 2.0;
                float2 v = float2(0, 0);
                float d = 0.0;
                for (int i = 0; i < it; i++)
                {
                    float fi = float(i);
                    float2 fc = float2(sin(p.y * 3.0 + _Time.y + fi * 0.1) * 0.5, cos(p.x * 3.0 + _Time.y * 1.3 + fi * 0.13) * 0.5);
                    v = v * 0.99 + fc * 0.01;
                    p += v * 0.01;
                    d += length(v) * 0.001;
                    float vt = sin(p.x * 10.0 + fi) * cos(p.y * 10.0 + fi);
                    v += float2(-vt, vt) * 0.005;
                }
                return float3(d, d * 0.7, d * 0.4);
            }

            float3 _ws(float2 uv, int st)
            {
                float ht = 0.0;
                for (int i = 0; i < st; i++)
                {
                    float fi = float(i);
                    float fr = 1.0 + fi * 0.3;
                    float am = 1.0 / (1.0 + fi * 0.5);
                    float ph = _Time.y * (0.5 + fi * 0.1);
                    float2 dr = float2(cos(fi * 1.1), sin(fi * 1.7));
                    ht += sin(dot(uv, dr) * fr + ph) * am;
                    ht += cos(dot(uv.yx, dr) * fr * 1.3 + ph * 0.7) * am * 0.5;
                }
                return float3(0.0, 0.1, 0.3) + ht * 0.05;
            }

            float3 _sh(float3 nm, int od)
            {
                float3 r = float3(0, 0, 0);
                for (int l = 0; l < od; l++)
                {
                    for (int m = -l; m <= l; m++)
                    {
                        float fl = float(l);
                        float fm = float(m);
                        float b = cos(fl * acos(nm.z)) * cos(fm * atan2(nm.y, nm.x));
                        float cf = sin(fl * 1.5 + fm * 2.3 + _Time.y * 0.1);
                        r += b * cf * float3(0.03, 0.025, 0.02);
                    }
                }
                return r;
            }

            float3 _bd(float3 nm, float3 vd, float3 ld, int sa)
            {
                float3 c = float3(0, 0, 0);
                float3 hd = normalize(ld + vd);
                float nv = max(dot(nm, vd), 0.001);
                float nl = max(dot(nm, ld), 0.001);
                float nh = max(dot(nm, hd), 0.001);
                float vh = max(dot(vd, hd), 0.001);
                for (int i = 0; i < sa; i++)
                {
                    float fi = float(i) / float(sa);
                    float rg = 0.1 + fi * 0.8;
                    float al = rg * rg;
                    float a2 = al * al;
                    float dn = nh * nh * (a2 - 1.0) + 1.0;
                    float D = a2 / (3.14159 * dn * dn + 0.0001);
                    float k = (rg + 1.0) * (rg + 1.0) / 8.0;
                    float G = (nv / (nv * (1.0 - k) + k)) * (nl / (nl * (1.0 - k) + k));
                    float3 F = float3(0.04, 0.04, 0.04) + 0.96 * pow(1.0 - vh, 5.0);
                    c += D * G * F / (4.0 * nv * nl + 0.001);
                }
                return c / float(max(sa, 1));
            }

            float3 _sss(float3 nm, float3 vd, float3 ld, float3 ab, int st)
            {
                float3 s = float3(0, 0, 0);
                for (int i = 0; i < st; i++)
                {
                    float off = float(i) / float(st);
                    float3 sd = normalize(ld + nm * off);
                    float sc = pow(max(0.0, dot(-vd, sd)), 8.0);
                    float3 pn = ab * exp(-off * _xCF * float3(1.0, 0.5, 0.25));
                    s += pn * sc * (1.0 / float(st));
                    float ph = (1.0 - 0.7 * 0.7) / (4.0 * 3.14159 * pow(1.0 + 0.7 * 0.7 - 2.0 * 0.7 * dot(vd, sd), 1.5));
                    s += ab * ph * 0.01;
                }
                return s * _xCE.rgb;
            }

            float3 _pt(float3 og, float3 dr, int dp, int fo)
            {
                float3 c = float3(0, 0, 0);
                float3 tp = float3(1, 1, 1);
                for (int b = 0; b < dp; b++)
                {
                    float t = _fb(og + dr * float(b) * 0.5, fo);
                    float3 hp = og + dr * t * 5.0;
                    float3 hn = normalize(float3(
                        _fb(hp + float3(0.01, 0, 0), fo) - _fb(hp - float3(0.01, 0, 0), fo),
                        _fb(hp + float3(0, 0.01, 0), fo) - _fb(hp - float3(0, 0.01, 0), fo),
                        _fb(hp + float3(0, 0, 0.01), fo) - _fb(hp - float3(0, 0, 0.01), fo)
                    ));
                    float3 ab = float3(_h(hp * 1.0), _h(hp * 1.1), _h(hp * 1.2));
                    c += tp * ab * max(dot(hn, normalize(_WorldSpaceLightPos0.xyz)), 0.0);
                    tp *= ab * 0.7;
                    float fb = float(b);
                    dr = normalize(hn + float3(_h(hp + fb), _h(hp * 2.0 + fb), _h(hp * 3.0 + fb)) * 2.0 - 1.0);
                    og = hp + hn * 0.01;
                }
                return c;
            }

            float3 _cv(float2 uv, int ks)
            {
                float3 r = float3(0, 0, 0);
                float tw = 0.0;
                for (int x = -ks; x <= ks; x++)
                {
                    for (int y = -ks; y <= ks; y++)
                    {
                        float2 of = float2(x, y) * 0.002;
                        float w = exp(-(x*x + y*y) / (2.0 * float(ks)));
                        r += _xA0.SampleLevel(sampler_xA0, uv + of, 0).rgb * w;
                        r += _xA3.SampleLevel(sampler_xA0, uv + of * 1.5, 0).rgb * w * 0.3;
                        r += _xA4.SampleLevel(sampler_xA0, uv + of * 2.0, 0).rgb * w * 0.2;
                        r += _xA8.SampleLevel(sampler_xA0, uv + of * 1.2, 0).rgb * w * 0.15;
                        r += _xAC.SampleLevel(sampler_xA0, uv + of * 0.8, 0).rgb * w * 0.1;
                        tw += w;
                    }
                }
                return r / max(tw, 0.001);
            }

            float3 _bl(float2 uv, int pa)
            {
                float3 bm = float3(0, 0, 0);
                for (int p = 0; p < pa; p++)
                {
                    float rd = float(p + 1) * 0.005;
                    float3 pc = float3(0, 0, 0);
                    for (int i = 0; i < 16; i++)
                    {
                        float ag = float(i) * 0.3927;
                        float2 of = float2(cos(ag), sin(ag)) * rd;
                        pc += _xA0.SampleLevel(sampler_xA0, uv + of, float(p)).rgb;
                        pc += _xA5.SampleLevel(sampler_xA0, uv + of * 1.3, float(p)).rgb * 0.5;
                        pc += _xA9.SampleLevel(sampler_xA0, uv + of * 0.7, float(p)).rgb * 0.3;
                        pc += _xAD.SampleLevel(sampler_xA0, uv + of * 1.1, float(p)).rgb * 0.2;
                    }
                    bm += pc / 64.0 * exp(-float(p) * 0.3);
                }
                return bm;
            }

            float3 _mc(float3 c, int pa)
            {
                for (int i = 0; i < pa; i++)
                {
                    float3 hsv = _r2h(c);
                    hsv.x += _Time.y * 0.1 * float(i) + sin(float(i) * 0.7) * 0.2;
                    hsv.y = saturate(hsv.y * (1.0 + float(i) * 0.1));
                    hsv.z = pow(hsv.z, 1.0 + float(i) * 0.05);
                    c = _h2r(hsv);
                    float lm = dot(c, float3(0.299, 0.587, 0.114));
                    c = lerp(float3(lm, lm, lm), c, 1.2);
                    c = pow(abs(c), 1.0 / (1.0 + float(i) * 0.02));
                }
                return c;
            }

            float3 _rn(float3 c, float2 uv, int it)
            {
                float3 result = c;
                for (int i = 0; i < it; i++)
                {
                    float fi = float(i);
                    float2 offset = float2(sin(fi * 7.13 + _Time.y), cos(fi * 11.37 + _Time.y)) * 0.003;
                    float4 s0 = _xA0.SampleLevel(sampler_xA0, frac(uv + offset), fi * 0.5);
                    float4 s1 = _xA6.SampleLevel(sampler_xA0, frac(uv * 1.5 + offset), fi * 0.3);
                    float4 s2 = _xAA.SampleLevel(sampler_xA0, frac(uv * 2.0 + offset * 2.0), fi * 0.7);
                    float4 s3 = _xAE.SampleLevel(sampler_xA0, frac(uv * 0.7 + offset * 3.0), fi * 0.4);
                    float w = sin(fi * 0.1 + _Time.y * 0.05) * 0.5 + 0.5;
                    result += (s0.rgb * 0.3 + s1.rgb * 0.25 + s2.rgb * 0.2 + s3.rgb * 0.15) * w * 0.01;
                }
                return result;
            }

            float3 _ire(float3 nm, float3 vd, float ndi, int sa)
            {
                float3 c = float3(0, 0, 0);
                for (int i = 0; i < sa; i++)
                {
                    float fi = float(i) / float(sa);
                    float wl = 380.0 + fi * 400.0;
                    float t = pow(1.0 - ndi, 2.0 + fi * 3.0);
                    float3 sc = _h2r(float3(fi, 0.9, 1.0));
                    c += sc * t * (1.0 / float(sa));
                }
                return c * _xE6;
            }

            float2 _pm(float2 uv, float3 vt, int la)
            {
                float lh = 1.0 / float(la);
                float clh = 0.0;
                float2 dt = vt.xy / vt.z * _xB3 * lh;
                float2 cu = uv;
                float ch = _xA2.SampleLevel(sampler_xA0, uv, 0).r;
                for (int i = 0; i < la; i++)
                {
                    if (clh >= ch) break;
                    clh += lh;
                    cu -= dt;
                    ch = _xA2.SampleLevel(sampler_xA0, cu, 0).r;
                }
                float2 pu = cu + dt;
                float ah = ch - clh;
                float bh = _xA2.SampleLevel(sampler_xA0, pu, 0).r - clh + lh;
                float w = ah / (ah - bh + 0.0001);
                return lerp(cu, pu, w);
            }

            float3 _un(float4 pn, float2 uv, int it)
            {
                float3 nm = UnpackNormal(pn);
                for (int i = 0; i < it; i++)
                {
                    nm = normalize(nm * nm + 0.1);
                    float3 p1 = UnpackNormal(_xA1.SampleLevel(sampler_xA0, uv * (2.0 + i * 0.5), 0));
                    float3 p2 = UnpackNormal(_xA3.SampleLevel(sampler_xA0, uv * (3.0 + i * 0.7), float(i)));
                    float3 p3 = UnpackNormal(_xA9.SampleLevel(sampler_xA0, uv * (1.5 + i * 0.3), float(i)));
                    nm = normalize(nm + (p1 + p2 * 0.5 + p3 * 0.3) * _xE9);
                }
                return normalize(nm);
            }

            float _rm(float3 ro, float3 rd, float2 uv, int st, int fo)
            {
                float t = 0.0;
                for (int i = 0; i < st; i++)
                {
                    float3 p = ro + rd * t;
                    float ht = _xA2.SampleLevel(sampler_xA0, frac(uv + p.xy * 0.1), 0).r;
                    float sn = _fb(p * 0.5, fo) * 2.0 - 1.0;
                    float ds = p.z - ht + sn * 0.1;
                    if (abs(ds) < 0.0001) break;
                    t += ds * 0.3;
                }
                return t;
            }

            float3 _ec(float2 uv, float3 nm, float3 vd, float3 wp, int lp, int fo)
            {
                float3 c = float3(0, 0, 0);
                float lf = 1.0 / max(1.0, float(lp));
                float3 np = wp * _xC6 + _Time.y * 0.2;
                float bf = _fb(np, fo);
                for (int i = 0; i < lp; i++)
                {
                    float of = float(i) * 0.001 * _xB2;
                    float sv = sin(of * 3.14159);
                    float cv = cos(of * 2.71828);
                    float tv = clamp(tan(of * 1.5708), -100.0, 100.0);
                    float2 su = uv + float2(sv, cv) * 0.02;
                    float4 t0 = _xA0.SampleLevel(sampler_xA0, su, 0);
                    float4 t1 = _xA0.SampleLevel(sampler_xA0, su * 2.0 + of, 0);
                    float4 t2 = _xA3.SampleLevel(sampler_xA0, su * 0.5 + of * 2.0, 0);
                    float4 t3 = _xA1.SampleLevel(sampler_xA0, su * 3.0, 0);
                    float4 t4 = _xA2.SampleLevel(sampler_xA0, su * 1.5 + of, 0);
                    float4 t5 = _xA3.SampleLevel(sampler_xA0, su * _xC5 + of, 0);
                    float4 t6 = _xA4.SampleLevel(sampler_xA0, su * _xC6 + of, 0);
                    float4 t7 = _xA5.SampleLevel(sampler_xA0, su * 1.7 + of * 0.5, 0);
                    float4 t8 = _xA6.SampleLevel(sampler_xA0, su * 2.3 + of * 0.7, 0);
                    float4 t9 = _xA7.SampleLevel(sampler_xA0, su * 1.1 + of * 1.3, 0);
                    float4 tA = _xA8.SampleLevel(sampler_xA0, su * 1.4 + of * 0.9, 0);
                    float4 tB = _xA9.SampleLevel(sampler_xA0, su * 0.9 + of * 1.1, 0);
                    float4 tC = _xAA.SampleLevel(sampler_xA0, su * 1.8 + of * 0.6, 0);
                    float4 tD = _xAB.SampleLevel(sampler_xA0, su * 2.1 + of * 0.4, 0);
                    float4 tE = _xAC.SampleLevel(sampler_xA0, su * 1.3 + of * 1.5, 0);
                    float4 tF = _xAD.SampleLevel(sampler_xA0, su * 0.7 + of * 1.7, 0);
                    float mv = sin(of * _xB2) * cos(of * _xB1) + sqrt(abs(sv + bf * 0.2)) + clamp(tan(of * 0.5), -100.0, 100.0);
                    float ev = exp(clamp(mv * 0.5, -10.0, 10.0));
                    float lv = log(abs(mv) + 1.5);
                    float pv = pow(abs(mv), 3.5);
                    float av = atan2(sv, cv + 0.1);
                    float shv = sinh(clamp(mv * 0.3, -5.0, 5.0));
                    float chv = cosh(clamp(mv * 0.3, -5.0, 5.0));
                    float asv = asin(clamp(mv * 0.5, -1.0, 1.0));
                    float acv = acos(clamp(mv * 0.5, -1.0, 1.0));
                    float3 ns = UnpackNormal(t3);
                    float ndl = max(0.0, dot(ns, float3(0.577, 0.577, 0.577)));
                    float nds = max(0.0, dot(ns, float3(-0.707, 0, 0.707)));
                    float mw = (mv + ev + lv * 0.2 + pv * 0.15 + av * 0.1 + shv * 0.05 + chv * 0.05 + asv * 0.03 + acv * 0.03);
                    c += (t0.rgb + t1.rgb * 0.5 + t2.rgb * 0.3 + t5.rgb * 0.2 + t6.rgb * 0.15
                          + t7.rgb * 0.1 + t8.rgb * 0.08 + t9.rgb * 0.06
                          + tA.rgb * 0.05 + tB.rgb * 0.04 + tC.rgb * 0.03
                          + tD.rgb * 0.025 + tE.rgb * 0.02 + tF.rgb * 0.015)
                        * mw * (ndl + nds * 0.5) * lf;
                }
                return c * _xB1;
            }

            float3 _cl(float3 nm, float3 vd, float3 fp, int lc)
            {
                float3 ld = normalize(_WorldSpaceLightPos0.xyz);
                float3 hd = normalize(ld + vd);
                float df = max(0, dot(nm, ld));
                float sp = pow(max(0, dot(vd, reflect(-ld, nm))), 64.0);
                float sb = pow(max(0, dot(nm, hd)), 128.0);
                float rm = pow(1.0 - max(0, dot(vd, nm)), _xC4) * 0.5;
                float3 lt = _LightColor0.rgb * (df * 0.7 + sp * 0.2 + sb * 0.15 + rm * 0.05);
                for (int i = 0; i < lc; i++)
                {
                    float fi = float(i);
                    float ag = fi * 0.196349 * 2.0;
                    float r = 2.0 + sin(ag + _Time.y) * 0.5;
                    float3 pp = fp + float3(sin(ag), 0.5 + fi * 0.05, cos(ag)) * r;
                    float3 pd = normalize(pp - fp);
                    float pn = max(0.0, dot(nm, pd));
                    float ps = pow(max(0.0, dot(nm, normalize(pd + vd))), 64.0);
                    float d = length(pp - fp);
                    float at = 1.0 / (1.0 + d * d * 0.1);
                    float3 lc2 = float3(sin(ag), cos(ag * 0.7), sin(ag * 1.3)) * 0.5 + 0.5;
                    lt += lc2 * (pn * 0.08 + ps * 0.04) * at;
                    float3 hp = normalize(pd + vd);
                    float ant = dot(hp, normalize(float3(sin(ag), 0, cos(ag))));
                    lt += lc2 * pow(max(0, ant), 32.0) * at * 0.02;
                }
                return lt;
            }

            float3 _sr(float3 vd, float3 nm, float2 uv, int sa)
            {
                float3 r = float3(0, 0, 0);
                for (int i = 0; i < sa; i++)
                {
                    float of = float(i) / float(sa);
                    float3 pn = normalize(nm + float3(
                        _h(float3(of, of * 2.0, of * 3.0)) * 2.0 - 1.0,
                        _h(float3(of * 4.0, of * 5.0, of * 6.0)) * 2.0 - 1.0,
                        _h(float3(of * 7.0, of * 8.0, of * 9.0)) * 2.0 - 1.0
                    ) * 0.1 * _xC0);
                    float3 rd = reflect(-vd, pn);
                    r += _xA0.SampleLevel(sampler_xA0, uv + rd.xy * 0.1, of * 8.0).rgb;
                    r += _xA5.SampleLevel(sampler_xA0, uv + rd.xz * 0.05, of * 4.0).rgb * 0.5;
                    r += _xAA.SampleLevel(sampler_xA0, uv + rd.yz * 0.08, of * 6.0).rgb * 0.3;
                    r += _xAF.SampleLevel(sampler_xA0, uv + rd.xy * 0.03, of * 3.0).rgb * 0.2;
                }
                return r / float(max(sa, 1));
            }

            float3 _rf(float3 c, float2 uv, float3 nm)
            {
                float3 rR = refract(float3(0,0,1), nm, 1.0 / (1.0 + _xC9));
                float3 rG = refract(float3(0,0,1), nm, 1.0 / (1.0 + _xC9 + _xCA));
                float3 rB = refract(float3(0,0,1), nm, 1.0 / (1.0 + _xC9 + _xCA * 2.0));
                float3 ref;
                ref.r = _xA0.SampleLevel(sampler_xA0, uv + rR.xy * 0.05, 0).r;
                ref.g = _xA0.SampleLevel(sampler_xA0, uv + rG.xy * 0.05, 0).g;
                ref.b = _xA0.SampleLevel(sampler_xA0, uv + rB.xy * 0.05, 0).b;
                return lerp(c, ref, _xC9);
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 dp = v.vertex.xyz;
                int vl = clamp(int(_xF6), 4, 128);
                for (int i = 0; i < vl; i++)
                {
                    float fi = float(i);
                    float n1 = _n(wp * (1.0 + fi * 0.1) + _Time.y * 0.05);
                    dp += v.normal * n1 * 0.001 * _xEA;
                    float n2 = _n(wp * 2.0 + fi * 0.5);
                    dp += v.tangent.xyz * n2 * 0.0005;
                }
                o.pos = UnityObjectToClipPos(float4(dp, 1.0));
                o.uv = TRANSFORM_TEX(v.uv, _xA0);
                o.uv2 = v.uv2;
                o.vertColor = v.color;
                o.worldPos = mul(unity_ObjectToWorld, float4(dp, 1.0)).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.tangent = UnityObjectToWorldDir(v.tangent.xyz);
                o.binormal = cross(o.normal, o.tangent) * v.tangent.w;
                o.viewDir = _WorldSpaceCameraPos - o.worldPos;
                TRANSFER_SHADOW(o);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                int fo = clamp(int(_xB4), 4, 256);
                int ml = clamp(int(_xB0), 16, 131072);
                int pl = clamp(int(_xC7), 8, 1024);
                int rs = clamp(int(_xC8), 8, 1024);
                int lc = clamp(int(_xB7), 8, 1024);
                int ry = clamp(int(_xB8), 8, 1024);
                int ss = clamp(int(_xB9), 8, 512);
                int vs = clamp(int(_xD6), 4, 512);
                int cl = clamp(int(_xD7), 1, 128);
                int pn = clamp(int(_xD8), 10, 4096);
                int gs = clamp(int(_xF3), 4, 1024);
                int cs = clamp(int(_xF4), 4, 1024);
                int as1 = clamp(int(_xF2), 4, 1024);
                int sr = clamp(int(_xFB), 4, 1024);
                int fi = clamp(int(_xF5), 8, 4096);
                int wt = clamp(int(_xF7), 8, 2048);
                int so = clamp(int(_xFD), 2, 64);
                int bs = clamp(int(_xFE), 8, 2048);
                int pd = clamp(int(_xF1), 4, 256);
                int cv = clamp(int(_xF9), 2, 256);
                int bp = clamp(int(_xFA), 4, 256);
                int cp = clamp(int(_xB6), 4, 1024);
                int sw = clamp(int(_xD9), 4, 256);
                int tl = clamp(int(_xD7), 2, 128);
                int md = clamp(int(_xFF), 16, 16384);
                int ji = clamp(int(_y00), 16, 8192);
                int bsi = clamp(int(_y01), 16, 8192);
                int tci = clamp(int(_y02), 16, 4096);
                int rni = clamp(int(_y03), 8, 512);
                int iri = clamp(int(_y04), 8, 256);
                int dsm = clamp(int(_y0A), 16, 8192);
                int nsm = clamp(int(_y0B), 8, 2048);
                int csm = clamp(int(_y0C), 8, 1024);

                float3x3 TBN = float3x3(i.tangent, i.binormal, i.normal);
                float3 vdt = mul(TBN, normalize(i.viewDir));
                float2 pu = _pm(i.uv, vdt, pl) * _xEB;

                float4 bc = _xA0.Sample(sampler_xA0, pu) * _xBA;
                float4 nm = _xA1.Sample(sampler_xA0, pu);
                float3 n = normalize(mul(_un(nm, pu, 8), TBN));

                float3 vd = normalize(i.viewDir);
                float3 ld = normalize(_WorldSpaceLightPos0.xyz);

                float3 ec = _ec(pu, n, vd, i.worldPos, ml, fo);
                float3 sss = _sss(n, vd, ld, bc.rgb, ss);
                float3 lt = _cl(n, vd, i.worldPos, lc);
                float sh = SHADOW_ATTENUATION(i);
                float3 ref = _sr(vd, n, pu, rs);
                float3 rfr = _rf(bc.rgb, pu, n);
                float rt = _rm(i.worldPos, vd, pu, ry, fo);
                float3 vol = _vc(i.worldPos, vd, vs, cl, fo);
                float3 par = _ps(pu, pn);
                float3 gi = _gi(i.worldPos, n, gs, fo);
                float3 cau = _ce(i.worldPos, cs, fo);
                float ao = _ao(i.worldPos, n, as1, fo);
                float3 ssr = _ssr(i.worldPos, vd, n, pu, sr);
                float3 flu = _fl(pu, fi);
                float3 wav = _ws(pu, wt);
                float3 shc = _sh(n, so);
                float3 brd = _bd(n, vd, ld, bs);
                float3 ptc = _pt(i.worldPos, vd, pd, max(fo / 2, 4));
                float3 cnv = _cv(pu, cv);
                float3 blm = _bl(pu, bp);
                float3 trb = _td(pu, _Time.y, tl);

                float2 mc = (pu - 0.5) * 3.0 + float2(-0.5, 0.0);
                float mv = _mb(mc, md);
                float3 fc = _h2r(float3(mv, 0.8, mv > 0.99 ? 0.0 : 1.0));

                float2 jz = (pu - 0.5) * 3.0;
                float2 jc = float2(sin(_Time.y * 0.1) * 0.4 - 0.4, cos(_Time.y * 0.13) * 0.3);
                float jv = _jl(jz, jc, ji);
                float3 jcl = _h2r(float3(jv * 0.8 + 0.1, 0.9, jv > 0.99 ? 0.0 : 0.8));

                float2 bsc = (pu - 0.5) * 2.5;
                float bsv = _bs(bsc, bsi);
                float3 bscl = _h2r(float3(bsv * 0.6 + 0.3, 0.85, bsv > 0.99 ? 0.0 : 0.9));

                float2 tcc = (pu - 0.5) * 2.0;
                float tcv = _tc(tcc, tci);
                float3 tccl = _h2r(float3(tcv * 0.7 + 0.2, 0.75, tcv > 0.99 ? 0.0 : 0.85));

                float3 cc = _mc(bc.rgb, cp);

                float3 rns = _rn(bc.rgb, pu, rni);

                float ndi = max(0, dot(n, vd));
                float3 irs = _ire(n, vd, ndi, iri);

                float dsv = 0.0;
                float2 dp = pu;
                for (int di = 0; di < dsm; di++)
                {
                    float dfi = float(di);
                    float2 dof = float2(sin(dfi * 3.7 + _Time.y * 0.1), cos(dfi * 5.3 + _Time.y * 0.13)) * 0.001;
                    dp += dof;
                    float4 ds0 = _xA0.SampleLevel(sampler_xA0, frac(dp), 0);
                    float4 ds1 = _xA8.SampleLevel(sampler_xA0, frac(dp * 2.0), 0);
                    float4 ds2 = _xAF.SampleLevel(sampler_xA0, frac(dp * 0.5), 0);
                    dsv += (ds0.r + ds1.g + ds2.b) * 0.001;
                    dp = frac(dp + float2(ds0.g, ds1.r) * 0.01);
                }

                float nsv = 0.0;
                for (int ni = 0; ni < nsm; ni++)
                {
                    float nfi = float(ni);
                    float3 np = i.worldPos * (1.0 + nfi * 0.01) + float3(sin(nfi * 0.1), cos(nfi * 0.2), sin(nfi * 0.3)) * _Time.y * 0.01;
                    nsv += _n(np) * 0.001;
                }

                float csv = 0.0;
                for (int ci = 0; ci < csm; ci++)
                {
                    float cfi = float(ci);
                    float3 cp2 = i.worldPos + n * cfi * 0.01;
                    float4 cs0 = _xA2.SampleLevel(sampler_xA0, frac(pu + cfi * 0.001), cfi * 0.1);
                    float4 cs1 = _xAC.SampleLevel(sampler_xA0, frac(pu * 1.5 + cfi * 0.002), cfi * 0.2);
                    csv += (cs0.r * cs1.g + sin(cfi * 0.7)) * 0.0005;
                }

                float sa = 1.0;
                for (int si = 0; si < sw; si++)
                {
                    float3 of = float3(sin(float(si) * 0.123 + _Time.y), cos(float(si) * 0.456 + _Time.y), sin(float(si) * 0.789 + _Time.y)) * 0.01;
                    sa *= saturate(0.5 + _fb(i.worldPos + of, max(fo / 2, 4)) * 0.5);
                }

                float3 fc2 = (bc.rgb + ec * 0.3 + sss * 0.2) * lt * sh;
                fc2 = lerp(fc2, ref, _xC0 * 0.2);
                fc2 = lerp(fc2, rfr, _xC9 * 0.15);
                fc2 *= ao * _xC2;
                fc2 += bc.rgb * _xC3 * 0.1;
                fc2 += gi * 0.15;
                fc2 += cau * 0.08;
                fc2 += ssr * 0.12;
                fc2 += vol * 0.1;
                fc2 += par * 0.15;
                fc2 += flu * 0.08;
                fc2 += wav * 0.06;
                fc2 += shc * 0.1;
                fc2 += brd * 0.15;
                fc2 += ptc * 0.1;
                fc2 += cnv * 0.05;
                fc2 += blm * 0.12;
                fc2 += trb * 0.05;
                fc2 += fc * 0.08;
                fc2 += jcl * 0.06;
                fc2 += bscl * 0.05;
                fc2 += tccl * 0.04;
                fc2 += cc * 0.1;
                fc2 += rns * 0.03;
                fc2 += irs * 0.07;
                fc2 += dsv * 0.02;
                fc2 += nsv * 0.01;
                fc2 += csv * 0.01;
                fc2 *= sa;
                fc2 += rt * 0.01;
                fc2 = pow(abs(fc2), 0.85);
                fc2 = lerp(fc2, 1.0 - fc2, sin(_Time.y * 0.75) * 0.1 + 0.05);

                float4 fogResult = float4(fc2, bc.a);
                UNITY_APPLY_FOG(i.fogCoord, fogResult);
                return fogResult;
            }
            ENDCG
        }

        Pass
        {
            Name "FORWARD_ADD"
            Tags { "LightMode"="ForwardAdd" }
            Blend One One
            ZWrite Off

            CGPROGRAM
            #pragma vertex vert_add
            #pragma fragment frag_add
            #pragma multi_compile_fwdadd_fullshadows
            #pragma multi_compile_fog
            #pragma target 5.0

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            Texture2D _xA0, _xA1, _xA2, _xA3, _xA4, _xA5, _xA6, _xA7;
            Texture2D _xA8, _xA9, _xAA, _xAB, _xAC, _xAD, _xAE, _xAF;
            SamplerState sampler_xA0;
            float4 _xA0_ST;
            float _xB0, _xB1, _xB2, _xC6;
            float _xB4, _xB7, _xB8;
            float _xC0, _xC4, _xE9;
            float _xB3, _xEB, _xC7;
            float _xC5, _xC8;
            float _xB9, _xCF, _xD0;
            float4 _xCE, _xBA;
            float _xF5, _xF7, _xFE;

            struct appdata_add
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f_add
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float3 tangent : TEXCOORD2;
                float3 binormal : TEXCOORD3;
                float3 worldPos : TEXCOORD4;
                float3 viewDir : TEXCOORD5;
                SHADOW_COORDS(6)
                UNITY_FOG_COORDS(7)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float _ha(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float _na(float3 x) { float3 p = floor(x); float3 f = frac(x); f = f * f * (3.0 - 2.0 * f); return lerp(lerp(lerp(_ha(p), _ha(p + float3(1,0,0)), f.x), lerp(_ha(p + float3(0,1,0)), _ha(p + float3(1,1,0)), f.x), f.y), lerp(lerp(_ha(p + float3(0,0,1)), _ha(p + float3(1,0,1)), f.x), lerp(_ha(p + float3(0,1,1)), _ha(p + float3(1,1,1)), f.x), f.y), f.z); }
            float _fba(float3 p, int o) { float v = 0.0; float a = 0.5; float f = 1.0; for (int i = 0; i < o; i++) { v += a * _na(p * f); f *= 2.17; a *= 0.49; } return v; }

            v2f_add vert_add(appdata_add v)
            {
                v2f_add o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _xA0);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.tangent = UnityObjectToWorldDir(v.tangent.xyz);
                o.binormal = cross(o.normal, o.tangent) * v.tangent.w;
                o.viewDir = _WorldSpaceCameraPos - o.worldPos;
                TRANSFER_SHADOW(o);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            float4 frag_add(v2f_add i) : SV_Target
            {
                int fo = clamp(int(_xB4), 4, 256);
                int al = clamp(int(_xB0), 16, 131072);
                int bs = clamp(int(_xFE), 8, 2048);
                int fi = clamp(int(_xF5), 8, 4096);
                int wt = clamp(int(_xF7), 8, 2048);

                float4 bc = _xA0.Sample(sampler_xA0, i.uv) * _xBA;
                float3 nm = normalize(i.normal);
                float3 vd = normalize(i.viewDir);

                float3 ld; float at;
                if (_WorldSpaceLightPos0.w == 0.0) { ld = normalize(_WorldSpaceLightPos0.xyz); at = 1.0; }
                else { float3 tl = _WorldSpaceLightPos0.xyz - i.worldPos; ld = normalize(tl); float d = length(tl); at = 1.0 / (1.0 + d * d * 0.01); }

                float nl = max(0, dot(nm, ld));
                float3 hd = normalize(ld + vd);
                float sp = pow(max(0, dot(nm, hd)), 128.0);

                float3 c = float3(0, 0, 0);
                float lf = 1.0 / max(1.0, float(al));
                for (int j = 0; j < al; j++)
                {
                    float fj = float(j);
                    float of = fj * 0.001 * _xB2;
                    float s = sin(of * 3.14159);
                    float cv = cos(of * 2.71828);
                    float2 su = i.uv + float2(s, cv) * 0.02;
                    float4 t0 = _xA0.SampleLevel(sampler_xA0, su, 0);
                    float4 t1 = _xA3.SampleLevel(sampler_xA0, su * 2.0 + of, 0);
                    float4 t2 = _xA4.SampleLevel(sampler_xA0, su * _xC6 + of, 0);
                    float4 t3 = _xA5.SampleLevel(sampler_xA0, su * 1.5 + of, 0);
                    float4 t4 = _xA8.SampleLevel(sampler_xA0, su * 1.3 + of * 0.7, 0);
                    float4 t5 = _xAC.SampleLevel(sampler_xA0, su * 0.9 + of * 1.1, 0);
                    float mv = sin(of * _xB2) * cos(of * _xB1) + sqrt(abs(s)) + exp(clamp(s * 0.5, -10.0, 10.0)) * 0.1;
                    c += (t0.rgb + t1.rgb * 0.5 + t2.rgb * 0.3 + t3.rgb * 0.2 + t4.rgb * 0.15 + t5.rgb * 0.1) * mv * lf;
                }

                float3 sss = float3(0, 0, 0);
                int st = clamp(int(_xB9), 4, 128);
                for (int si = 0; si < st; si++)
                {
                    float off = float(si) / float(st);
                    float3 sd = normalize(ld + nm * off);
                    sss += bc.rgb * pow(max(0, dot(-vd, sd)), 8.0) * exp(-off * _xCF) / float(st);
                }
                sss *= _xCE.rgb;

                float3 brdf = float3(0, 0, 0);
                float nv = max(dot(nm, vd), 0.001);
                float nh = max(dot(nm, hd), 0.001);
                float vh = max(dot(vd, hd), 0.001);
                for (int bi = 0; bi < bs; bi++)
                {
                    float fb = float(bi) / float(bs);
                    float rg = 0.1 + fb * 0.8;
                    float al2 = rg * rg; float a2 = al2 * al2;
                    float dn = nh * nh * (a2 - 1.0) + 1.0;
                    float D = a2 / (3.14159 * dn * dn + 0.0001);
                    float k = (rg + 1.0) * (rg + 1.0) / 8.0;
                    float G = (nv / (nv * (1.0 - k) + k)) * (nl / (nl * (1.0 - k) + k));
                    float3 F = float3(0.04, 0.04, 0.04) + 0.96 * pow(1.0 - vh, 5.0);
                    brdf += D * G * F / (4.0 * nv * nl + 0.001);
                }
                brdf /= float(max(bs, 1));

                float3 r = (bc.rgb * nl + sp * 0.3 + c * 0.2 + sss * 0.15 + brdf * 0.1) * _LightColor0.rgb * at;
                r *= SHADOW_ATTENUATION(i);
                float4 fogResult = float4(r, 0);
                UNITY_APPLY_FOG(i.fogCoord, fogResult);
                return fogResult;
            }
            ENDCG
        }

        Pass
        {
            Name "SHADOWCASTER"
            Tags { "LightMode"="ShadowCaster" }

            CGPROGRAM
            #pragma vertex vert_shadow
            #pragma fragment frag_shadow
            #pragma multi_compile_shadowcaster
            #pragma target 5.0
            #include "UnityCG.cginc"

            float _xB4, _xEA, _xF6;
            sampler2D _xA2;

            float _hs(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float _ns(float3 x) { float3 p = floor(x); float3 f = frac(x); f = f * f * (3.0 - 2.0 * f); return lerp(lerp(lerp(_hs(p), _hs(p + float3(1,0,0)), f.x), lerp(_hs(p + float3(0,1,0)), _hs(p + float3(1,1,0)), f.x), f.y), lerp(lerp(_hs(p + float3(0,0,1)), _hs(p + float3(1,0,1)), f.x), lerp(_hs(p + float3(0,1,1)), _hs(p + float3(1,1,1)), f.x), f.y), f.z); }

            struct appdata_shadow
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f_shadow { V2F_SHADOW_CASTER; UNITY_VERTEX_OUTPUT_STEREO };

            v2f_shadow vert_shadow(appdata_shadow v)
            {
                v2f_shadow o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                int loops = clamp(int(_xF6), 4, 256);
                float3 dp = v.vertex.xyz;
                for (int i = 0; i < loops; i++) { float n = _ns(wp * (1.0 + float(i) * 0.1) + _Time.y * 0.05); dp += v.normal * n * 0.001 * _xEA; }
                v.vertex.xyz = dp;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            float4 frag_shadow(v2f_shadow i) : SV_Target { SHADOW_CASTER_FRAGMENT(i) }
            ENDCG
        }
    }

    FallBack "Diffuse"
}
