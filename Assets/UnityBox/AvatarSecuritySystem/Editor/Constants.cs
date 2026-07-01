namespace UnityBox.AvatarSecuritySystem.Editor
{
    public static class Constants
    {
        private static class DefaultNames
        {
            public const string SharedEmptyClipDisplayName = "_Empty";
            public const string ParamPasswordCorrect = "ASS_PasswordCorrect";
            public const string ParamTimeUp = "ASS_TimeUp";
            public const string LayerLock = "ASS_Lock";
            public const string LayerPasswordInput = "ASS_PasswordInput";
            public const string LayerCountdown = "ASS_Countdown";
            public const string LayerAudio = "ASS_Audio";
            public const string LayerDefense = "ASS_Defense";
            public const string Overlay = "ASS_Overlay";
            public const string AudioWarning = "ASS_Audio_Warning";
            public const string AudioSuccess = "ASS_Audio_Success";
            public const string DefenseRoot = "ASS_Defense";
            public const string OverlayMesh = "Overlay";
            public const string ClipLock = "ASS_Lock";
            public const string ClipLockLocal = "ASS_LockLocal";
            public const string ClipRemote = "ASS_Remote";
            public const string ClipUnlock = "ASS_Unlock";
            public const string ClipCountdown = "ASS_Countdown";
            public const string ClipAudioWaiting = "ASS_AudioWaiting";
            public const string ClipAudioStop = "ASS_AudioStop";
            public const string ClipWarningLoop = "ASS_WarningLoop";
            public const string ClipDefenseActivate = "ASS_DefenseActivate";
            public const string ClipDefenseActiveDefault = "ASS_DefenseActive_Default";
            public const string ClipPasswordSuccess = "ASS_PasswordSuccess";
            public const string ClipPasswordCompleted = "ASS_PasswordCompleted";
            public const string ParticleRoot = "ParticleDefense";
            public const string LightRoot = "LightDefense";
            public const string PhysXRoot = "PhysXDefense";
            public const string ClothRoot = "ClothDefense";
            public const string ShaderRoot = "ShaderDefense";
            public const string DefenseMesh = "ASS_Mesh";
            public const string ShaderMesh = "ASS_ShaderMesh";
            public const string PsPrefix = "PS";
            public const string SubEmitterPrefix = "SubEmitter";
            public const string LightPrefix = "L";
            public const string RbPrefix = "Rigidbody";
            public const string ColliderPrefix = "Collider";
            public const string ClothPrefix = "Cloth";
            public const string ShaderMatPrefix = "ShaderMat";
        }

        public const string ASSET_FOLDER = "Assets/UnityBox/AvatarSecuritySystem/Generated";
        public const string CONTROLLER_NAME = "_FX.controller";
        public const string SHARED_EMPTY_CLIP_FILE = "_E.anim";
        public static string SHARED_EMPTY_CLIP_DISPLAY_NAME = DefaultNames.SharedEmptyClipDisplayName;
        public const string AUDIO_PASSWORD_SUCCESS = "PasswordSuccess";
        public const string AUDIO_COUNTDOWN_WARNING = "CountdownWarning";
        public static string PARAM_PASSWORD_CORRECT = DefaultNames.ParamPasswordCorrect;
        public static string PARAM_TIME_UP = DefaultNames.ParamTimeUp;
        public const string PARAM_IS_LOCAL = "IsLocal";
        public const string PARAM_GESTURE_LEFT = "GestureLeft";
        public const string PARAM_GESTURE_RIGHT = "GestureRight";
        public static string LAYER_LOCK = DefaultNames.LayerLock;
        public static string LAYER_PASSWORD_INPUT = DefaultNames.LayerPasswordInput;
        public static string LAYER_COUNTDOWN = DefaultNames.LayerCountdown;
        public static string LAYER_AUDIO = DefaultNames.LayerAudio;
        public static string LAYER_DEFENSE = DefaultNames.LayerDefense;
        public static string GO_OVERLAY = DefaultNames.Overlay;
        public static string GO_AUDIO_WARNING = DefaultNames.AudioWarning;
        public static string GO_AUDIO_SUCCESS = DefaultNames.AudioSuccess;
        public static string GO_DEFENSE_ROOT = DefaultNames.DefenseRoot;
        public static string GO_OVERLAY_MESH = DefaultNames.OverlayMesh;
        public static string CLIP_LOCK = DefaultNames.ClipLock;
        public static string CLIP_LOCK_LOCAL = DefaultNames.ClipLockLocal;
        public static string CLIP_REMOTE = DefaultNames.ClipRemote;
        public static string CLIP_UNLOCK = DefaultNames.ClipUnlock;
        public static string CLIP_COUNTDOWN = DefaultNames.ClipCountdown;
        public static string CLIP_AUDIO_WAITING = DefaultNames.ClipAudioWaiting;
        public static string CLIP_AUDIO_STOP = DefaultNames.ClipAudioStop;
        public static string CLIP_WARNING_LOOP = DefaultNames.ClipWarningLoop;
        public static string CLIP_DEFENSE_ACTIVATE = DefaultNames.ClipDefenseActivate;
        public static string CLIP_DEFENSE_ACTIVE_DEFAULT = DefaultNames.ClipDefenseActiveDefault;
        public static string CLIP_PASSWORD_SUCCESS = DefaultNames.ClipPasswordSuccess;
        public static string CLIP_PASSWORD_COMPLETED = DefaultNames.ClipPasswordCompleted;
        public static string GO_PARTICLE_ROOT = DefaultNames.ParticleRoot;
        public static string GO_LIGHT_ROOT = DefaultNames.LightRoot;
        public static string GO_PHYSX_ROOT = DefaultNames.PhysXRoot;
        public static string GO_CLOTH_ROOT = DefaultNames.ClothRoot;
        public static string GO_SHADER_ROOT = DefaultNames.ShaderRoot;
        public static string GO_DEFENSE_MESH = DefaultNames.DefenseMesh;
        public static string GO_SHADER_MESH = DefaultNames.ShaderMesh;
        public static string GO_PS_PREFIX = DefaultNames.PsPrefix;
        public static string GO_SUB_EMITTER_PREFIX = DefaultNames.SubEmitterPrefix;
        public static string GO_LIGHT_PREFIX = DefaultNames.LightPrefix;
        public static string GO_RB_PREFIX = DefaultNames.RbPrefix;
        public static string GO_COLLIDER_PREFIX = DefaultNames.ColliderPrefix;
        public static string GO_CLOTH_PREFIX = DefaultNames.ClothPrefix;
        public static string GO_SHADER_MAT_PREFIX = DefaultNames.ShaderMatPrefix;

        internal static void ApplyObfuscation()
        {
            PARAM_PASSWORD_CORRECT = Obfuscator.Param("PARAM_PASSWORD_CORRECT", DefaultNames.ParamPasswordCorrect);
            PARAM_TIME_UP = Obfuscator.Param("PARAM_TIME_UP", DefaultNames.ParamTimeUp);
            LAYER_LOCK = Obfuscator.Layer("LAYER_LOCK", DefaultNames.LayerLock);
            LAYER_PASSWORD_INPUT = Obfuscator.Layer("LAYER_PASSWORD_INPUT", DefaultNames.LayerPasswordInput);
            LAYER_COUNTDOWN = Obfuscator.Layer("LAYER_COUNTDOWN", DefaultNames.LayerCountdown);
            LAYER_AUDIO = Obfuscator.Layer("LAYER_AUDIO", DefaultNames.LayerAudio);
            LAYER_DEFENSE = Obfuscator.Layer("LAYER_DEFENSE", DefaultNames.LayerDefense);
            GO_OVERLAY = Obfuscator.GameObject("GO_OVERLAY", DefaultNames.Overlay);
            GO_AUDIO_WARNING = Obfuscator.GameObject("GO_AUDIO_WARNING", DefaultNames.AudioWarning);
            GO_AUDIO_SUCCESS = Obfuscator.GameObject("GO_AUDIO_SUCCESS", DefaultNames.AudioSuccess);
            GO_DEFENSE_ROOT = Obfuscator.GameObject("GO_DEFENSE_ROOT", DefaultNames.DefenseRoot);
            GO_OVERLAY_MESH = Obfuscator.GameObject("GO_OVERLAY_MESH", DefaultNames.OverlayMesh);
            SHARED_EMPTY_CLIP_DISPLAY_NAME = Obfuscator.Clip("SHARED_EMPTY_CLIP", DefaultNames.SharedEmptyClipDisplayName);
            CLIP_LOCK = Obfuscator.Clip("CLIP_LOCK", DefaultNames.ClipLock);
            CLIP_LOCK_LOCAL = Obfuscator.Clip("CLIP_LOCK_LOCAL", DefaultNames.ClipLockLocal);
            CLIP_REMOTE = Obfuscator.Clip("CLIP_REMOTE", DefaultNames.ClipRemote);
            CLIP_UNLOCK = Obfuscator.Clip("CLIP_UNLOCK", DefaultNames.ClipUnlock);
            CLIP_COUNTDOWN = Obfuscator.Clip("CLIP_COUNTDOWN", DefaultNames.ClipCountdown);
            CLIP_AUDIO_WAITING = Obfuscator.Clip("CLIP_AUDIO_WAITING", DefaultNames.ClipAudioWaiting);
            CLIP_AUDIO_STOP = Obfuscator.Clip("CLIP_AUDIO_STOP", DefaultNames.ClipAudioStop);
            CLIP_WARNING_LOOP = Obfuscator.Clip("CLIP_WARNING_LOOP", DefaultNames.ClipWarningLoop);
            CLIP_DEFENSE_ACTIVATE = Obfuscator.Clip("CLIP_DEFENSE_ACTIVATE", DefaultNames.ClipDefenseActivate);
            CLIP_DEFENSE_ACTIVE_DEFAULT = Obfuscator.Clip("CLIP_DEFENSE_ACTIVE_DEFAULT", DefaultNames.ClipDefenseActiveDefault);
            CLIP_PASSWORD_SUCCESS = Obfuscator.Clip("CLIP_PASSWORD_SUCCESS", DefaultNames.ClipPasswordSuccess);
            CLIP_PASSWORD_COMPLETED = Obfuscator.Clip("CLIP_PASSWORD_COMPLETED", DefaultNames.ClipPasswordCompleted);
            GO_PARTICLE_ROOT = Obfuscator.GameObject("GO_PARTICLE_ROOT", DefaultNames.ParticleRoot);
            GO_LIGHT_ROOT = Obfuscator.GameObject("GO_LIGHT_ROOT", DefaultNames.LightRoot);
            GO_PHYSX_ROOT = Obfuscator.GameObject("GO_PHYSX_ROOT", DefaultNames.PhysXRoot);
            GO_CLOTH_ROOT = Obfuscator.GameObject("GO_CLOTH_ROOT", DefaultNames.ClothRoot);
            GO_SHADER_ROOT = Obfuscator.GameObject("GO_SHADER_ROOT", DefaultNames.ShaderRoot);
            GO_DEFENSE_MESH = Obfuscator.GameObject("GO_DEFENSE_MESH", DefaultNames.DefenseMesh);
            GO_SHADER_MESH = Obfuscator.GameObject("GO_SHADER_MESH", DefaultNames.ShaderMesh);
            GO_PS_PREFIX = Obfuscator.GameObject("GO_PS_PREFIX", DefaultNames.PsPrefix);
            GO_SUB_EMITTER_PREFIX = Obfuscator.GameObject("GO_SUB_EMITTER_PREFIX", DefaultNames.SubEmitterPrefix);
            GO_LIGHT_PREFIX = Obfuscator.GameObject("GO_LIGHT_PREFIX", DefaultNames.LightPrefix);
            GO_RB_PREFIX = Obfuscator.GameObject("GO_RB_PREFIX", DefaultNames.RbPrefix);
            GO_COLLIDER_PREFIX = Obfuscator.GameObject("GO_COLLIDER_PREFIX", DefaultNames.ColliderPrefix);
            GO_CLOTH_PREFIX = Obfuscator.GameObject("GO_CLOTH_PREFIX", DefaultNames.ClothPrefix);
            GO_SHADER_MAT_PREFIX = Obfuscator.GameObject("GO_SHADER_MAT_PREFIX", DefaultNames.ShaderMatPrefix);
        }

        public static bool IsASSManagedLayerName(string layerName)
        {
            if (string.IsNullOrEmpty(layerName))
                return false;

            return layerName == LAYER_LOCK
                || layerName == LAYER_PASSWORD_INPUT
                || layerName == LAYER_COUNTDOWN
                || layerName == LAYER_AUDIO
                || layerName == LAYER_DEFENSE
                || layerName == DefaultNames.LayerLock
                || layerName == DefaultNames.LayerPasswordInput
                || layerName == DefaultNames.LayerCountdown
                || layerName == DefaultNames.LayerAudio
                || layerName == DefaultNames.LayerDefense;
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
