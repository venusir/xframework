using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XMessage;
using XFramework.XMessage.Internal;

namespace XFramework.XMessage.Tests
{
    /// <summary>
    /// Tests for async subscriptions: subscription-scoped cancellation, independent registration,
    /// keyed/filtered overloads and exception isolation.
    /// <para>
    /// 注意:本文件不使用依赖 PlayerLoop 的等待(如 UniTask.Delay + GetAwaiter().GetResult()),
    /// 在 Test Runner 主线程上会死锁;全部断言都基于同步可观察的状态。
    /// </para>
    /// </summary>
    [TestFixture]
    public class MessageAsyncSubscriptionTests
    {
        private sealed class TestMessage
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

        #region 订阅级令牌

        [Test]
        public void SubscribeAsync_HandlerReceivesSubscriptionToken_CancelledOnDispose()
        {
            CancellationToken observed = default;
            var handle = MessageManager.SubscribeAsync<TestMessage>((msg, ct) =>
            {
                observed = ct;
                return UniTask.CompletedTask;
            });

            MessageManager.Publish(new TestMessage { Value = 1 });
            Assert.IsTrue(observed.CanBeCanceled, "处理器应收到可取消的订阅令牌");
            Assert.IsFalse(observed.IsCancellationRequested, "退订前令牌不应处于已取消状态");

            handle.Dispose();
            Assert.IsTrue(observed.IsCancellationRequested, "退订应取消处理器收到的令牌");
        }

        [Test]
        public void SubscribeAsync_SubscribeTokenAlreadyCancelled_NotRegistered()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var called = false;
            var handle = MessageManager.SubscribeAsync<TestMessage>(
                (msg, ct) => { called = true; return UniTask.CompletedTask; }, cts.Token);

            // 结构断言必须在 Publish 之前:Publish 自身会建出通道(重放缓存语义),之后再断言就分不清是谁建的。
            // 若令牌守卫被挪回 AddAsyncSubscription 内,实参 GetOrCreateChannel<TMessage>() 已建出空壳通道,
            // 此处会看到 1,且下面那条 Trim 会返回 1。
            Assert.AreEqual(0, MessageManager.GetStats().ChannelCount,
                "已取消的令牌不应创建通道");
            Assert.AreEqual(0, MessageManager.TrimEmptyChannels(),
                "已取消的令牌不应留下待清理的空壳通道");

            MessageManager.Publish(new TestMessage { Value = 1 });

            Assert.IsFalse(called, "已取消的令牌不应登记订阅");
            Assert.DoesNotThrow(() => handle.Dispose(), "返回的空句柄释放时不得抛异常");
        }

        [Test]
        public void SubscribeAsync_Keyed_SubscribeTokenAlreadyCancelled_NotRegistered()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var called = false;
            var handle = MessageManager.SubscribeAsync<string, TestMessage>(
                "k", (msg, ct) => { called = true; return UniTask.CompletedTask; }, cts.Token);

            // 键值版更严格:连整条 _keyedChannels 存储表项都不该被建出来
            Assert.AreEqual(0, MessageManager.GetStats().ChannelCount,
                "已取消的令牌不应创建键值通道");
            Assert.AreEqual(0, MessageManager.GetStats().ChannelStoreCount,
                "已取消的令牌不应创建键值通道存储表项");

            MessageManager.Publish("k", new TestMessage { Value = 1 });

