namespace SeaLoongUnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// ASS 系统常量定义
    /// </summary>
    public static class Constants
    {
        // ============ 系统信息 ============
        public const string SYSTEM_NAME = "Avatar Security System";
        public const string SYSTEM_SHORT_NAME = "ASS";
        public const string PLUGIN_QUALIFIED_NAME = "top.sealoong.unitybox.avatar-security";

        // ============ 资源路径 ============
        public const string ASSET_FOLDER = "Assets/SeaLoong's UnityBox/Generated/ASS";
        public const string CONTROLLER_NAME = "ASS_Controller.controller";
        public const string ANIMATIONS_FOLDER = "Animations";
        public const string SHARED_EMPTY_CLIP_NAME = "ASS_SharedEmpty.anim";

        // ============ 音频资源 ============
        public const string AUDIO_RESOURCE_PATH = "AvatarSecuritySystem";
        public const string AUDIO_PASSWORD_SUCCESS = "PasswordSuccess";
        public const string AUDIO_COUNTDOWN_WARNING = "CountdownWarning";

        // ============ Animator 参数 ============
        public const string PARAM_PASSWORD_CORRECT = "ASS_PasswordCorrect";
        public const string PARAM_TIME_UP = "ASS_TimeUp";
        public const string PARAM_IS_LOCAL = "IsLocal";

        // ============ VRChat 手势参数 ============
        public const string PARAM_GESTURE_LEFT = "GestureLeft";
        public const string PARAM_GESTURE_RIGHT = "GestureRight";

        // ============ Animator 层名称 ============
        public const string LAYER_LOCK = "ASS_Lock";
        public const string LAYER_PASSWORD_INPUT = "ASS_PasswordInput";
        public const string LAYER_COUNTDOWN = "ASS_Countdown";
        public const string LAYER_AUDIO = "ASS_Audio";
        public const string LAYER_DEFENSE = "ASS_Defense";

        // ============ GameObject 名称 ============
        public const string GO_ASS_ROOT = "__ASS_System__";
        public const string GO_UI_CANVAS = "ASS_UI";
        public const string GO_AUDIO_WARNING = "ASS_Audio_Warning";
        public const string GO_AUDIO_SUCCESS = "ASS_Audio_Success";
        public const string GO_PARTICLES = "ASS_Particles";
        public const string GO_DEFENSE_ROOT = "__ASS_Defense__";
        public const string GO_OCCLUSION_MESH = "ASS_OcclusionMesh";

        // ============ VRChat 组件上限 ============
        /// <summary>
        /// PhysBone 数量上限（256个）
        /// 需要考虑：模型本身的PhysBone + ASS防御的PhysBone不能超过此值
        /// </summary>
        public const int PHYSBONE_MAX_COUNT = 256;

        /// <summary>
        /// Contact 组件总数上限（200个）
        /// Sender + Receiver 总计不超过200个
        /// </summary>
        public const int CONTACT_MAX_COUNT = 200;

        // ============ DefenseSystem 防御参数上限 ============
        /// <summary>
        /// Constraint链深度上限（100）
        /// 每条链的节点深度不超过100，以避免过度复杂的约束链
        /// </summary>
        public const int CONSTRAINT_CHAIN_MAX_DEPTH = 100;

        /// <summary>
        /// PhysBone链长度上限（256）
        /// 单条PhysBone链的骨骼数量不超过256
        /// </summary>
        public const int PHYSBONE_CHAIN_MAX_LENGTH = 256;

        /// <summary>
        /// PhysBone碰撞器数量上限（256）
        /// 单个PhysBone的碰撞器不超过256个
        /// </summary>
        public const int PHYSBONE_COLLIDER_MAX_COUNT = 256;

        /// <summary>
        /// Shader循环次数上限（3000000）
        /// 防御Shader的最大循环次数，用于GPU端的复杂计算防御
        /// </summary>
        public const int SHADER_LOOP_MAX_COUNT = 3000000;
    }
}