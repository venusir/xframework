using System;

namespace XFramework.XReactive
{

    /// <summary>
    /// 响应式属性。值变化时自动推送新值。
    /// </summary>
    public interface IReactiveProperty<T> : IReadonlySignal<T>
    {
        T Value { get; }
    }
}
