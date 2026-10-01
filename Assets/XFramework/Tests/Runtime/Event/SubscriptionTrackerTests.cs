using NUnit.Framework;
using XFramework.XEvent;

namespace XFramework.XEvent.Tests
{
    /// <summary>
    /// <see cref="SubscriptionTracker"/> 的自测。它自己必须先准确——否则接入它的那些 fixture 全是恒真的。
    /// <para>每个用例都从 <see cref="SubscriptionTracker.Reset"/> 出发(约束 2:跟踪器必须可复位,
    /// 否则它自己就是泄漏源),故断言可以用绝对值;接入到别的 fixture 时才用 diff。</para>
    /// </summary>
    [TestFixture]
    public class SubscriptionTrackerTests
    {
        [SetUp]
        public void SetUp() => SubscriptionTracker.Reset();

        [TearDown]
        public void TearDown() => SubscriptionTracker.Reset();

        [Test]
        public void SubscribeThenDispose_CountsUpAndDown()
        {
            Assert.AreEqual(0, SubscriptionTracker.LiveCount, "复位后归零");

            var stream = EventStream.Create<int>();
            var a = stream.Subscribe(_ => { });
            var b = stream.Subscribe(_ => { });
            Assert.AreEqual(2, SubscriptionTracker.LiveCount, "两条流订阅");

            a.Dispose();
            Assert.AreEqual(1, SubscriptionTracker.LiveCount, "退订递减");

            b.Dispose();
            Assert.AreEqual(0, SubscriptionTracker.LiveCount, "全部退订后归零");
        }

        [Test]
        public void StreamDispose_DropsRemainingAtOnce()
        {
            var stream = EventStream.Create<int>();
            stream.Subscribe(_ => { });
            stream.Subscribe(_ => { });
            Assert.AreEqual(2, SubscriptionTracker.LiveCount);

            stream.Dispose();

            Assert.AreEqual(0, SubscriptionTracker.LiveCount,
                "Dispose 是持有者主动终止,应按剩余订阅数一次性递减(而不是逐个走退订)");
        }

        [Test]
        public void CompletedStreamEmptyHandle_IsNotCounted()
        {
            var stream = EventStream.Create<int>();
            stream.Complete();

            var handle = stream.Subscribe(_ => { });
            Assert.AreEqual(0, SubscriptionTracker.LiveCount, "completed 流返回空句柄,不登记故不计数");

            handle.Dispose();
            Assert.AreEqual(0, SubscriptionTracker.LiveCount, "共享空句柄的释放同样不得递减(否则计数会变负)");
        }

        [Test]
        public void BufferedStream_CountsItsSubscriptions()
        {
            var stream = EventStream.CreateBuffered<int>();
            var handle = stream.Subscribe(_ => { });

            Assert.AreEqual(1, SubscriptionTracker.LiveCount, "缓冲流的订阅同样经流侧登记点");

            handle.Dispose();
            Assert.AreEqual(0, SubscriptionTracker.LiveCount);
        }

        [Test]
        public void Leak_IsVisibleAsDiff()
        {
            // 判别力:不退订必须能被 diff 看见——这正是接入到其他 fixture 的断言依据
            var mark = SubscriptionTracker.LiveCount;
            var stream = EventStream.Create<int>();
            stream.Subscribe(_ => { });   // 故意不退订

            Assert.AreNotEqual(mark, SubscriptionTracker.LiveCount, "未退订的订阅必须使计数偏离 mark");
        }
    }
}
