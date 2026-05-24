namespace XFramework.XPool
{
    /// <summary>
    /// 池配置。值类型，避免装箱。
    /// </summary>
    public struct PoolConfig
    {
        /// <summary>
        /// 预热数量。初始化时预先创建的实例数。默认 0。
        /// </summary>
        public int PrewarmSize;

        /// <summary>
        /// 池内最大闲置实例数。超出时归还的对象被丢弃，由 GC 回收。默认不限制（int.MaxValue）。
        /// </summary>
        public int MaxSize;

        /// <summary>
        /// 调试模式：Editor 下检测重复归还（同一实例被 Return 多次）。
        /// <para>Release 构建下自动关闭，零开销。</para>
        /// </summary>
        public bool CollectionCheck;

        /// <summary>
        /// 默认配置：无预热，不限制容量，启用错误检测。
        /// </summary>
        public static PoolConfig Default => new()
        {
            PrewarmSize = 0,
            MaxSize = int.MaxValue,
            CollectionCheck = true
        };
    }
}