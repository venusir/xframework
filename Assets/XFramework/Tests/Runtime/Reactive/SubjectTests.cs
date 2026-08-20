using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XReactive.Internal;

namespace XFramework.XReactive.Tests
{
    /// <summary>
    /// 自研响应式引擎测试(移除 R3 依赖计划 Phase 1)。
    /// <para>覆盖契约来源:R3BehaviorProbeTests 实测的 R3 行为(异常隔离、重入、派发中退订、completed 语义)+ 引擎特有设计(池复用、filter/preHandler 槽)。</para>
    /// </summary>
    [TestFixture]
    public class SubjectTests
    {
        #region 基本投递与退订

        [Test]
        public void Subscribe_ReceivesOnNext()
        {
            var subject = new Subject<int>();
            var calls = new List<int>();
            subject.Subscribe(calls.Add);

            subject.OnNext(1);
            subject.OnNext(2);

            CollectionAssert.AreEqual(new[] { 1, 2 }, calls);
        }

        [Test]
        public void Unsubscribe_StopsDelivery()
        {
            var subject = new Subject<int>();
            var calls = new List<int>();
            var handle = subject.Subscribe(calls.Add);

            subject.OnNext(1);
            handle.Dispose();
            subject.OnNext(2);

            CollectionAssert.AreEqual(new[] { 1 }, calls, "退订后不再收到投递");
        }

        [Test]
        public void Subscribe_NullOnNext_Throws()
        {
            var subject = new Subject<int>();
            Assert.Throws<ArgumentNullException>(() => subject.Subscribe(null));
        }

        [Test]
        public void OnNext_NoSubscribers_DoesNotThrow()
        {
            var subject = new Subject<int>();
            Assert.DoesNotThrow(() => subject.OnNext(1));
        }

        #endregion

        #region filter / preHandler 槽

        [Test]
        public void Filter_BlocksMatching_DeliversOthers()
        {
            var subject = new Subject<int>();
            var calls = new List<int>();
            subject.Subscribe(calls.Add, filter: x => x > 0);

            subject.OnNext(-1);
            subject.OnNext(5);

            CollectionAssert.AreEqual(new[] { 5 }, calls, "filter 返回 false 的消息不投递");
        }

        [Test]
        public void PreHandler_RunsBeforeOnNext()
        {
            var subject = new Subject<int>();
            var order = new List<string>();
            subject.Subscribe(
                _ => order.Add("onNext"),
                preHandler: _ => order.Add("preHandler"));

            subject.OnNext(1);

            CollectionAssert.AreEqual(new[] { "preHandler", "onNext" }, order);
        }

        [Test]
        public void PreHandler_Filtered_NotInvoked()
        {
            var subject = new Subject<int>();
            var preCalls = 0;
            subject.Subscribe(_ => { }, preHandler: _ => preCalls++, filter: x => x > 0);

            subject.OnNext(-1);

            Assert.AreEqual(0, preCalls, "filter 拦截时 preHandler 不应执行");
        }

        #endregion

        #region 派发中退订与重入(探针 7 系列)

        [Test]
        public void DisposeOwnHandle_DuringDispatch_DoesNotBreak()
        {
            var subject = new Subject<int>();
            var calls = new List<int>();
            IDisposable handle = null;
            handle = subject.Subscribe(x =>
            {
                calls.Add(x);
                handle.Dispose();
            });

            Assert.DoesNotThrow(() =>
            {
                subject.OnNext(1);
                subject.OnNext(2);
            });
            CollectionAssert.AreEqual(new[] { 1 }, calls, "派发中自退订后,后续消息不再投递");
        }

        [Test]
        public void DisposeAnother_DuringDispatch_OtherSubscribersStillReceive()
        {
            var subject = new Subject<int>();
            var other = new List<int>();
            IDisposable handle = null;
            handle = subject.Subscribe(_ => handle.Dispose());
            subject.Subscribe(other.Add);

            Assert.DoesNotThrow(() => subject.OnNext(1));
            CollectionAssert.AreEqual(new[] { 1 }, other, "一个订阅者退订不影响同轮投递中的其他订阅者");
        }

        [Test]
        public void ReentrantOnNext_DoesNotBreak()
        {
            var subject = new Subject<int>();
            var calls = new List<int>();
            subject.Subscribe(x =>
            {
                calls.Add(x);
                if (x == 1) subject.OnNext(2);
            });

            Assert.DoesNotThrow(() => subject.OnNext(1));
            CollectionAssert.AreEqual(new[] { 1, 2 }, calls, "重入 OnNext 递归投递");
        }

        [Test]
        public void SubscribeUnsubscribe_ManyCycles_NodePoolReused()
        {
            var subject = new Subject<int>();

            // 大量订阅/退订周期:验证节点池复用不泄漏、不崩溃
            for (int i = 0; i < 1000; i++)
            {
                var handle = subject.Subscribe(_ => { });
                handle.Dispose();
            }

            var calls = new List<int>();
            subject.Subscribe(calls.Add);
            subject.OnNext(42);
            CollectionAssert.AreEqual(new[] { 42 }, calls, "池复用后投递正常");
        }

        #endregion

