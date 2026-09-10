using System;
using System.Collections.Generic;
using NUnit.Framework;
using XFramework.XMessage;

namespace XFramework.XMessage.Tests
{
    /// <summary>
    /// Tests for buffered-channel eviction and empty-channel reclamation.
    /// <para>
    /// 语义边界:订阅清零的通道自动回收;持有重放缓存的缓冲通道<b>不</b>自动回收,
    /// 只能经 EvictBufferedChannel 系列显式淘汰。
    /// </para>
    /// </summary>
    [TestFixture]
    public class MessageEvictionTests
    {
        private sealed class TestMessage
        {
            public int Value { get; set; }
        }

        private sealed class AnotherMessage
        {
            public int Value { get; set; }
        }

        [SetUp]
        public void SetUp()
        {
            MessageManager.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            MessageManager.Clear();
        }

        #region 显式淘汰

        [Test]
        public void EvictBufferedChannel_RemovesReplayCache()
        {
            MessageManager.Publish(new TestMessage { Value = 77 });

            Assert.IsTrue(MessageManager.EvictBufferedChannel<TestMessage>(), "首次淘汰应命中");
            Assert.IsFalse(MessageManager.EvictBufferedChannel<TestMessage>(), "缓冲已淘汰,重复淘汰应返回 false");

            var received = new List<int>();
            MessageManager.SubscribeBuffered<TestMessage>(msg => received.Add(msg.Value));

            Assert.AreEqual(0, received.Count, "淘汰后新订阅者不应收到重放");
        }

        [Test]
        public void EvictBufferedChannel_Keyed_RemovesOnlyThatKey()
        {
            MessageManager.Publish("a", new TestMessage { Value = 1 });
            MessageManager.Publish("b", new TestMessage { Value = 2 });

            Assert.IsTrue(MessageManager.EvictBufferedChannel<string, TestMessage>("a"));

            var receivedA = new List<int>();
            var receivedB = new List<int>();
            MessageManager.SubscribeBuffered<string, TestMessage>("a", msg => receivedA.Add(msg.Value));
            MessageManager.SubscribeBuffered<string, TestMessage>("b", msg => receivedB.Add(msg.Value));

            Assert.AreEqual(0, receivedA.Count, "被淘汰的 Key 不应重放");
            CollectionAssert.AreEqual(new[] { 2 }, receivedB, "其他 Key 的重放不受影响");
        }

        [Test]
        public void EvictBufferedChannel_ThenPublish_ReplayWorksAgain()
        {
            MessageManager.Publish(new TestMessage { Value = 1 });
            MessageManager.EvictBufferedChannel<TestMessage>();

            MessageManager.Publish(new TestMessage { Value = 5 });

            var received = new List<int>();
            MessageManager.SubscribeBuffered<TestMessage>(msg => received.Add(msg.Value));

            CollectionAssert.AreEqual(new[] { 5 }, received, "淘汰后重新发布应重建重放缓存");
        }

        [Test]
        public void EvictBufferedChannels_RemovesTypeAndAllKeys()
        {
            MessageManager.Publish(new TestMessage { Value = 1 });
            MessageManager.Publish("a", new TestMessage { Value = 2 });
            MessageManager.Publish("b", new TestMessage { Value = 3 });

            var removed = MessageManager.EvictBufferedChannels<TestMessage>();

            Assert.AreEqual(3, removed, "类型级 1 条 + 键值 2 条应全部淘汰");
            Assert.AreEqual(0, MessageManager.EvictBufferedChannels<TestMessage>(), "重复淘汰应返回 0");
        }

        #endregion

        #region 空通道回收

        [Test]
        public void UnsubscribeLastSubscriber_AutoReclaimsChannel()
        {
            var handle = MessageManager.Subscribe<TestMessage>(_ => { });
            handle.Dispose();

            Assert.AreEqual(0, MessageManager.TrimEmptyChannels(),
                "订阅清零的通道已被自动回收,不应再有空通道可回收");

            // 回收后重新订阅仍须正常工作
            var received = new List<int>();
            MessageManager.Subscribe<TestMessage>(msg => received.Add(msg.Value));
            MessageManager.Publish(new TestMessage { Value = 9 });

            CollectionAssert.AreEqual(new[] { 9 }, received, "回收后重建的通道投递正常");
        }

