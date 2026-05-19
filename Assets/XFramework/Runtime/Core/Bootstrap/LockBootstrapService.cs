using System;
using XFramework.XLock;

namespace XFramework.XCore
{
    /// <summary>
    /// <see cref="LockManager"/> 的 <see cref="IModuleService"/> 适配器。
    /// <para>LockManager 是静态类，无需异步初始化，直接标记为已完成。</para>
    /// </summary>
    internal sealed class LockBootstrapService : IModuleService
    {
        #region IModuleService

        /// <summary>
        /// LockManager 是纯静态类，无需显式初始化，始终返回 true。
        /// </summary>
        public bool IsInitialized => true;

        public void Dispose()
        {
            LockManager.Dispose();
        }

        #endregion
    }
}
