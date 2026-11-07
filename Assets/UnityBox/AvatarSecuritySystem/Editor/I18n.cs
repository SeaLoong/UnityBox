using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace UnityBox.AvatarSecuritySystem.Editor
{
    public static class I18n
    {
        private static readonly SystemLanguage[] Languages =
        {
            SystemLanguage.English, SystemLanguage.ChineseSimplified,
            SystemLanguage.ChineseTraditional, SystemLanguage.Japanese
        };
        private static readonly Regex Tokens = new Regex(@"#\{(?<key>[A-Za-z0-9_.]+)\}");
        private static SystemLanguage currentLanguage = NormalizeLanguage(Application.systemLanguage);
        private static readonly Dictionary<string, string[]> Translations = new Dictionary<string, string[]>
        {
            ["common.confirm"] = new[] { "Confirm", "确定", "確定", "確認" },
            ["common.cancel"] = new[] { "Cancel", "取消", "取消", "キャンセル" },
            ["common.warning"] = new[] { "Notice", "提示", "提示", "お知らせ" },
            ["language.title"] = new[] { "Language", "语言", "語言", "言語" },
            ["language.auto"] = new[] { "Auto (System)", "自动（跟随系统）", "自動（跟隨系統）", "自動（システム）" },
            ["language.chinese"] = new[] { "Simplified Chinese", "简体中文", "簡體中文", "中国語（簡体）" },
            ["language.english"] = new[] { "English", "英语", "英語", "英語" },
            ["language.japanese"] = new[] { "Japanese", "日语", "日語", "日本語" },
            ["language.ui_language_tooltip"] = new[] { "Choose the interface language for this component. Auto follows the system language.", "选择此组件的界面语言。自动模式跟随系统语言。", "選擇此元件的介面語言。自動模式使用系統語言。", "このコンポーネントの表示言語を選択します。自動ではシステム言語を使用します。" },
            ["system.name"] = new[] { "Avatar Security System", "Avatar 安全系统", "Avatar 安全系統", "アバターセキュリティシステム" },
            ["system.short_name"] = new[] { "ASS", "ASS", "ASS", "ASS" },
            ["system.subtitle"] = new[] { "Gesture input, timing and build feedback", "手势输入、计时与构建反馈", "手勢輸入、計時與建置回饋", "ジェスチャー入力・時間制御・ビルド時のフィードバック" },
            ["password.config"] = new[] { "Password Configuration", "密码配置", "密碼設定", "パスワード設定" },
            ["password.use_right_hand"] = new[] { "Use Right Hand", "使用右手输入", "使用右手輸入", "右手を使用" },
            ["password.use_right_hand_tooltip"] = new[] { "Read the left hand when disabled, or the right hand when enabled.", "关闭时读取左手，开启时读取右手。", "關閉時讀取左手，開啟時讀取右手。", "無効時は左手、有効時は右手を読み取ります。" },
            ["gesture.hold_time_tooltip"] = new[] { "Keep the correct gesture continuously for this duration. A wrong gesture restarts this step’s hold timer. This value must exceed error tolerance.", "正确手势必须连续保持此时长。错误手势会重新开始本步的保持计时；此值必须大于容错时间。", "正確手勢必須連續維持此時間。切換到錯誤手勢會重新開始本步驟的計時；此值必須大於容錯時間。", "正しいジェスチャーをこの時間連続で保持します。誤ったジェスチャーでこのステップの保持時間がリセットされます。許容時間より長い値にします。" },
            ["gesture.error_tolerance_tooltip"] = new[] { "Correct a wrong gesture within this window to retain completed steps. Wrong input never contributes to continuous hold time. This value must be shorter than minimum hold.", "在此时间内纠正错误手势可保留已完成步骤；错误输入不会计入正确保持时间。此值必须小于最小保持时间。", "在此時間內修正錯誤手勢可保留已完成的步驟；錯誤期間不計入正確手勢的持續時間。此值必須小於最短持續時間。", "この時間内に誤入力を直すと、完了済みステップを保持します。誤入力は正しい保持時間に加算しません。最小保持時間より短く設定します。" },
            ["password.gesture_tooltip"] = new[] { "Define a sequence of gestures from 0 through 7. 0 is Idle; the others are the seven VRChat gestures.", "依次设置0–7的手势。0为闲置，其余为VRChat的七种手势。", "依序設定 0–7 的手勢。0 表示閒置，其餘為 VRChat 的七種手勢。", "0～7のジェスチャーを順番に設定します。0は待機、他の値はVRChatの7種類のジェスチャーです。" },
            ["gesture.config"] = new[] { "Gesture Recognition", "手势识别配置", "手勢辨識", "ジェスチャー認識設定" },
            ["gesture.hold_time"] = new[] { "Min Continuous Hold (sec)", "最小连续保持时间（秒）", "最短持續時間（秒）", "最小連続保持時間（秒）" },
            ["gesture.max_hold_time"] = new[] { "Next-Step Wait Limit (sec)", "下一步等待时限（秒）", "下一步等待時限（秒）", "次のステップの待機上限（秒）" },
            ["gesture.max_hold_time_tooltip"] = new[] { "Maximum wait after a gesture has been confirmed. Switch to the next step within this time, or restart the sequence.", "当前手势确认后，切换到下一步的最长等待时间；超时会重新开始序列。", "目前手勢確認後，切換到下一步的最長等待時間；逾時會重新開始序列。", "ジェスチャーの確定後、次のステップに切り替えられる最大待機時間です。超えると最初から入力します。" },
            ["gesture.error_tolerance"] = new[] { "Error Tolerance (sec)", "容错时间 (秒)", "容錯時間（秒）", "エラー許容時間 (秒)" },
            ["password.sequence"] = new[] { "Gesture Password Sequence:", "手势密码序列：", "手勢密碼序列：", "ジェスチャーパスワードシーケンス：" },
            ["password.step"] = new[] { "Step {0}:", "第 {0} 位：", "第 {0} 步：", "{0} 番目：" },
            ["password.add_gesture"] = new[] { "➕ Add Gesture", "➕ 添加手势", "➕ 新增手勢", "➕ ジェスチャーを追加" },
            ["password.clear"] = new[] { "🗑 Clear Password", "🗑 清空密码", "🗑 清空密碼", "🗑 パスワードをクリア" },
            ["password.clear_confirm"] = new[] { "Are you sure you want to clear the password?", "确定要清空密码吗？", "確定要清空這個元件的手勢密碼嗎？", "パスワードをクリアしてもよろしいですか？" },
            ["password.delete_step"] = new[] { "Delete this step", "删除此步骤", "刪除此步驟", "このステップを削除" },
            ["password.strength"] = new[] { "Password Strength: {0} ({1} digits)", "密码强度：{0} ({1} 位)", "密碼強度：{0}（{1} 步）", "パスワード強度：{0} ({1} 桁)" },
            ["password.strength.weak"] = new[] { "Weak", "弱", "弱", "弱い" },
            ["password.strength.medium"] = new[] { "Medium", "中", "中", "中程度" },
            ["password.strength.strong"] = new[] { "Strong", "强", "強", "強い" },
            ["password.empty_warning"] = new[] { "The sequence is empty. ASS gesture logic is not generated during the build.", "密码为空，构建时不生成ASS手势逻辑。", "密碼為空，建置時不生成 ASS 手勢邏輯。", "シーケンスが空のため、ビルド時にASSのジェスチャーロジックは生成されません。" },
            ["countdown.config"] = new[] { "Countdown Configuration", "倒计时配置", "倒數計時", "カウントダウン設定" },
            ["countdown.duration"] = new[] { "Countdown Duration (sec)", "倒计时时长 (秒)", "倒數時間（秒）", "カウントダウン時間 (秒)" },
            ["countdown.duration_tooltip"] = new[] { "Time allowed for local password input. Timeout sets the timeout state and applies the configured feedback.", "本地输入密码的时间限制。超时会设置超时状态，并按配置提供反馈。", "本機輸入密碼的時間限制。時間到時設定逾時狀態，並依設定提供回饋。", "ローカルでパスワードを入力できる時間です。時間切れになるとタイムアウト状態となり、設定したフィードバックを適用します。" },
            ["defense.options"] = new[] { "VRChat Defense Options", "VRChat防御选项", "VRChat 防禦設定", "VRChatの防御設定" },
            ["advanced.play_mode"] = new[] { "VRChat Editor Play Preview", "VRChat编辑器播放预览", "VRChat 編輯器播放預覽", "VRChatのエディター再生プレビュー" },
            ["advanced.play_mode_tooltip"] = new[] { "Preview ASS through the VRC SDK’s editor play workflow. Unity and VRChat builds are processed automatically regardless of this option.", "在VRC SDK的编辑器播放流程中预览ASS。正式Unity及VRChat构建都会自动处理，不依赖此选项。", "在 VRC SDK 的編輯器播放流程中預覽 ASS。正式 Unity 與 VRChat 建置都會自動處理，不依賴此開關。", "VRC SDKのエディター再生でASSをプレビューします。この設定にかかわらずUnityとVRChatのビルドは自動処理されます。" },
            ["defense.disable_defense"] = new[] { "Disable Defense", "禁用防御", "停用防禦", "防御を無効化" },
            ["defense.disable_defense_tooltip"] = new[] { "Generate only gestures, timing and feedback in VRChat builds. This option is disabled while Default Enable Defense is active.", "在VRChat构建中只生成手势、计时与反馈。开启默认启用防御模式时，此选项停用。", "在 VRChat 建置中只生成手勢、計時與回饋。預設啟用防禦模式開啟時，此選項停用。", "VRChatビルドでジェスチャー・時間制御・フィードバックのみ生成します。デフォルト防御が有効な間は変更できません。" },
            ["advanced.disable_overlay"] = new[] { "Disable Overlay Interface", "关闭覆盖界面", "關閉覆蓋介面", "オーバーレイ表示を無効化" },
            ["advanced.disable_overlay_tooltip"] = new[] { "Do not generate the fullscreen feedback interface. Audio uses its own settings.", "不生成全屏反馈界面；音效由各自设置控制。", "不生成全螢幕回饋介面；音效由各自的設定控制。", "全画面フィードバックを生成しません。音声は個別の設定で制御します。" },
            ["advanced.mute_warning"] = new[] { "Disable Warning Sound", "关闭警告音效", "關閉倒數警告音", "警告音を無効化" },
            ["advanced.mute_warning_tooltip"] = new[] { "Do not generate the countdown warning beep. Password success audio is independent of this option.", "不生成倒计时警告音；密码成功音不受此选项影响。", "不生成倒數警告音；密碼成功音不受此開關影響。", "カウントダウンの警告音を生成しません。パスワード成功音はこの設定とは独立しています。" },
            ["advanced.default_defense"] = new[] { "Default Enable Defense", "默认启用防御", "預設啟用防禦", "デフォルトで防御を有効化" },
            ["advanced.default_defense_tooltip"] = new[] { "Enable the existing defense configuration directly in VRChat builds, without a gesture-password input. Ordinary Unity builds still use gesture logic.", "在VRChat构建中直接启用现有防御配置，不生成手势密码输入。普通Unity构建仍使用手势逻辑。", "VRChat 建置直接啟用現有防禦設定，不生成手勢密碼輸入。一般 Unity 建置仍使用手勢邏輯。", "VRChatビルドで既存の防御設定を直接有効にし、ジェスチャーパスワードは生成しません。通常のUnityビルドではジェスチャーロジックを使用します。" },
            ["advanced.default_defense_warning"] = new[] { "VRChat default defense mode is selected, so no password input is generated. Disable this mode to configure a gesture sequence and countdown.", "当前选择了VRChat默认防御模式，不生成密码输入；关闭此模式即可设置手势序列与倒计时。", "目前使用 VRChat 預設防禦模式，不生成密碼輸入；關閉此模式即可設定手勢序列與倒數計時。", "VRChatのデフォルト防御モードではパスワード入力を生成しません。このモードを無効にすると、ジェスチャーとカウントダウンを設定できます。" },
            ["defense.lightweight_defense"] = new[] { "Lightweight Mode", "轻量模式", "輕量模式", "軽量モード" },
            ["defense.lightweight_defense_tooltip"] = new[] { "Use the existing lightweight generation strategy to reduce added particle systems. This option does not enable disabled defense features.", "沿用现有轻量生成策略，减少新增粒子系统；此选项不会启用已禁用的防御功能。", "沿用既有的輕量防禦生成策略，減少新增的粒子系統；此選項不會重新啟用已停用的防禦。", "既存の軽量生成方式で追加パーティクルシステムを減らします。無効な防御機能を有効にはしません。" },
            ["defense.lightweight_defense_note"] = new[] { "This option only affects VRChat defense generation. Ordinary Unity gesture and timing logic does not use these effects.", "此选项仅影响VRChat防御生成。普通Unity的手势与计时逻辑不使用这些效果。", "此選項僅影響 VRChat 防禦生成。一般 Unity 的手勢與計時邏輯不使用這些效果。", "VRChatの防御生成のみを変更します。通常のUnityのジェスチャーと時間制御ではこれらの効果を使用しません。" },
            ["advanced.options"] = new[] { "Build and Preview", "构建与预览", "建置與預覽", "ビルドとプレビュー" },
            ["advanced.disable_objects_tooltip"] = new[] { "Retained for legacy serialized configurations. Avatar object locking was removed; this value no longer generates scale or visibility animation.", "仅保留用于读取旧序列化配置。Avatar对象锁定已移除，此值不再生成缩放或隐藏动画。", "保留舊版序列化資料用的欄位。Avatar 物件鎖定已移除，此值不再生成縮放或隱藏動畫。", "旧設定の読み込み用フィールドです。アバター本体のロックは廃止され、拡縮や非表示のアニメーションは生成しません。" },
            ["advanced.wd_mode_tooltip"] = new[] { "Retained for legacy serialized configurations. The generator manages Write Defaults for its own states.", "仅保留用于读取旧序列化配置；生成器处理自身状态的Write Defaults，无需在此设置。", "保留舊版序列化資料用的欄位；生成器處理自身狀態的 Write Defaults，不需要在此設定。", "旧設定の読み込み用フィールドです。生成するステートのWrite Defaultsは生成器が処理します。" },
            ["advanced.obfuscation_section"] = new[] { "Obfuscation Options", "混淆选项", "命名與混淆", "難読化オプション" },
            ["advanced.disable_obfuscation"] = new[] { "Disable Obfuscation", "禁用混淆", "停用混淆", "難読化を無効化" },
            ["advanced.disable_obfuscation_tooltip"] = new[] { "Keep generated parameter, layer and object names readable for inspecting the controller.", "保留生成参数、层与对象的原始名称，方便检查控制器。", "保留生成參數、圖層和物件的原始名稱，方便檢查控制器。", "生成したパラメーター・レイヤー・オブジェクト名を保持して、コントローラーを確認しやすくします。" },
            ["advanced.obfuscate_playable_layers"] = new[] { "Obfuscate All Playable Layers", "混淆全部 Playable Layer", "混淆全部 Playable Layer", "すべてのPlayable Layerを難読化" },
            ["advanced.obfuscate_playable_layers_tooltip"] = new[] { "Copy and rename custom playable-controller graph nodes during VRChat builds. Original project assets remain unchanged.", "在VRChat构建中复制并重命名自定义Playable Controller的图节点；项目来源资源保持原样。", "在 VRChat 建置中複製並重新命名自訂 Playable Controller 內的圖節點；專案中的來源資源維持原樣。", "VRChatビルド時にカスタムPlayable Controllerを複製してノード名を変更します。元のプロジェクト資産は変更しません。" },
            ["advanced.decoy_layers"] = new[] { "Animation Layer Noise", "动画层噪声", "動畫圖層噪聲", "アニメーションレイヤーノイズ" },
            ["advanced.decoy_layers_tooltip"] = new[] { "Add naming-noise layers while retaining the normal gesture path. This increases controller size.", "添加命名噪声层，保留正常手势路径；这会增加控制器规模。", "新增命名噪聲圖層，保留正常手勢路徑；此功能會增加控制器規模。", "通常のジェスチャー経路を保ちながら、名前のノイズ用レイヤーを追加します。コントローラーの規模は増加します。" },
            ["advanced.decoy_states"] = new[] { "Animation State Noise", "动画状态噪声", "動畫狀態噪聲", "アニメーションステートノイズ" },
            ["advanced.decoy_states_tooltip"] = new[] { "Add naming-noise states while retaining the normal gesture path. This increases controller size.", "添加命名噪声状态，保留正常手势路径；这会增加控制器规模。", "在圖層內新增命名噪聲狀態，保留正常手勢路徑；此功能會增加控制器規模。", "通常のジェスチャー経路を保ちながら、名前のノイズ用ステートを追加します。コントローラーの規模は増加します。" },
            ["advanced.no_feedback_warning"] = new[] { "Overlay and countdown warning audio are disabled. Password success audio and timeout state still operate.", "覆盖界面和倒计时警告音已关闭；密码成功音与超时状态仍正常处理。", "覆蓋介面與倒數警告音都已關閉。輸入過程不顯示這兩種提示，密碼成功音與逾時狀態仍照常處理。", "オーバーレイとカウントダウン警告音は無効です。成功音とタイムアウト状態は引き続き処理されます。" },
            ["language.traditional_chinese"] = new[] { "Traditional Chinese", "繁体中文", "繁體中文", "中国語（繁体）" },
            ["gesture.continuous_hold_help"] = new[] { "Each step needs one uninterrupted correct hold. Wrong input restarts that step’s timer. Error tolerance is shorter than minimum hold; repeated gestures need a release between steps.", "每一步都需要连续保持正确手势；错误输入会重置该步计时。容错时间小于最小保持时间，相邻重复手势必须先释放再保持。", "每一步都需要連續保持正確手勢；錯誤輸入會重設該步計時。容錯時間小於最短持續時間，相鄰重複手勢必須先釋放再保持。", "各ステップで正しいジェスチャーを連続保持します。誤入力で保持時間をリセットし、許容時間は最小保持時間より短くなります。同じジェスチャーが続く場合は一度離します。" },
            ["feedback.options"] = new[] { "Feedback", "反馈", "回饋", "フィードバック" },
            ["build.automatic_help"] = new[] { "Unity and VRChat builds generate ASS automatically. Configure this component; no manual Generate step is required.", "Unity和VRChat构建都会自动生成ASS。配置此组件即可，不需要手动点击Generate。", "Unity 和 VRChat 建置都會自動生成 ASS。設定此元件即可，不需要手動點擊 Generate。", "UnityとVRChatのビルドでASSを自動生成します。このコンポーネントを設定するだけで、手動のGenerate操作は不要です。" },
            ["build.standalone_help"] = new[] { "Ordinary Unity builds keep existing animation layers and add gesture, timing and feedback logic. Supply gestures at runtime through StandaloneSecurityInput.SetGesture.", "普通Unity构建会保留现有动画层，并加入手势、计时与反馈逻辑。运行时通过StandaloneSecurityInput.SetGesture提供手势输入。", "一般 Unity 建置會保留現有動畫圖層，加入手勢、計時與回饋邏輯。執行時透過 StandaloneSecurityInput.SetGesture 提供手勢輸入。", "通常のUnityビルドでは既存レイヤーを保持し、ジェスチャー・時間制御・フィードバックを追加します。実行時にStandaloneSecurityInput.SetGestureで入力を渡します。" },
            ["build.vrchat_only"] = new[] { "These options apply to the optional VRChat build integration. Ordinary Unity builds use gestures, timing and feedback.", "这些选项用于可选的VRChat构建集成；普通Unity构建使用手势、计时与反馈。", "這些選項用於選用的 VRChat 建置整合；一般 Unity 建置使用手勢、計時與回饋。", "これらの設定はオプションのVRChatビルド連携で使用します。通常のUnityビルドはジェスチャー・時間制御・フィードバックを生成します。" },
            ["build.duplicate_configuration"] = new[] { "More than one ASS configuration targets {0}. Keep one configuration on that root.", "多个ASS配置指向{0}，请在该根对象上保留一个配置。", "多個 ASS 設定指向 {0}，請在該根物件保留一個設定。", "複数のASS設定が{0}を対象にしています。そのルートには1つの設定を残してください。" },
            ["build.invalid_output_path"] = new[] { "Choose a .controller output path below Assets.", "请选择Assets目录下的.controller输出路径。", "請選擇 Assets 目錄下的 .controller 輸出路徑。", "Assets配下の.controller出力先を指定してください。" },
            ["build.invalid_password"] = new[] { "Set at least one gesture from 0 through 7.", "请设置至少一个0–7范围内的手势。", "請設定至少一個 0–7 範圍內的手勢。", "0～7のジェスチャーを1つ以上設定してください。" },
            ["migration.menu_label"] = new[] { "Repair Legacy Components", "修复旧版组件引用", "修復舊版元件引用", "旧バージョンのコンポーネント参照を修復" },
            ["migration.restored"] = new[] { "[ASS] Restored {0} legacy references in {1} assets. Original files: {2}", "[ASS] 已恢复{1}个资源中的{0}处旧组件引用。原文件备份：{2}", "[ASS] 已恢復 {1} 個資源中的 {0} 處舊元件引用。原始檔備份：{2}", "[ASS] {1}個のアセットにある{0}件の旧参照を復元しました。元ファイル：{2}" },
            ["migration.deferred"] = new[] { "[ASS] Save the current scene or Prefab, then run {0} to restore its legacy references. Pending assets: {1}", "[ASS] 请先保存当前场景或Prefab，再运行{0}恢复旧组件引用。待处理资源：{1}", "[ASS] 請先儲存目前場景或 Prefab，再執行 {0} 恢復舊元件引用。待處理資源：{1}", "[ASS] 現在のシーンまたはPrefabを保存し、{0}で旧参照を復元してください。対象：{1}" },
            ["migration.script_not_ready"] = new[] { "The installed ASS configuration script must finish importing before legacy references are repaired.", "请等待已安装的ASS配置脚本导入完成，再修复旧组件引用。", "請等待已安裝的 ASS 設定腳本匯入完成，再修復舊元件引用。", "インストールしたASS設定スクリプトのインポート完了後に、旧参照を修復してください。" },
            ["gesture.name.0"] = new[] { "0: Idle", "0: 闲置", "0: 閒置", "0: 待機" },
            ["gesture.name.1"] = new[] { "1: Fist ✊", "1: 握拳 ✊", "1: 握拳 ✊", "1: グー ✊" },
            ["gesture.name.2"] = new[] { "2: Hand Open ✋", "2: 张开手掌 ✋", "2: 張開手掌 ✋", "2: パー ✋" },
            ["gesture.name.3"] = new[] { "3: Finger Point ☝", "3: 食指指向 ☝", "3: 食指指向 ☝", "3: 人差し指 ☝" },
            ["gesture.name.4"] = new[] { "4: Victory ✌", "4: 胜利 ✌", "4: 勝利 ✌", "4: ピース ✌" },
            ["gesture.name.5"] = new[] { "5: Rock n Roll 🤘", "5: 摇滚 🤘", "5: 搖滾 🤘", "5: ロック 🤘" },
            ["gesture.name.6"] = new[] { "6: Hand Gun 🔫", "6: 手枪 🔫", "6: 手槍 🔫", "6: ハンドガン 🔫" },
            ["gesture.name.7"] = new[] { "7: Thumbs Up 👍", "7: 竖拇指 👍", "7: 豎拇指 👍", "7: サムズアップ 👍" },

        };

        public static IReadOnlyList<SystemLanguage> SupportedLanguages => Languages;
        public static IEnumerable<string> Keys => Translations.Keys;

        public static void SetLanguage(SystemLanguage language)
        {
            currentLanguage = NormalizeLanguage(language == SystemLanguage.Unknown ? Application.systemLanguage : language);
        }

        public static SystemLanguage GetCurrentLanguage() => currentLanguage;
        public static string T(string key) => Translate(key, currentLanguage);

        public static string T(string key, params object[] arguments)
        {
            string culture = currentLanguage == SystemLanguage.ChineseSimplified ? "zh-CN"
                : currentLanguage == SystemLanguage.ChineseTraditional ? "zh-TW"
                : currentLanguage == SystemLanguage.Japanese ? "ja-JP" : "en-US";
            return string.Format(CultureInfo.GetCultureInfo(culture), T(key), arguments);
        }

        public static string Translate(string key, SystemLanguage language)
        {
            return Translations.TryGetValue(key, out var text)
                ? text[Array.IndexOf(Languages, NormalizeLanguage(language))]
                : "[Missing: " + key + "]";
        }

        public static GUIContent Content(string labelKey, string tooltipKey = null)
        {
            return new GUIContent(T(labelKey), tooltipKey == null ? "" : T(tooltipKey));
        }

        public static string ExpandTokens(string text)
        {
            return Tokens.Replace(text ?? "", match => T(match.Groups["key"].Value));
        }

        public static string[] GetGestureNames()
        {
            return Enumerable.Range(0, 8).Select(index => T("gesture.name." + index)).ToArray();
        }

        private static SystemLanguage NormalizeLanguage(SystemLanguage language)
        {
            if (language == SystemLanguage.Chinese) return SystemLanguage.ChineseSimplified;
            return Array.IndexOf(Languages, language) >= 0 ? language : SystemLanguage.English;
        }
    }
}
