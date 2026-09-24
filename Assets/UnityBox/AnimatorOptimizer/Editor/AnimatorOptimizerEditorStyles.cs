using System;
using UnityEditor;
using UnityEngine;

namespace UnityBox.AnimatorOptimizer
{
    internal static class AnimatorOptimizerEditorStyles
    {
        private static GUIStyle badge;
        private static GUIStyle sectionHeader;
        private static GUIStyle detail;
        private static GUIStyle detailError;
        private static GUIStyle detailSuccess;
        private static GUIStyle detailWarning;
        private static GUIStyle summaryValue;
        private static GUIStyle summaryLabel;
        private static GUIStyle searchField;
        private static GUIStyle clippedLabel;
        private static GUIStyle clippedTitle;
        private static GUIStyle clippedFoldout;
        private static bool? proSkin;

        public static void EnsureSkin()
        {
            if (proSkin == EditorGUIUtility.isProSkin) return;
            proSkin = EditorGUIUtility.isProSkin;
            badge = sectionHeader = detail = detailError = detailSuccess = detailWarning = null;
            summaryValue = summaryLabel = searchField = clippedLabel = clippedTitle = clippedFoldout = null;
        }

        public sealed class LabelWidthScope : IDisposable
        {
            private readonly float previous = EditorGUIUtility.labelWidth;
            public LabelWidthScope(float width) { EditorGUIUtility.labelWidth = width; }
            public void Dispose() { EditorGUIUtility.labelWidth = previous; }
        }

        public static readonly Color ConvertibleColor = new Color(0.36f, 0.72f, 0.42f);
        public static readonly Color UnsupportedColor = new Color(0.55f, 0.55f, 0.55f);
        public static readonly Color WarningColor = new Color(0.90f, 0.66f, 0.20f);
        public static readonly Color ErrorColor = new Color(0.86f, 0.36f, 0.36f);
        public static readonly Color AccentColor = new Color(0.33f, 0.60f, 0.92f);

        public static GUIStyle Badge => badge ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
            padding = new RectOffset(6, 6, 1, 1),
            margin = new RectOffset(2, 2, 2, 2),
            clipping = TextClipping.Clip,
        };

        public static float BadgeHeight => Mathf.Max(EditorGUIUtility.singleLineHeight + 2f, Badge.CalcSize(new GUIContent("Ag 中文")).y);

        public static GUIStyle ClippedLabel => clippedLabel ??= new GUIStyle(EditorStyles.miniLabel)
        {
            clipping = TextClipping.Clip, wordWrap = false,
        };

