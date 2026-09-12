using System;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using XFramework.XMessage;

namespace XFramework.XMessage.Tests
{
    /// <summary>
    /// Tests for the runtime statistics API (<see cref="MessageManager.GetStats"/> /
    /// <see cref="MessageManager.GetChannelStats{TMessage}"/>).
    /// </summary>
    [TestFixture]
    public class MessageStatsTests
    {
        private sealed class TestMessage
        {
            public int Value { get; set; }
        }

        private sealed class AnotherMessage
        {
            public int Value { get; set; }
        }

        private sealed class TestRequest
        {
            public int Input { get; set; }
        }

        private sealed class TestResponse
        {
            public int Result { get; set; }
        }

        private sealed class BlockNegativeFilter : IMessageFilter<TestMessage>
        {
            public void Invoke(TestMessage message, Action<TestMessage> next)
            {
                if (message.Value >= 0) next(message);
            }
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

        #region 总线快照

        [Test]
        public void GetStats_CountsChannelsSubscriptionsAndPublishes()
        {
            var sync = MessageManager.Subscribe<TestMessage>(_ => { });
            var buffered = MessageManager.SubscribeBuffered<AnotherMessage>(_ => { });
            MessageManager.SubscribeAsync<TestMessage>((msg, ct) => UniTask.CompletedTask);

            var beforePublish = MessageManager.GetStats();
            Assert.AreEqual(2, beforePublish.SyncSubscriptionCount, "1 个普通订阅 + 1 个缓冲订阅");
            Assert.AreEqual(1, beforePublish.AsyncSubscriptionCount);
            Assert.AreEqual(0, beforePublish.PublishCount);
            Assert.AreEqual(1, beforePublish.BufferedChannelCount,
                "此时只有 SubscribeBuffered 的通道持有重放缓存");

            MessageManager.Publish(new TestMessage { Value = 1 });
            MessageManager.Publish(new TestMessage { Value = 2 });
            MessageManager.Publish("k", new AnotherMessage { Value = 3 });

            var afterPublish = MessageManager.GetStats();
            Assert.AreEqual(3, afterPublish.PublishCount, "两次无键发布 + 一次键值发布");
            Assert.AreEqual(3, afterPublish.ChannelCount,
                "TestMessage 类型通道 + AnotherMessage 类型通道 + 键值 k 通道");

            // 发布也会为「订阅前无缓冲订阅」的通道建立重放缓存(订阅前发布可重放语义)
            Assert.AreEqual(3, afterPublish.BufferedChannelCount);

            sync.Dispose();
            buffered.Dispose();

            var afterDispose = MessageManager.GetStats();
            Assert.AreEqual(0, afterDispose.SyncSubscriptionCount, "同步订阅全部退订");
            Assert.AreEqual(1, afterDispose.AsyncSubscriptionCount, "异步订阅仍在");
        }

        [Test]
        public void GetStats_AfterClear_AllZero()
        {
            MessageManager.Subscribe<TestMessage>(_ => { });
            MessageManager.SubscribeAsync<TestMessage>((msg, ct) => UniTask.CompletedTask);
            MessageManager.SubscribeBuffered<AnotherMessage>(_ => { });
            MessageManager.Publish(new TestMessage { Value = 1 });
            MessageManager.AddFilter<TestMessage>(new BlockNegativeFilter());
            MessageManager.Register<TestRequest, TestResponse>((req, ct) => UniTask.FromResult(new TestResponse()));
            MessageManager.RequestAsync<TestRequest, TestResponse>(new TestRequest()).GetAwaiter().GetResult();

            MessageManager.Clear();

            var stats = MessageManager.GetStats();
            Assert.AreEqual(0, stats.MessageTypeCount);
            Assert.AreEqual(0, stats.ChannelCount);
            Assert.AreEqual(0, stats.SyncSubscriptionCount);
            Assert.AreEqual(0, stats.AsyncSubscriptionCount);
            Assert.AreEqual(0, stats.BufferedChannelCount);
            Assert.AreEqual(0, stats.PublishCount);
            Assert.AreEqual(0, stats.RequestCount);
            Assert.AreEqual(0, stats.RequestHandlerCount);
            Assert.AreEqual(0, stats.FilterCount);
        }

