using System;
using NUnit.Framework;
using XFramework.XLock;

namespace XFramework.XLock.Tests
{
    /// <summary>
    /// 主体身份：<see cref="ILockable"/> 作字典键时按**引用同一**判定，不看 <c>Equals</c> 重写。
    /// <para><b>为什么这是缺陷而非口味</b>：游戏实体按 Id 重写 <c>Equals</c> 很常见（值相等即「同一个
    /// 实体」）。默认比较器下，两个**不同**实体只要值相等就是同一个键——给 A 加锁连 B 也一起被锁、
    /// A 的订阅会被 B 的锁惊动、<c>RemoveAllLocks(A)</c> 会把 B 的锁一并清掉。全都是静默的，且只在
    /// 「两个值相等的实体同时存在」时显形（同一时刻的敌人克隆、复用 Id 的池化对象、DTO 与其镜像）。</para>
    /// <para>仓内先例：<c>XPool.Pool&lt;T&gt;.InstanceReferenceComparer</c>、
    /// <c>UpdateScheduler.NodeReferenceComparer</c>（后者的注释明写「身份判定用引用同一」）。</para>
    /// </summary>
    [TestFixture]
    public class LockSubjectIdentityTests
    {
        #region Test Doubles

        /// <summary>按 Id 判等的实体——两个实例 Equals 相等，但是两个东西。</summary>
        private sealed class IdSubject : ILockable
        {
            public int Id;

            public override bool Equals(object obj) => obj is IdSubject other && other.Id == Id;

            public override int GetHashCode() => Id;
        }

        private IdSubject _a;
        private IdSubject _b;

        #endregion

        #region Setup / Teardown

        [SetUp]
        public void SetUp()
        {
            LockManager.Dispose();
            _a = new IdSubject { Id = 7 };
            _b = new IdSubject { Id = 7 };
            Assert.IsTrue(_a.Equals(_b), "前置：两个替身确实值相等");
            Assert.IsFalse(ReferenceEquals(_a, _b), "前置：但它们是两个对象");
        }

        [TearDown]
        public void TearDown()
        {
            LockManager.Dispose();
        }

        #endregion

        [Test]
        public void ValueEqualSubjects_DoNotShareLockState()
        {
            LockManager.AddLock(_a, 0, new object());

            Assert.IsTrue(LockManager.IsLocked(_a, 0), "A 被锁");
            Assert.IsFalse(LockManager.IsLocked(_b, 0), "B 是另一个对象,不该跟着被锁");
        }

        [Test]
        public void ValueEqualSubjects_HaveIndependentCounts()
        {
            LockManager.AddLock(_a, 0, new object());
            LockManager.AddLock(_b, 0, new object());

            Assert.AreEqual(1, LockManager.GetLockCount(_a, 0), "各自一把");
            Assert.AreEqual(1, LockManager.GetLockCount(_b, 0));
        }

        [Test]
        public void ValueEqualSubjects_SubscriptionsAreIndependent()
        {
            int aCount = 0;
            using var sub = LockManager.OnLocked(_a, _ => aCount++);

            LockManager.AddLock(_b, 0, new object());

            Assert.AreEqual(0, aCount, "B 加锁不该惊动 A 的订阅者");
        }

        [Test]
        public void ValueEqualSubjects_RemoveAllLocks_AffectsOnlyOne()
        {
            LockManager.AddLock(_a, 0, new object());
            LockManager.AddLock(_b, 0, new object());

            LockManager.RemoveAllLocks(_a);

            Assert.IsFalse(LockManager.IsLocked(_a, 0));
            Assert.IsTrue(LockManager.IsLocked(_b, 0), "B 的锁不该被 A 的批量清理带走");
        }

        [Test]
        public void ValueEqualSubjects_DestroyBindingIsPerObject()
        {
            var token = new IdSubjectToken { Id = 7 };
            var twin = new IdSubjectToken { Id = 7 };

            LockManager.AddLock(token, 0, new object());
            Assert.IsTrue(LockManager.IsLocked(twin, 0) == false, "双胞胎不受影响");

            token.Destroy();
            Assert.IsFalse(LockManager.IsLocked(token, 0));
            token.DisposeCts();
        }

        /// <summary>同样按 Id 判等、但带销毁令牌的替身。</summary>
        private sealed class IdSubjectToken : ILockable, XFramework.XMessage.IDestroyCancellationToken
        {
            public int Id;

            private readonly System.Threading.CancellationTokenSource _cts =
                new System.Threading.CancellationTokenSource();

            public System.Threading.CancellationToken DestroyCancellationToken => _cts.Token;

            public void Destroy() => _cts.Cancel();

            public void DisposeCts() => _cts.Dispose();

            public override bool Equals(object obj) => obj is IdSubjectToken other && other.Id == Id;

            public override int GetHashCode() => Id;
        }
    }
}
