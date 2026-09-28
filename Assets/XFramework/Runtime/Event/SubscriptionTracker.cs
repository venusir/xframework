namespace XFramework.XEvent
{
    /// <summary>
    /// 订阅跟踪器(仅 Editor):统计**当前存活的订阅数**(事件流 + 消息模块的异步登记合计),
    /// 供测试断言「本用例没有泄漏订阅」——用法是取 mark、比 mark:
    /// <code>
    /// var mark = SubscriptionTracker.LiveCount;
    /// // ... 用例主体 ...
    /// Assert.AreEqual(mark, SubscriptionTracker.LiveCount, "本用例泄漏了订阅");
    /// </code>
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不在 <c>.Internal</c> 里</b>:本类型是**有意为之的跨模块共享设施**——它自己的
    /// 「两处登记点」约束就要求消息模块也调它。放进 <c>XFramework.XEvent.Internal</c> 会让消息侧只能
    /// <c>using XFramework.XEvent.Internal;</c> 才够得着,而 <c>ModuleBoundaryTests</c> 判它为「跨模块引用
    /// 他人 Internal」——那条规则防的是**实现细节外漏**,共享设施放进去是分类错误。可见性仍是
    /// <c>internal</c>(仅程序集内可见,Release 下整段不编译),故此处移动的是**命名空间**而非公开面。</para>
    /// <para><b>为什么它能这么便宜</b>:全仓订阅只有两处登记点——<see cref="XFramework.XEvent.Internal.EventStreamImpl{T}"/>(流)与
    /// <c>XFramework.XMessage.Internal.MessageChannel</c>(异步登记)。Rx 系实现的每个 Observer 各自登记,
    /// 做不到只挂两处。</para>
    /// <para><b>四条硬约束(全是反面教训,改它之前先读)</b></para>
    /// <list type="number">
    /// <item><description>仅 Editor 编译:Release 下整段是空操作(与 <c>MainThreadGuard</c>、<c>Pool&lt;T&gt;</c>
    /// 的活跃集同法),订阅热路径上只剩一次静态 <c>bool</c> 读。</description></item>
    /// <item><description><b>必须可复位</b>:<see cref="Reset"/> 由域重载与每个 fixture 的 SetUp 调用——
    /// 缺了它,跟踪器自己就会成为泄漏源,而它正是来抓这个的。</description></item>
    /// <item><description><b>diff 语义,不是「必须为 0」</b>:长期存活的订阅是合法的(单例上的属性、Settings
    /// 句柄;Reactive README 明写「源长期存活时只增不减**不必然是泄漏**」),故断言只应比较「用例前后是否
    /// 相同」,不要提供「当前必须为 0」之类的辅助——那必然误报。</description></item>
    /// <item><description>两处登记点都要接:只挂流侧会漏掉消息模块的异步订阅。</description></item>
    /// </list>
    /// <para><b>将来若要「是谁泄漏的」</b>(Rx 的 ObservableTracker 那种带创建调用栈的窗口):在
    /// <see cref="OnSubscribe"/> 里加栈捕获即可,调用方不必改——本类型刻意做成单一咽喉。</para>
    /// </remarks>
    internal static class SubscriptionTracker
    {
#if UNITY_EDITOR
        /// <summary>是否启用跟踪(默认开)。订阅热路径上只读它一次。</summary>
        internal static bool Enabled = true;

        private static int _liveCount;

        /// <summary>当前存活订阅数(流 + 异步登记合计)。</summary>
        internal static int LiveCount => System.Threading.Volatile.Read(ref _liveCount);

        /// <summary>登记一次订阅。</summary>
        internal static void OnSubscribe()
        {
            if (Enabled)
                System.Threading.Interlocked.Increment(ref _liveCount);
        }

        /// <summary>注销订阅;<paramref name="count"/> 用于 <c>Dispose</c> 这类一次性终止整个流的路径。</summary>
        internal static void OnUnsubscribe(int count = 1)
        {
            if (Enabled && count > 0)
                System.Threading.Interlocked.Add(ref _liveCount, -count);
        }

        /// <summary>清零。由域重载与每个 fixture 的 SetUp 调用(约束 2)。</summary>
        internal static void Reset() => System.Threading.Volatile.Write(ref _liveCount, 0);
#else
        /// <summary>Release:恒 0(整段跟踪不编译)。</summary>
        internal static int LiveCount => 0;

        /// <summary>Release:空操作。</summary>
        internal static void OnSubscribe()
        {
        }

        /// <summary>Release:空操作。</summary>
        internal static void OnUnsubscribe(int count = 1)
        {
        }

        /// <summary>Release:空操作。</summary>
        internal static void Reset()
        {
        }
#endif
    }
}
