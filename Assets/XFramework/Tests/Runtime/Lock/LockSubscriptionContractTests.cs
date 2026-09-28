using System;
using NUnit.Framework;
using XFramework.XLock;

namespace XFramework.XLock.Tests
{
    /// <summary>
    /// 订阅入口的契约：主体归一（null = Global）、以 <see cref="LockManager.Global"/> 订阅、
    /// 以及两个全局事件的载荷归一。
    /// <para><b>为什么是缺陷而非口味</b>：模块其余入口（<c>AddLock</c>/<c>IsLocked</c>/<c>IsLockedBy</c>/
    /// <c>GetLockCount</c>/<c>GetLockObjects</c>）一律把 null 主体约定为 <see cref="LockManager.Global"/>，
    /// 唯独订阅侧把它原样交给 <c>Dictionary</c>——抛出来的是 <c>ArgumentNullException</c>（paramName 是
    /// <c>key</c>），既没有 <c>[Lock]</c> 前缀也没有修复提示，与模块自己的约定相反。</para>
    /// <para>另一半是<b>静默失效</b>：以 <see cref="LockManager.Global"/> 订阅的处理器永远不会被调用
    /// ——全局分支显式跳过 Global 键，主体分支只按主体查表。而模块文档到处教「全局锁请使用 Global 作为
    /// lockSubject」，第三方照写即无声无效果。既然没有任何正确用法能依赖「永不触发」，就把它接上：
    /// 以 Global 订阅 = 只关心全局锁的加/解锁事件。</para>
    /// <para>第三处是载荷：两个全局事件的 subject 参数原本原样透传，于是同一件事有两种表现
    /// （显式传 Global 收到 Global，传 null 收到 null）。</para>
    /// </summary>
    [TestFixture]
    public class LockSubscriptionContractTests
    {
        #region Test Doubles

        private sealed class Subject : ILockable { }

        private const int TypeA = 0;

        private Subject _subjectA;

        #endregion

        #region Setup / Teardown

        [SetUp]
        public void SetUp()
        {
            LockManager.Dispose();
            _subjectA = new Subject();
        }

        [TearDown]
        public void TearDown()
        {
            LockManager.Dispose();
        }

        #endregion

        #region null 主体

        /// <summary>null 主体等价于 Global：订阅不抛、句柄可正常退订。</summary>
        [Test]
        public void OnLocked_NullSubject_BehavesAsGlobal()
        {
            int count = 0;
            IDisposable sub = null;
            Assert.DoesNotThrow(() => sub = LockManager.OnLocked(null, _ => count++),
                "其余入口都把 null 约定为 Global,订阅侧不该例外");

            LockManager.AddLock(LockManager.Global, TypeA, new object());
            Assert.AreEqual(1, count, "归一后的键应能收到全局锁事件");

            Assert.DoesNotThrow(() => sub.Dispose(), "句柄必须能退订——它拿到的也该是归一后的键");
        }

        /// <summary>null handler 必须被拒绝：否则会留下一个「有键无回调」的幽灵订阅。</summary>
        [Test]
        public void OnLocked_NullHandler_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => LockManager.OnLocked(_subjectA, null));
            Assert.Throws<ArgumentNullException>(() => LockManager.OnUnlocked(_subjectA, null));
        }

        #endregion

        #region 以 Global 订阅

        [Test]
        public void OnLocked_GlobalSubject_ReceivesGlobalLock()
        {
            int count = 0;
            using var sub = LockManager.OnLocked(LockManager.Global, _ => count++);
            using var handle = LockManager.AddLock(LockManager.Global, TypeA, new object());

            Assert.AreEqual(1, count, "以 Global 订阅应收到全局锁事件");
        }

        [Test]
        public void OnUnlocked_GlobalSubject_ReceivesGlobalUnlock()
        {
            int count = 0;
            using var sub = LockManager.OnUnlocked(LockManager.Global, _ => count++);
            using var handle = LockManager.AddLock(LockManager.Global, TypeA, new object());
            handle.Dispose();

            Assert.AreEqual(1, count, "以 Global 订阅应收到全局解锁事件");
        }

        /// <summary>边界：主体锁不通知以 Global 订阅者——它回答的是「全局锁有没有」。</summary>
        [Test]
        public void OnLocked_GlobalSubject_NotNotifiedBySubjectLock()
        {
            int count = 0;
            using var sub = LockManager.OnLocked(LockManager.Global, _ => count++);
            using var handle = LockManager.AddLock(_subjectA, TypeA, new object());

            Assert.AreEqual(0, count, "主体锁与「全局锁」是两件事");
        }

        #endregion

        #region 两个全局事件的载荷

        [Test]
        public void OnGlobalLocked_PayloadSubject_NormalizedToGlobal()
        {
            ILockable received = _subjectA;
            LockManager.OnGlobalLocked += (subject, _, _) => received = subject;

            LockManager.AddLock(null, TypeA, new object());

            Assert.AreSame(LockManager.Global, received, "载荷里的主体应归一为 Global,而不是原样透传 null");
        }

        [Test]
        public void OnGlobalUnlocked_PayloadSubject_NormalizedToGlobal()
        {
            ILockable received = _subjectA;
            var handle = LockManager.AddLock(null, TypeA, new object());
            LockManager.OnGlobalUnlocked += (subject, _, _) => received = subject;

            handle.Dispose();

            Assert.AreSame(LockManager.Global, received, "解锁事件的载荷同样要归一");
        }

        #endregion
    }
}
