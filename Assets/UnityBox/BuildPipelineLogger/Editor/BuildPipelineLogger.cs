using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;


namespace UnityBox.BuildPipelineLogger
{
    /// <summary>
    /// Unity 构建进程日志记录器 - 全面测试所有构建回调接口
    /// 每个接口都测试 -100, 0, 100 三种 callbackOrder
    /// 通过菜单控制各个管线的日志输出
    /// </summary>

    #region ==================== 设置管理 ====================
    
    public static class BuildPipelineLoggerSettings
    {
private const string PREF_PREFIX = "UnityBox.BuildPipelineLogger.";
        
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

    public static class BuildPipelineLoggerLog
    {
        private const string Prefix = "[BPL]";
        private static readonly HashSet<System.Type> loggedShaderCallbacks = new HashSet<System.Type>();

        public static void BeginBuild() => loggedShaderCallbacks.Clear();
        public static bool ShouldLogShader(System.Type callbackType) => loggedShaderCallbacks.Add(callbackType);

        public static void Log(string pipeline, int order, string message, string tag = null)
        {
            // Third-party names in these probes are historical order hints; this
            // callback does not establish that the framework itself ran.
            if (pipeline == "VRCSDK" &&
                (message.Contains("VRCFury") || message.Contains("NDMF") ||
                 message.Contains("RemoveEditorOnly") || message.Contains("RemoveAvatarEditorOnly")))
                message = "Callback-order sample (framework execution unverified): " + message;
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
    
    public static class BuildPipelineLoggerMenu
    {
        private const string MENU_PREFIX = "Tools/UnityBox/Build Pipeline Logger/";
        
        [MenuItem(MENU_PREFIX + "Asset Pipeline")]
        private static void ToggleAssetPipeline() => BuildPipelineLoggerSettings.AssetPipeline = !BuildPipelineLoggerSettings.AssetPipeline;
        [MenuItem(MENU_PREFIX + "Asset Pipeline", true)]
        private static bool ValidateAssetPipeline() { Menu.SetChecked(MENU_PREFIX + "Asset Pipeline", BuildPipelineLoggerSettings.AssetPipeline); return true; }
        
        [MenuItem(MENU_PREFIX + "Build Pipeline")]
        private static void ToggleBuildPipeline() => BuildPipelineLoggerSettings.BuildPipeline = !BuildPipelineLoggerSettings.BuildPipeline;
        [MenuItem(MENU_PREFIX + "Build Pipeline", true)]
        private static bool ValidateBuildPipeline() { Menu.SetChecked(MENU_PREFIX + "Build Pipeline", BuildPipelineLoggerSettings.BuildPipeline); return true; }
        
        [MenuItem(MENU_PREFIX + "VRCSDK")]
        private static void ToggleVRCSDK() => BuildPipelineLoggerSettings.VRCSDK = !BuildPipelineLoggerSettings.VRCSDK;
        [MenuItem(MENU_PREFIX + "VRCSDK", true)]
        private static bool ValidateVRCSDK()
        {
#if UNITYBOX_VRCSDK
            Menu.SetChecked(MENU_PREFIX + "VRCSDK", BuildPipelineLoggerSettings.VRCSDK);
            return true;
#else
            Menu.SetChecked(MENU_PREFIX + "VRCSDK", false);
            return false;
#endif
        }
        
        [MenuItem(MENU_PREFIX + "NDMF")]
        private static void ToggleNDMF() => BuildPipelineLoggerSettings.NDMF = !BuildPipelineLoggerSettings.NDMF;
        [MenuItem(MENU_PREFIX + "NDMF", true)]
        private static bool ValidateNDMF() { Menu.SetChecked(MENU_PREFIX + "NDMF", BuildPipelineLoggerSettings.NDMF); return true; }
        
        [MenuItem(MENU_PREFIX + "Enable All")]
        private static void EnableAll()
        {
            BuildPipelineLoggerSettings.AssetPipeline = true;
            BuildPipelineLoggerSettings.BuildPipeline = true;
            BuildPipelineLoggerSettings.VRCSDK = true;
            BuildPipelineLoggerSettings.NDMF = true;
            BuildPipelineLoggerLog.LogRaw("MENU", "⚙", "已启用所有管线日志");
        }
        
        [MenuItem(MENU_PREFIX + "Disable All")]
        private static void DisableAll()
        {
            BuildPipelineLoggerSettings.AssetPipeline = false;
            BuildPipelineLoggerSettings.BuildPipeline = false;
            BuildPipelineLoggerSettings.VRCSDK = false;
            BuildPipelineLoggerSettings.NDMF = false;
            BuildPipelineLoggerLog.LogRaw("MENU", "⚙", "已禁用所有管线日志");
        }
    }
    
    #endregion

    #region ==================== BuildPlayerProcessor ====================
    
    public class BuildPlayerProcessor_MinValue : BuildPlayerProcessor
    {
        public override int callbackOrder => int.MinValue;
        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "BuildPlayerProcessor.PrepareForBuild");
        }
    }
    
