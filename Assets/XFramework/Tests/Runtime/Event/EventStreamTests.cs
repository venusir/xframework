using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XEvent;

namespace XFramework.XEvent.Tests
{
    /// <summary>
    /// 事件流引擎测试。
    /// <para>覆盖契约:基本投递与退订、派发中退订与重入、completed 语义、异常隔离、节点池复用、
    /// 缓冲重放全套,以及公开面新增的三条(onEmpty、订阅数、缓存值)。</para>
    /// </summary>
    [TestFixture]
    public class EventStreamTests
    {
        #region 基本投递与退订

        [Test]
        public void Subscribe_ReceivesEmit()
        {
            var stream = EventStream.Create<int>();
            var calls = new List<int>();
            stream.Subscribe(calls.Add);

            stream.Emit(1);
            stream.Emit(2);

            CollectionAssert.AreEqual(new[] { 1, 2 }, calls);
        }

        [Test]
        public void Unsubscribe_StopsDelivery()
        {
            var stream = EventStream.Create<int>();
            var calls = new List<int>();
            var handle = stream.Subscribe(calls.Add);

            stream.Emit(1);
            handle.Dispose();
            stream.Emit(2);

            CollectionAssert.AreEqual(new[] { 1 }, calls, "退订后不再收到投递");
        }

        [Test]
        public void Subscribe_NullHandler_Throws()
        {
            var stream = EventStream.Create<int>();
            Assert.Throws<ArgumentNullException>(() => stream.Subscribe(null));
        }

        [Test]
        public void Emit_NoSubscribers_DoesNotThrow()
        {
            var stream = EventStream.Create<int>();
            Assert.DoesNotThrow(() => stream.Emit(1));
        }

        #endregion

        #region 派发中退订与重入

        [Test]
        public void DisposeOwnHandle_DuringDispatch_DoesNotBreak()
        {
            var stream = EventStream.Create<int>();
            var calls = new List<int>();
            IDisposable handle = null;
            handle = stream.Subscribe(x =>
            {
                calls.Add(x);
                handle.Dispose();
            });

            Assert.DoesNotThrow(() =>
            {
                stream.Emit(1);
                stream.Emit(2);
            });
            CollectionAssert.AreEqual(new[] { 1 }, calls, "派发中自退订后,后续消息不再投递");
        }

        [Test]
        public void DisposeAnother_DuringDispatch_OtherSubscribersStillReceive()
        {
            var stream = EventStream.Create<int>();
            var other = new List<int>();
            IDisposable handle = null;
            handle = stream.Subscribe(_ => handle.Dispose());
            stream.Subscribe(other.Add);

            Assert.DoesNotThrow(() => stream.Emit(1));
            CollectionAssert.AreEqual(new[] { 1 }, other, "一个订阅者退订不影响同轮投递中的其他订阅者");
        }

        [Test]
        public void ReentrantEmit_DoesNotBreak()
        {
            var stream = EventStream.Create<int>();
            var calls = new List<int>();
            stream.Subscribe(x =>
            {
                calls.Add(x);
                if (x == 1) stream.Emit(2);
            });

            Assert.DoesNotThrow(() => stream.Emit(1));
            CollectionAssert.AreEqual(new[] { 1, 2 }, calls, "重入 Emit 递归投递");
        }

        [Test]
        public void SubscribeUnsubscribe_ManyCycles_NodePoolReused()
        {
            var stream = EventStream.Create<int>();

            // 大量订阅/退订周期:验证节点池复用不泄漏、不崩溃
            for (int i = 0; i < 1000; i++)
            {
                var handle = stream.Subscribe(_ => { });
                handle.Dispose();
            }

            var calls = new List<int>();
            stream.Subscribe(calls.Add);
            stream.Emit(42);
            CollectionAssert.AreEqual(new[] { 42 }, calls, "池复用后投递正常");
        }

        #endregion

        #region completed 语义

        [Test]
        public void Complete_IgnoresSubsequentEmit()
        {
            var stream = EventStream.Create<int>();
            var calls = new List<int>();
            stream.Subscribe(calls.Add);

            stream.Emit(1);
            stream.Complete();
            stream.Emit(2);

            CollectionAssert.AreEqual(new[] { 1 }, calls, "Complete 后 Emit 被忽略");
        }

