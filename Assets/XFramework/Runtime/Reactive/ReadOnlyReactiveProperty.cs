using System;
using System.Collections.Generic;
using XFramework.XReactive.Internal;

namespace XFramework.XReactive
{
    /// <summary>
    /// 只读响应式属性。由 <see cref="ReactiveProperty{T}"/> 通过 <see cref="ReactivePropertyExtensions.Select{TSource, TResult}"/> 派生。
    /// <para>仅暴露 <see cref="Value"/>（只读）和 <see cref="Subscribe"/>，不可赋值。</para>
    /// <para>基于自研 Subject 实现(移除 R3 依赖计划 Phase 3),内部订阅源属性做值映射。</para>
    /// <para>使用完毕后需调用 <see cref="Dispose"/> 释放内部订阅。</para>
    /// </summary>
    /// <typeparam name="T">值的类型。</typeparam>
    /// <remarks>
    /// 行为契约(实测 R3 后固化,与 R3.ReadOnlyReactiveProperty 一致):
    /// - <see cref="Subscribe"/> 订阅时立即同步回调当前映射值(UI 初始绑定依赖)
    /// - 源值变化沿映射链传播,映射结果与当前值相同不通知(去重语义)
    /// - <see cref="Value"/> getter 不做 disposed 检查(宽容读取,与 R3 CurrentValue 一致);
    ///   <see cref="Subscribe"/> 在已释放时抛 <see cref="ObjectDisposedException"/>
    /// </remarks>
    public class ReadOnlyReactiveProperty<T> : IDisposable
    {
        #region Private Fields

        private readonly Subject<T> _subject = new();
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
            ReactiveProperty<TSource> source, Func<TSource, TResult> selector)
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
        /// <exception cref="ArgumentNullException">onNext 为 null 时抛出。</exception>
        /// <exception cref="ObjectDisposedException">属性已释放时抛出。</exception>
        public IDisposable Subscribe(Action<T> onNext)
        {
            if (onNext == null) throw new ArgumentNullException(nameof(onNext));
            ThrowIfDisposed();

            var handle = _subject.Subscribe(onNext);
            onNext(_value);
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
            _subject.Dispose();
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
            _subject.OnNext(value);
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
    /// <para>所有其他模块应通过此类的方法进行链式操作,避免直接依赖 R3 的 Observable 操作符。</para>
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
            this ReactiveProperty<TSource> source,
            Func<TSource, TResult> selector)
        {
            return ReadOnlyReactiveProperty<TResult>.Create(source, selector);
        }
    }
}
