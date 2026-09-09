using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XMessage.Internal;

namespace XFramework.XMessage.Tests
{
    /// <summary>
    /// 自研响应式引擎测试。
    /// <para>覆盖契约:基本投递与退订、派发中退订与重入、completed 语义、异常隔离、节点池复用、BufferedEventStream 重放。</para>
    /// </summary>
    [TestFixture]
    public class EventStreamTests
    {
        #region 基本投递与退订

        [Test]
        public void Subscribe_ReceivesOnNext()
        {
            var stream = new EventStream<int>();
            var calls = new List<int>();
            stream.Subscribe(calls.Add);

            stream.OnNext(1);
            stream.OnNext(2);

            CollectionAssert.AreEqual(new[] { 1, 2 }, calls);
        }

        [Test]
        public void Unsubscribe_StopsDelivery()
        {
            var stream = new EventStream<int>();
            var calls = new List<int>();
            var handle = stream.Subscribe(calls.Add);

            stream.OnNext(1);
            handle.Dispose();
            stream.OnNext(2);

            CollectionAssert.AreEqual(new[] { 1 }, calls, "退订后不再收到投递");
        }

        [Test]
        public void Subscribe_NullOnNext_Throws()
        {
            var stream = new EventStream<int>();
            Assert.Throws<ArgumentNullException>(() => stream.Subscribe(null));
        }

        [Test]
        public void OnNext_NoSubscribers_DoesNotThrow()
        {
            var stream = new EventStream<int>();
            Assert.DoesNotThrow(() => stream.OnNext(1));
        }

        #endregion

        #region 派发中退订与重入

        [Test]
        public void DisposeOwnHandle_DuringDispatch_DoesNotBreak()
        {
            var stream = new EventStream<int>();
            var calls = new List<int>();
            IDisposable handle = null;
            handle = stream.Subscribe(x =>
            {
                calls.Add(x);
                handle.Dispose();
            });

            Assert.DoesNotThrow(() =>
            {
                stream.OnNext(1);
                stream.OnNext(2);
            });
            CollectionAssert.AreEqual(new[] { 1 }, calls, "派发中自退订后,后续消息不再投递");
        }

        [Test]
        public void DisposeAnother_DuringDispatch_OtherSubscribersStillReceive()
        {
            var stream = new EventStream<int>();
            var other = new List<int>();
            IDisposable handle = null;
            handle = stream.Subscribe(_ => handle.Dispose());
            stream.Subscribe(other.Add);

            Assert.DoesNotThrow(() => stream.OnNext(1));
            CollectionAssert.AreEqual(new[] { 1 }, other, "一个订阅者退订不影响同轮投递中的其他订阅者");
        }

        [Test]
        public void ReentrantOnNext_DoesNotBreak()
        {
            var stream = new EventStream<int>();
            var calls = new List<int>();
            stream.Subscribe(x =>
            {
                calls.Add(x);
                if (x == 1) stream.OnNext(2);
            });

            Assert.DoesNotThrow(() => stream.OnNext(1));
            CollectionAssert.AreEqual(new[] { 1, 2 }, calls, "重入 OnNext 递归投递");
        }

        [Test]
        public void SubscribeUnsubscribe_ManyCycles_NodePoolReused()
        {
            var stream = new EventStream<int>();

            // 大量订阅/退订周期:验证节点池复用不泄漏、不崩溃
            for (int i = 0; i < 1000; i++)
            {
                var handle = stream.Subscribe(_ => { });
                handle.Dispose();
            }

            var calls = new List<int>();
            stream.Subscribe(calls.Add);
            stream.OnNext(42);
            CollectionAssert.AreEqual(new[] { 42 }, calls, "池复用后投递正常");
        }

        #endregion

        #region completed 语义

