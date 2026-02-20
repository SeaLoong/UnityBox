Shader "UnityBox/ASS_UI"
{
    Properties
    {
        _A7F3 ("", Color) = (1, 1, 1, 1)
        _B2E1 ("", Color) = (1, 0, 0, 1)
        _C9D4 ("", Range(0, 1)) = 1.0
        _D1A8 ("", Range(0, 0.5)) = 0.06
        _E3B5 ("", Range(-0.5, 0.5)) = -0.35
        _F0C2 ("", Range(0, 0.4)) = 0.1
        [NoScaleOffset] _G4D9 ("", 2D) = "black" {}
        _H6E7 ("", Range(0, 1)) = 0.9
    }

    SubShader
    {
        Tags { "RenderType"="Overlay" "Queue"="Overlay+5000" "IgnoreProjector"="True" "ForceNoShadowCasting"="True" "PreviewType"="Plane" "DisableBatching"="True" }
        LOD 100

        ZTest Always
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask RGBA
        Offset -1, -1

        Stencil
        {
            Ref 255
            Comp Always
            Pass Replace
        }

        Pass
        {
            Name "ASS_UI_PASS"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _A7F3;
            fixed4 _B2E1;
            float _C9D4;
            float _D1A8;
            float _E3B5;
            float _F0C2;

            sampler2D _G4D9;
            float4 _G4D9_TexelSize;
            float _H6E7;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = float4(v.uv * 2.0 - 1.0, 0, 1);
                #if UNITY_UV_STARTS_AT_TOP
                o.pos.y = -o.pos.y;
                #endif
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv = i.uv;

                float _cs = clamp(1.2 / max(unity_CameraProjection[1][1], 0.01), 1.0, 2.0);
                float2 cUV = (uv - 0.5) * _cs + 0.5;

                float barCenterY = 0.5 + _E3B5;
                float barBottom = barCenterY - _D1A8 * 0.5;
                float barTop = barCenterY + _D1A8 * 0.5;

                float barLeft = _F0C2;
                float barRight = _F0C2 + (1.0 - 2.0 * _F0C2) * _C9D4;

                bool inBar = (cUV.y >= barBottom && cUV.y <= barTop &&
                              cUV.x >= barLeft && cUV.x <= barRight);

                fixed4 baseColor = inBar ? _B2E1 : _A7F3;

                float screenAspect = _ScreenParams.x / _ScreenParams.y;
                float texAspect = _G4D9_TexelSize.z / max(_G4D9_TexelSize.w, 0.001);

                float logoHeight = _H6E7;
                float logoWidth = logoHeight * texAspect / screenAspect;

                float logoCenterY = (barTop + 1.0) * 0.5;
                float2 logoMin = float2(0.5 - logoWidth * 0.5, logoCenterY - logoHeight * 0.5);
                float2 logoMax = float2(0.5 + logoWidth * 0.5, logoCenterY + logoHeight * 0.5);

                bool inLogo = (cUV.x >= logoMin.x && cUV.x <= logoMax.x &&
                               cUV.y >= logoMin.y && cUV.y <= logoMax.y);

                if (inLogo && _H6E7 > 0.001)
                {
                    float2 logoUV = (cUV - logoMin) / (logoMax - logoMin);
                    fixed4 logoColor = tex2D(_G4D9, logoUV);
                    baseColor = lerp(baseColor, logoColor, logoColor.a);
                }

                return baseColor;
            }
            ENDCG
        }
    }

    FallBack "Unlit/Color"
}
