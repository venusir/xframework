using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using XFramework.XUpdate;

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

        /// <summary>当前生命周期状态。</summary>
        private ViewState _state;

        /// <summary>
        /// 每帧更新档位。Inspector 可配，默认每帧。
        /// </summary>
        [SerializeField]
        private UpdateTier _updateTier = UpdateTier.Tier0;

        /// <summary>
        /// 「打开中」的完成闸门。懒分配——只有真的有人在 OnOpen 期间发起关闭时才创建。
        /// </summary>
        private UniTaskCompletionSource _openingGate;

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
        /// 视图是否已打开（含「正在打开」阶段）。
        /// <para>打开期间即为 true：面板在自己 <c>OnOpen</c> 里调用 <c>CloseSelfAsync</c> 时必须能通过
        /// 「未打开则忽略」的守卫，否则那个请求会静默失败。</para>
        /// </summary>
        public bool IsOpen => _state == ViewState.Opening || _state == ViewState.Open;

        /// <summary>
        /// 是否正在执行打开（<c>OnOpen</c> 尚未返回）。
        /// <para>此时发出的关闭请求会被延迟到打开完成之后执行。</para>
        /// </summary>
        public bool IsOpening => _state == ViewState.Opening;

        /// <summary>
        /// 是否正在执行关闭。
        /// </summary>
        public bool IsClosing => _state == ViewState.Closing;

        /// <summary>
        /// 本视图的每帧更新档位。档位越高派发间隔越大（第 k 档 = 2^k 个节拍格，60Hz 基准下
        /// Tier1~Tier7 约 33 / 67 / 133 / 267 / 533 / 1067 / 2133ms，Tier0 为每帧）。
        /// <para>Inspector 可配；运行时改这个属性会在下一次派发时重排到新档位，无需重开面板。</para>
        /// <para>用不到每帧的面板（倒计时、进度插值等）声明较低档位即可显著降耗。只要按传入的
        /// <c>deltaTime</c> 积分，行为不随档位变化。</para>
        /// <para>属性名与类型同名（<c>UpdateTier UpdateTier</c>）是刻意的：简写成 <c>Tier</c> 会与
        /// 渲染侧的 LOD / 容器层级混淆，而它表达的就是这个枚举本身。</para>
        /// </summary>
        public UpdateTier UpdateTier
        {
            get => _updateTier;
            set => _updateTier = value;
        }

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
        /// <para><paramref name="deltaTime"/> 是距<strong>上次派发</strong>的间隔，不是 <c>Time.deltaTime</c>：
        /// 面板可声明较低档位而被降频派发，此时两者相差整数倍。做积分必须用它，否则会慢若干倍。</para>
        /// <para>替代直接使用 MonoBehaviour.Update()，避免分散的 Update 开销。</para>
        /// <para>适用场景：HUD 位置跟随、倒计时、进度条插值等每帧逻辑。</para>
        /// </summary>
        protected internal virtual void OnUpdate(float deltaTime, float time) { }

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
        /// 视图打开入口。激活 GameObject，进入 Opening 态，调用 <see cref="OnOpenImpl"/>，最后落为 Open。
        /// </summary>
        internal async UniTask DoOpenAsync(object userData)
        {
            gameObject.SetActive(true);
            _state = ViewState.Opening;

            try
            {
                await OnOpenImpl(userData);
            }
            catch
            {
                // 打开失败：状态归位，实例的回滚由调用方负责
                _state = ViewState.Closed;
                ReleaseOpeningGate();
                throw;
            }

            // OnOpen 期间可能已发出并执行了关闭，那时状态已不是 Opening，不要覆盖回去
            if (_state == ViewState.Opening)
                _state = ViewState.Open;

            ReleaseOpeningGate();
        }

        /// <summary>
        /// 视图关闭入口。进入 Closing 态，调用 <see cref="OnCloseImpl"/>，最后落为 Closed 并隐藏。
        /// </summary>
        internal async UniTask DoCloseAsync(bool immediate)
        {
            _state = ViewState.Closing;

            try
            {
                await OnCloseImpl(immediate);
            }
            finally
            {
                // 即便 OnClose 抛异常也要落到终态：停在 Closing 会让 IsOpen 一直撒谎，
                // 且物体保持激活
                _state = ViewState.Closed;
                gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 等待「正在打开」阶段结束。非 Opening 态立即完成，不分配。
        /// <para>供关闭路径调用：在 <c>OnOpen</c> 的调用栈上重入 <c>OnClose</c> 与关闭动画，
        /// 子类几乎必然写出「先初始化再被清理」的乱序。</para>
        /// </summary>
        internal UniTask WaitWhileOpeningAsync()
        {
            if (_state != ViewState.Opening)
                return UniTask.CompletedTask;

            if (_openingGate == null)
                _openingGate = new UniTaskCompletionSource();

            return _openingGate.Task;
        }

        /// <summary>
        /// 打开阶段结束，放行等待者。完成后清空闸门以便下次打开重新懒分配。
        /// </summary>
        private void ReleaseOpeningGate()
        {
            var gate = _openingGate;
            _openingGate = null;
            gate?.TrySetResult();
        }

        #endregion

        #region Internal — State

        /// <summary>视图生命周期状态。</summary>
        private enum ViewState : byte
        {
            /// <summary>未打开（含已回池）。</summary>
            Closed,

            /// <summary>正在执行打开，<c>OnOpen</c> 尚未返回。</summary>
            Opening,

            /// <summary>已打开。</summary>
            Open,

            /// <summary>正在执行关闭。</summary>
            Closing,
        }

        #endregion
    }
}