        #region completed 语义(探针 5 系列)

        [Test]
        public void OnCompleted_IgnoresSubsequentOnNext()
        {
            var subject = new Subject<int>();
            var calls = new List<int>();
            subject.Subscribe(calls.Add);

            subject.OnNext(1);
            subject.OnCompleted();
            subject.OnNext(2);

            CollectionAssert.AreEqual(new[] { 1 }, calls, "OnCompleted 后 OnNext 被忽略");
        }

        [Test]
        public void Subscribe_AfterCompleted_NotDelivered()
        {
            var subject = new Subject<int>();
            subject.OnCompleted();

            var calls = new List<int>();
            subject.Subscribe(calls.Add);
            subject.OnNext(1);

            Assert.AreEqual(0, calls.Count, "completed 后新订阅者不收到投递");
        }

        #endregion

        #region 异常语义(探针 6 系列:隔离 + 日志,不传播)

        [Test]
        public void HandlerThrows_Isolated_OtherSubscribersStillReceive()
        {
            var subject = new Subject<int>();
            var healthy = new List<int>();
            subject.Subscribe(_ => throw new InvalidOperationException("boom"));
            subject.Subscribe(healthy.Add);

            LogAssert.Expect(LogType.Error, "[Reactive] Subject handler threw exception");
            LogAssert.Expect(LogType.Error, "[Reactive] Subject handler threw exception");
            Assert.DoesNotThrow(() => subject.OnNext(1));
            Assert.DoesNotThrow(() => subject.OnNext(2));

            CollectionAssert.AreEqual(new[] { 1, 2 }, healthy, "异常订阅者不移除,其他订阅者每条消息都收到");
        }

        [Test]
        public void PreHandlerThrows_Isolated_OnNextStillRuns()
        {
            var subject = new Subject<int>();
            var calls = new List<int>();
            subject.Subscribe(calls.Add, preHandler: _ => throw new InvalidOperationException("boom"));

            LogAssert.Expect(LogType.Error, "[Reactive] Subject preHandler threw exception");
            Assert.DoesNotThrow(() => subject.OnNext(1));

            CollectionAssert.AreEqual(new[] { 1 }, calls, "preHandler 异常不影响 onNext 执行");
        }

        #endregion

        #region ReplaySubject(探针 3 系列)

        [Test]
        public void ReplaySubject_ReplaysLatest_Synchronously()
        {
            var subject = new ReplaySubject<int>();
            subject.OnNext(7);

            var calls = new List<int>();
            subject.Subscribe(calls.Add);

            CollectionAssert.AreEqual(new[] { 7 }, calls, "订阅时同步重放最近一条");
        }

        [Test]
        public void ReplaySubject_MultipleSubscribers_EachGetsReplay()
        {
            var subject = new ReplaySubject<int>();
            subject.OnNext(7);

            var c1 = new List<int>();
            var c2 = new List<int>();
            subject.Subscribe(c1.Add);
            subject.Subscribe(c2.Add);

            CollectionAssert.AreEqual(new[] { 7 }, c1, "第一个订阅者收到重放");
            CollectionAssert.AreEqual(new[] { 7 }, c2, "第二个订阅者各自收到重放");
        }

        [Test]
        public void ReplaySubject_ReplayBeforeNewMessages()
        {
            var subject = new ReplaySubject<int>();
            subject.OnNext(1);

            var calls = new List<int>();
            subject.Subscribe(calls.Add);
            subject.OnNext(2);

            CollectionAssert.AreEqual(new[] { 1, 2 }, calls, "先重放最近一条,再投递新消息");
        }

        [Test]
        public void ReplaySubject_NoMessages_StartsFromLive()
        {
            var subject = new ReplaySubject<int>();
            var calls = new List<int>();
            subject.Subscribe(calls.Add);
            subject.OnNext(3);

            CollectionAssert.AreEqual(new[] { 3 }, calls, "无缓存消息时从实时消息开始");
        }

        [Test]
        public void ReplaySubject_ReplayPassesFilter()
        {
            var subject = new ReplaySubject<int>();
            subject.OnNext(-1); // 缓存值,但被 filter 拦截
            subject.OnNext(5);

            var calls = new List<int>();
            subject.Subscribe(calls.Add, filter: x => x > 0);

            CollectionAssert.AreEqual(new[] { 5 }, calls, "重放也经过 filter(最近一条被拦截时无重放)");
        }

        [Test]
        public void ReplaySubject_Completed_NoReplay()
        {
            var subject = new ReplaySubject<int>();
            subject.OnNext(1);
            subject.OnCompleted();

            var calls = new List<int>();
            subject.Subscribe(calls.Add);
            subject.OnNext(2);

            Assert.AreEqual(0, calls.Count, "completed 后新订阅者不重放、不投递");
        }

        #endregion

        #region AnonymousDisposable

        [Test]
        public void AnonymousDisposable_DisposeOnce_IgnoresRepeat()
        {
            var count = 0;
            var disposable = AnonymousDisposable.Create(() => count++);

            disposable.Dispose();
            disposable.Dispose();

            Assert.AreEqual(1, count, "Dispose 只执行一次");
        }

        #endregion
    }
}
