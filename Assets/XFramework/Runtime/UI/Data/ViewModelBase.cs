using System;
using System.Collections.Generic;
using XFramework.XReactive;

namespace XFramework.XUI.Data
{
    /// <summary>
    /// ViewModel 基类。管理所有 <see cref="ReactiveProperty{T}"/> 的生命周期。
    /// <para>使用方式：在派生类的构造函数中通过 <see cref="CreateProperty{T}"/> 创建属性。</para>
    /// <para>面板关闭时统一调用 <see cref="Dispose"/> 释放所有 ReactiveProperty 和订阅。</para>
    /// <para>预分配容量列表，避免扩容产生 GC。</para>
    /// </summary>
    public abstract class ViewModelBase : IViewModel
    {
        #region Fields

        /// <summary>
        /// 所有通过 <see cref="CreateProperty{T}"/> 创建的 ReactiveProperty。
        /// <para>预分配容量 8，避免扩容 GC。</para>
        /// </summary>
        private readonly List<IDisposable> _properties = new List<IDisposable>(8);

        /// <summary>
        /// 额外的订阅（如 <see cref="ReactivePropertyExtensions.Select{TSource, TResult}"/> 生成的 disposable）。
        /// </summary>
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>(4);

        #endregion

        #region Lifecycle

        /// <inheritdoc />
        public virtual void OnBound() { }

        /// <inheritdoc />
        public virtual void OnUnbound() { }

        /// <summary>
        /// 释放所有 ReactiveProperty 和订阅。
        /// <para>由面板在关闭时调用。</para>
        /// </summary>
        public virtual void Dispose()
        {
            foreach (var prop in _properties)
                prop?.Dispose();
            _properties.Clear();

            foreach (var sub in _subscriptions)
                sub?.Dispose();
            _subscriptions.Clear();
        }

        #endregion

        #region Protected — Property Creation

        /// <summary>
        /// 创建可观察属性并自动加入生命周期管理。
        /// <para>面板关闭时 Dispose 会自动释放此属性。</para>
        /// </summary>
        /// <typeparam name="T">值的类型。</typeparam>
        /// <param name="initialValue">初始值。</param>
        /// <returns>一个新的 <see cref="ReactiveProperty{T}"/> 实例。</returns>
        protected ReactiveProperty<T> CreateProperty<T>(T initialValue = default)
        {
            var prop = new ReactiveProperty<T>(initialValue);
            _properties.Add(prop);
            return prop;
        }

        /// <summary>
        /// 创建只读可观察属性。通过 <see cref="ReactivePropertyExtensions.Select{TSource, TResult}"/> 从已有 ReactiveProperty 派生。
        /// <para>例如：PlayerLevel.Select(lv => $"Lv.{lv}") → ReadOnlyReactiveProperty。</para>
        /// </summary>
        /// <typeparam name="TSource">源值的类型。</typeparam>
        /// <typeparam name="TResult">结果值的类型。</typeparam>
        /// <param name="source">源响应式属性。</param>
        /// <param name="selector">值映射函数。</param>
        /// <returns>一个 <see cref="ReadOnlyReactiveProperty{TResult}"/>，自动跟随源变化。</returns>
        protected ReadOnlyReactiveProperty<TResult> CreateReadOnlyProperty<TSource, TResult>(
            ReactiveProperty<TSource> source,
            Func<TSource, TResult> selector)
        {
            var prop = source.Select(selector);
            _subscriptions.Add(prop);
            return prop;
        }

        /// <summary>
        /// 添加外部订阅到生命周期管理。
        /// <para>用于 Subscribe、Select 等生成的 disposable。</para>
        /// </summary>
        /// <param name="disposable">要管理的订阅。</param>
        protected void AddSubscription(IDisposable disposable)
        {
            if (disposable != null)
                _subscriptions.Add(disposable);
        }

        #endregion
    }
}
