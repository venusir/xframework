using NUnit.Framework;
using XFramework.XUpdate;

namespace XFramework.XInput.Tests
{
    /// <summary>
    /// 会话级复位的回归用例（**改前必红**）。
    /// <para><b>它锁的缺陷</b>：关闭域重载（Project Settings → Editor → Enter Play Mode Options →
    /// 取消 Reload Domain）时，上一播放会话的静态状态存活到第二会话，而门面的 <c>Initialize</c> 会因
    /// <c>_initialized</c> 仍为真而**告警后早退**——<c>_ticker</c> 还在、provider 还在，但没有任何调度器
    /// 持有它，全部 <c>Observe*</c> 静默失效。机制在 2026-10-01 已实测复现，本轮修它。</para>
    /// <para><b>为什么能在单个播放会话里验</b>：缺陷需要「静态状态跨会话存活」与「<c>Initialize</c> 因
    /// <c>_initialized</c> 早退」同时成立——后者在单会话里手动造得出来：调 <c>UpdateManager.OnQuitting()</c>
    /// （<c>Application.quitting</c> 上跑的正是它）模拟会话结束，再调 <c>UpdateManager.AutoInit()</c> 与
    /// <c>InputManager.ResetForNewSession()</c> 模拟会话开始（后者就是 <c>SubsystemRegistration</c> 上
    /// 真正会跑的东西）。<b>拿掉复位那一步即红</b>——这就是「改前必红」的证明路径。</para>
    /// </summary>
    [TestFixture]
    public class InputSessionResetTests
    {
        #region Fixture

        [SetUp]
        public void SetUp()
        {
            InputManager.Destroy();
            UpdateManager.AutoInit();
            UpdateManager.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            InputManager.Destroy();
            UpdateManager.AutoInit();
            UpdateManager.Clear();
        }

        #endregion

        #region 会话复位

        [Test]
        public void ResetForNewSession_LetsTheSecondSessionTickAgain()
        {
            // ① 会话 1：正常初始化，帧驱动进调度器
            InputManager.Initialize(new FakeInputProvider());
            Assert.AreEqual(1, UpdateManager.TotalCount, "会话 1 的帧驱动应当已进调度器");

            // ② 退出播放：调度器被整只丢弃（Application.quitting → UpdateManager.OnQuitting）
            UpdateManager.OnQuitting();
            Assert.AreEqual(0, UpdateManager.TotalCount, "退出后调度器应已清空");

            // ③ 会话 2 开始：SubsystemRegistration 上真正会跑的就是这两条
            UpdateManager.AutoInit();
            InputManager.ResetForNewSession();

            // ④ 会话 2 正常启动：必须真的重新初始化，而不是告警后早退
            InputManager.Initialize(new FakeInputProvider());

            Assert.IsTrue(InputManager.IsInitialized, "第二个会话里门面应处于已初始化态");
            Assert.AreEqual(1, UpdateManager.TotalCount,
                "第二个会话的帧驱动应重新进调度器——置不回来就是「关闭域重载后输入静默失效」那条缺陷");
        }

        /// <summary>
        /// 复位只丢引用、不调 <c>Dispose</c>：上一会话的 provider 可能握着已随域销毁的
        /// <c>InputActionAsset</c>，在会话开头对它调 Dispose 只会制造异常与噪音。
        /// </summary>
        [Test]
        public void ResetForNewSession_DoesNotDisposeThePreviousProvider()
        {
            var provider = new FakeInputProvider();
            InputManager.Initialize(provider);

            InputManager.ResetForNewSession();

            Assert.IsFalse(provider.Disposed,
                "复位不该对上一会话的 provider 调 Dispose——实例清理归 Destroy（谁初始化谁清理）");
            Assert.IsFalse(InputManager.IsInitialized, "复位后门面应回到未初始化态");
        }

        /// <summary>
        /// 复位<b>不动</b>帧脉冲上的订阅：句柄归订阅方，这是本模块的有意设计（README「已知限制」）。
        /// </summary>
        [Test]
        public void ResetForNewSession_KeepsFramePulseSubscriptions()
        {
            InputManager.Initialize(new FakeInputProvider { Pressed = true });

            int observed = 0;
            using (InputManager.ObservePressed("Jump", () => observed++))
            {
                InputManager.Tick();
                Assert.AreEqual(1, observed, "前提校验：会话 1 里订阅应当是活的");

                InputManager.ResetForNewSession();
                InputManager.Initialize(new FakeInputProvider { Pressed = true });

                InputManager.Tick();
                Assert.AreEqual(2, observed,
                    "复位不该退订帧脉冲上的订阅——句柄归订阅方，它有义务自己 Dispose");
            }
        }

        #endregion
    }
}
