using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace XFramework.XUI
{
    /// <summary>
    /// UI 面板基类。所有面板需继承此类。
    /// <para>面板生命周期由 <see cref="UIManager"/> 驱动：OnOpen → OnFocus/OnBlur → OnClose。</para>
    /// <para>支持打开/关闭动画：重写 <see cref="PlayOpenAnimation"/> 和 <see cref="PlayCloseAnimation"/>。</para>
    /// <para>多语言刷新：重写 <see cref="OnLanguageChanged"/>，与 <see cref="XLocalization.LocalizationManager"/> 联动。</para>
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(GraphicRaycaster))]
    public abstract class UIPanelBase : MonoBehaviour
    {
        #region Fields

        private Canvas _canvas;
        private GraphicRaycaster _raycaster;

        #endregion

        #region Properties

        /// <summary>
        /// 面板所属层级。
        /// </summary>
        public int Layer { get; internal set; }

        /// <summary>
        /// 面板预制体的 YooAsset 地址。
        /// </summary>
        public string AssetPath { get; internal set; }

        /// <summary>
        /// 面板是否已打开（处于激活状态）。
        /// </summary>
        public bool IsOpen { get; private set; }

        /// <summary>
        /// 面板是否处于暂停状态（被其他面板覆盖，失去焦点）。
        /// </summary>
        public bool IsPaused { get; private set; }

        /// <summary>
        /// 面板的 Canvas 组件。
        /// </summary>
        public Canvas Canvas
        {
            get
            {
                if (_canvas == null)
                    _canvas = GetComponent<Canvas>();
                return _canvas;
            }
        }

        /// <summary>
        /// 面板的 GraphicRaycaster 组件。
        /// </summary>
        public GraphicRaycaster Raycaster
        {
            get
            {
                if (_raycaster == null)
                    _raycaster = GetComponent<GraphicRaycaster>();
                return _raycaster;
            }
        }

        #endregion

        #region Lifecycle Methods (Called by UIManager)

        /// <summary>
        /// 面板打开时调用。子类必须实现此方法处理初始化逻辑。
        /// <para>此时面板已实例化完成，Canvas 已设置好 sorting order。</para>
        /// </summary>
        /// <param name="userData">调用 OpenAsync/PushAsync 时传入的自定义数据。</param>
        /// <returns>支持 await。</returns>
        protected internal abstract UniTask OnOpen(object userData);

        /// <summary>
        /// 面板关闭时调用。子类必须实现此方法处理清理逻辑。
        /// <para>关闭动画完成后、实例销毁前调用。</para>
        /// </summary>
        /// <returns>支持 await。</returns>
        protected internal abstract UniTask OnClose();

        /// <summary>
        /// 面板获得焦点时调用（回到堆栈顶层时）。
        /// <para>此时面板重新变为可交互状态。</para>
        /// </summary>
        protected internal virtual void OnFocus()
        {
            IsPaused = false;
            Raycaster.enabled = true;
        }

        /// <summary>
        /// 面板失去焦点时调用（被 Push 的新面板覆盖时）。
        /// <para>此时面板交互被禁用。</para>
        /// </summary>
        protected internal virtual void OnBlur()
        {
            IsPaused = true;
            Raycaster.enabled = false;
        }

        /// <summary>
        /// 语言切换时调用。子类可重写以刷新 UI 文本。
        /// <para>与 <see cref="XLocalization.LocalizationManager"/> 联动。</para>
        /// </summary>
        protected internal virtual void OnLanguageChanged(string lang) { }

        #endregion

        #region Animation Methods

        /// <summary>
        /// 打开动画。默认无动画。子类可重写以实现自定义打开动画（如 FadeIn、Scale）。
        /// </summary>
        /// <returns>一个 UniTask，动画结束后完成。</returns>
        protected internal virtual UniTask PlayOpenAnimation()
        {
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 关闭动画。默认无动画。子类可重写以实现自定义关闭动画（如 FadeOut、Scale）。
        /// </summary>
        /// <returns>一个 UniTask，动画结束后完成。</returns>
        protected internal virtual UniTask PlayCloseAnimation()
        {
            return UniTask.CompletedTask;
        }

        #endregion

        #region Convenience Methods

        /// <summary>
        /// 关闭自身面板。
        /// </summary>
        /// <param name="immediate">是否跳过关闭动画，直接销毁。</param>
        public UniTask CloseSelfAsync(bool immediate = false)
        {
            if (!IsOpen)
                return UniTask.CompletedTask;

            return UIManager.CloseAsync(this, immediate);
        }

        #endregion

        #region Internal

        /// <summary>
        /// 面板打开时由 UIManager 调用，执行动画并标记为 IsOpen。
        /// </summary>
        internal async UniTask DoOpenAsync(object userData)
        {
            gameObject.SetActive(true);
            await PlayOpenAnimation();
            await OnOpen(userData);
            IsOpen = true;
            IsPaused = false;
            Raycaster.enabled = true;
        }

        /// <summary>
        /// 面板关闭时由 UIManager 调用，执行动画、OnClose，最后销毁。
        /// </summary>
        internal async UniTask DoCloseAsync(bool immediate)
        {
            IsOpen = false;

            if (!immediate)
            {
                await PlayCloseAnimation();
            }

            await OnClose();

            // 销毁/回收实例
            Destroy(gameObject);
        }

        #endregion
    }
}