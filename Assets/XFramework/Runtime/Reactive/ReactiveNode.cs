using System;

namespace XFramework
{
    /// <summary>
    /// 响应式节点。包含一个可自动推送变化的响应式值。
    /// <para>适用于血量、分数、状态等需要被监听的属性。</para>
    /// </summary>
    /// <typeparam name="T">值的类型。</typeparam>
    public class ReactiveNode<T> : LeafNode
    {
        #region Private Fields

        private ReactiveProperty<T> _value;

        #endregion

        #region Public Properties

        /// <summary>响应式值接口。可通过 Subscribe 监听值变化。</summary>
        public IReactiveProperty<T> ValueProperty => _value;

        /// <summary>获取或设置值。设置时自动通知所有订阅者。</summary>
        public T Value
        {
            get => _value != null ? _value.Value : default;
            set
            {
                if (_value != null)
                {
                    // 通过反射设置 R3 ReactiveProperty 的值
                    // 这里使用内部方法设置
                    SetValueInternal(value);
                }
            }
        }

        #endregion

        #region Lifecycle Overrides

        protected override void OnAwake()
        {
            _value ??= new ReactiveProperty<T>(default);
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
                _value = new ReactiveProperty<T>(initialValue);
            }
        }

        #endregion

        #region Private Methods

        private void SetValueInternal(T value)
        {
            _value.Value = value;
        }

        #endregion
    }
}
