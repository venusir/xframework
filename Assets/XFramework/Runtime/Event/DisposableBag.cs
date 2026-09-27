using System;
using System.Collections.Generic;
using XFramework.XEvent.Internal;

namespace XFramework.XEvent
{
    /// <summary>
    /// 一组 <see cref="IDisposable"/> 的容器:把 N 个句柄的释放收成一次调用。
    /// <para><b>为什么需要它</b>:持有多个订阅句柄的类,若没有单一销毁点(要重置、要换目标、要部分重建),
    /// 目前只能自己维护一个 <c>List&lt;IDisposable&gt;</c> 并在每处记得遍历释放。<see cref="DisposableBagExtensions.AddTo{T}"/> 把它变成一句话。</para>
    /// <para><b>与 <see cref="XFramework.XMessage.IDestroyCancellationToken"/> 的分工</b>:那条路解决「订阅随对象销毁
    /// 自动释放」;本容器解决「**按需**批量释放,且容器自身可复用」——两者互补,不互斥。</para>
    /// </summary>
    /// <remarks>
    /// <para><b>三条语义(照抄自 Rx 系实现,刻意如此):</b></para>
    /// <list type="number">
    /// <item><description><see cref="Clear"/> 释放全部已登记句柄并清空容器,但**容器仍可继续用**——之后再
    /// <see cref="Add"/> 的句柄归下一次释放管。</description></item>
    /// <item><description><see cref="Dispose"/> 终结容器:释放全部句柄,且**之后 <see cref="Add"/> 进来的句柄会被立即释放**
    /// (不是静默丢弃)——这样「容器已终结」不会变成一处静默的资源泄漏。</description></item>
    /// <item><description><see cref="DisposableBagExtensions.AddTo{T}"/> 返回句柄本身,便于链式书写。</description></item>
    /// </list>
    /// <para><b>线程</b>:与事件流一致,按主线程使用设计(内部不加锁)。</para>
    /// <para><b>为什么在 Event 模块</b>:它聚合的主要是订阅句柄,而 Event 是框架最底层、被 Message / Reactive /
    /// Input / Settings 共用的那一层;为一个 ~60 行的容器新建模块不划算。</para>
    /// </remarks>
    public sealed class DisposableBag : IDisposable
    {
        #region Private Fields

        private List<IDisposable> _items = new();
        private bool _disposed;

        #endregion

        #region Public API

        /// <summary>当前已登记、尚未释放的句柄数(诊断用)。</summary>
        public int Count => _items.Count;

        /// <summary>
        /// 登记一个句柄。
        /// <para>容器已 <see cref="Dispose"/> 时,本句柄**立即被释放**(而不是登记进一个永远不会被释放的容器)。</para>
        /// </summary>
        /// <param name="disposable">要登记的句柄,不可为 <c>null</c>。</param>
        /// <exception cref="ArgumentNullException"><paramref name="disposable"/> 为 <c>null</c> 时抛出。</exception>
        public void Add(IDisposable disposable)
        {
            if (disposable == null) throw new ArgumentNullException(nameof(disposable));

            if (_disposed)
            {
                disposable.Dispose();
                return;
            }

            _items.Add(disposable);
        }

        /// <summary>
        /// 释放并清空当前全部句柄,容器保持可用。
        /// <para>重入安全:某个句柄的释放动作里再 <see cref="Add"/> 或再 <see cref="Clear"/> 都不会出错
        /// ——新项归新的一轮,既不会被本轮漏掉,也不会被丢弃。</para>
        /// </summary>
        public void Clear()
        {
            if (_items.Count == 0)
                return;

            // 换出列表再逐个释放:释放动作可能回调进来 Add / Clear,换列表让重入天然安全
            var pending = _items;
            _items = DispatchListPool<IDisposable>.Rent();
            try
            {
                for (int i = 0; i < pending.Count; i++)
                    pending[i].Dispose();
            }
            finally
            {
                DispatchListPool<IDisposable>.Return(pending);
            }
        }

        /// <summary>
        /// 释放全部句柄并终结容器:之后 <see cref="Add"/> 的句柄会被立即释放。幂等。
        /// </summary>
        public void Dispose()
        {
            _disposed = true;
            Clear();
        }

        #endregion
    }

    /// <summary>
    /// <see cref="DisposableBag"/> 的链式登记扩展。
    /// </summary>
    public static class DisposableBagExtensions
    {
        /// <summary>
        /// 把句柄登记进容器,并返回该句柄本身,便于链式书写:
        /// <c>var sub = stream.Subscribe(OnNext).AddTo(_bag);</c>
        /// </summary>
        /// <typeparam name="T">句柄的实际类型(返回它以便链式调用,不装箱)。</typeparam>
        /// <param name="disposable">要登记的句柄,不可为 <c>null</c>。</param>
        /// <param name="bag">目标容器,不可为 <c>null</c>。</param>
        /// <returns><paramref name="disposable"/> 本身。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="bag"/> 为 <c>null</c> 时抛出;
        /// <paramref name="disposable"/> 为 <c>null</c> 时同样抛出(由 <see cref="DisposableBag.Add"/> 传来)。</exception>
        public static T AddTo<T>(this T disposable, DisposableBag bag) where T : IDisposable
        {
            if (bag == null) throw new ArgumentNullException(nameof(bag));

            bag.Add(disposable);
            return disposable;
        }
    }
}