        [Test]
        public void GetStats_CountsRequestsAndFilters()
        {
            Assert.AreEqual(0, MessageManager.GetStats().RequestHandlerCount);

            MessageManager.Register<TestRequest, TestResponse>((req, ct) =>
                UniTask.FromResult(new TestResponse { Result = req.Input }));
            Assert.AreEqual(1, MessageManager.GetStats().RequestHandlerCount);

            MessageManager.RequestAsync<TestRequest, TestResponse>(new TestRequest { Input = 1 })
                .GetAwaiter().GetResult();
            MessageManager.RequestAsync<TestRequest, TestResponse>(new TestRequest { Input = 2 })
                .GetAwaiter().GetResult();
            Assert.AreEqual(2, MessageManager.GetStats().RequestCount);

            MessageManager.AddFilter<TestMessage>(new BlockNegativeFilter());
            Assert.AreEqual(1, MessageManager.GetStats().FilterCount);

            MessageManager.Unregister<TestRequest, TestResponse>();
            Assert.AreEqual(0, MessageManager.GetStats().RequestHandlerCount, "注销后处理器数回落");
        }

        [Test]
        public void GetStats_RequestWithoutHandler_DoesNotCount()
        {
            Assert.Throws<InvalidOperationException>(() =>
                MessageManager.RequestAsync<TestRequest, TestResponse>(new TestRequest()));

            Assert.AreEqual(0, MessageManager.GetStats().RequestCount, "未派发的请求不计入");
        }

        [Test]
        public void GetStats_TryRequestAsyncWithoutHandler_DoesNotCount()
        {
            var result = MessageManager.TryRequestAsync<TestRequest, TestResponse>(new TestRequest())
                .GetAwaiter().GetResult();

            Assert.IsFalse(result.Success, "前置:未注册处理器");
            Assert.AreEqual(0, MessageManager.GetStats().RequestCount,
                "TryRequestAsync 未派发时同样不计入,与 RequestAsync 抛出前不计数一致");
        }

        #endregion

        #region 通道快照

        [Test]
        public void GetChannelStats_UnknownType_ReturnsZeroSnapshot()
        {
            var stats = MessageManager.GetChannelStats<TestMessage>();

            Assert.AreEqual(0, stats.SyncSubscriptionCount);
            Assert.AreEqual(0, stats.AsyncSubscriptionCount);
            Assert.IsFalse(stats.HasBufferedValue);
            Assert.AreEqual(0, stats.KeyedChannelCount);
        }

        [Test]
        public void GetChannelStats_TypeLevel_ReflectsSubscriptionsAndBuffer()
        {
            MessageManager.Subscribe<TestMessage>(_ => { });
            MessageManager.SubscribeAsync<TestMessage>((msg, ct) => UniTask.CompletedTask);
            MessageManager.Publish(new TestMessage { Value = 1 });

            var stats = MessageManager.GetChannelStats<TestMessage>();

            Assert.AreEqual(1, stats.SyncSubscriptionCount);
            Assert.AreEqual(1, stats.AsyncSubscriptionCount);
            Assert.IsTrue(stats.HasBufferedValue, "发布为该类型通道建立了重放缓存");
        }

        [Test]
        public void GetChannelStats_Keyed_ReflectsThatKeyOnly()
        {
            MessageManager.Subscribe<string, TestMessage>("a", _ => { });
            MessageManager.Subscribe<string, TestMessage>("b", _ => { });
            MessageManager.Publish("a", new TestMessage { Value = 1 });

            var byKeyA = MessageManager.GetChannelStats<string, TestMessage>("a");
            Assert.AreEqual(1, byKeyA.SyncSubscriptionCount);
            Assert.IsTrue(byKeyA.HasBufferedValue);

            var byKeyB = MessageManager.GetChannelStats<string, TestMessage>("b");
            Assert.AreEqual(1, byKeyB.SyncSubscriptionCount);
            Assert.IsFalse(byKeyB.HasBufferedValue, "b 从未发布过,不应有重放缓存");

            var missing = MessageManager.GetChannelStats<string, TestMessage>("c");
            Assert.AreEqual(0, missing.SyncSubscriptionCount, "不存在的 Key 返回全 0 快照");
        }

        [Test]
        public void GetChannelStats_TypeLevel_ReportsKeyedChannelCount()
        {
            MessageManager.Publish("a", new TestMessage { Value = 1 });
            MessageManager.Publish("b", new TestMessage { Value = 2 });
            MessageManager.Publish(1, new TestMessage { Value = 3 });

            var stats = MessageManager.GetChannelStats<TestMessage>();

            Assert.AreEqual(3, stats.KeyedChannelCount, "两种 Key 类型下的键值通道合计 3 条");
        }

        #endregion
    }
}
