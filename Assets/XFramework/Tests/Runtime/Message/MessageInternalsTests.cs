using NUnit.Framework;
using XFramework.XMessage.Internal;

namespace XFramework.XMessage.Tests
{
    /// <summary>
    /// Message 模块内部工具类的契约测试。
    /// <para>
    /// 这里的工具都是 Message 自己的 internal 实现(事件引擎拆出为 XEvent 模块后,这些留在了本模块):
    /// <c>ActionDisposable</c> 服务 broker 的「无需登记」早退路径。它与
    /// <c>XEvent.Internal.ActionDisposable</c> 是同名不同模块的两份实现,故各自锁各自的契约
    /// ——合并它们等于重建跨模块实现依赖,那正是引擎拆出去要消掉的东西。
    /// </para>
    /// <para>本 fixture 不触碰静态门面,故无 SetUp/TearDown。</para>
    /// </summary>
    [TestFixture]
    public class MessageInternalsTests
    {
        [Test]
        public void ActionDisposable_DisposeOnce_IgnoresRepeat()
        {
            var count = 0;
            var disposable = ActionDisposable.Create(() => count++);

            disposable.Dispose();
            disposable.Dispose();

            Assert.AreEqual(1, count, "Dispose 只执行一次");
        }

        [Test]
        public void ActionDisposable_Empty_IsSharedAndNotNull()
        {
            Assert.IsNotNull(ActionDisposable.Empty, "空句柄不得为 null——调用方直接 Dispose,不判空");
            Assert.AreSame(ActionDisposable.Empty, ActionDisposable.Empty, "空句柄是共享单例");
            Assert.DoesNotThrow(() => ActionDisposable.Empty.Dispose(), "共享空句柄重复释放是 no-op");
        }
    }
}
