using System;
using System.Collections.Generic;
using XFramework.XMessage.Internal;

namespace XFramework.XReactive
{
    /// <summary>
    /// 响应式属性。包含一个可自动推送变化的响应式值。
    /// <para>适用于血量、分数、状态等需要被监听的属性。不依赖任何场景对象，可在任意 C# 类中使用。</para>
    /// <para>基于 XMessage 事件流引擎实现。实现 <see cref="IReactiveProperty{T}"/> 接口,可面向接口编程。</para>
    /// <para>使用完毕后需调用 <see cref="Dispose"/> 释放内部订阅。</para>
    /// </summary>
    /// <typeparam name="T">值的类型。</typeparam>
    /// <remarks>
    /// 行为契约:
    /// - <see cref="Subscribe"/> 订阅时立即同步回调当前值
    /// - 设置相同值不通知(去重语义)
    /// - <see cref="Dispose"/> 后<b>读取</b> <see cref="Value"/> 仍返回最后持有的值(宽容读取,与
    ///   <see cref="ReadOnlyReactiveProperty{T}"/> 一致);<b>写入</b> <see cref="Value"/> 与再次
    ///   <see cref="Subscribe"/> 抛 <see cref="ObjectDisposedException"/>
    /// </remarks>
    public class ReactiveProperty<T> : IReactiveProperty<T>, IReactivePropertyWriter<T>, IDisposable
    {
        #region Private Fields

        private readonly EventStream<T> _stream = new();
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

        /// <summary>
        /// 获取或设置值。设置时自动通知所有订阅者(相同值不通知)。
        /// <para>getter 宽容读取:已释放时仍返回最后持有的值、不抛异常。读一个失效对象的当前值
        /// 不改变任何状态,让它抛异常只会把「先 Dispose 再读一次收尾值」这类正常写法变成地雷;
        /// 这也使本属性与 <see cref="ReadOnlyReactiveProperty{T}"/> 的 <see cref="Value"/> 语义一致。
        /// setter 则相反——向已释放的属性写入是编程错误,照常抛异常。</para>
        /// </summary>
        /// <exception cref="ObjectDisposedException">属性已释放时<b>写入</b>抛出;读取不抛。</exception>
        public T Value
        {
            get
            {
                // 宽容读取:不查 _disposed(理由见属性文档)
                return _value;
            }
            set
            {
                ThrowIfDisposed();
                // 去重语义:相同值不通知
                if (EqualityComparer<T>.Default.Equals(_value, value))
                    return;
                _value = value;
                _stream.OnNext(value);
            }
        }

        #endregion

        #region Subscribe

        /// <summary>
        /// 订阅值变化。订阅时立即回调当前值,之后每次值改变时回调 <paramref name="onNext"/>。
        /// <para>返回的 <see cref="IDisposable"/> 可用于手动取消订阅。
        /// 调用 <see cref="Dispose"/> 时也会自动取消所有订阅。</para>
        /// <para>立即回调是注册期同步执行的代码,它抛出的异常原样上抛(订阅已自动清理,不会泄漏)；
        /// 之后投递中的异常按引擎约定记 Error 日志后继续。两条路径语义不同是有意的。</para>
        /// </summary>
        /// <param name="onNext">值变化时的回调。</param>
        /// <returns>订阅句柄，可用于取消订阅。</returns>
        /// <exception cref="ArgumentNullException">onNext 为 null 时抛出。</exception>
        /// <exception cref="ObjectDisposedException">属性已释放时抛出。</exception>
        public IDisposable Subscribe(Action<T> onNext)
        {
            if (onNext == null) throw new ArgumentNullException(nameof(onNext));
            ThrowIfDisposed();

            // 先注册再立即回调:确保回调中的订阅操作不会丢失后续消息
            var handle = _stream.Subscribe(onNext);
            try
            {
                onNext(_value);
            }
            catch
            {
                // 立即回调是注册期同步执行的代码,异常原样上抛(投递路径的异常隔离不适用于注册期:
                // 绑定初始化失败不该被静默)。但此刻句柄永远不会交给调用方——不在这里退订即永久泄漏:
                // 节点只经退订或本属性 Dispose 回收,属性活着期间该订阅既无法退订也不会被回收,
                // 回调与其闭包/目标对象被链表一直钉住。
                handle.Dispose();
                throw;
            }
            return handle;
        }

        /// <summary>
        /// 尝试写入值；已释放时返回 false 而不抛异常。
        /// <para>供双向绑定使用：绑定层不该因为目标失效而把异常抛进 UI 事件回调。</para>
        /// </summary>
        /// <param name="value">要写入的值。</param>
        /// <param name="actual">写入后本属性持有的值；返回 false 时为 default。</param>
        /// <returns>确实写入了返回 true。</returns>
        public bool TryWriteValue(T value, out T actual)
        {
            if (_disposed)
            {
                actual = default;
                return false;
            }

            Value = value;
            // 回传字段而非走 getter:这次赋值已经发生,字段必然是最新值。不读 getter 是为了
            // 不依赖它对「已释放」采取何种策略——写入会同步派发,订阅者可能在派发中释放本
            // 属性,而本次写入确实已经完成、值也已经赋好,故仍然返回 true。
            actual = _value;
            return true;
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// 释放内部事件流，取消所有订阅。
        /// <para>此后<b>写入</b> <see cref="Value"/> 或再次 <see cref="Subscribe"/> 会抛出
        /// <see cref="ObjectDisposedException"/>；<b>读取</b> <see cref="Value"/> 仍返回最后持有的值(宽容读取)。</para>
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _stream.Dispose();
        }

        #endregion

        #region Private

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name,
                    $"[Reactive] ReactiveProperty<{typeof(T).Name}> 已释放,请勿再写 Value 或订阅。");
        }

        #endregion
    }
}