        [Test]
        public void Subscribe_AfterCompleted_NotDelivered()
        {
            var stream = EventStream.Create<int>();
            stream.Complete();

            var calls = new List<int>();
            stream.Subscribe(calls.Add);
            stream.Emit(1);

            Assert.AreEqual(0, calls.Count, "completed 后新订阅者不收到投递");
        }

        [Test]
        public void EmptyHandle_FromCompletedStream_IsSharedInstanceAndDisposable()
        {
            var stream = EventStream.Create<int>();
            stream.Complete();

            // 空句柄在本模块内只有一处来源(引擎与缓冲流共用同一实例)。
            // 每次 new 一个也「能用」,但共享实例上「不得改为池化」这条不变量
            // (见 ActionDisposable.Empty)会失去测试锁定。
            var first = stream.Subscribe(_ => { });
            var second = stream.Subscribe(_ => { });

            Assert.AreSame(first, second, "completed 流返回的空句柄应是共享单例,而非每次新建");
            Assert.DoesNotThrow(() =>
            {
                first.Dispose();
                second.Dispose();
            }, "共享句柄重复释放是 no-op");

            var calls = new List<int>();
            stream.Subscribe(calls.Add);
            stream.Emit(1);
            Assert.AreEqual(0, calls.Count, "共享空句柄不改变「completed 后不投递」的语义");
        }

        #endregion

        #region onEmpty / 订阅数(公开面契约)

        [Test]
        public void Create_OnEmpty_InvokedWhenLastSubscriberLeaves()
        {
            var emptyCount = 0;
            var stream = EventStream.Create<int>(() => emptyCount++);

            var first = stream.Subscribe(_ => { });
            var second = stream.Subscribe(_ => { });

            first.Dispose();
            Assert.AreEqual(0, emptyCount, "仍有订阅者时不得回调");

            second.Dispose();
            Assert.AreEqual(1, emptyCount, "订阅数由 1 归零时回调一次");
        }

        [Test]
        public void Dispose_DoesNotInvokeOnEmpty()
        {
            var emptyCount = 0;
            var stream = EventStream.Create<int>(() => emptyCount++);
            stream.Subscribe(_ => { });

            stream.Dispose();

            Assert.AreEqual(0, emptyCount, "持有者主动终止流时不回调——它已在自行回收结构");
        }

        [Test]
        public void SubscriptionCount_TracksAliveSubscriptions()
        {
            var stream = EventStream.Create<int>();
            Assert.AreEqual(0, stream.SubscriptionCount);

            var a = stream.Subscribe(_ => { });
            stream.Subscribe(_ => { });
            Assert.AreEqual(2, stream.SubscriptionCount, "订阅递增");

            a.Dispose();
            Assert.AreEqual(1, stream.SubscriptionCount, "退订递减");

            a.Dispose();
            Assert.AreEqual(1, stream.SubscriptionCount, "重复退订幂等,不再递减");

            stream.Dispose();
            Assert.AreEqual(0, stream.SubscriptionCount, "释放清零");
        }

        #endregion

        #region 异常语义(隔离 + 日志,不传播)