    public class BuildPlayerProcessor_N10000 : BuildPlayerProcessor
    {
        public override int callbackOrder => -10000;
        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "BuildPlayerProcessor.PrepareForBuild", "⚑ N10000");
        }
    }
    
    public class BuildPlayerProcessor_Early : BuildPlayerProcessor
    {
        public override int callbackOrder => -100;
        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "BuildPlayerProcessor.PrepareForBuild", "◎");
        }
    }
    
    public class BuildPlayerProcessor_Mid : BuildPlayerProcessor
    {
        public override int callbackOrder => 0;
        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "BuildPlayerProcessor.PrepareForBuild", "○");
        }
    }
    
    public class BuildPlayerProcessor_Late : BuildPlayerProcessor
    {
        public override int callbackOrder => 100;
        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "BuildPlayerProcessor.PrepareForBuild", "◉");
        }
    }
    
    public class BuildPlayerProcessor_P10000 : BuildPlayerProcessor
    {
        public override int callbackOrder => 10000;
        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "BuildPlayerProcessor.PrepareForBuild", "⚑ P10000");
        }
    }
    
    public class BuildPlayerProcessor_MaxValue : BuildPlayerProcessor
    {
        public override int callbackOrder => int.MaxValue;
        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "BuildPlayerProcessor.PrepareForBuild", "⛳ END");
        }
    }
    
    #endregion

    #region ==================== IPreprocessBuildWithReport ====================
    
    public class PreBuildLogger_MinValue : IPreprocessBuildWithReport
    {
        public int callbackOrder => int.MinValue;
        public void OnPreprocessBuild(BuildReport report)
        {
            BuildPipelineLoggerLog.BeginBuild();
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "IPreprocessBuildWithReport.OnPreprocessBuild", "🚩 PRE");
        }
    }
    
    public class PreBuildLogger_N10000 : IPreprocessBuildWithReport
    {
        public int callbackOrder => -10000;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "IPreprocessBuildWithReport.OnPreprocessBuild", "⚑ N10000");
        }
    }
    
    public class PreBuildLogger_Early : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "IPreprocessBuildWithReport.OnPreprocessBuild", "◎");
        }
    }
    
    public class PreBuildLogger_Mid : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "IPreprocessBuildWithReport.OnPreprocessBuild", "○");
        }
    }
    
    public class PreBuildLogger_Late : IPreprocessBuildWithReport
    {
        public int callbackOrder => 100;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "IPreprocessBuildWithReport.OnPreprocessBuild", "◉");
        }
    }
    
    public class PreBuildLogger_P10000 : IPreprocessBuildWithReport
    {
        public int callbackOrder => 10000;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "IPreprocessBuildWithReport.OnPreprocessBuild", "⚑ P10000");
        }
    }
    
    public class PreBuildLogger_MaxValue : IPreprocessBuildWithReport
    {
        public int callbackOrder => int.MaxValue;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "IPreprocessBuildWithReport.OnPreprocessBuild", "⛳ END");
        }
    }
    
    #endregion

    #region ==================== IFilterBuildAssemblies ====================
    
    public class FilterAssembliesLogger_MinValue : IFilterBuildAssemblies
    {
        public int callbackOrder => int.MinValue;
        public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IFilterBuildAssemblies.OnFilterAssemblies Assemblies: {assemblies.Length}", "🚩 FILTER");
            return assemblies;
        }
    }
    
    public class FilterAssembliesLogger_N10000 : IFilterBuildAssemblies
    {
        public int callbackOrder => -10000;
        public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IFilterBuildAssemblies.OnFilterAssemblies Assemblies: {assemblies.Length}", "⚑ N10000");
            return assemblies;
        }
    }
    
    public class FilterAssembliesLogger_Early : IFilterBuildAssemblies
    {
        public int callbackOrder => -100;
        public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IFilterBuildAssemblies.OnFilterAssemblies Assemblies: {assemblies.Length}", "◎");
            return assemblies;
        }
    }
    
    public class FilterAssembliesLogger_Mid : IFilterBuildAssemblies
    {
        public int callbackOrder => 0;
        public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IFilterBuildAssemblies.OnFilterAssemblies Assemblies: {assemblies.Length}", "○");
            return assemblies;
        }
    }
    
    public class FilterAssembliesLogger_Late : IFilterBuildAssemblies
    {
        public int callbackOrder => 100;
        public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IFilterBuildAssemblies.OnFilterAssemblies Assemblies: {assemblies.Length}", "◉");
            return assemblies;
        }
    }
    
    public class FilterAssembliesLogger_P10000 : IFilterBuildAssemblies
    {
        public int callbackOrder => 10000;
        public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IFilterBuildAssemblies.OnFilterAssemblies Assemblies: {assemblies.Length}", "⚑ P10000");
            return assemblies;
        }
    }
    
    public class FilterAssembliesLogger_MaxValue : IFilterBuildAssemblies
    {
        public int callbackOrder => int.MaxValue;
        public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IFilterBuildAssemblies.OnFilterAssemblies Assemblies: {assemblies.Length}", "⛳ END");
            return assemblies;
        }
    }
    
    #endregion

    #region ==================== IPostBuildPlayerScriptDLLs ====================
    
    public class PostBuildScriptDLLsLogger_MinValue : IPostBuildPlayerScriptDLLs
    {
        public int callbackOrder => int.MinValue;
        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "IPostBuildPlayerScriptDLLs.OnPostBuildPlayerScriptDLLs", "🚩 POST-DLL");
        }
    }
    
    public class PostBuildScriptDLLsLogger_N10000 : IPostBuildPlayerScriptDLLs
    {
        public int callbackOrder => -10000;
        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "IPostBuildPlayerScriptDLLs.OnPostBuildPlayerScriptDLLs", "⚑ N10000");
        }
    }
    
    public class PostBuildScriptDLLsLogger_Early : IPostBuildPlayerScriptDLLs
    {
        public int callbackOrder => -100;
        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "IPostBuildPlayerScriptDLLs.OnPostBuildPlayerScriptDLLs", "◎");
        }
    }
    
    public class PostBuildScriptDLLsLogger_Mid : IPostBuildPlayerScriptDLLs
    {
        public int callbackOrder => 0;
        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "IPostBuildPlayerScriptDLLs.OnPostBuildPlayerScriptDLLs", "○");
        }
    }
    
    public class PostBuildScriptDLLsLogger_Late : IPostBuildPlayerScriptDLLs
    {
        public int callbackOrder => 100;
        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "IPostBuildPlayerScriptDLLs.OnPostBuildPlayerScriptDLLs", "◉");
        }
    }
    
    public class PostBuildScriptDLLsLogger_P10000 : IPostBuildPlayerScriptDLLs
    {
        public int callbackOrder => 10000;
        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "IPostBuildPlayerScriptDLLs.OnPostBuildPlayerScriptDLLs", "⚑ P10000");
        }
    }
    
    public class PostBuildScriptDLLsLogger_MaxValue : IPostBuildPlayerScriptDLLs
    {
        public int callbackOrder => int.MaxValue;
        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "IPostBuildPlayerScriptDLLs.OnPostBuildPlayerScriptDLLs", "⛳ END");
        }
    }
    
    #endregion

    #region ==================== IProcessSceneWithReport ====================
    
    public class ProcessSceneLogger_MinValue : IProcessSceneWithReport
    {
        public int callbackOrder => int.MinValue;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IProcessSceneWithReport.OnProcessScene Scene: {scene.name}", "🎬 SCENE");
        }
    }
    
    public class ProcessSceneLogger_N10000 : IProcessSceneWithReport
    {
        public int callbackOrder => -10000;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IProcessSceneWithReport.OnProcessScene Scene: {scene.name}", "⚑ N10000");
        }
    }
    
    public class ProcessSceneLogger_Early : IProcessSceneWithReport
    {
        public int callbackOrder => -100;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IProcessSceneWithReport.OnProcessScene Scene: {scene.name}", "◎");
        }
    }
    
    public class ProcessSceneLogger_Mid : IProcessSceneWithReport
    {
        public int callbackOrder => 0;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IProcessSceneWithReport.OnProcessScene Scene: {scene.name}", "○");
        }
    }
    
    public class ProcessSceneLogger_Late : IProcessSceneWithReport
    {
        public int callbackOrder => 100;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IProcessSceneWithReport.OnProcessScene Scene: {scene.name}", "◉");
        }
    }
    
    public class ProcessSceneLogger_P10000 : IProcessSceneWithReport
    {
        public int callbackOrder => 10000;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IProcessSceneWithReport.OnProcessScene Scene: {scene.name}", "⚑ P10000");
        }
    }
    
    public class ProcessSceneLogger_MaxValue : IProcessSceneWithReport
    {
        public int callbackOrder => int.MaxValue;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IProcessSceneWithReport.OnProcessScene Scene: {scene.name}", "⛳ END");
        }
    }
    
    #endregion

    #region ==================== IPreprocessShaders ====================
    
    public class PreprocessShadersLogger_MinValue : IPreprocessShaders
    {
        public int callbackOrder => int.MinValue;
        
        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline && BuildPipelineLoggerLog.ShouldLogShader(GetType()))
            {
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IPreprocessShaders.OnProcessShader Shader: {shader.name}", "🎨 SHADER");
            }
        }
    }
    
    public class PreprocessShadersLogger_N10000 : IPreprocessShaders
    {
        public int callbackOrder => -10000;
        
        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline && BuildPipelineLoggerLog.ShouldLogShader(GetType()))
            {
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IPreprocessShaders.OnProcessShader Shader: {shader.name}", "⚑ N10000");
            }
        }
    }
    
    public class PreprocessShadersLogger_Early : IPreprocessShaders
    {
        public int callbackOrder => -100;
        
        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline && BuildPipelineLoggerLog.ShouldLogShader(GetType()))
            {
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IPreprocessShaders.OnProcessShader Shader: {shader.name}", "◎");
            }
        }
    }
    
    public class PreprocessShadersLogger_Mid : IPreprocessShaders
    {
        public int callbackOrder => 0;
        
        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline && BuildPipelineLoggerLog.ShouldLogShader(GetType()))
            {
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IPreprocessShaders.OnProcessShader Shader: {shader.name}", "○");
            }
        }
    }
    
    public class PreprocessShadersLogger_Late : IPreprocessShaders
    {
        public int callbackOrder => 100;
        
        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline && BuildPipelineLoggerLog.ShouldLogShader(GetType()))
            {
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IPreprocessShaders.OnProcessShader Shader: {shader.name}", "◉");
            }
        }
    }
    
    public class PreprocessShadersLogger_P10000 : IPreprocessShaders
    {
        public int callbackOrder => 10000;
        
        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline && BuildPipelineLoggerLog.ShouldLogShader(GetType()))
            {
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IPreprocessShaders.OnProcessShader Shader: {shader.name}", "⚑ P10000");
            }
        }
    }
    
    public class PreprocessShadersLogger_MaxValue : IPreprocessShaders
    {
        public int callbackOrder => int.MaxValue;
        
        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline && BuildPipelineLoggerLog.ShouldLogShader(GetType()))
            {
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IPreprocessShaders.OnProcessShader Shader: {shader.name}", "⛳ END");
            }
        }
    }
    
    #endregion

    #region ==================== IPreprocessComputeShaders ====================
    
    public class PreprocessComputeShadersLogger_MinValue : IPreprocessComputeShaders
    {
        public int callbackOrder => int.MinValue;
        
        public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> data)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline && BuildPipelineLoggerLog.ShouldLogShader(GetType()))
            {
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IPreprocessComputeShaders.OnProcessComputeShader Shader: {shader.name}, Kernel: {kernelName}", "🎛 COMPUTE");
            }
        }
    }
    
    public class PreprocessComputeShadersLogger_N10000 : IPreprocessComputeShaders
    {
        public int callbackOrder => -10000;
        
        public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> data)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline && BuildPipelineLoggerLog.ShouldLogShader(GetType()))
            {
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IPreprocessComputeShaders.OnProcessComputeShader Shader: {shader.name}, Kernel: {kernelName}", "⚑ N10000");
            }
        }
    }
    
    public class PreprocessComputeShadersLogger_Early : IPreprocessComputeShaders
    {
        public int callbackOrder => -100;
        
        public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> data)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline && BuildPipelineLoggerLog.ShouldLogShader(GetType()))
            {
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IPreprocessComputeShaders.OnProcessComputeShader Shader: {shader.name}, Kernel: {kernelName}", "◎");
            }
        }
    }
    
    public class PreprocessComputeShadersLogger_Mid : IPreprocessComputeShaders
    {
        public int callbackOrder => 0;
        
        public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> data)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline && BuildPipelineLoggerLog.ShouldLogShader(GetType()))
            {
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IPreprocessComputeShaders.OnProcessComputeShader Shader: {shader.name}, Kernel: {kernelName}", "○");
            }
        }
    }
    
    public class PreprocessComputeShadersLogger_Late : IPreprocessComputeShaders
    {
        public int callbackOrder => 100;
        
        public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> data)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline && BuildPipelineLoggerLog.ShouldLogShader(GetType()))
            {
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IPreprocessComputeShaders.OnProcessComputeShader Shader: {shader.name}, Kernel: {kernelName}", "◉");
            }
        }
    }
    
    public class PreprocessComputeShadersLogger_P10000 : IPreprocessComputeShaders
    {
        public int callbackOrder => 10000;
        
        public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> data)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline && BuildPipelineLoggerLog.ShouldLogShader(GetType()))
            {
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IPreprocessComputeShaders.OnProcessComputeShader Shader: {shader.name}, Kernel: {kernelName}", "⚑ P10000");
            }
        }
    }
    
    public class PreprocessComputeShadersLogger_MaxValue : IPreprocessComputeShaders
    {
        public int callbackOrder => int.MaxValue;
        
        public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> data)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline && BuildPipelineLoggerLog.ShouldLogShader(GetType()))
            {
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, $"IPreprocessComputeShaders.OnProcessComputeShader Shader: {shader.name}, Kernel: {kernelName}", "⛳ END");
            }
        }
    }
    
    #endregion

    // IUnityLinkerProcessor 需要 Unity.Build.Pipeline 包，在VRChat项目中可能不可用
    // 如需测试，请取消注释并添加包引用

    /*
    #region ==================== IUnityLinkerProcessor ====================
    
    public class UnityLinkerLogger_Early : IUnityLinkerProcessor
    {
        public int callbackOrder => -100;
        
        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "IUnityLinkerProcessor.GenerateAdditionalLinkXmlFile", "◎");
            return null;
        }
    }
    
    public class UnityLinkerLogger_Mid : IUnityLinkerProcessor
    {
        public int callbackOrder => 0;
        
        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "IUnityLinkerProcessor.GenerateAdditionalLinkXmlFile", "○");
            return null;
        }
    }
    
    public class UnityLinkerLogger_Late : IUnityLinkerProcessor
    {
        public int callbackOrder => 100;
        
        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "IUnityLinkerProcessor.GenerateAdditionalLinkXmlFile", "◉");
            return null;
        }
    }
    
    #endregion
    */

    #region ==================== IPostprocessBuildWithReport ====================
    
    public class PostBuildLogger_MinValue : IPostprocessBuildWithReport
    {
        public int callbackOrder => int.MinValue;
        public void OnPostprocessBuild(BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "IPostprocessBuildWithReport.OnPostprocessBuild", "🚩 POST");
        }
    }
    
    public class PostBuildLogger_N10000 : IPostprocessBuildWithReport
    {
        public int callbackOrder => -10000;
        public void OnPostprocessBuild(BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "IPostprocessBuildWithReport.OnPostprocessBuild", "⚑ N10000");
        }
    }
    
    public class PostBuildLogger_Early : IPostprocessBuildWithReport
    {
        public int callbackOrder => -100;
        public void OnPostprocessBuild(BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "IPostprocessBuildWithReport.OnPostprocessBuild", "◎");
        }
    }
    
    public class PostBuildLogger_Mid : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPostprocessBuild(BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "IPostprocessBuildWithReport.OnPostprocessBuild", "○");
        }
    }
    
    public class PostBuildLogger_Late : IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;
        public void OnPostprocessBuild(BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "IPostprocessBuildWithReport.OnPostprocessBuild", "◉");
        }
    }
    
    public class PostBuildLogger_P10000 : IPostprocessBuildWithReport
    {
        public int callbackOrder => 10000;
        public void OnPostprocessBuild(BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "IPostprocessBuildWithReport.OnPostprocessBuild", "⚑ P10000");
        }
    }
    
    public class PostBuildLogger_MaxValue : IPostprocessBuildWithReport
    {
        public int callbackOrder => int.MaxValue;
        public void OnPostprocessBuild(BuildReport report)
        {
            if (BuildPipelineLoggerSettings.BuildPipeline)
                BuildPipelineLoggerLog.Log("BUILD", callbackOrder, "IPostprocessBuildWithReport.OnPostprocessBuild", "⛳ END");
        }
    }
    
    #endregion

    #region ==================== AssetPostprocessor ====================
    
    /// <summary>
    /// AssetPostprocessor 使用 postprocessOrder 而非 callbackOrder
    /// 这些回调在资产导入时触发，而非构建时
    /// </summary>
    
    public class AssetPostprocessorLogger_MinValue : AssetPostprocessor
    {
        public override int GetPostprocessOrder() => int.MinValue;
        
        void OnPreprocessTexture()
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessTexture Asset: {assetPath}", "🚩 IMPORT");
        }
        
        void OnPostprocessTexture(Texture2D texture)
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessTexture Asset: {assetPath}", "🚩 IMPORT");
        }
        
        void OnPreprocessModel()
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessModel Asset: {assetPath}", "🚩 IMPORT");
        }
        
        void OnPostprocessModel(GameObject g)
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessModel Asset: {assetPath}", "🚩 IMPORT");
        }
        
        void OnPreprocessAudio()
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessAudio Asset: {assetPath}", "🚩 IMPORT");
        }
        
        void OnPostprocessAudio(AudioClip clip)
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessAudio Asset: {assetPath}", "🚩 IMPORT");
        }
        
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (BuildPipelineLoggerSettings.AssetPipeline && (importedAssets.Length > 0 || deletedAssets.Length > 0 || movedAssets.Length > 0))
            {
                BuildPipelineLoggerLog.Log("ASSET", int.MinValue, $"AssetPostprocessor.OnPostprocessAllAssets Imported: {importedAssets.Length}, Deleted: {deletedAssets.Length}, Moved: {movedAssets.Length}", "📦 BATCH");
            }
        }
    }
    
    public class AssetPostprocessorLogger_N10000 : AssetPostprocessor
    {
        public override int GetPostprocessOrder() => -10000;
        
        void OnPreprocessTexture()
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessTexture Asset: {assetPath}", "⚑ N10000");
        }
        
        void OnPostprocessTexture(Texture2D texture)
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessTexture Asset: {assetPath}", "⚑ N10000");
        }
        
        void OnPreprocessModel()
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessModel Asset: {assetPath}", "⚑ N10000");
        }
        
        void OnPostprocessModel(GameObject g)
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessModel Asset: {assetPath}", "⚑ N10000");
        }
        
        void OnPreprocessAudio()
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessAudio Asset: {assetPath}", "⚑ N10000");
        }
        
        void OnPostprocessAudio(AudioClip clip)
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessAudio Asset: {assetPath}", "⚑ N10000");
        }
    }
    
    public class AssetPostprocessorLogger_Early : AssetPostprocessor
    {
        public override int GetPostprocessOrder() => -100;
        
        void OnPreprocessTexture()
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessTexture Asset: {assetPath}", "◎");
        }
        
        void OnPostprocessTexture(Texture2D texture)
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessTexture Asset: {assetPath}", "◎");
        }
        
        void OnPreprocessModel()
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessModel Asset: {assetPath}", "◎");
        }
        
        void OnPostprocessModel(GameObject g)
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessModel Asset: {assetPath}", "◎");
        }
        
        void OnPreprocessAudio()
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessAudio Asset: {assetPath}", "◎");
        }
        
        void OnPostprocessAudio(AudioClip clip)
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessAudio Asset: {assetPath}", "◎");
        }
    }
    
    public class AssetPostprocessorLogger_Mid : AssetPostprocessor
    {
        public override int GetPostprocessOrder() => 0;
        
        void OnPreprocessTexture()
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessTexture Asset: {assetPath}", "○");
        }
        
        void OnPostprocessTexture(Texture2D texture)
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessTexture Asset: {assetPath}", "○");
        }
        
        void OnPreprocessModel()
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessModel Asset: {assetPath}", "○");
        }
        
        void OnPostprocessModel(GameObject g)
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessModel Asset: {assetPath}", "○");
        }
        
        void OnPreprocessAudio()
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessAudio Asset: {assetPath}", "○");
        }
        
        void OnPostprocessAudio(AudioClip clip)
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessAudio Asset: {assetPath}", "○");
        }
    }
    
    public class AssetPostprocessorLogger_Late : AssetPostprocessor
    {
        public override int GetPostprocessOrder() => 100;
        
        void OnPreprocessTexture()
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessTexture Asset: {assetPath}", "◉");
        }
        
        void OnPostprocessTexture(Texture2D texture)
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessTexture Asset: {assetPath}", "◉");
        }
        
        void OnPreprocessModel()
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessModel Asset: {assetPath}", "◉");
        }
        
        void OnPostprocessModel(GameObject g)
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessModel Asset: {assetPath}", "◉");
        }
        
        void OnPreprocessAudio()
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessAudio Asset: {assetPath}", "◉");
        }
        
        void OnPostprocessAudio(AudioClip clip)
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessAudio Asset: {assetPath}", "◉");
        }
    }
    
    public class AssetPostprocessorLogger_P10000 : AssetPostprocessor
    {
        public override int GetPostprocessOrder() => 10000;
        
        void OnPreprocessTexture()
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessTexture Asset: {assetPath}", "⚑ P10000");
        }
        
        void OnPostprocessTexture(Texture2D texture)
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessTexture Asset: {assetPath}", "⚑ P10000");
        }
        
        void OnPreprocessModel()
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessModel Asset: {assetPath}", "⚑ P10000");
        }
        
        void OnPostprocessModel(GameObject g)
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessModel Asset: {assetPath}", "⚑ P10000");
        }
        
        void OnPreprocessAudio()
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessAudio Asset: {assetPath}", "⚑ P10000");
        }
        
        void OnPostprocessAudio(AudioClip clip)
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessAudio Asset: {assetPath}", "⚑ P10000");
        }
    }
    
    public class AssetPostprocessorLogger_MaxValue : AssetPostprocessor
    {
        public override int GetPostprocessOrder() => int.MaxValue;
        
        void OnPreprocessTexture()
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessTexture Asset: {assetPath}", "⛳ END");
        }
        
        void OnPostprocessTexture(Texture2D texture)
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessTexture Asset: {assetPath}", "⛳ END");
        }
        
        void OnPreprocessModel()
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessModel Asset: {assetPath}", "⛳ END");
        }
        
        void OnPostprocessModel(GameObject g)
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessModel Asset: {assetPath}", "⛳ END");
        }
        
        void OnPreprocessAudio()
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPreprocessAudio Asset: {assetPath}", "⛳ END");
        }
        
        void OnPostprocessAudio(AudioClip clip)
        {
            if (BuildPipelineLoggerSettings.AssetPipeline)
                BuildPipelineLoggerLog.Log("ASSET", GetPostprocessOrder(), $"AssetPostprocessor.OnPostprocessAudio Asset: {assetPath}", "⛳ END");
        }
    }
    
    #endregion


}
