using UnityEngine;
using UnityEditor;

namespace SeaLoongUnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// 动画剪辑生成工具 - 创建各种反馈和控制动画
    /// 所有生成的动画都嵌入到 AnimatorController 中作为子资产，不单独保存到磁盘
    /// </summary>
    public static class AnimationClipGenerator
    {
        /// <summary>
        /// 创建参数驱动动画（统一的参数驱动方法）
        /// </summary>
        public static AnimationClip CreateParameterDriverClip(string name, string parameterName, bool value)
        {
            return CreateParameterDriverClipInternal(name, parameterName, value ? 1f : 0f);
        }

        /// <summary>
        /// 创建参数驱动动画（设置 Float 参数值）
        /// </summary>
        public static AnimationClip CreateParameterDriverClip(string name, string parameterName, float value) =>
            CreateParameterDriverClipInternal(name, parameterName, value);
        
        private static AnimationClip CreateParameterDriverClipInternal(string name, string parameterName, float value)
        {
            var clip = new AnimationClip { name = name, legacy = false };
            
            ConfigureAnimationClipSettings(clip);
            var curve = AnimationCurve.Constant(0f, 1f / 60f, value);
            clip.SetCurve("", typeof(Animator), parameterName, curve);

            return clip;
        }
        
        private static void ConfigureAnimationClipSettings(AnimationClip clip)
        {
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
        }

        /// <summary>
        /// 创建音频触发动画（播放短音效并持续指定时间）
        /// </summary>
        public static AnimationClip CreateAudioTriggerClip(string name, string audioSourcePath, AudioClip audioClip, float duration)
        {
            var clip = new AnimationClip { name = name, legacy = false };
            ConfigureAnimationClipSettings(clip);

            // 设置 AudioSource.clip 和启用状态
            SetAudioClipReference(clip, audioSourcePath, audioClip, duration);
            SetAudioSourceEnabled(clip, audioSourcePath, duration);

            return clip;
        }
        
        private static void SetAudioClipReference(AnimationClip clip, string audioSourcePath, AudioClip audioClip, float duration)
        {
            AnimationUtility.SetObjectReferenceCurve(clip, 
                EditorCurveBinding.PPtrCurve(audioSourcePath, typeof(AudioSource), "m_audioClip"),
                new ObjectReferenceKeyframe[]
                {
                    new ObjectReferenceKeyframe { time = 0f, value = audioClip },
                    new ObjectReferenceKeyframe { time = duration, value = audioClip }
                });
        }
        
        private static void SetAudioSourceEnabled(AnimationClip clip, string audioSourcePath, float duration)
        {
            var enableCurve = AnimationCurve.Constant(0f, duration, 1f);
            clip.SetCurve(audioSourcePath, typeof(AudioSource), "m_Enabled", enableCurve);
        }
    }
}
