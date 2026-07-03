using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using static UnityBox.AvatarSecuritySystem.Editor.Constants;
namespace UnityBox.AvatarSecuritySystem.Editor
{
    public class Defense
    {
        private readonly AnimatorController controller;
        private readonly GameObject avatarRoot;
        private readonly ASSComponent config;
        private readonly bool isDebugMode;
        public Defense(AnimatorController controller, GameObject avatarRoot, ASSComponent config, bool isDebugMode = false)
        {
            this.controller = controller;
            this.avatarRoot = avatarRoot;
            this.config = config;
            this.isDebugMode = isDebugMode;
        }
        private static string ParticleRootName => GO_PARTICLE_ROOT;
        private static string LightRootName => GO_LIGHT_ROOT;
        private static string PSObjPrefix => GO_PS_PREFIX;
        private static string SubEmitterPrefix => GO_SUB_EMITTER_PREFIX;
        private static string LightPrefix => GO_LIGHT_PREFIX;
        private static string DefenseMeshName => GO_DEFENSE_MESH;
        private bool IsLightweightDefense => config != null && config.lightweightDefense;
        public void Generate()
        {
            if (config.disableDefense)
            {
                Debug.Log("[ASS] 禁用防御选项已勾选，跳过防御层创建（仅测试密码系统）");
                return;
            }
            if (config.defaultEnableDefense)
            {
                GenerateDefaultDefenseLayer();
                return;
            }
            var reusableLight = FindReusableAvatarLight();
            var layer = Utils.CreateLayer(Constants.LAYER_DEFENSE, 1f);
            layer.blendingMode = AnimatorLayerBlendingMode.Override;
            var inactiveState = layer.stateMachine.AddState(
                Obfuscator.State("Inactive"),
                new Vector3(100, 50, 0));
            inactiveState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_FILE);
            layer.stateMachine.defaultState = inactiveState;
            var activeState = layer.stateMachine.AddState(
                Obfuscator.State("Active"),
                new Vector3(100, 150, 0));
            var activateClip = new AnimationClip
            {
                name = CLIP_DEFENSE_ACTIVATE
            };
            activateClip.SetCurve(Constants.GO_DEFENSE_ROOT, typeof(GameObject), "m_IsActive",
                AnimationCurve.Constant(0f, 1f / 60f, 1f));
            ApplyReusableLightActivationCurves(activateClip, reusableLight);
            Utils.AddSubAsset(controller, activateClip);
            activeState.motion = activateClip;
            var toActive = Utils.CreateTransition(inactiveState, activeState);
            Utils.AddIsLocalCondition(toActive, controller, isTrue: true);
            toActive.AddCondition(AnimatorConditionMode.If, 0, Constants.PARAM_TIME_UP);
            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            Utils.AddSubAsset(controller, layer.stateMachine);
            try
            {
                CreateDefenseComponents(reusableLight);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ASS] CreateDefenseComponents调用失败: {e.Message}\n{e.StackTrace}");
                throw;
            }
            controller.AddLayer(layer);
        }
        private void GenerateDefaultDefenseLayer()
        {
            var reusableLight = FindReusableAvatarLight();
            var layer = Utils.CreateLayer(Constants.LAYER_DEFENSE, 1f);
            layer.blendingMode = AnimatorLayerBlendingMode.Override;
            var inactiveState = layer.stateMachine.AddState(
                Obfuscator.State("Inactive"),
                new Vector3(100, 50, 0));
            inactiveState.motion = Utils.GetOrCreateEmptyClip(ASSET_FOLDER, SHARED_EMPTY_CLIP_FILE);
            layer.stateMachine.defaultState = inactiveState;
            var activeState = layer.stateMachine.AddState(
                Obfuscator.State("Active"),
                new Vector3(100, 150, 0));
            var activateClip = new AnimationClip
            {
                name = CLIP_DEFENSE_ACTIVE_DEFAULT
            };
            activateClip.SetCurve(Constants.GO_DEFENSE_ROOT, typeof(GameObject), "m_IsActive",
                AnimationCurve.Constant(0f, 1f / 60f, 1f));
            ApplyReusableLightActivationCurves(activateClip, reusableLight);
            Utils.AddSubAsset(controller, activateClip);
            activeState.motion = activateClip;
            var toActive = Utils.CreateTransition(inactiveState, activeState);
            Utils.AddIsLocalCondition(toActive, controller, isTrue: true);
            toActive.AddCondition(AnimatorConditionMode.IfNot, 0, Constants.PARAM_PASSWORD_CORRECT);
            layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            Utils.AddSubAsset(controller, layer.stateMachine);
            try
            {
                CreateDefenseComponents(reusableLight);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ASS] CreateDefenseComponents调用失败: {e.Message}\n{e.StackTrace}");
                throw;
            }
            controller.AddLayer(layer);
            Debug.Log("[ASS] Default enable defense: Inactive→Active on IsLocal && !PasswordCorrect");
        }
        private GameObject CreateDefenseComponents(Light reusableLight)
        {
            var existingRoot = avatarRoot.transform.Find(Constants.GO_DEFENSE_ROOT);
            if (existingRoot != null)
                Object.DestroyImmediate(existingRoot.gameObject);
            var root = new GameObject(Constants.GO_DEFENSE_ROOT);
            root.transform.SetParent(avatarRoot.transform);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            root.SetActive(false);
            var parameters = ComputeDefenseParams();
            bool useOverflow = config.enableOverflow && !isDebugMode;
            var particlePlan = BuildParticleGenerationPlan(useOverflow);
            Material sharedDefenseMaterial = particlePlan.SystemCount > 0
                ? CreateSharedDefenseMaterial()
                : null;
            var defenseLights = particlePlan.SystemCount > 0
                ? GetParticleLights(root, parameters.LightCount, reusableLight, allowFallbackLight: !IsLightweightDefense)
                : null;
            if (parameters.ParticleCount > 0)
            {
                if (particlePlan.SystemCount > 0 && particlePlan.ParticleTarget > 0 && particlePlan.MeshPolyTarget > 0)
                    CreateParticleComponents(root, particlePlan.SystemCount, particlePlan.ParticleTarget, particlePlan.MeshPolyTarget,
                        defenseLights, useOverflow, sharedDefenseMaterial, IsLightweightDefense);
            }
            if (parameters.PhysXRigidbodyCount > 0 || parameters.ClothComponentCount > 0)
            {
                Debug.Log("[ASS] Defense optimization: PhysX/Cloth payload disabled to reduce generated object and component count");
            }
            if (IsLightweightDefense)
            {
                Debug.Log("[ASS] Lightweight mode: no new light will be generated; existing avatar lights may still be reused");
            }
            Debug.Log("[ASS] Defense optimization: Dedicated ShaderDefense meshes skipped; heavy shader contribution now comes from particle renderers sharing the defense material");
            return root;
        }
        private readonly struct DefenseParams
        {
            public readonly int PhysXRigidbodyCount;
            public readonly int PhysXColliderCount;
            public readonly int ClothComponentCount;
            public readonly int ParticleCount;
            public readonly int ParticleSystemCount;
            public readonly int LightCount;
            public DefenseParams(
                int physXRigidbodyCount, int physXColliderCount, int clothComponentCount,
                int particleCount, int particleSystemCount, int lightCount)
            {
                PhysXRigidbodyCount = physXRigidbodyCount;
                PhysXColliderCount = physXColliderCount;
                ClothComponentCount = clothComponentCount;
                ParticleCount = particleCount;
                ParticleSystemCount = particleSystemCount;
                LightCount = lightCount;
            }
        }

        private readonly struct ExistingParticleStats
        {
            public readonly int SystemCount;
            public readonly long ParticleCount;
            public readonly long MeshTriangles;

            public ExistingParticleStats(int systemCount, long particleCount, long meshTriangles)
            {
                SystemCount = systemCount;
                ParticleCount = particleCount;
                MeshTriangles = meshTriangles;
            }

            public bool HasExternalParticles => SystemCount > 0 && ParticleCount > 0;
        }

        private readonly struct ParticleGenerationPlan
        {
            public readonly int SystemCount;
            public readonly long ParticleTarget;
            public readonly long MeshPolyTarget;

            public ParticleGenerationPlan(int systemCount, long particleTarget, long meshPolyTarget)
            {
                SystemCount = systemCount;
                ParticleTarget = particleTarget;
                MeshPolyTarget = meshPolyTarget;
            }
        }

        private DefenseParams ComputeDefenseParams()
        {
            const int lightCount = 1;
            if (isDebugMode)
            {
                return new DefenseParams(0, 0, 0, 1, 1, lightCount);
            }
            string lightMode = IsLightweightDefense ? "reuse-existing-only" : "reuse-or-1";
            Debug.Log($"[ASS] Defense profile: MinimalParticleSystems=ON, Lightweight={(IsLightweightDefense ? "ON" : "OFF")}, Lights={lightMode}, PhysX=OFF, Cloth=OFF");
            return new DefenseParams(
                0, 0, 0,
                Constants.PARTICLE_MAX_COUNT, 1,
                lightCount
            );
        }

        private ParticleGenerationPlan BuildParticleGenerationPlan(bool useOverflow)
        {
            if (isDebugMode)
                return new ParticleGenerationPlan(1, 1, 1);

            var existing = GetExistingParticleStats();
            long meshPolyTarget = useOverflow
                ? (long)Constants.MESH_PARTICLE_MAX_POLYGONS + 1L
                : System.Math.Max(0L, (long)Constants.MESH_PARTICLE_MAX_POLYGONS - existing.MeshTriangles);

            if (meshPolyTarget <= 0)
            {
                Debug.Log("[ASS] Particle plan: no remaining mesh polygon budget, skipping generated defense particles");
                return new ParticleGenerationPlan(0, 0, 0);
            }

            if (useOverflow)
            {
                int generatedSystems = existing.HasExternalParticles ? 1 : 2;
                long particleTarget = (long)Constants.PARTICLE_MAX_COUNT * generatedSystems;
                Debug.Log($"[ASS] Particle plan (overflow): existingSystems={existing.SystemCount}, existingParticles={existing.ParticleCount}, generatedSystems={generatedSystems}, generatedParticleTarget={particleTarget}, lightweight={(IsLightweightDefense ? "ON" : "OFF")}");
                return new ParticleGenerationPlan(generatedSystems, particleTarget, meshPolyTarget);
            }

            long remainingParticles = System.Math.Max(0L, (long)Constants.PARTICLE_MAX_COUNT - existing.ParticleCount);
            int systemCount = remainingParticles > 0 ? 1 : 0;
            Debug.Log($"[ASS] Particle plan (capped): existingSystems={existing.SystemCount}, existingParticles={existing.ParticleCount}, remainingParticleBudget={remainingParticles}, generatedSystems={systemCount}, lightweight={(IsLightweightDefense ? "ON" : "OFF")}");
            return new ParticleGenerationPlan(systemCount, remainingParticles, meshPolyTarget);
        }

        private Material CreateSharedDefenseMaterial()
        {
            var heavyShader = GetHeavyDefenseShaderOrNull();
            if (heavyShader != null)
            {
                var material = new Material(heavyShader)
                {
                    name = "ASS_SharedDefenseMat",
                    renderQueue = 3000
                };
                material.enableInstancing = true;
                Debug.Log($"[ASS] Defense optimization: Created single shared defense material from shader '{heavyShader.name}' for all particle renderers");
                return material;
            }

            var reusableAvatarMaterial = FindReusableAvatarMaterial();
            if (reusableAvatarMaterial != null)
            {
                Debug.Log($"[ASS] Defense optimization: Heavy defense shader unavailable, reusing avatar material '{reusableAvatarMaterial.name}' instead");
                return reusableAvatarMaterial;
            }

            return CreateFallbackSharedDefenseMaterial();
        }

        private ExistingParticleStats GetExistingParticleStats()
        {
            int systemCount = 0;
            long particleCount = 0;
            long meshTriangles = 0;

            foreach (var ps in avatarRoot.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null || IsASSManagedTransform(ps.transform))
                    continue;

                systemCount++;
                particleCount += ps.main.maxParticles;

                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                if (renderer == null) continue;

                long trisPerParticle;
                if (renderer.renderMode == ParticleSystemRenderMode.Mesh && renderer.mesh != null)
                    trisPerParticle = renderer.mesh.triangles.Length / 3;
                else
                    trisPerParticle = 2;

                meshTriangles += (long)ps.main.maxParticles * trisPerParticle;
            }

            return new ExistingParticleStats(systemCount, particleCount, meshTriangles);
        }

        private Material FindReusableAvatarMaterial()
        {
            foreach (var renderer in avatarRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || IsASSManagedTransform(renderer.transform))
                    continue;

                var sharedMaterials = renderer.sharedMaterials;
                if (sharedMaterials == null) continue;

                for (int i = 0; i < sharedMaterials.Length; i++)
                {
                    var material = sharedMaterials[i];
                    if (material == null) continue;

                    string shaderName = material.shader != null ? material.shader.name : null;
                    if (shaderName == "UnityBox/UB_Overlay" || shaderName == "UnityBox/UB_Defense")
                        continue;

                    return material;
                }
            }

            return null;
        }

        private Material CreateFallbackSharedDefenseMaterial()
        {
            var shader = Shader.Find("Standard")
                ?? Shader.Find("Particles/Standard Unlit")
                ?? Shader.Find("Unlit/Color");
            if (shader == null)
                return null;

            var material = new Material(shader)
            {
                name = "ASS_SharedDefenseMat"
            };
            material.enableInstancing = true;
            if (material.HasProperty("_Color"))
                material.color = Color.white;
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", 0f);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);

            Debug.Log($"[ASS] Defense optimization: Created single shared fallback material '{material.shader.name}'");
            return material;
        }

        private Light[] GetParticleLights(GameObject root, int fallbackLightCount, Light reusableLight, bool allowFallbackLight)
        {
            if (reusableLight != null)
            {
                Debug.Log($"[ASS] Defense optimization: Reusing avatar light '{reusableLight.name}' for particle light module");
                return new[] { reusableLight };
            }

            if (!allowFallbackLight || fallbackLightCount <= 0)
                return null;

            int existing = avatarRoot.GetComponentsInChildren<Light>(true).Length;
            int budget = Mathf.Max(0, Constants.LIGHT_MAX_COUNT - existing);
            if (budget <= 0)
                return null;

            return CreateLightComponents(root, Mathf.Min(fallbackLightCount, budget));
        }

        private Light FindReusableAvatarLight()
        {
            Light fallback = null;
            foreach (var light in avatarRoot.GetComponentsInChildren<Light>(true))
            {
                if (light == null || IsASSManagedTransform(light.transform))
                    continue;

                if (fallback == null)
                    fallback = light;

                if (light.enabled && light.gameObject.activeInHierarchy)
                    return light;
            }

            return fallback;
        }

        private void ApplyReusableLightActivationCurves(AnimationClip clip, Light reusableLight)
        {
            if (clip == null || reusableLight == null)
                return;

            var enableCurve = AnimationCurve.Constant(0f, 1f / 60f, 1f);
            string lightPath = AnimationUtility.CalculateTransformPath(reusableLight.transform, avatarRoot.transform);
            clip.SetCurve(lightPath, typeof(Light), "m_Enabled", enableCurve);

            Transform current = reusableLight.transform;
            while (current != null && current != avatarRoot.transform)
            {
                string path = AnimationUtility.CalculateTransformPath(current, avatarRoot.transform);
                if (!string.IsNullOrEmpty(path))
                    clip.SetCurve(path, typeof(GameObject), "m_IsActive", enableCurve);

                current = current.parent;
            }
        }

        private bool IsASSManagedTransform(Transform transform)
        {
            Transform current = transform;
            while (current != null && current != avatarRoot.transform)
            {
                if (current.name == GO_OVERLAY
                    || current.name == GO_AUDIO_WARNING
                    || current.name == GO_AUDIO_SUCCESS
                    || current.name == GO_DEFENSE_ROOT)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static void CreateParticleComponents(GameObject root, int systemBudget, long particleTarget,
            long meshPolyBudget, Light[] lights, bool enableOverflow, Material sharedRendererMaterial,
            bool lightweightDefense)
        {
            if (meshPolyBudget <= 0)
            {
                Debug.LogWarning("[ASS] Particle mesh polygon budget exhausted by existing avatar particles, skipping particle defense");
                return;
            }

            var particleRoot = new GameObject(ParticleRootName);
            particleRoot.transform.SetParent(root.transform);
            int meshTriangles;
            Mesh sharedParticleMesh;
            Mesh sharedSubEmitterMesh;
            long idealTrisPerParticle = particleTarget > 0
                ? meshPolyBudget / particleTarget
                : meshPolyBudget;

            if (idealTrisPerParticle >= 8)
            {
                int meshSubdivisions = Mathf.Clamp(
                    Mathf.FloorToInt(Mathf.Sqrt(idealTrisPerParticle / 2f)), 2, 2);
                meshTriangles = meshSubdivisions * meshSubdivisions * 2;
                int meshVertexTarget = meshSubdivisions * meshSubdivisions * 6;
                sharedParticleMesh = GenerateSphereMesh(meshVertexTarget);
                sharedSubEmitterMesh = GenerateSphereMesh(meshVertexTarget);
            }
            else
            {
                meshTriangles = Mathf.Max(1, (int)idealTrisPerParticle);
                sharedParticleMesh = GenerateFanMesh(meshTriangles);
                sharedSubEmitterMesh = GenerateFanMesh(meshTriangles);
            }

            if (!enableOverflow && meshTriangles > 0 && particleTarget > meshPolyBudget / meshTriangles)
            {
                long clampedTarget = meshPolyBudget / meshTriangles;
                Debug.Log($"[ASS] Particle cap plan adjusted by mesh budget: requestedParticles={particleTarget}, trisPerParticle={meshTriangles}, clampedParticles={clampedTarget}");
                particleTarget = clampedTarget;
            }

            Material particleMaterial = sharedRendererMaterial;
            int systemsUsed = 0;
            long particlesUsed = 0;
            var mainSystems = new List<ParticleSystem>();
            var mainObjects = new List<GameObject>();
            while (systemsUsed < systemBudget && particlesUsed < particleTarget)
            {
                long remaining = particleTarget - particlesUsed;
                int remainingSystems = systemBudget - systemsUsed;
                int particlesForThis = (int)System.Math.Min(remaining / remainingSystems, int.MaxValue);
                if (particlesForThis <= 0) break;
                int s = mainSystems.Count;
                var psObj = new GameObject($"{PSObjPrefix}_{s}");
                psObj.transform.SetParent(particleRoot.transform);
                psObj.transform.localPosition = Vector3.zero;
                var ps = psObj.AddComponent<ParticleSystem>();
                var renderer = psObj.GetComponent<ParticleSystemRenderer>();
                var main = ps.main;
                main.duration = 1f;
                main.loop = true;
                main.prewarm = true;
                main.playOnAwake = true;
                main.simulationSpeed = 10000000f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 12f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startColor = new ParticleSystem.MinMaxGradient(
                    Color.HSVToRGB((float)s / systemBudget, 0.8f, 1f),
                    Color.HSVToRGB(((float)s / systemBudget + 0.3f) % 1f, 0.6f, 0.8f)
                );
                main.maxParticles = particlesForThis;
                main.gravityModifier = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startSize3D = true;
                main.startSizeX = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
                main.startSizeY = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
                main.startSizeZ = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
                main.startRotation3D = true;
                main.startRotationX = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
                main.startRotationY = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
                main.startRotationZ = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
                main.flipRotation = 1f;
                main.ringBufferMode = ParticleSystemRingBufferMode.PauseUntilReplaced;
                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = particlesForThis * 10f;
                emission.rateOverDistance = particlesForThis;
                emission.SetBursts(new ParticleSystem.Burst[] {
                    new ParticleSystem.Burst(0f, (short)Mathf.Min(particlesForThis, short.MaxValue), (short)Mathf.Min(particlesForThis, short.MaxValue), 10, 0.1f),
                    new ParticleSystem.Burst(0.5f, (short)Mathf.Min(particlesForThis / 2, short.MaxValue), (short)Mathf.Min(particlesForThis, short.MaxValue), 10, 0.1f)
                });
                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = (s % 3 == 0) ? ParticleSystemShapeType.Sphere :
                                  (s % 3 == 1) ? ParticleSystemShapeType.Cone :
                                                  ParticleSystemShapeType.Box;
                shape.radius = 2f;
                shape.angle = 45f;
                shape.randomDirectionAmount = 0.5f;
                shape.randomPositionAmount = 1f;
                var velocityOverLifetime = ps.velocityOverLifetime;
                velocityOverLifetime.enabled = true;
                velocityOverLifetime.space = ParticleSystemSimulationSpace.World;
                velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(-3f, 3f);
                velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(-3f, 3f);
                velocityOverLifetime.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
                velocityOverLifetime.orbitalX = new ParticleSystem.MinMaxCurve(0.5f, 2f);
                velocityOverLifetime.orbitalY = new ParticleSystem.MinMaxCurve(0.5f, 2f);
                velocityOverLifetime.orbitalZ = new ParticleSystem.MinMaxCurve(0.5f, 2f);
                velocityOverLifetime.radial = new ParticleSystem.MinMaxCurve(-1f, 1f);
                velocityOverLifetime.speedModifier = new ParticleSystem.MinMaxCurve(0.5f, 2f);
                var forceOverLifetime = ps.forceOverLifetime;
                forceOverLifetime.enabled = true;
                forceOverLifetime.x = new ParticleSystem.MinMaxCurve(-2f, 2f);
                forceOverLifetime.y = new ParticleSystem.MinMaxCurve(-1f, 3f);
                forceOverLifetime.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
                forceOverLifetime.randomized = true;
                var colorOverLifetime = ps.colorOverLifetime;
                colorOverLifetime.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(Color.HSVToRGB((float)s / systemBudget, 1f, 1f), 0f),
                        new GradientColorKey(Color.HSVToRGB(((float)s / systemBudget + 0.5f) % 1f, 1f, 1f), 0.5f),
                        new GradientColorKey(Color.HSVToRGB(((float)s / systemBudget + 0.8f) % 1f, 0.8f, 0.6f), 1f)
                    },
                    new GradientAlphaKey[] {
                        new GradientAlphaKey(0f, 0f),
                        new GradientAlphaKey(1f, 0.1f),
                        new GradientAlphaKey(1f, 0.7f),
                        new GradientAlphaKey(0f, 1f)
                    }
                );
                colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);
                var sizeOverLifetime = ps.sizeOverLifetime;
                sizeOverLifetime.enabled = true;
                sizeOverLifetime.separateAxes = true;
                sizeOverLifetime.x = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.2f, 1, 1.5f));
                sizeOverLifetime.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.3f, 1, 1.2f));
                sizeOverLifetime.z = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.2f, 1, 1.5f));
                var rotationOverLifetime = ps.rotationOverLifetime;
                rotationOverLifetime.enabled = true;
                rotationOverLifetime.separateAxes = true;
                rotationOverLifetime.x = new ParticleSystem.MinMaxCurve(-360f, 360f);
                rotationOverLifetime.y = new ParticleSystem.MinMaxCurve(-360f, 360f);
                rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(-360f, 360f);
                var noise = ps.noise;
                noise.enabled = true;
                noise.strength = new ParticleSystem.MinMaxCurve(1f, 3f);
                noise.frequency = 2f;
                noise.scrollSpeed = 1.5f;
                noise.damping = true;
                noise.octaveCount = 4;
                noise.octaveMultiplier = 0.5f;
                noise.octaveScale = 2f;
                noise.quality = ParticleSystemNoiseQuality.High;
                noise.separateAxes = true;
                noise.strengthX = new ParticleSystem.MinMaxCurve(1f, 3f);
                noise.strengthY = new ParticleSystem.MinMaxCurve(1f, 3f);
                noise.strengthZ = new ParticleSystem.MinMaxCurve(1f, 3f);
                noise.positionAmount = new ParticleSystem.MinMaxCurve(1f);
                noise.rotationAmount = new ParticleSystem.MinMaxCurve(0.5f);
                noise.sizeAmount = new ParticleSystem.MinMaxCurve(0.3f);
                if (!lightweightDefense)
                {
                    var collision = ps.collision;
                    collision.enabled = true;
                    collision.type = ParticleSystemCollisionType.World;
                    collision.mode = ParticleSystemCollisionMode.Collision3D;
                    collision.dampen = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
                    collision.bounce = new ParticleSystem.MinMaxCurve(0.5f, 1f);
                    collision.lifetimeLoss = new ParticleSystem.MinMaxCurve(0f, 0.1f);
                    collision.radiusScale = 1f;
                    collision.quality = ParticleSystemCollisionQuality.High;
                    collision.maxCollisionShapes = 256;
                    collision.enableDynamicColliders = true;
                    collision.collidesWith = ~0;
                    collision.sendCollisionMessages = true;
                    collision.multiplyColliderForceByCollisionAngle = true;
                    collision.multiplyColliderForceByParticleSize = true;
                    collision.multiplyColliderForceByParticleSpeed = true;

                    var trails = ps.trails;
                    trails.enabled = true;
                    trails.mode = ParticleSystemTrailMode.PerParticle;
                    trails.ratio = 0.8f;
                    trails.lifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
                    trails.minVertexDistance = 0.05f;
                    trails.worldSpace = true;
                    trails.dieWithParticles = true;
                    trails.textureMode = ParticleSystemTrailTextureMode.Stretch;
                    trails.sizeAffectsWidth = true;
                    trails.sizeAffectsLifetime = false;
                    trails.inheritParticleColor = true;
                    trails.generateLightingData = true;
                    trails.ribbonCount = 1;
                    trails.shadowBias = 0.5f;
                    var trailWidthCurve = new AnimationCurve(
                        new Keyframe(0f, 1f), new Keyframe(0.5f, 0.5f), new Keyframe(1f, 0f));
                    trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, trailWidthCurve);
                }
                var textureSheet = ps.textureSheetAnimation;
                textureSheet.enabled = true;
                textureSheet.mode = ParticleSystemAnimationMode.Grid;
                textureSheet.numTilesX = 4;
                textureSheet.numTilesY = 4;
                textureSheet.animation = ParticleSystemAnimationType.WholeSheet;
                textureSheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 1f);
                textureSheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 15f);
                textureSheet.cycleCount = 3;
                var limitVelocity = ps.limitVelocityOverLifetime;
                limitVelocity.enabled = true;
                limitVelocity.separateAxes = true;
                limitVelocity.limitX = new ParticleSystem.MinMaxCurve(5f);
                limitVelocity.limitY = new ParticleSystem.MinMaxCurve(5f);
                limitVelocity.limitZ = new ParticleSystem.MinMaxCurve(5f);
                limitVelocity.dampen = 0.5f;
                limitVelocity.drag = new ParticleSystem.MinMaxCurve(0.5f, 2f);
                limitVelocity.multiplyDragByParticleSize = true;
                limitVelocity.multiplyDragByParticleVelocity = true;
                var inheritVelocity = ps.inheritVelocity;
                inheritVelocity.enabled = true;
                inheritVelocity.mode = ParticleSystemInheritVelocityMode.Current;
                inheritVelocity.curve = new ParticleSystem.MinMaxCurve(0.5f);
                var lifetimeBySpeed = ps.lifetimeByEmitterSpeed;
                lifetimeBySpeed.enabled = true;
                lifetimeBySpeed.curve = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.5f, 1, 1.5f));
                lifetimeBySpeed.range = new Vector2(0f, 10f);
                var colorBySpeed = ps.colorBySpeed;
                colorBySpeed.enabled = true;
                var speedGradient = new Gradient();
                speedGradient.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(Color.blue, 0f),
                        new GradientColorKey(Color.yellow, 0.5f),
                        new GradientColorKey(Color.red, 1f)
                    },
                    new GradientAlphaKey[] {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(1f, 1f)
                    }
                );
                colorBySpeed.color = new ParticleSystem.MinMaxGradient(speedGradient);
                colorBySpeed.range = new Vector2(0f, 10f);
                var sizeBySpeed = ps.sizeBySpeed;
                sizeBySpeed.enabled = true;
                sizeBySpeed.separateAxes = true;
                sizeBySpeed.x = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.5f, 1, 2f));
                sizeBySpeed.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.5f, 1, 2f));
                sizeBySpeed.z = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.5f, 1, 2f));
                sizeBySpeed.range = new Vector2(0f, 10f);
                var rotationBySpeed = ps.rotationBySpeed;
                rotationBySpeed.enabled = true;
                rotationBySpeed.separateAxes = true;
                rotationBySpeed.x = new ParticleSystem.MinMaxCurve(-360f, 360f);
                rotationBySpeed.y = new ParticleSystem.MinMaxCurve(-360f, 360f);
                rotationBySpeed.z = new ParticleSystem.MinMaxCurve(-360f, 360f);
                rotationBySpeed.range = new Vector2(0f, 10f);
                var externalForces = ps.externalForces;
                externalForces.enabled = true;
                externalForces.multiplier = 10000000f;
                externalForces.multiplierCurve = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 1f, 1, 10000000f));
                Light particleLight = (lights != null && lights.Length > 0) ? lights[s % lights.Length] : null;
                var lightsModule = ps.lights;
                if (particleLight != null)
                {
                    lightsModule.enabled = true;
                    lightsModule.light = particleLight;
                    lightsModule.ratio = 10000000f;
                    lightsModule.useRandomDistribution = true;
                    lightsModule.useParticleColor = true;
                    lightsModule.sizeAffectsRange = true;
                    lightsModule.alphaAffectsIntensity = true;
                    lightsModule.rangeMultiplier = 10000000f;
                    lightsModule.intensityMultiplier = 10000000f;
                    lightsModule.maxLights = enableOverflow ? int.MaxValue : particlesForThis;
                }
                var customData = ps.customData;
                customData.enabled = true;
                customData.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Vector);
                customData.SetVector(ParticleSystemCustomData.Custom1, 0, new ParticleSystem.MinMaxCurve(-1f, 1f));
                customData.SetVector(ParticleSystemCustomData.Custom1, 1, new ParticleSystem.MinMaxCurve(-1f, 1f));
                customData.SetVector(ParticleSystemCustomData.Custom1, 2, new ParticleSystem.MinMaxCurve(-1f, 1f));
                customData.SetVector(ParticleSystemCustomData.Custom1, 3, new ParticleSystem.MinMaxCurve(-1f, 1f));
                customData.SetMode(ParticleSystemCustomData.Custom2, ParticleSystemCustomDataMode.Vector);
                customData.SetVector(ParticleSystemCustomData.Custom2, 0, new ParticleSystem.MinMaxCurve(0f, 1f));
                customData.SetVector(ParticleSystemCustomData.Custom2, 1, new ParticleSystem.MinMaxCurve(0f, 1f));
                customData.SetVector(ParticleSystemCustomData.Custom2, 2, new ParticleSystem.MinMaxCurve(0f, 1f));
                customData.SetVector(ParticleSystemCustomData.Custom2, 3, new ParticleSystem.MinMaxCurve(0f, 1f));
                var trigger = ps.trigger;
                trigger.enabled = true;
                trigger.inside = ParticleSystemOverlapAction.Callback;
                trigger.outside = ParticleSystemOverlapAction.Callback;
                trigger.enter = ParticleSystemOverlapAction.Callback;
                trigger.exit = ParticleSystemOverlapAction.Callback;
                trigger.radiusScale = 1f;
                if (renderer != null)
                {
                    renderer.renderMode = ParticleSystemRenderMode.Mesh;
                    renderer.mesh = sharedParticleMesh;
                    renderer.meshDistribution = ParticleSystemMeshDistribution.UniformRandom;
                    if (particleMaterial != null)
                    {
                        renderer.sharedMaterial = particleMaterial;
                        renderer.trailMaterial = particleMaterial;
                    }
                    renderer.maxParticleSize = 5f;
                    renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                    renderer.receiveShadows = true;
                    renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
                    renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                    renderer.allowOcclusionWhenDynamic = false;
                    renderer.alignment = ParticleSystemRenderSpace.World;
                    renderer.sortMode = ParticleSystemSortMode.Distance;
                    renderer.enableGPUInstancing = true;
                }
                mainSystems.Add(ps);
                mainObjects.Add(psObj);
                systemsUsed++;
                particlesUsed += particlesForThis;
            }
            int mainCount = mainSystems.Count;
            for (int s = 0; s < mainCount && systemsUsed < systemBudget && particlesUsed < particleTarget; s++)
            {
                long remaining = particleTarget - particlesUsed;
                int remainingSubs = Mathf.Min(mainCount - s, systemBudget - systemsUsed);
                int subParticles = (int)System.Math.Min(remaining / Mathf.Max(1, remainingSubs), int.MaxValue);
                if (subParticles <= 0) break;
                var ps = mainSystems[s];
                var psObj = mainObjects[s];
                var subEmitterObj = new GameObject($"{SubEmitterPrefix}_{s}");
                subEmitterObj.transform.SetParent(psObj.transform);
                subEmitterObj.transform.localPosition = Vector3.zero;
                var subPs = subEmitterObj.AddComponent<ParticleSystem>();
                var subRenderer = subEmitterObj.GetComponent<ParticleSystemRenderer>();
                var subMain = subPs.main;
                subMain.duration = 2f;
                subMain.loop = true;
                subMain.prewarm = true;
                subMain.playOnAwake = true;
                subMain.simulationSpeed = 10000000f;
                subMain.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 2f);
                subMain.startSpeed = new ParticleSystem.MinMaxCurve(1f, 4f);
                subMain.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
                subMain.maxParticles = subParticles;
                subMain.gravityModifier = 1.5f;
                subMain.startColor = new ParticleSystem.MinMaxGradient(Color.yellow, Color.red);
                subMain.simulationSpace = ParticleSystemSimulationSpace.World;
                subMain.startSize3D = true;
                subMain.startSizeX = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
                subMain.startSizeY = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
                subMain.startSizeZ = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
                subMain.startRotation3D = true;
                subMain.startRotationX = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
                subMain.startRotationY = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
                subMain.startRotationZ = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
                subMain.flipRotation = 1f;
                subMain.ringBufferMode = ParticleSystemRingBufferMode.PauseUntilReplaced;
                var subEmission = subPs.emission;
                subEmission.enabled = true;
                subEmission.rateOverTime = subParticles * 10f;
                subEmission.rateOverDistance = subParticles;
                subEmission.SetBursts(new ParticleSystem.Burst[] {
                    new ParticleSystem.Burst(0f, (short)Mathf.Min(subParticles, short.MaxValue), (short)Mathf.Min(subParticles, short.MaxValue), 10, 0.1f)
                });
                var subShape = subPs.shape;
                subShape.enabled = true;
                subShape.shapeType = ParticleSystemShapeType.Sphere;
                subShape.radius = 2f;
                subShape.randomDirectionAmount = 0.5f;
                subShape.randomPositionAmount = 1f;
                var subVelocity = subPs.velocityOverLifetime;
                subVelocity.enabled = true;
                subVelocity.space = ParticleSystemSimulationSpace.World;
                subVelocity.x = new ParticleSystem.MinMaxCurve(-3f, 3f);
                subVelocity.y = new ParticleSystem.MinMaxCurve(-3f, 3f);
                subVelocity.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
                subVelocity.orbitalX = new ParticleSystem.MinMaxCurve(0.5f, 2f);
                subVelocity.orbitalY = new ParticleSystem.MinMaxCurve(0.5f, 2f);
                subVelocity.orbitalZ = new ParticleSystem.MinMaxCurve(0.5f, 2f);
                var subForce = subPs.forceOverLifetime;
                subForce.enabled = true;
                subForce.x = new ParticleSystem.MinMaxCurve(-2f, 2f);
                subForce.y = new ParticleSystem.MinMaxCurve(-1f, 3f);
                subForce.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
                subForce.randomized = true;
                var subColor = subPs.colorOverLifetime;
                subColor.enabled = true;
                var subGradient = new Gradient();
                subGradient.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(Color.yellow, 0f),
                        new GradientColorKey(Color.red, 0.5f),
                        new GradientColorKey(Color.black, 1f)
                    },
                    new GradientAlphaKey[] {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(1f, 0.7f),
                        new GradientAlphaKey(0f, 1f)
                    }
                );
                subColor.color = new ParticleSystem.MinMaxGradient(subGradient);
                var subSize = subPs.sizeOverLifetime;
                subSize.enabled = true;
                subSize.separateAxes = true;
                subSize.x = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.2f, 1, 1.5f));
                subSize.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.3f, 1, 1.2f));
                subSize.z = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.2f, 1, 1.5f));
                var subRotation = subPs.rotationOverLifetime;
                subRotation.enabled = true;
                subRotation.separateAxes = true;
                subRotation.x = new ParticleSystem.MinMaxCurve(-360f, 360f);
                subRotation.y = new ParticleSystem.MinMaxCurve(-360f, 360f);
                subRotation.z = new ParticleSystem.MinMaxCurve(-360f, 360f);
                var subNoise = subPs.noise;
                subNoise.enabled = true;
                subNoise.strength = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
                subNoise.frequency = 3f;
                subNoise.quality = ParticleSystemNoiseQuality.High;
                subNoise.octaveCount = 4;
                subNoise.octaveMultiplier = 0.5f;
                subNoise.octaveScale = 2f;
                subNoise.separateAxes = true;
                subNoise.strengthX = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
                subNoise.strengthY = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
                subNoise.strengthZ = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
                subNoise.positionAmount = new ParticleSystem.MinMaxCurve(1f);
                subNoise.rotationAmount = new ParticleSystem.MinMaxCurve(0.5f);
                subNoise.sizeAmount = new ParticleSystem.MinMaxCurve(0.3f);
                if (!lightweightDefense)
                {
                    var subCollision = subPs.collision;
                    subCollision.enabled = true;
                    subCollision.type = ParticleSystemCollisionType.World;
                    subCollision.mode = ParticleSystemCollisionMode.Collision3D;
                    subCollision.quality = ParticleSystemCollisionQuality.High;
                    subCollision.maxCollisionShapes = 256;
                    subCollision.enableDynamicColliders = true;
                    subCollision.collidesWith = ~0;
                    subCollision.sendCollisionMessages = true;

                    var subTrails = subPs.trails;
                    subTrails.enabled = true;
                    subTrails.mode = ParticleSystemTrailMode.PerParticle;
                    subTrails.ratio = 1f;
                    subTrails.lifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
                    subTrails.minVertexDistance = 0.02f;
                    subTrails.worldSpace = true;
                    subTrails.dieWithParticles = true;
                    subTrails.textureMode = ParticleSystemTrailTextureMode.Stretch;
                    subTrails.sizeAffectsWidth = true;
                    subTrails.inheritParticleColor = true;
                    subTrails.generateLightingData = true;
                }
                var subTexSheet = subPs.textureSheetAnimation;
                subTexSheet.enabled = true;
                subTexSheet.mode = ParticleSystemAnimationMode.Grid;
                subTexSheet.numTilesX = 4;
                subTexSheet.numTilesY = 4;
                subTexSheet.animation = ParticleSystemAnimationType.WholeSheet;
                subTexSheet.cycleCount = 3;
                var subLimitVel = subPs.limitVelocityOverLifetime;
                subLimitVel.enabled = true;
                subLimitVel.separateAxes = true;
                subLimitVel.limitX = new ParticleSystem.MinMaxCurve(5f);
                subLimitVel.limitY = new ParticleSystem.MinMaxCurve(5f);
                subLimitVel.limitZ = new ParticleSystem.MinMaxCurve(5f);
                subLimitVel.dampen = 0.5f;
                var subInheritVel = subPs.inheritVelocity;
                subInheritVel.enabled = true;
                subInheritVel.mode = ParticleSystemInheritVelocityMode.Current;
                subInheritVel.curve = new ParticleSystem.MinMaxCurve(0.5f);
                var subLifeBySpeed = subPs.lifetimeByEmitterSpeed;
                subLifeBySpeed.enabled = true;
                subLifeBySpeed.curve = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.5f, 1, 1.5f));
                subLifeBySpeed.range = new Vector2(0f, 10f);
                var subColorBySpeed = subPs.colorBySpeed;
                subColorBySpeed.enabled = true;
                subColorBySpeed.range = new Vector2(0f, 10f);
                var subSizeBySpeed = subPs.sizeBySpeed;
                subSizeBySpeed.enabled = true;
                subSizeBySpeed.separateAxes = true;
                subSizeBySpeed.x = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.5f, 1, 2f));
                subSizeBySpeed.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.5f, 1, 2f));
                subSizeBySpeed.z = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.5f, 1, 2f));
                subSizeBySpeed.range = new Vector2(0f, 10f);
                var subRotBySpeed = subPs.rotationBySpeed;
                subRotBySpeed.enabled = true;
                subRotBySpeed.separateAxes = true;
                subRotBySpeed.x = new ParticleSystem.MinMaxCurve(-360f, 360f);
                subRotBySpeed.y = new ParticleSystem.MinMaxCurve(-360f, 360f);
                subRotBySpeed.z = new ParticleSystem.MinMaxCurve(-360f, 360f);
                subRotBySpeed.range = new Vector2(0f, 10f);
                var subExtForces = subPs.externalForces;
                subExtForces.enabled = true;
                subExtForces.multiplier = 10000000f;
                Light subParticleLight = (lights != null && lights.Length > 0) ? lights[(s + mainCount) % lights.Length] : null;
                var subLightsModule = subPs.lights;
                if (subParticleLight != null)
                {
                    subLightsModule.enabled = true;
                    subLightsModule.light = subParticleLight;
                    subLightsModule.ratio = 10000000f;
                    subLightsModule.useRandomDistribution = true;
                    subLightsModule.useParticleColor = true;
                    subLightsModule.sizeAffectsRange = true;
                    subLightsModule.alphaAffectsIntensity = true;
                    subLightsModule.rangeMultiplier = 10000000f;
                    subLightsModule.intensityMultiplier = 10000000f;
                    subLightsModule.maxLights = enableOverflow ? int.MaxValue : subParticles;
                }
                var subCustomData = subPs.customData;
                subCustomData.enabled = true;
                subCustomData.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Vector);
                subCustomData.SetVector(ParticleSystemCustomData.Custom1, 0, new ParticleSystem.MinMaxCurve(-1f, 1f));
                subCustomData.SetVector(ParticleSystemCustomData.Custom1, 1, new ParticleSystem.MinMaxCurve(-1f, 1f));
                subCustomData.SetVector(ParticleSystemCustomData.Custom1, 2, new ParticleSystem.MinMaxCurve(-1f, 1f));
                subCustomData.SetVector(ParticleSystemCustomData.Custom1, 3, new ParticleSystem.MinMaxCurve(-1f, 1f));
                var subTrigger = subPs.trigger;
                subTrigger.enabled = true;
                subTrigger.inside = ParticleSystemOverlapAction.Callback;
                subTrigger.outside = ParticleSystemOverlapAction.Callback;
                subTrigger.enter = ParticleSystemOverlapAction.Callback;
                subTrigger.exit = ParticleSystemOverlapAction.Callback;
                if (subRenderer != null)
                {
                    subRenderer.renderMode = ParticleSystemRenderMode.Mesh;
                    subRenderer.mesh = sharedSubEmitterMesh;
                    subRenderer.meshDistribution = ParticleSystemMeshDistribution.UniformRandom;
                    if (particleMaterial != null)
                    {
                        subRenderer.sharedMaterial = particleMaterial;
                        subRenderer.trailMaterial = particleMaterial;
                    }
                    subRenderer.maxParticleSize = 5f;
                    subRenderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                    subRenderer.receiveShadows = true;
                    subRenderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                    subRenderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
                    subRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                    subRenderer.allowOcclusionWhenDynamic = false;
                    subRenderer.alignment = ParticleSystemRenderSpace.World;
                    subRenderer.sortMode = ParticleSystemSortMode.Distance;
                    subRenderer.enableGPUInstancing = true;
                }
                var subEmitters = ps.subEmitters;
                subEmitters.enabled = true;
                subEmitters.AddSubEmitter(subPs, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritColor);
                subEmitters.AddSubEmitter(subPs, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritColor | ParticleSystemSubEmitterProperties.InheritSize);
                systemsUsed++;
                particlesUsed += subParticles;
            }
        }

        private static Light[] CreateLightComponents(GameObject root, int lightCount)
        {
            var lightRoot = new GameObject(LightRootName);
            lightRoot.transform.SetParent(root.transform);
            var lightList = new List<Light>(lightCount);
            for (int i = 0; i < lightCount; i++)
            {
                var lightObj = new GameObject($"{LightPrefix}_{i}");
                lightObj.transform.SetParent(lightRoot.transform);
                lightObj.transform.localPosition = Vector3.zero;
                var light = lightObj.AddComponent<Light>();
                if (i % 2 == 0)
                {
                    light.type = LightType.Point;
                }
                else
                {
                    light.type = LightType.Spot;
                    light.spotAngle = 179f;
                    light.innerSpotAngle = 170f;
                }
                light.intensity = 10000000f;
                light.bounceIntensity = 10000000f;
                light.range = 10000000f;
                light.renderMode = LightRenderMode.ForcePixel;
                float hue = (float)i / lightCount;
                light.color = Color.HSVToRGB(hue, 0.5f, 1f);
                light.shadows = LightShadows.Soft;
                light.shadowStrength = 1f;
                light.shadowResolution = UnityEngine.Rendering.LightShadowResolution.VeryHigh;
                light.shadowBias = 0.001f;
                light.shadowNormalBias = 0.4f;
                light.cullingMask = ~0;
                lightList.Add(light);
            }
            return lightList.ToArray();
        }
        private static Mesh GenerateSphereMesh(int targetVertexCount)
        {
            var mesh = new Mesh { name = DefenseMeshName };
            int subdivisions = Mathf.CeilToInt(Mathf.Sqrt(targetVertexCount / 6f));
            subdivisions = Mathf.Clamp(subdivisions, 2, 2);
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int lat = 0; lat <= subdivisions; lat++)
            {
                float theta = lat * Mathf.PI / subdivisions;
                float sinTheta = Mathf.Sin(theta);
                float cosTheta = Mathf.Cos(theta);
                for (int lon = 0; lon <= subdivisions; lon++)
                {
                    float phi = lon * 2 * Mathf.PI / subdivisions;
                    float sinPhi = Mathf.Sin(phi);
                    float cosPhi = Mathf.Cos(phi);
                    Vector3 vertex = new Vector3(
                        cosPhi * sinTheta,
                        cosTheta,
                        sinPhi * sinTheta
                    );
                    vertices.Add(vertex);
                }
            }
            for (int lat = 0; lat < subdivisions; lat++)
            {
                for (int lon = 0; lon < subdivisions; lon++)
                {
                    int first = (lat * (subdivisions + 1)) + lon;
                    int second = first + subdivisions + 1;
                    triangles.Add(first);
                    triangles.Add(second);
                    triangles.Add(first + 1);
                    triangles.Add(second);
                    triangles.Add(second + 1);
                    triangles.Add(first + 1);
                }
            }
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            var uv = new List<Vector2>(vertices.Count);
            var uv2 = new List<Vector2>(vertices.Count);
            var colors = new List<Color>(vertices.Count);
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 v = vertices[i].normalized;
                uv.Add(new Vector2((v.x + 1f) * 0.5f, (v.z + 1f) * 0.5f));
                uv2.Add(new Vector2((v.y + 1f) * 0.5f, (v.x + 1f) * 0.5f));
                colors.Add(new Color(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z), 1f));
            }
            mesh.uv = uv.ToArray();
            mesh.uv2 = uv2.ToArray();
            mesh.colors = colors.ToArray();
            mesh.RecalculateNormals();
