using UnityEngine;

namespace XFramework.XUI.View
{
    /// <summary>
    /// UI 根节点。挂载在场景中的 Canvas（或包含多个 Canvas 的根 GameObject）上。
    /// <para>Awake 时自动初始化 <see cref="UIManager"/>，Destroy 时自动销毁。</para>
    /// <para>每个场景只需放置一个 UIRootNode。</para>
    /// <para>面板的每帧更新由 <see cref="UIManager"/> 注册进 <c>UpdateManager</c> 统一调度，
    /// 本类不再参与每帧驱动——它只负责生命周期与层级参数。</para>
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class UIRootNode : MonoBehaviour
    {
        #region Fields

        /// <summary>
        /// 层级参考值（编辑器便利字段）。
        /// <para><b>这些字段不被运行时读取</b>：UI 是静态服务，不依赖场景组件。真正的推荐值在
        /// <see cref="UILayers"/> 里，这里只是把它们摆到 Inspector 上方便对照与复制。
        /// 面板实际用哪个层级由调用方在 <c>OpenAsync(path, layer)</c> 时决定。</para>
        /// </summary>
        [Header("Layer Reference Values")]
        [Tooltip("背景层（如主界面背景）。对应 UILayers.Background。")]
        public int layerBackground = UILayers.Background;

        [Tooltip("默认层（大部分面板使用）。对应 UILayers.Default。")]
        public int layerDefault = UILayers.Default;

        [Tooltip("弹出层（弹窗、确认框）。对应 UILayers.Popup。")]
        public int layerPopup = UILayers.Popup;

        [Tooltip("顶层（Toast、系统提示）。对应 UILayers.Top。")]
        public int layerTop = UILayers.Top;

        [Tooltip("模态遮罩层。对应 UILayers.Mask，也是 ShowMask 的默认值。")]
        public int layerMask = UILayers.Mask;

        /// <summary>
        /// 勾选后自动在本物体上挂一个 <see cref="UISafeArea"/>，使全部层级一并避让刘海与圆角。
        /// <para>这是少数会被运行时读取的字段——层级容器都是 UIRoot 的子节点，挂在这里即全局生效。</para>
        /// </summary>
        [Header("Safe Area")]
        [Tooltip("自动让 UI 避让刘海/圆角/手势条（在本物体上挂 UISafeArea）。")]
        public bool applySafeArea;

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

            // 层级容器都是本物体的子节点，故安全区挂在这里即对全部层级生效
            if (applySafeArea && GetComponent<UISafeArea>() == null)
            {
                gameObject.AddComponent<UISafeArea>();
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