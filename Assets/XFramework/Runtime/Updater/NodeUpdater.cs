namespace XFramework
{
    /// <summary>
    /// 节点更新管理器。将 <see cref="Updater"/> 与节点树事件订阅封装在一起，
    /// 自动注册/注销树中所有 <see cref="IUpdateable"/> 节点。
    /// </summary>
    public class NodeUpdater
    {
        #region Private Fields

        readonly Updater _updater;
        bool _disposed;

        #endregion

        #region Constructor

        /// <summary>
        /// 创建节点更新管理器实例。
        /// </summary>
        public NodeUpdater()
        {
            _updater = new Updater();
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
        /// 执行一帧更新。内部委托给 <see cref="Updater.Tick(float)"/>。
        /// </summary>
        /// <param name="deltaTime">帧时间差。</param>
        public void Tick(float deltaTime)
        {
            if (!_disposed)
                _updater.Tick(deltaTime);
        }

        /// <summary>
        /// 释放资源。清空 <see cref="Updater"/> 内部状态。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _updater.Clear();
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
                if (child is IUpdateable u)
                    _updater.Register(u, child.Depth);

                if (child is ParentNode childParent)
                    SubscribeTree(childParent);
            }
        }

        /// <summary>
        /// 子节点添加时，注册 <see cref="IUpdateable"/> 并递归订阅其子节点事件。
        /// </summary>
        void OnNodeAdded(BaseNode node)
        {
            if (node is IUpdateable u)
                _updater.Register(u, node.Depth);

            if (node is ParentNode parent)
            {
                parent.OnNodeAdded += OnNodeAdded;
                parent.OnNodeRemoved += OnNodeRemoved;

                // 新加入的 ParentNode 可能已有子节点（如批量添加）
                for (int i = 0; i < parent.ChildCount; i++)
                {
                    var child = parent[i];
                    if (child is IUpdateable u2)
                        _updater.Register(u2, child.Depth);
                    if (child is ParentNode childParent)
                        SubscribeTree(childParent);
                }
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
                for (int i = parent.ChildCount - 1; i >= 0; i--)
                    OnNodeRemoved(parent[i]);

                // 取消订阅该父节点的事件
                parent.OnNodeAdded -= OnNodeAdded;
                parent.OnNodeRemoved -= OnNodeRemoved;
            }

            if (node is IUpdateable u)
                _updater.Unregister(u);
        }

        #endregion
    }
}
