using System.Text;

namespace XFramework.XPool
{
    /// <summary>
    /// <see cref="StringBuilder"/> 对象池。
    /// <para>专为高频字符串拼接场景设计（如每帧日志、UI 动态文本）。</para>
    /// <para><c>Return(sb)</c> 时自动调用 <c>sb.Clear()</c>，无需手动清空。</para>
    /// <para>内部复用 <see cref="Pool{T}"/>，享有一致的容量管理和 Editor 重复归还检测。</para>
    /// <para>注意：返回前请取走需要的数据（如 <c>sb.ToString()</c>），归还后内容将被清空。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// var sb = StringBuilderPool.Get();
    /// sb.Append("HP: ").Append(currentHp).Append("/").Append(maxHp);
    /// string text = sb.ToString();
    /// StringBuilderPool.Return(sb);
    /// // text 变量持有结果字符串，不受 Return 影响
    /// </code>
    /// </example>
    public static class StringBuilderPool
    {
        private static Pool<StringBuilder> _pool = new(
            () => new StringBuilder(),
            PoolConfig.Default,
            onReturn: sb => sb.Clear());

        static StringBuilderPool()
        {
            CollectionPoolManager.Register(_pool.Clear);
        }

        /// <summary>
        /// 从池中获取一个 <see cref="StringBuilder"/>。池空时自动创建。
        /// </summary>
        public static StringBuilder Get()
        {
            return _pool.Get();
        }

        /// <summary>
        /// 归还 <see cref="StringBuilder"/> 到池，归还前自动 <c>Clear()</c>。
        /// </summary>
        /// <param name="sb">要归还的 StringBuilder 实例</param>
        public static void Return(StringBuilder sb)
        {
            _pool.Return(sb);
        }

        /// <summary>
        /// 池中当前闲置的 <see cref="StringBuilder"/> 数量。
        /// </summary>
        public static int CountInactive => _pool.CountInactive;

        /// <summary>
        /// 池历史创建的 <see cref="StringBuilder"/> 总数。
        /// </summary>
        public static int CountAll => _pool.CountAll;

        /// <summary>
        /// 预配置池参数。仅在一次都未 <c>Get()</c> 时有效。
        /// </summary>
        /// <param name="config">池配置</param>
        public static void Configure(PoolConfig config)
        {
            var oldCount = _pool.CountAll - _pool.CountInactive;
            if (oldCount > 0)
            {
                UnityEngine.Debug.LogWarning(
                    "[StringBuilderPool] 已有活跃实例，Configure 已忽略。请在首次 Get 前调用 Configure。");
                return;
            }

            _pool = new Pool<StringBuilder>(() => new StringBuilder(), config, onReturn: sb => sb.Clear());
            CollectionPoolManager.Register(_pool.Clear);
        }

        /// <summary>
        /// 清空池内所有闲置实例。
        /// </summary>
        public static void Clear()
        {
            _pool.Clear();
        }

        /// <summary>
        /// 获取内部 <see cref="IPool{T}"/> 接口，用于依赖反转。
        /// </summary>
        public static IPool<StringBuilder> GetPool()
        {
            return _pool;
        }
    }
}