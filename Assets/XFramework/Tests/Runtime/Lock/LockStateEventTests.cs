using System;
using System.Collections.Generic;
using NUnit.Framework;
using XFramework.XLock;

namespace XFramework.XLock.Tests
{
    /// <summary>
    /// 聚合状态事件 <see cref="LockManager.OnLockStateChanged"/> 的契约。
    /// <para><b>为什么需要它</b>：事件面是「每个 (主体,类型) 集合的边沿」，查询面
    /// （<see cref="LockManager.IsLocked"/>／<see cref="LockManager.GetLockCount"/>）却是含全局锁的
    /// **聚合**。全局锁释放时，仍被自己的锁挡住的主体照样收到 <c>OnUnlocked</c>——订阅者照事件维护
    /// 「现在能不能动」会显示错状态。本事件把聚合值真的翻转作为唯一触发条件，订阅者不必自己再查一次。</para>
    /// <para><b>订阅即回调</b>：订阅时立即同步播报「该主体当前处于锁定状态的每个 lockType」（值为 true）。
    /// 未锁定的类型不会被回调——lockType 是开放的 <c>int</c> 域，无从枚举「所有为假的类型」，
    /// 订阅者把「没收到」当作 false 即可（对齐 Reactive 的「订阅时立即同步回调」惯例）。</para>
    /// </summary>
    [TestFixture]
    public class LockStateEventTests
    {
        #region Test Doubles

        private sealed class Subject : ILockable { }

        private const int TypeA = 0;
        private const int TypeB = 1;

        private Subject _subjectA;

        /// <summary>记录 (lockType, isLocked) 的回调流水。</summary>
        private List<KeyValuePair<int, bool>> _calls;

        #endregion

        #region Setup / Teardown

        [SetUp]
        public void SetUp()
        {
            LockManager.Dispose();
            _subjectA = new Subject();
            _calls = new List<KeyValuePair<int, bool>>();
        }

        [TearDown]
        public void TearDown()
        {
            LockManager.Dispose();
        }

        private void Record(int lockType, bool isLocked) => _calls.Add(new KeyValuePair<int, bool>(lockType, isLocked));

        #endregion

        #region 订阅即回调

        [Test]
        public void Subscribe_ImmediatelyReportsCurrentlyLockedTypes()
        {
            LockManager.AddLock(_subjectA, TypeA, new object());

            using var sub = LockManager.OnLockStateChanged(_subjectA, Record);

            Assert.AreEqual(1, _calls.Count, "订阅时应立即播报当前已锁定的类型");
            Assert.AreEqual(TypeA, _calls[0].Key);
            Assert.IsTrue(_calls[0].Value);
        }

        [Test]
        public void Subscribe_NotLocked_ReportsNothing()
        {
            using var sub = LockManager.OnLockStateChanged(_subjectA, Record);

            Assert.AreEqual(0, _calls.Count, "没有锁定时无事可报——未锁定的类型无处枚举");
        }

        #endregion

        #region 聚合翻转才回调

        [Test]
        public void SubjectLock_Flips_FiresBothWays()
        {
            using var sub = LockManager.OnLockStateChanged(_subjectA, Record);

            var handle = LockManager.AddLock(_subjectA, TypeA, new object());
            Assert.AreEqual(1, _calls.Count);
            Assert.IsTrue(_calls[0].Value);

            handle.Dispose();
            Assert.AreEqual(2, _calls.Count);
            Assert.IsFalse(_calls[1].Value, "最后一把释放后聚合翻转为 false");
        }

        [Test]
        public void MoreHoldersOfSameType_DoNotFireAgain()
        {
            using var sub = LockManager.OnLockStateChanged(_subjectA, Record);

            var first = LockManager.AddLock(_subjectA, TypeA, new object());
            var second = LockManager.AddLock(_subjectA, TypeA, new object());
            Assert.AreEqual(1, _calls.Count, "聚合没变(仍有锁)——只有首次加锁算边沿");

            first.Dispose();
            Assert.AreEqual(1, _calls.Count, "还有别人持锁,聚合未翻转");

            second.Dispose();
            Assert.AreEqual(2, _calls.Count);
            Assert.IsFalse(_calls[1].Value);
        }

