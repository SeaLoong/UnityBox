using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using VRC.SDKBase.Editor.BuildPipeline;

#if NDMF_AVAILABLE
using nadena.dev.ndmf;
[assembly: ExportsPlugin(typeof(SeaLoongUnityBox.Editor.BuildPipelineValidatorPlugin))]
#endif

namespace SeaLoongUnityBox.Editor
{
    /// <summary>
    /// Unity 构建进程验证器 - 全面测试所有构建回调接口
    /// 每个接口都测试 -100, 0, 100 三种 callbackOrder
    /// 通过菜单控制各个管线的日志输出
    /// </summary>

    #region ==================== 设置管理 ====================
    
    public static class BuildPipelineValidatorSettings
    {
        private const string PREF_PREFIX = "SeaLoong.BuildPipelineValidator.";
        
        /// <summary>
        /// Asset Pipeline - 资产导入管线 (AssetPostprocessor)
        /// </summary>
        public static bool AssetPipeline
        {
            get => EditorPrefs.GetBool(PREF_PREFIX + "AssetPipeline", false);
            set => EditorPrefs.SetBool(PREF_PREFIX + "AssetPipeline", value);
        }
        
        /// <summary>
        /// Build Pipeline - Unity 构建管线 (BuildPlayerProcessor, IPreprocessBuild, IProcessScene, IPreprocessShaders 等)
        /// </summary>
        public static bool BuildPipeline
        {
            get => EditorPrefs.GetBool(PREF_PREFIX + "BuildPipeline", false);
            set => EditorPrefs.SetBool(PREF_PREFIX + "BuildPipeline", value);
        }
        
        /// <summary>
        /// VRCSDK - VRChat SDK 回调 (IVRCSDKBuildRequested, IVRCSDKPreprocessAvatar, IVRCSDKPostprocessAvatar)
        /// </summary>
        public static bool VRCSDK
        {
            get => EditorPrefs.GetBool(PREF_PREFIX + "VRCSDK", false);
            set => EditorPrefs.SetBool(PREF_PREFIX + "VRCSDK", value);
        }
        
        /// <summary>
        /// NDMF - Non-Destructive Modular Framework 阶段 (Resolving, Generating, Transforming, Optimizing)
        /// </summary>
        public static bool NDMF
        {
            get => EditorPrefs.GetBool(PREF_PREFIX + "NDMF", false);
            set => EditorPrefs.SetBool(PREF_PREFIX + "NDMF", value);
        }
    }
    
    public static class BuildPipelineValidatorMenu
    {
        private const string MENU_PREFIX = "Tools/Build Pipeline Validator/";
        
        [MenuItem(MENU_PREFIX + "Asset Pipeline")]
        private static void ToggleAssetPipeline() => BuildPipelineValidatorSettings.AssetPipeline = !BuildPipelineValidatorSettings.AssetPipeline;
        [MenuItem(MENU_PREFIX + "Asset Pipeline", true)]
        private static bool ValidateAssetPipeline() { Menu.SetChecked(MENU_PREFIX + "Asset Pipeline", BuildPipelineValidatorSettings.AssetPipeline); return true; }
        
        [MenuItem(MENU_PREFIX + "Build Pipeline")]
        private static void ToggleBuildPipeline() => BuildPipelineValidatorSettings.BuildPipeline = !BuildPipelineValidatorSettings.BuildPipeline;
        [MenuItem(MENU_PREFIX + "Build Pipeline", true)]
        private static bool ValidateBuildPipeline() { Menu.SetChecked(MENU_PREFIX + "Build Pipeline", BuildPipelineValidatorSettings.BuildPipeline); return true; }
        
        [MenuItem(MENU_PREFIX + "VRCSDK")]
        private static void ToggleVRCSDK() => BuildPipelineValidatorSettings.VRCSDK = !BuildPipelineValidatorSettings.VRCSDK;
        [MenuItem(MENU_PREFIX + "VRCSDK", true)]
        private static bool ValidateVRCSDK() { Menu.SetChecked(MENU_PREFIX + "VRCSDK", BuildPipelineValidatorSettings.VRCSDK); return true; }
        
