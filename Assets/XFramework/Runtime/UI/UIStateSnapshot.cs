namespace XFramework.XUI
{
    /// <summary>
    /// UI 子系统状态的一次快照。
    /// <para>纯值类型、无引用字段，读取时零分配——可以放心放进调试面板的刷新循环。</para>
    /// <para>用于回答「面板是不是漏关了」「遮罩为什么还亮着」「有几个打开卡在半路」这类
    /// 只能靠翻运行时状态才能定位的问题。</para>
    /// </summary>
    public readonly struct UIStateSnapshot
    {
        /// <summary>已打开的面板数。</summary>
        public readonly int OpenCount;

        /// <summary>在途打开的数量（同类型并发去重表里的条目数）。长期不为 0 说明有打开卡住了。</summary>
        public readonly int InFlightOpenCount;

        /// <summary>遮罩当前被持有的引用数。大于 0 却看不见遮罩，说明有持有者忘了释放。</summary>
        public readonly int MaskRefCount;

        /// <summary>遮罩是否正在显示。</summary>
        public readonly bool IsMaskShowing;

        /// <summary>显示栈中是否还有可退回的面板。</summary>
        public readonly bool CanGoBack;

        internal UIStateSnapshot(int openCount, int inFlightOpenCount, int maskRefCount,
            bool isMaskShowing, bool canGoBack)
        {
            OpenCount = openCount;
            InFlightOpenCount = inFlightOpenCount;
            MaskRefCount = maskRefCount;
            IsMaskShowing = isMaskShowing;
            CanGoBack = canGoBack;
        }

        /// <summary>
        /// 单行摘要，便于塞进日志或调试面板标题。
        /// </summary>
        public override string ToString()
        {
            return $"open={OpenCount} inFlight={InFlightOpenCount} mask={MaskRefCount}" +
                   $"(showing={IsMaskShowing}) canGoBack={CanGoBack}";
        }
    }
}
