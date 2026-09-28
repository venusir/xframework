using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XLock;

namespace XFramework.XLock.Tests
{
    /// <summary>
    /// 派发健壮性:全局锁派发期间的订阅增删、订阅者抛异常、嵌套派发。
    /// <para><b>为什么这是缺陷而非口味</b>:订阅表是 <c>Dictionary</c>,派发时 <c>foreach</c> 它——
    /// 订阅者在回调里退订会移除键、订阅新主体会插入键,两者都改 <c>_version</c>,枚举器下一次
    /// <c>MoveNext</c> 即抛 <c>InvalidOperationException</c>。「收到一次就退订」这种一次性订阅写法、
    /// 以及「解锁时顺手清理订阅」的 UI,都是极自然的触发路径。</para>
    /// <para><b>另一半是异常隔离</b>:<c>AddLock</c> 先入表再派发,订阅者抛出的异常若从派发里逃出去,
    /// 调用方就拿不到那个句柄——锁留在表里成了没人能释放的永久锁(<c>GetLockCount</c> 实测为 1)。
    /// 仓内先例:XEvent 引擎与 Pipeline 都记 <c>[模块]</c> LogError 后继续。</para>
    /// <para><b>粒度取舍</b>:全局分支逐订阅者隔离(它本来就在逐项遍历);主体分支与两个公开事件
    /// 是整条多播一次 try/catch——与 <c>Pipeline.DispatchSafely</c> 的取舍逐字相同(不用
    /// <c>GetInvocationList</c> 逐个隔离,那要给每次派发分配一个委托数组)。代价是抛异常者会饿死
    /// 同一条多播里排在它后面的订阅者,这一点写进模块 README 的「已知限制」。</para>
    /// </summary>
    [TestFixture]
    public class LockDispatchRobustnessTests
    {
        #region Test Doubles

        private sealed class Subject : ILockable { }

        private const int TypeA = 0;
        private const int TypeB = 1;

        private Subject _subjectA;
        private Subject _subjectB;

        #endregion

        #region Setup / Teardown

        [SetUp]
        public void SetUp()
        {
            LockManager.Dispose();
            _subjectA = new Subject();
            _subjectB = new Subject();
        }

        [TearDown]
        public void TearDown()
        {
            LockManager.Dispose();
        }

        #endregion

        #region 全局派发期间的订阅增删

        /// <summary>一次性订阅:收到即退订自己。改前抛 Collection was modified。</summary>
        [Test]
        public void GlobalDispatch_SelfUnsubscribeInsideHandler_DoesNotThrow()
        {
            IDisposable sub = null;
            sub = LockManager.OnLocked(_subjectA, _ => sub.Dispose());

            Assert.DoesNotThrow(() => LockManager.AddLock(LockManager.Global, TypeA, new object()));
        }

        /// <summary>回调里订阅一个全新主体——插入新键,同样是结构性改动。</summary>
        [Test]
        public void GlobalDispatch_SubscribeNewSubjectInsideHandler_DoesNotThrow()
        {
            LockManager.OnLocked(_subjectA, _ => LockManager.OnLocked(_subjectB, __ => { }));

            Assert.DoesNotThrow(() => LockManager.AddLock(LockManager.Global, TypeA, new object()));
        }

        /// <summary>回调里退订另一个主体的最后一个句柄——移除键。</summary>
        [Test]
        public void GlobalDispatch_UnsubscribeOtherSubjectInsideHandler_DoesNotThrow()
        {
            IDisposable other = LockManager.OnLocked(_subjectB, _ => { });
            LockManager.OnLocked(_subjectA, _ => other.Dispose());

            Assert.DoesNotThrow(() => LockManager.AddLock(LockManager.Global, TypeA, new object()));
        }

        /// <summary>对照项:主体分支本来就不枚举订阅表(取到的是多播委托的本地副本),改前也是绿的。</summary>
        [Test]
        public void SubjectDispatch_SelfUnsubscribeInsideHandler_DoesNotThrow()
        {
            IDisposable sub = null;
            sub = LockManager.OnLocked(_subjectA, _ => sub.Dispose());

            Assert.DoesNotThrow(() => LockManager.AddLock(_subjectA, TypeA, new object()));
        }

        /// <summary>快照语义:本轮被移除的订阅者仍会收到本轮通知,下一轮不再。</summary>
        [Test]
        public void GlobalDispatch_UnsubscribedDuringRound_StillNotifiedOnceThisRound()
        {
            int bCount = 0;
            IDisposable bSub = LockManager.OnLocked(_subjectB, _ => bCount++);
            LockManager.OnLocked(_subjectA, _ => bSub.Dispose());

            LockManager.AddLock(LockManager.Global, TypeA, new object());
            Assert.AreEqual(1, bCount, "本轮快照在派发前已取，B 仍应收到这一次");

            LockManager.AddLock(LockManager.Global, TypeB, new object());
            Assert.AreEqual(1, bCount, "下一轮不该再叫它");
        }

