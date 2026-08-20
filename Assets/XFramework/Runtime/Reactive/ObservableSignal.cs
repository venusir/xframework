using System;
using XFramework.XReactive.Internal;

namespace XFramework.XReactive
{
    /// <summary>
    /// 将自研 Subject 包装为 IReadonlySignal 的可订阅信号。
    /// <para>替代原基于 R3 Observable 的包装(移除 R3 依赖计划 Phase 2):携带 filter/preHandler 订阅配置,
    /// 每个 Subscribe 调用转发给 Subject 的对应槽位。</para>
    /// </summary>
    internal sealed class ObservableSignal<T> : IReadonlySignal<T>
    {
        #region Private Fields

        private readonly Subject<T> _subject;
        private readonly Action<T> _preHandler;
        private readonly Func<T, bool> _filter;

        #endregion

        #region Public API

        /// <summary>包装指定 Subject,可选携带订阅配置。</summary>
        /// <param name="subject">数据源,不可为 null。</param>
        /// <param name="preHandler">在 onNext 之前执行的回调(如异步触发),可为 null。</param>
        /// <param name="filter">过滤条件,返回 false 的消息不投递,可为 null。</param>
        public ObservableSignal(Subject<T> subject, Action<T> preHandler = null, Func<T, bool> filter = null)
        {
            _subject = subject ?? throw new ArgumentNullException(nameof(subject));
            _preHandler = preHandler;
            _filter = filter;
        }

        /// <summary>订阅消息,返回的句柄 Dispose 后不再收到投递。</summary>
        public IDisposable Subscribe(Action<T> onNext)
            => _subject.Subscribe(onNext, _preHandler, _filter);

        #endregion
    }
}
