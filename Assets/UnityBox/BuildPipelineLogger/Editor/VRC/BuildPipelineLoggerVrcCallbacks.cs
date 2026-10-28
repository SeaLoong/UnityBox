using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace UnityBox.BuildPipelineLogger
{
    #region ==================== VRCSDK Callbacks ====================

    /*
     * ===================== 已知框架的 callbackOrder 值 =====================
     *
     * 以下是各主要框架在 IVRCSDKPreprocessAvatarCallback 中使用的 callbackOrder 值:
     *
     * int.MinValue      : (无框架占用)
     * int.MinValue + 1  : VRCFury FailureCheckStart (构建失败检查起点)
     *                   : VRCFury IsActuallyUploadingHook (检测是否真正上传)
     *                   : VRCFury WhenBlueprintIdReadyHook (Blueprint ID 就绪回调)
     * -11000            : NDMF BuildFrameworkPreprocessHook (Resolving → Transforming)
     * -10000            : VRCFury VrcPreuploadHook (主处理, 调用 VRCFuryBuilder.RunMain)
     * -1025             : NDMF BuildFrameworkOptimizeHook (Optimizing → Last)
     * -1024             : VRCFury VrcfRemoveEditorOnlyObjects / VRCSDK RemoveAvatarEditorOnly
     * 0                 : 默认值
     * 100               : 常规后处理
     * int.MaxValue - 100: VRCFury ParameterCompressorHook (参数压缩，几乎最后执行)
     * int.MaxValue      : VRCFury FailureCheckEnd (构建失败检查终点)
     *                   : VRCFury VrcfRemoveEditorOnlyComponents (移除 EditorOnly 组件)
     *                   : MA ReplacementRemoveIEditorOnly (销毁所有 IEditorOnly 组件)
     *
     * VRCFury 的完整 Hook 列表 (按 callbackOrder 排序):
     *   1. FailureCheckStart          (int.MinValue + 1)  - 构建失败检查起点
     *   2. IsActuallyUploadingHook    (int.MinValue + 1)  - 检测是否真正上传
     *   3. WhenBlueprintIdReadyHook   (int.MinValue + 1)  - Blueprint ID 就绪
     *   4. VrcPreuploadHook           (-10000)            - 主处理入口
     *   5. VrcfRemoveEditorOnlyObjects(-1024)             - 移除 EditorOnly 对象
     *   6. ParameterCompressorHook    (int.MaxValue - 100)- 参数压缩
     *   7. FailureCheckEnd            (int.MaxValue)      - 构建失败检查终点
     *   8. VrcfRemoveEditorOnlyComponents(int.MaxValue)   - 移除 EditorOnly 组件
     *
     * 本日志记录器在这些关键点的前后都设置了探测器，以便确认执行顺序
     * ======================================================================
     */

    // ==================== IVRCSDKBuildRequestedCallback ====================

    public class VRCSDKBuildRequestedLogger_MinValue : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => int.MinValue;
        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, $"IVRCSDKBuildRequestedCallback.OnBuildRequested Type: {requestedBuildType}", "🚩 REQ");
            return true;
        }
    }

    public class VRCSDKBuildRequestedLogger_N10000 : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => -10000;
        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, $"IVRCSDKBuildRequestedCallback.OnBuildRequested Type: {requestedBuildType}", "⚑ N10000");
            return true;
        }
    }

    public class VRCSDKBuildRequestedLogger_Early : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => -100;
        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, $"IVRCSDKBuildRequestedCallback.OnBuildRequested Type: {requestedBuildType}", "◎");
            return true;
        }
    }

    public class VRCSDKBuildRequestedLogger_Mid : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => 0;
        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, $"IVRCSDKBuildRequestedCallback.OnBuildRequested Type: {requestedBuildType}", "○");
            return true;
        }
    }

    public class VRCSDKBuildRequestedLogger_Late : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => 100;
        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, $"IVRCSDKBuildRequestedCallback.OnBuildRequested Type: {requestedBuildType}", "◉");
            return true;
        }
    }

    public class VRCSDKBuildRequestedLogger_P10000 : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => 10000;
        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, $"IVRCSDKBuildRequestedCallback.OnBuildRequested Type: {requestedBuildType}", "⚑ P10000");
            return true;
        }
    }

    public class VRCSDKBuildRequestedLogger_MaxValue : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => int.MaxValue;
        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, $"IVRCSDKBuildRequestedCallback.OnBuildRequested Type: {requestedBuildType}", "⛳ END");
            return true;
        }
    }

    // ==================== IVRCSDKPreprocessAvatarCallback 完整阶段覆盖 ====================

    /// <summary>绝对最早 - int.MinValue</summary>
    public class VRCSDKPreprocessAvatarLogger_MinValue : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            BuildPipelineLoggerLog.BeginBuild();
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, $"IVRCSDKPreprocessAvatarCallback Avatar: {avatarGameObject.name}", "🚀 PRE");
            return true;
        }
    }

    /// <summary>VRCFury 早期 Hooks 位置探测 (int.MinValue + 1) - FailureCheckStart / IsActuallyUploadingHook / WhenBlueprintIdReadyHook</summary>
    public class VRCSDKPreprocessAvatarLogger_VRCFuryEarlyHooks : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 1;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, "VRCFury FailureCheckStart / IsActuallyUploadingHook / WhenBlueprintIdReadyHook", "★★ VRCFURY-EARLY");
            return true;
        }
    }

    /// <summary>在 VRCFury 早期 Hooks 之后 (int.MinValue + 2)</summary>
    public class VRCSDKPreprocessAvatarLogger_AfterVRCFuryEarlyHooks : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 2;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, "After VRCFury Early Hooks", "↘ AFTER VRCFURY-EARLY");
            return true;
        }
    }

    /// <summary>在 NDMF PreprocessHook 之前 (-11001)</summary>
    public class VRCSDKPreprocessAvatarLogger_BeforeNDMFPreprocess : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -11001;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, "Before NDMF PreprocessHook", "↗ BEFORE NDMF-PRE");
            return true;
        }
    }

    /// <summary>NDMF BuildFrameworkPreprocessHook 位置探测 (-11000)</summary>
    public class VRCSDKPreprocessAvatarLogger_NDMFPreprocess : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -11000;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, "NDMF PreprocessHook (Resolving → Transforming)", "★★ NDMF-PREPROC");
            return true;
        }
    }

    /// <summary>在 NDMF PreprocessHook 之后、VRCFury 之前 (-10999)</summary>
    public class VRCSDKPreprocessAvatarLogger_AfterNDMFPreprocess : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -10999;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, "After NDMF PreprocessHook", "↘ AFTER NDMF-PRE");
            return true;
        }
    }

    /// <summary>在 VRCFury 之前 (-10001)</summary>
    public class VRCSDKPreprocessAvatarLogger_BeforeVRCFury : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -10001;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, "Before VRCFury", "↗ BEFORE VRCFURY");
            return true;
        }
    }

    /// <summary>VRCFury 位置探测 (-10000)</summary>
    public class VRCSDKPreprocessAvatarLogger_VRCFury : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -10000;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, "VRCFury Hook", "★★ VRCFURY");
            return true;
        }
    }

    /// <summary>在 VRCFury 之后 (-9999)</summary>
    public class VRCSDKPreprocessAvatarLogger_AfterVRCFury : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -9999;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, "After VRCFury", "↘ AFTER VRCFURY");
            return true;
        }
    }

    /// <summary>在 NDMF OptimizeHook 之前 (-1026)</summary>
    public class VRCSDKPreprocessAvatarLogger_BeforeNDMFOptimize : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -1026;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, "Before NDMF OptimizeHook", "↗ BEFORE NDMF-OPT");
            return true;
        }
    }

    /// <summary>NDMF BuildFrameworkOptimizeHook 位置探测 (-1025)</summary>
    public class VRCSDKPreprocessAvatarLogger_NDMFOptimize : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -1025;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, "NDMF OptimizeHook (Optimizing → Last)", "★★ NDMF-OPT");
            return true;
        }
    }

    /// <summary>在 RemoveAvatarEditorOnly 之前 / NDMF OptimizeHook 之后 (-1024)</summary>
    public class VRCSDKPreprocessAvatarLogger_AfterNDMFOptimize : IVRCSDKPreprocessAvatarCallback
    {
        // 注意: -1024 同时被 VRCSDK RemoveAvatarEditorOnly 和 VRCFury VrcfRemoveEditorOnlyObjects 使用
        // VRCFury 通过 Harmony Patch 移除了原始 RemoveAvatarEditorOnly，替换为自己的 VrcfRemoveEditorOnlyObjects
        public int callbackOrder => -1024;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, "RemoveEditorOnly (VRCFury replaces VRCSDK's implementation)", "★ REMOVE-EDITOR");
            return true;
        }
    }

    /// <summary>在 RemoveAvatarEditorOnly 之后 (-1023)</summary>
    public class VRCSDKPreprocessAvatarLogger_AfterRemoveEditorOnly : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -1023;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, "After RemoveAvatarEditorOnly", "↘ AFTER REMOVE-EDITOR");
            return true;
        }
    }

    /// <summary>经典测试点 - Early (-100)</summary>
    public class VRCSDKPreprocessAvatarLogger_Early : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -100;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, $"IVRCSDKPreprocessAvatarCallback Avatar: {avatarGameObject.name}", "◎");
            return true;
        }
    }

    /// <summary>经典测试点 - Mid (0)</summary>
    public class VRCSDKPreprocessAvatarLogger_Mid : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => 0;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, $"IVRCSDKPreprocessAvatarCallback Avatar: {avatarGameObject.name}", "○");
            return true;
        }
    }

    /// <summary>经典测试点 - Late (100)</summary>
    public class VRCSDKPreprocessAvatarLogger_Late : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => 100;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, $"IVRCSDKPreprocessAvatarCallback Avatar: {avatarGameObject.name}", "◉");
            return true;
        }
    }

    /// <summary>在 VRCFury ParameterCompressorHook 之前 (int.MaxValue - 101)</summary>
    public class VRCSDKPreprocessAvatarLogger_BeforeParamCompressor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MaxValue - 101;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, "Before VRCFury ParameterCompressor", "↗ BEFORE PARAM-COMPRESS");
            return true;
        }
    }

    /// <summary>VRCFury ParameterCompressorHook 位置探测 (int.MaxValue - 100)</summary>
    public class VRCSDKPreprocessAvatarLogger_ParamCompressor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MaxValue - 100;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, "VRCFury ParameterCompressorHook (参数压缩)", "★★ PARAM-COMPRESS");
            return true;
        }
    }

    /// <summary>在 VRCFury ParameterCompressorHook 之后 (int.MaxValue - 99)</summary>
    public class VRCSDKPreprocessAvatarLogger_AfterParamCompressor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MaxValue - 99;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, "After VRCFury ParameterCompressor", "↘ AFTER PARAM-COMPRESS");
            return true;
        }
    }

    /// <summary>在 MaxValue 之前 (int.MaxValue - 1)</summary>
    public class VRCSDKPreprocessAvatarLogger_BeforeMaxValue : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MaxValue - 1;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, "Before MaxValue", "↗ BEFORE END");
            return true;
        }
    }

    /// <summary>绝对最后 - int.MaxValue (VRCFury FailureCheckEnd/VrcfRemoveEditorOnlyComponents, MA RemoveIEditorOnly)</summary>
    public class VRCSDKPreprocessAvatarLogger_MaxValue : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MaxValue;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, "VRCFury Cleanup + MA RemoveIEditorOnly (destroy IEditorOnly)", "★ FINAL-CLEANUP");
            return true;
        }
    }

    // ==================== IVRCSDKPostprocessAvatarCallback ====================

    public class VRCSDKPostprocessAvatarLogger_MinValue : IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue;
        public void OnPostprocessAvatar()
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, "IVRCSDKPostprocessAvatarCallback.OnPostprocessAvatar", "🚩 POST");
        }
    }

    public class VRCSDKPostprocessAvatarLogger_N10000 : IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => -10000;
        public void OnPostprocessAvatar()
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, "IVRCSDKPostprocessAvatarCallback.OnPostprocessAvatar", "⚑ N10000");
        }
    }

    public class VRCSDKPostprocessAvatarLogger_Early : IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => -100;
        public void OnPostprocessAvatar()
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, "IVRCSDKPostprocessAvatarCallback.OnPostprocessAvatar", "◎");
        }
    }

    public class VRCSDKPostprocessAvatarLogger_Mid : IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => 0;
        public void OnPostprocessAvatar()
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, "IVRCSDKPostprocessAvatarCallback.OnPostprocessAvatar", "○");
        }
    }

    public class VRCSDKPostprocessAvatarLogger_Late : IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => 100;
        public void OnPostprocessAvatar()
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, "IVRCSDKPostprocessAvatarCallback.OnPostprocessAvatar", "◉");
        }
    }

    public class VRCSDKPostprocessAvatarLogger_P10000 : IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => 10000;
        public void OnPostprocessAvatar()
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, "IVRCSDKPostprocessAvatarCallback.OnPostprocessAvatar", "⚑ P10000");
        }
    }

    public class VRCSDKPostprocessAvatarLogger_MaxValue : IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => int.MaxValue;
        public void OnPostprocessAvatar()
        {
            if (BuildPipelineLoggerSettings.VRCSDK)
                BuildPipelineLoggerLog.Log("VRCSDK", callbackOrder, "IVRCSDKPostprocessAvatarCallback.OnPostprocessAvatar", "⛳ END");
        }
    }

    #endregion

}
