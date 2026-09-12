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
    /// 只能经 EvictBufferedChannel 系列显式淘汰——淘汰本身会顺带回收因此变空的通道与存储,
    /// 故 TrimEmptyChannels 退居兜底,常规路径下返回 0。
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

            // 返回值只计淘汰数,不含顺带回收数;条目本身则必须真的清空
            Assert.AreEqual(0, MessageManager.GetStats().ChannelCount, "淘汰应顺带回收全部空通道");
            Assert.AreEqual(0, MessageManager.GetStats().MessageTypeCount);
            Assert.AreEqual(0, MessageManager.TrimEmptyChannels(), "已无可回收残留");
        }

        [Test]
        public void EvictBufferedChannels_ByKey_SpansAllMessageTypes()
        {
            // 同一实体往往参与多种带 Key 的消息类型;按消息类型淘汰要写 N 次,按 Key 一次覆盖
            MessageManager.Publish("e1", new TestMessage { Value = 1 });
            MessageManager.Publish("e1", new AnotherMessage { Value = 2 });

            Assert.AreEqual(2, MessageManager.EvictBufferedChannels("e1"),
                "同一 Key 下多个消息类型的缓冲通道都应淘汰");

            // 条目本身必须真的清空(返回值只计淘汰数,不含顺带回收数)
            Assert.AreEqual(0, MessageManager.GetStats().ChannelCount, "淘汰应顺带回收全部空通道");
            Assert.AreEqual(0, MessageManager.GetStats().MessageTypeCount, "空的键值存储表项应一并摘除");
            Assert.AreEqual(0, MessageManager.EvictBufferedChannels("e1"), "重复淘汰应返回 0");

            var fromTest = new List<int>();
            var fromAnother = new List<int>();
            MessageManager.SubscribeBuffered<string, TestMessage>("e1", msg => fromTest.Add(msg.Value));
            MessageManager.SubscribeBuffered<string, AnotherMessage>("e1", msg => fromAnother.Add(msg.Value));

            Assert.AreEqual(0, fromTest.Count, "被淘汰的消息类型不应重放");
            Assert.AreEqual(0, fromAnother.Count, "被淘汰的消息类型不应重放");
        }

        [Test]
        public void EvictBufferedChannels_ByKey_LeavesOtherKeysAndKeyTypesUntouched()
        {
            MessageManager.Publish("e1", new TestMessage { Value = 1 });
            MessageManager.Publish("e2", new TestMessage { Value = 2 });
            MessageManager.Publish(1, new TestMessage { Value = 3 });

            Assert.AreEqual(1, MessageManager.EvictBufferedChannels("e1"), "只淘汰 Key 类型与 Key 值都匹配的通道");

            var e1 = new List<int>();
            var e2 = new List<int>();
            var byIntKey = new List<int>();
            MessageManager.SubscribeBuffered<string, TestMessage>("e1", msg => e1.Add(msg.Value));
            MessageManager.SubscribeBuffered<string, TestMessage>("e2", msg => e2.Add(msg.Value));
            MessageManager.SubscribeBuffered<int, TestMessage>(1, msg => byIntKey.Add(msg.Value));

            Assert.AreEqual(0, e1.Count, "被淘汰的 Key 不应重放");
            CollectionAssert.AreEqual(new[] { 2 }, e2, "同一消息类型下的其他 Key 不受影响");
            CollectionAssert.AreEqual(new[] { 3 }, byIntKey, "其他 Key 类型不受影响");
        }

        [Test]
        public void EvictBufferedChannels_ByKey_WithLiveSubscriber_KeepsChannel()
        {
            var received = new List<int>();
            MessageManager.Subscribe<string, TestMessage>("e1", msg => received.Add(msg.Value));
            MessageManager.Publish("e1", new TestMessage { Value = 1 });
            CollectionAssert.AreEqual(new[] { 1 }, received, "前置:同步订阅者已收到消息");

            Assert.AreEqual(1, MessageManager.EvictBufferedChannels("e1"));

            // 与类型级版本共用同一条 IsReclaimable 闸门,删掉它会静默掐死活订阅
            Assert.AreEqual(1, MessageManager.GetStats().ChannelCount,
                "仍有同步订阅者的通道不得被顺带回收");

            MessageManager.Publish("e1", new TestMessage { Value = 2 });
            CollectionAssert.AreEqual(new[] { 1, 2 }, received, "淘汰不得影响同步投递");
        }

        [Test]
        public void EvictBufferedChannel_Keyed_LastKey_AlsoDropsStore()
        {
            MessageManager.Publish("only", new TestMessage { Value = 1 });

            Assert.IsTrue(MessageManager.EvictBufferedChannel<string, TestMessage>("only"));

            // 存储整体为空时必须连 _keyedChannels 表项一并摘除,
            // 否则 GetStats().MessageTypeCount 会虚高(键值表按 (消息类型, Key 类型) 计数)
            Assert.AreEqual(0, MessageManager.GetStats().MessageTypeCount,
                "最后一个键被淘汰后应连存储表项一并摘除");
            Assert.AreEqual(0, MessageManager.GetStats().ChannelCount);
            Assert.AreEqual(0, MessageManager.TrimEmptyChannels(), "已无可回收残留");
        }

        [Test]
        public void EvictBufferedChannel_WithLiveSubscriber_KeepsChannel()
        {
            var received = new List<int>();
            MessageManager.Subscribe<TestMessage>(msg => received.Add(msg.Value));
            MessageManager.Publish(new TestMessage { Value = 1 });
            CollectionAssert.AreEqual(new[] { 1 }, received, "前置:同步订阅者已收到消息");

            Assert.IsTrue(MessageManager.EvictBufferedChannel<TestMessage>());

            // 淘汰只丢重放缓存,不得回收仍有活订阅者的通道 ——
            // 回收前的 IsReclaimable 复查是这里的正确性闸门,删掉它会静默掐死活订阅
            Assert.AreEqual(1, MessageManager.GetStats().ChannelCount,
                "仍有同步订阅者的通道不得被顺带回收");

            MessageManager.Publish(new TestMessage { Value = 2 });
            CollectionAssert.AreEqual(new[] { 1, 2 }, received, "淘汰不得影响同步投递");
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
        public void EvictBufferedChannel_AlsoReclaimsEmptiedChannel()
        {
            MessageManager.Publish(new TestMessage { Value = 7 });

            Assert.IsTrue(MessageManager.EvictBufferedChannel<TestMessage>());

            // 通道条目必须真的从表里消失,而不是留下一个「已可回收但仍在表中」的空壳。
            // 不能用 GetChannelStats<T>() 断言:空壳通道的累加结果全 0,与「不存在」不可区分。
            Assert.AreEqual(0, MessageManager.GetStats().ChannelCount, "淘汰应顺带回收空通道");
            Assert.AreEqual(0, MessageManager.GetStats().MessageTypeCount);
            Assert.AreEqual(0, MessageManager.TrimEmptyChannels(), "已无可回收残留");
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