        [MenuItem(MENU_PREFIX + "NDMF")]
        private static void ToggleNDMF() => BuildPipelineValidatorSettings.NDMF = !BuildPipelineValidatorSettings.NDMF;
        [MenuItem(MENU_PREFIX + "NDMF", true)]
        private static bool ValidateNDMF() { Menu.SetChecked(MENU_PREFIX + "NDMF", BuildPipelineValidatorSettings.NDMF); return true; }
        
        [MenuItem(MENU_PREFIX + "Enable All")]
        private static void EnableAll()
        {
            BuildPipelineValidatorSettings.AssetPipeline = true;
            BuildPipelineValidatorSettings.BuildPipeline = true;
            BuildPipelineValidatorSettings.VRCSDK = true;
            BuildPipelineValidatorSettings.NDMF = true;
            Debug.Log("[Build Pipeline Validator] 已启用所有管线日志");
        }
        
        [MenuItem(MENU_PREFIX + "Disable All")]
        private static void DisableAll()
        {
            BuildPipelineValidatorSettings.AssetPipeline = false;
            BuildPipelineValidatorSettings.BuildPipeline = false;
            BuildPipelineValidatorSettings.VRCSDK = false;
            BuildPipelineValidatorSettings.NDMF = false;
            Debug.Log("[Build Pipeline Validator] 已禁用所有管线日志");
        }
    }
    
    #endregion

    #region ==================== BuildPlayerProcessor ====================
    
