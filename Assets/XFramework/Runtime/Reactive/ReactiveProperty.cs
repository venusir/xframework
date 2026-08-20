using System;
using System.Collections.Generic;
using XFramework.XReactive.Internal;

namespace XFramework.XReactive
{
    /// <summary>
    /// 响应式属性。包含一个可自动推送变化的响应式值。
    /// <para>适用于血量、分数、状态等需要被监听的属性。不依赖场景节点树，可在任意 C# 类中使用。</para>
    /// <para>基于自研 Subject 实现(移除 R3 依赖计划 Phase 3)。</para>
    /// <para>使用完毕后需调用 <see cref="Dispose"/> 释放内部订阅。</para>
    /// </summary>
    /// <typeparam name="T">值的类型。</typeparam>
    /// <remarks>
    /// 行为契约(实测 R3 后固化,与 R3.ReactiveProperty 一致):
    /// - <see cref="Subscribe"/> 订阅时立即同步回调当前值
    /// - 设置相同值不通知(自带 DistinctUntilChanged 语义)
    /// - <see cref="Dispose"/> 后访问 <see cref="Value"/> 抛 <see cref="ObjectDisposedException"/>,再次 Subscribe 同样抛出
    /// </remarks>
    public class ReactiveProperty<T> : IDisposable
    {
        #region Private Fields

        private readonly Subject<T> _subject = new();
        private T _value;
        private bool _disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// 创建响应式属性，使用类型的默认值作为初始值。
        /// </summary>
        public ReactiveProperty()
            : this(default)
        {
        }

        /// <summary>
        /// 创建响应式属性并指定初始值。
        /// </summary>
        /// <param name="initialValue">初始值。</param>
        public ReactiveProperty(T initialValue)
        {
            _value = initialValue;
        }

        #endregion

        #region Public Properties

        /// <summary>获取或设置值。设置时自动通知所有订阅者(相同值不通知)。</summary>
        /// <exception cref="ObjectDisposedException">属性已释放时抛出。</exception>
        public T Value
        {
            get
            {
                ThrowIfDisposed();
                return _value;
            }
            set
            {
                ThrowIfDisposed();
                // 去重语义:相同值不通知(与 R3 行为一致,探针 1c 实测)
                if (EqualityComparer<T>.Default.Equals(_value, value))
                    return;
                _value = value;
                _subject.OnNext(value);
            }
        }

        #endregion

        #region Subscribe

        /// <summary>
        /// 订阅值变化。订阅时立即回调当前值,之后每次值改变时回调 <paramref name="onNext"/>。
        /// <para>返回的 <see cref="IDisposable"/> 可用于手动取消订阅。
        /// 调用 <see cref="Dispose"/> 时也会自动取消所有订阅。</para>
        /// </summary>
        /// <param name="onNext">值变化时的回调。</param>
        /// <returns>订阅句柄，可用于取消订阅。</returns>
        /// <exception cref="ArgumentNullException">onNext 为 null 时抛出。</exception>
        /// <exception cref="ObjectDisposedException">属性已释放时抛出。</exception>
        public IDisposable Subscribe(Action<T> onNext)
        {
            if (onNext == null) throw new ArgumentNullException(nameof(onNext));
            ThrowIfDisposed();

            // 先注册再立即回调(与 R3 一致):确保回调中的订阅操作不会丢失后续消息
            var handle = _subject.Subscribe(onNext);
            onNext(_value);
            return handle;
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// 释放内部 Subject，取消所有订阅。
        /// <para>此后访问 <see cref="Value"/> 或再次 <see cref="Subscribe"/> 会抛出 <see cref="ObjectDisposedException"/>。</para>
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _subject.Dispose();
        }

        #endregion

        #region Private

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name,
                    $"[Reactive] ReactiveProperty<{typeof(T).Name}> 已释放,请勿再访问 Value 或订阅。");
        }

        #endregion
    }
}
