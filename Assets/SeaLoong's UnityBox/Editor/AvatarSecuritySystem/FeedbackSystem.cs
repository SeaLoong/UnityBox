using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.Constants;
using static SeaLoongUnityBox.AvatarSecuritySystem.Editor.I18n;

namespace SeaLoongUnityBox.AvatarSecuritySystem.Editor
{
    /// <summary>
    /// 视觉和音频反馈系统生成器
    /// 功能：倒计时警告、错误提示、成功反馈、HUD显示
    /// </summary>
    public static class FeedbackSystem
    {
        #region HUD Canvas创建

        /// <summary>
        /// 创建HUD Canvas用于显示倒计时和状态
        /// </summary>
        public static GameObject CreateHUDCanvas(GameObject avatarRoot, AvatarSecuritySystemComponent config)
        {
            // 查找或创建Canvas
            Transform existingCanvas = avatarRoot.transform.Find(Constants.GO_UI_CANVAS);
            if (existingCanvas != null)
            {
                Debug.Log(I18n.T("log.visual_existing_canvas"));
                return existingCanvas.gameObject;
            }

            // 创建Canvas GameObject（始终作为avatarRoot的直接子对象）
            var canvasObj = new GameObject(Constants.GO_UI_CANVAS);
            canvasObj.transform.SetParent(avatarRoot.transform, false);
            canvasObj.SetActive(true); // 确保Canvas默认启用

            // 添加Canvas组件 - 使用Screen Space Camera模式
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            // 相机会在运行时自动设置为主相机
            
            // 添加CanvasScaler
            var scaler = canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            // 添加GraphicRaycaster（UI交互需要）
            canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            Debug.Log(I18n.T("log.visual_canvas_created"));
            return canvasObj;
        }

        /// <summary>
        /// 创建倒计时进度条（显示剩余时间）
        /// 使用Image.fillAmount实现平滑过渡的进度条效果
        /// </summary>
        public static GameObject CreateCountdownBar(GameObject canvasObj, AvatarSecuritySystemComponent config)
        {
            // 创建容器
            var containerObj = new GameObject("CountdownBar");
            containerObj.transform.SetParent(canvasObj.transform, false);
            containerObj.SetActive(true);

            var containerRect = containerObj.AddComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.5f, 0.05f); // 屏幕下方
            containerRect.anchorMax = new Vector2(0.5f, 0.05f);
            containerRect.pivot = new Vector2(0.5f, 0.5f);
            containerRect.anchoredPosition = Vector2.zero;
            containerRect.sizeDelta = new Vector2(600, 40);

            // 创建背景（黑色半透明）
            var bgObj = new GameObject("Background");
            bgObj.transform.SetParent(containerObj.transform, false);
            var bgImage = bgObj.AddComponent<UnityEngine.UI.Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.5f);
            
            var bgRect = bgObj.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
            bgRect.anchoredPosition = Vector2.zero;

            // 创建进度条（浅红色）
            var barObj = new GameObject("Bar");
            barObj.transform.SetParent(containerObj.transform, false);
            var barImage = barObj.AddComponent<UnityEngine.UI.Image>();
            barImage.color = new Color(1f, 0.3f, 0.3f, 0.9f); // 浅红色

            var barRect = barObj.GetComponent<RectTransform>();
            // 使用anchor来控制宽度：左边固定，右边从0到1变化
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(1f, 1f); // 初始为满（右边在最右侧）
            barRect.sizeDelta = new Vector2(-4, -4); // 留出边框
            barRect.anchoredPosition = Vector2.zero;

            Debug.Log(I18n.T("log.visual_countdown_created"));
            return containerObj;
        }

        #endregion
    }
}