        /// <summary>本事件存在的理由：全局锁释放时，自己的锁还在 -> 聚合仍为锁定，不该报解锁。</summary>
        [Test]
        public void GlobalLockReleased_WhileSubjectStillLocked_DoesNotFire()
        {
            int fired = 0;
            using var sub = LockManager.OnLockStateChanged(_subjectA, (_, _) => fired++);

            using var own = LockManager.AddLock(_subjectA, TypeA, new object());
            var global = LockManager.AddLock(LockManager.Global, TypeA, new object());
            Assert.AreEqual(1, fired, "自己那把加锁时播报一次；全局锁不改聚合,不播报");

            global.Dispose();

            Assert.IsTrue(LockManager.IsLocked(_subjectA, TypeA), "前置：自己的锁还在");
            Assert.AreEqual(1, fired, "聚合仍为锁定,不该报解锁——原始 OnUnlocked 在这里是会触发的");
        }

        [Test]
        public void GlobalLock_FlippingSubjectAggregate_Fires()
        {
            using var sub = LockManager.OnLockStateChanged(_subjectA, Record);

            var global = LockManager.AddLock(LockManager.Global, TypeB, new object());
            Assert.AreEqual(1, _calls.Count, "全局锁把该主体的聚合翻成 true");
            Assert.AreEqual(TypeB, _calls[0].Key);
            Assert.IsTrue(_calls[0].Value);

            global.Dispose();
            Assert.AreEqual(2, _calls.Count);
            Assert.IsFalse(_calls[1].Value);
        }

        [Test]
        public void OtherSubjectLock_DoesNotFire()
        {
            var other = new Subject();
            using var sub = LockManager.OnLockStateChanged(_subjectA, Record);

            using var handle = LockManager.AddLock(other, TypeA, new object());

            Assert.AreEqual(0, _calls.Count, "别的主体的锁与本主体的聚合无关");
        }

        #endregion

        #region 退订与参数

        [Test]
        public void Dispose_StopsNotifications_AndIsIdempotent()
        {
            var sub = LockManager.OnLockStateChanged(_subjectA, Record);
            sub.Dispose();

            LockManager.AddLock(_subjectA, TypeA, new object());
            Assert.AreEqual(0, _calls.Count, "退订后不再回调");

            Assert.DoesNotThrow(() => sub.Dispose(), "重复释放应幂等");
        }

        [Test]
        public void NullSubject_BehavesAsGlobal()
        {
            using var sub = LockManager.OnLockStateChanged(null, Record);

            using var handle = LockManager.AddLock(LockManager.Global, TypeA, new object());

            Assert.AreEqual(1, _calls.Count, "null 主体与 Global 一致");
            Assert.IsTrue(_calls[0].Value);
        }

        [Test]
        public void NullHandler_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => LockManager.OnLockStateChanged(_subjectA, null));
        }

        #endregion

        #region 订阅期契约

        /// <summary>
        /// 订阅时立即播报会跑用户回调——它抛异常时不得把刚挂上的订阅留在表里。
        /// <para>改前：异常逃出 <c>OnLockStateChanged</c>，而两条内部订阅已经挂上、句柄却没返回给调用方
        /// ——表里永久残留该主体键与委托，主体被强引用住（非 MonoBehaviour 主体即永久泄漏）。</para>
        /// </summary>
        [Test]
        public void ThrowingHandlerDuringInitialPlayback_ThrowsButLeavesNoSubscription()
        {
            LockManager.AddLock(_subjectA, TypeA, new object());   // 让订阅时的播报有内容可播

            Assert.Throws<InvalidOperationException>(() =>
                LockManager.OnLockStateChanged(_subjectA, (_, _) => throw new InvalidOperationException("boom")));

            // 若订阅残留，它会在下面这次解锁边沿上被派发并再次抛出，DispatchSafely 会记一条
            // [Lock] Error —— 未预期的 Error 会让本用例失败。故这里等价于「没有残留订阅」。
            Assert.DoesNotThrow(() => LockManager.RemoveAllLocks(_subjectA));
        }

        #endregion
    }
}