        [Test]
        public void OnCompleted_IgnoresSubsequentOnNext()
        {
            var stream = new EventStream<int>();
            var calls = new List<int>();
            stream.Subscribe(calls.Add);

            stream.OnNext(1);
            stream.OnCompleted();
            stream.OnNext(2);

            CollectionAssert.AreEqual(new[] { 1 }, calls, "OnCompleted 后 OnNext 被忽略");
        }

        [Test]
        public void Subscribe_AfterCompleted_NotDelivered()
        {
            var stream = new EventStream<int>();
            stream.OnCompleted();

            var calls = new List<int>();
            stream.Subscribe(calls.Add);
            stream.OnNext(1);

            Assert.AreEqual(0, calls.Count, "completed 后新订阅者不收到投递");
        }

        #endregion

        #region 异常语义(隔离 + 日志,不传播)

        [Test]
        public void HandlerThrows_Isolated_OtherSubscribersStillReceive()
        {
            var stream = new EventStream<int>();
            var healthy = new List<int>();
            stream.Subscribe(_ => throw new InvalidOperationException("boom"));
            stream.Subscribe(healthy.Add);

            // 日志消息含异常详情后缀,Expect 字符串重载为全串精确匹配,需用正则做包含匹配
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("[Message] EventStream handler threw exception")));
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("[Message] EventStream handler threw exception")));
            Assert.DoesNotThrow(() => stream.OnNext(1));
            Assert.DoesNotThrow(() => stream.OnNext(2));

            CollectionAssert.AreEqual(new[] { 1, 2 }, healthy, "异常订阅者不移除,其他订阅者每条消息都收到");
        }

        #endregion

        #region BufferedEventStream

        [Test]
        public void BufferedEventStream_ReplaysLatest_Synchronously()
        {
            var stream = new BufferedEventStream<int>();
            stream.OnNext(7);

            var calls = new List<int>();
            stream.Subscribe(calls.Add);

            CollectionAssert.AreEqual(new[] { 7 }, calls, "订阅时同步重放最近一条");
        }

        [Test]
        public void BufferedEventStream_MultipleSubscribers_EachGetsReplay()
        {
            var stream = new BufferedEventStream<int>();
            stream.OnNext(7);

            var c1 = new List<int>();
            var c2 = new List<int>();
            stream.Subscribe(c1.Add);
            stream.Subscribe(c2.Add);

            CollectionAssert.AreEqual(new[] { 7 }, c1, "第一个订阅者收到重放");
            CollectionAssert.AreEqual(new[] { 7 }, c2, "第二个订阅者各自收到重放");
        }

        [Test]
        public void BufferedEventStream_ReplayBeforeNewMessages()
        {
            var stream = new BufferedEventStream<int>();
            stream.OnNext(1);

            var calls = new List<int>();
            stream.Subscribe(calls.Add);
            stream.OnNext(2);

            CollectionAssert.AreEqual(new[] { 1, 2 }, calls, "先重放最近一条,再投递新消息");
        }

        [Test]
        public void BufferedEventStream_NoMessages_StartsFromLive()
        {
            var stream = new BufferedEventStream<int>();
            var calls = new List<int>();
            stream.Subscribe(calls.Add);
            stream.OnNext(3);

            CollectionAssert.AreEqual(new[] { 3 }, calls, "无缓存消息时从实时消息开始");
        }

        [Test]
        public void BufferedEventStream_Completed_NoReplay()
        {
            var stream = new BufferedEventStream<int>();
            stream.OnNext(1);
            stream.OnCompleted();

            var calls = new List<int>();
            stream.Subscribe(calls.Add);
            stream.OnNext(2);

            Assert.AreEqual(0, calls.Count, "completed 后新订阅者不重放、不投递");
        }

        #endregion

        #region ActionDisposable

        [Test]
        public void ActionDisposable_DisposeOnce_IgnoresRepeat()
        {
            var count = 0;
            var disposable = ActionDisposable.Create(() => count++);

            disposable.Dispose();
            disposable.Dispose();

            Assert.AreEqual(1, count, "Dispose 只执行一次");
        }

        #endregion
    }
}
