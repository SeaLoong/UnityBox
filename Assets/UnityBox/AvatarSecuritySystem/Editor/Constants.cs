namespace UnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// ASS 系统常量定义
    /// 
    /// 混淆说明（v0.5.0）：
    /// 所有名称常量已从 const 改为 static 字段，用于支持 Obfuscator 的运行时名称替换。
    /// 当 Obfuscator.IsEnabled = true 时，字段值会被替换为混淆后的名称；
    /// 当 Obfuscator 未启用时，字段保持原始值，行为与之前版本完全一致。
    /// 
    /// 原始名称作为 internalOrigin 保留，用于 Obfuscator 的确定性哈希键。
    /// </summary>
    public static class Constants
    {
        // ============ 资源路径（不混淆，仅用于编辑器资产路径） ============
        public const string ASSET_FOLDER = "Assets/UnityBox/AvatarSecuritySystem/Generated";
        public const string CONTROLLER_NAME = "ASS_Controller.controller";
        public const string SHARED_EMPTY_CLIP_NAME = "ASS_SharedEmpty.anim";

        // ============ 音频资源（不混淆，Resources.Load 需要准确名称） ============
        public const string AUDIO_PASSWORD_SUCCESS = "PasswordSuccess";
        public const string AUDIO_COUNTDOWN_WARNING = "CountdownWarning";

        // ============ Animator 参数（可混淆） ============
        /// <summary>原始: ASS_PasswordCorrect</summary>
        public static string PARAM_PASSWORD_CORRECT = "ASS_PasswordCorrect";
        /// <summary>原始: ASS_TimeUp</summary>
        public static string PARAM_TIME_UP = "ASS_TimeUp";
        /// <summary>VRChat 内置参数，不混淆</summary>
        public const string PARAM_IS_LOCAL = "IsLocal";

        // ============ VRChat 手势参数（VRChat 内置，不混淆） ============
        public const string PARAM_GESTURE_LEFT = "GestureLeft";
        public const string PARAM_GESTURE_RIGHT = "GestureRight";

        // ============ Animator 层名称（可混淆） ============
        /// <summary>原始: ASS_Lock</summary>
        public static string LAYER_LOCK = "ASS_Lock";
        /// <summary>原始: ASS_PasswordInput</summary>
        public static string LAYER_PASSWORD_INPUT = "ASS_PasswordInput";
        /// <summary>原始: ASS_Countdown</summary>
        public static string LAYER_COUNTDOWN = "ASS_Countdown";
        /// <summary>原始: ASS_Audio</summary>
        public static string LAYER_AUDIO = "ASS_Audio";
        /// <summary>原始: ASS_Defense</summary>
        public static string LAYER_DEFENSE = "ASS_Defense";

        // ============ GameObject 名称（可混淆） ============
        /// <summary>原始: ASS_Overlay</summary>
        public static string GO_OVERLAY = "ASS_Overlay";
        /// <summary>原始: ASS_Audio_Warning</summary>
        public static string GO_AUDIO_WARNING = "ASS_Audio_Warning";
        /// <summary>原始: ASS_Audio_Success</summary>
        public static string GO_AUDIO_SUCCESS = "ASS_Audio_Success";
        /// <summary>原始: ASS_Defense</summary>
        public static string GO_DEFENSE_ROOT = "ASS_Defense";
        /// <summary>原始: Overlay (ASS_Overlay 的子 mesh 对象名称，用于倒计时进度条动画绑定)</summary>
        public static string GO_OVERLAY_MESH = "Overlay";


        /// <summary>
        /// 应用混淆名称。由 Obfuscator 在初始化时调用。
        /// 将所有可混淆字段替换为混淆后的值。
        /// </summary>
        internal static void ApplyObfuscation()
        {
            if (!Obfuscator.IsEnabled) return;

            PARAM_PASSWORD_CORRECT = Obfuscator.Param("PARAM_PASSWORD_CORRECT");
            PARAM_TIME_UP = Obfuscator.Param("PARAM_TIME_UP");
            LAYER_LOCK = Obfuscator.Layer("LAYER_LOCK");
            LAYER_PASSWORD_INPUT = Obfuscator.Layer("LAYER_PASSWORD_INPUT");
            LAYER_COUNTDOWN = Obfuscator.Layer("LAYER_COUNTDOWN");
            LAYER_AUDIO = Obfuscator.Layer("LAYER_AUDIO");
            LAYER_DEFENSE = Obfuscator.Layer("LAYER_DEFENSE");
            GO_OVERLAY = Obfuscator.GameObject("GO_OVERLAY");
            GO_AUDIO_WARNING = Obfuscator.GameObject("GO_AUDIO_WARNING");
            GO_AUDIO_SUCCESS = Obfuscator.GameObject("GO_AUDIO_SUCCESS");
            GO_DEFENSE_ROOT = Obfuscator.GameObject("GO_DEFENSE_ROOT");
            GO_OVERLAY_MESH = Obfuscator.GameObject("GO_OVERLAY_MESH");
        }

        // ============ VRChat 组件上限（不混淆，数值常量） ============
        public const int RIGIDBODY_MAX_COUNT = 256;
        public const int RIGIDBODY_COLLIDER_MAX_COUNT = 1024;
        public const int CLOTH_MAX_COUNT = 256;
        public const int PARTICLE_MAX_COUNT = 2147483647;
        public const int PARTICLE_SYSTEM_MAX_COUNT = 355;
        public const int LIGHT_MAX_COUNT = 256;
        public const int SHADER_DEFENSE_COUNT = 8;
        public const int MESH_PARTICLE_MAX_POLYGONS = 2147483647;
        public const int TOTAL_CLOTH_VERTICES_MAX = 2560000;
    }
}