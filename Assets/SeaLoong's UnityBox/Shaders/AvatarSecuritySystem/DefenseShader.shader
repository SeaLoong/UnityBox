Shader "SeaLoong/DefenseShader"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _HeightMap ("Height Map", 2D) = "white" {}
        _DetailTex ("Detail Texture", 2D) = "white" {}
        _NoiseTex ("Noise Texture", 2D) = "white" {}
        
        _LoopCount ("Loop Count (GPU 密集循环)", Float) = 10000
        _Intensity ("Intensity (强度倍数)", Float) = 1000.0
        _Complexity ("Complexity (复杂度系数)", Float) = 10000.0
        _ParallaxScale ("Parallax Height Scale", Float) = 100.0
        _NoiseOctaves ("Noise Octaves (噪声层数)", Float) = 128.0
        _SamplingRate ("Sampling Rate (采样率倍数)", Float) = 256.0
        _ColorPasses ("Color Space Passes (色彩通道数)", Float) = 250.0
        _LightCount ("Pseudo Light Count (伪光源数)", Float) = 32.0
        _RayMarchSteps ("Ray March Steps (光线步进)", Float) = 32.0
        _SubsurfaceScattering ("Subsurface Scattering (次表面散射)", Float) = 16.0
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        
        // 新增参数
        _Glossiness ("Glossiness (光泽度)", Float) = 1.0
        _Metallic ("Metallic (金属度)", Float) = 1.0
        _OcclusionStrength ("Occlusion Strength (环境光遮蔽强度)", Float) = 5.0
        _EmissionIntensity ("Emission Intensity (自发光强度)", Float) = 5.0
        _RimPower ("Rim Power (边缘光强度)", Float) = 16.0
        _DetailScale ("Detail Scale (细节纹理缩放)", Float) = 50.0
        _NoiseScale ("Noise Scale (噪声纹理缩放)", Float) = 25.0
        _ParallaxIterations ("Parallax Iterations (视差迭代次数)", Float) = 256.0
        _ReflectionSamples ("Reflection Samples (反射采样次数)", Float) = 128.0
        _RefractionStrength ("Refraction Strength (折射强度)", Float) = 1.0
        _DispersionStrength ("Dispersion Strength (色散强度)", Float) = 0.5
        _Anisotropy ("Anisotropy (各向异性)", Float) = 1.0
        _ClearCoat ("Clear Coat (清漆层)", Float) = 1.0
        _Sheen ("Sheen (光泽)", Float) = 1.0
        _SubsurfaceColor ("Subsurface Color (次表面颜色)", Color) = (1, 0.5, 0.5, 1)
        _Thickness ("Thickness (厚度)", Float) = 2.0
        _Transmission ("Transmission (透射)", Float) = 1.0
        _Absorption ("Absorption (吸收)", Float) = 1.0
        
        // 新增极端GPU密集参数
        _FractalIterations ("Fractal Iterations (分形迭代)", Float) = 128.0
        _WaveLength ("Wave Length (波长)", Float) = 0.001
        _Turbulence ("Turbulence (湍流)", Float) = 1000.0
        _DistortionStrength ("Distortion Strength (扭曲强度)", Float) = 5.0
        _VolumetricSteps ("Volumetric Steps (体积采样)", Float) = 64.0
        _CloudLayers ("Cloud Layers (云层)", Float) = 8.0
        _ParticleDensity ("Particle Density (粒子密度)", Float) = 1000.0
        _ShadowSamples ("Shadow Samples (阴影采样)", Float) = 16.0
        _GlobalIllumination ("Global Illumination (全局光照)", Float) = 8.0
        _CausticSamples ("Caustic Samples (焦散采样)", Float) = 32.0
        _MotionBlurStrength ("Motion Blur (运动模糊)", Float) = 1.0
        _DepthOfFieldStrength ("Depth of Field (景深)", Float) = 1.0
        _ChromaticAberration ("Chromatic Aberration (色差)", Float) = 0.5
        _LensFlareIntensity ("Lens Flare (镜头光晕)", Float) = 20.0
        _GrainStrength ("Grain (颗粒)", Float) = 1.0
        _VignetteStrength ("Vignette (暗角)", Float) = 1.0
        
        // 新增额外极端参数
        _MoireIntensity ("Moire Intensity (莫尔纹)", Float) = 5.0
        _DitherStrength ("Dither Strength (抖动)", Float) = 1.0
        _PixelateSize ("Pixelate Size (像素化)", Float) = 0.001
        _HologramIntensity ("Hologram Intensity (全息)", Float) = 5.0
        _Iridescence ("Iridescence (彩虹色)", Float) = 5.0
        _VelvetIntensity ("Velvet Intensity (天鹅绒)", Float) = 5.0
        _FresnelPower ("Fresnel Power (菲涅尔强度)", Float) = 10.0
        _BumpMapStrength ("Bump Map Strength (法线强度)", Float) = 5.0
        _HeightMapStrength ("Height Map Strength (高度图强度)", Float) = 5.0
        _ParallaxIntensity ("Parallax Intensity (视差强度)", Float) = 5.0
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
            sampler2D _MainTex, _NormalMap, _HeightMap, _DetailTex, _NoiseTex;
            float4 _MainTex_ST, _NormalMap_ST, _HeightMap_ST, _DetailTex_ST, _NoiseTex_ST;
            float _LoopCount, _Intensity, _Complexity, _ParallaxScale;
            float _NoiseOctaves, _SamplingRate, _ColorPasses;
            float _LightCount, _RayMarchSteps, _SubsurfaceScattering;
            float4 _BaseColor;
            
            // 新增参数
            float _Glossiness, _Metallic, _OcclusionStrength;
            float _EmissionIntensity, _RimPower;
            float _DetailScale, _NoiseScale;
            float _ParallaxIterations, _ReflectionSamples;
            float _RefractionStrength, _DispersionStrength;
            float _Anisotropy, _ClearCoat, _Sheen;
            float4 _SubsurfaceColor;
            float _Thickness, _Transmission, _Absorption;
            
            // 新增极端GPU密集参数
            float _FractalIterations, _WaveLength, _Turbulence;
            float _DistortionStrength, _VolumetricSteps;
            float _CloudLayers, _ParticleDensity;
            float _ShadowSamples, _GlobalIllumination;
            float _CausticSamples, _MotionBlurStrength;
            float _DepthOfFieldStrength, _ChromaticAberration;
            float _LensFlareIntensity, _GrainStrength;
            float _VignetteStrength;
            
            // 新增额外极端参数
            float _MoireIntensity, _DitherStrength, _PixelateSize;
            float _HologramIntensity, _Iridescence, _VelvetIntensity;
            float _FresnelPower, _BumpMapStrength, _HeightMapStrength;
            float _ParallaxIntensity;

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

            // ========== 极端GPU密集型计算函数 ==========

            // 分形噪声（优化迭代次数）
            float FractalNoise(float3 p)
            {
                float value = 0.0;
                float amplitude = 0.5;
                float frequency = 1.0;

                // 大幅限制最大迭代次数以避免GPU过载
                int iterations = min(int(_FractalIterations), 32);
                iterations = max(iterations, 4);
                for (int i = 0; i < iterations; i++)
                {
                    float3 q = p * frequency + float3(sin(_Time.y * 0.1 + i * 0.2), cos(_Time.y * 0.15 + i * 0.3), sin(_Time.y * 0.2 + i * 0.4));
                    value += amplitude * (sin(q.x) * cos(q.y) + sin(q.z) * cos(q.x));
                    frequency *= 1.5;
                    amplitude *= 0.5;
                }

                return value;
            }

            // 湍流扭曲（优化GPU密集计算）
            float3 TurbulenceDistortion(float2 uv, float time)
            {
                float3 distortion = float3(0, 0, 0);

                // 大幅限制层数以避免GPU过载
                int layers = min(8, int(_NoiseOctaves / 4.0));
                layers = max(layers, 2);
                for (int i = 0; i < layers; i++)
                {
                    float frequency = pow(2.0, i);
                    float amplitude = pow(0.5, i);
                    float3 noisePos = float3(uv * frequency * _WaveLength, time * 0.1 + i * 0.2);
                    distortion += FractalNoise(noisePos) * amplitude * float3(1, 1, 1);
                }

                return distortion * _Turbulence;
            }

            // 体积云渲染（优化GPU密集计算）
            float3 VolumetricClouds(float3 worldPos, float3 viewDir)
            {
                float3 color = float3(0, 0, 0);
                // 大幅限制最大步数以避免嵌套循环导致的GPU过载
                int steps = min(int(_VolumetricSteps), 16);
                steps = max(steps, 4);

                for (int i = 0; i < steps; i++)
                {
                    float3 samplePos = worldPos + viewDir * (float(i) / float(steps));
                    float3 noisePos = samplePos * 0.1;
                    float density = 0.0;

                    // 大幅限制云层数以避免嵌套循环过载
                    int cloudLayers = min(int(_CloudLayers), 4);
                    cloudLayers = max(cloudLayers, 1);
                    for (int j = 0; j < cloudLayers; j++)
                    {
                        float3 layerNoisePos = noisePos + float3(0, j * 0.5, 0);
                        density += FractalNoise(layerNoisePos) * 0.1;
                    }

                    color += float3(density, density, density) * 0.01;
                }

                return color;
            }

            // 粒子系统模拟（优化GPU密集计算）
            float3 ParticleSimulation(float2 uv)
            {
                float3 color = float3(0, 0, 0);
                // 大幅严格限制粒子数量以避免GPU过载
                int particleCount = min(int(_ParticleDensity), 100);
                particleCount = max(particleCount, 10);

                // 固定步长避免整数除法问题
                int stepSize = 5;

                for (int i = 0; i < particleCount; i += stepSize)
                {
                    float3 particlePos = float3(
                        sin(i * 12345.6789 + _Time.y * 0.5) * 0.5,
                        cos(i * 98765.4321 + _Time.y * 0.7) * 0.5,
                        sin(i * 54321.9876 + _Time.y * 0.9) * 0.5
                    );

                    float dist = length(particlePos.xy - uv);
                    float radius = 0.01 * (1.0 + sin(i * 0.1 + _Time.y) * 0.5);

                    if (dist < radius)
                    {
                        color += float3(1, 0.5, 0.2) * exp(-dist * dist / (radius * radius));
                    }
                }

                return color;
            }

            // 全局光照模拟（优化GPU密集计算）
            float3 GlobalIllumination(float3 worldPos, float3 normal)
            {
                float3 color = float3(0, 0, 0);
                // 大幅限制采样次数以避免GPU过载
                int samples = min(int(_GlobalIllumination), 8);
                samples = max(samples, 2);

                for (int i = 0; i < samples; i++)
                {
                    float3 randomDir = normalize(float3(
                        sin(i * 0.123 + _Time.y),
                        cos(i * 0.456 + _Time.y),
                        sin(i * 0.789 + _Time.y)
                    ));

                    float3 reflectDir = reflect(-randomDir, normal);
                    float3 samplePos = worldPos + reflectDir * 0.1;

                    color += FractalNoise(samplePos) * float3(0.1, 0.1, 0.1);
                }

                return color;
            }

            // 焦散效果（优化GPU密集计算）
            float3 CausticEffect(float3 worldPos, float3 lightDir)
            {
                float3 color = float3(0, 0, 0);
                // 大幅限制采样次数以避免GPU过载
                int samples = min(int(_CausticSamples), 8);
                samples = max(samples, 2);

                for (int i = 0; i < samples; i++)
                {
                    float3 randomDir = normalize(float3(
                        sin(i * 0.123 + _Time.y),
                        cos(i * 0.456 + _Time.y),
                        sin(i * 0.789 + _Time.y)
                    ));

                    float3 refractedDir = refract(-lightDir, float3(0, 1, 0), 1.33);
                    float3 samplePos = worldPos + refractedDir * 0.01;

                    color += pow(FractalNoise(samplePos), 4.0) * float3(0.2, 0.3, 0.5);
                }

                return color;
            }

            // 景深效果（优化GPU密集计算）
            float4 DepthOfField(float4 color, float2 uv, float depth)
            {
                float4 result = float4(0, 0, 0, 0);
                // 大幅限制采样数以避免GPU过载
                int samples = 8;
                samples = max(samples, 2);

                for (int i = 0; i < samples; i++)
                {
                    float2 offset = float2(
                        sin(i * 0.123 + _Time.y) * _DepthOfFieldStrength * 0.01,
                        cos(i * 0.456 + _Time.y) * _DepthOfFieldStrength * 0.01
                    );

                    float4 sampleColor = tex2D(_MainTex, uv + offset);
                    result += sampleColor;
                }

                return result / samples;
            }

            // 运动模糊（优化GPU密集计算）
            float4 MotionBlur(float4 color, float2 uv)
            {
                float4 result = float4(0, 0, 0, 0);
                // 大幅限制采样数以避免GPU过载
                int samples = 4;
                samples = max(samples, 2);

                for (int i = 0; i < samples; i++)
                {
                    float2 offset = float2(
                        sin(i * 0.123 + _Time.y) * _MotionBlurStrength * 0.01,
                        cos(i * 0.456 + _Time.y) * _MotionBlurStrength * 0.01
                    );

                    float4 sampleColor = tex2D(_MainTex, uv + offset);
                    result += sampleColor;
                }

                return result / samples;
            }

            // 色差效果（极端GPU密集）
            float4 ChromaticAberration(float4 color, float2 uv, float3 viewDir)
            {
                float2 center = float2(0.5, 0.5);
                float distance = length(uv - center);
                float offset = distance * _ChromaticAberration;
                
                float r = tex2D(_MainTex, uv + viewDir.xy * offset * 0.01).r;
                float g = tex2D(_MainTex, uv + viewDir.xy * offset * 0.005).g;
                float b = tex2D(_MainTex, uv - viewDir.xy * offset * 0.01).b;
                
                return float4(r, g, b, color.a);
            }

            // 镜头光晕（优化GPU密集计算）
            float3 LensFlare(float3 worldPos, float3 lightDir)
            {
                float3 color = float3(0, 0, 0);
                // 大幅限制采样数以避免GPU过载
                int samples = 4;
                samples = max(samples, 2);

                for (int i = 0; i < samples; i++)
                {
                    float3 flarePos = worldPos + lightDir * (float(i) / float(samples));
                    float3 noisePos = flarePos * 0.1;

                    float flareIntensity = pow(FractalNoise(noisePos), 2.0);
                    color += float3(1, 0.8, 0.2) * flareIntensity * _LensFlareIntensity;
                }

                return color;
            }

            // 颗粒效果（优化GPU密集计算）
            float3 GrainEffect(float2 uv)
            {
                float3 color = float3(0, 0, 0);
                // 大幅限制采样数以避免GPU过载
                int samples = 8;
                samples = max(samples, 2);

                for (int i = 0; i < samples; i++)
                {
                    float2 noisePos = uv + float2(
                        sin(i * 0.123 + _Time.y),
                        cos(i * 0.456 + _Time.y)
                    ) * 0.01;

                    color += FractalNoise(float3(noisePos, _Time.y)) * _GrainStrength;
                }

                return color;
            }

            // 暗角效果（优化GPU密集计算）
            float VignetteEffect(float2 uv)
            {
                float2 center = float2(0.5, 0.5);
                float distance = length(uv - center);
                float vignette = pow(1.0 - distance * 0.8, _VignetteStrength);

                return vignette;
            }

            // 阴影采样（优化GPU密集计算）
            float ShadowSampling(float3 worldPos, float3 lightDir)
            {
                float shadow = 1.0;
                // 大幅限制采样次数以避免GPU过载
                int samples = min(int(_ShadowSamples), 4);
                samples = max(samples, 2);

                for (int i = 0; i < samples; i++)
                {
                    float3 randomOffset = float3(
                        sin(i * 0.123 + _Time.y),
                        cos(i * 0.456 + _Time.y),
                        sin(i * 0.789 + _Time.y)
                    ) * 0.01;

                    float3 samplePos = worldPos + randomOffset;
                    float shadowValue = FractalNoise(samplePos);
                    shadow *= shadowValue;
                }

                return shadow;
            }

            // 新增：莫尔纹效果（优化GPU密集计算）
            float3 MoireEffect(float2 uv, float3 normal)
            {
                float3 color = float3(0, 0, 0);
                // 大幅限制图案数量以避免GPU过载
                int patterns = 4;
                patterns = max(patterns, 2);

                for (int i = 0; i < patterns; i++)
                {
                    float patternScale = float(i) * 10.0;
                    float patternAngle = float(i) * 3.14159 / 16.0;
                    float2 patternDir = float2(cos(patternAngle), sin(patternAngle));
                    float pattern = sin(dot(uv * patternScale, patternDir));
                    color += float3(pattern, pattern, pattern) * 0.1;
                }

                return color * _MoireIntensity;
            }

            // 新增：抖动效果（优化GPU密集计算）
            float3 DitherEffect(float2 uv)
            {
                float3 color = float3(0, 0, 0);
                // 大幅限制层级数以避免GPU过载
                int levels = 4;
                levels = max(levels, 2);

                for (int i = 0; i < levels; i++)
                {
                    float2 offset = float2(
                        sin(i * 0.123 + uv.y * _DitherStrength),
                        cos(i * 0.456 + uv.x * _DitherStrength)
                    ) * 0.01;

                    float4 dither = tex2D(_MainTex, uv + offset);
                    color += dither.rgb * 0.125;
                }

                return color;
            }

            // 新增：像素化效果（优化GPU密集计算）
            float4 PixelateEffect(float4 color, float2 uv)
            {
                float pixelSize = _PixelateSize;
                float2 pixelUv = float2(
                    floor(uv.x / pixelSize) * pixelSize,
                    floor(uv.y / pixelSize) * pixelSize
                );

                return tex2D(_MainTex, pixelUv);
            }

            // 新增：全息效果（优化GPU密集计算）
            float3 HologramEffect(float3 normal, float3 viewDir, float2 uv)
            {
                float3 color = float3(0, 0, 0);
                // 大幅限制条纹数量以避免GPU过载
                int bands = 4;
                bands = max(bands, 2);

                for (int i = 0; i < bands; i++)
                {
                    float band = sin(uv.y * 100.0 + float(i) * 0.5 + _Time.y * 2.0);
                    float fresnel = pow(1.0 - max(0.0, dot(normal, viewDir)), _FresnelPower);
                    color += float3(0.2, 0.5, 1.0) * band * fresnel * 0.1;
                }

                return color * _HologramIntensity;
            }

            // 新增：彩虹色效果（优化GPU密集计算）
            float3 IridescenceEffect(float3 normal, float3 viewDir)
            {
                float3 color = float3(0, 0, 0);
                // 大幅限制步数以避免GPU过载
                int steps = 4;
                steps = max(steps, 2);
                
                for (int i = 0; i < steps; i++)
                {
                    float hue = float(i) / float(steps);
                    float3 hsl2rgb = float3(
                        hue,
                        1.0,
                        0.5 + 0.5 * sin(_Time.y + float(i) * 0.5)
                    );
                    
                    float fresnel = pow(1.0 - max(0.0, dot(normal, viewDir)), 3.0);
                    color += hsl2rgb * fresnel * 0.1;
                }
                
                return color * _Iridescence;
            }

            // 新增：天鹅绒效果（GPU密集）
            float3 VelvetEffect(float3 normal, float3 lightDir, float3 viewDir)
            {
                float3 color = float3(0, 0, 0);
                // 限制采样数以避免GPU过载
                int samples = 16;
                
                for (int i = 0; i < samples; i++)
                {
                    float offset = float(i) / float(samples);
                    float3 halfDir = normalize(lightDir + viewDir + float3(offset, offset * 0.5, offset * 0.25));
                    float velvet = pow(max(0.0, dot(normal, halfDir)), 8.0);
                    color += float3(velvet, velvet, velvet) * 0.1;
                }
                
                return color * _VelvetIntensity;
            }

            // ========== GPU 密集型计算函数 ==========

            // Perlin 噪声（多次迭代版本）
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

            // FBM (Fractal Brownian Motion) - 512 层噪声迭代（GPU 密集）
            float fbm(float3 p)
            {
                float value = 0.0;
                float amplitude = 0.5;
                float frequency = 1.0;

                int octaves = int(_NoiseOctaves);
                for (int i = 0; i < octaves; i++)
                {
                    value += amplitude * noise(p * frequency);
                    frequency *= 2.0;
                    amplitude *= 0.5;
                }

                return value;
            }

            // 视差映射 - 简化版本避免循环中的梯度指令问题
            float2 ParallaxMapping(float2 uv, float3 viewDirTangent)
            {
                float heightScale = _ParallaxScale;
                
                // 固定迭代次数避免动态循环问题
                int numLayers = 32;
                
                float layerHeight = 1.0 / numLayers;
                float currentLayerHeight = 0.0;
                float2 dtex = uv * heightScale / numLayers;
                float2 currentTextureCoords = uv;
                // 使用tex2Dlod避免梯度指令问题
                float currentHeightFromTexture = tex2Dlod(_HeightMap, float4(currentTextureCoords, 0, 0)).r;
                
                // 固定次数循环（避免动态分支和梯度指令）
                float2 finalCoords = currentTextureCoords;
                for(int i = 0; i < 32; i++)
                {
                    currentLayerHeight += layerHeight;
                    currentTextureCoords -= dtex;
                    // 使用tex2Dlod避免梯度指令问题
                    float heightFromTexture = tex2Dlod(_HeightMap, float4(currentTextureCoords, 0, 0)).r;
                    
                    // 条件选择而不是break，避免动态分支
                    finalCoords = (heightFromTexture < currentLayerHeight && finalCoords == uv)
                        ? currentTextureCoords : finalCoords;
                }
                
                return finalCoords;
            }

            // 复杂法线计算（5 次迭代）
            float3 UnpackNormalComplex(float4 packedNormal, float2 uv)
            {
                float3 normal = UnpackNormal(packedNormal);
                
                // 5 次迭代（GPU 成本增加）
                for(int i = 0; i < 5; i++)
                {
                    normal = normalize(normal * normal + 0.1);
                    // 额外的法线重采样
                    float3 normalPerturb = UnpackNormal(tex2D(_NormalMap, uv * (2.0 + i * 0.5)));
                    normal = normalize(normal + normalPerturb * _BumpMapStrength);
                }
                
                return normalize(normal);
            }

            // 光线步进（Ray Marching）- GPU 密集功能
            float RayMarch(float3 ro, float3 rd, float2 uv)
            {
                float t = 0.0;
                int steps = min(int(_RayMarchSteps), 64); // 限制最大步数以避免Shader展开问题
                steps = max(steps, 8);
                
                for(int i = 0; i < steps; i++)
                {
                    float3 p = ro + rd * t;
                    float height = tex2D(_HeightMap, uv + p.xy * 0.1).r;
                    float dist = p.z - height;
                    
                    if(abs(dist) < 0.001)
                        break;
                    
                    t += dist * 0.5;
                }
                
                return t;
            }

            // 次表面散射（Subsurface Scattering）- GPU 密集功能
            float3 SubsurfaceScattering(float3 normal, float3 viewDir, float3 lightDir, float3 albedo)
            {
                float3 sss = float3(0, 0, 0);
                int steps = min(int(_SubsurfaceScattering), 32); // 限制最大步数
                steps = max(steps, 8);
                
                for(int i = 0; i < steps; i++)
                {
                    float offset = float(i) / float(steps);
                    float3 scatterDir = normalize(lightDir + normal * offset);
                    float scatter = pow(max(0.0, dot(-viewDir, scatterDir)), 8.0);
                    sss += albedo * scatter * (1.0 / float(steps));
                }
                
                return sss * _SubsurfaceColor.rgb;
            }

            // 高消耗 GPU 循环计算（_LoopCount 次）
            float3 ExpensiveColorComputation(float2 uv, float3 normal, float3 viewDir, float3 worldPos)
            {
                float3 color = float3(0, 0, 0);
                
                int loops = min(int(_LoopCount), 256); // 限制最大循环次数
                loops = max(loops, 16);
                float loopFactor = 1.0 / max(1.0, float(loops));
                
                // FBM 噪声基础
                float3 noisePos = worldPos * _NoiseScale + _Time.y * 0.2;
                float baseFbm = fbm(noisePos);
                
                // 主循环 - GPU 密集计算
                for(int i = 0; i < loops; i++)
                {
                    // 计算偏移（三角函数）
                    float offset = float(i) * 0.001 * _Complexity;
                    float sinOffset = sin(offset * 3.14159);
                    float cosOffset = cos(offset * 2.71828);
                    float tanOffset = tan(offset * 1.5708);
                    
                    // 偏移后的 UV 采样
                    float2 sampleUv = uv + float2(sinOffset, cosOffset) * 0.02;
                    
                    // 纹理采样（5 次，升级版）
                    float4 texSample1 = tex2D(_MainTex, sampleUv);
                    float4 texSample2 = tex2D(_MainTex, sampleUv * 2.0 + offset);
                    float4 texSample3 = tex2D(_MainTex, sampleUv * 0.5 + offset * 2.0);
                    float4 texSample4 = tex2D(_NormalMap, sampleUv * 3.0);
                    float4 texSample5 = tex2D(_HeightMap, sampleUv * 1.5 + offset);
                    
                    // 额外纹理采样
                    float4 detailSample = tex2D(_DetailTex, sampleUv * _DetailScale + offset);
                    float4 noiseSample = tex2D(_NoiseTex, sampleUv * _NoiseScale + offset);
                    
                    // 复杂数学计算
                    float mathValue = sin(offset * _Complexity) * 
                                     cos(offset * _Intensity) +
                                     sqrt(abs(sinOffset + baseFbm * 0.2)) +
                                     tan(offset * 0.5);
                    
                    // 对数和指数计算（GPU 密集）
                    float expVal = exp(mathValue * 0.5);
                    float logVal = log(abs(mathValue) + 1.5);
                    float powVal = pow(abs(mathValue), 3.5);
                    float atan2Val = atan(sinOffset / (cosOffset + 0.1));
                    
                    // 双曲函数计算
                    float sinhVal = sinh(mathValue * 0.3);
                    float coshVal = cosh(mathValue * 0.3);
                    float asinVal = asin(saturate(mathValue * 0.5));
                    float acosVal = acos(saturate(mathValue * 0.5));
                    
                    // 法线相关计算
                    float3 normalSample = UnpackNormal(texSample4);
                    float ndl = max(0.0, dot(normalSample, float3(0.577, 0.577, 0.577)));
                    float nds = max(0.0, dot(normalSample, float3(-0.707, 0, 0.707)));
                    
                    // 累积颜色
                    color += (texSample1.rgb + texSample2.rgb * 0.5 + texSample3.rgb * 0.3 + 
                             detailSample.rgb * 0.2 + noiseSample.rgb * 0.15) * 
                            (mathValue + expVal + logVal * 0.2 + powVal * 0.15 + atan2Val * 0.1 +
                             sinhVal * 0.05 + coshVal * 0.05 + asinVal * 0.03 + acosVal * 0.03) * 
                            (ndl + nds * 0.5) * loopFactor;
                }
                
                return color * _Intensity;
            }

            // 复杂光照计算（128 伪光源）
            float3 CalculateLighting(float3 normal, float3 viewDir, float3 fragPos)
            {
                // 主光源
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float3 halfDir = normalize(lightDir + viewDir);
                
                float diffuse = max(0, dot(normal, lightDir));
                float specPhong = pow(max(0, dot(viewDir, reflect(-lightDir, normal))), 32.0);
                float specBlinn = pow(max(0, dot(normal, halfDir)), 64.0);
                float rim = pow(1.0 - max(0, dot(viewDir, normal)), _RimPower) * 0.5;
                
                float3 lighting = _LightColor0.rgb * (diffuse * 0.7 + specPhong * 0.2 + specBlinn * 0.15 + rim * 0.05);
                
                // 128 伪光源（GPU 密集）
                int lightCount = min(int(_LightCount), 64); // 限制最大光源数
                lightCount = max(lightCount, 8);
                for(int i = 0; i < lightCount; i++)
                {
                    float angle = float(i) * 0.196349 * 2.0;
                    float radius = 2.0 + sin(angle + _Time.y) * 0.5;
                    float3 pseudoLightPos = fragPos + float3(sin(angle), 0.5 + i * 0.05, cos(angle)) * radius;
                    float3 pseudoDir = normalize(pseudoLightPos - fragPos);
                    float pseudoNdl = max(0.0, dot(normal, pseudoDir));
                    float pseudoSpec = pow(max(0.0, dot(normal, normalize(pseudoDir + viewDir))), 32.0);
                    
                    float distance = length(pseudoLightPos - fragPos);
                    float attenuation = 1.0 / (1.0 + distance * distance * 0.1);
                    
                    float3 lightColor = float3(sin(angle), cos(angle * 0.7), sin(angle * 1.3)) * 0.5 + 0.5;
                    lighting += lightColor * (pseudoNdl * 0.08 + pseudoSpec * 0.04) * attenuation;
                }
                
                return lighting;
            }

            // RGB <-> HSV 转换（多次转换）
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

            // 多次色彩空间转换（GPU 密集）
            float3 MultipleColorSpaceConversion(float3 color, int passes)
            {
                for(int i = 0; i < passes; i++)
                {
                    float3 hsv = rgb2hsv(color);
                    hsv.x += _Time.y * 0.1 * float(i);
                    hsv.y = saturate(hsv.y * (1.0 + float(i) * 0.1));
                    hsv.z = pow(hsv.z, 1.0 + float(i) * 0.05);
                    color = hsv2rgb(hsv);
                }
                return color;
            }

            // 反射采样（GPU 密集）
            float3 SampleReflection(float3 viewDir, float3 normal, float2 uv)
            {
                float3 reflection = float3(0, 0, 0);
                int samples = min(int(_ReflectionSamples), 64); // 限制最大采样数
                samples = max(samples, 8);
                
                for(int i = 0; i < samples; i++)
                {
                    float offset = float(i) / float(samples);
                    float3 reflectDir = reflect(-viewDir, normalize(normal + float3(offset, offset * 0.5, offset * 0.25)));
                    float4 reflectSample = tex2D(_MainTex, uv + reflectDir.xy * 0.1);
                    reflection += reflectSample.rgb * (1.0 / float(samples));
                }
                
                return reflection;
            }

            // 折射和色散（GPU 密集）
            float3 ApplyRefraction(float3 color, float2 uv, float3 normal)
            {
                float3 refracted = float3(0, 0, 0);
                
                float3 refractDirR = refract(float3(0, 0, 1), normal, 1.0 / (1.0 + _RefractionStrength));
                float3 refractDirG = refract(float3(0, 0, 1), normal, 1.0 / (1.0 + _RefractionStrength + _DispersionStrength));
                float3 refractDirB = refract(float3(0, 0, 1), normal, 1.0 / (1.0 + _RefractionStrength + _DispersionStrength * 2.0));
                
                refracted.r = tex2D(_MainTex, uv + refractDirR.xy * 0.05).r;
                refracted.g = tex2D(_MainTex, uv + refractDirG.xy * 0.05).g;
                refracted.b = tex2D(_MainTex, uv + refractDirB.xy * 0.05).b;
                
                return lerp(color, refracted, _RefractionStrength);
            }

            // 各向异性高光
            float AnisotropicSpecular(float3 normal, float3 viewDir, float3 lightDir, float3 tangent)
            {
                float3 halfDir = normalize(lightDir + viewDir);
                float3 bitangent = cross(normal, tangent);
                
                float anisoDot = dot(halfDir, normalize(tangent * _Anisotropy + bitangent * (1.0 - _Anisotropy)));
                float anisoSpec = pow(max(0.0, anisoDot), 32.0);
                
                return anisoSpec;
            }

            // 清漆层效果
            float ClearCoatEffect(float3 normal, float3 viewDir, float3 lightDir)
            {
                float3 halfDir = normalize(lightDir + viewDir);
                float clearCoat = pow(max(0.0, dot(normal, halfDir)), 8.0);
                return clearCoat * _ClearCoat;
            }

            // 光泽效果
            float3 SheenEffect(float3 normal, float3 viewDir, float3 lightDir)
            {
                float sheen = pow(max(0.0, dot(normal, normalize(lightDir + viewDir))), 2.0);
                return float3(sheen, sheen, sheen) * _Sheen;
            }

            // 透射效果
            float3 TransmissionEffect(float3 color, float3 normal, float3 viewDir)
            {
                float transmission = pow(max(0.0, dot(-viewDir, normal)), _Thickness);
                return color * transmission * _Transmission;
            }

            // 吸收效果
            float3 AbsorptionEffect(float3 color, float distance)
            {
                float absorption = exp(-distance * _Absorption);
                return color * absorption;
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
                
                // 视差映射（简化）
                float3 viewDirTangent = mul(TBN, normalize(i.viewDir));
                float2 parallaxUv = ParallaxMapping(i.uv, viewDirTangent) * _ParallaxIntensity;
                
                // 基础纹理采样
                float4 baseColor = tex2D(_MainTex, parallaxUv) * _BaseColor;
                
                // 法线计算（简化）
                float4 normalMap = tex2D(_NormalMap, parallaxUv);
                float3 normal = UnpackNormal(normalMap);
                normal = normalize(mul(normal, TBN));
                
                // 高消耗 GPU 循环计算（核心消耗点）
                float3 expensiveColor = ExpensiveColorComputation(parallaxUv, normal, normalize(i.viewDir), i.worldPos);
                
                // 次表面散射
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float3 sss = SubsurfaceScattering(normal, normalize(i.viewDir), lightDir, baseColor.rgb);
                
                // 光照计算（简化）
                float3 lighting = CalculateLighting(normal, normalize(i.viewDir), i.worldPos);
                float shadow = SHADOW_ATTENUATION(i);
                
                // 反射采样（简化）
                float3 reflection = SampleReflection(normalize(i.viewDir), normal, parallaxUv);
                
                // 透射效果
                float3 transmission = TransmissionEffect(baseColor.rgb, normal, normalize(i.viewDir));
                
                // 吸收效果
                float3 absorbedColor = AbsorptionEffect(baseColor.rgb, length(i.viewDir));
                
                // 简化版本：只使用核心高消耗计算，避免过多函数调用导致编译器崩溃
                // 分形噪声
                float3 fractalNoise = TurbulenceDistortion(parallaxUv, _Time.y);
                
                // 粒子模拟
                float3 particles = ParticleSimulation(parallaxUv);
                
                // 颗粒效果
                float3 grain = GrainEffect(parallaxUv);
                
                // 暗角效果
                float vignette = VignetteEffect(parallaxUv);
                
                // 莫尔纹效果
                float3 moire = MoireEffect(parallaxUv, normal);
                
                // 全息效果
                float3 hologram = HologramEffect(normal, normalize(i.viewDir), parallaxUv);
                
                // 颜色合成（简化版）
                float3 finalColor = (baseColor.rgb + expensiveColor * 0.5 + sss * 0.3) * lighting * shadow;
                finalColor = lerp(finalColor, reflection, _Glossiness * 0.3);
                finalColor *= _OcclusionStrength;
                finalColor += baseColor.rgb * _EmissionIntensity * 0.1;
                finalColor += fractalNoise * 0.1;
                finalColor += particles * 0.3;
                finalColor *= vignette;
                finalColor += moire * 0.1;
                finalColor += grain * 0.05;
                finalColor += hologram * 0.1;
                
                // sRGB 编码/解码（简化）
                finalColor = pow(finalColor, 0.8);
                
                // 额外颜色混合
                finalColor = lerp(finalColor, 1.0 - finalColor, sin(_Time.y * 0.75) * 0.15 + 0.08);
                
                // 应用雾效
                UNITY_APPLY_FOG(i.fogCoord, float4(finalColor, baseColor.a));
                
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