    public class BuildPlayerProcessor_Early : BuildPlayerProcessor
    {
        public override int callbackOrder => -100;
        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                Debug.Log($"[Build Pipeline] BuildPlayerProcessor.PrepareForBuild (Order: {callbackOrder})");
        }
    }
    
    public class BuildPlayerProcessor_Mid : BuildPlayerProcessor
    {
        public override int callbackOrder => 0;
        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                Debug.Log($"[Build Pipeline] BuildPlayerProcessor.PrepareForBuild (Order: {callbackOrder})");
        }
    }
    
    public class BuildPlayerProcessor_Late : BuildPlayerProcessor
    {
        public override int callbackOrder => 100;
        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                Debug.Log($"[Build Pipeline] BuildPlayerProcessor.PrepareForBuild (Order: {callbackOrder})");
        }
    }
    
    #endregion

    #region ==================== IPreprocessBuildWithReport ====================
    
    public class PreBuildValidator_Early : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                Debug.Log($"[Build Pipeline] IPreprocessBuildWithReport.OnPreprocessBuild (Order: {callbackOrder})");
        }
    }
    
    public class PreBuildValidator_Mid : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                Debug.Log($"[Build Pipeline] IPreprocessBuildWithReport.OnPreprocessBuild (Order: {callbackOrder})");
        }
    }
    
    public class PreBuildValidator_Late : IPreprocessBuildWithReport
    {
        public int callbackOrder => 100;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                Debug.Log($"[Build Pipeline] IPreprocessBuildWithReport.OnPreprocessBuild (Order: {callbackOrder})");
        }
    }
    
    #endregion

    #region ==================== IFilterBuildAssemblies ====================
    
    public class FilterAssembliesValidator_Early : IFilterBuildAssemblies
    {
        public int callbackOrder => -100;
        public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                Debug.Log($"[Build Pipeline] IFilterBuildAssemblies.OnFilterAssemblies (Order: {callbackOrder}) Assemblies: {assemblies.Length}");
            return assemblies;
        }
    }
    
    public class FilterAssembliesValidator_Mid : IFilterBuildAssemblies
    {
        public int callbackOrder => 0;
        public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                Debug.Log($"[Build Pipeline] IFilterBuildAssemblies.OnFilterAssemblies (Order: {callbackOrder}) Assemblies: {assemblies.Length}");
            return assemblies;
        }
    }
    
    public class FilterAssembliesValidator_Late : IFilterBuildAssemblies
    {
        public int callbackOrder => 100;
        public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                Debug.Log($"[Build Pipeline] IFilterBuildAssemblies.OnFilterAssemblies (Order: {callbackOrder}) Assemblies: {assemblies.Length}");
            return assemblies;
        }
    }
    
    #endregion

    #region ==================== IPostBuildPlayerScriptDLLs ====================
    
    public class PostBuildScriptDLLsValidator_Early : IPostBuildPlayerScriptDLLs
    {
        public int callbackOrder => -100;
        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                Debug.Log($"[Build Pipeline] IPostBuildPlayerScriptDLLs.OnPostBuildPlayerScriptDLLs (Order: {callbackOrder})");
        }
    }
    
    public class PostBuildScriptDLLsValidator_Mid : IPostBuildPlayerScriptDLLs
    {
        public int callbackOrder => 0;
        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                Debug.Log($"[Build Pipeline] IPostBuildPlayerScriptDLLs.OnPostBuildPlayerScriptDLLs (Order: {callbackOrder})");
        }
    }
    
    public class PostBuildScriptDLLsValidator_Late : IPostBuildPlayerScriptDLLs
    {
        public int callbackOrder => 100;
        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                Debug.Log($"[Build Pipeline] IPostBuildPlayerScriptDLLs.OnPostBuildPlayerScriptDLLs (Order: {callbackOrder})");
        }
    }
    
    #endregion

    #region ==================== IProcessSceneWithReport ====================
    
    public class ProcessSceneValidator_Early : IProcessSceneWithReport
    {
        public int callbackOrder => -100;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                Debug.Log($"[Build Pipeline] IProcessSceneWithReport.OnProcessScene (Order: {callbackOrder}) Scene: {scene.name}");
        }
    }
    
    public class ProcessSceneValidator_Mid : IProcessSceneWithReport
    {
        public int callbackOrder => 0;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                Debug.Log($"[Build Pipeline] IProcessSceneWithReport.OnProcessScene (Order: {callbackOrder}) Scene: {scene.name}");
        }
    }
    
    public class ProcessSceneValidator_Late : IProcessSceneWithReport
    {
        public int callbackOrder => 100;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                Debug.Log($"[Build Pipeline] IProcessSceneWithReport.OnProcessScene (Order: {callbackOrder}) Scene: {scene.name}");
        }
    }
    
    #endregion

    #region ==================== IPreprocessShaders ====================
    
    public class PreprocessShadersValidator_Early : IPreprocessShaders
    {
        public int callbackOrder => -100;
        private static bool _logged = false;
        
        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (!_logged && BuildPipelineValidatorSettings.BuildPipeline)
            {
                Debug.Log($"[Build Pipeline] IPreprocessShaders.OnProcessShader (Order: {callbackOrder}) Shader: {shader.name}");
                _logged = true;
            }
        }
    }
    
    public class PreprocessShadersValidator_Mid : IPreprocessShaders
    {
        public int callbackOrder => 0;
        private static bool _logged = false;
        
        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (!_logged && BuildPipelineValidatorSettings.BuildPipeline)
            {
                Debug.Log($"[Build Pipeline] IPreprocessShaders.OnProcessShader (Order: {callbackOrder}) Shader: {shader.name}");
                _logged = true;
            }
        }
    }
    
    public class PreprocessShadersValidator_Late : IPreprocessShaders
    {
        public int callbackOrder => 100;
        private static bool _logged = false;
        
        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (!_logged && BuildPipelineValidatorSettings.BuildPipeline)
            {
                Debug.Log($"[Build Pipeline] IPreprocessShaders.OnProcessShader (Order: {callbackOrder}) Shader: {shader.name}");
                _logged = true;
            }
        }
    }
    
    #endregion

    #region ==================== IPreprocessComputeShaders ====================
    
    public class PreprocessComputeShadersValidator_Early : IPreprocessComputeShaders
    {
        public int callbackOrder => -100;
        private static bool _logged = false;
        
        public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> data)
        {
            if (!_logged && BuildPipelineValidatorSettings.BuildPipeline)
            {
                Debug.Log($"[Build Pipeline] IPreprocessComputeShaders.OnProcessComputeShader (Order: {callbackOrder}) Shader: {shader.name}, Kernel: {kernelName}");
                _logged = true;
            }
        }
    }
    
    public class PreprocessComputeShadersValidator_Mid : IPreprocessComputeShaders
    {
        public int callbackOrder => 0;
        private static bool _logged = false;
        
        public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> data)
        {
            if (!_logged && BuildPipelineValidatorSettings.BuildPipeline)
            {
                Debug.Log($"[Build Pipeline] IPreprocessComputeShaders.OnProcessComputeShader (Order: {callbackOrder}) Shader: {shader.name}, Kernel: {kernelName}");
                _logged = true;
            }
        }
    }
    
    public class PreprocessComputeShadersValidator_Late : IPreprocessComputeShaders
    {
        public int callbackOrder => 100;
        private static bool _logged = false;
        
        public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> data)
        {
            if (!_logged && BuildPipelineValidatorSettings.BuildPipeline)
            {
                Debug.Log($"[Build Pipeline] IPreprocessComputeShaders.OnProcessComputeShader (Order: {callbackOrder}) Shader: {shader.name}, Kernel: {kernelName}");
                _logged = true;
            }
        }
    }
    
    #endregion

    // IUnityLinkerProcessor 需要 Unity.Build.Pipeline 包，在VRChat项目中可能不可用
    // 如需测试，请取消注释并添加包引用

    /*
    #region ==================== IUnityLinkerProcessor ====================
    
    public class UnityLinkerValidator_Early : IUnityLinkerProcessor
    {
        public int callbackOrder => -100;
        
        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            Debug.Log($"[Build Pipeline] IUnityLinkerProcessor.GenerateAdditionalLinkXmlFile (Order: {callbackOrder})");
            return null;
        }
    }
    
    public class UnityLinkerValidator_Mid : IUnityLinkerProcessor
    {
        public int callbackOrder => 0;
        
        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            Debug.Log($"[Build Pipeline] IUnityLinkerProcessor.GenerateAdditionalLinkXmlFile (Order: {callbackOrder})");
            return null;
        }
    }
    
    public class UnityLinkerValidator_Late : IUnityLinkerProcessor
    {
        public int callbackOrder => 100;
        
        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            Debug.Log($"[Build Pipeline] IUnityLinkerProcessor.GenerateAdditionalLinkXmlFile (Order: {callbackOrder})");
            return null;
        }
    }
    
    #endregion
    */

    #region ==================== IPostprocessBuildWithReport ====================
    
    public class PostBuildValidator_Early : IPostprocessBuildWithReport
    {
        public int callbackOrder => -100;
        public void OnPostprocessBuild(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                Debug.Log($"[Build Pipeline] IPostprocessBuildWithReport.OnPostprocessBuild (Order: {callbackOrder})");
        }
    }
    
    public class PostBuildValidator_Mid : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPostprocessBuild(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                Debug.Log($"[Build Pipeline] IPostprocessBuildWithReport.OnPostprocessBuild (Order: {callbackOrder})");
        }
    }
    
    public class PostBuildValidator_Late : IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;
        public void OnPostprocessBuild(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                Debug.Log($"[Build Pipeline] IPostprocessBuildWithReport.OnPostprocessBuild (Order: {callbackOrder})");
        }
    }
    
    #endregion

    #region ==================== VRCSDK Callbacks ====================
    
    public class VRCSDKBuildRequestedValidator_Early : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => -100;
        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                Debug.Log($"[Build Pipeline] IVRCSDKBuildRequestedCallback.OnBuildRequested (Order: {callbackOrder}) Type: {requestedBuildType}");
            return true;
        }
    }
    
    public class VRCSDKBuildRequestedValidator_Mid : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => 0;
        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                Debug.Log($"[Build Pipeline] IVRCSDKBuildRequestedCallback.OnBuildRequested (Order: {callbackOrder}) Type: {requestedBuildType}");
            return true;
        }
    }
    
    public class VRCSDKBuildRequestedValidator_Late : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => 100;
        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                Debug.Log($"[Build Pipeline] IVRCSDKBuildRequestedCallback.OnBuildRequested (Order: {callbackOrder}) Type: {requestedBuildType}");
            return true;
        }
    }
    
    public class VRCSDKPreprocessAvatarValidator_Early : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -100;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                Debug.Log($"[Build Pipeline] IVRCSDKPreprocessAvatarCallback.OnPreprocessAvatar (Order: {callbackOrder}) Avatar: {avatarGameObject.name}");
            return true;
        }
    }
    
    public class VRCSDKPreprocessAvatarValidator_Mid : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => 0;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                Debug.Log($"[Build Pipeline] IVRCSDKPreprocessAvatarCallback.OnPreprocessAvatar (Order: {callbackOrder}) Avatar: {avatarGameObject.name}");
            return true;
        }
    }
    
    public class VRCSDKPreprocessAvatarValidator_Late : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => 100;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                Debug.Log($"[Build Pipeline] IVRCSDKPreprocessAvatarCallback.OnPreprocessAvatar (Order: {callbackOrder}) Avatar: {avatarGameObject.name}");
            return true;
        }
    }
    
    public class VRCSDKPostprocessAvatarValidator_Early : IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => -100;
        public void OnPostprocessAvatar()
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                Debug.Log($"[Build Pipeline] IVRCSDKPostprocessAvatarCallback.OnPostprocessAvatar (Order: {callbackOrder})");
        }
    }
    
    public class VRCSDKPostprocessAvatarValidator_Mid : IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => 0;
        public void OnPostprocessAvatar()
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                Debug.Log($"[Build Pipeline] IVRCSDKPostprocessAvatarCallback.OnPostprocessAvatar (Order: {callbackOrder})");
        }
    }
    
    public class VRCSDKPostprocessAvatarValidator_Late : IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => 100;
        public void OnPostprocessAvatar()
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                Debug.Log($"[Build Pipeline] IVRCSDKPostprocessAvatarCallback.OnPostprocessAvatar (Order: {callbackOrder})");
        }
    }
    
    #endregion

    #region ==================== AssetPostprocessor ====================
    
    /// <summary>
    /// AssetPostprocessor 使用 postprocessOrder 而非 callbackOrder
    /// 这些回调在资产导入时触发，而非构建时
    /// </summary>
    public class AssetPostprocessorValidator_Early : AssetPostprocessor
    {
        public override int GetPostprocessOrder() => -100;
        
        void OnPreprocessTexture()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                Debug.Log($"[Asset Pipeline] AssetPostprocessor.OnPreprocessTexture (Order: {GetPostprocessOrder()}) Asset: {assetPath}");
        }
        
        void OnPostprocessTexture(Texture2D texture)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                Debug.Log($"[Asset Pipeline] AssetPostprocessor.OnPostprocessTexture (Order: {GetPostprocessOrder()}) Asset: {assetPath}");
        }
        
        void OnPreprocessModel()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                Debug.Log($"[Asset Pipeline] AssetPostprocessor.OnPreprocessModel (Order: {GetPostprocessOrder()}) Asset: {assetPath}");
        }
        
        void OnPostprocessModel(GameObject g)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                Debug.Log($"[Asset Pipeline] AssetPostprocessor.OnPostprocessModel (Order: {GetPostprocessOrder()}) Asset: {assetPath}");
        }
        
        void OnPreprocessAudio()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                Debug.Log($"[Asset Pipeline] AssetPostprocessor.OnPreprocessAudio (Order: {GetPostprocessOrder()}) Asset: {assetPath}");
        }
        
        void OnPostprocessAudio(AudioClip clip)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                Debug.Log($"[Asset Pipeline] AssetPostprocessor.OnPostprocessAudio (Order: {GetPostprocessOrder()}) Asset: {assetPath}");
        }
        
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline && (importedAssets.Length > 0 || deletedAssets.Length > 0 || movedAssets.Length > 0))
            {
                Debug.Log($"[Asset Pipeline] AssetPostprocessor.OnPostprocessAllAssets (Early) Imported: {importedAssets.Length}, Deleted: {deletedAssets.Length}, Moved: {movedAssets.Length}");
            }
        }
    }
    
    public class AssetPostprocessorValidator_Mid : AssetPostprocessor
    {
        public override int GetPostprocessOrder() => 0;
        
        void OnPreprocessTexture()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                Debug.Log($"[Asset Pipeline] AssetPostprocessor.OnPreprocessTexture (Order: {GetPostprocessOrder()}) Asset: {assetPath}");
        }
        
        void OnPostprocessTexture(Texture2D texture)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                Debug.Log($"[Asset Pipeline] AssetPostprocessor.OnPostprocessTexture (Order: {GetPostprocessOrder()}) Asset: {assetPath}");
        }
        
        void OnPreprocessModel()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                Debug.Log($"[Asset Pipeline] AssetPostprocessor.OnPreprocessModel (Order: {GetPostprocessOrder()}) Asset: {assetPath}");
        }
        
        void OnPostprocessModel(GameObject g)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                Debug.Log($"[Asset Pipeline] AssetPostprocessor.OnPostprocessModel (Order: {GetPostprocessOrder()}) Asset: {assetPath}");
        }
        
        void OnPreprocessAudio()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                Debug.Log($"[Asset Pipeline] AssetPostprocessor.OnPreprocessAudio (Order: {GetPostprocessOrder()}) Asset: {assetPath}");
        }
        
        void OnPostprocessAudio(AudioClip clip)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                Debug.Log($"[Asset Pipeline] AssetPostprocessor.OnPostprocessAudio (Order: {GetPostprocessOrder()}) Asset: {assetPath}");
        }
    }
    
    public class AssetPostprocessorValidator_Late : AssetPostprocessor
    {
        public override int GetPostprocessOrder() => 100;
        
        void OnPreprocessTexture()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                Debug.Log($"[Asset Pipeline] AssetPostprocessor.OnPreprocessTexture (Order: {GetPostprocessOrder()}) Asset: {assetPath}");
        }
        
        void OnPostprocessTexture(Texture2D texture)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                Debug.Log($"[Asset Pipeline] AssetPostprocessor.OnPostprocessTexture (Order: {GetPostprocessOrder()}) Asset: {assetPath}");
        }
        
        void OnPreprocessModel()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                Debug.Log($"[Asset Pipeline] AssetPostprocessor.OnPreprocessModel (Order: {GetPostprocessOrder()}) Asset: {assetPath}");
        }
        
        void OnPostprocessModel(GameObject g)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                Debug.Log($"[Asset Pipeline] AssetPostprocessor.OnPostprocessModel (Order: {GetPostprocessOrder()}) Asset: {assetPath}");
        }
        
        void OnPreprocessAudio()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                Debug.Log($"[Asset Pipeline] AssetPostprocessor.OnPreprocessAudio (Order: {GetPostprocessOrder()}) Asset: {assetPath}");
        }
        
        void OnPostprocessAudio(AudioClip clip)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                Debug.Log($"[Asset Pipeline] AssetPostprocessor.OnPostprocessAudio (Order: {GetPostprocessOrder()}) Asset: {assetPath}");
        }
    }
    
    #endregion

    #region ==================== NDMF Plugin ====================
    
