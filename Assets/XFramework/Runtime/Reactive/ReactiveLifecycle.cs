using System;
using System.Runtime.CompilerServices;

namespace XFramework
{
    /// <summary>
    /// 为 BaseNode 添加响应式生命周期支持的扩展类。
    /// <para>通过 <see cref="AddLifecycle(BaseNode)"/> 激活，不侵入 Core 层节点树。</para>
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
        /// 为节点添加响应式生命周期支持。
        /// <para>激活后可通过 <see cref="GetLifecycle(BaseNode)"/> 获取生命周期信号。</para>
        /// <para>多次调用同一节点会返回同一个实例。</para>
        /// </summary>
        /// <param name="node">要添加生命周期支持的节点。</param>
        /// <returns>生命周期信号接口。</returns>
        public static IReactiveLifecycle AddLifecycle(this BaseNode node)
        {
            return _handlers.GetValue(node, n => new LifecycleHandler(n));
        }

        /// <summary>
        /// 获取节点上的生命周期信号。
        /// <para>如果未调用过 <see cref="AddLifecycle(BaseNode)"/>，返回 null。</para>
        /// </summary>
        /// <param name="node">要获取生命周期信号的节点。</param>
        /// <returns>生命周期信号接口，未找到则返回 null。</returns>
        public static IReactiveLifecycle GetLifecycle(this BaseNode node)
        {
            _handlers.TryGetValue(node, out var handler);
            return handler;
        }

        #endregion

        #region LifecycleHandler

        /// <summary>
        /// 生命周期处理器。通过订阅目标节点的事件来提供生命周期信号。
        /// <para>不继承 BaseNode，不挂入节点树，完全在外部监听。</para>
        /// </summary>
        private sealed class LifecycleHandler : IReactiveLifecycle
        {
            #region Private Fields

            private readonly Signal<Unit> _onInitialized = new();
            private readonly Signal<Unit> _onStarted = new();
            private readonly Signal<Unit> _onDestroyed = new();

            #endregion

            #region IReactiveLifecycle

            public IReadonlySignal<Unit> OnInitializedSignal => _onInitialized;
            public IReadonlySignal<Unit> OnStartedSignal => _onStarted;
            public IReadonlySignal<Unit> OnDestroyedSignal => _onDestroyed;

            #endregion

            #region Constructor

            /// <summary>
            /// 创建生命周期处理器并订阅目标节点的事件。
            /// </summary>
            /// <param name="target">要监听的目标节点。</param>
            public LifecycleHandler(BaseNode target)
            {
                // 如果节点已 Awake 完成，立即触发初始化信号
                // 注意：AddLifecycle 可能在节点 Awake 之前或之后调用，
                // 这里假设调用 AddLifecycle 时节点已 Awake（通常如此）
                _onInitialized.Publish(default);

                // 订阅 Start 事件
                target.OnNodeStarted += _ => _onStarted.Publish(default);

                // 订阅 Destroy 事件，并在销毁时清理资源
                target.OnNodeDestroyed += _ =>
                {
                    _onDestroyed.Publish(default);
                    _onInitialized.Dispose();
                    _onStarted.Dispose();
                    _onDestroyed.Dispose();
                };
            }

            #endregion
        }

        #endregion
    }
}
