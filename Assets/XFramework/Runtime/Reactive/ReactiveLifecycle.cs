using System;
using System.Runtime.CompilerServices;

namespace XFramework
{
    /// <summary>
    /// 为 BaseNode 添加响应式生命周期支持的扩展类。
    /// <para>通过 <see cref="GetLifecycle(BaseNode)"/> 获取生命周期信号，首次访问时自动创建。</para>
    /// <para>内部使用 <see cref="ConditionalWeakTable{TKey,TValue}"/> 管理生命周期处理器，
    /// 节点销毁时自动释放，不会阻止 GC 回收。</para>
    /// </summary>
    public static class ReactiveLifecycle
    {
        #region Private Fields

        /// <summary>
        /// 弱引用表。key 为目标节点，value 为生命周期处理器。
        /// <para>当 key（BaseNode）被 GC 回收时，条目自动清理，无需手动移除。</para>
        /// </summary>
        private static readonly ConditionalWeakTable<BaseNode, LifecycleHandler> _handlers = new();

        #endregion

        #region Public Methods

        /// <summary>
        /// 获取节点上的生命周期信号。首次访问时自动创建。
        /// <para>可通过返回的接口订阅节点的 Awake/Start/Destroy 事件。</para>
        /// </summary>
        /// <param name="node">要获取生命周期信号的节点。</param>
        /// <returns>生命周期信号接口。</returns>
        public static IReactiveLifecycle GetLifecycle(this BaseNode node)
        {
            return _handlers.GetValue(node, n => new LifecycleHandler(n));
        }

        /// <summary>
        /// 获取节点 Awake 完成时的信号。首次访问时自动创建生命周期处理器。
        /// </summary>
        public static IReadonlySignal OnInitializedSignal(this BaseNode node)
            => node.GetLifecycle().OnInitializedSignal;

        /// <summary>
        /// 获取节点 Start 完成时的信号。首次访问时自动创建生命周期处理器。
        /// </summary>
        public static IReadonlySignal OnStartedSignal(this BaseNode node)
            => node.GetLifecycle().OnStartedSignal;

        /// <summary>
        /// 获取节点 Destroy 时的信号。首次访问时自动创建生命周期处理器。
        /// </summary>
        public static IReadonlySignal OnDestroyedSignal(this BaseNode node)
            => node.GetLifecycle().OnDestroyedSignal;

        #endregion

        #region LifecycleHandler

        /// <summary>
        /// 生命周期处理器。通过订阅目标节点的事件来提供生命周期信号。
        /// <para>不继承 BaseNode，不挂入节点树，完全在外部监听。</para>
        /// <para>支持节点池复用：节点销毁后重新 Awake 时，Signal 会被重建。</para>
        /// </summary>
        private sealed class LifecycleHandler : IReactiveLifecycle
        {
            #region Private Fields

            private Signal _onInitialized;
            private Signal _onStarted;
            private Signal _onDestroyed;
            private readonly BaseNode _target;

            #endregion

            #region IReactiveLifecycle

            public IReadonlySignal OnInitializedSignal => _onInitialized;
            public IReadonlySignal OnStartedSignal => _onStarted;
            public IReadonlySignal OnDestroyedSignal => _onDestroyed;

            #endregion

            #region Constructor

            /// <summary>
            /// 创建生命周期处理器并订阅目标节点的事件。
            /// </summary>
            /// <param name="target">要监听的目标节点。</param>
            public LifecycleHandler(BaseNode target)
            {
                _target = target;
                CreateSignals();
                SubscribeEvents();
            }

            #endregion

            #region Private Methods

            private void CreateSignals()
            {
                _onInitialized = new Signal();
                _onStarted = new Signal();
                _onDestroyed = new Signal();
            }

            private void DisposeSignals()
            {
                _onInitialized?.Dispose();
                _onStarted?.Dispose();
                _onDestroyed?.Dispose();
            }

            private void SubscribeEvents()
            {
                // 订阅 Awake 事件：节点被池复用后重新 Awake 时重建 Signal
                _target.OnNodeAwakened += OnNodeAwakened;

                // 订阅 Start 事件
                _target.OnNodeStarted += OnNodeStarted;

                // 订阅 Destroy 事件，并在销毁时清理资源
                _target.OnNodeDestroyed += OnNodeDestroyed;
            }

            private void UnsubscribeEvents()
            {
                _target.OnNodeAwakened -= OnNodeAwakened;
                _target.OnNodeStarted -= OnNodeStarted;
                _target.OnNodeDestroyed -= OnNodeDestroyed;
            }

            private void OnNodeAwakened(BaseNode _)
            {
                // 节点被池复用后重新 Awake，需要重建 Signal
                // 注意：此时旧的 Signal 已在 OnNodeDestroyed 中被 Dispose
                CreateSignals();

                // 发布初始化信号
                _onInitialized.Publish();
            }

            private void OnNodeStarted(BaseNode _)
            {
                _onStarted.Publish();
            }

            private void OnNodeDestroyed(BaseNode _)
            {
                // 先发布销毁信号
                _onDestroyed.Publish();

                // 清理事件订阅
                UnsubscribeEvents();

                // 销毁 Signal
                DisposeSignals();
            }

            #endregion
        }

        #endregion
    }
}
