using System;
using R3;

namespace XFramework.XReactive
{
    /// <summary>
    /// 响应式属性。包含一个可自动推送变化的响应式值。
    /// <para>适用于血量、分数、状态等需要被监听的属性。不依赖场景节点树，可在任意 C# 类中使用。</para>
    /// <para>内部基于 R3 实现，其他模块通过此类使用响应式属性，无需直接依赖 R3。</para>
    /// <para>使用完毕后需调用 <see cref="Dispose"/> 释放内部订阅。</para>
    /// </summary>
    /// <typeparam name="T">值的类型。</typeparam>
    public class ReactiveProperty<T> : IDisposable
    {
        #region Private Fields

        private R3.ReactiveProperty<T> _value;

        #endregion

        #region Constructors

        /// <summary>
        /// 创建响应式属性，使用类型的默认值作为初始值。
        /// </summary>
        public ReactiveProperty()
        {
            _value = new R3.ReactiveProperty<T>(default);
        }

        /// <summary>
        /// 创建响应式属性并指定初始值。
        /// </summary>
        /// <param name="initialValue">初始值。</param>
        public ReactiveProperty(T initialValue)
        {
            _value = new R3.ReactiveProperty<T>(initialValue);
        }

        #endregion

        #region Public Properties

        /// <summary>获取或设置值。设置时自动通知所有订阅者。</summary>
        public T Value
        {
            get => _value.Value;
            set => _value.Value = value;
        }

        #endregion

        #region Internal — R3 Bridge

        /// <summary>
        /// 暴露内部 R3 ReactiveProperty 引用，供 XReactive 内部的扩展方法链式操作时使用。
        /// <para>第三方代码不应直接调用此方法。</para>
        /// </summary>
        internal R3.ReactiveProperty<T> GetR3Value() => _value;

        /// <summary>
        /// 从给定的 ReactiveProperty 中提取内部 R3 reactive property。
        /// </summary>
        internal static R3.ReactiveProperty<T> GetR3Value(ReactiveProperty<T> property)
        {
            if (property == null)
                throw new ArgumentNullException(nameof(property));
            return property._value;
        }

        #endregion

        #region Subscribe

        /// <summary>
        /// 订阅值变化。每次值改变时回调 <paramref name="onNext"/>。
        /// <para>返回的 <see cref="IDisposable"/> 可用于手动取消订阅。
        /// 调用 <see cref="Dispose"/> 时也会自动取消所有订阅。</para>
        /// </summary>
        /// <param name="onNext">值改变时的回调。订阅时不会立即回调当前值（与 R3 行为一致）。</param>
        /// <returns>订阅句柄，可用于取消订阅。</returns>
        public IDisposable Subscribe(Action<T> onNext)
        {
            return _value.Subscribe(onNext);
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// 释放内部 ReactiveProperty，取消所有订阅。
        /// <para>此后访问 <see cref="Value"/> 会抛出异常。</para>
        /// </summary>
        public void Dispose()
        {
            _value?.Dispose();
            _value = null;
        }

        #endregion
    }
}
