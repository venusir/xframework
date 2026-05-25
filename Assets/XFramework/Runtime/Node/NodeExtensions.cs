using System;
using System.Threading;
using XFramework.XReactive;

namespace XFramework.XNode
{

    /// <summary>
    /// 所有关于节点的扩展方法统一入口。
    /// <para>包括生命周期绑定（AddTo）和消息订阅自动绑定（Subscribe）等。</para>
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

        #region Subscribe (auto-bind to node lifecycle)

        /// <summary>
        /// 订阅指定类型的消息，订阅自动绑定到节点销毁时取消。
        /// <para>要求调用者同时实现 <see cref="IMessageSubscriber"/> 和 <see cref="IDestroyCancellationToken"/>，
        /// 订阅将在节点销毁时自动取消。</para>
        /// </summary>
        public static IDisposable Subscribe<TMessage>(this IMessageSubscriber subscriber, Action<TMessage> handler)
            where TMessage : class
        {
            var disposable = MessageManager.Subscribe<TMessage>(handler);
            if (subscriber is IDestroyCancellationToken dt)
                disposable.AddTo(dt.DestroyCancellationToken);
            return disposable;
        }

        #endregion
    }
}
