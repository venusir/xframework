using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XMessage;

namespace XFramework.XMessage.Tests
{
    /// <summary>
    /// Tests for <see cref="MessageManager.PublishAsync{TMessage}"/> and its dispatch strategies.
    /// <para>
    /// 全部用例由测试侧驱动 <see cref="UniTaskCompletionSource"/> 控制处理器完成时机,
    /// 不依赖 PlayerLoop、不依赖计时,也不使用会阻塞主线程的等待写法。
    /// </para>
    /// </summary>
    [TestFixture]
    public class MessagePublishAsyncTests
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

        #region 等待语义

        [Test]
        public void PublishAsync_Parallel_AllHandlersStartBeforeAnyCompletes()
        {
            var gateA = new UniTaskCompletionSource();
            var gateB = new UniTaskCompletionSource();
            var started = new List<string>();
            var finished = new List<string>();

            MessageManager.SubscribeAsync<TestMessage>(async (msg, ct) =>
            {
                started.Add("a");
                await gateA.Task;
                finished.Add("a");
            });
            MessageManager.SubscribeAsync<TestMessage>(async (msg, ct) =>
            {
                started.Add("b");
                await gateB.Task;
                finished.Add("b");
            });

            var task = MessageManager.PublishAsync(new TestMessage { Value = 1 });

            CollectionAssert.AreEqual(new[] { "a", "b" }, started, "并行策略下全部处理器都已启动");
            Assert.AreEqual(0, finished.Count, "尚未完成的处理器不应计入完成");

            gateA.TrySetResult();
            CollectionAssert.AreEqual(new[] { "a" }, finished, "仅放行第一个处理器");
            Assert.IsFalse(task.Status.IsCompleted(), "仍有处理器在途时整体不应提前完成");

            gateB.TrySetResult();

            Assert.IsTrue(task.Status.IsCompleted(), "全部处理器完成后 PublishAsync 才完成");
            CollectionAssert.AreEqual(new[] { "a", "b" }, finished);

            task.GetAwaiter().GetResult();
        }

        [Test]
        public void PublishAsync_Sequential_SecondStartsOnlyAfterFirstCompletes()
        {
            var gate = new UniTaskCompletionSource();
            var order = new List<string>();

            MessageManager.SubscribeAsync<TestMessage>(async (msg, ct) =>
            {
                order.Add("a-start");
                await gate.Task;
                order.Add("a-end");
            });
            MessageManager.SubscribeAsync<TestMessage>((msg, ct) =>
            {
                order.Add("b-start");
                order.Add("b-end");
                return UniTask.CompletedTask;
            });

            var task = MessageManager.PublishAsync(new TestMessage { Value = 1 }, MessagePublishStrategy.Sequential);

            CollectionAssert.AreEqual(new[] { "a-start" }, order, "顺序策略下第二个处理器尚未启动");
            Assert.IsFalse(task.Status.IsCompleted());

            gate.TrySetResult();

            CollectionAssert.AreEqual(new[] { "a-start", "a-end", "b-start", "b-end" }, order,
                "前一个处理器完成后才启动下一个");
            Assert.IsTrue(task.Status.IsCompleted());
        }

        [Test]
        public void PublishAsync_SyncSubscribersDeliverBeforeAsyncHandlers()
        {
            var order = new List<string>();
            MessageManager.Subscribe<TestMessage>(msg => order.Add("sync"));
            MessageManager.SubscribeAsync<TestMessage>((msg, ct) =>
            {
                order.Add("async");
                return UniTask.CompletedTask;
            });

            MessageManager.PublishAsync(new TestMessage { Value = 1 }).GetAwaiter().GetResult();

            CollectionAssert.AreEqual(new[] { "sync", "async" }, order, "同步订阅者先于异步处理器");
        }

        [Test]
        public void PublishAsync_NoAsyncSubscribers_CompletesImmediately()
        {
            MessageManager.Subscribe<TestMessage>(msg => { });

            var task = MessageManager.PublishAsync(new TestMessage { Value = 1 });

            Assert.IsTrue(task.Status.IsCompleted(), "无异步订阅时应立即完成,await 不产生额外等待");
        }

