using UnityEngine;

namespace XFramework.XUI.View
{
    /// <summary>
    /// UI 根节点。挂载在场景中的 Canvas（或包含多个 Canvas 的根 GameObject）上。
    /// <para>Awake 时自动初始化 <see cref="UIManager"/>，Destroy 时自动销毁。</para>
    /// <para>每个场景只需放置一个 UIRootNode。</para>
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class UIRootNode : MonoBehaviour
    {
        #region Fields

        /// <summary>
        /// 层级常量。数值越大越靠前。可在 Inspector 中调整基值，或通过代码扩展新层级。
        /// <para>默认: Background = 0, Default = 100, Popup = 200, Top = 300, Mask = 500</para>
        /// </summary>
        [Header("Layer Constants")]
        [Tooltip("背景层（如主界面背景、HUD）。")]
        public int layerBackground = 0;

        [Tooltip("默认层（大部分面板使用）。")]
        public int layerDefault = 100;

        [Tooltip("弹出层（弹窗、确认框）。")]
        public int layerPopup = 200;

        [Tooltip("顶层（Toast、加载遮罩、系统提示）。")]
        public int layerTop = 300;

        [Tooltip("模态遮罩层。")]
        public int layerMask = 500;

        #endregion

        #region Lifecycle

        private void Awake()
        {
            // 设置 UIRoot 的 Canvas 为基础 Canvas
            var canvas = GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            // 初始化 UIManager
            if (!UIManager.IsInitialized)
            {
                UIManager.Initialize(transform);
            }
        }

        private void OnDestroy()
        {
            // 销毁 UIManager
            if (UIManager.IsInitialized)
            {
                UIManager.Destroy();
            }
        }

        #endregion

        #region Convenience Methods

        /// <summary>
        /// 获取当前场景中第一个 UIRootNode 实例。
        /// </summary>
        public static UIRootNode FindInScene()
        {
            return Object.FindObjectOfType<UIRootNode>();
        }

        /// <summary>
        /// 获取场景中的 UI 根 Transform。如果没有 UIRootNode 则返回 null。
        /// </summary>
        public static Transform GetUIRoot()
        {
            var node = FindInScene();
            return node != null ? node.transform : null;
        }

        #endregion
    }
}