#if NDMF_AVAILABLE
    /// <summary>
    /// NDMF 构建流水线验证插件
    /// 在每个 BuildPhase 的开始和结束处打印日志
    /// </summary>
    public class BuildPipelineValidatorPlugin : Plugin<BuildPipelineValidatorPlugin>
    {
        public override string QualifiedName => "top.sealoong.unitybox.build-pipeline-validator";
        public override string DisplayName => "Build Pipeline Validator";

        protected override void Configure()
        {
            // NDMF BuildPhase: Resolving -> Generating -> Transforming -> Optimizing -> PlatformFinish
            
            InPhase(BuildPhase.Resolving).Run("Validator_Resolving_Start", ctx =>
            {
                if (BuildPipelineValidatorSettings.NDMF)
                    Debug.Log($"[Build Pipeline] NDMF BuildPhase.Resolving (Start) Avatar: {ctx.AvatarRootObject.name}");
            });
            
            InPhase(BuildPhase.Generating).Run("Validator_Generating_Start", ctx =>
            {
                if (BuildPipelineValidatorSettings.NDMF)
                    Debug.Log($"[Build Pipeline] NDMF BuildPhase.Generating (Start) Avatar: {ctx.AvatarRootObject.name}");
            });
            
            InPhase(BuildPhase.Transforming).Run("Validator_Transforming_Start", ctx =>
            {
                if (BuildPipelineValidatorSettings.NDMF)
                    Debug.Log($"[Build Pipeline] NDMF BuildPhase.Transforming (Start) Avatar: {ctx.AvatarRootObject.name}");
            });
            
            InPhase(BuildPhase.Optimizing).Run("Validator_Optimizing_Start", ctx =>
            {
                if (BuildPipelineValidatorSettings.NDMF)
                    Debug.Log($"[Build Pipeline] NDMF BuildPhase.Optimizing (Start) Avatar: {ctx.AvatarRootObject.name}");
            });
        }
    }
#endif
    
    #endregion
}
