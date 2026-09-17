using UnityEngine;
using XFramework.XNode;

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

        /// <summary>
        /// 订阅时所用的父节点。必须在 <see cref="OnStart"/> 缓存，不能依赖 <see cref="BaseNode.Parent"/>。
        /// </summary>
        private ParentNode _subscribedParent;

        protected override void OnStart()
        {
            base.OnStart();

            // 自动绑定到父节点（即 RootNode），订阅递归冒泡事件并注册现有 IUpdateable 节点
            if (Parent != null)
            {
                _subscribedParent = Parent;
                _subscribedParent.OnDescendantAdded += OnDescendantAdded;
                _subscribedParent.OnDescendantRemoved += OnDescendantRemoved;
                _subscribedParent.OnDescendantStarted += OnDescendantStarted;

                // 注册树中已有的 IUpdateable 节点
                _subscribedParent.ForEach(child => TryRegister(child), recursive: true);
            }
        }

        protected override void OnDestroy()
        {
            // UpdateManager 由 GameLauncher 统一驱动，不在此处清理

            // 必须用 OnStart 时缓存的引用退订：BaseNode.Destroy 的 Phase 1 会先把 _parent 置空再调
            // OnDestroy，此时 Parent 恒为 null。原先写的 `if (Parent != null) { 退订 }` 因此是
            // **永不执行的死代码**——已销毁的 UpdateNode 会永久留在父节点的事件链上，
            // 而父节点回池复用后，这些幽灵订阅者会把新树的节点重复注册进调度器，
            // 表现为「同一个节点每帧被派发多次」（OnUpdate 被调用 N 次）。
            if (_subscribedParent != null)
            {
                _subscribedParent.OnDescendantAdded -= OnDescendantAdded;
                _subscribedParent.OnDescendantRemoved -= OnDescendantRemoved;
                _subscribedParent.OnDescendantStarted -= OnDescendantStarted;
                _subscribedParent = null;
            }
            base.OnDestroy();
        }

        #endregion

        #region IUpdateNode Implementation

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
            if (node.Started)
                RegisterNode(node);
        }

        /// <summary>
        /// 把节点登记进调度器：实现了哪个时机的接口就登记到哪个时机，两个都实现则两处都登记。
        /// <para>时间轴取节点自行声明的（未声明则为逻辑轴）。轴只在注册时读一次：之后由调度器
        /// 记住，节点中途改声明不会自动迁移——需要迁移时先 <see cref="UpdateManager.Unregister"/>
        /// 再重新注册。</para>
        /// </summary>
        static void RegisterNode(BaseNode node)
        {
            // 时间轴取节点自行声明的（未声明则为逻辑轴）。
            // 原先经 UpdateManagerExtensions.ResolveTimeMode 读取，该扩展随「Update 模块解绑节点」一并删除，故内联于此。
            UpdateTimeMode mode = node is IUpdateTimeMode declared ? declared.TimeMode : UpdateTimeMode.Scaled;

            if (node is IUpdateable updateable)
            {
                UpdateManager.Register(updateable, node.Depth, timeMode: mode);
            }

            if (node is ILateUpdateable lateUpdateable)
            {
                UpdateManager.RegisterLate(lateUpdateable, node.Depth, timeMode: mode);
            }

            if (node is IFixedUpdateable fixedUpdateable)
            {
                // 固定步长时机没有时间轴参数：Unity 的固定步长本就随 timeScale 停摆
                UpdateManager.RegisterFixed(fixedUpdateable, node.Depth);
            }
        }

        /// <summary>
        /// 子孙节点添加时触发。已 Start 的 <see cref="IUpdateable"/> 立即注册到 <see cref="UpdateManager"/>，
        /// 未 Start 的等待 Start 事件。
        /// </summary>
        void OnDescendantAdded(BaseNode node)
        {
            if (node.Started)
                RegisterNode(node);
        }

        /// <summary>
        /// 子孙节点 Start 完成时触发。注册 <see cref="IUpdateable"/> / <see cref="ILateUpdateable"/>
        /// 节点到 <see cref="UpdateManager"/>。
        /// </summary>
        void OnDescendantStarted(BaseNode node)
        {
            RegisterNode(node);
        }

        /// <summary>
        /// 子孙节点移除时触发。从 <see cref="UpdateManager"/> 注销该节点。
        /// <para>按 <see cref="IUpdateLifecycle"/> 注销即可覆盖全部时机：门面会把注销转发给各时机，
        /// 只有持有它的那套会真正删除。</para>
        /// </summary>
        void OnDescendantRemoved(BaseNode node)
        {
            if (node is IUpdateLifecycle lifecycle)
                UpdateManager.Unregister(lifecycle);
        }

        #endregion
    }
}
