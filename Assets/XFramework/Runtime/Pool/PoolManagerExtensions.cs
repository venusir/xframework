using System;

namespace XFramework.XPool
{
    /// <summary>
    /// <see cref="PoolManager"/> 的便捷扩展方法。
    /// <para>在任意 object 上调用 <c>this.GetFromPool<T>()</c> 和 <c>item.ReturnToPool()</c>，
    /// 减少对 <c>PoolManager</c> 命名空间的显式引用。</para>
    /// </summary>
    public static class PoolManagerExtensions
    {
        /// <summary>
        /// 从池获取实例。池空时自动 <c>new T()</c>。
        /// </summary>
        /// <typeparam name="T">对象类型，需为引用类型且具有无参构造函数</typeparam>
        /// <param name="_">调用方 object 自身（任意 MonoBehavior / 普通对象），仅用于语法糖</param>
        /// <returns>池中的实例</returns>
        public static T GetFromPool<T>(this object _) where T : class, new()
            => PoolManager.Get<T>();

        /// <summary>
        /// 从池获取实例，使用自定义生成器（池空时调用）。
        /// </summary>
        /// <typeparam name="T">对象类型，需为引用类型</typeparam>
        /// <param name="_">调用方 object 自身</param>
        /// <param name="generator">池空时的实例生成器</param>
        /// <returns>池中的实例</returns>
        public static T GetFromPool<T>(this object _, Func<T> generator) where T : class
            => PoolManager.Get(generator);

        /// <summary>
        /// 归还实例到池。
        /// <para>若池不存在或已销毁，静默忽略。</para>
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="item">要归还的实例</param>
        public static void ReturnToPool<T>(this T item) where T : class
            => PoolManager.Return(item);
    }
}