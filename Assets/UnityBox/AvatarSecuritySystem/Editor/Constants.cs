namespace UnityBox.AvatarSecuritySystem.Editor
{
    public static class Constants
    {
        public const string ASSET_FOLDER = "Assets/UnityBox/AvatarSecuritySystem/Generated";
        public const string CONTROLLER_NAME = "ASS_Controller.controller";
        public const string SHARED_EMPTY_CLIP_FILE = "_E.anim";
        public static string SHARED_EMPTY_CLIP_DISPLAY_NAME = "_Empty";
        public const string AUDIO_PASSWORD_SUCCESS = "PasswordSuccess";
        public const string AUDIO_COUNTDOWN_WARNING = "CountdownWarning";
        public static string PARAM_PASSWORD_CORRECT = "ASS_PasswordCorrect";
        public static string PARAM_TIME_UP = "ASS_TimeUp";
        public const string PARAM_IS_LOCAL = "IsLocal";
        public const string PARAM_GESTURE_LEFT = "GestureLeft";
        public const string PARAM_GESTURE_RIGHT = "GestureRight";
        public static string LAYER_LOCK = "ASS_Lock";
        public static string LAYER_PASSWORD_INPUT = "ASS_PasswordInput";
        public static string LAYER_COUNTDOWN = "ASS_Countdown";
        public static string LAYER_AUDIO = "ASS_Audio";
        public static string LAYER_DEFENSE = "ASS_Defense";
        public static string GO_OVERLAY = "ASS_Overlay";
        public static string GO_AUDIO_WARNING = "ASS_Audio_Warning";
        public static string GO_AUDIO_SUCCESS = "ASS_Audio_Success";
        public static string GO_DEFENSE_ROOT = "ASS_Defense";
        public static string GO_OVERLAY_MESH = "Overlay";
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
            SHARED_EMPTY_CLIP_DISPLAY_NAME = Obfuscator.Clip("SHARED_EMPTY_CLIP");
        }
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
