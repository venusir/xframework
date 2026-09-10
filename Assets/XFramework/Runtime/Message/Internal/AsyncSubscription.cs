using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XMessage.Internal
{
    /// <summary>
    /// 一条异步订阅登记项,同时作为返回给调用方的退订句柄。
    /// <para>
    /// 异步处理器不落在 EventStream 的订阅链表中,而是单独登记在通道的异步列表里——
    /// 这样同步派发与「等待全部异步处理器完成」才能各走各的路径。
    /// </para>
    /// </summary>
    /// <remarks>
    /// 生命周期:
    /// - 订阅自身持有一个 CTS,退订(<see cref="Dispose"/>)时取消它,使在途 await 提前结束
    /// - 注册时传入的调用方令牌与订阅生命周期绑定:令牌取消即自动退订,
    ///   与框架既有的 AddTo 约定一致(否则会出现「令牌已取消却仍在收消息」)
    /// 线程模型:登记表的增删由持有者保证单线程(与本引擎主线程模型一致)。
    /// <see cref="Token"/> 在构造时缓存,因为 <see cref="Dispose"/> 会释放 CTS,
    /// 此后访问 <c>Cts.Token</c> 会抛 ObjectDisposedException,而在途派发仍会读取本字段
    /// (CancellationToken 的 <c>IsCancellationRequested</c> 在源释放后读取是安全的)。
    /// </remarks>
    internal sealed class AsyncSubscription<TMessage> : IDisposable
    {
        #region Subscription Data

        /// <summary>订阅级过滤条件;为 <c>null</c> 表示不过滤。</summary>
        internal readonly Predicate<TMessage> Filter;

        /// <summary>异步处理器。</summary>
        internal readonly Func<TMessage, CancellationToken, UniTask> Handler;

        /// <summary>处理器收到的取消令牌:订阅自身的令牌,退订即取消。</summary>
        internal readonly CancellationToken Token;

        #endregion

        #region Private Fields

        private List<AsyncSubscription<TMessage>> _owner;
        private CancellationTokenSource _cts;
        private readonly Action _onOwnerEmpty;

        private CancellationTokenRegistration _externalRegistration;
        private bool _hasExternalRegistration;

        /// <summary>是否正处于外部令牌的取消回调中(此时该登记已执行完,不得再 Dispose 它)。</summary>
        private bool _inExternalCallback;

        #endregion

        #region Lifecycle

        /// <summary>创建异步订阅登记项。</summary>
        /// <param name="owner">持有本项的登记表。</param>
        /// <param name="filter">订阅级过滤条件,可为 <c>null</c>。</param>
        /// <param name="handler">异步处理器。</param>
        /// <param name="cancellationToken">调用方令牌;可取消时与其绑定,令牌取消即自动退订。</param>
        /// <param name="onOwnerEmpty">登记表被本项清空时的回调(持有者据此回收空通道),可为 <c>null</c>。</param>
        internal AsyncSubscription(
            List<AsyncSubscription<TMessage>> owner,
            Predicate<TMessage> filter,
            Func<TMessage, CancellationToken, UniTask> handler,
            CancellationToken cancellationToken,
            Action onOwnerEmpty)
        {
            _owner = owner;
            _onOwnerEmpty = onOwnerEmpty;
            Filter = filter;
            Handler = handler;

            _cts = new CancellationTokenSource();
            Token = _cts.Token;

            if (cancellationToken.CanBeCanceled)
            {
                // 静态 lambda 无捕获,由编译器缓存委托实例;仅 CancellationTokenRegistration 本身分配一次
                _externalRegistration = cancellationToken.Register(
                    static state => ((AsyncSubscription<TMessage>)state).OnExternalCancelled(), this);
                _hasExternalRegistration = true;
            }
        }

        /// <summary>是否已退订(或已随通道清理而失效)。</summary>
        internal bool IsDisposed => _owner == null;

        /// <summary>
        /// 退订:从登记表移除、取消在途处理器并释放 CTS。幂等。
        /// <para>仅在确实移除且登记表随之清空时回调持有者,用于回收只剩异步订阅的通道。</para>
        /// </summary>
        public void Dispose()
        {
            var owner = _owner;
            if (owner == null)
                return;

            _owner = null;
            var removed = owner.Remove(this);
            var ownerBecameEmpty = removed && owner.Count == 0;

            ReleaseExternalRegistration();
            ReleaseCts();

            if (ownerBecameEmpty)
                _onOwnerEmpty?.Invoke();
        }

        /// <summary>
        /// 清理路径专用:断开与登记表的联系后再释放,不再回调持有者。
        /// <para>持有者正在清表,此时回调会在其遍历中改表。</para>
        /// </summary>
        internal void DisposeDetached()
        {
            _owner = null;
            ReleaseExternalRegistration();
            ReleaseCts();
        }

        #endregion

        #region Private

        /// <summary>外部令牌取消入口:自动退订。</summary>
        private void OnExternalCancelled()
        {
            // 回调即该登记的末次执行,标记后不再去 Dispose 已执行完的登记
            _inExternalCallback = true;
            try
            {
                Dispose();
            }
            finally
            {
                _inExternalCallback = false;
            }
        }

        /// <summary>解除外部令牌上的登记;正在其回调中时跳过(该登记已执行完,无需也无法再释放)。</summary>
        private void ReleaseExternalRegistration()
        {
            if (!_hasExternalRegistration)
                return;

            _hasExternalRegistration = false;

            if (_inExternalCallback)
                return;

            _externalRegistration.Dispose();
            _externalRegistration = default;
        }

        /// <summary>取消并释放订阅自身的 CTS,使在途处理器提前结束。</summary>
        private void ReleaseCts()
        {
            if (_cts == null)
                return;

            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        #endregion
    }
}
