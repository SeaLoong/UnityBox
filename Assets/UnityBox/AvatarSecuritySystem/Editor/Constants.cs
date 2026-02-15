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


    }
}