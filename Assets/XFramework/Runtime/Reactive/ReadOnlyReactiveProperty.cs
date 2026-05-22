using System;
using R3;

namespace XFramework.XReactive
{
    /// <summary>
    /// 只读响应式属性。由 <see cref="ReactiveProperty{T}"/> 通过 <see cref="ReactivePropertyExtensions.Select{TSource, TResult}"/> 派生。
    /// <para>仅暴露 <see cref="Value"/>（只读）和 <see cref="Subscribe"/>，不可赋值。</para>
    /// <para>使用完毕后需调用 <see cref="Dispose"/> 释放内部订阅。</para>
    /// </summary>
    /// <typeparam name="T">值的类型。</typeparam>
    public class ReadOnlyReactiveProperty<T> : IDisposable
    {
        #region Private Fields

        private readonly R3.ReadOnlyReactiveProperty<T> _value;

        #endregion

        #region Constructor (internal — only created via ReactivePropertyExtensions.Select)

        internal ReadOnlyReactiveProperty(R3.ReadOnlyReactiveProperty<T> r3Property)
        {
            _value = r3Property ?? throw new ArgumentNullException(nameof(r3Property));
        }

        #endregion

        #region Public Properties

        /// <summary>获取当前值。</summary>
        public T Value => _value.CurrentValue;

        #endregion

        #region Subscribe

        /// <summary>订阅值变化。</summary>
        public IDisposable Subscribe(Action<T> onNext)
        {
            return _value.Subscribe(onNext);
        }

        #endregion

        #region IDisposable

        /// <summary>释放内部属性，取消所有订阅。</summary>
        public void Dispose()
        {
            _value?.Dispose();
        }

        #endregion
    }

    /// <summary>
    /// 为 <see cref="ReactiveProperty{T}"/> 提供 LINQ 风格的转换扩展。
    /// <para>所有其他模块应通过此类的方法进行链式操作，避免直接依赖 R3 的 Observable 操作符。</para>
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
        public static ReadOnlyReactiveProperty<TResult> Select<TSource, TResult>(
            this ReactiveProperty<TSource> source,
            Func<TSource, TResult> selector)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (selector == null)
                throw new ArgumentNullException(nameof(selector));

            var r3Prop = source.GetR3Value();
            var r3ReadOnly = r3Prop.Select(selector).ToReadOnlyReactiveProperty();
            return new ReadOnlyReactiveProperty<TResult>(r3ReadOnly);
        }
    }
}
