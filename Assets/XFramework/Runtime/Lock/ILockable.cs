namespace XFramework.XLock
{

    /// <summary>
    /// 可锁定标记接口。实现此接口的对象可通过 <see cref="LockableExtensions"/> 扩展方法获得全局锁服务能力。
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
