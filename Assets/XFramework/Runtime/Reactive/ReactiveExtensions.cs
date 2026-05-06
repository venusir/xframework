using System;

namespace XFramework
{
    /// <summary>
    /// 响应式扩展方法。
    /// </summary>
    public static class ReactiveExtensions
    {
        /// <summary>
        /// 将订阅绑定到节点的生命周期，节点销毁时自动取消订阅。
        /// <para>这是防止响应式编程中内存泄漏的关键方法。</para>
        /// </summary>
        /// <param name="disposable">要绑定的订阅。</param>
        /// <param name="node">绑定到的节点。</param>
        /// <returns>传入的订阅，支持链式调用。</returns>
        public static IDisposable AddTo(this IDisposable disposable, BaseNode node)
        {
            if (disposable == null)
                throw new ArgumentNullException(nameof(disposable));
            if (node == null)
                throw new ArgumentNullException(nameof(node));

            node.OnNodeDestroy += _ => disposable.Dispose();
            return disposable;
        }
    }
}
