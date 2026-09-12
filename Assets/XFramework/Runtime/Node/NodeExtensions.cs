using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XMessage;

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

        /// <summary>
        /// 将 <paramref name="disposable"/> 绑定到目标 <see cref="BaseNode"/> 的自动清理列表。
        /// <para>节点销毁时统一 Dispose，相比 <see cref="AddTo{T}(T, CancellationToken)"/> 
        /// 减少了 <see cref="CancellationToken.Register"/> 的逐个分配开销。</para>
        /// <para>推荐在 <see cref="BaseNode"/> 派生类内部使用此方法绑定 disposable。</para>
        /// <para>返回 <paramref name="disposable"/> 自身，支持链式调用。</para>
        /// </summary>
        /// <typeparam name="T"><see cref="IDisposable"/> 或其实现类型。</typeparam>
        /// <param name="disposable">要绑定的 disposable。</param>
        /// <param name="node">目标节点。</param>
        /// <returns><paramref name="disposable"/> 自身。</returns>
        public static T AddToNode<T>(this T disposable, BaseNode node)
            where T : IDisposable
        {
            if (node == null)
            {
                disposable.Dispose();
                return disposable;
            }

            node.RegisterAutoDispose(disposable);
            return disposable;
        }

        #region Subscribe (auto-bind to node lifecycle)

        /// <summary>
        /// 订阅指定类型的消息，订阅自动绑定到节点生命周期，节点销毁时统一释放。
        /// <para>
        /// 重载决议说明：本方法与 <c>MessageManager.Subscribe&lt;TMessage&gt;(this IMessageSubscriber, Action&lt;TMessage&gt;)</c>
        /// 同形，同时引入两个命名空间时<b>不会</b>二义——接收者同时可转换为 <see cref="BaseNode"/> 与
        /// <see cref="IMessageSubscriber"/>，而存在 <c>BaseNode → IMessageSubscriber</c> 的隐式转换、反之不存在，
        /// 故 <see cref="BaseNode"/> 是「更好的转换目标」，本方法确定胜出。
        /// </para>
        /// <para>
        /// 该保证的前提是 <see cref="BaseNode"/> 实现了 <see cref="IMessageSubscriber"/>——
        /// 一旦移除，两个方向都不存在转换，重载决议会退化为编译错误 CS0121。
        /// </para>
        /// </summary>
        /// <typeparam name="TMessage">消息类型，struct 与 class 均支持。</typeparam>
        /// <param name="node">目标节点，不可为 <c>null</c>。</param>
        /// <param name="handler">消息回调，不可为 <c>null</c>。</param>
        /// <returns>退订句柄；节点已销毁时返回已释放的空句柄。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> 或 <paramref name="handler"/> 为 <c>null</c> 时抛出。</exception>
        public static IDisposable Subscribe<TMessage>(this BaseNode node, Action<TMessage> handler)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            return MessageManager.Subscribe<TMessage>(handler).AddToNode(node);
        }

        /// <summary>
        /// 订阅指定类型的消息，并附加过滤条件。订阅自动绑定到节点生命周期。
        /// </summary>
        /// <typeparam name="TMessage">消息类型，struct 与 class 均支持。</typeparam>
        /// <param name="node">目标节点，不可为 <c>null</c>。</param>
        /// <param name="filter">订阅级过滤条件，不可为 <c>null</c>。</param>
        /// <param name="handler">消息回调，不可为 <c>null</c>。</param>
        /// <returns>退订句柄；节点已销毁时返回已释放的空句柄。</returns>
        /// <exception cref="ArgumentNullException">任一参数为 <c>null</c> 时抛出。</exception>
        public static IDisposable Subscribe<TMessage>(
            this BaseNode node, Predicate<TMessage> filter, Action<TMessage> handler)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            return MessageManager.Subscribe<TMessage>(filter, handler).AddToNode(node);
        }

        /// <summary>
        /// 异步订阅指定类型的消息。订阅自动绑定到节点生命周期，节点销毁时统一释放并取消在途处理器。
        /// </summary>
        /// <typeparam name="TMessage">消息类型，struct 与 class 均支持。</typeparam>
        /// <param name="node">目标节点，不可为 <c>null</c>。</param>
        /// <param name="asyncHandler">异步处理器，不可为 <c>null</c>。</param>
        /// <param name="cancellationToken">绑定订阅生命周期的令牌，取消即自动退订。</param>
        /// <returns>退订句柄；节点已销毁时返回已释放的空句柄。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> 或 <paramref name="asyncHandler"/> 为 <c>null</c> 时抛出。</exception>
        public static IDisposable SubscribeAsync<TMessage>(
            this BaseNode node,
            Func<TMessage, CancellationToken, UniTask> asyncHandler,
            CancellationToken cancellationToken = default)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (asyncHandler == null) throw new ArgumentNullException(nameof(asyncHandler));

            return MessageManager.SubscribeAsync(asyncHandler, cancellationToken).AddToNode(node);
        }

        /// <summary>
        /// 订阅带缓冲的消息，新订阅者会立即收到最近一次发布的消息；订阅自动绑定到节点生命周期。
        /// <para>
        /// <b>刻意只提供这一个缓冲重载：</b>带 Key 与带过滤条件的变体都<b>不加</b>——一旦
        /// <c>(TKey, Action&lt;TMessage&gt;)</c> 与 <c>(Predicate&lt;TMessage&gt;, Action&lt;TMessage&gt;)</c>
        /// 同时存在，<c>node.SubscribeBuffered(predicate, handler)</c> 这类调用就会退化为 CS0121，
        /// 或被静默解析为「把谓词当 Key」。这两类订阅请改用
        /// <c>MessageManager.SubscribeBuffered(...).AddToNode(this)</c>。
        /// </para>
        /// </summary>
        /// <typeparam name="TMessage">消息类型，struct 与 class 均支持。</typeparam>
        /// <param name="node">目标节点，不可为 <c>null</c>。</param>
        /// <param name="handler">消息回调，不可为 <c>null</c>。</param>
        /// <returns>退订句柄；节点已销毁时返回已释放的空句柄。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> 或 <paramref name="handler"/> 为 <c>null</c> 时抛出。</exception>
        public static IDisposable SubscribeBuffered<TMessage>(this BaseNode node, Action<TMessage> handler)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            return MessageManager.SubscribeBuffered<TMessage>(handler).AddToNode(node);
        }

        #endregion
    }
}
