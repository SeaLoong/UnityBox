using UnityEngine;
using System.Collections.Generic;

namespace UnityBox.AvatarSecuritySystem.Editor
{
    public sealed class ASSConfigData
    {
        public ASSConfigData Clone()
        {
            var clone = (ASSConfigData)MemberwiseClone();
            clone.gesturePassword = gesturePassword == null ? null : new System.Collections.Generic.List<int>(gesturePassword);
            return clone;
        }

        public SystemLanguage uiLanguage;
        public List<int> gesturePassword;
        public bool useRightHand;
        public float countdownDuration;
        public float warningThreshold;
        public float gestureHoldTime;
        public float gestureErrorTolerance;
        public float gestureMaxHoldTime;
        public AudioClip warningBeep;
        public AudioClip successSound;
        public bool enabledInPlaymode;
        public bool disableDefense;
        public bool disableRootChildren;
        public bool disableOverlay;
        public bool disableWarningSound;
        public bool defaultEnableDefense;
        public bool lightweightDefense;
        public bool disableObfuscation;
        public bool enablePlayableLayerObfuscation;
        public bool enableDecoyLayers;
        public bool enableDecoyStates;
        public int writeDefaultsMode;
        public string sourceObjectName;

        public static ASSConfigData FromAvatar(GameObject avatarRoot)
        {
            if (avatarRoot == null) return null;

            return FromComponent(
                avatarRoot.GetComponent<ASSComponent>()
                ?? avatarRoot.GetComponentInChildren<ASSComponent>(true));
        }

        public static ASSConfigData FromComponent(ASSComponent source)
        {
            if (source == null) return null;

            return new ASSConfigData
            {
                uiLanguage = source.uiLanguage,
                gesturePassword = source.gesturePassword != null
                    ? new List<int>(source.gesturePassword)
                    : new List<int>(),
                useRightHand = source.useRightHand,
                countdownDuration = source.countdownDuration,
                warningThreshold = source.warningThreshold,
                gestureHoldTime = source.gestureHoldTime,
                gestureErrorTolerance = source.gestureErrorTolerance,
                gestureMaxHoldTime = source.gestureMaxHoldTime,
                warningBeep = source.warningBeep,
                successSound = source.successSound,
                enabledInPlaymode = source.enabledInPlaymode,
                disableDefense = source.disableDefense,
                disableRootChildren = source.disableRootChildren,
                disableOverlay = source.disableOverlay,
                disableWarningSound = source.disableWarningSound,
                defaultEnableDefense = source.defaultEnableDefense,
                lightweightDefense = source.lightweightDefense,
                disableObfuscation = source.disableObfuscation,
                enablePlayableLayerObfuscation = source.enablePlayableLayerObfuscation,
                enableDecoyLayers = source.enableDecoyLayers,
                enableDecoyStates = source.enableDecoyStates,
                writeDefaultsMode = (int)source.writeDefaultsMode,
                sourceObjectName = source.gameObject != null ? source.gameObject.name : null
            };
        }

        public bool IsPasswordValid()
        {
            if (gesturePassword == null || gesturePassword.Count == 0)
            {
                return true;
            }

            const int minimumGestureValue = 0;
            const int maximumGestureValue = 7;
            foreach (int gesture in gesturePassword)
            {
                if (gesture < minimumGestureValue || gesture > maximumGestureValue) return false;
            }

            return true;
        }
    }

}
