using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using XFramework.XLock;
using XFramework.XMessage;

namespace XFramework.XLock.Tests
{
    /// <summary>
    /// 诊断面：<see cref="LockManager.GetSnapshot"/> / <see cref="LockManager.DumpState"/> /
    /// <see cref="LockManager.CopyLockedTypes"/>。
    /// <para><b>为什么需要</b>：Lock 是仓内唯一没有诊断面的成熟模块（Message 有 <c>GetStats</c>、UI 有
    /// <c>UIStateSnapshot</c>/<c>DumpState</c>、Reactive 有 <c>SubscriptionCount</c>），而「玩家一直不能动」
    /// 正是这类系统最经典的事故——此前唯一的公开手段是 <c>GetLockObjects(subject, type)</c>，要求你先知道
    /// **主体与类型**，且拿到的是持有者对象本身（推荐写法下就是一堆 <c>System.Object</c>）。</para>
    /// <para>本 fixture 锁的是**口径**（`GetSnapshot` 的每个字段自述可行动判据，口径写错了比没有更糟）。</para>
    /// </summary>
    [TestFixture]
    public class LockDiagnosticsTests
    {
        #region Test Doubles

        private sealed class Subject : ILockable { }

        private sealed class TokenSubject : ILockable, IDestroyCancellationToken
        {
            private readonly CancellationTokenSource _cts = new CancellationTokenSource();

            public CancellationToken DestroyCancellationToken => _cts.Token;

            public void DisposeCts() => _cts.Dispose();
        }

        /// <summary>ToString 会抛的持有者——验证诊断不碰用户代码。</summary>
        private sealed class HostileHolder
        {
            public override string ToString() => throw new InvalidOperationException("别调我");
        }

        private const int TypeA = 0;
        private const int TypeB = 1;

        private Subject _a;
        private Subject _b;

        #endregion

        #region Setup / Teardown

        [SetUp]
        public void SetUp()
        {
            LockManager.Dispose();
            _a = new Subject();
            _b = new Subject();
        }

        [TearDown]
        public void TearDown()
        {
            LockManager.Dispose();
        }

        #endregion

        #region GetSnapshot

        [Test]
        public void GetSnapshot_CountsSubjectsAndLocks()
        {
            LockManager.AddLock(_a, TypeA, new object());
            LockManager.AddLock(_a, TypeA, new object());
            LockManager.AddLock(_b, TypeB, new object());

            var snapshot = LockManager.GetSnapshot();

            Assert.AreEqual(2, snapshot.LockedSubjectCount);
            Assert.AreEqual(3, snapshot.LockCount);
        }

        [Test]
        public void GetSnapshot_UnboundSubjectCount_TracksDestroyBinding()
        {
            var token = new TokenSubject();
            LockManager.AddLock(_a, TypeA, new object());        // 普通对象:无销毁绑定
            LockManager.AddLock(token, TypeA, new object());     // 有令牌:绑定成功

            var snapshot = LockManager.GetSnapshot();

            Assert.AreEqual(2, snapshot.LockedSubjectCount);
            Assert.AreEqual(1, snapshot.UnboundSubjectCount, "只有普通对象那个是「永远不会自动释放」的");

            token.DisposeCts();
        }

        [Test]
        public void GetSnapshot_UnboundEqualsLocked_WhenAutoReleaseDisabled()
        {
            LockManager.AutoReleaseOnDestroy = false;
            var token = new TokenSubject();
            LockManager.AddLock(token, TypeA, new object());

            var snapshot = LockManager.GetSnapshot();

            Assert.AreEqual(snapshot.LockedSubjectCount, snapshot.UnboundSubjectCount,
                "开关关闭时一个都没绑——口径如此");

            token.DisposeCts();
        }

        [Test]
        public void GetSnapshot_SubscribedSubjectCount_DedupesTwoTables()
        {
            LockManager.OnLocked(_a, _ => { });
            LockManager.OnUnlocked(_a, _ => { });   // 同一主体的两张表
            LockManager.OnLocked(_b, _ => { });

            var snapshot = LockManager.GetSnapshot();

            Assert.AreEqual(2, snapshot.SubscribedSubjectCount, "两张表的键取并集");
            Assert.AreEqual(2, snapshot.UnboundSubscribedSubjectCount, "两个都是普通对象,都没有销毁绑定");
        }

