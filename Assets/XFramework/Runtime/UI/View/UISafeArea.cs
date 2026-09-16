using UnityEngine;

namespace XFramework.XUI.View
{
    /// <summary>
    /// 安全区适配：把自身的 RectTransform 收缩到 <see cref="Screen.safeArea"/>，避让刘海、圆角与
    /// 手势条。
    /// <para><b>推荐挂在 UIRoot 上</b>：层级容器都是它的子节点，于是所有层级一并生效，不需要逐层处理。</para>
    /// <para>需要让单个面板避让（如全屏背景图要铺满、但按钮要避让）时，可在该面板内部单独挂一个。</para>
    /// <para><b>屏幕尺寸变化时需自行调用 <see cref="Apply"/></b>（见该方法的说明）。</para>
    /// </summary>
    [AddComponentMenu("XFramework/UI/UISafeArea")]
    [RequireComponent(typeof(RectTransform))]
    public sealed class UISafeArea : MonoBehaviour
    {
        #region Fields

        [Tooltip("启用时自动应用一次。取消勾选则只在手动调用 Apply() 时生效。")]
        [SerializeField]
        private bool _applyAutomatically = true;

        private RectTransform _rectTransform;

        /// <summary>正在应用中的重入标志，见 <see cref="Apply"/>。</summary>
        private bool _applying;

        #endregion

        #region Properties

        /// <summary>
        /// 矩形组件（懒加载缓存）。
        /// </summary>
        public RectTransform RectTransform
        {
            get
            {
                // alreadyDestroyed 时对已销毁组件调 GetComponent 会抛 MissingReferenceException。
                // 消息回调有可能在销毁边缘被打到，这里对销毁态保持安静。
                if (_rectTransform == null && this != null)
                    _rectTransform = GetComponent<RectTransform>();

                return _rectTransform;
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// 立即应用一次安全区。
        /// <para><b>屏幕尺寸变化（旋转、分辨率切换、窗口缩放）后需自行调用一次</b>。本组件刻意
        /// 不监听 <c>OnRectTransformDimensionsChange</c> 做自动适配：设置锚点本身会改变 RectTransform
        /// 的尺寸，从而再次触发该回调，二者会互相激发。实测在无 Canvas 父级的裸 RectTransform 上
        /// 该循环不会收敛，日志以每秒数千行的速度增长直至进程被撑爆；而一个可能把宿主应用挂死的
        /// 组件，比一个需要显式调用一次的组件糟糕得多。</para>
        /// <para>需要自动适配时，在自己的屏幕尺寸变化回调里调用本方法即可。</para>
        /// </summary>
        public void Apply()
        {
            // 重入守卫：即便调用方在自己的尺寸回调里调本方法，也不会与外层互相激发
            if (_applying)
                return;

            var rect = RectTransform;
            if (rect == null)
                return;

            CalculateAnchors(Screen.safeArea, new Vector2(Screen.width, Screen.height),
                out var anchorMin, out var anchorMax);

            // 值没变就不写。省几次赋值是次要的，主要是让「写值 → 尺寸变化 → 回调 → 再写」
            // 这条路径在第二次调用时自然终止
            if (rect.anchorMin == anchorMin && rect.anchorMax == anchorMax &&
                rect.offsetMin == Vector2.zero && rect.offsetMax == Vector2.zero)
            {
                return;
            }

            _applying = true;

            try
            {
                rect.anchorMin = anchorMin;
                rect.anchorMax = anchorMax;

                // 锚点决定矩形框，偏移必须清零——否则旧偏移会把安全区又推开
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
            finally
            {
                _applying = false;
            }
        }

        /// <summary>
        /// 把安全区（屏幕像素）换算成归一化锚点。
        /// <para>抽成纯函数是为了能脱离 <see cref="Screen"/> 的当前状态直接验证换算——否则这条
        /// 逻辑只能在特定设备上碰运气。</para>
        /// </summary>
        /// <param name="safeArea">安全区，屏幕像素坐标。</param>
        /// <param name="screenSize">屏幕尺寸，像素。</param>
        /// <param name="anchorMin">输出的归一化左下锚点。</param>
        /// <param name="anchorMax">输出的归一化右上锚点。</param>
        public static void CalculateAnchors(in Rect safeArea, in Vector2 screenSize,
            out Vector2 anchorMin, out Vector2 anchorMax)
        {
            // 屏幕尺寸为 0（未初始化、或某些批处理环境）时退回全屏，避免除零
            if (screenSize.x <= 0f || screenSize.y <= 0f)
            {
                anchorMin = Vector2.zero;
                anchorMax = Vector2.one;
                return;
            }

            anchorMin = new Vector2(safeArea.xMin / screenSize.x, safeArea.yMin / screenSize.y);
            anchorMax = new Vector2(safeArea.xMax / screenSize.x, safeArea.yMax / screenSize.y);
        }

        #endregion

        #region Unity Lifecycle

        private void OnEnable()
        {
            if (_applyAutomatically)
                Apply();
        }

        #endregion
    }
}
