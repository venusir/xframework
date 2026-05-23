using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework.XUI.View
{
    /// <summary>
    /// HUD 元素基类。继承自 <see cref="UIViewBase"/>，用于 NPC/怪物头顶名字、血条等世界空间 HUD。
    /// <para>每帧自动跟随 <see cref="FollowTarget"/>，将世界坐标转为屏幕坐标并更新 <see cref="RectTransform.position"/>。</para>
    /// <para>当 <see cref="FollowTarget"/> 为 null 时，触发 <see cref="OnTargetLost"/> 事件，由 <see cref="UIHudManager"/> 自动回收。</para>
    /// <para>预制体由第三方自由设计，只需挂载继承 <see cref="UIHudItem"/> 的脚本即可。</para>
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class UIHudItem : UIViewBase
    {
        #region Fields

        private Camera _camera;
        private CanvasGroup _canvasGroup;
        private RectTransform _rectTransform;

        #endregion

        #region Properties

        /// <summary>
        /// 要跟随的 3D 目标 Transform。设置为 null 时 HUD 将在下一帧自动回收。
        /// </summary>
        public Transform FollowTarget { get; set; }

        /// <summary>
        /// 屏幕坐标偏移（像素）。常用于将 HUD 偏移到目标头顶上方。
        /// </summary>
        public Vector2 ScreenOffset { get; set; }

        /// <summary>
        /// CanvasGroup 组件（懒加载缓存）。
        /// </summary>
        public CanvasGroup CanvasGroup
        {
            get
            {
                if (_canvasGroup == null)
                    _canvasGroup = GetComponent<CanvasGroup>();
                return _canvasGroup;
            }
        }

        /// <summary>
        /// RectTransform 组件（懒加载缓存）。
        /// </summary>
        public RectTransform RectTransform
        {
            get
            {
                if (_rectTransform == null)
                    _rectTransform = (RectTransform)transform;
                return _rectTransform;
            }
        }

        #endregion

        #region Events

        /// <summary>
        /// 跟随目标丢失时触发。<see cref="UIHudManager"/> 订阅此事件实现自动回收。
        /// </summary>
        internal event Action<UIHudItem> OnTargetLost;

        #endregion

        #region UIViewBase Implementation

        /// <summary>
        /// HUD 打开时缓存 Camera 和 RectTransform 引用。子类可重写以添加自定义初始化，但必须调用 base。
        /// </summary>
        protected override UniTask OnOpenImpl(object userData)
        {
            _camera = Camera.main;
            // 确保 CanvasGroup 已缓存
            if (_canvasGroup == null)
                _canvasGroup = GetComponent<CanvasGroup>();
            if (_rectTransform == null)
                _rectTransform = (RectTransform)transform;

            // Canvas 不需要射线检测（HUD 通常不可交互）
            Raycaster.enabled = false;

            return UniTask.CompletedTask;
        }

        /// <summary>
        /// HUD 关闭时清理引用。子类可重写以添加自定义清理，但必须调用 base。
        /// </summary>
        protected override UniTask OnCloseImpl(bool immediate)
        {
            FollowTarget = null;
            OnTargetLost = null;
            _camera = null;
            return UniTask.CompletedTask;
        }

        #endregion

        #region Per-Frame Update

        /// <summary>
        /// 每帧跟随目标。由 <see cref="UIManager"/> 集中驱动。
        /// <para>子类如需添加自定义每帧逻辑，应重写此方法并调用 base.OnUpdate()。</para>
        /// </summary>
        protected internal override void OnUpdate()
        {
            if (FollowTarget == null)
            {
                OnTargetLost?.Invoke(this);
                return;
            }

            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null)
                    return;
            }

            var screenPos = _camera.WorldToScreenPoint(FollowTarget.position);

            // 目标在屏幕后方时自动隐藏（如镜头背后）
            var alpha = screenPos.z > 0f ? 1f : 0f;
            if (CanvasGroup != null)
                CanvasGroup.alpha = alpha;

            RectTransform.position = screenPos + (Vector3)ScreenOffset;
        }

        #endregion
    }
}