        [Test]
        public void PublishAsync_NoSubscribers_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                MessageManager.PublishAsync(new TestMessage { Value = 1 }).GetAwaiter().GetResult());
        }

        #endregion

        #region 异常与取消

        [Test]
        public void PublishAsync_HandlerThrows_IsolatedAndLogged()
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
            Assert.DoesNotThrow(() =>
                MessageManager.PublishAsync(new TestMessage { Value = 1 }).GetAwaiter().GetResult(),
                "处理器异常不得传播给发布方");

            Assert.AreEqual(1, healthyCount, "异常处理器不影响其他异步订阅");
        }

        [Test]
        public void PublishAsync_AsyncFilterThrows_OnlyThatHandlerSkipped()
        {
            var called = false;
            MessageManager.SubscribeAsync<TestMessage>(
                msg => throw new InvalidOperationException("filter boom"),
                (msg, ct) => { called = true; return UniTask.CompletedTask; });

            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("[Message] Async subscription filter threw exception")));
            Assert.DoesNotThrow(() =>
                MessageManager.PublishAsync(new TestMessage { Value = 1 }).GetAwaiter().GetResult());

            Assert.IsFalse(called, "过滤条件抛异常的订阅本条不投递");
        }

        [Test]
        public void PublishAsync_CancelledToken_ThrowsOperationCanceled()
        {
            // 处理器保持在途,使取消作用在真正等待中的接受者上
            var gate = new UniTaskCompletionSource();
            MessageManager.SubscribeAsync<TestMessage>(async (msg, ct) => await gate.Task);

            using var cts = new CancellationTokenSource();
            var task = MessageManager.PublishAsync(
                new TestMessage { Value = 1 }, MessagePublishStrategy.Parallel, cts.Token);

            Assert.IsFalse(task.Status.IsCompleted(), "处理器在途时本次等待尚未完成");

            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult(),
                "取消本次等待应抛出 OperationCanceledException");
        }

        #endregion

        #region 键值与派发中退订

        [Test]
        public void PublishAsync_WithKey_OnlyMatchingKeyInvoked()
        {
            var matched = new List<int>();
            MessageManager.SubscribeAsync<string, TestMessage>(
                "Score", (msg, ct) => { matched.Add(msg.Value); return UniTask.CompletedTask; });

            // 键值重载要求显式传入策略(不设默认值),否则两参调用与无键重载二义
            MessageManager.PublishAsync("Score", new TestMessage { Value = 1 }, MessagePublishStrategy.Parallel)
                .GetAwaiter().GetResult();
            MessageManager.PublishAsync("Health", new TestMessage { Value = 2 }, MessagePublishStrategy.Parallel)
                .GetAwaiter().GetResult();

            CollectionAssert.AreEqual(new[] { 1 }, matched, "键值异步发布只触发匹配 Key 的处理器");
        }

        [Test]
        public void PublishAsync_MessageAndStrategy_ResolvesToUnkeyedOverload()
        {
            var calls = 0;
            MessageManager.SubscribeAsync<TestMessage>((msg, ct) =>
            {
                calls++;
                return UniTask.CompletedTask;
            });

            // 该两参写法历史上与键值重载二义(CS0121);
            // 由「键值重载的 strategy 无默认值」保证此处唯一解析到无键重载
            MessageManager.PublishAsync(new TestMessage { Value = 1 }, MessagePublishStrategy.Sequential)
                .GetAwaiter().GetResult();

            Assert.AreEqual(1, calls);
        }

        [Test]
        public void PublishAsync_SubscriptionDisposedByEarlierHandler_IsSkipped()
        {
            var laterCalled = 0;
            IDisposable later = null;

            MessageManager.SubscribeAsync<TestMessage>((msg, ct) =>
            {
                later.Dispose();
                return UniTask.CompletedTask;
            });
            later = MessageManager.SubscribeAsync<TestMessage>((msg, ct) =>
            {
                laterCalled++;
                return UniTask.CompletedTask;
            });

            Assert.DoesNotThrow(() =>
                MessageManager.PublishAsync(new TestMessage { Value = 1 }).GetAwaiter().GetResult());

            Assert.AreEqual(0, laterCalled, "同轮内已被前一个处理器退订的订阅不再收到本条消息");
        }

        #endregion
    }
}
