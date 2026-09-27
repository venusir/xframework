using System.Collections.Generic;

namespace XFramework.XEvent.Internal
{
    /// <summary>派发快照缓冲的 List 静态对象池(避免每轮 OnNext 分配)。</summary>
    /// <remarks>
    /// <para><b>与 <c>XPool.ListPool&lt;T&gt;</c> 刻意并存,不要合并:</b>那个池明文「线程不安全,
    /// 应在主线程使用」,本池两侧加锁——但<b>锁不是跨线程许可</b>:锁与快照只为让引擎的订阅链表
    /// 在并发退订下不被写坏,从未给出可用的跨线程发布/订阅路径(线程契约见模块 README「线程」节)。
    /// 本池存在的真正理由是它服务每帧每订阅的派发路径,不能走 PoolManager 的字典查找。</para>
    /// <para><b>与 <c>XFramework.XMessage.Internal.DispatchListPool&lt;T&gt;</c> 是两份同名实现,
    /// 也不要去合并:</b>总线模块的派发路径同样需要自己的快照池,而跨模块取用本池就等于让
    /// Message 依赖 Event 的 internal——那正是本模块从 Message 拆出来要消掉的东西。</para>
    /// <para>名字必须区分开:<c>using XFramework.XPool;</c> 与本命名空间一旦同时可见,同名的
    /// <c>ListPool&lt;T&gt;</c> 会让引用变成 CS0104 二义——判的是<b>类型名</b>,与成员名无关,
    /// 所以方法名不同(<c>Rent</c> vs <c>Get</c>)防不住。</para>
    /// </remarks>
    internal static class DispatchListPool<T>
    {
        private static readonly Stack<List<T>> _pool = new();

        public static List<T> Rent()
        {
            lock (_pool)
            {
                return _pool.Count > 0 ? _pool.Pop() : new List<T>();
            }
        }

        public static void Return(List<T> list)
        {
            list.Clear();
            lock (_pool)
            {
                _pool.Push(list);
            }
        }
    }
}