        /// <summary>嵌套派发:回调里再加一把全局锁,内层不得冲掉外层的派发。
        /// <para>本项在修复前也是绿的(<c>AddLock</c> 不改订阅表,故不触发枚举器失效)——它守的是
        /// <b>新实现</b>:快照缓冲若只用单一字段,内层派发会把外层的缓冲冲掉。</para></summary>
        [Test]
        public void NestedDispatch_AddGlobalLockInsideHandler_AllSubscribersNotifiedForBoth()
        {
            int aCount = 0;
            int bCount = 0;
            LockManager.OnLocked(_subjectA, _ =>
            {
                aCount++;
                if (aCount == 1)
                    LockManager.AddLock(LockManager.Global, TypeB, new object());
            });
            LockManager.OnLocked(_subjectB, _ => bCount++);

            Assert.DoesNotThrow(() => LockManager.AddLock(LockManager.Global, TypeA, new object()));
            Assert.AreEqual(2, aCount, "A 应收到两把全局锁(外层的 TypeA + 嵌套的 TypeB)");
            Assert.AreEqual(2, bCount, "B 同样收到两把——嵌套不得吞掉外层快照里的订阅者");
        }

        #endregion

        #region 订阅者异常隔离

        /// <summary>订阅者抛异常不得从加锁路径逃逸,且锁必须仍被那个句柄持有。</summary>
        [Test]
        public void GlobalDispatch_ThrowingSubscriber_DoesNotEscapeAcquireNorLeakHandle()
        {
            int laterCount = 0;
            LockManager.OnLocked(_subjectA, _ => throw new InvalidOperationException("subscriber boom"));
            LockManager.OnLocked(_subjectB, _ => laterCount++);

            LogAssert.Expect(LogType.Error, new Regex(@"\[Lock\] OnLocked subscriber threw"));
            var handle = default(LockHandle);
            Assert.DoesNotThrow(() => handle = LockManager.AddLock(LockManager.Global, TypeA, new object()));

            Assert.IsTrue(handle.IsHeld, "锁已入表,句柄必须仍持有它(否则就是没人能释放的永久锁)");
            Assert.AreEqual(1, LockManager.GetLockCount(LockManager.Global, TypeA));
            Assert.AreEqual(1, laterCount, "全局分支逐订阅者隔离:排在抛异常者之后的订阅者仍应收到");
        }

        /// <summary>对照项:主体分支是整条多播一次隔离——不逃逸、状态一致,但同链后续订阅者会被饿死。</summary>
        [Test]
        public void SubjectDispatch_ThrowingSubscriber_DoesNotEscapeAcquire()
        {
            LockManager.OnLocked(_subjectA, _ => throw new InvalidOperationException("subscriber boom"));

            LogAssert.Expect(LogType.Error, new Regex(@"\[Lock\] OnLocked subscriber threw"));
            var handle = default(LockHandle);
            Assert.DoesNotThrow(() => handle = LockManager.AddLock(_subjectA, TypeA, new object()));

            Assert.IsTrue(handle.IsHeld);
            Assert.AreEqual(1, LockManager.GetLockCount(_subjectA, TypeA));
        }

        /// <summary>解锁路径同型:状态先一致,再谈隔离。</summary>
        [Test]
        public void UnlockDispatch_ThrowingSubscriber_DoesNotEscapeRelease()
        {
            var handle = LockManager.AddLock(_subjectA, TypeA, new object());
            LockManager.OnUnlocked(_subjectA, _ => throw new InvalidOperationException("subscriber boom"));

            LogAssert.Expect(LogType.Error, new Regex(@"\[Lock\] OnUnlocked subscriber threw"));
            Assert.DoesNotThrow(() => handle.Dispose());

            Assert.IsFalse(handle.IsHeld);
            Assert.AreEqual(0, LockManager.GetLockCount(_subjectA, TypeA), "状态必须先落地,异常只影响通知");
        }

        /// <summary>两个公开事件(bus)的订阅者抛异常同样不得逃逸,也不得阻断订阅派发。</summary>
        [Test]
        public void GlobalBusEvents_ThrowingSubscriber_DoesNotEscapeNorBlockSubscriptionDispatch()
        {
            int subjectNotified = 0;
            LockManager.OnGlobalLocked += (_, _, _) => throw new InvalidOperationException("bus boom");
            LockManager.OnLocked(_subjectA, _ => subjectNotified++);

            LogAssert.Expect(LogType.Error, new Regex(@"\[Lock\] OnGlobalLocked subscriber threw"));
            Assert.DoesNotThrow(() => LockManager.AddLock(LockManager.Global, TypeA, new object()));

            Assert.AreEqual(1, subjectNotified, "总线订阅者抛异常不得阻断紧接着的订阅派发");
        }

        /// <summary>释放路径上的公开事件同型。</summary>
        [Test]
        public void GlobalBusEvents_ThrowingSubscriber_DoesNotEscapeRelease()
        {
            var handle = LockManager.AddLock(_subjectA, TypeA, new object());
            LockManager.OnGlobalUnlocked += (_, _, _) => throw new InvalidOperationException("bus boom");

            LogAssert.Expect(LogType.Error, new Regex(@"\[Lock\] OnGlobalUnlocked subscriber threw"));
            Assert.DoesNotThrow(() => handle.Dispose());

            Assert.AreEqual(0, LockManager.GetLockCount(_subjectA, TypeA));
        }

        #endregion
    }
}
