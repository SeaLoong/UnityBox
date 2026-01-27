namespace SeaLoongUnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// ASS 系统常量定义
    /// </summary>
    public static class Constants
    {
        // 系统名称
        public const string SYSTEM_NAME = "Avatar Security System";
        public const string SYSTEM_SHORT_NAME = "ASS";
        public const string PLUGIN_QUALIFIED_NAME = "top.sealoong.unitybox.avatar-security";

        // 资源路径
        public const string ASSET_FOLDER = "Assets/SeaLoong's UnityBox/Generated/ASS";
        public const string CONTROLLER_NAME = "ASS_Controller.controller";
        public const string ANIMATIONS_FOLDER = "Animations";

        // 音频资源路径（Resources 文件夹相对路径）
        public const string AUDIO_RESOURCE_PATH = "AvatarSecuritySystem";
        public const string AUDIO_PASSWORD_SUCCESS = "PasswordSuccess";
        public const string AUDIO_COUNTDOWN_WARNING = "CountdownWarning";

        // Animator 参数名称
        public const string PARAM_PASSWORD_CORRECT = "ASS_PasswordCorrect";
        public const string PARAM_TIME_UP = "ASS_TimeUp";
        public const string PARAM_IS_LOCAL = "IsLocal"; // VRChat 内置参数

        // VRChat 手势参数
        public const string PARAM_GESTURE_LEFT = "GestureLeft";
        public const string PARAM_GESTURE_RIGHT = "GestureRight";

        // Layer 名称
        public const string LAYER_INITIAL_LOCK = "ASS_InitialLock";
        public const string LAYER_PASSWORD_INPUT = "ASS_PasswordInput";
        public const string LAYER_COUNTDOWN = "ASS_Countdown";
        public const string LAYER_WARNING_AUDIO = "ASS_WarningAudio";
        public const string LAYER_DEFENSE = "ASS_Defense";

        // GameObject 名称
        public const string GO_ASS_ROOT = "__ASS_System__";
        public const string GO_UI_CANVAS = "ASS_UI";
        public const string GO_FEEDBACK_AUDIO = "ASS_Audio";
        public const string GO_WARNING_AUDIO = "ASS_WarningAudio";
        public const string GO_PARTICLES = "ASS_Particles";
        public const string GO_DEFENSE_ROOT = "__ASS_Defense__";
        public const string GO_OCCLUSION_MESH = "ASS_OcclusionMesh";

        // 优化相关
        public const string SHARED_EMPTY_CLIP_NAME = "ASS_SharedEmpty.anim";

        // VRChat 组件上限（防止超过上限导致模型无法上传）
        // 参考：https://docs.vrchat.com/docs/avatars
        
        /// <summary>
        /// PhysBone 数量上限
        /// "Very Poor" Avatar 等级允许更多 PhysBone，但为了保险起见设为256
        /// 需要考虑：模型本身的PhysBone + ASS防御的PhysBone不能超过此值
        /// </summary>
        public const int PHYSBONE_MAX_COUNT = 256;

        /// <summary>
        /// 单个PhysBone链的最大骨骼数
        /// 每条链代表一个VRCPhysBone组件，链越长消耗越大
        /// 建议保持在256以内避免极端性能问题
        /// </summary>
        public const int PHYSBONE_CHAIN_MAX_LENGTH = 256;

        /// <summary>
        /// PhysBone Collider 上限（单个PhysBone可引用的Collider数）
        /// VRCPhysBone最多可以配置256个Collider
        /// </summary>
        public const int PHYSBONE_COLLIDER_MAX_COUNT = 256;

        /// <summary>
        /// Contact Sender/Receiver 组件的总数上限
        /// 为了避免过度消耗，建议不超过200个（Sender+Receiver总计）
        /// </summary>
        public const int CONTACT_MAX_COUNT = 200;

        /// <summary>
        /// Constraint 链的最大深度
        /// 过深会导致计算链过长，建议不超过100
        /// </summary>
        public const int CONSTRAINT_CHAIN_MAX_DEPTH = 100;

        /// <summary>
        /// Overdraw 层数上限（无实际限制，仅编辑器优化）
        /// </summary>
        public const int OVERDRAW_MAX_LAYERS = 10000;

        /// <summary>
        /// 高多边形Mesh顶点总数上限（无硬限制，可分散到多个Mesh中）
        /// 可分散到多个Mesh中突破单Mesh65k顶点限制
        /// </summary>
        public const int HIGHPOLY_MESH_MAX_VERTICES = 100000000; // 1亿顶点

        /// <summary>
        /// Shader 循环次数上限（无硬限制）
        /// GPU会因计算而过载，但VRChat不会阻止
        /// </summary>
        public const int SHADER_LOOP_MAX_COUNT = 1000000; // 100万次循环
    }
}
