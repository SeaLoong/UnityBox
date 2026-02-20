using UnityEngine;
using System.Collections.Generic;

namespace UnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// ASS 国际化系统
    /// 支持中文、英文、日文
    /// </summary>
    public static class I18n
    {
        private static SystemLanguage _currentLanguage;
        private static Dictionary<string, Dictionary<SystemLanguage, string>> _translations;

        static I18n()
        {
            _currentLanguage = DetectSystemLanguage();
            InitializeTranslations();
        }

        /// <summary>
        /// 获取翻译文本
        /// </summary>
        public static string T(string key)
        {
            if (!_translations.TryGetValue(key, out var languageDict))
            {
                return FormatMissingKey(key);
            }

            return GetLocalizedText(languageDict);
        }

        private static string GetLocalizedText(Dictionary<SystemLanguage, string> languageDict)
        {
            if (languageDict.TryGetValue(_currentLanguage, out var text))
            {
                return text;
            }

            if (IsChinese(_currentLanguage) && languageDict.TryGetValue(SystemLanguage.ChineseSimplified, out var simplifiedText))
            {
                return simplifiedText;
            }

            if (languageDict.TryGetValue(SystemLanguage.English, out var englishText))
            {
                return englishText;
            }

            return string.Empty;
        }

        /// <summary>
        /// 设置语言
        /// </summary>
        public static void SetLanguage(SystemLanguage language)
        {
            _currentLanguage = language == SystemLanguage.Unknown
                ? DetectSystemLanguage()
                : NormalizeLanguage(language);
        }

        /// <summary>
        /// 获取当前语言
        /// </summary>
        public static SystemLanguage GetCurrentLanguage()
        {
            return _currentLanguage;
        }

        private static SystemLanguage DetectSystemLanguage()
        {
            SystemLanguage detectedLanguage = Application.systemLanguage;
            return IsSupportedLanguage(detectedLanguage) ? detectedLanguage : SystemLanguage.English;
        }

        private static SystemLanguage NormalizeLanguage(SystemLanguage language)
        {
            return IsSupportedLanguage(language) ? language : SystemLanguage.English;
        }

        private static bool IsSupportedLanguage(SystemLanguage language)
        {
            return language == SystemLanguage.ChineseSimplified ||
                   language == SystemLanguage.ChineseTraditional ||
                   language == SystemLanguage.Japanese ||
                   language == SystemLanguage.English ||
                   language == SystemLanguage.Chinese;
        }

        private static bool IsChinese(SystemLanguage language)
        {
            return language == SystemLanguage.ChineseTraditional ||
                   language == SystemLanguage.Chinese;
        }

        private static string FormatMissingKey(string key)
        {
            return $"[Missing: {key}]";
        }

        private static void InitializeTranslations()
        {
            _translations = new Dictionary<string, Dictionary<SystemLanguage, string>>
            {
                // ========== 通用 ==========
                ["common.confirm"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Confirm" },
                    { SystemLanguage.ChineseSimplified, "确定" },
                    { SystemLanguage.Japanese, "確認" }
                },
                ["common.cancel"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Cancel" },
                    { SystemLanguage.ChineseSimplified, "取消" },
                    { SystemLanguage.Japanese, "キャンセル" }
                },
                ["common.warning"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Warning" },
                    { SystemLanguage.ChineseSimplified, "警告" },
                    { SystemLanguage.Japanese, "警告" }
                },

                // ========== 语言选择 ==========
                ["language.title"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Language" },
                    { SystemLanguage.ChineseSimplified, "语言" },
                    { SystemLanguage.Japanese, "言語" }
                },
                ["language.auto"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Auto (System)" },
                    { SystemLanguage.ChineseSimplified, "自动（跟随系统）" },
                    { SystemLanguage.Japanese, "自動（システム）" }
                },
                ["language.chinese"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Chinese" },
                    { SystemLanguage.ChineseSimplified, "简体中文" },
                    { SystemLanguage.Japanese, "中国語" }
                },
                ["language.english"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "English" },
                    { SystemLanguage.ChineseSimplified, "英语" },
                    { SystemLanguage.Japanese, "英語" }
                },
                ["language.japanese"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Japanese" },
                    { SystemLanguage.ChineseSimplified, "日语" },
                    { SystemLanguage.Japanese, "日本語" }
                },
                ["language.ui_language_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "UI Language / 界面语言" },
                    { SystemLanguage.ChineseSimplified, "界面语言 / UI Language" },
                    { SystemLanguage.Japanese, "UI言語 / 界面语言" }
                },

                // ========== 系统名称 ==========
                ["system.name"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Avatar Security System" },
                    { SystemLanguage.ChineseSimplified, "Avatar 安全系统" },
                    { SystemLanguage.Japanese, "アバターセキュリティシステム" }
                },
                ["system.short_name"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "ASS" },
                    { SystemLanguage.ChineseSimplified, "ASS" },
                    { SystemLanguage.Japanese, "ASS" }
                },
                ["system.subtitle"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Anti-Theft Password Protection System" },
                    { SystemLanguage.ChineseSimplified, "防盗模密码保护系统" },
                    { SystemLanguage.Japanese, "盗難防止パスワード保護システム" }
                },

                // ========== 密码配置 ==========
                ["password.config"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Password Configuration" },
                    { SystemLanguage.ChineseSimplified, "密码配置" },
                    { SystemLanguage.Japanese, "パスワード設定" }
                },
                ["password.use_right_hand"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Use Right Hand" },
                    { SystemLanguage.ChineseSimplified, "使用右手输入" },
                    { SystemLanguage.Japanese, "右手を使用" }
                },
                ["password.use_right_hand_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "false=Left Hand, true=Right Hand" },
                    { SystemLanguage.ChineseSimplified, "false=左手, true=右手" },
                    { SystemLanguage.Japanese, "false=左手、true=右手" }
                },
                ["gesture.hold_time_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Gesture stability detection time (seconds), must hold gesture for this duration to confirm input" },
                    { SystemLanguage.ChineseSimplified, "手势稳定检测时间（秒），需要保持手势此时间才能确认输入" },
                    { SystemLanguage.Japanese, "ジェスチャー安定検出時間（秒）、確認入力には常にジェスチャーを保持必要" }
                },
                ["gesture.error_tolerance_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Gesture error tolerance time (seconds), can correct after inputting wrong gesture for this duration" },
                    { SystemLanguage.ChineseSimplified, "手势错误容错时间（秒），输入错误手势后有此时间可以纠正" },
                    { SystemLanguage.Japanese, "ジェスチャーエラー許容時間（秒）、間違ったジェスチャーの入力後、この期間中に修正できます" }
                },
                ["password.gesture_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Gesture password sequence, use 1-7 for VRChat gestures:\n1=Fist, 2=HandOpen, 3=Fingerpoint\n4=Victory, 5=RockNRoll, 6=HandGun, 7=ThumbsUp" },
                    { SystemLanguage.ChineseSimplified, "手势密码序列，使用1-7表示VRChat手势:\n1=Fist, 2=HandOpen, 3=Fingerpoint\n4=Victory, 5=RockNRoll, 6=HandGun, 7=ThumbsUp" },
                    { SystemLanguage.Japanese, "ジェスチャーパスワードシーケンス、1-7でVRChatジェスチャーを表す:\n1=Fist, 2=HandOpen, 3=Fingerpoint\n4=Victory, 5=RockNRoll, 6=HandGun, 7=ThumbsUp" }
                },
                // ========== 手势识别配置 ==========
                ["gesture.config"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Gesture Recognition" },
                    { SystemLanguage.ChineseSimplified, "手势识别配置" },
                    { SystemLanguage.Japanese, "ジェスチャー認識設定" }
                },
                ["gesture.hold_time"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Hold Time (sec)" },
                    { SystemLanguage.ChineseSimplified, "保持时间 (秒)" },
                    { SystemLanguage.Japanese, "保持時間 (秒)" }
                },
                ["gesture.error_tolerance"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Error Tolerance (sec)" },
                    { SystemLanguage.ChineseSimplified, "容错时间 (秒)" },
                    { SystemLanguage.Japanese, "エラー許容時間 (秒)" }
                },

                ["password.sequence"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Gesture Password Sequence:" },
                    { SystemLanguage.ChineseSimplified, "手势密码序列：" },
                    { SystemLanguage.Japanese, "ジェスチャーパスワードシーケンス：" }
                },
                ["password.step"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Step {0}:" },
                    { SystemLanguage.ChineseSimplified, "第 {0} 位：" },
                    { SystemLanguage.Japanese, "{0} 番目：" }
                },
                ["password.add_gesture"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "➕ Add Gesture" },
                    { SystemLanguage.ChineseSimplified, "➕ 添加手势" },
                    { SystemLanguage.Japanese, "➕ ジェスチャーを追加" }
                },
                ["password.clear"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "🗑 Clear Password" },
                    { SystemLanguage.ChineseSimplified, "🗑 清空密码" },
                    { SystemLanguage.Japanese, "🗑 パスワードをクリア" }
                },
                ["password.clear_confirm"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Are you sure you want to clear the password?" },
                    { SystemLanguage.ChineseSimplified, "确定要清空密码吗？" },
                    { SystemLanguage.Japanese, "パスワードをクリアしてもよろしいですか？" }
                },
                ["password.delete_step"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Delete this step" },
                    { SystemLanguage.ChineseSimplified, "删除此步骤" },
                    { SystemLanguage.Japanese, "このステップを削除" }
                },
                ["password.strength"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Password Strength: {0} ({1} digits)" },
                    { SystemLanguage.ChineseSimplified, "密码强度：{0} ({1} 位)" },
                    { SystemLanguage.Japanese, "パスワード強度：{0} ({1} 桁)" }
                },
                ["password.strength.weak"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Weak" },
                    { SystemLanguage.ChineseSimplified, "弱" },
                    { SystemLanguage.Japanese, "弱い" }
                },
                ["password.strength.medium"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Medium" },
                    { SystemLanguage.ChineseSimplified, "中" },
                    { SystemLanguage.Japanese, "中程度" }
                },
                ["password.strength.strong"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Strong" },
                    { SystemLanguage.ChineseSimplified, "强" },
                    { SystemLanguage.Japanese, "強い" }
                },
                ["password.empty_warning"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Password is empty (0 digits). ASS is disabled and will not be generated." },
                    { SystemLanguage.ChineseSimplified, "密码为空（0位）。ASS 已禁用，不会生成保护系统。" },
                    { SystemLanguage.Japanese, "パスワードが空です（0桁）。ASSは無効化され、保護システムは生成されません。" }
                },

                // ========== 倒计时配置 ==========
                ["countdown.config"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Countdown Configuration" },
                    { SystemLanguage.ChineseSimplified, "倒计时配置" },
                    { SystemLanguage.Japanese, "カウントダウン設定" }
                },
                ["countdown.duration"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Countdown Duration (sec)" },
                    { SystemLanguage.ChineseSimplified, "倒计时时长 (秒)" },
                    { SystemLanguage.Japanese, "カウントダウン時間 (秒)" }
                },
                ["countdown.duration_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Defense mechanisms are triggered after timeout" },
                    { SystemLanguage.ChineseSimplified, "超时后触发防御" },
                    { SystemLanguage.Japanese, "タイムアウト後に防御が発動" }
                },
                ["countdown.warning_threshold"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Warning Threshold (sec)" },
                    { SystemLanguage.ChineseSimplified, "警告阈值 (秒)" },
                    { SystemLanguage.Japanese, "警告しきい値 (秒)" }
                },

                // ========== 防御配置 ==========
                ["defense.config"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Defense Configuration" },
                    { SystemLanguage.ChineseSimplified, "防御配置" },
                    { SystemLanguage.Japanese, "防御設定" }
                },
                ["defense.level"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Defense Level" },
                    { SystemLanguage.ChineseSimplified, "防御等级" },
                    { SystemLanguage.Japanese, "防御レベル" }
                },
                ["defense.level_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Defense strength after timeout\n0: Only password system (no defense)\n1: CPU defense (all CPU components filled to VRChat limits)\n2: CPU+GPU defense (all CPU+GPU components filled to VRChat limits, including MAX_INT particles, 256 lights, etc.)" },
                    { SystemLanguage.ChineseSimplified, "倒计时结束后触发的防御强度\n0：仅密码系统（不生成防御）\n1：CPU 防御（所有 CPU 组件填满至 VRChat 上限）\n2：CPU+GPU 防御（所有 CPU+GPU 组件填满至 VRChat 上限，包括 MAX_INT 粒子、256 光源等）" },
                    { SystemLanguage.Japanese, "タイムアウト後の防御強度\n0: パスワードシステムのみ（防御なし）\n1: CPU防御（全CPUコンポーネントをVRChat上限まで充填）\n2: CPU+GPU防御（全CPU+GPUコンポーネントをVRChat上限まで充填、MAX_INTパーティクル・256ライト等含む）" }
                },
                ["defense.level_0_desc"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Level 0: Only password system (no defense components)" },
                    { SystemLanguage.ChineseSimplified, "等级 0：仅密码系统（不生成任何防御组件）" },
                    { SystemLanguage.Japanese, "レベル0：パスワードシステムのみ（防御コンポーネントなし）" }
                },
                ["defense.level_1_desc"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Level 1: Password + CPU Defense (all CPU components filled to VRChat limits)\n- Constraint: up to 2000\n- PhysBone: up to 256 chains × 256 bones, 256 colliders\n- Contact: up to 256\n- Animator: up to 256" },
                    { SystemLanguage.ChineseSimplified, "等级 1：密码 + CPU 防御（所有 CPU 组件填满至 VRChat 上限）\n- 约束链：最多 2000\n- PhysBone：最多 256 条 × 256 骨骼 + 256 碰撞器\n- Contact：最多 256\n- Animator：最多 256" },
                    { SystemLanguage.Japanese, "レベル1：パスワード+CPU防御（全CPUコンポーネントをVRChat上限まで充填）\n- 制約：最大2000\n- PhysBone：最大256チェーン×256ボーン+256コライダー\n- Contact：最大256\n- Animator：最大256" }
                },
                ["defense.level_2_desc"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Level 2: Password + CPU + GPU Defense (all CPU+GPU components filled to VRChat limits)\n- CPU: All Level 1 + Rigidbody (256) + Colliders (1024) + Cloth (256)\n- Particles: MAX_INT × 355 systems (auto mesh complexity)\n- Lights: 256\n- Defense Shader: 8 GPU-intensive materials" },
                    { SystemLanguage.ChineseSimplified, "等级 2：密码 + CPU + GPU 防御（所有 CPU+GPU 组件填满至 VRChat 上限）\n- CPU：等级 1 全部 + 刚体 (256) + 碰撞器 (1024) + 布料 (256)\n- 粒子：MAX_INT 粒子 × 355 系统（自适应 Mesh 复杂度）\n- 光源：256\n- 防御 Shader：8 个 GPU 密集材质" },
                    { SystemLanguage.Japanese, "レベル2：パスワード+CPU+GPU防御（全CPU+GPUコンポーネントをVRChat上限まで充填）\n- CPU：レベル1全て+Rigidbody(256)+Collider(1024)+Cloth(256)\n- パーティクル：MAX_INTパーティクル×355システム（自動メッシュ複雑度）\n- ライト：256\n- 防御シェーダー：GPU高負荷マテリアル×8" }
                },
                ["defense.note"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Defense mechanisms are only generated in build mode and do not affect edit/play mode." },
                    { SystemLanguage.ChineseSimplified, "防御机制仅在构建模式生成，编辑和 Play 模式不受影响。" },
                    { SystemLanguage.Japanese, "防御メカニズムはビルドモードでのみ生成され、編集/プレイモードには影響しません。" }
                },

                // ========== 高级选项 ==========
                ["advanced.config"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Advanced Options" },
                    { SystemLanguage.ChineseSimplified, "高级选项" },
                    { SystemLanguage.Japanese, "詳細オプション" }
                },
                ["advanced.play_mode"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Disable in Play Mode" },
                    { SystemLanguage.ChineseSimplified, "Play 模式中禁用" },
                    { SystemLanguage.Japanese, "プレイモードで無効化" }
                },
                ["advanced.play_mode_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Skip ASS generation in Play Mode. When unchecked, defense components use minimal parameters (1 each) for quick testing." },
                    { SystemLanguage.ChineseSimplified, "在 Play 模式下跳过 ASS 生成。取消勾选后，防御组件会使用最小参数（各 1 个）以快速测试。" },
                    { SystemLanguage.Japanese, "プレイモードでASS生成をスキップ。チェックを外すと、防御コンポーネントは最小パラメータ（各 1 個）で素早くテストできます。" }
                },
                ["advanced.disable_defense"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Disable Defense" },
                    { SystemLanguage.ChineseSimplified, "不生成防御" },
                    { SystemLanguage.Japanese, "防御を生成しない" }
                },
                ["advanced.disable_defense_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Do not generate defense mechanisms (only test password system)" },
                    { SystemLanguage.ChineseSimplified, "不生成防御机制（仅测试密码系统）" },
                    { SystemLanguage.Japanese, "防御メカニズムを生成しない（パスワードシステムのみテスト）" }
                },
                ["advanced.hide_ui"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Hide UI" },
                    { SystemLanguage.ChineseSimplified, "隐藏 UI" },
                    { SystemLanguage.Japanese, "UIを非表示" }
                },
                ["advanced.hide_ui_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Do not generate fullscreen overlay UI (mask + progress bar). Audio feedback is still generated." },
                    { SystemLanguage.ChineseSimplified, "不生成全屏覆盖 UI（遮罩 + 进度条）。音频反馈仍然会生成。" },
                    { SystemLanguage.Japanese, "フルスクリーンオーバーレイUI（マスク+プログレスバー）を生成しない。オーディオフィードバックは引き続き生成されます。" }
                },
                ["advanced.overflow_trick"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Overflow Trick" },
                    { SystemLanguage.ChineseSimplified, "溢出技巧" },
                    { SystemLanguage.Japanese, "オーバーフロートリック" }
                },
                ["advanced.overflow_trick_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Overflow Trick: The last particle system and mesh will have their max particles and triangle count increased by 1, causing VRChat stats to overflow past int.MaxValue and display -2147483648." },
                    { SystemLanguage.ChineseSimplified, "溢出技巧：最后一个粒子系统和Mesh的最大粒子数与三角面数各+1，使VRChat统计超出int.MaxValue，显示-2147483648。" },
                    { SystemLanguage.Japanese, "オーバーフロートリック：最後のパーティクルシステムとメッシュの最大パーティクル数・三角数を+1し、VRChat統計をint.MaxValue超えにして-2147483648を表示。" }
                },
                ["advanced.options"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Advanced Options" },
                    { SystemLanguage.ChineseSimplified, "高级选项" },
                    { SystemLanguage.Japanese, "詳細オプション" }
                },
                ["advanced.lock_options"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Lock Options" },
                    { SystemLanguage.ChineseSimplified, "锁定选项" },
                    { SystemLanguage.Japanese, "ロックオプション" }
                },
                ["advanced.disable_objects"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Hide Objects" },
                    { SystemLanguage.ChineseSimplified, "隐藏对象" },
                    { SystemLanguage.Japanese, "オブジェクトを非表示" }
                },
                ["advanced.disable_objects_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Hide all root level child objects when locked" },
                    { SystemLanguage.ChineseSimplified, "锁定时隐藏所有根级子对象" },
                    { SystemLanguage.Japanese, "ロック時にすべてのルートレベル子オブジェクトを非表示" }
                },
                
                // ========== Write Defaults 模式 ==========
                ["advanced.wd_mode"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Write Defaults Mode" },
                    { SystemLanguage.ChineseSimplified, "Write Defaults 模式" },
                    { SystemLanguage.Japanese, "Write Defaults モード" }
                },
                ["advanced.wd_mode_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Animation Write Defaults mode:\nAuto = Detect from existing FX layers (recommended)\nOn = Auto restore\nOff = Explicit restore" },
                    { SystemLanguage.ChineseSimplified, "动画 Write Defaults 模式：\nAuto = 从已有 FX 层自动检测（推荐）\nOn = 自动恢复\nOff = 显式恢复" },
                    { SystemLanguage.Japanese, "アニメーション Write Defaults モード：\nAuto = 既存FXレイヤーから自動検出（推奨）\nOn = 自動復元\nOff = 明示的復元" }
                },
                ["advanced.wd_mode_auto"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Auto" },
                    { SystemLanguage.ChineseSimplified, "自动" },
                    { SystemLanguage.Japanese, "自動" }
                },
                ["advanced.wd_mode_on"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "WD On" },
                    { SystemLanguage.ChineseSimplified, "WD On" },
                    { SystemLanguage.Japanese, "WD On" }
                },
                ["advanced.wd_mode_off"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "WD Off" },
                    { SystemLanguage.ChineseSimplified, "WD Off" },
                    { SystemLanguage.Japanese, "WD Off" }
                },
            };
        }
    }
}
