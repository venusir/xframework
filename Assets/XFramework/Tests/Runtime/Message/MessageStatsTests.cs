using System;
using System.Collections.Generic;
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
            Assert.AreEqual(0, stats.ChannelStoreCount);
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

        #region 按类型下钻

        /// <summary>
        /// 按消息类型取行。行序未定义(受内部字典遍历序影响),故一律按类型查、不按下标取。
        /// </summary>
        private static MessageTypeStats RowOf(List<MessageTypeStats> rows, Type messageType)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].MessageType == messageType)
                    return rows[i];
            }

            Assert.Fail($"缓冲区里没有消息类型 {messageType.Name} 的统计行");
            return default;
        }

        [Test]
        public void CopyTypeStats_EmptyBus_ReturnsZeroAndClearsBuffer()
        {
            var buffer = new List<MessageTypeStats> { default };

            var count = MessageManager.CopyTypeStats(buffer);

            Assert.AreEqual(0, count, "空总线应写 0 行");
            Assert.AreEqual(0, buffer.Count, "进入时应先清空缓冲区(而非覆盖到一半)");
        }

        [Test]
        public void CopyTypeStats_MergesTypeLevelAndKeyedIntoOneRow()
        {
            MessageManager.Subscribe<TestMessage>(_ => { });
            MessageManager.SubscribeAsync<TestMessage>((msg, ct) => UniTask.CompletedTask);
            MessageManager.Publish(new TestMessage { Value = 0 });        // 类型级通道 + 缓冲流
            MessageManager.Publish("a", new TestMessage { Value = 1 });   // 键值通道 a
            MessageManager.Publish("b", new TestMessage { Value = 2 });   // 键值通道 b
            MessageManager.Publish(1, new TestMessage { Value = 3 });     // 另一种 Key 类型
            MessageManager.Publish(new AnotherMessage { Value = 4 });     // 另一个消息类型

            var buffer = new List<MessageTypeStats>();

            Assert.AreEqual(2, MessageManager.CopyTypeStats(buffer), "两个消息类型各一行");

            var row = RowOf(buffer, typeof(TestMessage));
            Assert.AreEqual(4, row.ChannelCount, "类型级 1 条 + 键值 3 条");
            Assert.AreEqual(3, row.KeyedChannelCount, "两种 Key 类型下的键值通道合计");
            Assert.AreEqual(1, row.SyncSubscriptionCount);
            Assert.AreEqual(1, row.AsyncSubscriptionCount);
            Assert.AreEqual(4, row.BufferedChannelCount, "四条通道都建过缓冲流");

            var other = RowOf(buffer, typeof(AnotherMessage));
            Assert.AreEqual(1, other.ChannelCount, "另一个消息类型只有 1 条类型级通道");
            Assert.AreEqual(0, other.KeyedChannelCount);
        }

        [Test]
        public void CopyTypeStats_KeyedOnlyType_StillGetsARow()
        {
            // 只有键值通道、没有类型级通道的类型:归并时新起一行(第 2 步的建行分支)
            MessageManager.Publish("a", new TestMessage { Value = 1 });
            MessageManager.Publish(1, new TestMessage { Value = 2 });

            var buffer = new List<MessageTypeStats>();

            Assert.AreEqual(1, MessageManager.CopyTypeStats(buffer));
            Assert.AreEqual(2, buffer[0].ChannelCount, "只有键值通道时,通道数即键值通道数");
            Assert.AreEqual(2, buffer[0].KeyedChannelCount);
            Assert.AreEqual(0, buffer[0].SyncSubscriptionCount);
        }

        [Test]
        public void CopyTypeStats_RowsSumToBusStats()
        {
            // 不变量:两套统计互为校验。任一侧口径被改(例如又去动「缓冲通道是否必须持有重放缓存」),这里先红
            MessageManager.Subscribe<TestMessage>(_ => { });
            MessageManager.SubscribeBuffered<AnotherMessage>(_ => { });
            MessageManager.SubscribeAsync<TestMessage>((msg, ct) => UniTask.CompletedTask);
            MessageManager.Publish("a", new TestMessage { Value = 1 });
            MessageManager.Publish(7, new TestMessage { Value = 2 });
            MessageManager.Publish(new AnotherMessage { Value = 3 });

            var buffer = new List<MessageTypeStats>();
            MessageManager.CopyTypeStats(buffer);
            var bus = MessageManager.GetStats();

            var channels = 0;
            var sync = 0;
            var async = 0;
            var buffered = 0;
            for (int i = 0; i < buffer.Count; i++)
            {
                channels += buffer[i].ChannelCount;
                sync += buffer[i].SyncSubscriptionCount;
                async += buffer[i].AsyncSubscriptionCount;
                buffered += buffer[i].BufferedChannelCount;
            }

            Assert.AreEqual(bus.ChannelCount, channels, "各行通道数之和应等于总线通道数");
            Assert.AreEqual(bus.SyncSubscriptionCount, sync, "各行同步订阅数之和应等于总线同步订阅数");
            Assert.AreEqual(bus.AsyncSubscriptionCount, async, "各行异步订阅数之和应等于总线异步订阅数");
            Assert.AreEqual(bus.BufferedChannelCount, buffered, "各行缓冲通道数之和应等于总线缓冲通道数");
        }

        [Test]
        public void CopyTypeStats_NullBuffer_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => MessageManager.CopyTypeStats(null));
        }

        [Test]
        public void CopyTypeStats_ReusesCallerBuffer_WithoutAllocating()
        {
            MessageManager.Subscribe<TestMessage>(_ => { });
            MessageManager.Publish("a", new TestMessage { Value = 1 });
            MessageManager.Publish(1, new TestMessage { Value = 2 });
            MessageManager.Publish(new AnotherMessage { Value = 3 });

            var buffer = new List<MessageTypeStats>(8);
            for (int i = 0; i < 4; i++)
                MessageManager.CopyTypeStats(buffer);   // 预热:容量与内部状态就位

            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < 100; i++)
                MessageManager.CopyTypeStats(buffer);

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0, allocated,
                "由调用方持有缓冲区,反复调用不应产生 GC——这是「零分配主入口」的全部意义");
        }

        [Test]
        public void Stats_ToString_AreCompactAndReadable()
        {
            // 三个统计结构的 ToString() 此前零断言(审计的「公开成员 → 有无直接用例」映射发现的另一处缺口)。
            // 只断言关键词与数字出现在其中,不逐字锁格式——格式属实现细节,断言太死会变成第二份真相。
            MessageManager.Subscribe<TestMessage>(_ => { });
            MessageManager.Publish(new TestMessage { Value = 1 });

            var bus = MessageManager.GetStats().ToString();
            StringAssert.Contains("通道", bus);
            StringAssert.Contains("发布 1", bus, "发布次数应出现在总线快照里");

            var channel = MessageManager.GetChannelStats<TestMessage>().ToString();
            StringAssert.Contains("同步订阅 1", channel, "该类型级通道有 1 个同步订阅");

            var rows = new List<MessageTypeStats>();
            MessageManager.CopyTypeStats(rows);
            StringAssert.Contains(nameof(TestMessage), rows[0].ToString(), "按类型下钻的行应带上消息类型名");
        }

        #endregion
    }
}
