namespace XFramework
{
    /// <summary>
    /// 更新绑定器。将 <see cref="UpdateScheduler"/> 与节点树事件订阅封装在一起，
    /// 自动注册/注销树中所有 <see cref="IUpdateable"/> 节点。
    /// <para>节点只有在 <see cref="BaseNode.Start"/> 完成后才会注册到 <see cref="UpdateScheduler"/>，
    /// 确保加载中的节点不会收到 Update 调用。</para>
    /// <para>通过 <see cref="EnableNode(IUpdateable)"/> 和 <see cref="DisableNode(IUpdateable)"/> 手动控制节点更新开关。</para>
    /// </summary>
    public class UpdateBinder
    {
        #region Private Fields

        readonly UpdateScheduler _scheduler;
        bool _disposed;

        #endregion

        #region Constructor

        /// <summary>
        /// 创建更新绑定器实例。
        /// </summary>
        public UpdateBinder()
        {
            _scheduler = new UpdateScheduler();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 绑定到根节点。遍历整棵树注册所有 <see cref="IUpdateable"/>，
        /// 并订阅所有 <see cref="ParentNode"/> 的事件以自动处理后续的添加/移除。
        /// </summary>
        /// <param name="root">节点树的根节点。</param>
        public void Bind(RootNode root)
        {
            SubscribeTree(root);
        }

        /// <summary>
        /// 执行一帧更新。内部委托给 <see cref="UpdateScheduler.Tick(float)"/>。
        /// </summary>
        /// <param name="time">当前时间（<see cref="UnityEngine.Time.time"/>），由外部传入避免重复获取。</param>
        public void Tick(float time)
        {
            if (!_disposed)
                _scheduler.Tick(time);
        }

        /// <summary>
        /// 启用指定节点的 Update 调用。
        /// <para>会触发 <see cref="IUpdateable.OnEnable"/>。</para>
        /// </summary>
        /// <param name="node">要启用的节点。</param>
        public void EnableNode(IUpdateable node)
        {
            if (!_disposed)
                _scheduler.Enable(node);
        }

        /// <summary>
        /// 禁用指定节点的 Update 调用。
        /// <para>会触发 <see cref="IUpdateable.OnDisable"/>。</para>
        /// </summary>
        /// <param name="node">要禁用的节点。</param>
        public void DisableNode(IUpdateable node)
        {
            if (!_disposed)
                _scheduler.Disable(node);
        }

        /// <summary>
        /// 检查指定节点是否处于启用状态。
        /// </summary>
        /// <param name="node">要检查的节点。</param>
        /// <returns>如果节点未被禁用则返回 true。</returns>
        public bool IsNodeEnabled(IUpdateable node)
        {
            return !_disposed && _scheduler.IsEnabled(node);
        }

        /// <summary>
        /// 立即对指定节点执行一次更新并重新调整 LOD。
        /// <para>用于外部逻辑变化时需要立即响应，不等下一次时间切片。</para>
        /// </summary>
        /// <param name="node">要立即更新的节点。</param>
        /// <param name="deltaTime">传入的时间差。</param>
        /// <param name="time">当前时间（<see cref="UnityEngine.Time.time"/>）。</param>
        public void RequestImmediateUpdate(IUpdateable node, float deltaTime, float time)
        {
            if (!_disposed)
                _scheduler.ProcessImmediate(node, deltaTime, time);
        }

        /// <summary>
        /// 释放资源。清空 <see cref="UpdateScheduler"/> 内部状态。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _scheduler.Clear();
        }

        #endregion

        #region Private Methods - Event Subscription

        /// <summary>
        /// 递归订阅指定父节点及其所有子节点的事件，并注册 <see cref="IUpdateable"/>。
        /// </summary>
        void SubscribeTree(ParentNode parent)
        {
            parent.OnNodeAdded += OnNodeAdded;
            parent.OnNodeRemoved += OnNodeRemoved;

            for (int i = 0; i < parent.ChildCount; i++)
            {
                var child = parent[i];
                TryRegister(child);

                if (child is ParentNode childParent)
                    SubscribeTree(childParent);
            }
        }

        /// <summary>
        /// 尝试注册 <see cref="IUpdateable"/> 节点。
        /// <para>如果节点已 Start 则立即注册，否则订阅 <see cref="BaseNode.OnStarted"/> 延迟注册。</para>
        /// </summary>
        void TryRegister(BaseNode node)
        {
            if (node is IUpdateable u)
            {
                if (node.Started)
                {
                    _scheduler.Register(u, node.Depth);
                }
                else
                {
                    node.OnStarted += OnNodeStarted;
                }
            }
        }

        /// <summary>
        /// 节点启动完成时触发，将节点注册到 <see cref="UpdateScheduler"/>。
        /// </summary>
        void OnNodeStarted(BaseNode node)
        {
            node.OnStarted -= OnNodeStarted;

            if (node is IUpdateable u)
                _scheduler.Register(u, node.Depth);
        }

        /// <summary>
        /// 子节点添加时，注册 <see cref="IUpdateable"/> 并递归订阅其子节点事件。
        /// </summary>
        void OnNodeAdded(BaseNode node)
        {
            // 复用 SubscribeTree 逻辑，消除代码重复
            if (node is ParentNode parent)
            {
                SubscribeTree(parent);
            }
            else
            {
                TryRegister(node);
            }
        }

        /// <summary>
        /// 子节点移除时，注销 <see cref="IUpdateable"/> 并取消订阅其子节点事件。
        /// <para>利用 <see cref="BaseNode.Destroy"/> 先 RemoveChild 后 DestroyInternal 的时间差，
        /// 此时子节点列表仍可安全遍历。</para>
        /// </summary>
        void OnNodeRemoved(BaseNode node)
        {
            if (node is ParentNode parent)
            {
                // 先递归处理子节点（注销 + 取消订阅）
                // 使用快照遍历避免在事件回调中修改列表的潜在问题
                int childCount = parent.ChildCount;
                for (int i = childCount - 1; i >= 0; i--)
                    OnNodeRemoved(parent[i]);

                // 取消订阅该父节点的事件
                parent.OnNodeAdded -= OnNodeAdded;
                parent.OnNodeRemoved -= OnNodeRemoved;
            }

            // 取消 OnStarted 订阅（如果尚未触发）
            node.OnStarted -= OnNodeStarted;

            if (node is IUpdateable u)
                _scheduler.Unregister(u);
        }

        #endregion
    }
}
