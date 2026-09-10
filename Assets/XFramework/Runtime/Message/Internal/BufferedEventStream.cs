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
    /// <para>
    /// 本类持有自己的 _sync,与基类的锁<b>永不嵌套</b>:每个重写方法都先完成自身的锁内工作并离开锁,
    /// 再调用 base 实现。派生类无需也不得复用基类的锁。
    /// </para>
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
        /// </summary>
        /// <remarks>重放与实时共用同一订阅回调:订阅侧过滤等逻辑由订阅闭包自身表达,两路径行为一致。</remarks>
        /// <param name="onNext">事件回调,不可为 null。</param>
        public override IDisposable Subscribe(Action<T> onNext)
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
        /// <remarks>
        /// 已终止的流直接短路:基类 OnNext 会忽略该次投递、Subscribe 也只会返回空句柄,
        /// 此时写入缓存不仅滞留一条消息引用,还会让新订阅者收到一条本该被忽略的重放。
        /// 先读基类状态再进自己的锁,保证两把锁不嵌套。
        /// </remarks>
        public override void OnNext(T value)
        {
            if (IsCompleted)
                return;

            lock (_sync)
            {
                _last = value;
                _hasLast = true;
            }
            base.OnNext(value);
        }

        /// <summary>清空缓存并完成事件流。</summary>
        public override void OnCompleted()
        {
            lock (_sync)
            {
                _hasLast = false;
                _last = default;
            }
            base.OnCompleted();
        }

        /// <summary>释放所有订阅并清空缓存。</summary>
        public override void Dispose()
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
