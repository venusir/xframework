using XFramework.XLock;

namespace XFramework.XCore
{
    /// <summary>
    /// <see cref="LockManager"/> 的清理节点。LockManager 是纯静态类，无需异步初始化，
    /// 仅需在节点销毁时调用 <see cref="LockManager.Dispose()"/> 清理资源。
    /// </summary>
    internal sealed class LockBootstrapNode : EntityNode
    {
        #region Lifecycle

        protected override void OnDestroy()
        {
            LockManager.Dispose();
            base.OnDestroy();
        }

        #endregion
    }
}
