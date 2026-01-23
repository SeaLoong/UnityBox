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
        public const string AUDIO_FOLDER = "Audio";

        // 音频资源路径（Resources 文件夹相对路径）
        public const string AUDIO_RESOURCE_PATH = "AvatarSecuritySystem";
        public const string AUDIO_STEP_SUCCESS = "StepSuccess";
        public const string AUDIO_PASSWORD_SUCCESS = "PasswordSuccess";
        public const string AUDIO_INPUT_ERROR = "InputError";
        public const string AUDIO_COUNTDOWN_WARNING = "CountdownWarning";

        // Animator 参数名称
        public const string PARAM_LOCKED = "ASS_Locked";
        public const string PARAM_UNLOCKED = "ASS_Unlocked";
        public const string PARAM_PASSWORD_CORRECT = "ASS_PasswordCorrect";
        public const string PARAM_TIME_UP = "ASS_TimeUp";
        public const string PARAM_PASSWORD_ERROR = "ASS_PasswordError";
        public const string PARAM_IS_LOCAL = "IsLocal"; // VRChat 内置参数

        // VRChat 手势参数
        public const string PARAM_GESTURE_LEFT = "GestureLeft";
        public const string PARAM_GESTURE_RIGHT = "GestureRight";

        // Layer 名称
        public const string LAYER_INITIAL_LOCK = "ASS_InitialLock";
        public const string LAYER_PASSWORD_INPUT = "ASS_PasswordInput";
        public const string LAYER_COUNTDOWN = "ASS_Countdown";
        public const string LAYER_DEFENSE = "ASS_Defense";

        // GameObject 名称
        public const string GO_ASS_ROOT = "__ASS_System__";
        public const string GO_UI_CANVAS = "ASS_UI";
        public const string GO_FEEDBACK_AUDIO = "ASS_Audio";
        public const string GO_WARNING_AUDIO = "ASS_WarningAudio";
        public const string GO_PARTICLES = "ASS_Particles";

        // 优化相关
        public const string SHARED_EMPTY_CLIP_NAME = "ASS_SharedEmpty.anim";
        public const int BLENDTREE_CHILDREN_PER_TREE = 100; // 每个 BlendTree 的子项数

        // VRChat 内置手势值
        public const int GESTURE_IDLE = 0;
        public const int GESTURE_FIST = 1;
        public const int GESTURE_HANDOPEN = 2;
        public const int GESTURE_FINGERPOINT = 3;
        public const int GESTURE_VICTORY = 4;
        public const int GESTURE_ROCKNROLL = 5;
        public const int GESTURE_HANDGUN = 6;
        public const int GESTURE_THUMBSUP = 7;
    }
}
