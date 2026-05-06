using System;
using R3;

namespace XFramework
{
    /// <summary>
    /// 基于 R3 Subject 的信号实现（无参数版）。
    /// </summary>
    internal sealed class Signal : ISignal
    {
        private readonly Subject<Unit> _subject = new();

        public IDisposable Subscribe(Action onNext)
            => _subject.Subscribe(_ => onNext());

        public void Publish()
            => _subject.OnNext(default);

        public void Dispose()
            => _subject.Dispose();
    }

    /// <summary>
    /// 基于 R3 Subject 的信号实现。
    /// </summary>
    internal sealed class Signal<T> : ISignal<T>
    {
        private readonly Subject<T> _subject = new();

        public IDisposable Subscribe(Action<T> onNext)
            => _subject.Subscribe(onNext);

        public void Publish(T value)
            => _subject.OnNext(value);

        public void Dispose()
            => _subject.Dispose();
    }
}