        public static GUIStyle ClippedTitle => clippedTitle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            clipping = TextClipping.Clip, wordWrap = false,
        };

        public static GUIStyle ClippedFoldout => clippedFoldout ??= new GUIStyle(EditorStyles.foldout)
        {
            clipping = TextClipping.Clip, wordWrap = false,
        };

        public static GUIStyle SectionHeader => sectionHeader ??= new GUIStyle(EditorStyles.foldoutHeader)
        {
            fontStyle = FontStyle.Bold,
        };

        public static GUIStyle Detail => detail ??= new GUIStyle(EditorStyles.wordWrappedMiniLabel)
        {
            padding = new RectOffset(18, 4, 1, 1),
        };

        public static GUIStyle DetailError => detailError ??= new GUIStyle(Detail)
        {
            normal = { textColor = EditorGUIUtility.isProSkin ? ErrorColor : new Color(0.65f, 0.16f, 0.16f) },
        };

        public static GUIStyle DetailSuccess => detailSuccess ??= new GUIStyle(Detail)
        {
            normal = { textColor = EditorGUIUtility.isProSkin ? ConvertibleColor : new Color(0.12f, 0.42f, 0.20f) },
        };

        public static GUIStyle DetailWarning => detailWarning ??= new GUIStyle(Detail)
        {
            normal = { textColor = EditorGUIUtility.isProSkin ? WarningColor : new Color(0.52f, 0.32f, 0.04f) },
        };

        public static GUIStyle SummaryValue => summaryValue ??= new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 16,
            alignment = TextAnchor.MiddleCenter,
        };

        public static GUIStyle SummaryLabel => summaryLabel ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel);

        public static GUIStyle SearchField => searchField ??= GUI.skin.FindStyle("ToolbarSearchTextField") ??
                                                            GUI.skin.FindStyle("ToolbarSeachTextField") ??
                                                            EditorStyles.toolbarTextField;

        public static Color KindColor(AnimatorOptimizerConversionKind kind)
        {
            switch (kind)
            {
                case AnimatorOptimizerConversionKind.ConstantMotion:
                    return new Color(0.40f, 0.62f, 0.85f);
                case AnimatorOptimizerConversionKind.BooleanTruthTable:
                    return new Color(0.36f, 0.72f, 0.42f);
                case AnimatorOptimizerConversionKind.NumericSelector:
                    return new Color(0.55f, 0.66f, 0.30f);
                case AnimatorOptimizerConversionKind.FiniteParameterTable:
                    return new Color(0.28f, 0.68f, 0.62f);
                case AnimatorOptimizerConversionKind.AapStateRegister:
                    return new Color(0.72f, 0.48f, 0.80f);
                default:
                    return UnsupportedColor;
            }
        }

        public static string KindLabel(AnimatorOptimizerConversionKind kind)
        {
            switch (kind)
            {
                case AnimatorOptimizerConversionKind.ConstantMotion:
                    return "单一输出";
                case AnimatorOptimizerConversionKind.BooleanTruthTable:
                    return "Bool 决策";
                case AnimatorOptimizerConversionKind.NumericSelector:
                    return "数值选择";
                case AnimatorOptimizerConversionKind.FiniteParameterTable:
                    return "混合决策";
                case AnimatorOptimizerConversionKind.AapStateRegister:
                    return "AAP 状态";
                default:
                    return "保留";
            }
        }

        public static void DrawBadge(string text, Color color, float minWidth = 0f)
        {
            var content = new GUIContent(text);
            var width = Mathf.Max(minWidth, Badge.CalcSize(content).x);
            var rect = GUILayoutUtility.GetRect(width, BadgeHeight, GUILayout.Width(width));
            DrawBadge(rect, text, color);
        }

        public static void DrawBadge(Rect rect, string text, Color color)
        {
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, 0f, 4f);
            }
            GUI.Label(rect, new GUIContent(Ellipsize(text, Badge, rect.width), text), Badge);
        }

        public static string Ellipsize(string text, GUIStyle style, float width)
        {
            text ??= string.Empty;
            if (width <= 0f) return string.Empty;
            if (style.CalcSize(new GUIContent(text)).x <= width) return text;
            const string suffix = "…";
            var low = 0;
            var high = text.Length;
            while (low < high)
            {
                var middle = low + (high - low + 1) / 2;
                if (style.CalcSize(new GUIContent(text.Substring(0, middle) + suffix)).x <= width) low = middle;
                else high = middle - 1;
            }
            if (low > 0 && char.IsHighSurrogate(text[low - 1])) low--;
            return text.Substring(0, low) + suffix;
        }

        public static void ClippedText(string text, GUIStyle style = null, string tooltip = null)
        {
            style ??= ClippedLabel;
            var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight,
                GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true));
            EditorGUI.LabelField(rect, new GUIContent(Ellipsize(text, style, rect.width), tooltip ?? text), style);
        }

        // Use measured width, not character count: CJK labels and custom fonts
        // must not collide with the value field in a narrow docked Inspector.
        public static GUIContent FieldLabel(string label, string tooltip = null, float minimumFieldWidth = 90f)
        {
            var content = new GUIContent(label, tooltip ?? label);
            var available = Mathf.Max(0f, EditorGUIUtility.currentViewWidth - 48f);
            if (EditorStyles.label.CalcSize(content).x + 8f > Mathf.Min(EditorGUIUtility.labelWidth, available - minimumFieldWidth))
            {
                EditorGUILayout.LabelField(content, EditorStyles.wordWrappedMiniLabel);
                return GUIContent.none;
            }
            return content;
        }

        public static bool Section(string key, string title, bool defaultOpen = true, string subtitle = null)
        {
            var prefKey = "UnityBox.AnimatorOptimizer.Section." + key;
            var open = SessionState.GetBool(prefKey, defaultOpen);
            var content = string.IsNullOrEmpty(subtitle) ? title : title + "  ·  " + subtitle;
            var next = EditorGUILayout.BeginFoldoutHeaderGroup(open, content, SectionHeader);
            EditorGUILayout.EndFoldoutHeaderGroup();
            if (next != open)
                SessionState.SetBool(prefKey, next);
            return next;
        }

        public static void Separator()
        {
            var rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.08f) : new Color(0f, 0f, 0f, 0.12f));
        }

        public static string BuildOptionsKey(AnimatorOptimizerOptions options)
        {
            if (options == null)
                return string.Empty;
            try
            {
                return JsonUtility.ToJson(options);
            }
            catch (Exception)
            {
                return options.GetHashCode().ToString();
            }
        }

        public static AnimatorOptimizerComponent.HeuristicFlags DrawHeuristics(
            AnimatorOptimizerComponent.HeuristicFlags flags, string context, out bool showDetails)
        {
            var previous = flags;
            var previousChanged = GUI.changed;
            var preset = flags == AnimatorOptimizerComponent.HeuristicFlags.None ? 0 :
                flags == AnimatorOptimizerOptions.PracticalHeuristics ? 1 :
                flags == AnimatorOptimizerOptions.AggressiveHeuristics ? 2 : 3;
            var next = EditorGUILayout.Popup(FieldLabel("优化方式", "只调整五项启发式，不改变 WD、过渡时序或冲突检查。"), preset,
                new[] { "仅自动证明", "实用启发式", "激进启发式", "自定义…" });
            var detailsKey = "HeuristicDetails." + context;
            if (next != preset)
            {
                if (next == 0) flags = AnimatorOptimizerComponent.HeuristicFlags.None;
                else if (next == 1) flags = AnimatorOptimizerOptions.PracticalHeuristics;
                else if (next == 2) flags = AnimatorOptimizerOptions.AggressiveHeuristics;
                else SessionState.SetBool("UnityBox.AnimatorOptimizer.Section." + detailsKey, true);
            }
            var summary = flags == AnimatorOptimizerComponent.HeuristicFlags.None
                ? "五项启发式已关闭；额外放宽设置保持不变。"
                : flags == AnimatorOptimizerOptions.PracticalHeuristics
                ? "0/1 Float 开关 + 短动画端点；可能改变连续输入和动画过程。"
                : flags == AnimatorOptimizerOptions.AggressiveHeuristics
                ? "再启用时间采样、双态补集和稳定输入收敛；不保证逐帧等价。"
                : "使用自定义策略。实际采用的假设会在逐层诊断中列出。";
            EditorGUILayout.LabelField(summary, EditorStyles.wordWrappedMiniLabel);
            showDetails = Section(detailsKey, "自定义策略与采样预算", false);
            if (!showDetails)
            {
                GUI.changed = previousChanged || flags != previous;
                return flags;
            }
            void Toggle(AnimatorOptimizerComponent.HeuristicFlags flag, string label, string tooltip)
            {
                var enabled = EditorGUILayout.Toggle(FieldLabel(label, tooltip, 24f), (flags & flag) != 0);
                flags = enabled ? flags | flag : flags & ~flag;
            }
            Toggle(AnimatorOptimizerComponent.HeuristicFlags.BinaryFloatToggles, "Float 开关假定 0/1",
                "仅识别默认值为 0/1、阈值处于 0~1 且两端判断不同的非全局 Float 参数。不是外部 writer 值域证明；连续输入可能改变效果。");
            Toggle(AnimatorOptimizerComponent.HeuristicFlags.ShortAnimationEndpoints, "短动画取端点 / 零速固定",
                "普通属性非循环 Clip：正向取末帧、反向或零速取首帧；同时限制原动画帧数和实际播放秒数。动画中间过程会消失。");
            Toggle(AnimatorOptimizerComponent.HeuristicFlags.SampleMotionTime, "Motion Time 采样树",
                "把普通属性 Clip 的时间参数转为 1D 采样树，补入关键时间，保留时间参数读取顺序；缓动区间为线性近似，不处理变化的对象引用或离散曲线。");
            Toggle(AnimatorOptimizerComponent.HeuristicFlags.ComplementaryToggle, "双态返回条件按补集补齐",
                "仅用于同参数、反向条件的简单双态图，以默认态出发条件为准。可能取消阈值空档、重叠和有意设计的滞回。");
            Toggle(AnimatorOptimizerComponent.HeuristicFlags.StableInputConvergence, "稳定输入下消除中间状态",
                "只有每个源状态最终收敛到相同输出才放行；假定输入暂时不变，直接输出终态。不是逐帧等价，仍拒绝输出振荡环。");
            GUI.changed = previousChanged || flags != previous;
            return flags;
        }

        public static void DrawAapSummary()
        {
            EditorGUILayout.HelpBox("AAP 支持混合条件和状态保持。每个源层生成更新 + 输出两层，不保证减层或提速；反馈延迟与首帧行为尚未进行 Unity 验证。", MessageType.Warning);
            EditorGUILayout.LabelField("构建时由 VRCSDK 回退流程处理；仅运行 NDMF 的预览不会执行 AAP 转换。", EditorStyles.wordWrappedMiniLabel);
        }

    }
}
