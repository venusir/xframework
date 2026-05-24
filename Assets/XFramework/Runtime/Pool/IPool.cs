namespace XFramework.XPool
{
    /// <summary>
    /// 池操作接口。用于依赖反转和单元测试。
    /// <para>所有泛型池实现均实现此接口，可通过 <see cref="PoolManager.GetPool{T}"/> 获取。</para>
    /// </summary>
    /// <typeparam name="T">池中存储的对象类型</typeparam>
    public interface IPool<T>
    {
        /// <summary>
        /// 获取一个实例。池空时自动调用生成器新建。
        /// </summary>
        T Get();

        /// <summary>
        /// 归还实例。池满时（超出 <see cref="PoolConfig.MaxSize"/>）丢弃。
        /// </summary>
        void Return(T item);

        /// <summary>
        /// 池内当前闲置实例数。
        /// </summary>
        int CountInactive { get; }

        /// <summary>
        /// 池自创建以来生成过的总实例数（含活跃和闲置）。
        /// </summary>
        int CountAll { get; }

        /// <summary>
        /// 清空池内所有闲置实例。
        /// <para>已取出的活跃实例不受影响，但归还时会重新入池。</para>
        /// </summary>
        void Clear();
    }
}