        [Test]
        public void HandlerThrows_Isolated_OtherSubscribersStillReceive()
        {
            var stream = EventStream.Create<int>();
            var healthy = new List<int>();
            stream.Subscribe(_ => throw new InvalidOperationException("boom"));
            stream.Subscribe(healthy.Add);

            // 日志消息含异常详情后缀,Expect 字符串重载为全串精确匹配,需用正则做包含匹配
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("[Event] EventStream handler threw exception")));
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("[Event] EventStream handler threw exception")));
            Assert.DoesNotThrow(() => stream.Emit(1));
            Assert.DoesNotThrow(() => stream.Emit(2));

            CollectionAssert.AreEqual(new[] { 1, 2 }, healthy, "异常订阅者不移除,其他订阅者每条消息都收到");
        }

        #endregion

        #region BufferedEventStream

        [Test]
        public void BufferedEventStream_ReplaysLatest_Synchronously()
        {
            var stream = EventStream.CreateBuffered<int>();
            stream.Emit(7);

            var calls = new List<int>();
            stream.Subscribe(calls.Add);

            CollectionAssert.AreEqual(new[] { 7 }, calls, "订阅时同步重放最近一条");
        }

        [Test]
        public void BufferedEventStream_MultipleSubscribers_EachGetsReplay()
        {
            var stream = EventStream.CreateBuffered<int>();
            stream.Emit(7);

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
            var stream = EventStream.CreateBuffered<int>();
            stream.Emit(1);

            var calls = new List<int>();
            stream.Subscribe(calls.Add);
            stream.Emit(2);

            CollectionAssert.AreEqual(new[] { 1, 2 }, calls, "先重放最近一条,再投递新消息");
        }

        [Test]
        public void BufferedEventStream_NoMessages_StartsFromLive()
        {
            var stream = EventStream.CreateBuffered<int>();
            var calls = new List<int>();
            stream.Subscribe(calls.Add);
            stream.Emit(3);

            CollectionAssert.AreEqual(new[] { 3 }, calls, "无缓存消息时从实时消息开始");
        }

        [Test]
        public void BufferedEventStream_HasCachedValue_TracksCacheState()
        {
            var stream = EventStream.CreateBuffered<int>();
            Assert.IsFalse(stream.HasCachedValue, "从未投递过时没有可重放的值");

            stream.Emit(1);
            Assert.IsTrue(stream.HasCachedValue);

            ((IDisposable)stream).Dispose();
            Assert.IsFalse(stream.HasCachedValue, "释放后缓存被清空");
        }

        [Test]
        public void BufferedEventStream_Completed_NoReplay()
        {
            var stream = EventStream.CreateBuffered<int>();
            stream.Emit(1);
            stream.Complete();

            var calls = new List<int>();
            stream.Subscribe(calls.Add);
            stream.Emit(2);

            Assert.AreEqual(0, calls.Count, "completed 后新订阅者不重放、不投递");
        }

        [Test]
        public void BufferedEventStream_EmitAfterCompleted_DoesNotReplayToNewSubscriber()
        {
            var stream = EventStream.CreateBuffered<int>();
            stream.Complete();
            stream.Emit(5);

            var calls = new List<int>();
            stream.Subscribe(calls.Add);

            Assert.AreEqual(0, calls.Count, "completed 之后的 Emit 必须被彻底忽略,不得写回缓存供新订阅者重放");
        }

        [Test]
        public void BufferedEventStream_DisposedViaIDisposable_NoReplayToNewSubscriber()
        {
            var stream = EventStream.CreateBuffered<int>();
            stream.Emit(7);

            // 经接口引用释放:必须走到派生类的 Dispose,否则缓存不被清空
            ((IDisposable)stream).Dispose();

            var calls = new List<int>();
            stream.Subscribe(calls.Add);

            Assert.AreEqual(0, calls.Count, "经 IDisposable 释放后,新订阅者不收到陈旧缓存的重放");
        }

        [Test]
        public void BufferedEventStream_ThroughInterfaceReference_StillCaches()
        {
            // 经接口引用多态调用:实现必须隐式实现 IEventStream<T>(而非显式实现),
            // 否则经接口引用的 Emit 会落到基类实现,缓存永远不写、重放静默失效——
            // 本用例是这条陷阱的唯一锁,不要把它退化成直接调具体类型。
            IEventStream<int> stream = EventStream.CreateBuffered<int>();
            stream.Emit(8);

            var calls = new List<int>();
            stream.Subscribe(calls.Add);

            CollectionAssert.AreEqual(new[] { 8 }, calls, "经接口引用投递的事件同样写入缓存并可重放");
        }

        [Test]
        public void BufferedEventStream_CompleteViaInterfaceReference_ClearsCache()
        {
            IEventStream<int> stream = EventStream.CreateBuffered<int>();
            stream.Emit(1);
            stream.Complete();

            var calls = new List<int>();
            stream.Subscribe(calls.Add);
            stream.Emit(2);

            Assert.AreEqual(0, calls.Count, "经接口引用 Complete 后缓存被清空,新订阅者不重放");
        }

        #endregion

        #region ActionDisposable

        [Test]
        public void ActionDisposable_DisposeOnce_IgnoresRepeat()
        {
            var count = 0;
            var disposable = XFramework.XEvent.Internal.ActionDisposable.Create(() => count++);

            disposable.Dispose();
            disposable.Dispose();

            Assert.AreEqual(1, count, "Dispose 只执行一次");
        }

        #endregion
    }
}
