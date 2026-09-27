using System;

namespace XFramework.XEvent
{
    /// <summary>
    /// 事件流:订阅、投递、完成、释放。一个对象即一条流,订阅者按订阅的逆序收到投递(后订阅先收到)。
    /// <para><b>它是什么:</b>把 C# <c>event</c> 提升为一等公民的形态——可在字段里持有、可作为参数传递、
    /// 可把「退订」以句柄交出去。相比 <c>event</c> 多出四件事:①订阅返回幂等句柄;②单个订阅回调抛异常
    /// 不影响其余订阅者;③订阅数可读(诊断,以及上层据此回收空容器);④「最后一个订阅者离开」可通知持有者。</para>
    /// <para><b>它不是消息总线:</b>流不知道「有哪些频道」,没有类型 / Key 寻址,没有过滤器管道与异步登记
    /// ——那些是 <c>XFramework.XMessage</c> 的职责(总线按类型 / Key 持有并治理一批流)。判据:状态是
    /// 「一条流」用本模块,状态是「一张表」用 Message。</para>
    /// <para><b>线程:</b>按主线程使用设计。内部的锁与快照只用于让订阅链表在并发退订下不被写坏,
    /// <b>不构成跨线程发布 / 订阅的许可</b>(详见模块 README「线程」节)。</para>
    /// </summary>
    /// <typeparam name="T">事件载荷类型。</typeparam>
    public interface IEventStream<T> : IDisposable
    {
        /// <summary>
        /// 订阅。返回的句柄 Dispose 后不再收到投递(幂等)。
        /// <para>已 <see cref="OnCompleted"/> 的流返回共享的空句柄——不登记、也不投递。</para>
        /// <para><b>退订在同一轮派发内立即生效</b>:派发途中退订自身或他人,该订阅者本轮起就不再被调用。
        /// 这一点与 C# <c>event</c> 不同——后者的调用表是派发时的快照,已退订的回调本轮仍会被调一次。</para>
        /// </summary>
        /// <param name="onNext">事件回调,不可为 <c>null</c>。</param>
        /// <exception cref="ArgumentNullException"><paramref name="onNext"/> 为 <c>null</c> 时抛出。</exception>
        IDisposable Subscribe(Action<T> onNext);

        /// <summary>
        /// 投递事件给所有存活订阅者。
        /// <para>订阅回调抛出的异常被捕获并记 Error 日志(<c>[Event]</c> 前缀),不传播给调用方,
        /// 也不移除该订阅者——同一轮内其余订阅者照常收到。</para>
        /// <para>回调内重入本方法会递归派发(嵌套那一轮先跑完);回调内退订自身或他人由快照与标志安全处理。</para>
        /// </summary>
        /// <param name="value">事件载荷。</param>
        void OnNext(T value);

        /// <summary>
        /// 标记完成:之后的 <see cref="OnNext"/> 被忽略,已订阅者不再收到投递。
        /// <para><b>与 <see cref="IDisposable.Dispose"/> 的差别是刻意行为,两条都被消费方依赖:</b>
        /// <c>OnCompleted</c> 只置终止标志、<b>不清订阅</b>——此前已发出的句柄从此静默失效(调用方无法把
        /// 「不再收到」与「值没变」区分开),此后 <see cref="Subscribe"/> 一律返回空句柄;而 <c>Dispose</c>
        /// 会清空订阅链表并回收节点。</para>
        /// </summary>
        void OnCompleted();

        /// <summary>
        /// 当前存活订阅数(订阅递增,退订与释放递减)。
        /// <para><b>拉取面</b>(与 <c>XFramework.XPipeline.IPipeline.Status</c> 同族):供诊断与上层决策
        /// (例如持有者据此回收空容器),<b>不是</b>可供业务分支依赖的契约——它反映的是实现此刻的状态。
        /// 无锁读取;本引擎按主线程使用设计。</para>
        /// </summary>
        int SubscriptionCount { get; }
    }
}
