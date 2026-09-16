using System;

namespace XFramework.XUI
{
    /// <summary>
    /// 一次模态遮罩持有的句柄。遮罩按引用计数：只有全部句柄都释放后才真正隐藏。
    /// <para>多个系统各自需要遮罩时（弹窗、引导、网络等待）不再互相踩——谁开谁负责关，
    /// 谁的流程结束都不会把别人的遮罩一起关掉。</para>
    /// <para>零 GC：<c>readonly struct</c>，只存所有者与令牌两个字段，不持有委托
    /// （与 <c>LockHandle</c> 同一惯例）。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// using (UIManager.Mask.Show(new UIMaskStyle(UILayers.Mask, Color.black, clickToClose: true)))
    /// {
    ///     await DoSomethingAsync();
    /// }   // 离开作用域即释放
    /// </code>
    /// </example>
    public readonly struct UIMaskHandle : IDisposable
    {
        private readonly UIManagerImpl _owner;
        private readonly int _token;

        internal UIMaskHandle(UIManagerImpl owner, int token)
        {
            _owner = owner;
            _token = token;
        }

        /// <summary>
        /// 句柄是否仍然有效（尚未释放且管理器仍在）。
        /// </summary>
        public bool IsValid
        {
            get { return _owner != null && _owner.IsMaskHandleAlive(_token); }
        }

        /// <summary>
        /// 释放本次持有。幂等——重复释放不会影响其它持有者。
        /// </summary>
        public void Dispose()
        {
            _owner?.ReleaseMask(_token);
        }
    }
}
