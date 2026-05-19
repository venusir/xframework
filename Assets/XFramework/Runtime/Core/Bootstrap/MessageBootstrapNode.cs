using XFramework.XReactive;

namespace XFramework.XCore
{
    /// <summary>
    /// <see cref="MessageManager"/> 的清理节点。MessageManager 是纯静态类，无需异步初始化，
    /// 仅需在节点销毁时调用 <see cref="MessageManager.Clear()"/> 清理资源。
    /// </summary>
    internal sealed class MessageBootstrapNode : EntityNode
    {
        #region Lifecycle

        protected override void OnDestroy()
        {
            MessageManager.Clear();
            base.OnDestroy();
        }

        #endregion
    }
}
