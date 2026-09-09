using System;

namespace XFramework.XMessage.Internal
{
    /// <summary>
    /// 带缓冲的事件流:新订阅者会立即同步收到最近一次投递的事件(重放先于实时)。
    /// </summary>
    /// <remarks>
    /// 语义:
    /// - 订阅时同步重放最近一条;无事件时不重放,从实时事件开始
    /// - 每个订阅者各自收到重放
    /// - 重放先于实时事件(订阅后立即投递的新事件排在重放之后)
    /// 线程模型:与 EventStream 相同(锁 + 快照);重放的读取在锁内取缓存、锁外调用。
    /// 注意:重放与订阅之间若发生并发 OnNext,顺序不保证(本项目使用场景为主线程,可接受)。
    /// </remarks>
    internal sealed class BufferedEventStream<T> : EventStream<T>
    {
        #region Private Fields

        private readonly object _sync = new object();
        private T _last;
        private bool _hasLast;

        #endregion

        #region Public API

        /// <summary>
        /// 订阅事件流,并立即同步重放最近一次投递的事件(若有)。
        /// <para>重放与实时共用同一订阅回调:订阅侧过滤等逻辑由订阅闭包自身表达,两路径行为一致。</para>
        /// </summary>
        /// <param name="onNext">事件回调,不可为 null。</param>
        public new IDisposable Subscribe(Action<T> onNext)
        {
            var handle = base.Subscribe(onNext);

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
                // 重放路径复用 EventStream 的统一投递语义(回调异常隔离),与实时行为一致
                EventStream<T>.Deliver(replay, onNext);
            }

            return handle;
        }

        /// <summary>投递事件并缓存为最近一条(供新订阅者重放)。</summary>
        public new void OnNext(T value)
        {
            lock (_sync)
            {
                _last = value;
                _hasLast = true;
            }
            base.OnNext(value);
        }

        /// <summary>清空缓存并完成事件流。</summary>
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
