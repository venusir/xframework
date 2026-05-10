using UnityEngine;
using XFramework.XCore;

namespace XFramework.XUpdate

{
    /// <summary>
    /// 更新服务节点。作为 <see cref="LeafNode"/> 挂载到节点树中，提供全局 Update 调度能力。
    /// <para>其他节点通过 <see cref="BaseNode.Get{T}"/> 获取此服务。</para>
    /// <para>自动监听节点树的添加/移除事件，注册/注销 <see cref="IUpdateable"/> 节点。</para>
    /// </summary>
    public class UpdateNode : LeafNode, IUpdateNode
    {
        #region Private Fields

        /// <summary>内部调度器，负责 LOD 分桶、时间切片等纯调度逻辑。</summary>
        private readonly UpdateScheduler _scheduler = new UpdateScheduler();

        #endregion

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
            _scheduler.Clear();
            base.OnDestroy();
        }

        #endregion

        #region IUpdateNode Implementation

        /// <summary>
        /// 执行一帧更新。按 <see cref="UpdateLOD"/> 时间切片算法分发更新。
        /// <para>每帧调用一次，建议在 MonoBehaviour.Update 中调用。</para>
        /// </summary>
        /// <param name="time">当前时间（<see cref="Time.time"/>），由外部传入避免重复获取。</param>
        public void Tick(float time) => _scheduler.Tick(time);

        /// <summary>
        /// 启用指定节点的 Update 调用。
        /// <para>会触发 <see cref="IUpdateable.OnEnable"/>。</para>
        /// </summary>
        public void Enable(IUpdateable node) => _scheduler.Enable(node);

        /// <summary>
        /// 禁用指定节点的 Update 调用。
        /// <para>会触发 <see cref="IUpdateable.OnDisable"/>。</para>
        /// </summary>
        public void Disable(IUpdateable node) => _scheduler.Disable(node);

        /// <summary>
        /// 检查节点是否处于启用状态。
        /// </summary>
        public bool IsEnabled(IUpdateable node) => _scheduler.IsEnabled(node);

        /// <summary>
        /// 立即对指定节点执行一次更新并重新调整 LOD。
        /// <para>用于外部逻辑变化时需要立即响应，不等下一次时间切片。</para>
        /// </summary>
        public void ProcessImmediate(IUpdateable node, float deltaTime, float time)
            => _scheduler.ProcessImmediate(node, deltaTime, time);

        #endregion

        #region Private Methods - Event Subscription

        /// <summary>
        /// 尝试注册 <see cref="IUpdateable"/> 节点。
        /// <para>仅当节点已 Start 时才立即注册，否则等待 <see cref="OnDescendantStarted"/> 事件。</para>
        /// </summary>
        void TryRegister(BaseNode node)
        {
            if (node is IUpdateable u && node.Started)
                _scheduler.Register(u, node.Depth);
        }

        /// <summary>
        /// 子孙节点添加时触发。已 Start 的 <see cref="IUpdateable"/> 立即注册，未 Start 的等待 Start 事件。
        /// </summary>
        void OnDescendantAdded(BaseNode node)
        {
            if (node is IUpdateable u && node.Started)
                _scheduler.Register(u, node.Depth);
        }

        /// <summary>
        /// 子孙节点 Start 完成时触发。注册 <see cref="IUpdateable"/> 节点。
        /// </summary>
        void OnDescendantStarted(BaseNode node)
        {
            if (node is IUpdateable u)
                _scheduler.Register(u, node.Depth);
        }

        /// <summary>
        /// 子孙节点移除时触发。注销 <see cref="IUpdateable"/> 节点。
        /// </summary>
        void OnDescendantRemoved(BaseNode node)
        {
            if (node is IUpdateable u)
                _scheduler.Unregister(u);
        }

        #endregion
    }
}
