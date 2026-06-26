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
                    { SystemLanguage.English, "Min Hold Time (sec)" },
                    { SystemLanguage.ChineseSimplified, "最小保持时间 (秒)" },
                    { SystemLanguage.Japanese, "最小保持時間 (秒)" }
                },
                ["gesture.max_hold_time"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Max Hold Time (sec)" },
                    { SystemLanguage.ChineseSimplified, "最大保持时间 (秒)" },
                    { SystemLanguage.Japanese, "最大保持時間 (秒)" }
                },
                ["gesture.max_hold_time_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Maximum time (seconds) to hold a single gesture during password input. If held longer, the input resets. Prevents holding a gesture while thinking about the next step." },
                    { SystemLanguage.ChineseSimplified, "密码输入过程中保持单个手势的最大时间（秒）。超过此时间输入将重置。防止一直保持手势去思考下一步手势。" },
                    { SystemLanguage.Japanese, "パスワード入力中に単一ジェスチャーを保持する最大時間（秒）。超えると入力がリセットされます。次のステップを考えながらジェスチャーを保持し続けるのを防ぎます。" }
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

                // ========== 防御选项 ==========
                ["defense.options"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Defense Options" },
                    { SystemLanguage.ChineseSimplified, "防御选项" },
                    { SystemLanguage.Japanese, "防御オプション" }
                },
                ["defense.desc_gpu"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "GPU Defense (all components filled to VRChat limits):\n- Particles: MAX_INT × 355 systems (auto mesh complexity)\n- Lights: 256\n- Rigidbody: 256 + Colliders: 1024\n- Cloth: 256\n- Defense Shader: 8 GPU-intensive materials" },
                    { SystemLanguage.ChineseSimplified, "GPU 防御（所有组件填满至 VRChat 上限）：\n- 粒子：MAX_INT 粒子 × 355 系统（自适应 Mesh 复杂度）\n- 光源：256\n- 刚体：256 + 碰撞器：1024\n- 布料：256\n- 防御 Shader：8 个 GPU 密集材质" },
                    { SystemLanguage.Japanese, "GPU防御（全コンポーネントをVRChat上限まで充填）：\n- パーティクル：MAX_INT×355システム（自動メッシュ複雑度）\n- ライト：256\n- Rigidbody：256+Collider：1024\n- Cloth：256\n- 防御シェーダー：GPU高負荷マテリアル×8" }
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
                    { SystemLanguage.English, "Enable in Play Mode" },
                    { SystemLanguage.ChineseSimplified, "播放模式中启用" },
                    { SystemLanguage.Japanese, "プレイモードで有効化" }
                },
                ["advanced.play_mode_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Enable ASS generation in Play Mode. Defense components use minimal parameters (1 each) for quick testing." },
                    { SystemLanguage.ChineseSimplified, "在播放模式下启用 ASS 生成。防御组件会使用最小参数（各 1 个）以快速测试。" },
                    { SystemLanguage.Japanese, "プレイモードでASS生成を有効化。防御コンポーネントは最小パラメータ（各 1 個）で素早くテストできます。" }
                },
                ["defense.disable_defense"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Disable Defense" },
                    { SystemLanguage.ChineseSimplified, "禁用防御" },
                    { SystemLanguage.Japanese, "防御を無効化" }
                },
                ["defense.disable_defense_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Do not generate defense mechanisms (only test password system). Disabled when 'Default Enable Defense' is active." },
                    { SystemLanguage.ChineseSimplified, "不生成防御机制（仅测试密码系统）。启用'默认启用防御'时此选项不可用。" },
                    { SystemLanguage.Japanese, "防御メカニズムを生成しない（パスワードシステムのみテスト）。「デフォルトで防御を有効化」が有効な場合は使用できません。" }
                },
                ["defense.disable_defense_locked"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Locked while 'Default Enable Defense' is enabled" },
                    { SystemLanguage.ChineseSimplified, "已锁定：当前启用了「默认启用防御」" },
                    { SystemLanguage.Japanese, "ロック中：「デフォルトで防御を有効化」が有効です" }
                },
                ["advanced.hide_ui"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Hide Fullscreen Overlay" },
                    { SystemLanguage.ChineseSimplified, "隐藏全屏覆盖" },
                    { SystemLanguage.Japanese, "フルスクリーンオーバーレイを非表示" }
                },
                ["advanced.hide_ui_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Do not generate the fullscreen overlay (white background + progress bar + logo). Audio feedback is still generated." },
                    { SystemLanguage.ChineseSimplified, "不生成全屏覆盖（白色背景 + 进度条 + 徽标）。音频反馈仍然会生成。" },
                    { SystemLanguage.Japanese, "フルスクリーンオーバーレイ（白背景+プログレスバー+ロゴ）を生成しない。オーディオフィードバックは引き続き生成されます。" }
                },
                ["advanced.mute_warning"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Disable Warning Sound" },
                    { SystemLanguage.ChineseSimplified, "关闭警告音效" },
                    { SystemLanguage.Japanese, "警告音を無効化" }
                },
                ["advanced.mute_warning_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Do not generate the countdown warning beep. The fullscreen overlay (mask + progress bar) is still shown. Use with 'Hide Fullscreen Overlay' to completely silence the system." },
                    { SystemLanguage.ChineseSimplified, "不生成倒计时警告音效。全屏覆盖（遮罩 + 进度条）仍然显示。与'隐藏全屏覆盖'配合可完全关闭系统的所有反馈。" },
                    { SystemLanguage.Japanese, "カウントダウン警告音を生成しない。フルスクリーンオーバーレイ（マスク+プログレスバー）は引き続き表示されます。「フルスクリーンオーバーレイを非表示」と組み合わせてシステムのフィードバックを完全に無効化できます。" }
                },
                ["advanced.default_defense"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Default Enable Defense" },
                    { SystemLanguage.ChineseSimplified, "默认启用防御" },
                    { SystemLanguage.Japanese, "デフォルトで防御を有効化" }
                },
                ["advanced.default_defense_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Defense system is always ON by default. No password input needed. Use after uploading a password-protected model and unlocking it — the saved ASS_PasswordCorrect parameter will keep defense disabled for legitimate users, while thieves who steal the model cannot disable it." },
                    { SystemLanguage.ChineseSimplified, "防御系统默认始终开启，无需密码输入。在上传带密码的模型并解锁后使用此模式——已保存的 ASS_PasswordCorrect 参数会为合法用户保持防御关闭，而盗模者无法关闭防御。" },
                    { SystemLanguage.Japanese, "防御システムがデフォルトで常に有効になります。パスワード入力は不要です。パスワード保護されたモデルをアップロードしてロック解除した後にこのモードを使用してください。保存された ASS_PasswordCorrect パラメータにより正規ユーザーは防御が無効になり、モデルを盗んだ者は防御を無効化できません。" }
                },
                ["advanced.default_defense_warning"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "DANGER: With \"Default Enable Defense\" enabled, NO password system will be generated. You MUST first upload a password-protected version, unlock it in VRChat, then re-upload with this option enabled. If you skip the initial unlock step, defense will trigger for ALL users including yourself! Ensure you understand the workflow before enabling this." },
                    { SystemLanguage.ChineseSimplified, "危险：启用\"默认启用防御\"后，将不会生成密码系统。你必须先上传带密码保护的版本，在VRChat中解锁它，然后再上传启用此选项的版本。如果跳过初始解锁步骤，防御将对所有用户（包括你自己）触发！在启用此选项前请确保你理解整个工作流程。" },
                    { SystemLanguage.Japanese, "危険：「デフォルトで防御を有効化」を有効にすると、パスワードシステムは生成されません。まずパスワード保護されたバージョンをアップロードし、VRChatでロック解除してから、このオプションを有効にして再アップロードする必要があります。最初のロック解除手順をスキップすると、自分自身を含むすべてのユーザーに対して防御が作動します！このオプションを有効にする前に、ワークフローを理解していることを確認してください。" }
                },
                ["defense.enable_overflow"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Overflow Mode" },
                    { SystemLanguage.ChineseSimplified, "溢出模式" },
                    { SystemLanguage.Japanese, "オーバーフローモード" }
                },
                ["defense.enable_overflow_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Overflow: Particle & mesh counts use int.MaxValue+1 directly (bypass budget), making VRChat stats display -2147483648. Disable this for quick testing in Play Mode." },
                    { SystemLanguage.ChineseSimplified, "溢出模式：粒子总数和Mesh面数直接使用int.MaxValue+1（跳过预算计算），VRChat统计显示为-2147483648。Play Mode 测试时可关闭以加快构建。" },
                    { SystemLanguage.Japanese, "オーバーフローモード：パーティクル数とメッシュポリゴン数をint.MaxValue+1で直接生成（予算計算をスキップ）、VRChat統計に-2147483648と表示。Play Modeでのテスト時は無効にすると構築が速くなります。" }
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
                    { SystemLanguage.English, "Conceal Avatar" },
                    { SystemLanguage.ChineseSimplified, "隐藏Avatar本体" },
                    { SystemLanguage.Japanese, "アバター本体を隠す" }
                },
                ["advanced.disable_objects_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Hide all root-level child objects (the avatar itself) when locked. Leave enabled to completely conceal the avatar from the thief." },
                    { SystemLanguage.ChineseSimplified, "锁定时隐藏所有根级子对象（Avatar 本身）。建议保持开启，让盗模者完全看不到模型。" },
                    { SystemLanguage.Japanese, "ロック時にすべてのルートレベル子オブジェクト（アバター本体）を非表示にします。有効にしておくと、盗んだ側からモデルが見えなくなります。" }
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
