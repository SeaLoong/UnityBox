namespace UnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// ASS 系统常量定义
    /// </summary>
    public static class Constants
    {
        // ============ 资源路径 ============
        public const string ASSET_FOLDER = "Assets/UnityBox/AvatarSecuritySystem/Generated";
        public const string CONTROLLER_NAME = "ASS_Controller.controller";
        public const string SHARED_EMPTY_CLIP_NAME = "ASS_SharedEmpty.anim";

        // ============ 音频资源 ============
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
        public const string GO_UI = "ASS_UI";
        public const string GO_AUDIO_WARNING = "ASS_Audio_Warning";
        public const string GO_AUDIO_SUCCESS = "ASS_Audio_Success";
        public const string GO_DEFENSE_ROOT = "ASS_Defense";

        // ============ VRChat 组件上限 ============
        public const int PHYSBONE_MAX_COUNT = 256;
        public const int CONTACT_MAX_COUNT = 256;
        public const int CONSTRAINT_MAX_COUNT = 2000;
        public const int PHYSBONE_COLLIDER_MAX_COUNT = 256;
        public const int PHYSBONE_COLLIDER_CHECK_MAX_COUNT = 10000;
        public const int RIGIDBODY_MAX_COUNT = 256;
        public const int CLOTH_MAX_COUNT = 256;
        public const int ANIMATOR_MAX_COUNT = 256;
        public const int POLY_VERTICES_MAX_COUNT = 2560000;
        public const int PARTICLE_MAX_COUNT = 2560000;
        public const int PARTICLE_SYSTEM_MAX_COUNT = 256;
        public const int LIGHT_MAX_COUNT = 256;
        public const int MATERIAL_MAX_COUNT = 4;
    }
}