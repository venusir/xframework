using System;

namespace XFramework.XReactive.Internal
{
    /// <summary>
    /// 带缓冲的 Subject:新订阅者会立即同步收到最近一次投递的消息(重放先于实时)。
    /// <para>替代 R3.ReplaySubject(1) 的自研实现(移除 R3 依赖计划 Phase 1)。</para>
    /// </summary>
    /// <remarks>
    /// 语义(实测 R3 行为后固化):
    /// - 订阅时同步重放最近一条;无消息时不重放,从实时消息开始
    /// - 每个订阅者各自收到重放
    /// - 重放先于实时消息(订阅后立即投递的新消息排在重放之后)
    /// 线程模型:与 Subject 相同(锁 + 快照);重放的读取在锁内取缓存、锁外调用。
    /// 注意:重放与订阅之间若发生并发 OnNext,顺序不保证(本项目使用场景为主线程,可接受)。
    /// </remarks>
    internal sealed class ReplaySubject<T> : Subject<T>
    {
        #region Private Fields

        private readonly object _sync = new object();
        private T _last;
        private bool _hasLast;

        #endregion

        #region Public API

        /// <summary>
        /// 订阅消息,并立即同步重放最近一次投递的消息(若有)。
        /// <para>重放也经过 filter 与 preHandler 槽位,与实时消息路径一致。</para>
        /// </summary>
        public new IDisposable Subscribe(Action<T> onNext, Action<T> preHandler = null, Func<T, bool> filter = null)
        {
            var handle = base.Subscribe(onNext, preHandler, filter);

            // 锁内取缓存、锁外重放:避免持锁调用用户代码
            T replay;
            bool hasReplay;
            lock (_sync)
            {
                hasReplay = _hasLast;
                replay = _last;
            }

            if (hasReplay)
            {
                // 重放路径复用 Subject 的统一投递语义(filter → preHandler → onNext,异常隔离)
                Subject<T>.Deliver(replay, preHandler, filter, onNext);
            }

            return handle;
        }

        /// <summary>投递消息并缓存为最近一条(供新订阅者重放)。</summary>
        public new void OnNext(T value)
        {
            lock (_sync)
            {
                _last = value;
                _hasLast = true;
            }
            base.OnNext(value);
        }

        /// <summary>清空缓存并完成 Subject。</summary>
        public new void OnCompleted()
        {
            lock (_sync)
            {
                _hasLast = false;
                _last = default;
            }
            base.OnCompleted();
        }

        /// <summary>释放所有订阅并清空缓存。</summary>
        public new void Dispose()
        {
            lock (_sync)
            {
                _hasLast = false;
                _last = default;
            }
            base.Dispose();
        }

        #endregion
    }
}
