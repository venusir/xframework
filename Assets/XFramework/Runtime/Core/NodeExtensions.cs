using System;
using System.Threading;

namespace XFramework.XCore
{

    /// <summary>
    /// <see cref="IDisposable"/> 的扩展方法，提供将订阅绑定到节点生命周期的便捷 API。
    /// <para>配合 <see cref="BaseNode.Dispose()"/> 使用，节点销毁时自动释放所有绑定的订阅。</para>
    /// </summary>
    public static class NodeExtensions
    {
        /// <summary>
        /// 将 <paramref name="disposable"/> 绑定到 <paramref name="token"/> 的 CancellationToken 上，
        /// 当 CancellationToken 被取消时自动释放 <paramref name="disposable"/>。
        /// <para>返回 <paramref name="disposable"/> 自身，支持链式调用。</para>
        /// </summary>
        /// <typeparam name="T"><see cref="IDisposable"/> 或其实现类型（包括 struct 如 <see cref="CancellationTokenRegistration"/>）。</typeparam>
        /// <param name="disposable">要绑定的 disposable。</param>
        /// <param name="token">节点销毁时的 CancellationToken。</param>
        /// <returns><paramref name="disposable"/> 自身。</returns>
        public static T AddTo<T>(this T disposable, CancellationToken token)
            where T : IDisposable
        {
            if (!token.CanBeCanceled || token.IsCancellationRequested)
            {
                disposable.Dispose();
            }
            else
            {
                token.Register(s => ((IDisposable)s).Dispose(), disposable);
            }
            return disposable;
        }

        /// <summary>
        /// 将 <paramref name="disposable"/> 绑定到 <paramref name="token"/> 的节点生命周期上，
        /// 节点销毁时自动释放 <paramref name="disposable"/>。
        /// <para>返回 <paramref name="disposable"/> 自身，支持链式调用。</para>
        /// </summary>
        /// <typeparam name="T"><see cref="IDisposable"/> 或其实现类型（包括 struct）。</typeparam>
        /// <param name="disposable">要绑定的 disposable。</param>
        /// <param name="token">实现了 <see cref="IDestroyCancellationToken"/> 的节点。</param>
        /// <returns><paramref name="disposable"/> 自身。</returns>
        public static T AddTo<T>(this T disposable, IDestroyCancellationToken token)
            where T : IDisposable
        {
            return AddTo(disposable, token.DestroyCancellationToken);
        }
    }
}