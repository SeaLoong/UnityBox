using UnityEngine;
using System.Collections.Generic;

namespace SeaLoongUnityBox.AvatarSecuritySystem.Editor
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

                // ========== 警告信息 ==========
                ["warning.main"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "⚠️ WARNING ⚠️\n\nThis system is for protecting your Avatar from malicious theft. Please ensure:\n1. You own the legal rights to this Avatar\n2. You understand the performance impact of defense mechanisms\n3. You comply with VRChat Terms of Service and relevant laws\n\nBy using this system, you agree to take all responsibility." },
                    { SystemLanguage.ChineseSimplified, "⚠️ 警告 ⚠️\n\n此系统仅用于保护您的 Avatar 免受恶意盗取。请确保：\n1. 您拥有此 Avatar 的合法权利\n2. 理解防御机制可能影响性能\n3. 遵守 VRChat 服务条款和相关法律\n\n使用此系统即表示您同意承担所有责任。" },
                    { SystemLanguage.Japanese, "⚠️ 警告 ⚠️\n\nこのシステムは、悪意のある盗難からアバターを保護するためのものです。以下を確認してください：\n1. このアバターの合法的な権利を所有していること\n2. 防御メカニズムがパフォーマンスに影響を与える可能性があることを理解していること\n3. VRChatの利用規約と関連法を遵守していること\n\nこのシステムを使用することで、すべての責任を負うことに同意したものとします。" }
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
                ["countdown.urgent_threshold"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Urgent Threshold (sec)" },
                    { SystemLanguage.ChineseSimplified, "紧急阈值 (秒)" },
                    { SystemLanguage.Japanese, "緊急しきい値 (秒)" }
                },

                // ========== 反馈配置 ==========
                ["feedback.config"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Feedback Configuration" },
                    { SystemLanguage.ChineseSimplified, "反馈配置" },
                    { SystemLanguage.Japanese, "フィードバック設定" }
                },
                ["feedback.error_sound"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Error Sound" },
                    { SystemLanguage.ChineseSimplified, "错误音效" },
                    { SystemLanguage.Japanese, "エラーサウンド" }
                },
                ["feedback.warning_beep"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Warning Beep" },
                    { SystemLanguage.ChineseSimplified, "警告哔哔声" },
                    { SystemLanguage.Japanese, "警告ビープ音" }
                },
                ["feedback.success_sound"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Success Sound" },
                    { SystemLanguage.ChineseSimplified, "成功音效" },
                    { SystemLanguage.Japanese, "成功サウンド" }
                },
                ["feedback.particle_effects"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Enable Particle Effects" },
                    { SystemLanguage.ChineseSimplified, "启用粒子特效" },
                    { SystemLanguage.Japanese, "パーティクルエフェクトを有効化" }
                },
                ["feedback.asset_specs"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "See ASS_RequiredAssets.md for asset specifications" },
                    { SystemLanguage.ChineseSimplified, "查看 ASS_RequiredAssets.md 了解素材规格" },
                    { SystemLanguage.Japanese, "アセット仕様については ASS_RequiredAssets.md を参照" }
                },
                ["feedback.use_default"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "🔊 Use Default Audio" },
                    { SystemLanguage.ChineseSimplified, "🔊 使用默认音效" },
                    { SystemLanguage.Japanese, "🔊 デフォルトオーディオを使用" }
                },

                // ========== 防御配置 ==========
                ["defense.config"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Defense Configuration" },
                    { SystemLanguage.ChineseSimplified, "防御配置" },
                    { SystemLanguage.Japanese, "防御設定" }
                },
                ["defense.enhancement"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Defense Enhancement" },
                    { SystemLanguage.ChineseSimplified, "防御增强" },
                    { SystemLanguage.Japanese, "防御強化" }
                },
                ["defense.particle_count"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Particle System Count" },
                    { SystemLanguage.ChineseSimplified, "粒子系统数量" },
                    { SystemLanguage.Japanese, "パーティクルシステム数" }
                },
                ["defense.particle_count_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Number of particle systems to generate (1000 particles each)\nIncreases GPU load and frame time" },
                    { SystemLanguage.ChineseSimplified, "生成的粒子系统数量（每个 1000 粒子）\n增加 GPU 负载和帧时间消耗" },
                    { SystemLanguage.Japanese, "生成するパーティクルシステムの数（各1000パーティクル）\nGPU負荷とフレーム時間が増加します" }
                },
                ["defense.material_count"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Extra Material Count" },
                    { SystemLanguage.ChineseSimplified, "额外材质数量" },
                    { SystemLanguage.Japanese, "追加マテリアル数" }
                },
                ["defense.material_count_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Number of extra materials to generate (increase Draw Calls)\n1000 Draw Calls ≈ 2ms" },
                    { SystemLanguage.ChineseSimplified, "额外生成的材质数量（增加 Draw Calls）\n1000 Draw Calls ≈ 2ms" },
                    { SystemLanguage.Japanese, "追加で生成されるマテリアル数（Draw Callsを増やす）\n1000 Draw Calls ≈ 2ms" }
                },
                ["defense.light_count"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Point Light Count" },
                    { SystemLanguage.ChineseSimplified, "点光源数量" },
                    { SystemLanguage.Japanese, "ポイントライト数" }
                },
                ["defense.light_count_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Number of point lights to generate\nIncreases lighting calculation overhead" },
                    { SystemLanguage.ChineseSimplified, "生成的点光源数量\n增加光照计算开销" },
                    { SystemLanguage.Japanese, "生成するポイントライトの数\nライティング計算のオーバーヘッドが増加します" }
                },
                ["defense.cloth_enabled"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Enable Cloth Defense" },
                    { SystemLanguage.ChineseSimplified, "启用 Cloth 防御" },
                    { SystemLanguage.Japanese, "Cloth防御を有効化" }
                },
                ["defense.cloth_enabled_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Enable Cloth component (very performance-intensive)\n0.2 ms per 1000 vertices" },
                    { SystemLanguage.ChineseSimplified, "启用 Cloth 组件（非常消耗性能）\n0.2 ms per 1000 vertices" },
                    { SystemLanguage.Japanese, "Clothコンポーネントを有効化（非常にパフォーマンス集約的）\n0.2 ms per 1000 vertices" }
                },
                ["defense.cloth_vertex_count"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Cloth Vertex Count" },
                    { SystemLanguage.ChineseSimplified, "Cloth 顶点数" },
                    { SystemLanguage.Japanese, "Cloth頂点数" }
                },
                ["defense.cloth_vertex_count_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Total vertex count for Cloth component" },
                    { SystemLanguage.ChineseSimplified, "Cloth 组件的总顶点数" },
                    { SystemLanguage.Japanese, "Clothコンポーネントの総頂点数" }
                },
                ["defense.level"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Defense Level" },
                    { SystemLanguage.ChineseSimplified, "防御等级" },
                    { SystemLanguage.Japanese, "防御レベル" }
                },
                ["defense.level_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Timeout triggers defense strength (0=minimal, 4=maximum)\n0: Basic file protection\n1: CPU methods only\n2: CPU+GPU basic\n3: CPU+GPU balanced\n4: CPU+GPU maximum (default)" },
                    { SystemLanguage.ChineseSimplified, "倒计时结束后触发的防御强度（0=最小，4=最强）\n0: 基础文件保护\n1: 仅CPU防御\n2: CPU+GPU基础\n3: CPU+GPU均衡\n4: CPU+GPU最强（默认）" },
                    { SystemLanguage.Japanese, "タイムアウト後に起動する防御強度（0=最小、4=最大）\n0: 基本ファイル保護\n1: CPU防御のみ\n2: CPU+GPU基本\n3: CPU+GPU均衡\n4: CPU+GPU最大（デフォルト）" }
                },
                ["defense.cpu_methods"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "CPU Defense Methods" },
                    { SystemLanguage.ChineseSimplified, "CPU 防御方法" },
                    { SystemLanguage.Japanese, "CPU防御方式" }
                },
                ["defense.constraint_chain"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Constraint Chain" },
                    { SystemLanguage.ChineseSimplified, "约束链" },
                    { SystemLanguage.Japanese, "制約チェーン" }
                },
                ["defense.constraint_chain_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Enable Constraint Chain defense" },
                    { SystemLanguage.ChineseSimplified, "启用约束链防御" },
                    { SystemLanguage.Japanese, "制約チェーン防御を有効化" }
                },
                ["defense.constraint_depth"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Constraint Depth" },
                    { SystemLanguage.ChineseSimplified, "约束深度" },
                    { SystemLanguage.Japanese, "制約の深さ" }
                },
                ["defense.constraint_depth_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Chain depth (10-100)" },
                    { SystemLanguage.ChineseSimplified, "链深度（10-100）" },
                    { SystemLanguage.Japanese, "チェーンの深さ（10-100）" }
                },
                ["defense.phys_bone"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "PhysBone Defense" },
                    { SystemLanguage.ChineseSimplified, "PhysBone 防御" },
                    { SystemLanguage.Japanese, "PhysBone防御" }
                },
                ["defense.phys_bone_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Enable PhysBone defense" },
                    { SystemLanguage.ChineseSimplified, "启用 PhysBone 防御" },
                    { SystemLanguage.Japanese, "PhysBone防御を有効化" }
                },
                ["defense.phys_bone_length"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "PhysBone Chain Length" },
                    { SystemLanguage.ChineseSimplified, "PhysBone 链长度" },
                    { SystemLanguage.Japanese, "PhysBoneチェーン長" }
                },
                ["defense.phys_bone_length_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Chain length (5-50)" },
                    { SystemLanguage.ChineseSimplified, "链长度（5-50）" },
                    { SystemLanguage.Japanese, "チェーン長（5-50）" }
                },
                ["defense.phys_bone_colliders"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "PhysBone Colliders" },
                    { SystemLanguage.ChineseSimplified, "PhysBone 碰撞体数量" },
                    { SystemLanguage.Japanese, "PhysBoneコライダー数" }
                },
                ["defense.phys_bone_colliders_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Collider count (0-100)" },
                    { SystemLanguage.ChineseSimplified, "碰撞体数量（0-100）" },
                    { SystemLanguage.Japanese, "コライダー数（0-100）" }
                },
                ["defense.contact_system"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Contact System" },
                    { SystemLanguage.ChineseSimplified, "接触系统防御" },
                    { SystemLanguage.Japanese, "接触システム防御" }
                },
                ["defense.contact_system_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Enable Contact System defense" },
                    { SystemLanguage.ChineseSimplified, "启用接触系统防御" },
                    { SystemLanguage.Japanese, "接触システム防御を有効化" }
                },
                ["defense.contact_count"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Contact Count" },
                    { SystemLanguage.ChineseSimplified, "接触组件数量" },
                    { SystemLanguage.Japanese, "接触コンポーネント数" }
                },
                ["defense.contact_count_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Number of contact components (10-200)" },
                    { SystemLanguage.ChineseSimplified, "接触组件数量（10-200）" },
                    { SystemLanguage.Japanese, "接触コンポーネント数（10-200）" }
                },
                ["defense.gpu_methods"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "GPU Defense Methods" },
                    { SystemLanguage.ChineseSimplified, "GPU 防御方法" },
                    { SystemLanguage.Japanese, "GPU防御方式" }
                },
                ["defense.use_custom"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Use Custom Defense Settings" },
                    { SystemLanguage.ChineseSimplified, "使用自定义防御设置" },
                    { SystemLanguage.Japanese, "カスタム防御設定を使用" }
                },
                ["defense.use_custom_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Enable to manually configure all defense parameters (Defense Level will be ignored)" },
                    { SystemLanguage.ChineseSimplified, "启用后可手动配置所有防御参数（防御等级将失效）" },
                    { SystemLanguage.Japanese, "有効にするとすべての防御パラメータを手動設定できます（防御レベルは無視されます）" }
                },
                ["defense.custom_mode_hint"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Custom mode enabled. Configure each defense method individually. Defense Level is ignored." },
                    { SystemLanguage.ChineseSimplified, "已启用自定义模式。请单独配置每个防御方法。防御等级将被忽略。" },
                    { SystemLanguage.Japanese, "カスタムモードが有効です。各防御方法を個別に設定してください。防御レベルは無視されます。" }
                },
                ["defense.level_0_desc"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Level 0: Password system only (no defense components)" },
                    { SystemLanguage.ChineseSimplified, "等级 0：仅密码系统（不生成防御组件）" },
                    { SystemLanguage.Japanese, "レベル0：パスワードシステムのみ（防御コンポーネントなし）" }
                },
                ["defense.level_1_desc"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Level 1: Password + CPU defense (Constraint Chain, PhysBone, Contact - MAX parameters)" },
                    { SystemLanguage.ChineseSimplified, "等级 1：密码 + CPU 防御（约束链、PhysBone、Contact - 最高参数）" },
                    { SystemLanguage.Japanese, "レベル1：パスワード+CPU防御（制約チェーン、PhysBone、コンタクト - 最大パラメータ）" }
                },
                ["defense.level_2_desc"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Level 2: Password + CPU defense (MAX) + GPU defense (medium-low parameters)" },
                    { SystemLanguage.ChineseSimplified, "等级 2：密码 + CPU 防御（最高）+ GPU 防御（中低参数）" },
                    { SystemLanguage.Japanese, "レベル2：パスワード+CPU防御（最大）+GPU防御（中低パラメータ）" }
                },
                ["defense.level_3_desc"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Level 3: Password + CPU defense (MAX) + GPU defense (MAX parameters)" },
                    { SystemLanguage.ChineseSimplified, "等级 3：密码 + CPU 防御（最高）+ GPU 防御（最高参数）" },
                    { SystemLanguage.Japanese, "レベル3：パスワード+CPU防御（最大）+GPU防御（最大パラメータ）" }
                },
                ["defense.heavy_shader"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Heavy Shader" },
                    { SystemLanguage.ChineseSimplified, "重型 Shader" },
                    { SystemLanguage.Japanese, "ヘビーシェーダー" }
                },
                ["defense.heavy_shader_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Enable heavy shader defense" },
                    { SystemLanguage.ChineseSimplified, "启用重型 Shader 防御" },
                    { SystemLanguage.Japanese, "ヘビーシェーダー防御を有効化" }
                },
                ["defense.shader_loops"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Shader Loop Count" },
                    { SystemLanguage.ChineseSimplified, "Shader 循环数量" },
                    { SystemLanguage.Japanese, "シェーダーループ数" }
                },
                ["defense.shader_loops_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Number of shader loops (0-200)" },
                    { SystemLanguage.ChineseSimplified, "Shader 循环数量（0-200）" },
                    { SystemLanguage.Japanese, "シェーダーループ数（0-200）" }
                },
                ["defense.overdraw"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Overdraw Layers" },
                    { SystemLanguage.ChineseSimplified, "过度绘制层" },
                    { SystemLanguage.Japanese, "オーバードロウレイヤー" }
                },
                ["defense.overdraw_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Enable overdraw layers defense" },
                    { SystemLanguage.ChineseSimplified, "启用过度绘制层防御" },
                    { SystemLanguage.Japanese, "オーバードロウレイヤー防御を有効化" }
                },
                ["defense.overdraw_layers"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Overdraw Layer Count" },
                    { SystemLanguage.ChineseSimplified, "过度绘制层数量" },
                    { SystemLanguage.Japanese, "オーバードロウレイヤー数" }
                },
                ["defense.overdraw_layers_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Layer count (5-50)" },
                    { SystemLanguage.ChineseSimplified, "层数量（5-50）" },
                    { SystemLanguage.Japanese, "レイヤー数（5-50）" }
                },
                ["defense.high_poly"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "High Poly Mesh" },
                    { SystemLanguage.ChineseSimplified, "高多边形网格" },
                    { SystemLanguage.Japanese, "高ポリゴンメッシュ" }
                },
                ["defense.high_poly_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Enable high poly mesh defense" },
                    { SystemLanguage.ChineseSimplified, "启用高多边形网格防御" },
                    { SystemLanguage.Japanese, "高ポリゴンメッシュ防御を有効化" }
                },
                ["defense.high_poly_vertices"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "High Poly Vertex Count" },
                    { SystemLanguage.ChineseSimplified, "高多边形顶点数量" },
                    { SystemLanguage.Japanese, "高ポリゴン頂点数" }
                },
                ["defense.high_poly_vertices_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Vertex count (10k-200k)" },
                    { SystemLanguage.ChineseSimplified, "顶点数量（10k-200k）" },
                    { SystemLanguage.Japanese, "頂点数（10k-200k）" }
                },
                ["defense.enable_cpu_defense_desc"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Enable CPU Defense (Constraint, PhysBone, Contact)" },
                    { SystemLanguage.ChineseSimplified, "启用CPU防御（Constraint、PhysBone、Contact）" },
                    { SystemLanguage.Japanese, "CPU防御を有効化（Constraint、PhysBone、Contact）" }
                },
                ["defense.enable_cpu_defense"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Enable CPU Defense" },
                    { SystemLanguage.ChineseSimplified, "启用CPU防御" },
                    { SystemLanguage.Japanese, "CPU防御を有効化" }
                },
                ["defense.enable_gpu_defense_desc"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Enable GPU Defense (Shader, Overdraw, HighPoly)" },
                    { SystemLanguage.ChineseSimplified, "启用GPU防御（Shader、Overdraw、高多边形）" },
                    { SystemLanguage.Japanese, "GPU防御を有効化（Shader、Overdraw、ハイポリゴン）" }
                },
                ["defense.enable_gpu_defense"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Enable GPU Defense" },
                    { SystemLanguage.ChineseSimplified, "启用GPU防御" },
                    { SystemLanguage.Japanese, "GPU防御を有効化" }
                },
                ["defense.constraint_chain_desc"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Enable Constraint chain consumption" },
                    { SystemLanguage.ChineseSimplified, "启用Constraint链式消耗" },
                    { SystemLanguage.Japanese, "Constraint連鎖消費を有効化" }
                },
                ["defense.constraint_depth_desc"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Constraint chain depth (fixed at 5 in debug mode)" },
                    { SystemLanguage.ChineseSimplified, "Constraint链深度（调试模式下固定为5）" },
                    { SystemLanguage.Japanese, "制約チェーン深度（デバッグモードでは5に固定）" }
                },
                ["defense.phys_bone_desc"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Enable PhysBone physics consumption" },
                    { SystemLanguage.ChineseSimplified, "启用PhysBone物理骨骼消耗" },
                    { SystemLanguage.Japanese, "PhysBone物理骨格消費を有効化" }
                },
                ["defense.phys_bone_length_desc"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "PhysBone chain length (fixed at 3 in debug mode)" },
                    { SystemLanguage.ChineseSimplified, "PhysBone链长度（调试模式下固定为3）" },
                    { SystemLanguage.Japanese, "PhysBoneチェーン長（デバッグモードでは3に固定）" }
                },
                ["defense.phys_bone_colliders_desc"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "PhysBone Collider count (fixed at 2 in debug mode)" },
                    { SystemLanguage.ChineseSimplified, "PhysBone Collider数量（调试模式下固定为2）" },
                    { SystemLanguage.Japanese, "PhysBoneコライダー数（デバッグモードでは2に固定）" }
                },
                ["defense.contact_system_desc"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Enable Contact component consumption" },
                    { SystemLanguage.ChineseSimplified, "启用Contact组件消耗" },
                    { SystemLanguage.Japanese, "Contactコンポーネント消費を有効化" }
                },
                ["defense.contact_count_desc"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Contact Sender/Receiver count (fixed at 4 in debug mode)" },
                    { SystemLanguage.ChineseSimplified, "Contact Sender/Receiver数量（调试模式下固定为4）" },
                    { SystemLanguage.Japanese, "Contact Sender/Receiver数（デバッグモードでは4に固定）" }
                },
                ["defense.heavy_shader_desc"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Enable complex Shader consumption" },
                    { SystemLanguage.ChineseSimplified, "启用复杂Shader消耗" },
                    { SystemLanguage.Japanese, "複雑なシェーダー消費を有効化" }
                },
                ["defense.heavy_shader_explanation"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "\"Heavy Shader\" means a shader with complex calculations that increases GPU workload:\n• Multiple texture samples\n• Floating-point math operations\n• Loops in fragment/vertex shader\n• Multiple render passes\n\nThis doesn't directly protect against model theft, but makes the avatar perform very poorly for anyone wearing it without the correct password, discouraging use." },
                    { SystemLanguage.ChineseSimplified, "\"重型Shader\"是指拥有复杂计算的着色器，会增加GPU工作量：\n• 多次纹理采样\n• 浮点数学运算\n• 在片元/顶点着色器中循环\n• 多个渲染通道\n\n这并不能直接防止模型被盗，但会让没有输入正确密码的人穿着该Avatar时性能极差，从而起到威慑作用。" },
                    { SystemLanguage.Japanese, "\"重いシェーダー\"は複雑な計算を持つシェーダーで、GPUワークロードを増加させます：\n• 複数のテクスチャサンプリング\n• 浮動小数点演算\n• フラグメント/頂点シェーダー内のループ\n• 複数のレンダリングパス\n\nこれはモデルの盗難を直接防ぐものではありませんが、正しいパスワードなしで着用した人のアバターのパフォーマンスを大幅に低下させ、使用を阻止します。" }
                },
                ["defense.shader_loops_desc"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Shader loop count (fixed at 0, disabled in debug mode)" },
                    { SystemLanguage.ChineseSimplified, "Shader循环次数（调试模式下固定为0，不启用）" },
                    { SystemLanguage.Japanese, "シェーダーループ数（デバッグモードでは0に固定、無効）" }
                },
                ["defense.overdraw_desc"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Enable Overdraw layer stacking" },
                    { SystemLanguage.ChineseSimplified, "启用Overdraw层堆叠" },
                    { SystemLanguage.Japanese, "Overdrawレイヤースタッキングを有効化" }
                },
                ["defense.overdraw_layers_desc"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Overdraw transparency layer count (fixed at 3 in debug mode)" },
                    { SystemLanguage.ChineseSimplified, "Overdraw透明层数量（调试模式下固定为3）" },
                    { SystemLanguage.Japanese, "オーバードロウ透明レイヤー数（デバッグモードでは3に固定）" }
                },
                ["defense.high_poly_desc"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Enable high polygon mesh" },
                    { SystemLanguage.ChineseSimplified, "启用高面数Mesh" },
                    { SystemLanguage.Japanese, "高ポリゴンメッシュを有効化" }
                },
                ["defense.high_poly_vertices_desc"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "High-poly mesh vertex count (fixed at 1000 in debug mode)" },
                    { SystemLanguage.ChineseSimplified, "高面数Mesh顶点数（调试模式下固定为1000）" },
                    { SystemLanguage.Japanese, "高ポリゴンメッシュ頂点数（デバッグモードでは1000に固定）" }
                },
                ["defense.state_count"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "State Count" },
                    { SystemLanguage.ChineseSimplified, "状态数量" },
                    { SystemLanguage.Japanese, "状態数" }
                },
                ["defense.state_count_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Higher count = larger file size and greater impact on thieves" },
                    { SystemLanguage.ChineseSimplified, "数量越多，文件越大，对盗取者的影响越大" },
                    { SystemLanguage.Japanese, "数が多いほどファイルサイズが大きくなり、盗難者への影響が大きくなる" }
                },
                ["defense.hide_avatar"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Hide Avatar on Defense" },
                    { SystemLanguage.ChineseSimplified, "防御时隐藏 Avatar" },
                    { SystemLanguage.Japanese, "防御時にアバターを非表示" }
                },
                ["defense.shader"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "GPU Intensive Shader" },
                    { SystemLanguage.ChineseSimplified, "GPU 密集 Shader" },
                    { SystemLanguage.Japanese, "GPU 集約型シェーダー" }
                },
                ["defense.shader_auto"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "GPU Shader will be auto-generated during build." },
                    { SystemLanguage.ChineseSimplified, "GPU Shader 将在构建时自动生成。" },
                    { SystemLanguage.Japanese, "GPU シェーダーはビルド時に自動生成されます。" }
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
                    { SystemLanguage.ChineseSimplified, "Play 模式测试" },
                    { SystemLanguage.Japanese, "プレイモードで有効化" }
                },
                ["advanced.play_mode_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Keep password system in Play Mode (for testing)" },
                    { SystemLanguage.ChineseSimplified, "在 Play 模式下保留密码系统（用于测试）" },
                    { SystemLanguage.Japanese, "プレイモードでパスワードシステムを保持（テスト用）" }
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
                ["advanced.debug_options"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Debug Options" },
                    { SystemLanguage.ChineseSimplified, "调试选项" },
                    { SystemLanguage.Japanese, "デバッグオプション" }
                },
                ["advanced.debug_advanced"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Advanced Debug Options" },
                    { SystemLanguage.ChineseSimplified, "高级调试选项" },
                    { SystemLanguage.Japanese, "高度なデバッグオプション" }
                },
                ["advanced.verbose_logging"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Verbose Logging" },
                    { SystemLanguage.ChineseSimplified, "详细日志" },
                    { SystemLanguage.Japanese, "詳細ログ" }
                },
                ["advanced.verbose_logging_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Enable detailed logging during build" },
                    { SystemLanguage.ChineseSimplified, "构建时启用详细日志输出" },
                    { SystemLanguage.Japanese, "ビルド時に詳細なログを出力" }
                },
                ["advanced.skip_lock"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Skip Lock System" },
                    { SystemLanguage.ChineseSimplified, "跳过锁定系统" },
                    { SystemLanguage.Japanese, "ロックシステムをスキップ" }
                },
                ["advanced.skip_lock_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Skip lock system generation (for testing)" },
                    { SystemLanguage.ChineseSimplified, "跳过锁定系统生成（用于测试）" },
                    { SystemLanguage.Japanese, "ロックシステムの生成をスキップ（テスト用）" }
                },
                ["advanced.skip_password"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Skip Password System" },
                    { SystemLanguage.ChineseSimplified, "跳过密码系统" },
                    { SystemLanguage.Japanese, "パスワードシステムをスキップ" }
                },
                ["advanced.skip_feedback"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Skip Feedback System" },
                    { SystemLanguage.ChineseSimplified, "跳过反馈系统" },
                    { SystemLanguage.Japanese, "フィードバックシステムをスキップ" }
                },
                ["advanced.skip_feedback_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Skip feedback system generation (UI/Audio)" },
                    { SystemLanguage.ChineseSimplified, "跳过反馈系统生成（UI/音效）" },
                    { SystemLanguage.Japanese, "フィードバックシステムの生成をスキップ（UI/音声）" }
                },
                ["advanced.skip_password_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Skip password system generation (for testing)" },
                    { SystemLanguage.ChineseSimplified, "跳过密码系统生成（用于测试）" },
                    { SystemLanguage.Japanese, "パスワードシステムの生成をスキップ（テスト用）" }
                },
                ["advanced.skip_countdown"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Skip Countdown System" },
                    { SystemLanguage.ChineseSimplified, "跳过倒计时系统" },
                    { SystemLanguage.Japanese, "カウントダウンシステムをスキップ" }
                },
                ["advanced.skip_countdown_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Skip countdown system generation (for testing)" },
                    { SystemLanguage.ChineseSimplified, "跳过倒计时系统生成（用于测试）" },
                    { SystemLanguage.Japanese, "カウントダウンシステムの生成をスキップ（テスト用）" }
                },
                ["advanced.skip_defense"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Skip Defense System" },
                    { SystemLanguage.ChineseSimplified, "跳过防御系统" },
                    { SystemLanguage.Japanese, "防御システムをスキップ" }
                },
                ["advanced.skip_defense_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Skip defense system generation (for testing)" },
                    { SystemLanguage.ChineseSimplified, "跳过防御系统生成（用于测试）" },
                    { SystemLanguage.Japanese, "防御システムの生成をスキップ（テスト用）" }
                },
                ["advanced.validate_build"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Validate After Build" },
                    { SystemLanguage.ChineseSimplified, "构建后验证" },
                    { SystemLanguage.Japanese, "ビルド後に検証" }
                },
                ["advanced.validate_build_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Validate animator controller after build" },
                    { SystemLanguage.ChineseSimplified, "构建后验证动画控制器" },
                    { SystemLanguage.Japanese, "ビルド後にアニメーターコントローラーを検証" }
                },
                ["advanced.lock_options"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Lock Options" },
                    { SystemLanguage.ChineseSimplified, "锁定选项" },
                    { SystemLanguage.Japanese, "ロックオプション" }
                },
                ["advanced.lock_fx_layers"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Lock FX Layers" },
                    { SystemLanguage.ChineseSimplified, "锁定FX层" },
                    { SystemLanguage.Japanese, "FXレイヤーをロック" }
                },
                ["advanced.lock_fx_layers_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Set all non-ASS FX layer weights to 0 when locked" },
                    { SystemLanguage.ChineseSimplified, "锁定时将所有非ASS的FX层权重设为0" },
                    { SystemLanguage.Japanese, "ロック時にASS以外のすべてのFXレイヤーのウェイトを0に設定" }
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
                    { SystemLanguage.English, "Animation Write Defaults mode:\nOn = Auto restore (recommended)\nOff = Explicit restore (better compatibility)" },
                    { SystemLanguage.ChineseSimplified, "动画 Write Defaults 模式：\nOn = 自动恢复（推荐）\nOff = 显式恢复（兼容性更好）" },
                    { SystemLanguage.Japanese, "アニメーション Write Defaults モード：\nOn = 自動復元（推奨）\nOff = 明示的復元（互換性向上）" }
                },
                ["advanced.wd_mode_on"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "WD On (Auto Restore)" },
                    { SystemLanguage.ChineseSimplified, "WD On（自动恢复）" },
                    { SystemLanguage.Japanese, "WD On（自動復元）" }
                },
                ["advanced.wd_mode_off"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "WD Off (Explicit Restore)" },
                    { SystemLanguage.ChineseSimplified, "WD Off（显式恢复）" },
                    { SystemLanguage.Japanese, "WD Off（明示的復元）" }
                },
                ["advanced.wd_mode_on_hint"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "💡 WD On: Animation system automatically restores values when state exits. Simpler but may conflict with some avatar setups." },
                    { SystemLanguage.ChineseSimplified, "💡 WD On：动画系统在状态退出时自动恢复值。更简单，但可能与某些 Avatar 设置冲突。" },
                    { SystemLanguage.Japanese, "💡 WD On：状態終了時にアニメーションシステムが自動的に値を復元。シンプルですが、一部のアバター設定と競合する可能性があります。" }
                },
                ["advanced.wd_mode_off_hint"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "💡 WD Off: Explicitly writes restore values in animations. Better compatibility with other systems but uses more animation curves." },
                    { SystemLanguage.ChineseSimplified, "💡 WD Off：在动画中显式写入恢复值。与其他系统兼容性更好，但使用更多动画曲线。" },
                    { SystemLanguage.Japanese, "💡 WD Off：アニメーションに明示的に復元値を書き込み。他のシステムとの互換性が高いですが、より多くのアニメーションカーブを使用します。" }
                },

                // ========== 视觉反馈 ==========
                ["visual.config"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Visual Feedback" },
                    { SystemLanguage.ChineseSimplified, "视觉反馈" },
                    { SystemLanguage.Japanese, "視覚フィードバック" }
                },
                ["visual.countdown_text"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Countdown Text" },
                    { SystemLanguage.ChineseSimplified, "倒计时文本" },
                    { SystemLanguage.Japanese, "カウントダウンテキスト" }
                },

                // ========== 调试日志 ==========
                ["log.simplified_defense"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Debug mode: Generating simplified defense (no performance impact)" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 调试模式：生成简化版反制措施（无性能影响）" },
                    { SystemLanguage.Japanese, "[ASS] デバッグモード：簡易版対策を生成（パフォーマンスへの影響なし）" }
                },
                ["log.play_mode_test"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Play mode: Generating test system (no defense)" },
                    { SystemLanguage.ChineseSimplified, "[ASS] Play 模式：生成测试系统（无反制措施）" },
                    { SystemLanguage.Japanese, "[ASS] プレイモード：テストシステムを生成（対策なし）" }
                },
                ["log.play_mode_simplified"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Play mode: Added simplified defense layer" },
                    { SystemLanguage.ChineseSimplified, "[ASS] Play模式：已添加简化版反制措施层" },
                    { SystemLanguage.Japanese, "[ASS] プレイモード：簡易版対策レイヤーを追加しました" }
                },
                ["log.build_mode_full"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Build mode: Generating full system (with defense)" },
                    { SystemLanguage.ChineseSimplified, "[ASS] Build 模式：生成完整系统（含反制措施）" },
                    { SystemLanguage.Japanese, "[ASS] ビルドモード：完全システムを生成（対策あり）" }
                },

                // ========== 反制措施层日志 ==========
                ["log.defense_created"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Defense layer created, state count: {0}" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 防御层已创建，状态数: {0}" },
                    { SystemLanguage.Japanese, "[ASS] 防御レイヤーが作成されました、状態数: {0}" }
                },
                ["log.defense_start"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Starting to generate {0} defense states..." },
                    { SystemLanguage.ChineseSimplified, "[ASS] 开始生成 {0} 个防御状态..." },
                    { SystemLanguage.Japanese, "[ASS] {0} 個の防御状態の生成を開始..." }
                },
                ["log.defense_complete"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Defense state generation complete, created {0} sub BlendTrees" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 防御状态生成完成，创建了 {0} 个子 BlendTree" },
                    { SystemLanguage.Japanese, "[ASS] 防御状態の生成が完了、{0} 個のサブBlendTreeを作成" }
                },
                ["log.shader_failed"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Shader generation failed, skipping material replacement" },
                    { SystemLanguage.ChineseSimplified, "[ASS] Shader 生成失败，跳过材质替换" },
                    { SystemLanguage.Japanese, "[ASS] シェーダー生成失敗、マテリアル置換をスキップ" }
                },
                ["log.material_failed"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Material creation failed" },
                    { SystemLanguage.ChineseSimplified, "[ASS] Material 创建失败" },
                    { SystemLanguage.Japanese, "[ASS] マテリアル作成失敗" }
                },
                ["log.shader_animation_created"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Shader replacement animation created, replaced {0} material slots" },
                    { SystemLanguage.ChineseSimplified, "[ASS] Shader 替换动画已创建，替换了 {0} 个材质槽" },
                    { SystemLanguage.Japanese, "[ASS] シェーダー置換アニメーション作成、{0} 個のマテリアルスロットを置換" }
                },
                ["log.shader_template_missing"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Shader template file does not exist: {0}" },
                    { SystemLanguage.ChineseSimplified, "[ASS] Shader 模板文件不存在: {0}" },
                    { SystemLanguage.Japanese, "[ASS] シェーダーテンプレートファイルが存在しません: {0}" }
                },
                ["log.shader_load_failed"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Shader loading failed: {0}" },
                    { SystemLanguage.ChineseSimplified, "[ASS] Shader 加载失败: {0}" },
                    { SystemLanguage.Japanese, "[ASS] シェーダーの読み込み失敗: {0}" }
                },
                ["log.shader_generated"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Shader generated: {0}" },
                    { SystemLanguage.ChineseSimplified, "[ASS] Shader 已生成: {0}" },
                    { SystemLanguage.Japanese, "[ASS] シェーダーが生成されました: {0}" }
                },
                ["log.material_created"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Material created: {0}" },
                    { SystemLanguage.ChineseSimplified, "[ASS] Material 已创建: {0}" },
                    { SystemLanguage.Japanese, "[ASS] マテリアルが作成されました: {0}" }
                },
                ["log.particle_disabled"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Particle system defense disabled" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 粒子系统反制措施已禁用" },
                    { SystemLanguage.Japanese, "[ASS] パーティクルシステム対策が無効化されました" }
                },
                ["log.particle_animation_created"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Particle system defense animation created, count: {0}" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 粒子系统反制措施动画已创建，数量: {0}" },
                    { SystemLanguage.Japanese, "[ASS] パーティクルシステム対策アニメーション作成、数: {0}" }
                },
                ["log.particle_objects_created"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Created {0} particle systems (total particles: {1})" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 已创建 {0} 个粒子系统（总粒子数: {1}）" },
                    { SystemLanguage.Japanese, "[ASS] {0} 個のパーティクルシステムを作成（総パーティクル数: {1}）" }
                },
                ["log.drawcall_disabled"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Draw call defense disabled" },
                    { SystemLanguage.ChineseSimplified, "[ASS] Draw Call 反制措施已禁用" },
                    { SystemLanguage.Japanese, "[ASS] ドローコール対策が無効化されました" }
                },
                ["log.drawcall_animation_created"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Draw call defense animation created, extra materials: {0}" },
                    { SystemLanguage.ChineseSimplified, "[ASS] Draw Call 反制措施动画已创建，额外材质数: {0}" },
                    { SystemLanguage.Japanese, "[ASS] ドローコール対策アニメーション作成、追加マテリアル数: {0}" }
                },
                ["log.drawcall_shader_warning"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Cannot generate Shader, draw call defense cannot be created" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 无法生成 Shader，Draw Call 反制措施无法创建" },
                    { SystemLanguage.Japanese, "[ASS] シェーダーを生成できません、ドローコール対策を作成できません" }
                },
                ["log.drawcall_objects_created"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Created {0} draw call objects" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 已创建 {0} 个 Draw Call 对象" },
                    { SystemLanguage.Japanese, "[ASS] {0} 個のドローコールオブジェクトを作成" }
                },
                ["log.light_disabled"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Point light defense disabled" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 点光源反制措施已禁用" },
                    { SystemLanguage.Japanese, "[ASS] ポイントライト対策が無効化されました" }
                },
                ["log.light_animation_created"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Point light defense animation created, count: {0}" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 点光源反制措施动画已创建，数量: {0}" },
                    { SystemLanguage.Japanese, "[ASS] ポイントライト対策アニメーション作成、数: {0}" }
                },
                ["log.light_objects_created"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Created {0} point lights" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 已创建 {0} 个点光源" },
                    { SystemLanguage.Japanese, "[ASS] {0} 個のポイントライトを作成" }
                },
                ["log.cloth_disabled"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Cloth defense disabled" },
                    { SystemLanguage.ChineseSimplified, "[ASS] Cloth 反制措施已禁用" },
                    { SystemLanguage.Japanese, "[ASS] クロス対策が無効化されました" }
                },
                ["log.cloth_created"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Cloth defense animation created, vertex count: {0}" },
                    { SystemLanguage.ChineseSimplified, "[ASS] Cloth 反制措施动画已创建，顶点数: {0}" },
                    { SystemLanguage.Japanese, "[ASS] クロス対策アニメーションが作成されました、頂点数: {0}" }
                },
                ["log.cloth_objects_created"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Created Cloth object, vertex count: {0}" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 已创建 Cloth 对象，顶点数: {0}" },
                    { SystemLanguage.Japanese, "[ASS] クロスオブジェクトが作成されました、頂点数: {0}" }
                },

                // ========== 视觉反馈日志 ==========
                ["log.visual_existing_canvas"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Using existing UI Canvas" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 使用现有UI Canvas" },
                    { SystemLanguage.Japanese, "[ASS] 既存のUI Canvasを使用" }
                },
                ["log.visual_no_head_bone"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Head bone not found, UI Canvas will be placed at Avatar root" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 未找到头部骨骼，UI Canvas将放置在Avatar根节点" },
                    { SystemLanguage.Japanese, "[ASS] 頭部ボーンが見つかりません、UI Canvasはアバタールートに配置されます" }
                },
                ["log.visual_canvas_created"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] HUD Canvas created" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 已创建HUD Canvas" },
                    { SystemLanguage.Japanese, "[ASS] HUD Canvasが作成されました" }
                },
                ["log.visual_countdown_created"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Countdown text created" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 已创建倒计时文本" },
                    { SystemLanguage.Japanese, "[ASS] カウントダウンテキストが作成されました" }
                },
                ["log.visual_animation_created"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Countdown text animation created ({0} seconds)" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 已创建倒计时文本动画（{0}秒）" },
                    { SystemLanguage.Japanese, "[ASS] カウントダウンテキストアニメーションが作成されました（{0}秒）" }
                },

                // ========== 密码系统日志 ==========
                ["log.password_empty"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Password sequence is empty, unable to create password layer" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 密码序列为空，无法创建密码层" },
                    { SystemLanguage.Japanese, "[ASS] パスワードシーケンスが空です、パスワードレイヤーを作成できません" }
                },
                ["log.password_layer_created"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Gesture password layer created (tail matching with timeout), password length: {0}" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 手势密码层已创建（尾部匹配，带超时保护），密码长度: {0}" },
                    { SystemLanguage.Japanese, "[ASS] ジェスチャーパスワードレイヤーが作成されました（末尾マッチング、タイムアウト保護付き）、パスワード長: {0}" }
                },

                // ========== 倒计时系统日志 ==========
                ["log.countdown_layer_created"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Countdown layer created (with warnings), duration: {0} seconds, warning threshold: {1:F1} seconds" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 倒计时层已创建（带警告），时长: {0}秒，警告阈值: {1:F1}秒" },
                    { SystemLanguage.Japanese, "[ASS] カウントダウンレイヤーが作成されました（警告付き）、期間: {0}秒、警告閾値: {1:F1}秒" }
                },

                // ========== 初始锁定系统日志 ==========
                ["log.lock_layer_created"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Initial lock layer created" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 初始锁定层已创建" },
                    { SystemLanguage.Japanese, "[ASS] 初期ロックレイヤーが作成されました" }
                },
                ["log.lock_unlock_animation_created"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Unlock animation created (empty animation, allows objects to restore original state)" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 解锁动画已创建（空动画，允许对象恢复原始状态）" },
                    { SystemLanguage.Japanese, "[ASS] アンロックアニメーションが作成されました（空のアニメーション、オブジェクトが元の状態に戻ることができます）" }
                },
                ["log.lock_targets_found"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Found {0} objects that need to be locked" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 找到 {0} 个需要锁定的对象" },
                    { SystemLanguage.Japanese, "[ASS] ロックが必要な {0} 個のオブジェクトが見つかりました" }
                },
                ["log.lock_parameters_inverted"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Inverted default values of {0} parameters" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 已反转 {0} 个参数的默认值" },
                    { SystemLanguage.Japanese, "[ASS] {0} 個のパラメータのデフォルト値を反転しました" }
                },
                ["log.lock_ma_missing"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Parameter inversion requires Modular Avatar, but not found" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 参数反转功能需要 Modular Avatar，但未找到" },
                    { SystemLanguage.Japanese, "[ASS] パラメータ反転にはModular Avatarが必要ですが、見つかりません" }
                },

                // ========== 插件系统日志 ==========
                ["log.password_empty"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Password is empty (0 digits), ASS is disabled. Skipping generation." },
                    { SystemLanguage.ChineseSimplified, "[ASS] 密码为空（0位），ASS已禁用，跳过生成。" },
                    { SystemLanguage.Japanese, "[ASS] パスワードが空です（0桁）、ASSが無効化されています。生成をスキップします。" }
                },
                ["log.disabled_in_playmode"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Play mode disabled, skipping" },
                    { SystemLanguage.ChineseSimplified, "[ASS] Play 模式已禁用，跳过" },
                    { SystemLanguage.Japanese, "[ASS] Playモードが無効化されています、スキップします" }
                },
                ["log.no_descriptor"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] VRCAvatarDescriptor not found" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 未找到 VRCAvatarDescriptor" },
                    { SystemLanguage.Japanese, "[ASS] VRCAvatarDescriptorが見つかりません" }
                },
                ["log.plugin_config_empty"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Config is empty, skipping audio loading" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 配置为空，跳过音频加载" },
                    { SystemLanguage.Japanese, "[ASS] 設定が空です、オーディオ読み込みをスキップします" }
                },
                ["log.plugin_audio_missing"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Some audio files not found, please ensure Resources/AvatarSecuritySystem/ folder contains all audio files" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 部分音频文件未找到，请确认 Resources/AvatarSecuritySystem/ 文件夹中包含所有音频文件" },
                    { SystemLanguage.Japanese, "[ASS] 一部のオーディオファイルが見つかりません、Resources/AvatarSecuritySystem/ フォルダに全てのオーディオファイルが含まれていることを確認してください" }
                },

                // ========== 文件大小预估 ==========
                ["estimate.title"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "File Size Estimation" },
                    { SystemLanguage.ChineseSimplified, "文件大小预估" },
                    { SystemLanguage.Japanese, "ファイルサイズの推定" }
                },
                ["estimate.details"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Estimated file size: {0}\nState count: {1}\nPassword length: {2} digits" },
                    { SystemLanguage.ChineseSimplified, "预估文件大小：{0}\n状态数量：{1}\n密码长度：{2} 位" },
                    { SystemLanguage.Japanese, "推定ファイルサイズ：{0}\n状態数：{1}\nパスワード長：{2} 桁" }
                },
                ["estimate.file_size"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Estimated File Size" },
                    { SystemLanguage.ChineseSimplified, "预估文件大小" },
                    { SystemLanguage.Japanese, "推定ファイルサイズ" }
                },

                // ========== 操作按钮 ==========
                ["actions.title"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Actions" },
                    { SystemLanguage.ChineseSimplified, "操作" },
                    { SystemLanguage.Japanese, "操作" }
                },
                ["actions.test"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "🧪 Test Password Flow" },
                    { SystemLanguage.ChineseSimplified, "🧪 测试密码流程" },
                    { SystemLanguage.Japanese, "🧪 パスワードフローをテスト" }
                },
                ["actions.docs"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "📖 View Documentation" },
                    { SystemLanguage.ChineseSimplified, "📖 查看文档" },
                    { SystemLanguage.Japanese, "📖 ドキュメントを表示" }
                },
                ["actions.build"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "🔨 Manual Build (Requires VRChat SDK)" },
                    { SystemLanguage.ChineseSimplified, "🔨 手动构建 (需要 VRChat SDK)" },
                    { SystemLanguage.Japanese, "🔨 手動ビルド (VRChat SDK が必要)" }
                },
                ["actions.build_message"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Please use VRChat SDK's Build & Publish feature to build the Avatar.\nThe ASS system will be generated automatically during the build." },
                    { SystemLanguage.ChineseSimplified, "请使用 VRChat SDK 的 Build & Publish 功能构建 Avatar。\nASS 系统会在构建时自动生成。" },
                    { SystemLanguage.Japanese, "VRChat SDKのBuild & Publish機能を使用してアバターをビルドしてください。\nASSシステムはビルド時に自動的に生成されます。" }
                },

                // ========== 日志消息 ==========
                ["log.not_found"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] No valid AvatarSecuritySystem component found, skipping" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 未找到有效的 AvatarSecuritySystem 组件，跳过" },
                    { SystemLanguage.Japanese, "[ASS] 有効なAvatarSecuritySystemコンポーネントが見つかりません、スキップ" }
                },
                ["log.generating"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Starting to generate security system..." },
                    { SystemLanguage.ChineseSimplified, "[ASS] 开始生成安全系统..." },
                    { SystemLanguage.Japanese, "[ASS] セキュリティシステムの生成を開始..." }
                },
                ["log.complete"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Security system generation complete!" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 安全系统生成完成！" },
                    { SystemLanguage.Japanese, "[ASS] セキュリティシステムの生成が完了しました！" }
                },
            };
        }
    }
}
