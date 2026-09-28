using System;
using NUnit.Framework;
using XFramework.XLock;

namespace XFramework.XLock.Tests
{
    /// <summary>
    /// 键的语义（现状契约，非缺陷）：
    /// <list type="bullet">
    /// <item><description><c>lockObj</c> 按**值相等**判定——键相等就是同一把锁；</description></item>
    /// <item><description><c>lockType</c> 是全框架共享的 <c>int</c> 命名空间——同值即同类型。</description></item>
    /// </list>
    /// <para>这两条都是有意设计（同一来源重复加锁幂等、跨主体用同一类型号编排），代价是**两个互不相关的
    /// 来源取了同一个键就会互相影响**。本 fixture 把它们钉成事实，免得哪天被无声改掉；正确用法是每个
    /// 加锁点用专用 token、跨模块先约定 lockType 分段（见 README「键的语义」）。</para>
    /// <para>与之相对，**主体**键按引用同一判定（见 <see cref="LockSubjectIdentityTests"/>）——两者规则
    /// 不同是有意的：主体是「锁谁」，身份就是那个对象；<c>lockObj</c> 是「谁锁的」，是调用方选定的名字。</para>
    /// </summary>
    [TestFixture]
    public class LockKeySemanticsTests
    {
        #region Test Doubles

        private sealed class Subject : ILockable { }

        private const int TypeA = 0;

        private Subject _subject;

        private static readonly object TokenA = new object();
        private static readonly object TokenB = new object();

        #endregion

        #region Setup / Teardown

        [SetUp]
        public void SetUp()
        {
            LockManager.Dispose();
            _subject = new Subject();
        }

        [TearDown]
        public void TearDown()
        {
            LockManager.Dispose();
        }

        #endregion

        [Test]
        public void EqualStringKeys_AreOneHolder_ReleaseByEitherUnlocksBoth()
        {
            // 两个来源用了同一个字面量（会被驻留，必然相等）
            var dialogue = LockManager.AddLock(_subject, TypeA, "dialogue");
            LockManager.AddLock(_subject, TypeA, "dialogue");

            Assert.AreEqual(1, LockManager.GetLockCount(_subject, TypeA), "键相等 = 同一把锁");

            dialogue.Dispose();

            Assert.IsFalse(LockManager.IsLocked(_subject, TypeA),
                "一方释放即全部解锁——这就是「两个来源撞名字」的代价，用专用 token 规避");
        }

        [Test]
        public void DedicatedTokens_AreIndependent()
        {
            var a = LockManager.AddLock(_subject, TypeA, TokenA);
            var b = LockManager.AddLock(_subject, TypeA, TokenB);

            Assert.AreEqual(2, LockManager.GetLockCount(_subject, TypeA), "各用一枚 token 即各算一把");

            a.Dispose();
            Assert.IsTrue(b.IsHeld, "释放自己不牵连别人");
            Assert.IsTrue(LockManager.IsLocked(_subject, TypeA));
        }

        [Test]
        public void SameTokenTwice_IsIdempotent()
        {
            LockManager.AddLock(_subject, TypeA, TokenA);
            LockManager.AddLock(_subject, TypeA, TokenA);

            Assert.AreEqual(1, LockManager.GetLockCount(_subject, TypeA), "同一 token 重复加锁幂等");
        }

        /// <summary>两个「模块」各自定义了自己的 lockType（撞号）——同一类型号即同一类型。</summary>
        [Test]
        public void SameLockTypeValue_IsTheSameTypeAcrossModules()
        {
            const int ModuleAMovement = 1;
            const int ModuleBMovement = 1;

            LockManager.AddLock(_subject, ModuleAMovement, TokenA);

            Assert.IsTrue(LockManager.IsLocked(_subject, ModuleBMovement),
                "跨模块撞号：B 的查询看到 A 的锁，且编译期毫无提示");
        }
    }
}
