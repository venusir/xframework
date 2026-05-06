using System;
using R3;

namespace XFramework
{
    /// <summary>
    /// 响应式属性节点。包含一个可自动推送变化的响应式值。
    /// <para>适用于血量、分数、状态等需要被监听的属性。</para>
    /// <para>节点本身实现了 <see cref="IReactiveProperty{T}"/>，可直接通过 <see cref="Subscribe"/> 监听值变化。</para>
    /// </summary>
    /// <typeparam name="T">值的类型。</typeparam>
    public class ReactiveProperty<T> : LeafNode, IReactiveProperty<T>
    {
        #region Private Fields

        private R3.ReactiveProperty<T> _value;

        #endregion

        #region Public Properties

        /// <summary>获取或设置值。设置时自动通知所有订阅者。</summary>
        public T Value
        {
            get => _value != null ? _value.Value : default;
            set
            {
                if (_value != null)
                {
                    _value.Value = value;
                }
            }
        }

        #endregion

        #region IReadonlySignal

        /// <summary>订阅值变化。节点销毁时自动取消订阅。</summary>
        public IDisposable Subscribe(Action<T> onNext)
            => _value?.Subscribe(onNext);

        #endregion

        #region Lifecycle Overrides

        protected override void OnAwake()
        {
            _value ??= new R3.ReactiveProperty<T>(default);
        }

        protected override void OnDestroy()
        {
            _value?.Dispose();
            _value = null;
        }

        protected override void OnInit(object arg)
        {
            if (arg is T initialValue)
            {
                _value?.Dispose();
                _value = new R3.ReactiveProperty<T>(initialValue);
            }
        }

        #endregion
    }
}
