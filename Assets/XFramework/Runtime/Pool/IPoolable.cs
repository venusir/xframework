namespace XFramework.XPool
{
    /// <summary>
    /// 对象生命周期回调（可选接口）。
    /// <para>实现此接口的对象在被取出或归还池时自动收到回调，用于重置状态或清理临时数据。</para>
    /// <para>未实现此接口的对象仍可正常出入池，只是不触发回调。</para>
    /// <para>如果同时传入委托回调和实现此接口，委托优先触发。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// public class BulletData : IPoolable
    /// {
    ///     public Vector3 Position;
    ///     public bool IsAlive;
    ///     
    ///     void IPoolable.OnRent() => IsAlive = true;
    ///     void IPoolable.OnReturn() => IsAlive = false;
    /// }
    /// </code>
    /// </example>
    public interface IPoolable
    {
        /// <summary>
        /// 从池中取出时调用。可用于初始化或重置状态。
        /// </summary>
        void OnRent();

        /// <summary>
        /// 归还池时调用。可用于清理临时数据。
        /// </summary>
        void OnReturn();
    }
}