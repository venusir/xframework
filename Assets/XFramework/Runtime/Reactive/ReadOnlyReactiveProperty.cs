using System;
using System.Collections.Generic;
using XFramework.XMessage.Internal;

namespace XFramework.XReactive
{
    /// <summary>
    /// 只读响应式属性。由 <see cref="ReactiveProperty{T}"/> 通过 <see cref="ReactivePropertyExtensions.Select{TSource, TResult}"/> 派生。
    /// <para>仅暴露 <see cref="Value"/>（只读）和 <see cref="Subscribe"/>，不可赋值。</para>
    /// <para>基于 XMessage 事件流引擎实现,内部订阅源属性做值映射。</para>
    /// <para>使用完毕后需调用 <see cref="Dispose"/> 释放内部订阅。</para>
    /// </summary>
    /// <typeparam name="T">值的类型。</typeparam>
    /// <remarks>
    /// 行为契约:
    /// - <see cref="Subscribe"/> 订阅时立即同步回调当前映射值(UI 初始绑定依赖)
    /// - 源值变化沿映射链传播,映射结果与当前值相同不通知(去重语义)
    /// - <see cref="Value"/> getter 不做 disposed 检查(宽容读取);
    ///   <see cref="Subscribe"/> 在已释放时抛 <see cref="ObjectDisposedException"/>
    /// <para>
    /// 实现 <see cref="IReactiveProperty{T}"/>:既可直接交给收该接口的绑定 API,也可作为
    /// <see cref="ReactivePropertyExtensions.Select{TSource, TResult}"/> 的源继续映射
    /// (派生值仍是可被订阅的只读值,链式成立)。这正是它「只读视图」定位的体现——
    /// 接口本身无 setter,不会因此获得写入能力。
    /// </para>
    /// </remarks>
    public class ReadOnlyReactiveProperty<T> : IReactiveProperty<T>, IDisposable
    {
        #region Private Fields

        private readonly EventStream<T> _stream = new();
        private IDisposable _sourceSub;
        private T _value;
        private bool _disposed;

        #endregion

        #region Constructor (internal — only created via ReactivePropertyExtensions.Select)

        private ReadOnlyReactiveProperty()
        {
        }

        /// <summary>
        /// 从源属性派生只读属性(静态泛型工厂:TSource 无法在类级泛型表达,故用方法级泛型)。
        /// <para>初始化时取源当前值作为初始值(不通知),之后订阅源做映射推送。</para>
        /// </summary>
        internal static ReadOnlyReactiveProperty<TResult> Create<TSource, TResult>(
            IReactiveProperty<TSource> source, Func<TSource, TResult> selector)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (selector == null) throw new ArgumentNullException(nameof(selector));

            var result = new ReadOnlyReactiveProperty<TResult>
            {
                _value = selector(source.Value)
            };
            // 源订阅:值变化时映射并推送(源自身已去重,这里对映射结果再去重一次)
            result._sourceSub = source.Subscribe(srcValue => result.Set(selector(srcValue)));
            return result;
        }

        #endregion

        #region Public Properties

        /// <summary>获取当前值。</summary>
        public T Value => _value;

        #endregion

        #region Subscribe

        /// <summary>订阅值变化。订阅时立即回调当前值。</summary>
        /// <param name="onNext">值变化时的回调。</param>
        /// <returns>订阅句柄，可用于取消订阅。</returns>
        /// <exception cref="ArgumentNullException">onNext 为 null 时抛出。</exception>
        /// <exception cref="ObjectDisposedException">属性已释放时抛出。</exception>
        /// <remarks>立即回调抛出的异常原样上抛(订阅已自动清理,不会泄漏)；之后投递中的异常记 Error 日志后继续。</remarks>
        public IDisposable Subscribe(Action<T> onNext)
        {
            if (onNext == null) throw new ArgumentNullException(nameof(onNext));
            ThrowIfDisposed();

            var handle = _stream.Subscribe(onNext);
            try
            {
                onNext(_value);
            }
            catch
            {
                // 同 ReactiveProperty.Subscribe:注册期异常原样上抛,但必须先退订,
                // 否则句柄交不出去、该订阅永久泄漏。
                handle.Dispose();
                throw;
            }
            return handle;
        }

        #endregion

        #region IDisposable

        /// <summary>释放内部订阅并取消源订阅。</summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _sourceSub?.Dispose();
            _stream.Dispose();
        }

        #endregion

        #region Private

        private void Set(T value)
        {
            if (_disposed)
                return;
            // 映射结果去重:与当前值相同不通知
            if (EqualityComparer<T>.Default.Equals(_value, value))
                return;
            _value = value;
            _stream.OnNext(value);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name,
                    $"[Reactive] ReadOnlyReactiveProperty<{typeof(T).Name}> 已释放,请勿再订阅。");
        }

        #endregion
    }

    /// <summary>
    /// 为 <see cref="ReactiveProperty{T}"/> 提供 LINQ 风格的转换扩展。
    /// <para>响应式属性的链式操作（映射派生）统一经此类扩展方法进行。</para>
    /// </summary>
    public static class ReactivePropertyExtensions
    {
        /// <summary>
        /// 将响应式属性映射为只读派生属性，值随源自动变化。
        /// <para>例: <c>level.Select(lv => $"Lv.{lv}")</c></para>
        /// <para>返回值是 <see cref="ReadOnlyReactiveProperty{T}"/>，只能 Subscribe 不能赋值，符合派生值的语义。</para>
        /// </summary>
        /// <typeparam name="TSource">源值类型。</typeparam>
        /// <typeparam name="TResult">结果值类型。</typeparam>
        /// <param name="source">源响应式属性。</param>
        /// <param name="selector">值映射函数。</param>
        /// <returns>新的只读响应式属性。</returns>
        /// <exception cref="ArgumentNullException">source 或 selector 为 null 时抛出。</exception>
        public static ReadOnlyReactiveProperty<TResult> Select<TSource, TResult>(
            this IReactiveProperty<TSource> source,
            Func<TSource, TResult> selector)
        {
            return ReadOnlyReactiveProperty<TResult>.Create(source, selector);
        }
    }
}
