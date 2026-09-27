using System;
using System.Threading;

namespace XFramework.XMessage.Internal
{
    /// <summary>
    /// 将任意 Action 包装为 IDisposable 的轻量实现（委托工厂）。
    /// </summary>
    /// <remarks>
    /// 幂等:Dispose 只执行一次,重复调用被忽略(Interlocked.Exchange 置空后判断)。
    /// </remarks>
    internal sealed class ActionDisposable : IDisposable
    {
        private Action _dispose;

        private ActionDisposable(Action dispose)
        {
            _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
        }

        /// <summary>创建包装指定 Action 的 IDisposable。</summary>
        public static IDisposable Create(Action dispose) => new ActionDisposable(dispose);

        /// <summary>
        /// 空句柄:「无需登记」路径(已完成的事件流、令牌已取消的订阅)共用同一实例。
        /// </summary>
        /// <remarks>
        /// <para>三条不变量:①<b>不得返回 <c>null</c></b>——调用方(<c>MessageManager.TryBindToDestroy</c>、
        /// <c>EventSubscription.Dispose</c>)会直接调 <see cref="Dispose"/>,不判空;②共享实例安全,
        /// 因为释放动作是空操作,而 <see cref="Dispose"/> 幂等;③<b>本类型不得改为池化</b>——共享实例的
        /// <c>_dispose</c> 字段会在首次释放时被置空,若实例被归还进池并重复发放,后续拿到它的调用方
        /// 会误以为自己持有的是一个可用的句柄。</para>
        /// <para>共享的意义是「空句柄只有一处来源」:引擎(EventStream)与 broker 若各造各的,
        /// 两份实现对上述不变量的理解就会各自漂移。</para>
        /// </remarks>
        internal static readonly IDisposable Empty = Create(static () => { });

        /// <summary>执行一次释放动作,重复调用忽略。</summary>
        public void Dispose()
        {
            var action = Interlocked.Exchange(ref _dispose, null);
            action?.Invoke();
        }
    }
}
