using UnityEngine;
using XFramework.XCore;

namespace XFramework.XUpdate
{

    /// <summary>
    /// 更新服务节点。作为 <see cref="LeafNode"/> 挂载到节点树中，作为节点树内获取更新服务的桥梁。
    /// <para>其他节点通过 <see cref="BaseNode.Get{T}"/> 获取此服务。</para>
    /// <para>自动监听节点树的添加/移除事件，将节点树中的 <see cref="IUpdateable"/> 节点注册到 <see cref="UpdateManager"/>。</para>
    /// <para>实际的调度逻辑由 <see cref="UpdateManager"/>（静态服务）统一管理。</para>
    /// </summary>
    public class UpdateNode : LeafNode, IUpdateNode
    {
        #region Lifecycle

        protected override void OnStart()
        {
            base.OnStart();

            // 自动绑定到父节点（即 RootNode），订阅递归冒泡事件并注册现有 IUpdateable 节点
            if (Parent != null)
            {
                Parent.OnDescendantAdded += OnDescendantAdded;
                Parent.OnDescendantRemoved += OnDescendantRemoved;
                Parent.OnDescendantStarted += OnDescendantStarted;

                // 注册树中已有的 IUpdateable 节点
                Parent.ForEach(child => TryRegister(child), recursive: true);
            }
        }

        protected override void OnDestroy()
        {
            // UpdateManager 由 GameLauncher 统一驱动，不在此处清理
            if (Parent != null)
            {
                Parent.OnDescendantAdded -= OnDescendantAdded;
                Parent.OnDescendantRemoved -= OnDescendantRemoved;
                Parent.OnDescendantStarted -= OnDescendantStarted;
            }
            base.OnDestroy();
        }

        #endregion

        #region IUpdateNode Implementation

        /// <summary>
        /// 执行一帧更新。委托给 <see cref="UpdateManager.Tick(float)"/>。
        /// </summary>
        public void Tick(float time) => UpdateManager.Tick(time);

        /// <summary>
        /// 启用指定节点的 Update 调用。委托给 <see cref="UpdateManager.Enable(IUpdateable)"/>。
        /// </summary>
        public void Enable(IUpdateable node) => UpdateManager.Enable(node);

        /// <summary>
        /// 禁用指定节点的 Update 调用。委托给 <see cref="UpdateManager.Disable(IUpdateable)"/>。
        /// </summary>
        public void Disable(IUpdateable node) => UpdateManager.Disable(node);

        /// <summary>
        /// 检查节点是否处于启用状态。委托给 <see cref="UpdateManager.IsEnabled(IUpdateable)"/>。
        /// </summary>
        public bool IsEnabled(IUpdateable node) => UpdateManager.IsEnabled(node);

        /// <summary>
        /// 立即对指定节点执行一次更新并重新调整 LOD。委托给 <see cref="UpdateManager.ProcessImmediate(IUpdateable, float, float)"/>。
        /// </summary>
        public void ProcessImmediate(IUpdateable node, float deltaTime, float time)
            => UpdateManager.ProcessImmediate(node, deltaTime, time);

        #endregion

        #region Private Methods - Event Subscription

        /// <summary>
        /// 尝试注册 <see cref="IUpdateable"/> 节点到 <see cref="UpdateManager"/>。
        /// <para>仅当节点已 Start 时才立即注册，否则等待 <see cref="OnDescendantStarted"/> 事件。</para>
        /// </summary>
        void TryRegister(BaseNode node)
        {
            if (node is IUpdateable u && node.Started)
                UpdateManager.Register(u, node.Depth);
        }

        /// <summary>
        /// 子孙节点添加时触发。已 Start 的 <see cref="IUpdateable"/> 立即注册到 <see cref="UpdateManager"/>，
        /// 未 Start 的等待 Start 事件。
        /// </summary>
        void OnDescendantAdded(BaseNode node)
        {
            if (node is IUpdateable u && node.Started)
                UpdateManager.Register(u, node.Depth);
        }

        /// <summary>
        /// 子孙节点 Start 完成时触发。注册 <see cref="IUpdateable"/> 节点到 <see cref="UpdateManager"/>。
        /// </summary>
        void OnDescendantStarted(BaseNode node)
        {
            if (node is IUpdateable u)
                UpdateManager.Register(u, node.Depth);
        }

        /// <summary>
        /// 子孙节点移除时触发。从 <see cref="UpdateManager"/> 注销 <see cref="IUpdateable"/> 节点。
        /// </summary>
        void OnDescendantRemoved(BaseNode node)
        {
            if (node is IUpdateable u)
                UpdateManager.Unregister(u);
        }

        #endregion
    }
}
