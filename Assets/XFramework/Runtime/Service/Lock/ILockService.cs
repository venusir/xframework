using System;

namespace XFramework
{
    /// <summary>
    /// 逻辑锁服务公共接口。与节点树无关，可供任何对象直接使用。
    /// <para>通过 <see cref="LockSystem"/> 静态入口获取全局实例，或自行 <c>new LockService()</c> 创建独立实例。</para>
    /// </summary>
    public interface ILockService : IDisposable
    {
        /// <summary>
        /// 请求一个锁，引用计数 +1。
        /// <para>返回 <see cref="LockHandle"/>，可通过 <c>using</c> 自动释放，也可手动调用 <see cref="Release"/>。</para>
        /// </summary>
        /// <param name="lockType">锁类型标识，由游戏工程自行定义。</param>
        /// <param name="owner">锁的持有者，用于调试和日志。</param>
        /// <returns>锁句柄，Dispose 时自动释放锁。</returns>
        LockHandle Acquire(int lockType, object owner);

        /// <summary>
        /// 释放一个锁，引用计数 -1。
        /// <para>与 <see cref="Acquire"/> 配对使用，适用于跨作用域的生命周期管理。</para>
        /// </summary>
        /// <param name="lockType">锁类型标识。</param>
        /// <param name="owner">锁的持有者，仅用于日志记录。</param>
        void Release(int lockType, object owner);

        /// <summary>
        /// 指定类型的锁是否处于激活状态（引用计数 > 0）。
        /// </summary>
        /// <param name="lockType">锁类型标识。</param>
        /// <returns>如果引用计数大于 0 则返回 true。</returns>
        bool IsLocked(int lockType);

        /// <summary>
        /// 获取指定类型锁的引用计数。
        /// </summary>
        /// <param name="lockType">锁类型标识。</param>
        /// <returns>当前引用计数。</returns>
        int GetLockCount(int lockType);

        /// <summary>
        /// 锁定事件：当某类型锁从"无锁"变为"有锁"时触发（引用计数 0→1）。
        /// <para>参数为锁类型和持有者。</para>
        /// </summary>
        event Action<int, object> OnLocked;

        /// <summary>
        /// 解锁事件：当某类型锁从"有锁"变为"无锁"时触发（引用计数 1→0）。
        /// <para>参数为锁类型和持有者。</para>
        /// </summary>
        event Action<int, object> OnUnlocked;
    }
}
