using UnityEngine;
using System.Collections.Generic;
namespace UnityBox.AvatarSecuritySystem.Editor
{
    public static class I18n
    {
        private static SystemLanguage _currentLanguage;
        private static Dictionary<string, Dictionary<SystemLanguage, string>> _translations;
        static I18n()
        {
            _currentLanguage = DetectSystemLanguage();
            InitializeTranslations();
        }
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
        public static void SetLanguage(SystemLanguage language)
        {
            _currentLanguage = language == SystemLanguage.Unknown
                ? DetectSystemLanguage()
                : NormalizeLanguage(language);
        }
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
                    { SystemLanguage.English, "Gesture stability detection time (seconds)\nMust hold gesture this long before it is accepted as input." },
                    { SystemLanguage.ChineseSimplified, "手势稳定检测时间（秒）\n需要保持手势此时间后才会被确认为有效输入。" },
                    { SystemLanguage.Japanese, "ジェスチャー安定検出時間（秒）\nこの時間ジェスチャーを保持しないと入力として認識されません。" }
                },
                ["gesture.error_tolerance_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Gesture error tolerance time (seconds)\nAfter inputting a wrong gesture, you have this window to correct it before the input resets." },
                    { SystemLanguage.ChineseSimplified, "手势错误容错时间（秒）\n输入错误手势后，在此时限内可纠正为正确手势，超时则输入重置。" },
                    { SystemLanguage.Japanese, "ジェスチャーエラー許容時間（秒）\n間違ったジェスチャーを入力した後、この期間内に修正すれば継続できます。" }
                },
                ["password.gesture_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Gesture password sequence, use 1-7 for VRChat gestures:\n1=Fist, 2=HandOpen, 3=Fingerpoint\n4=Victory, 5=RockNRoll, 6=HandGun, 7=ThumbsUp" },
                    { SystemLanguage.ChineseSimplified, "手势密码序列，使用1-7表示VRChat手势:\n1=Fist, 2=HandOpen, 3=Fingerpoint\n4=Victory, 5=RockNRoll, 6=HandGun, 7=ThumbsUp" },
                    { SystemLanguage.Japanese, "ジェスチャーパスワードシーケンス、1-7でVRChatジェスチャーを表す:\n1=Fist, 2=HandOpen, 3=Fingerpoint\n4=Victory, 5=RockNRoll, 6=HandGun, 7=ThumbsUp" }
                },
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
                    { SystemLanguage.English, "Maximum time (seconds) to hold a single gesture during password input.\nIf held longer, the input resets.\nPrevents holding a gesture while thinking about the next step." },
                    { SystemLanguage.ChineseSimplified, "密码输入过程中保持单个手势的最大时间（秒）。\n超过此时间输入将重置。\n防止一直保持手势去思考下一步手势。" },
                    { SystemLanguage.Japanese, "パスワード入力中に単一ジェスチャーを保持する最大時間（秒）。\n超えると入力がリセットされます。\n次のステップを考えながらジェスチャーを保持し続けるのを防ぎます。" }
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
                    { SystemLanguage.English, "Password is empty (0 digits).\nASS is disabled and will not be generated." },
                    { SystemLanguage.ChineseSimplified, "密码为空（0位）。\nASS 已禁用，不会生成保护系统。" },
                    { SystemLanguage.Japanese, "パスワードが空です（0桁）。\nASSは無効化され、保護システムは生成されません。" }
                },
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
                ["defense.options"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Defense Options" },
                    { SystemLanguage.ChineseSimplified, "防御选项" },
                    { SystemLanguage.Japanese, "防御オプション" }
                },
                ["defense.desc_gpu"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "GPU Defense (current behavior):\n- Particles: minimal system-count strategy with shared UB_Defense material\n- Lights: reuse 1 external light or create 1 fallback light\n- Lightweight Mode: never creates new lights, but reuses an existing avatar light if one already exists; particle Collision / Trails stay off\n- PhysX / Cloth / dedicated ShaderDefense meshes: disabled" },
                    { SystemLanguage.ChineseSimplified, "GPU 防御（当前实现）：\n- 粒子：最少系统数策略，共享 UB_Defense 材质\n- 光源：优先复用 1 个外部 Light，没有则补 1 个后备 Light\n- 轻量模式：不主动生成光源；如果 Avatar 本来就有光源则直接复用；粒子碰撞 / 拖尾保持关闭\n- PhysX / Cloth / 独立 ShaderDefense 网格：默认关闭" },
                    { SystemLanguage.Japanese, "GPU防御（現在の実装）：\n- パーティクル：最小システム数戦略 + 共有 UB_Defense マテリアル\n- ライト：外部 Light を 1 つ再利用、なければ 1 つだけ生成\n- 軽量モード：新しいライトは生成しないが、既存のアバター Light があれば再利用する。Particle Collision / Trails は無効のまま\n- PhysX / Cloth / 専用 ShaderDefense メッシュ：無効" }
                },
                ["defense.note"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Defense mechanisms are only generated during VRChat Build & Publish.\nThey do NOT affect Edit Mode or Play Mode." },
                    { SystemLanguage.ChineseSimplified, "防御机制仅在 VRChat 构建/上传时生成。\n编辑模式和 Play 模式不受任何影响。" },
                    { SystemLanguage.Japanese, "防御メカニズムはVRChatビルド/アップロード時のみ生成されます。\n編集モードやプレイモードには影響しません。" }
                },
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
                    { SystemLanguage.English, "Enable ASS generation in Play Mode.\nDefense components use minimal parameters (1 each) for quick testing." },
                    { SystemLanguage.ChineseSimplified, "在播放模式下启用 ASS 生成。\n防御组件会使用最小参数（各 1 个）以快速测试。" },
                    { SystemLanguage.Japanese, "プレイモードでASS生成を有効化。\n防御コンポーネントは最小パラメータ（各 1 個）で素早くテストできます。" }
                },
                ["defense.disable_defense"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Disable Defense" },
                    { SystemLanguage.ChineseSimplified, "禁用防御" },
                    { SystemLanguage.Japanese, "防御を無効化" }
                },
                ["defense.disable_defense_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Do not generate defense mechanisms (only test the password system).\nDisabled while 'Default Enable Defense' is active." },
                    { SystemLanguage.ChineseSimplified, "不生成防御机制（仅测试密码系统）。\n启用「默认启用防御」时此选项不可用。" },
                    { SystemLanguage.Japanese, "防御メカニズムを生成しない（パスワードシステムのみテスト）。\n「デフォルトで防御を有効化」が有効な場合は使用できません。" }
                },
                ["defense.disable_defense_locked"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Locked while 'Default Enable Defense' is enabled" },
                    { SystemLanguage.ChineseSimplified, "已锁定：当前启用了「默认启用防御」" },
                    { SystemLanguage.Japanese, "ロック中：「デフォルトで防御を有効化」が有効です" }
                },
                ["advanced.disable_overlay"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Disable Overlay Interface" },
                    { SystemLanguage.ChineseSimplified, "关闭覆盖界面" },
                    { SystemLanguage.Japanese, "オーバーレイ表示を無効化" }
                },
                ["advanced.disable_overlay_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Do not generate the fullscreen overlay\n(white background + progress bar + logo).\nAudio feedback is still generated." },
                    { SystemLanguage.ChineseSimplified, "不生成全屏覆盖\n（白色背景 + 进度条 + 徽标）。\n音频反馈仍然会生成。" },
                    { SystemLanguage.Japanese, "フルスクリーンオーバーレイを生成しない\n（白背景+プログレスバー+ロゴ）。\nオーディオフィードバックは引き続き生成されます。" }
                },
                ["advanced.mute_warning"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Disable Warning Sound" },
                    { SystemLanguage.ChineseSimplified, "关闭警告音效" },
                    { SystemLanguage.Japanese, "警告音を無効化" }
                },
                ["advanced.mute_warning_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Do not generate the countdown warning beep.\nThe fullscreen overlay (mask + progress bar) is still shown.\nUse with 'Disable Overlay Interface' to completely silence the system." },
                    { SystemLanguage.ChineseSimplified, "不生成倒计时警告音效。\n全屏覆盖（遮罩 + 进度条）仍然显示。\n与「关闭覆盖界面」配合可完全关闭系统的所有反馈。" },
                    { SystemLanguage.Japanese, "カウントダウン警告音を生成しない。\nフルスクリーンオーバーレイ（マスク+プログレスバー）は引き続き表示されます。\n「オーバーレイ表示を無効化」と組み合わせて完全に沈黙させることができます。" }
                },
                ["advanced.default_defense"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Default Enable Defense" },
                    { SystemLanguage.ChineseSimplified, "默认启用防御" },
                    { SystemLanguage.Japanese, "デフォルトで防御を有効化" }
                },
                ["advanced.default_defense_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Defense is always ON. No password input.\n\nUse after uploading and unlocking a\npassword-protected version in VRChat." },
                    { SystemLanguage.ChineseSimplified, "防御始终开启，无需密码输入。\n\n请先在 VRChat 中上传并解锁\n带密码保护的版本后再使用。" },
                    { SystemLanguage.Japanese, "防御が常に有効。パスワード入力不要。\n\nVRChatでパスワード保護版をアップロード\nしてロック解除した後で使用してください。" }
                },
                ["advanced.default_defense_warning"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "DANGER: No password system will be generated.\n\nWorkflow:\n1. Upload a password-protected version first\n2. Unlock it in VRChat\n3. Re-upload with this option enabled\n\nSkipping step 2 will trigger defense for ALL users\nincluding yourself!" },
                    { SystemLanguage.ChineseSimplified, "危险：启用后不会生成密码系统。\n\n正确流程：\n1. 先上传带密码保护的版本\n2. 在 VRChat 中解锁\n3. 再上传启用此选项的版本\n\n跳过第 2 步会导致防御对所有人生效\n（包括你自己）！" },
                    { SystemLanguage.Japanese, "危険：パスワードシステムは生成されません。\n\nワークフロー：\n1. パスワード保護版をアップロード\n2. VRChatでロック解除\n3. このオプションを有効にして再アップロード\n\n手順2をスキップすると自分を含む\n全ユーザーに防御が作動します！" }
                },
                ["defense.enable_overflow"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Overflow Mode" },
                    { SystemLanguage.ChineseSimplified, "溢出模式" },
                    { SystemLanguage.Japanese, "オーバーフローモード" }
                },
                ["defense.enable_overflow_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Overflow mode.\n\nDuring Build & Publish, particle / mesh counts target int.MaxValue+1 and intentionally overflow VRChat stats to -2147483648.\n\nRecommended: enable this together with Lightweight Mode so particle-related stats are more likely to stay green. In Lightweight Mode, existing avatar lights may still be reused, but no new lights will be generated.\n\nPlay Mode always uses minimal defense regardless of this setting." },
                    { SystemLanguage.ChineseSimplified, "溢出模式。\n\n构建 / 上传时，粒子数与 Mesh 面数会以 int.MaxValue+1 为目标，故意让 VRChat 统计溢出到 -2147483648。\n\n建议与「轻量模式」一起开启，这样更容易让粒子相关参数保持绿色。轻量模式下如果 Avatar 本来就有光源仍会复用，但不会主动生成新光源。\n\nPlay Mode 始终使用最小防御，不受此选项影响。" },
                    { SystemLanguage.Japanese, "オーバーフローモード。\n\nビルド / アップロード時、パーティクル数とメッシュポリゴン数を int.MaxValue+1 目標で生成し、VRChat の統計を -2147483648 へ意図的にオーバーフローさせます。\n\n推奨：軽量モードと併用すると、粒子関連ステータスをグリーン表示に保ちやすくなります。軽量モードでは既存のアバター Light は再利用される場合がありますが、新しいライトは生成しません。\n\nPlay Mode では常に最小防御となり、この設定は無視されます。" }
                },
                ["defense.lightweight_defense"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Lightweight Mode" },
                    { SystemLanguage.ChineseSimplified, "轻量模式" },
                    { SystemLanguage.Japanese, "軽量モード" }
                },
                ["defense.lightweight_defense_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Green-oriented mode.\n\n• Minimizes newly generated particle systems\n• Never creates new lights; if the avatar already has a light, it may be reused directly\n• Disables particle Collision and Trails\n• Overflow ON: if the avatar already has particles, add 1 full particle system; otherwise add 2 full systems to preserve overflow\n\nRecommended: enable Overflow Mode together so particle-related stats are more likely to stay green via overflow." },
                    { SystemLanguage.ChineseSimplified, "偏向绿模的模式。\n\n• 尽量减少新生成的粒子系统\n• 不主动生成光源；如果 Avatar 已经有光源，则直接复用\n• 禁用粒子碰撞与拖尾\n• 开启溢出时：如果 Avatar 已经有粒子，则只补 1 个顶满上限的粒子系统；否则生成 2 个顶满上限的粒子系统以保持溢出\n\n建议与「溢出模式」同时开启，以通过数值溢出让粒子相关参数更容易保持绿色。" },
                    { SystemLanguage.Japanese, "できるだけグリーン評価を維持するモードです。\n\n• 新規生成する ParticleSystem を最小限に抑える\n• 新しいライトは生成しないが、アバターに既存の Light があればそのまま再利用する\n• Particle Collision と Trails を無効化する\n• Overflow ON：既存粒子がある場合は上限まで埋めた 1 システム、ない場合は Overflow 維持のため 2 システムを生成\n\n推奨：Overflow Mode も同時に有効化すると、オーバーフローにより粒子関連ステータスをグリーン表示に保ちやすくなります。" }
                },
                ["defense.lightweight_defense_note"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Lightweight Mode summary:\n• Goal: stay as close to green as possible while keeping defense active\n• Never creates new lights; existing avatar lights may still be reused\n• Disables particle Collision and Trails to reduce extra load\n• Overflow ON: add 1 full system if the avatar already has particles, otherwise add 2 full systems\n• Overflow OFF: use 1 system and fill the remaining particle / mesh budget as tightly as possible without exceeding the cap" },
                    { SystemLanguage.ChineseSimplified, "轻量模式摘要：\n• 目标是在保留防御效果的前提下，尽量贴近绿模\n• 不主动生成光源；若 Avatar 本来就有光源，则允许直接复用\n• 关闭粒子碰撞与拖尾，减少额外负担\n• 溢出开启：如果 Avatar 已有粒子，则补 1 个顶满系统；否则补 2 个顶满系统\n• 溢出关闭：只用 1 个系统，尽量把剩余粒子 / 面数预算补满，但不主动超限" },
                    { SystemLanguage.Japanese, "軽量モード概要：\n• 防御を維持しつつ、できるだけグリーン評価に近づけることが目的\n• 新しいライトは生成しないが、アバターに既存の Light があれば再利用する\n• Particle Collision と Trails を無効化し、追加負荷を抑える\n• Overflow ON：既存粒子がある場合は 1 つ、ない場合は 2 つのフルシステムを追加\n• Overflow OFF：1 システムのみで残りの粒子数 / ポリゴン予算を、上限を超えない範囲で可能な限り埋めます" }
                },
                ["defense.lightweight_defense_overflow_recommendation"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Recommendation: enable Overflow Mode together with Lightweight Mode. Lightweight Mode does not create new lights, so it relies mainly on particle-count / mesh-count overflow to keep particle-related stats green. If the avatar already has lights they may still be reused, but if Overflow stays off, system count only stays minimal while the stats continue to accumulate against the normal cap." },
                    { SystemLanguage.ChineseSimplified, "建议：将「溢出模式」与「轻量模式」一起开启。轻量模式不会主动生成新光源，因此主要依赖粒子数 / 面数的溢出来让粒子相关参数保持绿色。若 Avatar 本来就有光源仍可能被复用，但如果不启用溢出，就只是把系统数量压到最少，相关参数仍会按正常上限累计。" },
                    { SystemLanguage.Japanese, "推奨：「軽量モード」と一緒に「Overflow Mode」も有効化してください。軽量モードでは新しいライトを生成しないため、粒子数 / ポリゴン数のオーバーフローによって粒子関連ステータスをグリーンに保つ比重が高くなります。既存のアバター Light は再利用される場合がありますが、Overflow を無効のままにすると、システム数が最小化されるだけで、ステータスは通常上限に対して加算され続けます。" }
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
                    { SystemLanguage.English, "Hide all root-level child objects (the avatar itself) when locked.\nLeave enabled to completely conceal the avatar from the thief." },
                    { SystemLanguage.ChineseSimplified, "锁定时隐藏所有根级子对象（Avatar 本身）。\n建议保持开启，让盗模者完全看不到模型。" },
                    { SystemLanguage.Japanese, "ロック時にすべてのルートレベル子オブジェクト（アバター本体）を非表示にします。\n有効にしておくと、盗んだ側からモデルが見えなくなります。" }
                },
                ["advanced.wd_mode"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Write Defaults Mode" },
                    { SystemLanguage.ChineseSimplified, "Write Defaults 模式" },
                    { SystemLanguage.Japanese, "Write Defaults モード" }
                },
                ["advanced.wd_mode_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Animation Write Defaults mode for ASS-generated animations:\n• Auto = Auto-detect from existing FX layers (recommended)\n• On  = Auto-restore with Write Defaults\n• Off = Explicit restore without Write Defaults" },
                    { SystemLanguage.ChineseSimplified, "ASS 生成动画的 Write Defaults 模式：\n• Auto = 从已有 FX 层自动检测（推荐）\n• On  = 使用 Write Defaults 自动恢复\n• Off = 不使用 Write Defaults，显式恢复" },
                    { SystemLanguage.Japanese, "ASS生成アニメーションの Write Defaults モード：\n• Auto = 既存FXレイヤーから自動検出（推奨）\n• On  = Write Defaults で自動復元\n• Off = Write Defaults なしで明示的復元" }
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
                ["advanced.obfuscation_section"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Obfuscation" },
                    { SystemLanguage.ChineseSimplified, "混淆" },
                    { SystemLanguage.Japanese, "難読化" }
                },
                ["advanced.disable_obfuscation"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Disable Obfuscation" },
                    { SystemLanguage.ChineseSimplified, "禁用混淆" },
                    { SystemLanguage.Japanese, "難読化を無効化" }
                },
                ["advanced.disable_obfuscation_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Use original parameter/layer/object names in the build.\nOnly recommended for debugging." },
                    { SystemLanguage.ChineseSimplified, "在构建中使用原始参数/层/对象名称。\n仅建议用于调试。" },
                    { SystemLanguage.Japanese, "ビルドで元のパラメータ/レイヤー/オブジェクト名を使用。\nデバッグ時のみ推奨。" }
                },
                ["advanced.obfuscate_playable_layers"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Obfuscate All Playable Layers" },
                    { SystemLanguage.ChineseSimplified, "混淆全部 Playable Layer" },
                    { SystemLanguage.Japanese, "すべてのPlayable Layerを難読化" }
                },
                ["advanced.obfuscate_playable_layers_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "During build, duplicate all custom playable controllers used by the avatar and obfuscate their layer/state/state machine/blend tree/clip names.\nOriginal project assets stay unchanged. Disabled by default to preserve existing workflow." },
                    { SystemLanguage.ChineseSimplified, "构建时复制 Avatar 使用的所有自定义 Playable Controller，并混淆其中的层 / 状态 / 状态机 / BlendTree / Clip 名称。\n原始项目资源不会被直接修改。默认关闭，以保持现有工作流不变。" },
                    { SystemLanguage.Japanese, "ビルド時にアバターが使用するすべてのカスタムPlayable Controllerを複製し、その中のレイヤー / ステート / ステートマシン / BlendTree / Clip 名を難読化します。\n元のプロジェクト資産は直接変更されません。既存ワークフローを維持するため、既定では無効です。" }
                },
                ["advanced.decoy_layers"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Animation Layer Noise" },
                    { SystemLanguage.ChineseSimplified, "动画层噪声" },
                    { SystemLanguage.Japanese, "アニメーションレイヤーノイズ" }
                },
                ["advanced.decoy_layers_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Add an extra animation layer to the FX controller.\nDoes not affect avatar appearance or performance." },
                    { SystemLanguage.ChineseSimplified, "向 FX 控制器添加一个额外的动画层。\n不会影响 Avatar 外观或性能。" },
                    { SystemLanguage.Japanese, "FXコントローラーに追加のアニメーションレイヤーを追加。\nアバターの外観やパフォーマンスに影響しません。" }
                },
                ["advanced.decoy_states"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Animation State Noise" },
                    { SystemLanguage.ChineseSimplified, "动画状态噪声" },
                    { SystemLanguage.Japanese, "アニメーションステートノイズ" }
                },
                ["advanced.decoy_states_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Add extra animation states to existing layers.\nDoes not affect avatar appearance or performance." },
                    { SystemLanguage.ChineseSimplified, "向现有动画层添加额外的动画状态。\n不会影响 Avatar 外观或性能。" },
                    { SystemLanguage.Japanese, "既存のレイヤーに追加のアニメーションステートを追加。\nアバターの外観やパフォーマンスに影響しません。" }
                },
                ["advanced.no_feedback_warning"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Overlay and warning sound are both disabled.\nNo visual/audio cue during password input.\n(Password success sound still plays on unlock.)\nCountdown will still trigger defense when expired." },
                    { SystemLanguage.ChineseSimplified, "已关闭覆盖界面和警告音效。\n输入密码时将没有视觉和声音提示。\n（密码正确解锁时仍会播放提示音。）\n倒计时结束后仍会触发防御。" },
                    { SystemLanguage.Japanese, "オーバーレイと警告音が両方無効です。\nパスワード入力中の手がかりがありません。\n（解除時の成功音は再生されます。）\nカウントダウン後も防御は作動します。" }
                },
            };
        }
    }
}
