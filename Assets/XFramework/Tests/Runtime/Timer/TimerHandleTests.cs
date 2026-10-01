using NUnit.Framework;

namespace XFramework.XTimer.Tests
{
    /// <summary>
    /// 句柄的值语义与默认值安全。
    /// <para><b>本 fixture 只覆盖「不需要引擎也能验证」的部分</b>——句柄是纯值类型，<c>default</c> 上的
    /// 每个成员都必须在<b>核心从未被触碰</b>的前提下安全返回。这不只是防御性编程：它是
    /// 「令牌取消时创建会返回 default 句柄」这条契约不把调用方拖进异常的前提
    /// （对照 <c>AudioHandle</c> 的 <c>default</c> 语义）。</para>
    /// <para><b>它顺带是一条哨兵</b>：本 fixture 不做任何 SetUp/TearDown，因此若哪天有人给
    /// <c>IsActive</c> / <c>Stop</c> / <c>Restart</c> 去掉了 <c>IsDefault</c> 短路，
    /// 这些用例会立刻在「未初始化/未驱动」的上下文里炸出来。</para>
    /// </summary>
    [TestFixture]
    public class TimerHandleTests
    {
        #region IsDefault

        [Test]
        public void Default_IsDefault()
        {
            Assert.IsTrue(default(TimerHandle).IsDefault);
            Assert.IsTrue(new TimerHandle(0, 0, 0).IsDefault);
            Assert.IsTrue(new TimerHandle(7, 3, 0).IsDefault, "代际为 0 即默认值，与表/槽位无关");
        }

        [Test]
        public void NonZeroGeneration_IsNotDefault()
        {
            Assert.IsFalse(new TimerHandle(0, 0, 1).IsDefault);
            Assert.IsFalse(new TimerHandle(7, 3, 2).IsDefault);
        }

        #endregion

        #region 默认值上的成员

        /// <summary>
        /// 默认句柄上的查询与控制一律短路，<b>不碰门面也不碰核心</b>。
        /// </summary>
        [Test]
        public void Default_QueriesAndControls_AreShortCircuited()
        {
            var handle = default(TimerHandle);

            Assert.IsFalse(handle.IsActive);
            Assert.AreEqual(0f, handle.Remaining);
            Assert.IsFalse(handle.Stop());
            Assert.IsFalse(handle.Restart());
        }

        /// <summary>
        /// <c>Dispose</c> 必须对默认句柄安全，且可重复调用。
        /// <para>「池已满/创建被忽略」这类路径返回的正是默认句柄，而调用方往往无脑写在
        /// <c>using</c> 或统一清理里——那里抛异常是设计事故。</para>
        /// </summary>
        [Test]
        public void Default_Dispose_IsSafeAndIdempotent()
        {
            var handle = default(TimerHandle);

            handle.Dispose();
            handle.Dispose();
        }

        /// <summary>
        /// 代际非 0 但门面从未创建过核心时同样安全：句柄可以比核心活得更久
        /// （<see cref="TimerManager.AutoInit"/> 会整个换掉核心，旧句柄随即失效）。
        /// </summary>
        [Test]
        public void StaleHandle_AfterCoreReplaced_IsSafe()
        {
            // 不调用 CancelAll：本用例要的正是「核心被换掉、句柄还在手上」这一刻
            var stale = new TimerHandle(4242, 17, 3);

            Assert.IsFalse(stale.IsActive);
            Assert.AreEqual(0f, stale.Remaining);
            Assert.IsFalse(stale.Stop());
            Assert.IsFalse(stale.Restart());
            stale.Dispose();
        }

        #endregion

        #region 值语义

        /// <summary>
        /// 句柄是可自由复制的值：复制品与原件行为一致，且复制本身不产生任何副作用。
        /// </summary>
        [Test]
        public void Handle_IsCopyableValue()
        {
            var original = new TimerHandle(1, 2, 3);
            var copy = original;

            Assert.AreEqual(original.IsDefault, copy.IsDefault);
            Assert.AreEqual(original.IsActive, copy.IsActive);
            Assert.AreEqual(original.Remaining, copy.Remaining);
        }

        #endregion
    }
}
