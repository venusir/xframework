using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace XFramework.XUI.View
{
    /// <summary>
    /// UI 视图基类。所有 UI 节点（面板、HUD 等）的最底层抽象。
    /// <para>提供 Canvas/Raycaster 管理、层级/AssetPath 属性、<see cref="DoOpenAsync"/> / <see cref="DoCloseAsync"/> 生命周期入口、<see cref="OnUpdate"/> 集中驱动。</para>
    /// <para>子类通过实现 <see cref="OnOpenImpl"/> 和 <see cref="OnCloseImpl"/> 定义具体的打开/关闭行为。</para>
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(GraphicRaycaster))]
    public abstract class UIViewBase : MonoBehaviour
    {
        #region Fields

        private Canvas _canvas;
        private GraphicRaycaster _raycaster;

        /// <summary>
        /// 随本视图回池自动释放的订阅。懒分配——多数视图不登记任何东西。
        /// </summary>
        private List<IDisposable> _tracked;

        #endregion

        #region Public API — Lifetime-bound Subscriptions

        /// <summary>
        /// 登记一个 <see cref="IDisposable"/>，它将在本视图回池时自动释放。
        /// <para>这是「订阅随视图生命周期自动释放」的唯一出口。面板与 HUD 都是<strong>回池而非销毁</strong>，
        /// 挂在 <c>OnDestroy</c> 上的释放永远不会触发——关闭后订阅会一直存活到下次打开同一面板，
        /// 若此后再不打开就永久驻留。</para>
        /// </summary>
        /// <param name="disposable">要登记的订阅；为 null 时原样返回，不登记。</param>
        /// <returns>传入的 <paramref name="disposable"/>，便于在赋值语句里链式使用。</returns>
        /// <example>
        /// <code>
        /// // 在 OnOpen 里
        /// Track(LocalizationManager.Subscribe(_ =&gt; Refresh()));
        /// Track(UIBinder.BindToText(vm.Title, titleText));
        /// </code>
        /// </example>
        public IDisposable Track(IDisposable disposable)
        {
            if (disposable == null)
                return null;

            if (_tracked == null)
                _tracked = new List<IDisposable>(4);

            _tracked.Add(disposable);
            return disposable;
        }

        /// <summary>
        /// 释放全部已登记的订阅。回池时由 <see cref="OnPoolRecycle"/> 调用。
        /// </summary>
        private void DisposeTracked()
        {
            if (_tracked == null)
                return;

            for (int i = 0; i < _tracked.Count; i++)
                _tracked[i]?.Dispose();

            // 保留 List 实例以便复用，只清元素
            _tracked.Clear();
        }

        #endregion

        #region Properties

        /// <summary>
        /// 视图所属层级。数值越大越靠前。
        /// </summary>
        public int Layer { get; internal set; }

        /// <summary>
        /// 视图预制体的 YooAsset 地址。
        /// </summary>
        public string AssetPath { get; internal set; }

        /// <summary>
        /// 视图是否已打开（处于激活状态）。
        /// </summary>
        public bool IsOpen { get; private set; }

        /// <summary>
        /// 视图的 Canvas 组件（懒加载）。
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
        /// 视图的 GraphicRaycaster 组件（懒加载）。
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

        #region Abstract Methods — Subclass Open/Close Logic

        /// <summary>
        /// 视图打开时由框架调用。子类实现具体的打开逻辑。
        /// </summary>
        /// <param name="userData">调用方传入的自定义数据。</param>
        /// <returns>支持 await。</returns>
        protected abstract UniTask OnOpenImpl(object userData);

        /// <summary>
        /// 视图关闭时由框架调用。子类实现具体的关闭逻辑。
        /// </summary>
        /// <param name="immediate">是否跳过动画，直接关闭。</param>
        /// <returns>支持 await。</returns>
        protected abstract UniTask OnCloseImpl(bool immediate);

        #endregion

        #region Virtual Methods

        /// <summary>
        /// 每帧更新。由 <see cref="UIManager"/> 统一驱动，仅当 IsOpen 为 true 时调用。
        /// <para>替代直接使用 MonoBehaviour.Update()，避免分散的 Update 开销。</para>
        /// <para>适用场景：HUD 位置跟随、倒计时、进度条插值等每帧逻辑。</para>
        /// </summary>
        protected internal virtual void OnUpdate() { }

        /// <summary>
        /// 视图即将回池时由框架调用。子类可重写以重置自定义状态。
        /// <para>基类实现释放 <see cref="Track"/> 登记的订阅，并重置 Canvas.sortingOrder 与 overrideSorting。</para>
        /// </summary>
        protected internal virtual void OnPoolRecycle()
        {
            DisposeTracked();

            if (Canvas != null)
            {
                Canvas.sortingOrder = 0;
                Canvas.overrideSorting = false;
            }
        }

        #endregion

        #region Internal — Lifecycle Entry Points (Called by UIManager / UIHudManager)

        /// <summary>
        /// 视图打开入口。激活 GameObject，调用 <see cref="OnOpenImpl"/>，标记 IsOpen。
        /// </summary>
        internal async UniTask DoOpenAsync(object userData)
        {
            gameObject.SetActive(true);
            await OnOpenImpl(userData);
            IsOpen = true;
        }

        /// <summary>
        /// 视图关闭入口。标记 IsOpen=false，调用 <see cref="OnCloseImpl"/>，最后 SetActive(false)。
        /// </summary>
        internal async UniTask DoCloseAsync(bool immediate)
        {
            IsOpen = false;
            await OnCloseImpl(immediate);
            gameObject.SetActive(false);
        }

        #endregion
    }
}