        [Test]
        public void GetSnapshot_EmptyManager_IsAllZero()
        {
            var snapshot = LockManager.GetSnapshot();

            Assert.AreEqual(0, snapshot.LockedSubjectCount);
            Assert.AreEqual(0, snapshot.LockCount);
            Assert.AreEqual(0, snapshot.UnboundSubjectCount);
            Assert.AreEqual(0, snapshot.SubscribedSubjectCount);
            Assert.AreEqual(0, snapshot.UnboundSubscribedSubjectCount);
            Assert.IsNotNull(snapshot.ToString());
        }

        [Test]
        public void GetSnapshot_CountsGlobalAsASubject()
        {
            LockManager.AddLock(LockManager.Global, TypeA, new object());

            var snapshot = LockManager.GetSnapshot();

            Assert.AreEqual(1, snapshot.LockedSubjectCount, "Global 也是一个主体");
            Assert.AreEqual(1, snapshot.LockCount);
        }

        #endregion

        #region CopyLockedTypes

        [Test]
        public void CopyLockedTypes_ClearsBuffer_AndReturnsCount()
        {
            LockManager.AddLock(_a, TypeA, new object());
            LockManager.AddLock(_a, TypeB, new object());

            var buffer = new List<int> { 99, 98 };
            int written = LockManager.CopyLockedTypes(_a, buffer);

            Assert.AreEqual(2, written);
            Assert.AreEqual(2, buffer.Count, "缓冲先被清空");
            CollectionAssert.Contains(buffer, TypeA);
            CollectionAssert.Contains(buffer, TypeB);
        }

        [Test]
        public void CopyLockedTypes_IncludesGlobalLock_ButNotOtherSubjects()
        {
            LockManager.AddLock(_a, TypeA, new object());
            LockManager.AddLock(LockManager.Global, TypeB, new object());

            var forA = new List<int>();
            LockManager.CopyLockedTypes(_a, forA);

            var forB = new List<int>();
            LockManager.CopyLockedTypes(_b, forB);

            CollectionAssert.AreEquivalent(new[] { TypeA, TypeB }, forA, "聚合视角:自己的 + 全局的");
            CollectionAssert.AreEquivalent(new[] { TypeB }, forB, "别人的锁看不见,全局的看得见");
        }

        [Test]
        public void CopyLockedTypes_DedupesTypeLockedBothLocallyAndGlobally()
        {
            LockManager.AddLock(_a, TypeA, new object());
            LockManager.AddLock(LockManager.Global, TypeA, new object());

            var buffer = new List<int>();
            int written = LockManager.CopyLockedTypes(_a, buffer);

            Assert.AreEqual(1, written, "同一类型同时被主体与全局锁住时只出现一次");
            CollectionAssert.AreEqual(new[] { TypeA }, buffer);
        }

        [Test]
        public void CopyLockedTypes_NoLocks_ReturnsZeroAndEmptyBuffer()
        {
            var buffer = new List<int> { 7 };
            int written = LockManager.CopyLockedTypes(_a, buffer);

            Assert.AreEqual(0, written);
            Assert.AreEqual(0, buffer.Count);
        }

        [Test]
        public void CopyLockedTypes_NullBuffer_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => LockManager.CopyLockedTypes(_a, null));
        }

        [Test]
        public void CopyLockedTypes_NullSubject_BehavesAsGlobal()
        {
            LockManager.AddLock(LockManager.Global, TypeA, new object());

            var buffer = new List<int>();
            int written = LockManager.CopyLockedTypes(null, buffer);

            Assert.AreEqual(1, written, "null 主体 = Global，与其余入口一致");
        }

        #endregion

        #region DumpState

        [Test]
        public void DumpState_ListsSubjectsTypesAndGlobalTag()
        {
            LockManager.AddLock(LockManager.Global, TypeB, new object());
            LockManager.AddLock(_a, TypeA, new object());

            string dump = LockManager.DumpState();

            Assert.IsNotNull(dump);
            StringAssert.Contains("global", dump, "Global 必须被标出来——它是最常见的成因");
            StringAssert.Contains(TypeA.ToString(), dump);
            StringAssert.Contains(TypeB.ToString(), dump);
            StringAssert.Contains(nameof(Subject), dump, "主体名用类型名呈现");
        }

        [Test]
        public void DumpState_DoesNotCallHolderToString()
        {
            LockManager.AddLock(_a, TypeA, new HostileHolder());

            string dump = null;
            Assert.DoesNotThrow(() => dump = LockManager.DumpState(),
                "持有者名一律取 GetType().Name：ToString 是用户代码，可能在诊断路径上二次抛");
            StringAssert.Contains(nameof(HostileHolder), dump);
        }

        [Test]
        public void DumpState_EmptyManager_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => LockManager.DumpState());
        }

        #endregion
    }
}
