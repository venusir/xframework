using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using XFramework.XLock;
using XFramework.XMessage;

namespace XFramework.XLock.Tests
{
    /// <summary>
    /// 内部容器的回收护栏（反射）。四个容器（锁、两张订阅表、销毁绑定）都是静态根，任何一条路径漏了
    /// 摘除就是**无界增长**——而这类缺陷在功能断言上完全看不出来（功能照常工作，内存只增不减）。
    /// <para>正常释放路径原本只有「门面重置」一条护栏，逐路径回收没有守卫，本 fixture 补上。</para>
    /// <para>本组是**现状护栏**，不留红基线（它守的是「别在未来改坏」）。</para>
    /// </summary>
    [TestFixture]
    public class LockInternalStateTests
    {
        #region Test Doubles

        private sealed class Subject : ILockable { }

        private sealed class TokenSubject : ILockable, IDestroyCancellationToken
        {
            private readonly CancellationTokenSource _cts = new CancellationTokenSource();

            public CancellationToken DestroyCancellationToken => _cts.Token;

            public void Destroy() => _cts.Cancel();

            public void DisposeCts() => _cts.Dispose();
        }

        private const int TypeA = 0;

        #endregion

        #region Setup / Teardown

        [SetUp]
        public void SetUp()
        {
            LockManager.Dispose();
        }

        [TearDown]
        public void TearDown()
        {
            LockManager.Dispose();
        }

        #endregion

        #region Reflection

        private static readonly string[] ContainerNames =
        {
            "_locks", "_onLockedSubjects", "_onUnlockedSubjects", "_destroyBindings",
        };

        private static IDictionary Container(string name)
        {
            var field = typeof(LockManager).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, $"找不到字段 {name}——它被改名了吗？改名请同步本 fixture");
            return (IDictionary)field.GetValue(null);
        }

        private static void AssertAllEmpty(string because)
        {
            foreach (var name in ContainerNames)
                Assert.AreEqual(0, Container(name).Count, $"{name} 残留条目（{because}）");
        }

        #endregion

        [Test]
        public void AfterReleaseAndUnsubscribe_AllContainersAreEmpty()
        {
            var subject = new Subject();
            var handle = LockManager.AddLock(subject, TypeA, new object());
            var sub = LockManager.OnLocked(subject, _ => { });
            var stateSub = LockManager.OnLockStateChanged(subject, (_, _) => { });

            handle.Dispose();
            sub.Dispose();
            stateSub.Dispose();

            AssertAllEmpty("加锁→解锁、订阅→退订后应全部回收");
        }

        [Test]
        public void AfterRemoveAll_AllContainersAreEmpty()
        {
            var subject = new Subject();
            LockManager.AddLock(subject, TypeA, new object());
            LockManager.OnLocked(subject, _ => { });

            LockManager.RemoveAllLocks(subject);
            LockManager.RemoveAllSubscriptions(subject);

            AssertAllEmpty("批量清理后应全部回收");
        }

        [Test]
        public void AfterSubjectDestroyed_AllContainersAreEmpty()
        {
            var subject = new TokenSubject();
            LockManager.AddLock(subject, TypeA, new object());
            LockManager.OnLocked(subject, _ => { });

            subject.Destroy();

            AssertAllEmpty("主体销毁应把它的四个表项都摘掉");
            subject.DisposeCts();
        }

        [Test]
        public void AfterFacadeDispose_ResetToDefaults()
        {
            var subject = new TokenSubject();
            LockManager.AddLock(subject, TypeA, new object());
            LockManager.OnLocked(subject, _ => { });
            LockManager.AutoReleaseOnDestroy = false;

            LockManager.Dispose();

            AssertAllEmpty("门面重置应清空");
            Assert.IsTrue(LockManager.AutoReleaseOnDestroy, "开关应复位为 true");

            // 字段式事件的 backing field 是私有的同名委托字段
            var busField = typeof(LockManager).GetField("OnGlobalLocked", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(busField, "找不到 OnGlobalLocked 的 backing field");
            Assert.IsNull(busField.GetValue(null), "总线事件应被置空");

            subject.DisposeCts();
        }
    }
}
