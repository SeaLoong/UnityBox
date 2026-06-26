using UnityEngine;
using UnityEngine.Serialization;
using System.Collections.Generic;
using VRC.SDKBase;

namespace UnityBox.AvatarSecuritySystem
{
#if UNITY_EDITOR
    [AddComponentMenu("UnityBox/Avatar Security System (ASS)")]
#endif
    public class ASSComponent : MonoBehaviour, IEditorOnly
    {
        [Tooltip("#{language.ui_language_tooltip}")]
        public SystemLanguage uiLanguage = SystemLanguage.Unknown;

        [Tooltip("#{password.gesture_tooltip}")]
        public List<int> gesturePassword = new List<int> { 1, 7, 2, 4 };

        [Tooltip("#{password.use_right_hand_tooltip}")]
        public bool useRightHand = false;

        [Range(10f, 30f)]
        [Tooltip("#{countdown.duration_tooltip}")]
        public float countdownDuration = 30f;

        [HideInInspector] public float warningThreshold = 10f;

        [Range(0.1f, 1f)]
        [Tooltip("#{gesture.hold_time_tooltip}")]
        public float gestureHoldTime = 0.15f;

        [Range(0.1f, 1f)]
        [Tooltip("#{gesture.error_tolerance_tooltip}")]
        public float gestureErrorTolerance = 0.3f;

        [Range(1f, 10f)]
        [Tooltip("#{gesture.max_hold_time_tooltip}")]
        public float gestureMaxHoldTime = 3f;

        [HideInInspector] public AudioClip warningBeep;
        [HideInInspector] public AudioClip successSound;

        [Tooltip("#{advanced.play_mode_tooltip}")]
        public bool enabledInPlaymode = false;

        [Tooltip("#{defense.disable_defense_tooltip}")]
        public bool disableDefense = false;

        [Tooltip("#{advanced.disable_objects_tooltip}")]
        public bool disableRootChildren = true;

        [FormerlySerializedAs("hideUI")]
        [FormerlySerializedAs("hideOverlay")]
        [Tooltip("#{advanced.disable_overlay_tooltip}")]
        public bool disableOverlay = false;

        [FormerlySerializedAs("muteWarningSound")]
        [Tooltip("#{advanced.mute_warning_tooltip}")]
        public bool disableWarningSound = false;

        [Tooltip("#{advanced.default_defense_tooltip}")]
        public bool defaultEnableDefense = false;

        [Tooltip("#{defense.enable_overflow_tooltip}")]
        public bool enableOverflow = true;

        public enum WriteDefaultsMode
        {
            Auto,
            On,
            Off
        }

        [Tooltip("#{advanced.wd_mode_tooltip}")]
        public WriteDefaultsMode writeDefaultsMode = WriteDefaultsMode.Auto;

        public enum VRChatGesture
        {
            Idle = 0,
            Fist = 1,
            HandOpen = 2,
            Fingerpoint = 3,
            Victory = 4,
            RockNRoll = 5,
            HandGun = 6,
            ThumbsUp = 7
        }

        public static string GetGestureName(int gestureIndex)
        {
            const int minimumGestureIndex = 0;
            const int maximumGestureIndex = 7;

            if (gestureIndex < minimumGestureIndex || gestureIndex > maximumGestureIndex)
            {
                return "Invalid";
            }

            return ((VRChatGesture)gestureIndex).ToString();
        }

        public bool IsPasswordValid()
        {
            if (gesturePassword == null || gesturePassword.Count == 0)
            {
                return true;
            }

            return AreAllGesturesValid();
        }

        private bool AreAllGesturesValid()
        {
            const int minimumGestureValue = 0;
            const int maximumGestureValue = 7;

            foreach (int gesture in gesturePassword)
            {
                if (gesture < minimumGestureValue || gesture > maximumGestureValue) return false;
            }

            return true;
        }

        public string GetPasswordStrength()
        {
            if (!IsPasswordValid()) return "Invalid";
            if (gesturePassword.Count == 0) return "Weak";

            int passwordLength = gesturePassword.Count;
            int uniqueGestureCount = new HashSet<int>(gesturePassword).Count;

            if (passwordLength >= 6 && uniqueGestureCount >= 4) return "Strong";
            if (passwordLength >= 4) return "Medium";

            return "Weak";
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            InitializePasswordIfNeeded();
            ValidateWarningThreshold();
            ValidateGestureTimes();
            SanitizeDefenseModes();
        }

        private void InitializePasswordIfNeeded()
        {
            if (gesturePassword == null)
            {
                gesturePassword = new List<int> { 1, 7, 2, 4 };
            }
        }

        private void ValidateWarningThreshold()
        {
            if (warningThreshold > countdownDuration / 2)
            {
                warningThreshold = countdownDuration / 2f;
            }
        }

        private void SanitizeDefenseModes()
        {
            // defaultEnableDefense 和 disableDefense 互斥
            if (defaultEnableDefense && disableDefense)
                disableDefense = false;
        }

        private void ValidateGestureTimes()
        {
            float minSum = gestureHoldTime + gestureErrorTolerance;
            if (gestureMaxHoldTime <= minSum)
            {
                gestureMaxHoldTime = minSum + 0.5f;
                if (gestureMaxHoldTime > 10f)
                    gestureMaxHoldTime = 10f;
            }
        }

        private void Reset()
        {
            const float defaultCountdownDuration = 30f;
            const float defaultWarningThreshold = 10f;
            const float defaultGestureMaxHoldTime = 3f;

            gesturePassword = new List<int> { 1, 7, 2, 4 };
            countdownDuration = defaultCountdownDuration;
            warningThreshold = defaultWarningThreshold;
            gestureMaxHoldTime = defaultGestureMaxHoldTime;
            gestureHoldTime = 0.15f;
            gestureErrorTolerance = 0.3f;
            enabledInPlaymode = false;
            disableRootChildren = true;
            disableWarningSound = false;
            disableOverlay = false;
            defaultEnableDefense = false;
            enableOverflow = true;
        }
#endif
    }
}
