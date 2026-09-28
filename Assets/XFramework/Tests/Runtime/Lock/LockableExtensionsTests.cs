using System;
using NUnit.Framework;
using XFramework.XLock;

namespace XFramework.XLock.Tests
{
    /// <summary>
    /// <see cref="LockableExtensions"/> 的逐个成员覆盖（此前**零直接覆盖**）。
    /// <para>每条都断言**正反两半**：<c>this</c> 确实被影响，**且**另一个主体没被牵连。只断前半的话，
    /// 「转发接错线」（把 <c>self</c> 丢掉、改用 <c>Global</c>、或整条转发到别的方法）照样能过——
    /// 那是反射式完备性检查抓不到的一类缺陷，只能靠行为测试。</para>
    /// </summary>
    [TestFixture]
    public class LockableExtensionsTests
    {
        #region Test Doubles

        private sealed class Subject : ILockable { }

        private const int TypeA = 0;
        private const int TypeB = 1;

        private Subject _a;
        private Subject _b;

        private readonly object _tokenX = new object();

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

        #region 加锁 / 解锁

        [Test]
        public void AddLock_BindsToThis()
        {
            var handle = _a.AddLock(TypeA, _tokenX);

            Assert.IsTrue(handle.IsHeld);
            Assert.IsTrue(_a.IsLocked(TypeA), "this 被锁");
            Assert.IsFalse(_b.IsLocked(TypeA), "另一个主体不受影响");
        }

        [Test]
        public void RemoveLock_BindsToThis()
        {
            LockManager.AddLock(_a, TypeA, _tokenX);
            LockManager.AddLock(_b, TypeA, _tokenX);

            _a.RemoveLock(TypeA, _tokenX);

            Assert.IsFalse(_a.IsLocked(TypeA));
            Assert.IsTrue(_b.IsLocked(TypeA), "只解开 this 的");
        }

        [Test]
        public void RemoveAllLocks_BindsToThis_AndReturnsCount()
        {
            LockManager.AddLock(_a, TypeA, _tokenX);
            LockManager.AddLock(_a, TypeB, _tokenX);
            LockManager.AddLock(_b, TypeA, _tokenX);

            int removed = _a.RemoveAllLocks();

            Assert.AreEqual(2, removed);
            Assert.IsFalse(_a.IsLocked(TypeA));
            Assert.IsFalse(_a.IsLocked(TypeB));
            Assert.IsTrue(_b.IsLocked(TypeA), "不碰别人的");
        }

        [Test]
        public void RemoveAllSubscriptions_BindsToThis()
        {
            int aCount = 0;
            int bCount = 0;
            _a.OnLocked(_ => aCount++);
            _b.OnLocked(_ => bCount++);

            _a.RemoveAllSubscriptions();

            LockManager.AddLock(LockManager.Global, TypeA, _tokenX);   // 全局锁会通知所有主体订阅者

            Assert.AreEqual(0, aCount, "this 的订阅被丢弃");
            Assert.AreEqual(1, bCount, "别人的订阅还在");
        }

        #endregion

        #region 查询

        [Test]
        public void IsLocked_BindsToThis()
        {
            LockManager.AddLock(_a, TypeA, _tokenX);

            Assert.IsTrue(_a.IsLocked(TypeA));
            Assert.IsFalse(_b.IsLocked(TypeA));
        }

        [Test]
        public void GetLockCount_BindsToThis()
        {
            LockManager.AddLock(_a, TypeA, _tokenX);
            LockManager.AddLock(_a, TypeA, new object());
            LockManager.AddLock(_b, TypeA, _tokenX);

            Assert.AreEqual(2, _a.GetLockCount(TypeA));
            Assert.AreEqual(1, _b.GetLockCount(TypeA));
        }

        [Test]
        public void GetLockObjects_BindsToThis()
        {
            LockManager.AddLock(_a, TypeA, _tokenX);
            LockManager.AddLock(_b, TypeA, new object());

            var objects = _a.GetLockObjects(TypeA);

            Assert.AreEqual(1, objects.Count);
            CollectionAssert.Contains(objects, _tokenX);
            Assert.AreEqual(1, _b.GetLockObjects(TypeA).Count, "各自只看自己的");
        }

        #endregion

        #region 订阅

        [Test]
        public void OnLocked_BindsToThis()
        {
            int count = 0;
            using var sub = _a.OnLocked(_ => count++);

            LockManager.AddLock(_b, TypeA, _tokenX);
            Assert.AreEqual(0, count, "别人的锁不该通知 this 的订阅");

            LockManager.AddLock(_a, TypeA, _tokenX);
            Assert.AreEqual(1, count);
        }

        [Test]
        public void OnUnlocked_BindsToThis()
        {
            int count = 0;
            using var sub = _a.OnUnlocked(_ => count++);
            var handle = LockManager.AddLock(_a, TypeA, _tokenX);

            LockManager.AddLock(_b, TypeA, _tokenX);
            LockManager.RemoveLock(_b, TypeA, _tokenX);
            Assert.AreEqual(0, count, "别人的解锁不该通知 this 的订阅");

            handle.Dispose();
            Assert.AreEqual(1, count);
        }

        [Test]
        public void OnLockStateChanged_BindsToThis()
        {
            int count = 0;
            using var sub = _a.OnLockStateChanged((_, _) => count++);

            LockManager.AddLock(_b, TypeA, _tokenX);
            Assert.AreEqual(0, count, "别人的聚合与本主体无关");

            var handle = LockManager.AddLock(_a, TypeA, _tokenX);
            Assert.AreEqual(1, count, "this 的聚合翻转为锁定");

            handle.Dispose();
            Assert.AreEqual(2, count);
        }

        #endregion
    }
}