#if UNITY_2019_1_OR_NEWER
            mesh.RecalculateTangents();
#endif
            mesh.RecalculateBounds();
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1f);
            return mesh;
        }
        private static Mesh GenerateFanMesh(int triangleCount)
        {
            triangleCount = Mathf.Max(1, triangleCount);
            var mesh = new Mesh { name = DefenseMeshName };
            int vertexCount = triangleCount + 2;
            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];
            var colors = new Color[vertexCount];
            vertices[0] = Vector3.zero;
            normals[0] = Vector3.back;
            uvs[0] = new Vector2(0.5f, 0.5f);
            colors[0] = Color.white;
            for (int i = 0; i <= triangleCount; i++)
            {
                float angle = i * 2f * Mathf.PI / triangleCount;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                vertices[i + 1] = new Vector3(cos, sin, 0f);
                normals[i + 1] = Vector3.back;
                uvs[i + 1] = new Vector2((cos + 1f) * 0.5f, (sin + 1f) * 0.5f);
                colors[i + 1] = Color.HSVToRGB((float)i / triangleCount, 1f, 1f);
            }
            var tris = new int[triangleCount * 3];
            for (int i = 0; i < triangleCount; i++)
            {
                tris[i * 3] = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = i + 2;
            }
            mesh.vertices = vertices;
            mesh.triangles = tris;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.colors = colors;
            mesh.RecalculateBounds();
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
            return mesh;
        }

        private static Shader GetHeavyDefenseShaderOrNull()
        {
            var shader = Obfuscator.GetObfuscatedShader("UnityBox/UB_Defense");
            if (shader != null) return shader;
            return Shader.Find("UnityBox/UB_Defense");
        }
    }
}
