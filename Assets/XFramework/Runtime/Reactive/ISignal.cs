using System;

namespace XFramework
{
    /// <summary>
    /// 只读信号。可订阅但不可推送。
    /// </summary>
    public interface IReadonlySignal<out T>
    {
        IDisposable Subscribe(Action<T> onNext);
    }

    /// <summary>
    /// 完整信号。可订阅也可推送。
    /// </summary>
    public interface ISignal<T> : IReadonlySignal<T>
    {
        void Publish(T value);
    }
}
