using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace SeaLoongUnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// 动画剪辑生成工具 - 创建各种反馈和控制动画
    /// </summary>
    public static class AnimationClipGenerator
    {
        /// <summary>
        /// 创建 GameObject 激活状态动画
        /// 警告：此方法使用 m_IsActive，在 VRChat 中不受 Write Defaults 影响
        /// 建议使用 Scale=0 代替 m_IsActive 来控制对象可见性
        /// </summary>
        [System.Obsolete("VRChat's m_IsActive is not affected by Write Defaults. Consider using Scale=0 instead.")]
        public static AnimationClip CreateGameObjectActiveClip(string name, GameObject root, GameObject[] targets, bool[] activeStates)
        {
            var clip = new AnimationClip { name = name, legacy = false };
            
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            for (int i = 0; i < targets.Length; i++)
            {
                string path = AnimatorUtils.GetRelativePath(root, targets[i]);
                var curve = AnimationCurve.Constant(0f, 1f / 60f, activeStates[i] ? 1f : 0f);
                clip.SetCurve(path, typeof(GameObject), "m_IsActive", curve);
            }

            return clip;
        }

        /// <summary>
        /// 创建倒计时动画（用于驱动时间参数）
        /// </summary>
        public static AnimationClip CreateCountdownTimerClip(float duration)
        {
            var clip = new AnimationClip { name = "ASS_CountdownTimer" };
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            // 创建一个从 duration 到 0 的曲线（用于剩余时间）
            var curve = AnimationCurve.Linear(0f, duration, duration, 0f);
            
            // 使用一个虚拟属性存储时间值
            clip.SetCurve("", typeof(Animator), "ASS_TimeValue", curve);

            return clip;
        }

        /// <summary>
        /// 创建参数反转动画（用于初始锁定）
        /// </summary>
        public static AnimationClip CreateParameterInversionClip(Dictionary<string, object> parameterValues)
        {
            var clip = new AnimationClip { name = "ASS_ParameterInversion" };
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            // 这里应该根据实际的参数类型创建曲线
            // 由于无法直接在AnimationClip中修改Animator参数默认值
            // 实际实现需要通过其他方式（如Modular Avatar Parameters）

            return clip;
        }

        /// <summary>
        /// 创建音效触发动画
        /// </summary>
        public static AnimationClip CreateAudioTriggerClip(string audioPath, float duration = 0.1f)
        {
            var clip = new AnimationClip { name = "ASS_AudioTrigger" };
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var curve = AnimationCurve.Constant(0f, duration, 1f);
            clip.SetCurve(audioPath, typeof(AudioSource), "m_Enabled", curve);

            return clip;
        }

        /// <summary>
        /// 保存动画剪辑到资产
        /// </summary>
        public static void SaveClip(AnimationClip clip, string relativePath)
        {
            string fullPath = $"{Constants.ASSET_FOLDER}/{Constants.ANIMATIONS_FOLDER}/{relativePath}";
            string directory = System.IO.Path.GetDirectoryName(fullPath);
            
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            AssetDatabase.CreateAsset(clip, fullPath);
        }

        /// <summary>
        /// 批量保存动画剪辑
        /// </summary>
        public static void SaveClips(Dictionary<string, AnimationClip> clips)
        {
            foreach (var kvp in clips)
            {
                SaveClip(kvp.Value, kvp.Key);
            }
        }

        /// <summary>
        /// 创建参数驱动动画（设置 Bool 参数值）
        /// 注意：在 VRChat 中，Animator 参数由动画剪辑通过虚拟属性驱动
        /// </summary>
        public static AnimationClip CreateParameterDriverClip(string name, string parameterName, bool value)
        {
            var clip = new AnimationClip { name = name, legacy = false };
            
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            // 使用虚拟属性绑定来驱动参数
            // 在 VRChat 中，这会在运行时被转换为参数设置
            var curve = AnimationCurve.Constant(0f, 1f / 60f, value ? 1f : 0f);
            
            // 绑定到 Animator 的虚拟属性
            clip.SetCurve("", typeof(Animator), parameterName, curve);

            return clip;
        }

        /// <summary>
        /// 创建参数驱动动画（设置 Float 参数值）
        /// </summary>
        public static AnimationClip CreateParameterDriverClip(string name, string parameterName, float value)
        {
            var clip = new AnimationClip { name = name, legacy = false };
            
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var curve = AnimationCurve.Constant(0f, 1f / 60f, value);
            clip.SetCurve("", typeof(Animator), parameterName, curve);

            return clip;
        }

        /// <summary>
        /// 创建音频触发动画（播放短音效并持续指定时间）
        /// </summary>
        /// <param name="name">动画名称</param>
        /// <param name="audioSourcePath">AudioSource 相对路径</param>
        /// <param name="audioClip">音频剪辑</param>
        /// <param name="duration">持续时间（秒）</param>
        public static AnimationClip CreateAudioTriggerClip(string name, string audioSourcePath, AudioClip audioClip, float duration)
        {
            var clip = new AnimationClip { name = name, legacy = false };
            
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            // 设置 AudioSource.clip
            var clipCurve = new AnimationCurve();
            clipCurve.AddKey(new Keyframe(0f, 0f));
            clipCurve.AddKey(new Keyframe(duration, 0f));
            AnimationUtility.SetObjectReferenceCurve(clip, 
                EditorCurveBinding.PPtrCurve(audioSourcePath, typeof(AudioSource), "m_audioClip"),
                new ObjectReferenceKeyframe[] {
                    new ObjectReferenceKeyframe { time = 0f, value = audioClip },
                    new ObjectReferenceKeyframe { time = duration, value = audioClip }
                });

            // 启用 AudioSource (m_Enabled = true)
            var enableCurve = AnimationCurve.Constant(0f, duration, 1f);
            clip.SetCurve(audioSourcePath, typeof(AudioSource), "m_Enabled", enableCurve);

            // 播放音频 (播放时刻)
            var playCurve = new AnimationCurve();
            playCurve.AddKey(new Keyframe(0f, 1f));  // 在动画开始时触发播放
            playCurve.AddKey(new Keyframe(0.05f, 0f));  // 立即结束触发
            playCurve.AddKey(new Keyframe(duration, 0f));

            return clip;
        }
    }
}
