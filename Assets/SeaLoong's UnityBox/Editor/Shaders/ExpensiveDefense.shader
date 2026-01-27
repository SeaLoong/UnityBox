Shader "SeaLoong/ExpensiveDefense"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
        _LoopCount ("Loop Count", Float) = 1000
        _Intensity ("Intensity", Float) = 1.0
        _Complexity ("Complexity", Float) = 10.0
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _HeightMap ("Height Map", 2D) = "white" {}
        _ParallaxScale ("Parallax Scale", Float) = 0.05
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            sampler2D _MainTex, _NormalMap, _HeightMap;
            float4 _MainTex_ST, _NormalMap_ST, _HeightMap_ST;
            float _LoopCount, _Intensity, _Complexity, _ParallaxScale;
            float4 _BaseColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float3 tangent : TEXCOORD2;
                float3 binormal : TEXCOORD3;
                float3 worldPos : TEXCOORD4;
                SHADOW_COORDS(5)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv0, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.tangent = UnityObjectToWorldDir(v.tangent.xyz);
                o.binormal = cross(o.normal, o.tangent) * v.tangent.w;
                
                TRANSFER_SHADOW(o);
                return o;
            }

            // 视差映射 - GPU 密集计算
            float2 ParallaxMapping(float2 uv, float3 viewDir, out float parallaxHeight)
            {
                float heightScale = _ParallaxScale;
                float height = tex2D(_HeightMap, uv).r;
                float3 v = normalize(viewDir);
                
                // 多次迭代的视差映射（增加 GPU 计算）
                float2 p = v.xy / v.z * (height - 0.5) * heightScale;
                
                // 陡峭视差映射迭代（GPU 密集）
                const int minLayers = 8;
                const int maxLayers = 32;
                int numLayers = int(lerp(maxLayers, minLayers, abs(dot(float3(0, 0, 1), v))));
                
                float layerHeight = 1.0 / numLayers;
                float currentLayerHeight = 0.0;
                float2 dtex = uv / numLayers;
                float2 currentTextureCoords = uv;
                float currentHeightFromTexture = tex2D(_HeightMap, currentTextureCoords).r;
                
                // 循环计算（GPU 消耗）
                for(int i = 0; i < numLayers; i++)
                {
                    currentLayerHeight += layerHeight;
                    currentTextureCoords -= dtex;
                    currentHeightFromTexture = tex2D(_HeightMap, currentTextureCoords).r;
                    
                    if(currentHeightFromTexture < currentLayerHeight)
                        break;
                }
                
                float2 prevTexCoords = currentTextureCoords + dtex;
                float heightAfterLayer = currentHeightFromTexture - currentLayerHeight;
                float heightBeforeLayer = tex2D(_HeightMap, prevTexCoords).r - currentLayerHeight + layerHeight;
                
                float weight = heightAfterLayer / (heightAfterLayer - heightBeforeLayer);
                float2 finalTexCoords = mix(currentTextureCoords, prevTexCoords, weight);
                
                parallaxHeight = mix(currentLayerHeight, currentLayerHeight - layerHeight, weight);
                return finalTexCoords;
            }

            // 复杂法线贴图计算
            float3 UnpackNormalComplex(float4 packedNormal)
            {
                // 多次采样和计算（GPU 密集）
                float3 normal = UnpackNormal(packedNormal);
                
                // 额外的法线计算（增加成本）
                for(int i = 0; i < 3; i++)
                {
                    normal = normalize(normal * normal + 0.1);
                }
                
                return normalize(normal);
            }

            // 高消耗 GPU 循环计算
            float3 ExpensiveColorComputation(float2 uv, float3 normal, float3 viewDir)
            {
                float3 color = float3(0, 0, 0);
                
                // 根据 _LoopCount 进行大量迭代计算
                int loops = int(_LoopCount);
                float loopFactor = 1.0 / max(1.0, float(loops));
                
                // 主循环 - GPU 密集计算
                for(int i = 0; i < loops; i++)
                {
                    // 纹理采样（多次）
                    float offset = float(i) * 0.001 * _Complexity;
                    float2 sampleUv = uv + float2(sin(offset), cos(offset)) * 0.01;
                    
                    float4 texSample = tex2D(_MainTex, sampleUv);
                    
                    // 复杂的数学计算（三角函数、根号等）
                    float value = sin(offset * 3.14159 * _Complexity) * 
                                 cos(offset * 2.71828 * _Intensity) +
                                 sqrt(abs(sin(offset * _Complexity)));
                    
                    // 法线相关计算
                    float3 normalSample = UnpackNormal(tex2D(_NormalMap, sampleUv));
                    float ndl = max(0.0, dot(normalSample, float3(0.577, 0.577, 0.577)));
                    
                    // 累积颜色（GPU 继续计算）
                    color += texSample.rgb * value * ndl * loopFactor;
                }
                
                return color;
            }

            // 多光源计算
            float3 CalculateLighting(float3 normal, float3 viewDir, float3 fragPos)
            {
                float3 lighting = float3(0, 0, 0);
                
                // 主光源
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float ndl = max(0.0, dot(normal, lightDir));
                float3 halfDir = normalize(lightDir + viewDir);
                float spec = pow(max(0.0, dot(normal, halfDir)), 128.0);
                
                lighting += _LightColor0.rgb * (ndl * 0.7 + spec * 0.3);
                
                // 模拟多光源计算（GPU 消耗）
                for(int i = 0; i < 8; i++)
                {
                    float angle = float(i) * 0.785398; // 360/8
                    float3 pseudoLight = float3(sin(angle), 0.5, cos(angle));
                    float3 pseudoDir = normalize(pseudoLight - fragPos);
                    float pseudoNdl = max(0.0, dot(normal, pseudoDir));
                    lighting += float3(0.1, 0.1, 0.1) * pseudoNdl * 0.1;
                }
                
                return lighting;
            }

            float4 frag(v2f i) : SV_Target
            {
                // 构建 TBN 矩阵
                float3x3 TBN = float3x3(i.tangent, i.binormal, i.normal);
                
                // 视差映射（GPU 密集）
                float parallaxHeight = 0;
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 viewDirTangent = mul(TBN, -viewDir);
                float2 finalUv = ParallaxMapping(i.uv, viewDirTangent, parallaxHeight);
                
                // 采样纹理和法线
                float4 texColor = tex2D(_MainTex, finalUv);
                float4 normalMap = tex2D(_NormalMap, finalUv);
                float3 normal = UnpackNormalComplex(normalMap);
                normal = normalize(mul(normal, TBN));
                
                // 高消耗 GPU 计算（根据 _LoopCount）
                float3 expensiveColor = ExpensiveColorComputation(finalUv, normal, viewDir);
                
                // 光照计算
                float3 lighting = CalculateLighting(normal, viewDir, i.worldPos);
                
                // 阴影
                float shadow = SHADOW_ATTENUATION(i);
                
                // 最终颜色合成（GPU 计算）
                float3 finalColor = (texColor.rgb + expensiveColor) * _BaseColor.rgb * lighting * shadow;
                
                // 额外的色彩空间转换（GPU 消耗）
                finalColor = pow(finalColor, 2.2); // sRGB 编码
                finalColor = saturate(finalColor);
                finalColor = pow(finalColor, 1.0 / 2.2); // 线性化
                
                return float4(finalColor, 1.0);
            }
            ENDCG
        }

        Pass
        {
            Name "SHADOW"
            Tags { "LightMode" = "ShadowCaster" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                V2F_SHADOW_CASTER;
            };

            v2f vert(appdata v)
            {
                v2f o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }

    Fallback "Diffuse"
}
