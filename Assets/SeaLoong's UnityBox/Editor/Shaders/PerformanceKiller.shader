Shader "SeaLoong/PerformanceKiller"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _HeightMap ("Height Map", 2D) = "white" {}
        
        _LoopCount ("Loop Count (GPU 密集循环)", Float) = 100000
        _Intensity ("Intensity (强度倍数)", Float) = 2.5
        _Complexity ("Complexity (复杂度系数)", Float) = 15.0
        _ParallaxScale ("Parallax Height Scale", Float) = 0.15
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
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
            
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            // ========== 纹理和参数 ==========
            sampler2D _MainTex, _NormalMap, _HeightMap;
            float4 _MainTex_ST, _NormalMap_ST, _HeightMap_ST;
            float _LoopCount, _Intensity, _Complexity, _ParallaxScale;
            float4 _BaseColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
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
                SHADOW_COORDS(6)
                UNITY_FOG_COORDS(7)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ========== GPU 密集型计算函数 ==========

            // Perlin 噪声（来自 SecurityBurnShader）
            float hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float noise(float3 x)
            {
                float3 p = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);

                return lerp(
                    lerp(
                        lerp(hash(p + float3(0,0,0)), hash(p + float3(1,0,0)), f.x),
                        lerp(hash(p + float3(0,1,0)), hash(p + float3(1,1,0)), f.x),
                        f.y),
                    lerp(
                        lerp(hash(p + float3(0,0,1)), hash(p + float3(1,0,1)), f.x),
                        lerp(hash(p + float3(0,1,1)), hash(p + float3(1,1,1)), f.x),
                        f.y),
                    f.z);
            }

            // FBM (Fractal Brownian Motion) - 8 层噪声迭代
            float fbm(float3 p)
            {
                float value = 0.0;
                float amplitude = 0.5;
                float frequency = 1.0;

                for (int i = 0; i < 8; i++)
                {
                    value += amplitude * noise(p * frequency);
                    frequency *= 2.0;
                    amplitude *= 0.5;
                }

                return value;
            }

            // 视差映射 - 多层陡峭视差映射
            float2 ParallaxMapping(float2 uv, float3 viewDirTangent)
            {
                float heightScale = _ParallaxScale;
                
                // 陡峭视差映射迭代（8-32 层）
                const int minLayers = 8;
                const int maxLayers = 32;
                int numLayers = int(lerp(maxLayers, minLayers, abs(dot(float3(0, 0, 1), normalize(viewDirTangent)))));
                
                float layerHeight = 1.0 / numLayers;
                float currentLayerHeight = 0.0;
                float2 dtex = uv * heightScale / numLayers;
                float2 currentTextureCoords = uv;
                float currentHeightFromTexture = tex2D(_HeightMap, currentTextureCoords).r;
                
                // 多次纹理采样和迭代计算（GPU 密集）
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
                
                float weight = heightAfterLayer / (heightAfterLayer - heightBeforeLayer + 0.001);
                return mix(currentTextureCoords, prevTexCoords, weight);
            }

            // 复杂法线计算（3 次迭代）
            float3 UnpackNormalComplex(float4 packedNormal, float2 uv)
            {
                float3 normal = UnpackNormal(packedNormal);
                
                // 额外法线计算迭代（GPU 消耗）
                for(int i = 0; i < 3; i++)
                {
                    normal = normalize(normal * normal + 0.1);
                    // 额外的法线重采样
                    float3 normalPerturb = UnpackNormal(tex2D(_NormalMap, uv * (2.0 + i)));
                    normal = normalize(normal + normalPerturb * 0.1);
                }
                
                return normalize(normal);
            }

            // 高消耗 GPU 循环计算（_LoopCount 次）
            float3 ExpensiveColorComputation(float2 uv, float3 normal, float3 viewDir, float3 worldPos)
            {
                float3 color = float3(0, 0, 0);
                
                int loops = int(_LoopCount);
                float loopFactor = 1.0 / max(1.0, float(loops));
                
                // FBM 噪声基础
                float3 noisePos = worldPos * 2.0 + _Time.y * 0.1;
                float baseFbm = fbm(noisePos);
                
                // 主循环 - GPU 密集计算
                for(int i = 0; i < loops; i++)
                {
                    // 计算偏移（三角函数）
                    float offset = float(i) * 0.001 * _Complexity;
                    float sinOffset = sin(offset * 3.14159);
                    float cosOffset = cos(offset * 2.71828);
                    
                    // 偏移后的 UV 采样
                    float2 sampleUv = uv + float2(sinOffset, cosOffset) * 0.01;
                    
                    // 纹理采样（3 次）
                    float4 texSample1 = tex2D(_MainTex, sampleUv);
                    float4 texSample2 = tex2D(_MainTex, sampleUv * 2.0 + offset);
                    float4 texSample3 = tex2D(_NormalMap, sampleUv * 0.5);
                    
                    // 复杂数学计算
                    float mathValue = sin(offset * _Complexity) * 
                                     cos(offset * _Intensity) +
                                     sqrt(abs(sinOffset + baseFbm * 0.1));
                    
                    // 对数和指数计算（GPU 密集）
                    float expVal = exp(mathValue * 0.3);
                    float logVal = log(abs(mathValue) + 1.0);
                    float powVal = pow(abs(mathValue), 2.5);
                    
                    // 法线相关计算
                    float3 normalSample = UnpackNormal(texSample3);
                    float ndl = max(0.0, dot(normalSample, float3(0.577, 0.577, 0.577)));
                    
                    // 累积颜色
                    color += (texSample1.rgb + texSample2.rgb * 0.5) * 
                            (mathValue + expVal + logVal * 0.1 + powVal * 0.1) * 
                            ndl * loopFactor;
                }
                
                return color * _Intensity;
            }

            // 复杂光照计算（多光源模拟）
            float3 CalculateLighting(float3 normal, float3 viewDir, float3 fragPos)
            {
                // 主光源
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float3 halfDir = normalize(lightDir + viewDir);
                
                float diffuse = max(0, dot(normal, lightDir));
                float specPhong = pow(max(0, dot(viewDir, reflect(-lightDir, normal))), 32.0);
                float specBlinn = pow(max(0, dot(normal, halfDir)), 64.0);
                float rim = pow(1.0 - max(0, dot(viewDir, normal)), 3.0) * 0.5;
                
                float3 lighting = _LightColor0.rgb * (diffuse * 0.7 + specPhong * 0.2 + specBlinn * 0.15 + rim * 0.05);
                
                // 模拟多光源计算（8 个伪光源）
                for(int i = 0; i < 8; i++)
                {
                    float angle = float(i) * 0.785398; // 360/8 = 45度
                    float3 pseudoLightPos = fragPos + float3(sin(angle), 0.5 + i * 0.1, cos(angle)) * 2.0;
                    float3 pseudoDir = normalize(pseudoLightPos - fragPos);
                    float pseudoNdl = max(0.0, dot(normal, pseudoDir));
                    float pseudoSpec = pow(max(0.0, dot(normal, normalize(pseudoDir + viewDir))), 32.0);
                    
                    lighting += float3(0.1, 0.1, 0.1) * (pseudoNdl * 0.05 + pseudoSpec * 0.02);
                }
                
                return lighting;
            }

            // RGB <-> HSV 转换（色彩空间转换 - GPU 消耗）
            float3 rgb2hsv(float3 c)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            float3 hsv2rgb(float3 c)
            {
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            // ========== 顶点着色器 ==========
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.tangent = UnityObjectToWorldDir(v.tangent.xyz);
                o.binormal = cross(o.normal, o.tangent) * v.tangent.w;
                
                o.viewDir = _WorldSpaceCameraPos - o.worldPos;
                
                TRANSFER_SHADOW(o);
                UNITY_TRANSFER_FOG(o, o.pos);

                return o;
            }

            // ========== 片元着色器 ==========
            float4 frag(v2f i) : SV_Target
            {
                // 构建 TBN 矩阵
                float3x3 TBN = float3x3(i.tangent, i.binormal, i.normal);
                
                // 视差映射（GPU 密集）
                float3 viewDirTangent = mul(TBN, normalize(i.viewDir));
                float2 parallaxUv = ParallaxMapping(i.uv, viewDirTangent);
                
                // 基础纹理采样
                float4 baseColor = tex2D(_MainTex, parallaxUv) * _BaseColor;
                
                // 法线计算（GPU 密集）
                float4 normalMap = tex2D(_NormalMap, parallaxUv);
                float3 normal = UnpackNormalComplex(normalMap, parallaxUv);
                normal = normalize(mul(normal, TBN));
                
                // 高消耗 GPU 循环计算
                float3 expensiveColor = ExpensiveColorComputation(parallaxUv, normal, normalize(i.viewDir), i.worldPos);
                
                // 光照计算
                float3 lighting = CalculateLighting(normal, normalize(i.viewDir), i.worldPos);
                float shadow = SHADOW_ATTENUATION(i);
                
                // 颜色合成
                float3 finalColor = (baseColor.rgb + expensiveColor * 0.3) * lighting * shadow;
                
                // HSV 色彩空间变换（GPU 消耗）
                float3 hsv = rgb2hsv(finalColor);
                hsv.x += _Time.y * 0.05;
                finalColor = hsv2rgb(hsv);
                
                // sRGB 编码/解码（GPU 消耗）
                finalColor = pow(finalColor, 2.2);     // 编码
                finalColor = saturate(finalColor);
                finalColor = pow(finalColor, 1.0/2.2); // 线性化
                
                // 额外颜色混合
                finalColor = lerp(finalColor, 1.0 - finalColor, sin(_Time.y * 0.5) * 0.1 + 0.05);
                
                // 应用雾效
                UNITY_APPLY_FOG(i.fogCoord, float4(finalColor, 1.0));
                
                return float4(finalColor, baseColor.a);
            }
            ENDCG
        }

        Pass
        {
            Name "SHADOWCASTER"
            Tags { "LightMode"="ShadowCaster" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                V2F_SHADOW_CASTER;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
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

    FallBack "Diffuse"
}
