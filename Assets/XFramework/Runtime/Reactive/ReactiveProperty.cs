using System;
using R3;

namespace XFramework
{
    /// <summary>
    /// 基于 R3 ReactiveProperty 的响应式属性实现。
    /// </summary>
    internal sealed class ReactiveProperty<T> : IReactiveProperty<T>
    {
        private readonly R3.ReactiveProperty<T> _property;

        public T Value => _property.Value;

        public ReactiveProperty(T initialValue)
        {
            _property = new R3.ReactiveProperty<T>(initialValue);
        }

        public IDisposable Subscribe(Action<T> onNext)
            => _property.Subscribe(onNext);

        public void Dispose()
            => _property.Dispose();
    }
}
