using System;
using XFramework.XEvent.Internal;

namespace XFramework.XEvent
{
    /// <summary>
    /// 事件流工厂。与 <c>XFramework.XPipeline.Pipeline.Create()</c> 同形:公开接口 + 静态工厂 + internal 实现——
    /// 引擎内部(节点池、派发快照、锁与快照的取舍)可以自由演进,不经公开面泄漏。
    /// </summary>
    public static class EventStream
    {
        /// <summary>
        /// 创建一条事件流。
        /// </summary>
        /// <typeparam name="T">事件载荷类型。</typeparam>
        /// <param name="onEmpty">
        /// 订阅数由 1 归零时的回调(在锁外调用),供持有者回收空容器;可为 <c>null</c>。
        /// <para><b>两条硬约束:</b>① 回调<b>可能发生在派发途中</b>(订阅者在自己的回调里退订自身);
        /// ② 因此回调只应摘除持有者自己的表项,<b>不得回头调用本流的任何方法</b>。</para>
        /// <para>它是构造期一次性接线(不是可变属性):订阅 / 退订路径因此零额外分配,且「谁负责回收」
        /// 在类型形状上唯一——先例是 <c>XPool.Pool&lt;T&gt;</c> 把 onRent / onReturn / onDestroy 放在构造函数上。</para>
        /// </param>
        public static IEventStream<T> Create<T>(Action onEmpty = null) => new EventStreamImpl<T>(onEmpty);

        /// <summary>
        /// 创建一条带缓冲的事件流(新订阅者立即收到最近一条)。
        /// <para><b>刻意不支持领取「空流通知」</b>:缓冲流能否回收取决于「有没有缓存值」,而丢弃缓存会破坏
        /// 「订阅前投递可重放」的语义——引擎无法在丢缓存之前参与回收判别,给一个空流回调只会误导。
        /// 缓冲流的释放路径只有持有者显式淘汰(见 <c>XMessage</c> 的 `EvictBufferedChannel` 系列)。</para>
        /// </summary>
        /// <typeparam name="T">事件载荷类型。</typeparam>
        public static IBufferedEventStream<T> CreateBuffered<T>() => new BufferedEventStreamImpl<T>();
    }
}
