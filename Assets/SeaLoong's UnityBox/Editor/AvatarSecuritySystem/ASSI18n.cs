using UnityEngine;
using System.Collections.Generic;

namespace SeaLoongUnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// ASS 国际化系统
    /// 支持中文、英文、日文
    /// </summary>
    public static class ASSI18n
    {
        private static SystemLanguage _currentLanguage;
        private static Dictionary<string, Dictionary<SystemLanguage, string>> _translations;

        static ASSI18n()
        {
            // 自动检测系统语言
            _currentLanguage = Application.systemLanguage;
            
            // 如果不支持的语言，默认使用英文
            if (_currentLanguage != SystemLanguage.Chinese &&
                _currentLanguage != SystemLanguage.ChineseSimplified &&
                _currentLanguage != SystemLanguage.ChineseTraditional &&
                _currentLanguage != SystemLanguage.Japanese)
            {
                _currentLanguage = SystemLanguage.English;
            }

            InitializeTranslations();
        }

        /// <summary>
        /// 获取翻译文本
        /// </summary>
        public static string T(string key)
        {
            if (_translations.TryGetValue(key, out var languageDict))
            {
                if (languageDict.TryGetValue(_currentLanguage, out var text))
                    return text;
                
                // 简体中文回退
                if (_currentLanguage == SystemLanguage.ChineseTraditional &&
                    languageDict.TryGetValue(SystemLanguage.ChineseSimplified, out var simplifiedText))
                    return simplifiedText;
                
                // 英文回退
                if (languageDict.TryGetValue(SystemLanguage.English, out var englishText))
                    return englishText;
            }

            return $"[Missing: {key}]";
        }

        /// <summary>
        /// 设置语言
        /// </summary>
        public static void SetLanguage(SystemLanguage language)
        {
            // 如果是 Unknown，使用系统语言
            if (language == SystemLanguage.Unknown)
            {
                _currentLanguage = Application.systemLanguage;
            }
            else
            {
                _currentLanguage = language;
            }
            
            // 如果不支持的语言，默认使用英文
            if (_currentLanguage != SystemLanguage.Chinese &&
                _currentLanguage != SystemLanguage.ChineseSimplified &&
                _currentLanguage != SystemLanguage.ChineseTraditional &&
                _currentLanguage != SystemLanguage.Japanese &&
                _currentLanguage != SystemLanguage.English)
            {
                _currentLanguage = SystemLanguage.English;
            }
        }

        /// <summary>
        /// 获取当前语言
        /// </summary>
        public static SystemLanguage GetCurrentLanguage()
        {
            return _currentLanguage;
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
                },                ["defense.enhancement"] = new Dictionary<SystemLanguage, string>
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
                },                ["defense.state_count"] = new Dictionary<SystemLanguage, string>
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
                ["advanced.unlimited_time"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Unlimited Password Time" },
                    { SystemLanguage.ChineseSimplified, "不限制密码输入时间" },
                    { SystemLanguage.Japanese, "パスワード入力時間制限なし" }
                },
                ["advanced.unlimited_time_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Disable countdown (no time limit for password input)" },
                    { SystemLanguage.ChineseSimplified, "禁用倒计时（密码输入无时间限制）" },
                    { SystemLanguage.Japanese, "カウントダウンを無効化（パスワード入力に時間制限なし）" }
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
                ["advanced.lock_options"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Lock Options" },
                    { SystemLanguage.ChineseSimplified, "锁定选项" },
                    { SystemLanguage.Japanese, "ロックオプション" }
                },
                ["advanced.invert_params"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Invert Parameters" },
                    { SystemLanguage.ChineseSimplified, "反转参数" },
                    { SystemLanguage.Japanese, "パラメータを反転" }
                },
                ["advanced.invert_params_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Invert all Avatar parameters as initial lock" },
                    { SystemLanguage.ChineseSimplified, "反转所有 Avatar 参数作为初始锁定" },
                    { SystemLanguage.Japanese, "初期ロックとしてすべてのアバターパラメータを反転" }
                },
                ["advanced.disable_objects"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Disable Root Children" },
                    { SystemLanguage.ChineseSimplified, "禁用根对象" },
                    { SystemLanguage.Japanese, "ルートオブジェクトを無効化" }
                },
                ["advanced.disable_objects_tooltip"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Disable all root level child objects as initial lock" },
                    { SystemLanguage.ChineseSimplified, "禁用所有根级子对象作为初始锁定" },
                    { SystemLanguage.Japanese, "初期ロックとしてすべてのルートレベル子オブジェクトを無効化" }
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
                ["visual.unlimited_text"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Unlimited" },
                    { SystemLanguage.ChineseSimplified, "不限时" },
                    { SystemLanguage.Japanese, "無制限" }
                },

                // ========== 调试日志 ==========
                ["log.debug_mode"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Debug mode: Unlimited password input time" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 调试模式：无限密码输入时间" },
                    { SystemLanguage.Japanese, "[ASS] デバッグモード：パスワード入力時間無制限" }
                },
                ["log.simplified_countermeasures"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Debug mode: Generating simplified countermeasures (no performance impact)" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 调试模式：生成简化版反制措施（无性能影响）" },
                    { SystemLanguage.Japanese, "[ASS] デバッグモード：簡易版対策を生成（パフォーマンスへの影響なし）" }
                },
                ["log.play_mode_test"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Play mode: Generating test system (no countermeasures)" },
                    { SystemLanguage.ChineseSimplified, "[ASS] Play 模式：生成测试系统（无反制措施）" },
                    { SystemLanguage.Japanese, "[ASS] プレイモード：テストシステムを生成（対策なし）" }
                },
                ["log.play_mode_simplified"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Play mode: Added simplified countermeasures layer" },
                    { SystemLanguage.ChineseSimplified, "[ASS] Play模式：已添加简化版反制措施层" },
                    { SystemLanguage.Japanese, "[ASS] プレイモード：簡易版対策レイヤーを追加しました" }
                },
                ["log.build_mode_full"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Build mode: Generating full system (with countermeasures)" },
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
                    { SystemLanguage.English, "[ASS] Particle system countermeasure disabled" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 粒子系统反制措施已禁用" },
                    { SystemLanguage.Japanese, "[ASS] パーティクルシステム対策が無効化されました" }
                },
                ["log.particle_animation_created"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Particle system countermeasure animation created, count: {0}" },
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
                    { SystemLanguage.English, "[ASS] Draw call countermeasure disabled" },
                    { SystemLanguage.ChineseSimplified, "[ASS] Draw Call 反制措施已禁用" },
                    { SystemLanguage.Japanese, "[ASS] ドローコール対策が無効化されました" }
                },
                ["log.drawcall_animation_created"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Draw call countermeasure animation created, extra materials: {0}" },
                    { SystemLanguage.ChineseSimplified, "[ASS] Draw Call 反制措施动画已创建，额外材质数: {0}" },
                    { SystemLanguage.Japanese, "[ASS] ドローコール対策アニメーション作成、追加マテリアル数: {0}" }
                },
                ["log.drawcall_shader_warning"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Cannot generate Shader, draw call countermeasure cannot be created" },
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
                    { SystemLanguage.English, "[ASS] Point light countermeasure disabled" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 点光源反制措施已禁用" },
                    { SystemLanguage.Japanese, "[ASS] ポイントライト対策が無効化されました" }
                },
                ["log.light_animation_created"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Point light countermeasure animation created, count: {0}" },
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
                    { SystemLanguage.English, "[ASS] Cloth countermeasure disabled" },
                    { SystemLanguage.ChineseSimplified, "[ASS] Cloth 反制措施已禁用" },
                    { SystemLanguage.Japanese, "[ASS] クロス対策が無効化されました" }
                },
                ["log.cloth_created"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Cloth countermeasure animation created, vertex count: {0}" },
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
                ["log.visual_status_created"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Status text created (unlimited)" },
                    { SystemLanguage.ChineseSimplified, "[ASS] 已创建状态文本（不限时）" },
                    { SystemLanguage.Japanese, "[ASS] ステータステキストが作成されました（無制限）" }
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
                ["log.plugin_password_empty"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Password is empty (0 digits), ASS is disabled. Skipping generation." },
                    { SystemLanguage.ChineseSimplified, "[ASS] 密码为空（0位），ASS已禁用，跳过生成。" },
                    { SystemLanguage.Japanese, "[ASS] パスワードが空です（0桁）、ASSが無効化されています。生成をスキップします。" }
                },
                ["log.plugin_play_disabled"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "[ASS] Play mode disabled, skipping" },
                    { SystemLanguage.ChineseSimplified, "[ASS] Play 模式已禁用，跳过" },
                    { SystemLanguage.Japanese, "[ASS] Playモードが無効化されています、スキップします" }
                },
                ["log.plugin_no_descriptor"] = new Dictionary<SystemLanguage, string>
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

                // ========== 构建确认对话框 ==========
                ["build.confirm_title"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Avatar Security System" },
                    { SystemLanguage.ChineseSimplified, "Avatar 安全系统" },
                    { SystemLanguage.Japanese, "アバターセキュリティシステム" }
                },
                ["build.confirm_message"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "About to generate security system for Avatar:\n\n• Password length: {0} digits\n• Countdown: {1} seconds\n• Defense states: {2}\n• Estimated file size: {3:F1} KB\n\nDo you want to continue?" },
                    { SystemLanguage.ChineseSimplified, "即将为 Avatar 生成安全系统：\n\n• 密码长度：{0} 位\n• 倒计时：{1} 秒\n• 防御状态：{2} 个\n• 预估文件大小：{3:F1} KB\n\n确定要继续吗？" },
                    { SystemLanguage.Japanese, "アバターのセキュリティシステムを生成します：\n\n• パスワード長：{0} 桁\n• カウントダウン：{1} 秒\n• 防御状態：{2} 個\n• 推定ファイルサイズ：{3:F1} KB\n\n続行しますか？" }
                },
                ["build.continue"] = new Dictionary<SystemLanguage, string>
                {
                    { SystemLanguage.English, "Continue Build" },
                    { SystemLanguage.ChineseSimplified, "继续构建" },
                    { SystemLanguage.Japanese, "ビルドを続行" }
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
