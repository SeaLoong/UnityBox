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

    internal static class BuildPipelineValidatorLog
    {
        private const string Prefix = "[BPV]";

        public static void Log(string pipeline, int order, string message, string tag = null)
        {
            var pipelineColored = $"<color=#00FFFF>{pipeline}</color>";
            var orderColored = $"<color=#00FF00>Order={order}</color>";
            
            var middle = string.IsNullOrWhiteSpace(tag) 
                ? orderColored
                : $"<color=#FFFF00>{tag}</color> | {orderColored}";

            Debug.Log($"{Prefix} {pipelineColored} | {middle} | {message}");
        }

        public static void LogRaw(string pipeline, string marker, string message)
        {
            var pipelineColored = $"<color=#00FFFF>{pipeline}</color>";
            var markerColored = $"<color=#FFFF00>{marker}</color>";
            Debug.Log($"{Prefix} {pipelineColored} | {markerColored} | {message}");
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
            BuildPipelineValidatorLog.LogRaw("MENU", "⚙", "已启用所有管线日志");
        }
        
        [MenuItem(MENU_PREFIX + "Disable All")]
        private static void DisableAll()
        {
            BuildPipelineValidatorSettings.AssetPipeline = false;
            BuildPipelineValidatorSettings.BuildPipeline = false;
            BuildPipelineValidatorSettings.VRCSDK = false;
            BuildPipelineValidatorSettings.NDMF = false;
            BuildPipelineValidatorLog.LogRaw("MENU", "⚙", "已禁用所有管线日志");
        }
    }
    
    #endregion

    #region ==================== BuildPlayerProcessor ====================
    
    public class BuildPlayerProcessor_MinValue : BuildPlayerProcessor
    {
        public override int callbackOrder => int.MinValue;
        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "BuildPlayerProcessor.PrepareForBuild");
        }
    }
    
    public class BuildPlayerProcessor_N10000 : BuildPlayerProcessor
    {
        public override int callbackOrder => -10000;
        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "BuildPlayerProcessor.PrepareForBuild", "⚑ N10000");
        }
    }
    
    public class BuildPlayerProcessor_Early : BuildPlayerProcessor
    {
        public override int callbackOrder => -100;
        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "BuildPlayerProcessor.PrepareForBuild", "◎");
        }
    }
    
    public class BuildPlayerProcessor_Mid : BuildPlayerProcessor
    {
        public override int callbackOrder => 0;
        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "BuildPlayerProcessor.PrepareForBuild", "○");
        }
    }
    
    public class BuildPlayerProcessor_Late : BuildPlayerProcessor
    {
        public override int callbackOrder => 100;
        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "BuildPlayerProcessor.PrepareForBuild", "◉");
        }
    }
    
    public class BuildPlayerProcessor_P10000 : BuildPlayerProcessor
    {
        public override int callbackOrder => 10000;
        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "BuildPlayerProcessor.PrepareForBuild", "⚑ P10000");
        }
    }
    
    public class BuildPlayerProcessor_MaxValue : BuildPlayerProcessor
    {
        public override int callbackOrder => int.MaxValue;
        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "BuildPlayerProcessor.PrepareForBuild", "⛳ END");
        }
    }
    
    #endregion

    #region ==================== IPreprocessBuildWithReport ====================
    
    public class PreBuildValidator_MinValue : IPreprocessBuildWithReport
    {
        public int callbackOrder => int.MinValue;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "IPreprocessBuildWithReport.OnPreprocessBuild", "🚩 PRE");
        }
    }
    
    public class PreBuildValidator_N10000 : IPreprocessBuildWithReport
    {
        public int callbackOrder => -10000;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "IPreprocessBuildWithReport.OnPreprocessBuild", "⚑ N10000");
        }
    }
    
    public class PreBuildValidator_Early : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "IPreprocessBuildWithReport.OnPreprocessBuild", "◎");
        }
    }
    
    public class PreBuildValidator_Mid : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "IPreprocessBuildWithReport.OnPreprocessBuild", "○");
        }
    }
    
    public class PreBuildValidator_Late : IPreprocessBuildWithReport
    {
        public int callbackOrder => 100;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "IPreprocessBuildWithReport.OnPreprocessBuild", "◉");
        }
    }
    
    public class PreBuildValidator_P10000 : IPreprocessBuildWithReport
    {
        public int callbackOrder => 10000;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "IPreprocessBuildWithReport.OnPreprocessBuild", "⚑ P10000");
        }
    }
    
    public class PreBuildValidator_MaxValue : IPreprocessBuildWithReport
    {
        public int callbackOrder => int.MaxValue;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "IPreprocessBuildWithReport.OnPreprocessBuild", "⛳ END");
        }
    }
    
    #endregion

    #region ==================== IFilterBuildAssemblies ====================
    
    public class FilterAssembliesValidator_MinValue : IFilterBuildAssemblies
    {
        public int callbackOrder => int.MinValue;
        public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IFilterBuildAssemblies.OnFilterAssemblies Assemblies: {assemblies.Length}", "🚩 FILTER");
            return assemblies;
        }
    }
    
    public class FilterAssembliesValidator_N10000 : IFilterBuildAssemblies
    {
        public int callbackOrder => -10000;
        public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IFilterBuildAssemblies.OnFilterAssemblies Assemblies: {assemblies.Length}", "⚑ N10000");
            return assemblies;
        }
    }
    
    public class FilterAssembliesValidator_Early : IFilterBuildAssemblies
    {
        public int callbackOrder => -100;
        public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IFilterBuildAssemblies.OnFilterAssemblies Assemblies: {assemblies.Length}", "◎");
            return assemblies;
        }
    }
    
    public class FilterAssembliesValidator_Mid : IFilterBuildAssemblies
    {
        public int callbackOrder => 0;
        public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IFilterBuildAssemblies.OnFilterAssemblies Assemblies: {assemblies.Length}", "○");
            return assemblies;
        }
    }
    
    public class FilterAssembliesValidator_Late : IFilterBuildAssemblies
    {
        public int callbackOrder => 100;
        public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IFilterBuildAssemblies.OnFilterAssemblies Assemblies: {assemblies.Length}", "◉");
            return assemblies;
        }
    }
    
    public class FilterAssembliesValidator_P10000 : IFilterBuildAssemblies
    {
        public int callbackOrder => 10000;
        public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IFilterBuildAssemblies.OnFilterAssemblies Assemblies: {assemblies.Length}", "⚑ P10000");
            return assemblies;
        }
    }
    
    public class FilterAssembliesValidator_MaxValue : IFilterBuildAssemblies
    {
        public int callbackOrder => int.MaxValue;
        public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IFilterBuildAssemblies.OnFilterAssemblies Assemblies: {assemblies.Length}", "⛳ END");
            return assemblies;
        }
    }
    
    #endregion

    #region ==================== IPostBuildPlayerScriptDLLs ====================
    
    public class PostBuildScriptDLLsValidator_MinValue : IPostBuildPlayerScriptDLLs
    {
        public int callbackOrder => int.MinValue;
        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "IPostBuildPlayerScriptDLLs.OnPostBuildPlayerScriptDLLs", "🚩 POST-DLL");
        }
    }
    
    public class PostBuildScriptDLLsValidator_N10000 : IPostBuildPlayerScriptDLLs
    {
        public int callbackOrder => -10000;
        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "IPostBuildPlayerScriptDLLs.OnPostBuildPlayerScriptDLLs", "⚑ N10000");
        }
    }
    
    public class PostBuildScriptDLLsValidator_Early : IPostBuildPlayerScriptDLLs
    {
        public int callbackOrder => -100;
        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "IPostBuildPlayerScriptDLLs.OnPostBuildPlayerScriptDLLs", "◎");
        }
    }
    
    public class PostBuildScriptDLLsValidator_Mid : IPostBuildPlayerScriptDLLs
    {
        public int callbackOrder => 0;
        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "IPostBuildPlayerScriptDLLs.OnPostBuildPlayerScriptDLLs", "○");
        }
    }
    
    public class PostBuildScriptDLLsValidator_Late : IPostBuildPlayerScriptDLLs
    {
        public int callbackOrder => 100;
        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "IPostBuildPlayerScriptDLLs.OnPostBuildPlayerScriptDLLs", "◉");
        }
    }
    
    public class PostBuildScriptDLLsValidator_P10000 : IPostBuildPlayerScriptDLLs
    {
        public int callbackOrder => 10000;
        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "IPostBuildPlayerScriptDLLs.OnPostBuildPlayerScriptDLLs", "⚑ P10000");
        }
    }
    
    public class PostBuildScriptDLLsValidator_MaxValue : IPostBuildPlayerScriptDLLs
    {
        public int callbackOrder => int.MaxValue;
        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "IPostBuildPlayerScriptDLLs.OnPostBuildPlayerScriptDLLs", "⛳ END");
        }
    }
    
    #endregion

    #region ==================== IProcessSceneWithReport ====================
    
    public class ProcessSceneValidator_MinValue : IProcessSceneWithReport
    {
        public int callbackOrder => int.MinValue;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IProcessSceneWithReport.OnProcessScene Scene: {scene.name}", "🎬 SCENE");
        }
    }
    
    public class ProcessSceneValidator_N10000 : IProcessSceneWithReport
    {
        public int callbackOrder => -10000;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IProcessSceneWithReport.OnProcessScene Scene: {scene.name}", "⚑ N10000");
        }
    }
    
    public class ProcessSceneValidator_Early : IProcessSceneWithReport
    {
        public int callbackOrder => -100;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IProcessSceneWithReport.OnProcessScene Scene: {scene.name}", "◎");
        }
    }
    
    public class ProcessSceneValidator_Mid : IProcessSceneWithReport
    {
        public int callbackOrder => 0;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IProcessSceneWithReport.OnProcessScene Scene: {scene.name}", "○");
        }
    }
    
    public class ProcessSceneValidator_Late : IProcessSceneWithReport
    {
        public int callbackOrder => 100;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IProcessSceneWithReport.OnProcessScene Scene: {scene.name}", "◉");
        }
    }
    
    public class ProcessSceneValidator_P10000 : IProcessSceneWithReport
    {
        public int callbackOrder => 10000;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IProcessSceneWithReport.OnProcessScene Scene: {scene.name}", "⚑ P10000");
        }
    }
    
    public class ProcessSceneValidator_MaxValue : IProcessSceneWithReport
    {
        public int callbackOrder => int.MaxValue;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IProcessSceneWithReport.OnProcessScene Scene: {scene.name}", "⛳ END");
        }
    }
    
    #endregion

    #region ==================== IPreprocessShaders ====================
    
    public class PreprocessShadersValidator_MinValue : IPreprocessShaders
    {
        public int callbackOrder => int.MinValue;
        private static bool _logged = false;
        
        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (!_logged && BuildPipelineValidatorSettings.BuildPipeline)
            {
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IPreprocessShaders.OnProcessShader Shader: {shader.name}", "🎨 SHADER");
                _logged = true;
            }
        }
    }
    
    public class PreprocessShadersValidator_N10000 : IPreprocessShaders
    {
        public int callbackOrder => -10000;
        private static bool _logged = false;
        
        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (!_logged && BuildPipelineValidatorSettings.BuildPipeline)
            {
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IPreprocessShaders.OnProcessShader Shader: {shader.name}", "⚑ N10000");
                _logged = true;
            }
        }
    }
    
    public class PreprocessShadersValidator_Early : IPreprocessShaders
    {
        public int callbackOrder => -100;
        private static bool _logged = false;
        
        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (!_logged && BuildPipelineValidatorSettings.BuildPipeline)
            {
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IPreprocessShaders.OnProcessShader Shader: {shader.name}", "◎");
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
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IPreprocessShaders.OnProcessShader Shader: {shader.name}", "○");
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
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IPreprocessShaders.OnProcessShader Shader: {shader.name}", "◉");
                _logged = true;
            }
        }
    }
    
    public class PreprocessShadersValidator_P10000 : IPreprocessShaders
    {
        public int callbackOrder => 10000;
        private static bool _logged = false;
        
        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (!_logged && BuildPipelineValidatorSettings.BuildPipeline)
            {
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IPreprocessShaders.OnProcessShader Shader: {shader.name}", "⚑ P10000");
                _logged = true;
            }
        }
    }
    
    public class PreprocessShadersValidator_MaxValue : IPreprocessShaders
    {
        public int callbackOrder => int.MaxValue;
        private static bool _logged = false;
        
        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (!_logged && BuildPipelineValidatorSettings.BuildPipeline)
            {
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IPreprocessShaders.OnProcessShader Shader: {shader.name}", "⛳ END");
                _logged = true;
            }
        }
    }
    
    #endregion

    #region ==================== IPreprocessComputeShaders ====================
    
    public class PreprocessComputeShadersValidator_MinValue : IPreprocessComputeShaders
    {
        public int callbackOrder => int.MinValue;
        private static bool _logged = false;
        
        public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> data)
        {
            if (!_logged && BuildPipelineValidatorSettings.BuildPipeline)
            {
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IPreprocessComputeShaders.OnProcessComputeShader Shader: {shader.name}, Kernel: {kernelName}", "🎛 COMPUTE");
                _logged = true;
            }
        }
    }
    
    public class PreprocessComputeShadersValidator_N10000 : IPreprocessComputeShaders
    {
        public int callbackOrder => -10000;
        private static bool _logged = false;
        
        public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> data)
        {
            if (!_logged && BuildPipelineValidatorSettings.BuildPipeline)
            {
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IPreprocessComputeShaders.OnProcessComputeShader Shader: {shader.name}, Kernel: {kernelName}", "⚑ N10000");
                _logged = true;
            }
        }
    }
    
    public class PreprocessComputeShadersValidator_Early : IPreprocessComputeShaders
    {
        public int callbackOrder => -100;
        private static bool _logged = false;
        
        public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> data)
        {
            if (!_logged && BuildPipelineValidatorSettings.BuildPipeline)
            {
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IPreprocessComputeShaders.OnProcessComputeShader Shader: {shader.name}, Kernel: {kernelName}", "◎");
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
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IPreprocessComputeShaders.OnProcessComputeShader Shader: {shader.name}, Kernel: {kernelName}", "○");
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
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IPreprocessComputeShaders.OnProcessComputeShader Shader: {shader.name}, Kernel: {kernelName}", "◉");
                _logged = true;
            }
        }
    }
    
    public class PreprocessComputeShadersValidator_P10000 : IPreprocessComputeShaders
    {
        public int callbackOrder => 10000;
        private static bool _logged = false;
        
        public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> data)
        {
            if (!_logged && BuildPipelineValidatorSettings.BuildPipeline)
            {
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IPreprocessComputeShaders.OnProcessComputeShader Shader: {shader.name}, Kernel: {kernelName}", "⚑ P10000");
                _logged = true;
            }
        }
    }
    
    public class PreprocessComputeShadersValidator_MaxValue : IPreprocessComputeShaders
    {
        public int callbackOrder => int.MaxValue;
        private static bool _logged = false;
        
        public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> data)
        {
            if (!_logged && BuildPipelineValidatorSettings.BuildPipeline)
            {
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, $"IPreprocessComputeShaders.OnProcessComputeShader Shader: {shader.name}, Kernel: {kernelName}", "⛳ END");
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
            BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "IUnityLinkerProcessor.GenerateAdditionalLinkXmlFile", "◎");
            return null;
        }
    }
    
    public class UnityLinkerValidator_Mid : IUnityLinkerProcessor
    {
        public int callbackOrder => 0;
        
        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "IUnityLinkerProcessor.GenerateAdditionalLinkXmlFile", "○");
            return null;
        }
    }
    
    public class UnityLinkerValidator_Late : IUnityLinkerProcessor
    {
        public int callbackOrder => 100;
        
        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "IUnityLinkerProcessor.GenerateAdditionalLinkXmlFile", "◉");
            return null;
        }
    }
    
    #endregion
    */

    #region ==================== IPostprocessBuildWithReport ====================
    
    public class PostBuildValidator_MinValue : IPostprocessBuildWithReport
    {
        public int callbackOrder => int.MinValue;
        public void OnPostprocessBuild(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "IPostprocessBuildWithReport.OnPostprocessBuild", "🚩 POST");
        }
    }
    
    public class PostBuildValidator_N10000 : IPostprocessBuildWithReport
    {
        public int callbackOrder => -10000;
        public void OnPostprocessBuild(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "IPostprocessBuildWithReport.OnPostprocessBuild", "⚑ N10000");
        }
    }
    
    public class PostBuildValidator_Early : IPostprocessBuildWithReport
    {
        public int callbackOrder => -100;
        public void OnPostprocessBuild(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "IPostprocessBuildWithReport.OnPostprocessBuild", "◎");
        }
    }
    
    public class PostBuildValidator_Mid : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPostprocessBuild(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "IPostprocessBuildWithReport.OnPostprocessBuild", "○");
        }
    }
    
    public class PostBuildValidator_Late : IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;
        public void OnPostprocessBuild(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "IPostprocessBuildWithReport.OnPostprocessBuild", "◉");
        }
    }
    
    public class PostBuildValidator_P10000 : IPostprocessBuildWithReport
    {
        public int callbackOrder => 10000;
        public void OnPostprocessBuild(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "IPostprocessBuildWithReport.OnPostprocessBuild", "⚑ P10000");
        }
    }
    
    public class PostBuildValidator_MaxValue : IPostprocessBuildWithReport
    {
        public int callbackOrder => int.MaxValue;
        public void OnPostprocessBuild(BuildReport report)
        {
            if (BuildPipelineValidatorSettings.BuildPipeline)
                BuildPipelineValidatorLog.Log("BUILD", callbackOrder, "IPostprocessBuildWithReport.OnPostprocessBuild", "⛳ END");
        }
    }
    
    #endregion

    #region ==================== VRCSDK Callbacks ====================
    
    /*
     * ===================== 已知框架的 callbackOrder 值 =====================
     * 
     * 以下是各主要框架在 IVRCSDKPreprocessAvatarCallback 中使用的 callbackOrder 值:
     * 
     * int.MinValue (+1): VRCFury FailureCheckStart / IsActuallyUploadingHook / WhenBlueprintIdReadyHook
     * -11000           : NDMF BuildFrameworkPreprocessHook (Resolving → Transforming)
     * -10000           : VRCFury VrcPreuploadHook (主处理, 调用 VRCFuryBuilder.RunMain)
     * -1026            : ★ ASS Processor (在此位置注入安全系统)
     * -1025            : NDMF BuildFrameworkOptimizeHook (Optimizing → Last)
     * -1024            : VRCFury VrcfRemoveEditorOnlyObjects / VRCSDK RemoveAvatarEditorOnly
     * 0                : 默认值
     * 100              : 常规后处理
     * int.MaxValue-100 : VRCFury ParameterCompressorHook (参数压缩，几乎最后执行)
     * int.MaxValue     : VRCFury FailureCheckEnd / VrcfRemoveEditorOnlyComponents
     *                  : MA ReplacementRemoveIEditorOnly (销毁所有 IEditorOnly 组件)
     * 
     * 参数安全说明：
     *   ASS 在 -1026 注入参数，VRCFury 参数压缩在 int.MaxValue-100 执行。
     *   由于参数压缩在 ASS 之后运行，ASS 注入的参数会被 VRCFury 正确处理。
     * 
     * 本验证器在这些关键点的前后都设置了探测器，以便确认执行顺序
     * ======================================================================
     */
    
    // ==================== IVRCSDKBuildRequestedCallback ====================
    
    public class VRCSDKBuildRequestedValidator_MinValue : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => int.MinValue;
        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, $"IVRCSDKBuildRequestedCallback.OnBuildRequested Type: {requestedBuildType}", "🚩 REQ");
            return true;
        }
    }
    
    public class VRCSDKBuildRequestedValidator_N10000 : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => -10000;
        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, $"IVRCSDKBuildRequestedCallback.OnBuildRequested Type: {requestedBuildType}", "⚑ N10000");
            return true;
        }
    }
    
    public class VRCSDKBuildRequestedValidator_Early : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => -100;
        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, $"IVRCSDKBuildRequestedCallback.OnBuildRequested Type: {requestedBuildType}", "◎");
            return true;
        }
    }
    
    public class VRCSDKBuildRequestedValidator_Mid : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => 0;
        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, $"IVRCSDKBuildRequestedCallback.OnBuildRequested Type: {requestedBuildType}", "○");
            return true;
        }
    }
    
    public class VRCSDKBuildRequestedValidator_Late : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => 100;
        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, $"IVRCSDKBuildRequestedCallback.OnBuildRequested Type: {requestedBuildType}", "◉");
            return true;
        }
    }
    
    public class VRCSDKBuildRequestedValidator_P10000 : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => 10000;
        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, $"IVRCSDKBuildRequestedCallback.OnBuildRequested Type: {requestedBuildType}", "⚑ P10000");
            return true;
        }
    }
    
    public class VRCSDKBuildRequestedValidator_MaxValue : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => int.MaxValue;
        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, $"IVRCSDKBuildRequestedCallback.OnBuildRequested Type: {requestedBuildType}", "⛳ END");
            return true;
        }
    }
    
    // ==================== IVRCSDKPreprocessAvatarCallback 完整阶段覆盖 ====================
    
    /// <summary>绝对最早 - int.MinValue</summary>
    public class VRCSDKPreprocessAvatarValidator_MinValue : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, $"IVRCSDKPreprocessAvatarCallback Avatar: {avatarGameObject.name}", "🚀 PRE");
            return true;
        }
    }
    
    /// <summary>在 NDMF PreprocessHook 之前 (-11001)</summary>
    public class VRCSDKPreprocessAvatarValidator_BeforeNDMFPreprocess : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -11001;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, "Before NDMF PreprocessHook", "↗ BEFORE NDMF-PRE");
            return true;
        }
    }
    
    /// <summary>NDMF BuildFrameworkPreprocessHook 位置探测 (-11000)</summary>
    public class VRCSDKPreprocessAvatarValidator_NDMFPreprocess : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -11000;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, "NDMF PreprocessHook (Resolving → Transforming)", "★★ NDMF-PREPROC");
            return true;
        }
    }
    
    /// <summary>在 NDMF PreprocessHook 之后、VRCFury 之前 (-10999)</summary>
    public class VRCSDKPreprocessAvatarValidator_AfterNDMFPreprocess : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -10999;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, "After NDMF PreprocessHook", "↘ AFTER NDMF-PRE");
            return true;
        }
    }
    
    /// <summary>在 VRCFury 之前 (-10001)</summary>
    public class VRCSDKPreprocessAvatarValidator_BeforeVRCFury : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -10001;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, "Before VRCFury", "↗ BEFORE VRCFURY");
            return true;
        }
    }
    
    /// <summary>VRCFury 位置探测 (-10000)</summary>
    public class VRCSDKPreprocessAvatarValidator_VRCFury : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -10000;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, "VRCFury Hook", "★★ VRCFURY");
            return true;
        }
    }
    
    /// <summary>在 VRCFury 之后 (-9999)</summary>
    public class VRCSDKPreprocessAvatarValidator_AfterVRCFury : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -9999;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, "After VRCFury", "↘ AFTER VRCFURY");
            return true;
        }
    }
    
    /// <summary>在 NDMF OptimizeHook 之前 (-1026)</summary>
    public class VRCSDKPreprocessAvatarValidator_BeforeNDMFOptimize : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -1026;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, "Before NDMF OptimizeHook", "↗ BEFORE NDMF-OPT");
            return true;
        }
    }
    
    /// <summary>NDMF BuildFrameworkOptimizeHook 位置探测 (-1025)</summary>
    public class VRCSDKPreprocessAvatarValidator_NDMFOptimize : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -1025;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, "NDMF OptimizeHook (Optimizing → Last)", "★★ NDMF-OPT");
            return true;
        }
    }
    
    /// <summary>在 RemoveAvatarEditorOnly 之前 / NDMF OptimizeHook 之后 (-1024)</summary>
    public class VRCSDKPreprocessAvatarValidator_AfterNDMFOptimize : IVRCSDKPreprocessAvatarCallback
    {
        // 注意: -1024 同时被 VRCSDK RemoveAvatarEditorOnly 和 VRCFury VrcfRemoveEditorOnlyObjects 使用
        // VRCFury 通过 Harmony Patch 移除了原始 RemoveAvatarEditorOnly，替换为自己的 VrcfRemoveEditorOnlyObjects
        public int callbackOrder => -1024;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, "RemoveEditorOnly (VRCFury replaces VRCSDK's implementation)", "★ REMOVE-EDITOR");
            return true;
        }
    }
    
    /// <summary>在 RemoveAvatarEditorOnly 之后 (-1023)</summary>
    public class VRCSDKPreprocessAvatarValidator_AfterRemoveEditorOnly : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -1023;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, "After RemoveAvatarEditorOnly", "↘ AFTER REMOVE-EDITOR");
            return true;
        }
    }
    
    /// <summary>经典测试点 - Early (-100)</summary>
    public class VRCSDKPreprocessAvatarValidator_Early : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -100;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, $"IVRCSDKPreprocessAvatarCallback Avatar: {avatarGameObject.name}", "◎");
            return true;
        }
    }
    
    /// <summary>经典测试点 - Mid (0)</summary>
    public class VRCSDKPreprocessAvatarValidator_Mid : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => 0;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, $"IVRCSDKPreprocessAvatarCallback Avatar: {avatarGameObject.name}", "○");
            return true;
        }
    }
    
    /// <summary>经典测试点 - Late (100)</summary>
    public class VRCSDKPreprocessAvatarValidator_Late : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => 100;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, $"IVRCSDKPreprocessAvatarCallback Avatar: {avatarGameObject.name}", "◉");
            return true;
        }
    }
    
    /// <summary>在 VRCFury ParameterCompressorHook 之前 (int.MaxValue - 101)</summary>
    public class VRCSDKPreprocessAvatarValidator_BeforeParamCompressor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MaxValue - 101;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, "Before VRCFury ParameterCompressor", "↗ BEFORE PARAM-COMPRESS");
            return true;
        }
    }
    
    /// <summary>VRCFury ParameterCompressorHook 位置探测 (int.MaxValue - 100)</summary>
    public class VRCSDKPreprocessAvatarValidator_ParamCompressor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MaxValue - 100;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, "VRCFury ParameterCompressorHook (参数压缩)", "★★ PARAM-COMPRESS");
            return true;
        }
    }
    
    /// <summary>在 VRCFury ParameterCompressorHook 之后 (int.MaxValue - 99)</summary>
    public class VRCSDKPreprocessAvatarValidator_AfterParamCompressor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MaxValue - 99;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, "After VRCFury ParameterCompressor", "↘ AFTER PARAM-COMPRESS");
            return true;
        }
    }
    
    /// <summary>在 MaxValue 之前 (int.MaxValue - 1)</summary>
    public class VRCSDKPreprocessAvatarValidator_BeforeMaxValue : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MaxValue - 1;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, "Before MaxValue", "↗ BEFORE END");
            return true;
        }
    }
    
    /// <summary>绝对最后 - int.MaxValue (VRCFury FailureCheckEnd/VrcfRemoveEditorOnlyComponents, MA RemoveIEditorOnly)</summary>
    public class VRCSDKPreprocessAvatarValidator_MaxValue : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MaxValue;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, "VRCFury Cleanup + MA RemoveIEditorOnly (destroy IEditorOnly)", "★ FINAL-CLEANUP");
            return true;
        }
    }
    
    // ==================== IVRCSDKPostprocessAvatarCallback ====================
    
    public class VRCSDKPostprocessAvatarValidator_MinValue : IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue;
        public void OnPostprocessAvatar()
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, "IVRCSDKPostprocessAvatarCallback.OnPostprocessAvatar", "🚩 POST");
        }
    }
    
    public class VRCSDKPostprocessAvatarValidator_N10000 : IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => -10000;
        public void OnPostprocessAvatar()
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, "IVRCSDKPostprocessAvatarCallback.OnPostprocessAvatar", "⚑ N10000");
        }
    }
    
    public class VRCSDKPostprocessAvatarValidator_Early : IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => -100;
        public void OnPostprocessAvatar()
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, "IVRCSDKPostprocessAvatarCallback.OnPostprocessAvatar", "◎");
        }
    }
    
    public class VRCSDKPostprocessAvatarValidator_Mid : IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => 0;
        public void OnPostprocessAvatar()
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, "IVRCSDKPostprocessAvatarCallback.OnPostprocessAvatar", "○");
        }
    }
    
    public class VRCSDKPostprocessAvatarValidator_Late : IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => 100;
        public void OnPostprocessAvatar()
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, "IVRCSDKPostprocessAvatarCallback.OnPostprocessAvatar", "◉");
        }
    }
    
    public class VRCSDKPostprocessAvatarValidator_P10000 : IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => 10000;
        public void OnPostprocessAvatar()
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, "IVRCSDKPostprocessAvatarCallback.OnPostprocessAvatar", "⚑ P10000");
        }
    }
    
    public class VRCSDKPostprocessAvatarValidator_MaxValue : IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => int.MaxValue;
        public void OnPostprocessAvatar()
        {
            if (BuildPipelineValidatorSettings.VRCSDK)
                BuildPipelineValidatorLog.Log("VRCSDK", callbackOrder, "IVRCSDKPostprocessAvatarCallback.OnPostprocessAvatar", "⛳ END");
        }
    }
    
    #endregion

    #region ==================== AssetPostprocessor ====================
    
    /// <summary>
    /// AssetPostprocessor 使用 postprocessOrder 而非 callbackOrder
    /// 这些回调在资产导入时触发，而非构建时
    /// </summary>
    
    public class AssetPostprocessorValidator_MinValue : AssetPostprocessor
    {
        public override int GetPostprocessOrder() => int.MinValue;
        
        void OnPreprocessTexture()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessTexture Asset: {assetPath}", "🚩 IMPORT");
        }
        
        void OnPostprocessTexture(Texture2D texture)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessTexture Asset: {assetPath}", "🚩 IMPORT");
        }
        
        void OnPreprocessModel()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessModel Asset: {assetPath}", "🚩 IMPORT");
        }
        
        void OnPostprocessModel(GameObject g)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessModel Asset: {assetPath}", "🚩 IMPORT");
        }
        
        void OnPreprocessAudio()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessAudio Asset: {assetPath}", "🚩 IMPORT");
        }
        
        void OnPostprocessAudio(AudioClip clip)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessAudio Asset: {assetPath}", "🚩 IMPORT");
        }
        
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline && (importedAssets.Length > 0 || deletedAssets.Length > 0 || movedAssets.Length > 0))
            {
                BuildPipelineValidatorLog.Log("ASSET", int.MinValue, $"AssetPostprocessor.OnPostprocessAllAssets Imported: {importedAssets.Length}, Deleted: {deletedAssets.Length}, Moved: {movedAssets.Length}", "📦 BATCH");
            }
        }
    }
    
    public class AssetPostprocessorValidator_N10000 : AssetPostprocessor
    {
        public override int GetPostprocessOrder() => -10000;
        
        void OnPreprocessTexture()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessTexture Asset: {assetPath}", "⚑ N10000");
        }
        
        void OnPostprocessTexture(Texture2D texture)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessTexture Asset: {assetPath}", "⚑ N10000");
        }
        
        void OnPreprocessModel()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessModel Asset: {assetPath}", "⚑ N10000");
        }
        
        void OnPostprocessModel(GameObject g)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessModel Asset: {assetPath}", "⚑ N10000");
        }
        
        void OnPreprocessAudio()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessAudio Asset: {assetPath}", "⚑ N10000");
        }
        
        void OnPostprocessAudio(AudioClip clip)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessAudio Asset: {assetPath}", "⚑ N10000");
        }
    }
    
    public class AssetPostprocessorValidator_Early : AssetPostprocessor
    {
        public override int GetPostprocessOrder() => -100;
        
        void OnPreprocessTexture()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessTexture Asset: {assetPath}", "◎");
        }
        
        void OnPostprocessTexture(Texture2D texture)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessTexture Asset: {assetPath}", "◎");
        }
        
        void OnPreprocessModel()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessModel Asset: {assetPath}", "◎");
        }
        
        void OnPostprocessModel(GameObject g)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessModel Asset: {assetPath}", "◎");
        }
        
        void OnPreprocessAudio()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessAudio Asset: {assetPath}", "◎");
        }
        
        void OnPostprocessAudio(AudioClip clip)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessAudio Asset: {assetPath}", "◎");
        }
    }
    
    public class AssetPostprocessorValidator_Mid : AssetPostprocessor
    {
        public override int GetPostprocessOrder() => 0;
        
        void OnPreprocessTexture()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessTexture Asset: {assetPath}", "○");
        }
        
        void OnPostprocessTexture(Texture2D texture)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessTexture Asset: {assetPath}", "○");
        }
        
        void OnPreprocessModel()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessModel Asset: {assetPath}", "○");
        }
        
        void OnPostprocessModel(GameObject g)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessModel Asset: {assetPath}", "○");
        }
        
        void OnPreprocessAudio()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessAudio Asset: {assetPath}", "○");
        }
        
        void OnPostprocessAudio(AudioClip clip)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessAudio Asset: {assetPath}", "○");
        }
    }
    
    public class AssetPostprocessorValidator_Late : AssetPostprocessor
    {
        public override int GetPostprocessOrder() => 100;
        
        void OnPreprocessTexture()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessTexture Asset: {assetPath}", "◉");
        }
        
        void OnPostprocessTexture(Texture2D texture)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessTexture Asset: {assetPath}", "◉");
        }
        
        void OnPreprocessModel()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessModel Asset: {assetPath}", "◉");
        }
        
        void OnPostprocessModel(GameObject g)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessModel Asset: {assetPath}", "◉");
        }
        
        void OnPreprocessAudio()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessAudio Asset: {assetPath}", "◉");
        }
        
        void OnPostprocessAudio(AudioClip clip)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessAudio Asset: {assetPath}", "◉");
        }
    }
    
    public class AssetPostprocessorValidator_P10000 : AssetPostprocessor
    {
        public override int GetPostprocessOrder() => 10000;
        
        void OnPreprocessTexture()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessTexture Asset: {assetPath}", "⚑ P10000");
        }
        
        void OnPostprocessTexture(Texture2D texture)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessTexture Asset: {assetPath}", "⚑ P10000");
        }
        
        void OnPreprocessModel()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessModel Asset: {assetPath}", "⚑ P10000");
        }
        
        void OnPostprocessModel(GameObject g)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessModel Asset: {assetPath}", "⚑ P10000");
        }
        
        void OnPreprocessAudio()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessAudio Asset: {assetPath}", "⚑ P10000");
        }
        
        void OnPostprocessAudio(AudioClip clip)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessAudio Asset: {assetPath}", "⚑ P10000");
        }
    }
    
    public class AssetPostprocessorValidator_MaxValue : AssetPostprocessor
    {
        public override int GetPostprocessOrder() => int.MaxValue;
        
        void OnPreprocessTexture()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessTexture Asset: {assetPath}", "⛳ END");
        }
        
        void OnPostprocessTexture(Texture2D texture)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessTexture Asset: {assetPath}", "⛳ END");
        }
        
        void OnPreprocessModel()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessModel Asset: {assetPath}", "⛳ END");
        }
        
        void OnPostprocessModel(GameObject g)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessModel Asset: {assetPath}", "⛳ END");
        }
        
        void OnPreprocessAudio()
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessAudio Asset: {assetPath}", "⛳ END");
        }
        
        void OnPostprocessAudio(AudioClip clip)
        {
            if (BuildPipelineValidatorSettings.AssetPipeline)
                BuildPipelineValidatorLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessAudio Asset: {assetPath}", "⛳ END");
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
                    BuildPipelineValidatorLog.LogRaw("NDMF", "◆ PHASE", $"BuildPhase.Resolving (Start) Avatar: {ctx.AvatarRootObject.name}");
            });
            
            InPhase(BuildPhase.Generating).Run("Validator_Generating_Start", ctx =>
            {
                if (BuildPipelineValidatorSettings.NDMF)
                    BuildPipelineValidatorLog.LogRaw("NDMF", "◆ PHASE", $"BuildPhase.Generating (Start) Avatar: {ctx.AvatarRootObject.name}");
            });
            
            InPhase(BuildPhase.Transforming).Run("Validator_Transforming_Start", ctx =>
            {
                if (BuildPipelineValidatorSettings.NDMF)
                    BuildPipelineValidatorLog.LogRaw("NDMF", "◆ PHASE", $"BuildPhase.Transforming (Start) Avatar: {ctx.AvatarRootObject.name}");
            });
            
            InPhase(BuildPhase.Optimizing).Run("Validator_Optimizing_Start", ctx =>
            {
                if (BuildPipelineValidatorSettings.NDMF)
                    BuildPipelineValidatorLog.LogRaw("NDMF", "◆ PHASE", $"BuildPhase.Optimizing (Start) Avatar: {ctx.AvatarRootObject.name}");
            });
        }
    }
#endif
    
    #endregion
}
