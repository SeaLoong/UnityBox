using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.IO;
using System.Collections.Generic;
using SeaLoongUnityBox;

namespace SeaLoongUnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// 防御系统生成器
    /// 功能：倒计时结束时激活的防御机制（仅构建模式）
    /// 
    /// 防御机制包括：
    /// 1. Shader 替换 - GPU 密集型 Shader 替换所有材质
    /// 2. 防御状态 - 大量虚假 Animator 状态混淆逆向
    /// 3. 粒子系统 - 大量粒子消耗 GPU/CPU
    /// 4. Draw Calls - 额外材质增加渲染负载
    /// 5. 点光源 - 实时光照计算
    /// 6. Cloth 模拟 - 物理模拟消耗 CPU
    /// </summary>
    public static class DefenseSystem
    {
        #region Constants

        private const string SHADER_TEMPLATE_PATH = "Assets/SeaLoong's UnityBox/Editor/AvatarSecuritySystem/SecurityBurnShader.txt";
        private const string GENERATED_ASSETS_PATH = "Assets/SeaLoong's UnityBox/Generated/AvatarSecurity";

        #endregion

        #region Public API

        /// <summary>
        /// 创建防御层
        /// </summary>
        /// <param name="isDebugMode">是否是调试模式（生成简化版）</param>
        public static AnimatorControllerLayer CreateDefenseLayer(
            AnimatorController controller,
            GameObject avatarRoot,
            AvatarSecuritySystemComponent config,
            bool isDebugMode = false)
        {
            var layer = ASSAnimatorUtils.CreateLayer(ASSConstants.LAYER_DEFENSE, 1f);
            layer.blendingMode = AnimatorLayerBlendingMode.Override;

            // 状态：Inactive（防御未激活）
            var inactiveState = layer.stateMachine.AddState("Inactive", new Vector3(100, 50, 0));
            inactiveState.motion = ASSAnimatorUtils.SharedEmptyClip;
            layer.stateMachine.defaultState = inactiveState;

            // 状态：Active（防御激活）
            var activeState = layer.stateMachine.AddState("Active", new Vector3(100, 150, 0));
            activeState.motion = ASSAnimatorUtils.SharedEmptyClip;

            // 调试模式：生成简化版防御
            int stateCount = isDebugMode ? 50 : config.stateCount;
            
            // 直接在stateMachine中生成大量空状态（混淆逆向）
            GenerateDefenseStates(layer.stateMachine, stateCount);
            
            AnimationClip shaderMotion = null;
            AnimationClip particleMotion = null;
            AnimationClip drawCallMotion = null;
            AnimationClip lightMotion = null;
            AnimationClip clothMotion = null;

            if (!isDebugMode)
            {
                // 完整版本：生成所有反制措施
                shaderMotion = GenerateShaderReplacementAnimation(controller, avatarRoot, config);
                particleMotion = GenerateParticleSystemAnimation(controller, avatarRoot, config);
                drawCallMotion = GenerateDrawCallAnimation(controller, avatarRoot, config);
                lightMotion = GenerateLightAnimation(controller, avatarRoot, config);
                clothMotion = GenerateClothAnimation(controller, avatarRoot, config);
                
                // 将反制措施动画应用到Active状态
                activeState.motion = CombineDefenseMotions(controller, 
                    shaderMotion, particleMotion, drawCallMotion, lightMotion, clothMotion);
            }
            else
            {
                // 调试版本：仅生成占位符动画
                Debug.Log(ASSI18n.T("log.simplified_countermeasures"));
            }
            
            // 转换条件：IsLocal && TimeUp
            var toActive = ASSAnimatorUtils.CreateTransition(inactiveState, activeState);
            toActive.AddCondition(AnimatorConditionMode.If, 0, ASSConstants.PARAM_IS_LOCAL);
            toActive.AddCondition(AnimatorConditionMode.If, 0, ASSConstants.PARAM_TIME_UP);

            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            ASSAnimatorUtils.AddSubAsset(controller, layer.stateMachine);

            Debug.Log(string.Format(ASSI18n.T("log.defense_created"), stateCount));
            return layer;
        }

        #endregion

        #region Defense States (防御状态)

        /// <summary>
        /// 直接生成大量空状态（混淆逆向）
        /// </summary>
        private static void GenerateDefenseStates(AnimatorStateMachine stateMachine, int stateCount)
        {
            Debug.Log(string.Format(ASSI18n.T("log.defense_start"), stateCount));

            var sharedClip = ASSAnimatorUtils.SharedEmptyClip;
            
            // 直接生成N个空状态
            // 排列成网格布局避免重叠
            int columns = Mathf.CeilToInt(Mathf.Sqrt(stateCount));
            float spacing = 150f;
            float baseX = 400f;
            float baseY = 50f;

            for (int i = 0; i < stateCount; i++)
            {
                int row = i / columns;
                int col = i % columns;
                
                var defenseState = stateMachine.AddState($"Defense_{i}", 
                    new Vector3(baseX + col * spacing, baseY + row * spacing, 0));
                defenseState.motion = sharedClip;
                defenseState.writeDefaultValues = true;
            }

            Debug.Log(string.Format(ASSI18n.T("log.defense_complete"), stateCount));
        }

        #endregion

        #region Motion Combination (动作混合)

        /// <summary>
        /// 组合防御动画（不包含状态BlendTree）
        /// </summary>
        private static Motion CombineDefenseMotions(
            AnimatorController controller,
            Motion shaderMotion,
            Motion particleMotion,
            Motion drawCallMotion,
            Motion lightMotion,
            Motion clothMotion)
        {
            // 如果只有一个动画，直接返回
            var motions = new List<Motion>();
            if (shaderMotion != null) motions.Add(shaderMotion);
            if (particleMotion != null) motions.Add(particleMotion);
            if (drawCallMotion != null) motions.Add(drawCallMotion);
            if (lightMotion != null) motions.Add(lightMotion);
            if (clothMotion != null) motions.Add(clothMotion);
            
            if (motions.Count == 0)
                return ASSAnimatorUtils.SharedEmptyClip;
            
            if (motions.Count == 1)
                return motions[0];
            
            // 使用Direct BlendTree组合多个动画
            var blendTree = new BlendTree
            {
                name = "ASS_DefenseCombined",
                blendType = BlendTreeType.Direct,
                hideFlags = HideFlags.HideInHierarchy
            };

            var children = new List<ChildMotion>();
            foreach (var motion in motions)
            {
                children.Add(new ChildMotion 
                { 
                    motion = motion, 
                    directBlendParameter = "Unity Reserved", // Direct模式不使用参数
                    timeScale = 1f 
                });
            }

            blendTree.children = children.ToArray();
            ASSAnimatorUtils.AddSubAsset(controller, blendTree);

            return blendTree;
        }

        #endregion

        #region Shader Defense (Shader 防御)

        /// <summary>
        /// 生成 Shader 替换动画
        /// </summary>
        private static AnimationClip GenerateShaderReplacementAnimation(
            AnimatorController controller,
            GameObject avatarRoot,
            AvatarSecuritySystemComponent config)
        {
            Shader burnShader = GenerateBurnShader(avatarRoot.name);
            if (burnShader == null)
            {
                Debug.LogWarning(ASSI18n.T("log.shader_warning"));
                return ASSAnimatorUtils.SharedEmptyClip;
            }

            Material burnMaterial = CreateBurnMaterial(burnShader, avatarRoot.name);
            if (burnMaterial == null)
            {
                Debug.LogWarning(ASSI18n.T("log.material_warning"));
                return ASSAnimatorUtils.SharedEmptyClip;
            }

            var clip = new AnimationClip
            {
                name = "ASS_ShaderReplacement",
                hideFlags = HideFlags.HideInHierarchy
            };

            int replacedCount = 0;

            // 替换所有 SkinnedMeshRenderer
            var skinnedRenderers = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var renderer in skinnedRenderers)
            {
                string path = AnimationUtility.CalculateTransformPath(renderer.transform, avatarRoot.transform);
                
                for (int i = 0; i < renderer.sharedMaterials.Length; i++)
                {
                    var binding = EditorCurveBinding.PPtrCurve(path, typeof(SkinnedMeshRenderer), $"m_Materials.Array.data[{i}]");
                    AnimationUtility.SetObjectReferenceCurve(clip, binding, new ObjectReferenceKeyframe[]
                    {
                        new ObjectReferenceKeyframe { time = 0f, value = burnMaterial }
                    });
                    
                    replacedCount++;
                }
            }

            // 替换所有 MeshRenderer
            var meshRenderers = avatarRoot.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var renderer in meshRenderers)
            {
                string path = AnimationUtility.CalculateTransformPath(renderer.transform, avatarRoot.transform);
                
                for (int i = 0; i < renderer.sharedMaterials.Length; i++)
                {
                    var binding = EditorCurveBinding.PPtrCurve(path, typeof(MeshRenderer), $"m_Materials.Array.data[{i}]");
                    AnimationUtility.SetObjectReferenceCurve(clip, binding, new ObjectReferenceKeyframe[]
                    {
                        new ObjectReferenceKeyframe { time = 0f, value = burnMaterial }
                    });
                    
                    replacedCount++;
                }
            }

            ASSAnimatorUtils.AddSubAsset(controller, clip);
            Debug.Log(string.Format(ASSI18n.T("log.shader_replacement_created"), replacedCount));

            return clip;
        }

        /// <summary>
        /// 生成 GPU 密集型燃烧 Shader
        /// </summary>
        private static Shader GenerateBurnShader(string avatarName)
        {
            string assetDir = $"{GENERATED_ASSETS_PATH}/{avatarName}";
            if (!AssetDatabase.IsValidFolder(assetDir))
            {
                string parentDir = Path.GetDirectoryName(assetDir).Replace('\\', '/');
                if (!AssetDatabase.IsValidFolder(parentDir))
                {
                    AssetDatabase.CreateFolder("Assets/SeaLoong's UnityBox", "Generated");
                    AssetDatabase.CreateFolder("Assets/SeaLoong's UnityBox/Generated", "AvatarSecurity");
                }
                AssetDatabase.CreateFolder(parentDir, avatarName);
            }

            string shaderPath = $"{assetDir}/SecurityBurnShader_{avatarName}.shader";

            if (!File.Exists(SHADER_TEMPLATE_PATH))
            {
                Debug.LogError(string.Format(ASSI18n.T("log.shader_template_missing"), SHADER_TEMPLATE_PATH));
                return null;
            }

            string shaderCode = File.ReadAllText(SHADER_TEMPLATE_PATH);
            shaderCode = shaderCode.Replace(
                "Shader \"SeaLoong/ASS/SecurityBurnShader\"",
                $"Shader \"SeaLoong/ASS/SecurityBurnShader_{avatarName}\""
            );

            File.WriteAllText(shaderPath, shaderCode);
            AssetDatabase.ImportAsset(shaderPath);
            AssetDatabase.Refresh();

            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader == null)
            {
                Debug.LogError(string.Format(ASSI18n.T("log.shader_error_load"), shaderPath));
            }
            else
            {
                Debug.Log(string.Format(ASSI18n.T("log.shader_generated"), shaderPath));
            }

            return shader;
        }

        /// <summary>
        /// 创建使用燃烧 Shader 的 Material
        /// </summary>
        private static Material CreateBurnMaterial(Shader shader, string avatarName)
        {
            string materialPath = $"{GENERATED_ASSETS_PATH}/{avatarName}/SecurityBurnMaterial_{avatarName}.mat";

            var material = new Material(shader)
            {
                name = $"SecurityBurnMaterial_{avatarName}"
            };

            material.SetColor("_BurnColor", new Color(1f, 0.3f, 0f, 1f));
            material.SetFloat("_BurnIntensity", 2.0f);

            AssetDatabase.CreateAsset(material, materialPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(string.Format(ASSI18n.T("log.material_created"), materialPath));
            return material;
        }

        #endregion

        #region Particle System Defense (粒子系统防御)

        /// <summary>
        /// 生成粒子系统防御动画
        /// </summary>
        private static AnimationClip GenerateParticleSystemAnimation(
            AnimatorController controller,
            GameObject avatarRoot,
            AvatarSecuritySystemComponent config)
        {
            if (config.particleSystemCount <= 0)
            {
                Debug.Log(ASSI18n.T("log.particle_disabled"));
                return null;
            }

            var clip = new AnimationClip
            {
                name = "ASS_ParticleSystemDefense",
                hideFlags = HideFlags.HideInHierarchy
            };

            string containerPath = "ASS_ParticleSystems";
            var activeCurve = AnimationCurve.Constant(0f, 1f, 1f);
            clip.SetCurve(containerPath, typeof(GameObject), "m_IsActive", activeCurve);

            for (int i = 0; i < config.particleSystemCount; i++)
            {
                string particlePath = $"{containerPath}/ParticleSystem_{i}";
                clip.SetCurve(particlePath, typeof(GameObject), "m_IsActive", activeCurve);
                
                var emissionCurve = AnimationCurve.Constant(0f, 1f, 1f);
                clip.SetCurve(particlePath, typeof(ParticleSystem), "EmissionModule.enabled", emissionCurve);
            }

            ASSAnimatorUtils.AddSubAsset(controller, clip);
            Debug.Log(string.Format(ASSI18n.T("log.particle_created"), config.particleSystemCount));

            return clip;
        }

        /// <summary>
        /// 创建粒子系统 GameObject
        /// </summary>
        public static void CreateParticleSystemObjects(GameObject avatarRoot, AvatarSecuritySystemComponent config)
        {
            if (config.particleSystemCount <= 0) return;

            Transform container = avatarRoot.transform.Find("ASS_ParticleSystems");
            if (container == null)
            {
                var containerObj = new GameObject("ASS_ParticleSystems");
                containerObj.transform.SetParent(avatarRoot.transform, false);
                containerObj.SetActive(false);
                container = containerObj.transform;
            }

            for (int i = 0; i < config.particleSystemCount; i++)
            {
                var particleObj = new GameObject($"ParticleSystem_{i}");
                particleObj.transform.SetParent(container, false);
                particleObj.transform.localPosition = Random.insideUnitSphere * 0.5f;

                var ps = particleObj.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.loop = true;
                main.startLifetime = 5f;
                main.startSpeed = 2f;
                main.maxParticles = 1000;

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 200f;

                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 1f;

                var renderer = particleObj.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
            }

            Debug.Log(string.Format(ASSI18n.T("log.particle_objects_created"), config.particleSystemCount, config.particleSystemCount * 1000));
        }

        #endregion

        #region Draw Call Defense (Draw Call 防御)

        /// <summary>
        /// 生成 Draw Call 防御动画
        /// </summary>
        private static AnimationClip GenerateDrawCallAnimation(
            AnimatorController controller,
            GameObject avatarRoot,
            AvatarSecuritySystemComponent config)
        {
            if (config.extraMaterialCount <= 0)
            {
                Debug.Log(ASSI18n.T("log.drawcall_disabled"));
                return null;
            }

            var clip = new AnimationClip
            {
                name = "ASS_DrawCallDefense",
                hideFlags = HideFlags.HideInHierarchy
            };

            string containerPath = "ASS_DrawCalls";
            var activeCurve = AnimationCurve.Constant(0f, 1f, 1f);
            clip.SetCurve(containerPath, typeof(GameObject), "m_IsActive", activeCurve);

            for (int i = 0; i < config.extraMaterialCount; i++)
            {
                string meshPath = $"{containerPath}/DrawCallMesh_{i}";
                clip.SetCurve(meshPath, typeof(GameObject), "m_IsActive", activeCurve);
            }

            ASSAnimatorUtils.AddSubAsset(controller, clip);
            Debug.Log(string.Format(ASSI18n.T("log.drawcall_created"), config.extraMaterialCount));

            return clip;
        }

        /// <summary>
        /// 创建 Draw Call 对象
        /// </summary>
        public static void CreateDrawCallObjects(GameObject avatarRoot, AvatarSecuritySystemComponent config)
        {
            if (config.extraMaterialCount <= 0) return;

            Transform container = avatarRoot.transform.Find("ASS_DrawCalls");
            if (container == null)
            {
                var containerObj = new GameObject("ASS_DrawCalls");
                containerObj.transform.SetParent(avatarRoot.transform, false);
                containerObj.SetActive(false);
                container = containerObj.transform;
            }

            Mesh quadMesh = CreateSimpleQuadMesh();
            Shader burnShader = GenerateBurnShader(avatarRoot.name);
            if (burnShader == null)
            {
                Debug.LogWarning(ASSI18n.T("log.drawcall_shader_warning"));
                return;
            }

            for (int i = 0; i < config.extraMaterialCount; i++)
            {
                var meshObj = new GameObject($"DrawCallMesh_{i}");
                meshObj.transform.SetParent(container, false);
                meshObj.transform.localPosition = Random.insideUnitSphere * 0.1f;
                meshObj.transform.localScale = Vector3.one * 0.01f;

                var meshFilter = meshObj.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = quadMesh;

                var meshRenderer = meshObj.AddComponent<MeshRenderer>();
                var material = new Material(burnShader)
                {
                    name = $"DrawCallMaterial_{i}"
                };
                material.SetColor("_BurnColor", Random.ColorHSV());
                material.SetFloat("_BurnIntensity", Random.Range(1f, 3f));
                
                meshRenderer.sharedMaterial = material;
            }

            Debug.Log(string.Format(ASSI18n.T("log.drawcall_objects_created"), config.extraMaterialCount));
        }

        /// <summary>
        /// 创建简单的 Quad Mesh
        /// </summary>
        private static Mesh CreateSimpleQuadMesh()
        {
            var mesh = new Mesh
            {
                name = "ASS_QuadMesh",
                vertices = new Vector3[]
                {
                    new Vector3(-0.5f, -0.5f, 0),
                    new Vector3(0.5f, -0.5f, 0),
                    new Vector3(0.5f, 0.5f, 0),
                    new Vector3(-0.5f, 0.5f, 0)
                },
                triangles = new int[] { 0, 1, 2, 0, 2, 3 },
                normals = new Vector3[]
                {
                    Vector3.forward,
                    Vector3.forward,
                    Vector3.forward,
                    Vector3.forward
                },
                uv = new Vector2[]
                {
                    new Vector2(0, 0),
                    new Vector2(1, 0),
                    new Vector2(1, 1),
                    new Vector2(0, 1)
                }
            };

            mesh.RecalculateBounds();
            return mesh;
        }

        #endregion

        #region Point Light Defense (点光源防御)

        /// <summary>
        /// 生成点光源防御动画
        /// </summary>
        private static AnimationClip GenerateLightAnimation(
            AnimatorController controller,
            GameObject avatarRoot,
            AvatarSecuritySystemComponent config)
        {
            if (config.pointLightCount <= 0)
            {
                Debug.Log(ASSI18n.T("log.light_disabled"));
                return null;
            }

            var clip = new AnimationClip
            {
                name = "ASS_LightDefense",
                hideFlags = HideFlags.HideInHierarchy
            };

            string containerPath = "ASS_Lights";
            var activeCurve = AnimationCurve.Constant(0f, 1f, 1f);
            clip.SetCurve(containerPath, typeof(GameObject), "m_IsActive", activeCurve);

            for (int i = 0; i < config.pointLightCount; i++)
            {
                string lightPath = $"{containerPath}/PointLight_{i}";
                clip.SetCurve(lightPath, typeof(GameObject), "m_IsActive", activeCurve);

                var intensityCurve = AnimationCurve.EaseInOut(0f, 2f, 1f, 0f);
                intensityCurve.postWrapMode = WrapMode.PingPong;
                clip.SetCurve(lightPath, typeof(Light), "m_Intensity", intensityCurve);
            }

            ASSAnimatorUtils.AddSubAsset(controller, clip);
            Debug.Log(string.Format(ASSI18n.T("log.light_created"), config.pointLightCount));

            return clip;
        }

        /// <summary>
        /// 创建点光源对象
        /// </summary>
        public static void CreateLightObjects(GameObject avatarRoot, AvatarSecuritySystemComponent config)
        {
            if (config.pointLightCount <= 0) return;

            Transform container = avatarRoot.transform.Find("ASS_Lights");
            if (container == null)
            {
                var containerObj = new GameObject("ASS_Lights");
                containerObj.transform.SetParent(avatarRoot.transform, false);
                containerObj.SetActive(false);
                container = containerObj.transform;
            }

            for (int i = 0; i < config.pointLightCount; i++)
            {
                var lightObj = new GameObject($"PointLight_{i}");
                lightObj.transform.SetParent(container, false);
                lightObj.transform.localPosition = Random.insideUnitSphere * 1f;

                var light = lightObj.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 5f;
                light.intensity = 2f;
                light.color = Random.ColorHSV();
                light.shadows = LightShadows.Soft;
            }

            Debug.Log(string.Format(ASSI18n.T("log.light_objects_created"), config.pointLightCount));
        }

        #endregion

        #region Cloth Defense (Cloth 防御)

        /// <summary>
        /// 生成 Cloth 防御动画
        /// </summary>
        private static AnimationClip GenerateClothAnimation(
            AnimatorController controller,
            GameObject avatarRoot,
            AvatarSecuritySystemComponent config)
        {
            if (!config.enableClothCountermeasure || config.clothVertexCount <= 0)
            {
                Debug.Log(ASSI18n.T("log.cloth_disabled"));
                return null;
            }

            var clip = new AnimationClip
            {
                name = "ASS_ClothDefense",
                hideFlags = HideFlags.HideInHierarchy
            };

            string containerPath = "ASS_Cloths";
            var activeCurve = AnimationCurve.Constant(0f, 1f, 1f);
            clip.SetCurve(containerPath, typeof(GameObject), "m_IsActive", activeCurve);
            clip.SetCurve($"{containerPath}/ClothPlane", typeof(GameObject), "m_IsActive", activeCurve);

            ASSAnimatorUtils.AddSubAsset(controller, clip);
            Debug.Log(string.Format(ASSI18n.T("log.cloth_created"), config.clothVertexCount));

            return clip;
        }

        /// <summary>
        /// 创建 Cloth 对象
        /// </summary>
        public static void CreateClothObjects(GameObject avatarRoot, AvatarSecuritySystemComponent config)
        {
            if (!config.enableClothCountermeasure || config.clothVertexCount <= 0) return;

            Transform container = avatarRoot.transform.Find("ASS_Cloths");
            if (container == null)
            {
                var containerObj = new GameObject("ASS_Cloths");
                containerObj.transform.SetParent(avatarRoot.transform, false);
                containerObj.SetActive(false);
                container = containerObj.transform;
            }

            Mesh clothMesh = CreateClothMesh(config.clothVertexCount);

            var clothObj = new GameObject("ClothPlane");
            clothObj.transform.SetParent(container, false);
            clothObj.transform.localPosition = Vector3.up * 2f;

            var meshFilter = clothObj.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = clothMesh;

            var meshRenderer = clothObj.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = new Material(Shader.Find("Standard"));

            var cloth = clothObj.AddComponent<Cloth>();
            cloth.stretchingStiffness = 0.5f;
            cloth.bendingStiffness = 0.1f;
            cloth.useTethers = true;
            cloth.useGravity = true;

            Debug.Log(string.Format(ASSI18n.T("log.cloth_objects_created"), clothMesh.vertexCount));
        }

        /// <summary>
        /// 创建高细分度平面 Mesh（用于 Cloth）
        /// </summary>
        private static Mesh CreateClothMesh(int targetVertexCount)
        {
            int resolution = Mathf.CeilToInt(Mathf.Sqrt(targetVertexCount));
            
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();

            float step = 1f / (resolution - 1);

            // 生成顶点
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    vertices.Add(new Vector3(x * step - 0.5f, 0, y * step - 0.5f));
                    normals.Add(Vector3.up);
                    uvs.Add(new Vector2(x * step, y * step));
                }
            }

            // 生成三角形
            for (int y = 0; y < resolution - 1; y++)
            {
                for (int x = 0; x < resolution - 1; x++)
                {
                    int i = y * resolution + x;
                    triangles.Add(i);
                    triangles.Add(i + resolution);
                    triangles.Add(i + 1);

                    triangles.Add(i + 1);
                    triangles.Add(i + resolution);
                    triangles.Add(i + resolution + 1);
                }
            }

            var mesh = new Mesh
            {
                name = "ASS_ClothMesh",
                vertices = vertices.ToArray(),
                triangles = triangles.ToArray(),
                normals = normals.ToArray(),
                uv = uvs.ToArray()
            };

            mesh.RecalculateBounds();
            return mesh;
        }

        #endregion
    }
}