            Assert.IsFalse(called, "已取消的令牌不应登记键值订阅");
            Assert.DoesNotThrow(() => handle.Dispose(), "返回的空句柄释放时不得抛异常");
        }

        [Test]
        public void SubscribeAsync_SubscribeTokenCancelled_Unsubscribes()
        {
            using var cts = new CancellationTokenSource();
            var count = 0;
            MessageManager.SubscribeAsync<TestMessage>(
                (msg, ct) => { count++; return UniTask.CompletedTask; }, cts.Token);

            MessageManager.Publish(new TestMessage { Value = 1 });
            Assert.AreEqual(1, count, "令牌未取消时正常投递");

            cts.Cancel();
            MessageManager.Publish(new TestMessage { Value = 2 });
            Assert.AreEqual(1, count, "令牌取消应自动退订,不再收到消息");
        }

        [Test]
        public void SubscribeAsync_DisposeIsIdempotent()
        {
            var handle = MessageManager.SubscribeAsync<TestMessage>(
                (msg, ct) => UniTask.CompletedTask);

            Assert.DoesNotThrow(() =>
            {
                handle.Dispose();
                handle.Dispose();
            });
        }

        [Test]
        public void SubscribeAsync_LastSubscriptionDisposed_AutoReclaimsChannel()
        {
            var handle = MessageManager.SubscribeAsync<TestMessage>(
                (msg, ct) => UniTask.CompletedTask);
            handle.Dispose();

            Assert.AreEqual(0, MessageManager.TrimEmptyChannels(),
                "只有异步订阅的通道在退订后也应被自动回收");
        }

        #endregion

        #region 登记表不变量

        /// <summary>
        /// 令牌在构造期间被取消时,登记项不得入表。
        /// <para>
        /// 复现的是竞态窗口:守卫判定之后、<c>AsyncSubscription</c> 构造函数注册外部令牌之前被取消。
        /// 此时 <c>Register</c> 同步内联触发退订,而该项尚未入表,<c>owner.Remove</c> 落空、不会回调空通知;
        /// 若仍入表,登记表永远非空 → <c>IsReclaimable</c> 恒为 false,自动回收与
        /// <c>TrimEmptyChannels</c> 共用该谓词而双双失效,只能等 <c>Clear()</c>。
        /// </para>
        /// <para>
        /// 端到端竞态无法确定性复现,故直调内部 <c>AddAsync</c> 钉住这个结构不变量。
        /// </para>
        /// </summary>
        [Test]
        public void AddAsync_TokenCancelledDuringConstruction_NotRegistered()
        {
            var channel = new MessageChannel<TestMessage>(null);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var subscription = channel.AddAsync(
                null, (msg, ct) => UniTask.CompletedTask, cts.Token);

            Assert.IsTrue(subscription.IsDisposed, "已取消的令牌应让登记项立即失效");
            Assert.AreEqual(0, channel.Async.Count, "已退订的登记项不得留在登记表中");
            Assert.IsTrue(channel.IsReclaimable,
                "登记表为空且无缓冲流,通道应仍可回收 —— 否则它将永远无法被回收");
        }

        #endregion

        #region 同步 Publish 的异步派发

        [Test]
        public void Publish_Sync_InvokesAsyncHandlerOnce()
        {
            var count = 0;
            MessageManager.SubscribeAsync<TestMessage>((msg, ct) =>
            {
                count++;
                return UniTask.CompletedTask;
            });

            MessageManager.Publish(new TestMessage { Value = 1 });

            Assert.AreEqual(1, count, "同步 Publish 以 fire-and-forget 触发异步处理器,且只触发一次");
        }

        [Test]
        public void SubscribeAsync_HandlerThrows_IsolatedAndLogged()
        {
            var healthyCount = 0;
            MessageManager.SubscribeAsync<TestMessage>((msg, ct) =>
            {
                healthyCount++;
                return UniTask.CompletedTask;
            });
            MessageManager.SubscribeAsync<TestMessage>((msg, ct) =>
                throw new InvalidOperationException("async boom"));

            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("[Message] Async handler threw exception")));
            Assert.DoesNotThrow(() => MessageManager.Publish(new TestMessage { Value = 1 }),
                "异步处理器同步抛出不得打崩发布方");

            Assert.AreEqual(1, healthyCount, "异常处理器不影响其他异步订阅");
        }

        [Test]
        public void SubscribeAsync_FilterThrows_OnlyThatHandlerSkipped()
        {
            var called = false;
            MessageManager.SubscribeAsync<TestMessage>(
                msg => throw new InvalidOperationException("filter boom"),
                (msg, ct) => { called = true; return UniTask.CompletedTask; });

            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("[Message] Async subscription filter threw exception")));
            Assert.DoesNotThrow(() => MessageManager.Publish(new TestMessage { Value = 1 }));

            Assert.IsFalse(called, "过滤条件抛异常的订阅本条不投递");
        }

        #endregion

        #region 键值与过滤

        [Test]
        public void SubscribeAsync_Keyed_OnlyMatchingKeyInvoked()
        {
            var matched = new List<int>();
            MessageManager.SubscribeAsync<string, TestMessage>(
                "Score", (msg, ct) => { matched.Add(msg.Value); return UniTask.CompletedTask; });

            MessageManager.Publish("Score", new TestMessage { Value = 1 });
            MessageManager.Publish("Health", new TestMessage { Value = 2 });

            CollectionAssert.AreEqual(new[] { 1 }, matched, "键值异步订阅只收到匹配 Key 的消息");
        }

        [Test]
        public void SubscribeAsync_KeyedWithFilter_OnlyMatching()
        {
            var matched = new List<int>();
            MessageManager.SubscribeAsync<string, TestMessage>(
                "Score",
                msg => msg.Value > 10,
                (msg, ct) => { matched.Add(msg.Value); return UniTask.CompletedTask; });

            MessageManager.Publish("Score", new TestMessage { Value = 5 });
            MessageManager.Publish("Score", new TestMessage { Value = 15 });

            CollectionAssert.AreEqual(new[] { 15 }, matched, "键值异步订阅的过滤条件生效");
        }

        [Test]
        public void SubscribeBuffered_WithKeyAndFilter_ReplayAndLiveBothFiltered()
        {
            MessageManager.Publish("Score", new TestMessage { Value = 5 });
            MessageManager.Publish("Score", new TestMessage { Value = 15 });

            var received = new List<int>();
            MessageManager.SubscribeBuffered<string, TestMessage>(
                "Score", msg => msg.Value > 10, msg => received.Add(msg.Value));

            CollectionAssert.AreEqual(new[] { 15 }, received, "重放同样走过滤:5 被拦、15 通过");

            MessageManager.Publish("Score", new TestMessage { Value = 25 });
            MessageManager.Publish("Score", new TestMessage { Value = 1 });

            CollectionAssert.AreEqual(new[] { 15, 25 }, received, "实时同样走过滤:25 通过、1 被拦");
        }

        #endregion
    }
}
