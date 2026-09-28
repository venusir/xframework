namespace XFramework.XLock
{

    /// <summary>
    /// 可锁定标记接口。实现此接口的对象可通过 <see cref="LockableExtensions"/> 扩展方法获得全局锁服务能力。
    /// <para><b>必须由引用类型实现</b>：本模块以主体作容器键、按**引用同一**判定身份（不看 <c>Equals</c>
    /// 重写——实体常按 Id 判等，若按值相等，两个不同实体就会被当成同一个）。struct 每次装箱都是新身份，
    /// 键永不相等，加锁即等于泄漏。</para>
    /// <para>使用示例：</para>
    /// <code>
    /// public class MyPlayer : ILockable
    /// {
    ///     public void DoSomething()
    ///     {
    ///         using (this.AddLock(LockType.Input, this))
    ///         {
    ///             // ...
    ///         }
    ///     }
    /// }
    /// </code>
    /// </summary>
    public interface ILockable
    {
    }
}
