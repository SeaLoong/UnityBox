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
            ["common.warning"] = new[] { "Warning", "警告", "警告", "警告" },
            ["language.title"] = new[] { "Language", "语言", "語言", "言語" },
            ["language.auto"] = new[] { "Auto (System)", "自动（跟随系统）", "自動（跟隨系統）", "自動（システム）" },
            ["language.chinese"] = new[] { "Simplified Chinese", "简体中文", "簡體中文", "中国語（簡体）" },
            ["language.english"] = new[] { "English", "英语", "英語", "英語" },
            ["language.japanese"] = new[] { "Japanese", "日语", "日語", "日本語" },
            ["language.ui_language_tooltip"] = new[] { "UI Language / 界面语言", "界面语言 / UI Language", "介面語言 / UI Language", "UI言語 / 界面语言" },
            ["system.name"] = new[] { "Avatar Security System", "Avatar 安全系统", "Avatar 安全系統", "アバターセキュリティシステム" },
            ["system.short_name"] = new[] { "ASS", "ASS", "ASS", "ASS" },
            ["system.subtitle"] = new[] { "Anti-Theft Password Protection System", "防盗模密码保护系统", "防盜模密碼保護系統", "盗難防止パスワード保護システム" },
            ["password.config"] = new[] { "Password Configuration", "密码配置", "密碼設定", "パスワード設定" },
            ["password.use_right_hand"] = new[] { "Use Right Hand", "使用右手输入", "使用右手輸入", "右手を使用" },
            ["password.use_right_hand_tooltip"] = new[] { "false=Left Hand, true=Right Hand", "false=左手, true=右手", "false=左手，true=右手", "false=左手、true=右手" },
            ["gesture.hold_time_tooltip"] = new[] { "Gesture stability detection time (seconds).\nHold the correct gesture continuously for this long before it is accepted. A wrong gesture restarts this step’s hold timer.\nMust be at least 0.01 s longer than error tolerance. The Inspector enforces this when editing.", "手势稳定检测时间（秒）。\n需要连续保持正确手势此时间后，才会被确认为有效输入；错误手势会重置当前步骤的保持计时。\n最小保持时间必须比容错时间至少长 0.01 秒，编辑时由 Inspector 强制限制。", "手勢穩定偵測時間（秒）。\n需要連續保持正確手勢此時間後，才會被確認為有效輸入；錯誤手勢會重設目前步驟的保持計時。\n最短保持時間必須比容錯時間至少長 0.01 秒，編輯時由 Inspector 強制限制。", "ジェスチャー安定検出時間（秒）。\n正しいジェスチャーをこの時間連続保持すると入力として認識されます。誤入力すると現在のステップの保持時間をリセットします。\nエラー許容時間より0.01秒以上長く設定します。編集時にInspectorで制限を適用します。" },
            ["gesture.error_tolerance_tooltip"] = new[] { "Gesture error tolerance time (seconds).\nCorrect a wrong gesture within this window to retain completed steps; otherwise the password input resets. The current step needs a fresh continuous hold.\nMust be shorter than minimum hold time.", "手势错误容错时间（秒）。\n输入错误手势后，在此时限内纠正可保留已完成的步骤，超时则输入重置；当前步骤需要重新连续保持正确手势。\n容错时间必须小于最小保持时间。", "手勢錯誤容錯時間（秒）。\n輸入錯誤手勢後，在此時限內修正可保留已完成的步驟，逾時則輸入重設；目前步驟需要重新連續保持正確手勢。\n容錯時間必須小於最短保持時間。", "ジェスチャーエラー許容時間（秒）。\nこの時間内に誤入力を修正すると完了済みステップを保持し、超えると最初から入力します。現在のステップは再度連続保持が必要です。\n最小保持時間より短く設定してください。" },
            ["password.gesture_tooltip"] = new[] { "Gesture password sequence, use 0–7 for VRChat gestures:\n0=Idle, 1=Fist, 2=HandOpen, 3=Fingerpoint\n4=Victory, 5=RockNRoll, 6=HandGun, 7=ThumbsUp", "手势密码序列，使用 0–7 表示 VRChat 手势：\n0=Idle, 1=Fist, 2=HandOpen, 3=Fingerpoint\n4=Victory, 5=RockNRoll, 6=HandGun, 7=ThumbsUp", "手勢密碼序列，使用 0–7 表示 VRChat 手勢：\n0=Idle, 1=Fist, 2=HandOpen, 3=Fingerpoint\n4=Victory, 5=RockNRoll, 6=HandGun, 7=ThumbsUp", "ジェスチャーパスワードシーケンス、0～7でVRChatジェスチャーを設定：\n0=Idle, 1=Fist, 2=HandOpen, 3=Fingerpoint\n4=Victory, 5=RockNRoll, 6=HandGun, 7=ThumbsUp" },
            ["gesture.config"] = new[] { "Gesture Recognition", "手势识别配置", "手勢辨識", "ジェスチャー認識設定" },
            ["gesture.hold_time"] = new[] { "Min Hold Time (sec)", "最小保持时间 (秒)", "最短保持時間（秒）", "最小保持時間 (秒)" },
            ["gesture.max_hold_time"] = new[] { "Max Hold Time (sec)", "最大保持时间 (秒)", "最長保持時間（秒）", "最大保持時間 (秒)" },
            ["gesture.max_hold_time_tooltip"] = new[] { "Maximum wait (seconds) after a password gesture is confirmed.\nSwitch to the next gesture within this time, or the password input resets.\nPrevents holding a confirmed gesture while thinking about the next step.", "密码手势确认后，等待下一步的最长时间（秒）。\n超过此时间仍未切换到下一手势，输入将重置。\n防止一直保持已确认的手势去思考下一步手势。", "密碼手勢確認後，等待下一步的最長時間（秒）。\n超過此時間仍未切換到下一手勢，輸入將重設。\n防止一直保持已確認的手勢去思考下一步手勢。", "パスワードのジェスチャーが確定した後の最大待機時間（秒）。\nこの時間内に次のジェスチャーへ切り替えないと入力がリセットされます。\n次のステップを考えながら確定済みジェスチャーを保持し続けるのを防ぎます。" },
            ["gesture.error_tolerance"] = new[] { "Error Tolerance (sec)", "容错时间 (秒)", "容錯時間（秒）", "エラー許容時間 (秒)" },
            ["gesture.invalid_stored_times"] = new[] { "These saved timing values need correction. Hold time must exceed tolerance by at least 0.01 s; maximum wait must allow the hold and tolerance. Opening this Inspector keeps the saved values. Edit a time or choose Correct Times to apply the limits.", "已保存的时间参数需要修正：最小保持时间必须比容错时间至少长 0.01 秒，最大等待时间须容纳保持与容错。打开 Inspector 保留原值；编辑时间或点击「修正时间」时应用限制。", "已儲存的時間參數需要修正：最短保持時間必須比容錯時間至少長 0.01 秒，最長等待時間須容納保持與容錯。開啟 Inspector 保留原值；編輯時間或按「修正時間」時套用限制。", "保存された時間設定の修正が必要です。保持時間は許容時間より0.01秒以上長くし、最大待機時間は保持時間と許容時間を含める必要があります。Inspectorを開くだけでは保存値を変更しません。時間を編集するか「時間を修正」を選ぶと制限を適用します。" },
            ["gesture.correct_times"] = new[] { "Correct Times", "修正时间", "修正時間", "時間を修正" },
            ["gesture.edit_times"] = new[] { "Edit ASS Gesture Times", "修改 ASS 手势时间", "修改 ASS 手勢時間", "ASSジェスチャー時間を編集" },
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
            ["password.empty_warning"] = new[] { "Password is empty (0 digits).\nASS is disabled and will not be generated.", "密码为空（0位）。\nASS 已禁用，不会生成保护系统。", "密碼為空（0 位）。\nASS 已停用，不會生成保護系統。", "パスワードが空です（0桁）。\nASSは無効化され、保護システムは生成されません。" },
            ["countdown.config"] = new[] { "Countdown Configuration", "倒计时配置", "倒數計時", "カウントダウン設定" },
            ["countdown.duration"] = new[] { "Countdown Duration (sec)", "倒计时时长 (秒)", "倒數時間（秒）", "カウントダウン時間 (秒)" },
            ["countdown.duration_tooltip"] = new[] { "Password input time limit (seconds).\nEnabled defense mechanisms are triggered after timeout.", "输入密码的倒计时时长（秒）。\n超时后触发已启用的防御。", "輸入密碼的倒數時間（秒）。\n逾時後觸發已啟用的防禦。", "パスワード入力の制限時間（秒）。\nタイムアウト後、有効な防御が発動します。" },
            ["defense.options"] = new[] { "Defense Options", "防御选项", "防禦選項", "防御オプション" },
            ["advanced.play_mode"] = new[] { "Enable in Play Mode", "播放模式中启用", "播放模式中啟用", "プレイモードで有効化" },
            ["advanced.play_mode_tooltip"] = new[] { "Preview the ASS password input workflow in the VRC SDK’s Play Mode.\nNo defense objects are generated during preview; normal builds generate defense according to the configuration. Requires the VRChat SDK.", "在 VRC SDK 的播放模式中预览 ASS 密码输入流程。\n预览时不生成防御对象；正式构建按配置生成防御。需要安装 VRChat SDK。", "在 VRC SDK 的播放模式中預覽 ASS 密碼輸入流程。\n預覽時不生成防禦物件；正式建置依設定生成防禦。需要安裝 VRChat SDK。", "VRC SDKのプレイモードでASSのパスワード入力をプレビューします。\nプレビューでは防御オブジェクトを生成せず、通常のビルドでは設定に従って防御を生成します。VRChat SDKが必要です。" },
            ["defense.disable_defense"] = new[] { "Disable Defense", "禁用防御", "停用防禦", "防御を無効化" },
            ["defense.disable_defense_tooltip"] = new[] { "Do not generate defense mechanisms (only test the password system).\nDisabled while 'Default Enable Defense' is active.", "不生成防御机制（仅测试密码系统）。\n启用「默认启用防御」时此选项不可用。", "不生成防禦機制（僅測試密碼系統）。\n啟用「預設啟用防禦」時此選項不可用。", "防御メカニズムを生成しない（パスワードシステムのみテスト）。\n「デフォルトで防御を有効化」が有効な場合は使用できません。" },
            ["advanced.disable_overlay"] = new[] { "Disable Overlay Interface", "关闭覆盖界面", "關閉覆蓋介面", "オーバーレイ表示を無効化" },
            ["advanced.disable_overlay_tooltip"] = new[] { "Do not generate the fullscreen overlay\n(white background + progress bar + logo).\nAudio feedback is still generated.", "不生成全屏覆盖\n（白色背景 + 进度条 + 徽标）。\n音频反馈仍然会生成。", "不生成全螢幕覆蓋\n（白色背景 + 進度條 + 徽標）。\n音訊回饋仍然會生成。", "フルスクリーンオーバーレイを生成しない\n（白背景+プログレスバー+ロゴ）。\nオーディオフィードバックは引き続き生成されます。" },
            ["advanced.mute_warning"] = new[] { "Disable Warning Sound", "关闭警告音效", "關閉倒數警告音", "警告音を無効化" },
            ["advanced.mute_warning_tooltip"] = new[] { "Do not generate the countdown warning beep.\nThe fullscreen overlay (mask + progress bar) is still shown unless disabled separately.\nPassword success audio still plays.", "不生成倒计时警告音效。\n全屏覆盖（遮罩 + 进度条）仍然显示，可通过「关闭覆盖界面」单独关闭。\n密码正确时仍会播放提示音。", "不生成倒數警告音效。\n全螢幕覆蓋（遮罩 + 進度條）仍然顯示，可透過「關閉覆蓋介面」另外關閉。\n密碼正確時仍會播放提示音。", "カウントダウン警告音を生成しません。\n全画面オーバーレイ（マスク + プログレスバー）は別途無効にしない限り表示されます。\nパスワード成功音は引き続き再生されます。" },
            ["advanced.default_defense"] = new[] { "Default Enable Defense", "默认启用防御", "預設啟用防禦", "デフォルトで防御を有効化" },
            ["advanced.default_defense_tooltip"] = new[] { "Defense is always ON. No password input.\n\nUse after uploading and unlocking a\npassword-protected version in VRChat.", "防御始终开启，无需密码输入。\n\n请先在 VRChat 中上传并解锁\n带密码保护的版本后再使用。", "防禦始終開啟，無需輸入密碼。\n\n請先在 VRChat 中上傳並解鎖\n帶密碼保護的版本後再使用。", "防御が常に有効。パスワード入力不要。\n\nVRChatでパスワード保護版をアップロード\nしてロック解除した後で使用してください。" },
            ["advanced.default_defense_warning"] = new[] { "DANGER: No password system will be generated.\n\nWorkflow:\n1. Upload a password-protected version first\n2. Unlock it in VRChat\n3. Re-upload with this option enabled\n\nSkipping step 2 will trigger defense for ALL users\nincluding yourself!", "危险：启用后不会生成密码系统。\n\n正确流程：\n1. 先上传带密码保护的版本\n2. 在 VRChat 中解锁\n3. 再上传启用此选项的版本\n\n跳过第 2 步会导致防御对所有人生效\n（包括你自己）！", "危險：啟用後不會生成密碼系統。\n\n正確流程：\n1. 先上傳帶密碼保護的版本\n2. 在 VRChat 中解鎖\n3. 再上傳啟用此選項的版本\n\n跳過第 2 步會導致防禦對所有人生效\n（包括你自己）！", "危険：パスワードシステムは生成されません。\n\nワークフロー：\n1. パスワード保護版をアップロード\n2. VRChatでロック解除\n3. このオプションを有効にして再アップロード\n\n手順2をスキップすると自分を含む\n全ユーザーに防御が作動します！" },
            ["defense.lightweight_defense"] = new[] { "Lightweight Mode", "轻量模式", "輕量模式", "軽量モード" },
            ["defense.lightweight_defense_tooltip"] = new[] { "Green-oriented mode.\n\n• Minimizes newly generated particle systems\n• Does not proactively introduce new lights / Collision / Trails\n• If the avatar already uses these features, ASS-generated particles inherit them instead of forcing them off", "偏向绿模的模式。\n\n• 尽量减少新生成的粒子系统\n• 不会主动新增光源 / 粒子碰撞 / 拖尾\n• 如果 Avatar 本来就在使用这些功能，ASS 生成的粒子会继承对应特性，而不是强行关闭", "偏向綠模的模式。\n\n• 盡量減少新生成的粒子系統\n• 不會主動新增光源 / 粒子碰撞 / 拖尾\n• 如果 Avatar 原本就在使用這些功能，ASS 生成的粒子會繼承對應特性，而不是強行關閉", "できるだけグリーン評価を維持するモードです。\n\n• 新規生成する ParticleSystem を最小限に抑える\n• 新しい Light / Collision / Trails は追加しない\n• アバターが既にこれらを使っている場合は、ASS 生成の粒子も対応機能を継承する" },
            ["defense.lightweight_defense_note"] = new[] { "Lightweight Mode summary:\n• Goal: stay as close to green as possible while keeping defense active\n• New lights / Collision / Trails are not introduced proactively\n• If the avatar already uses these rank-sensitive features, ASS-generated particles inherit them\n• Uses 1 particle system and fills the remaining particle / mesh budget as tightly as possible without exceeding the cap", "轻量模式摘要：\n• 目标是在保留防御效果的前提下，尽量贴近绿模\n• 不会主动新增光源 / 粒子碰撞 / 拖尾\n• 如果 Avatar 本来就在使用这些性能敏感特性，ASS 生成的粒子会继承它们\n• 使用 1 个粒子系统，尽量把剩余粒子 / 面数预算补满，但不超限", "輕量模式摘要：\n• 目標是在保留防禦效果的前提下，盡量貼近綠模\n• 不會主動新增光源 / 粒子碰撞 / 拖尾\n• 如果 Avatar 原本就在使用這些效能敏感特性，ASS 生成的粒子會繼承它們\n• 使用 1 個粒子系統，盡量把剩餘粒子 / 面數預算補滿，但不超限", "軽量モード概要：\n• 防御を維持しつつ、できるだけグリーン評価に近づけることが目的\n• 新しい Light / Collision / Trails は積極的に追加しない\n• アバターが既にこれらの性能影響機能を使っている場合は、ASS 生成の粒子も継承する\n• 1 システムのみで残りの粒子数 / ポリゴン予算を、上限を超えない範囲で可能な限り埋めます" },
            ["advanced.options"] = new[] { "Advanced Options", "高级选项", "進階選項", "詳細オプション" },
            ["advanced.disable_objects_tooltip"] = new[] { "Retained for legacy serialized configurations. Avatar object locking was removed; this value no longer generates scale or visibility animation.", "仅保留用于读取旧序列化配置。Avatar对象锁定已移除，此值不再生成缩放或隐藏动画。", "保留舊版序列化資料用的欄位。Avatar 物件鎖定已移除，此值不再生成縮放或隱藏動畫。", "旧設定の読み込み用フィールドです。アバター本体のロックは廃止され、拡縮や非表示のアニメーションは生成しません。" },
            ["advanced.wd_mode_tooltip"] = new[] { "Retained for legacy serialized configurations. The generator manages Write Defaults for its own states.", "仅保留用于读取旧序列化配置；生成器处理自身状态的Write Defaults，无需在此设置。", "保留舊版序列化資料用的欄位；生成器處理自身狀態的 Write Defaults，不需要在此設定。", "旧設定の読み込み用フィールドです。生成するステートのWrite Defaultsは生成器が処理します。" },
            ["advanced.obfuscation_section"] = new[] { "Obfuscation Options", "混淆选项", "混淆選項", "難読化オプション" },
            ["advanced.disable_obfuscation"] = new[] { "Disable Obfuscation", "禁用混淆", "停用混淆", "難読化を無効化" },
            ["advanced.disable_obfuscation_tooltip"] = new[] { "Use original parameter/layer/object names in the build.\nOnly recommended for debugging.", "在构建中使用原始参数/层/对象名称。\n仅建议用于调试。", "在建置中使用原始參數 / 圖層 / 物件名稱。\n僅建議用於除錯。", "ビルドで元のパラメータ/レイヤー/オブジェクト名を使用。\nデバッグ時のみ推奨。" },
            ["advanced.obfuscate_playable_layers"] = new[] { "Obfuscate All Playable Layers", "混淆全部 Playable Layer", "混淆全部 Playable Layer", "すべてのPlayable Layerを難読化" },
            ["advanced.obfuscate_playable_layers_tooltip"] = new[] { "During the late VRChat build callback, obfuscate custom Playable controller copies. Original project assets stay unchanged.\nWith Contact components, keep third-party graph names for compatibility; ASS-owned obfuscation still applies.", "在 VRChat 构建的末期回调中混淆自定义 Playable Controller 副本，原项目资源保持。\n存在 Contact 组件时，为兼容保留第三方图名称；ASS 自身的混淆仍生效。", "在 VRChat 建置末期回呼中混淆自訂 Playable Controller 副本，原專案資源保持。\n存在 Contact 元件時，為相容保留第三方圖名稱；ASS 自身的混淆仍生效。", "VRChatビルドの最終コールバックでPlayable Controllerのコピーを難読化します。元アセットは保持します。\nContactがある場合は互換性のため外部グラフ名を保持し、ASS自身の難読化は適用します。" },
            ["advanced.decoy_layers"] = new[] { "Animation Layer Noise", "动画层噪声", "動畫圖層噪聲", "アニメーションレイヤーノイズ" },
            ["advanced.decoy_layers_tooltip"] = new[] { "Add an extra animation layer to the controller for obfuscation.\nDoes not change avatar appearance.", "向动画控制器添加一个额外的动画层，用于混淆。\n不会改变 Avatar 外观。", "向動畫控制器新增一個額外的動畫圖層，用於混淆。\n不會改變 Avatar 外觀。", "難読化のため、コントローラーにアニメーションレイヤーを追加します。\nアバターの外観は変更しません。" },
            ["advanced.decoy_states"] = new[] { "Animation State Noise", "动画状态噪声", "動畫狀態噪聲", "アニメーションステートノイズ" },
            ["advanced.decoy_states_tooltip"] = new[] { "Add extra animation states to existing layers for obfuscation.\nDoes not change avatar appearance.", "向现有动画层添加额外的动画状态，用于混淆。\n不会改变 Avatar 外观。", "向現有動畫圖層新增額外的動畫狀態，用於混淆。\n不會改變 Avatar 外觀。", "難読化のため、既存のレイヤーにアニメーションステートを追加します。\nアバターの外観は変更しません。" },
            ["advanced.no_feedback_warning"] = new[] { "Overlay and warning sound are both disabled.\nNo visual/audio cue during password input.\n(Password success sound still plays.)\nEnabled defense still triggers after countdown expires.", "已关闭覆盖界面和警告音效。\n输入密码时将没有视觉和声音提示。\n（密码正确时仍会播放提示音。）\n倒计时结束后仍会触发已启用的防御。", "已關閉覆蓋介面和警告音效。\n輸入密碼時將沒有視覺和聲音提示。\n（密碼正確時仍會播放提示音。）\n倒數結束後仍會觸發已啟用的防禦。", "オーバーレイと警告音が両方無効です。\nパスワード入力中の視覚・音声の手がかりはありません。\n（パスワード成功音は再生されます。）\nカウントダウン終了後も有効な防御は作動します。" },
            ["language.traditional_chinese"] = new[] { "Traditional Chinese", "繁体中文", "繁體中文", "中国語（繁体）" },
            ["gesture.continuous_hold_help"] = new[] { "Each step needs one uninterrupted correct hold. Wrong input restarts that step’s timer. Error tolerance is shorter than minimum hold; repeated gestures need a release between steps.", "每一步都需要连续保持正确手势；错误输入会重置该步计时。容错时间小于最小保持时间，相邻重复手势必须先释放再保持。", "每一步都需要連續保持正確手勢；錯誤輸入會重設該步計時。容錯時間小於最短持續時間，相鄰重複手勢必須先釋放再保持。", "各ステップで正しいジェスチャーを連続保持します。誤入力で保持時間をリセットし、許容時間は最小保持時間より短くなります。同じジェスチャーが続く場合は一度離します。" },
            ["feedback.options"] = new[] { "Feedback", "反馈", "回饋", "フィードバック" },
            ["build.standalone_help"] = new[] { "In ordinary Unity projects, supply gesture password input through StandaloneSecurityInput.SetGesture.", "普通 Unity 项目中，通过 StandaloneSecurityInput.SetGesture 输入手势密码。", "一般 Unity 專案中，透過 StandaloneSecurityInput.SetGesture 輸入手勢密碼。", "通常のUnityプロジェクトでは、StandaloneSecurityInput.SetGestureでジェスチャーパスワードを入力します。" },
            ["build.vrchat_only"] = new[] { "These options apply to the optional VRChat build integration. Ordinary Unity builds use gestures, timing and feedback.", "这些选项用于可选的VRChat构建集成；普通Unity构建使用手势、计时与反馈。", "這些選項用於選用的 VRChat 建置整合；一般 Unity 建置使用手勢、計時與回饋。", "これらの設定はオプションのVRChatビルド連携で使用します。通常のUnityビルドはジェスチャー・時間制御・フィードバックを生成します。" },
            ["build.duplicate_configuration"] = new[] { "More than one ASS configuration targets {0}. Keep one configuration on that root.", "多个ASS配置指向{0}，请在该根对象上保留一个配置。", "多個 ASS 設定指向 {0}，請在該根物件保留一個設定。", "複数のASS設定が{0}を対象にしています。そのルートには1つの設定を残してください。" },
            ["build.invalid_output_path"] = new[] { "Choose a .controller output path below Assets.", "请选择Assets目录下的.controller输出路径。", "請選擇 Assets 目錄下的 .controller 輸出路徑。", "Assets配下の.controller出力先を指定してください。" },
            ["build.invalid_password"] = new[] { "Set at least one gesture from 0 through 7.", "请设置至少一个0–7范围内的手势。", "請設定至少一個 0–7 範圍內的手勢。", "0～7のジェスチャーを1つ以上設定してください。" },
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
