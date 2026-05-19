using System;
using XFramework.XReactive;

namespace XFramework.XCore
{
    /// <summary>
    /// <see cref="MessageManager"/> 的 <see cref="IModuleService"/> 适配器。
    /// <para>MessageManager 是静态类，无需异步初始化，直接标记为已完成。</para>
    /// </summary>
    internal sealed class MessageBootstrapService : IModuleService
    {
        #region IModuleService

        /// <summary>
        /// MessageManager 是纯静态类，无需显式初始化，始终返回 true。
        /// </summary>
        public bool IsInitialized => true;

        public void Dispose()
        {
            MessageManager.Clear();
        }

        #endregion
    }
}
