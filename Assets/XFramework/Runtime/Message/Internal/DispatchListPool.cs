using System.Collections.Generic;

namespace XFramework.XMessage.Internal
{
    /// <summary>派发快照与淘汰遍历用的 List 静态对象池(避免每轮派发分配)。</summary>
    /// <remarks>
    /// <para><b>与 <c>XPool.ListPool&lt;T&gt;</c> 刻意并存,不要合并:</b>那个池明文「线程不安全,应在主线程使用」,
    /// 本池两侧加锁;且本池服务的是每帧每订阅的派发路径,不能走 PoolManager 的字典查找。</para>
    /// <para><b>与 <c>XFramework.XEvent.Internal.DispatchListPool&lt;T&gt;</c> 是两份同名实现,也不要去合并:</b>
    /// 事件引擎那侧服务它自己的派发快照,而让 Message 取用 Event 的 internal(或把池提升为 Event 的公开面再取用)
    /// 都会重新造出跨模块的实现细节依赖——那正是引擎拆出去要消掉的东西。两份池各自 internal、各自服务本模块,
    /// 任一侧改动都不影响另一侧。</para>
    /// <para>名字必须区分开:<c>using XFramework.XPool;</c> 与本命名空间一旦同时可见,同名的 <c>ListPool&lt;T&gt;</c>
    /// 会让引用变成 CS0104 二义——判的是<b>类型名</b>,与成员名无关,所以方法名不同(<c>Rent</c> vs <c>Get</c>)防不住。</para>
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
