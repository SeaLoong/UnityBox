// ASS UI Shader
// 全屏覆盖渲染：直接渲染在玩家摄像机上，无需世界空间定位
// 通过 _Progress 属性控制进度条宽度（由动画驱动）
// Logo 图片居中显示，自动适配屏幕宽高比
Shader "UnityBox/AvatarSecuritySystem/UI"
{
    Properties
    {
        _BackgroundColor ("背景颜色", Color) = (1, 1, 1, 1)
        _BarColor ("进度条颜色", Color) = (1, 0, 0, 1)
        _Progress ("进度 (0~1)", Range(0, 1)) = 1.0
        _BarHeight ("进度条高度比例", Range(0, 0.5)) = 0.06
        _BarOffsetY ("进度条Y偏移", Range(-0.5, 0.5)) = -0.35
        _BarPaddingX ("进度条X边距", Range(0, 0.4)) = 0.1
        [NoScaleOffset] _LogoTex ("Logo 纹理", 2D) = "black" {}
        _LogoScale ("Logo 大小", Range(0, 1)) = 0.9
    }

    SubShader
    {
        // Overlay+100 确保在所有其他物体之上渲染
        Tags { "RenderType"="Overlay" "Queue"="Overlay+100" }
        LOD 100

        // 关闭深度测试和深度写入，忽略剔除
        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ASS_UI_PASS"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            fixed4 _BackgroundColor;
            fixed4 _BarColor;
            float _Progress;
            float _BarHeight;
            float _BarOffsetY;
            float _BarPaddingX;

            sampler2D _LogoTex;
            float4 _LogoTex_TexelSize;  // Unity 自动提供: (1/w, 1/h, w, h)
            float _LogoScale;

            v2f vert(appdata v)
            {
                v2f o;
                // 将顶点直接映射到裁剪空间全屏位置
                // UV (0,0)→(-1,-1), (1,1)→(1,1)
                o.pos = float4(v.uv * 2.0 - 1.0, 0, 1);
                // 处理 DirectX 等平台 Y 轴翻转
                #if UNITY_UV_STARTS_AT_TOP
                o.pos.y = -o.pos.y;
                #endif
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // UV 坐标：(0,0) 左下 → (1,1) 右上
                float2 uv = i.uv;

                // 进度条区域判定
                // Y 方向：以 _BarOffsetY 为中心，_BarHeight 为高度
                float barCenterY = 0.5 + _BarOffsetY;
                float barBottom = barCenterY - _BarHeight * 0.5;
                float barTop = barCenterY + _BarHeight * 0.5;

                // X 方向：从 _BarPaddingX 到 (1 - _BarPaddingX)，乘以 _Progress
                float barLeft = _BarPaddingX;
                float barRight = _BarPaddingX + (1.0 - 2.0 * _BarPaddingX) * _Progress;

                // 判断当前像素是否在进度条区域内
                bool inBar = (uv.y >= barBottom && uv.y <= barTop &&
                              uv.x >= barLeft && uv.x <= barRight);

                fixed4 baseColor = inBar ? _BarColor : _BackgroundColor;

                // Logo 渲染：居中显示，考虑屏幕和纹理宽高比
                float screenAspect = _ScreenParams.x / _ScreenParams.y;
                float texAspect = _LogoTex_TexelSize.z / max(_LogoTex_TexelSize.w, 0.001);

                float logoHeight = _LogoScale;
                float logoWidth = logoHeight * texAspect / screenAspect;

                // Logo 居中在进度条上方的可用空间中央
                float logoCenterY = (barTop + 1.0) * 0.5;
                float2 logoMin = float2(0.5 - logoWidth * 0.5, logoCenterY - logoHeight * 0.5);
                float2 logoMax = float2(0.5 + logoWidth * 0.5, logoCenterY + logoHeight * 0.5);

                bool inLogo = (uv.x >= logoMin.x && uv.x <= logoMax.x &&
                               uv.y >= logoMin.y && uv.y <= logoMax.y);

                if (inLogo && _LogoScale > 0.001)
                {
                    float2 logoUV = (uv - logoMin) / (logoMax - logoMin);
                    fixed4 logoColor = tex2D(_LogoTex, logoUV);
                    baseColor = lerp(baseColor, logoColor, logoColor.a);
                }

                return baseColor;
            }
            ENDCG
        }
    }

    FallBack "Unlit/Color"
}