        [Test]
        public void UnsubscribeLastKeyedSubscriber_AutoReclaimsKeyBucket()
        {
            var handle = MessageManager.Subscribe<string, TestMessage>("k", _ => { });
            handle.Dispose();

            Assert.AreEqual(0, MessageManager.TrimEmptyChannels(),
                "键值通道订阅清零后应被自动回收");

            var received = new List<int>();
            MessageManager.Subscribe<string, TestMessage>("k", msg => received.Add(msg.Value));
            MessageManager.Publish("k", new TestMessage { Value = 4 });

            CollectionAssert.AreEqual(new[] { 4 }, received, "回收后重建的键值通道投递正常");
        }

        [Test]
        public void TrimEmptyChannels_DoesNotBreakBufferedReplay()
        {
            MessageManager.Publish(new TestMessage { Value = 7 });

            // 该通道持有重放缓存,不属可回收之列
            Assert.AreEqual(0, MessageManager.TrimEmptyChannels(), "持有重放缓存的通道不应被回收");

            var received = new List<int>();
            MessageManager.SubscribeBuffered<TestMessage>(msg => received.Add(msg.Value));

            CollectionAssert.AreEqual(new[] { 7 }, received, "Trim 不得破坏缓冲通道的重放缓存");
        }

        [Test]
        public void TrimEmptyChannels_KeepsActiveChannels()
        {
            var received = new List<int>();
            MessageManager.Subscribe<TestMessage>(msg => received.Add(msg.Value));

            Assert.AreEqual(0, MessageManager.TrimEmptyChannels(), "仍有订阅者的通道不应被回收");

            MessageManager.Publish(new TestMessage { Value = 3 });

            CollectionAssert.AreEqual(new[] { 3 }, received, "回收后活跃通道投递正常");
        }

        [Test]
        public void TrimEmptyChannels_AfterEviction_ReclaimsChannel()
        {
            MessageManager.Publish(new TestMessage { Value = 7 });
            MessageManager.EvictBufferedChannel<TestMessage>();

            Assert.AreEqual(1, MessageManager.TrimEmptyChannels(), "淘汰缓冲后的空通道应可被回收");
            Assert.AreEqual(0, MessageManager.TrimEmptyChannels(), "回收后不应再有空通道");
        }

        #endregion

        #region 派发中与释放路径的健壮性

        [Test]
        public void UnsubscribeDuringDispatch_LastSubscriber_DoesNotThrow()
        {
            IDisposable handle = null;
            handle = MessageManager.Subscribe<TestMessage>(_ => handle.Dispose());

            Assert.DoesNotThrow(() => MessageManager.Publish(new TestMessage { Value = 1 }),
                "派发中退订最后一个订阅者触发通道回收,不得抛异常");

            // 回收不得留下损坏状态:重新订阅后投递仍正常
            var received = new List<int>();
            MessageManager.Subscribe<TestMessage>(msg => received.Add(msg.Value));
            MessageManager.Publish(new TestMessage { Value = 2 });

            CollectionAssert.AreEqual(new[] { 2 }, received, "回收后重建的通道投递正常");
        }

        [Test]
        public void SubscriptionHandle_DisposedAfterEvict_DoesNotBreakLaterSubscribers()
        {
            // 淘汰会释放缓冲流并回收其订阅节点;此后陈旧句柄的 Dispose 不得再触碰该节点,
            // 否则会重复回池,导致同一节点被发放给两个订阅者
            var stale = MessageManager.SubscribeBuffered<TestMessage>(_ => { });
            MessageManager.Publish(new TestMessage { Value = 1 });
            MessageManager.EvictBufferedChannel<TestMessage>();

            Assert.DoesNotThrow(() => stale.Dispose(), "淘汰后释放陈旧句柄不得抛异常");

            var received = new List<int>();
            MessageManager.SubscribeBuffered<TestMessage>(msg => received.Add(msg.Value));

            MessageManager.Publish(new TestMessage { Value = 2 });

            CollectionAssert.AreEqual(new[] { 2 }, received, "陈旧句柄释放后,新订阅者仍须恰好收到一条实时消息");
        }

        [Test]
        public void Clear_WithActiveSubscriptionsAndBuffers_DoesNotThrow()
        {
            MessageManager.Subscribe<TestMessage>(_ => { });
            MessageManager.SubscribeBuffered<AnotherMessage>(_ => { });
            MessageManager.Publish(new TestMessage { Value = 1 });
            MessageManager.Publish(new AnotherMessage { Value = 2 });

            Assert.DoesNotThrow(() => MessageManager.Clear());
            Assert.AreEqual(0, MessageManager.TrimEmptyChannels(), "Clear 之后不应残留通道");
        }

        #endregion
    }
}
