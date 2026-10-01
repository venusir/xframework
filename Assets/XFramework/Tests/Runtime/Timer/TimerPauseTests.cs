using System;
using NUnit.Framework;
using UnityEngine;
using XFramework.XUpdate;

namespace XFramework.XTimer.Tests
{
    /// <summary>
    /// 暂停与时间缩放：本模块<b>没有自己的暂停开关</b>，语义全部来自 Update 的双时间轴。
    /// <para><b>这是 Roadmap 立项时划的红线</b>：暂停与时间缩放必须架在 Update 的双时间轴之上，
    /// 不得另开一套时间口径——那等于替使用方决定时间模型，撞框架的第一条非目标。故
    /// <see cref="TimerManager"/> 上没有 <c>Pause</c>，只有一个 <c>UpdateTimeMode</c> 参数；
    /// 下面的用例锁的就是「这条转接没走样」。</para>
    /// <para><b>时钟用显式传入的 <c>UpdateClock</c> 构造</b>：真实 <c>Time.timeScale = 0</c> 那条路径
    /// 由 <c>UpdateManager.Tick()</c> 的 <c>BuildClock</c> 把 <c>timeScale &lt;= 0</c> 填进
    /// <c>IsPaused</c>，与这里传的字段是同一条通道，故无需依赖真实时间流逝（那会让用例变慢且不稳）。</para>
    /// </summary>
    [TestFixture]
    public class TimerPauseTests
    {
        #region Fixture

        private float _originalTimeScale;
        private TimerDriver _driver;

        [SetUp]
        public void SetUp()
        {
            _originalTimeScale = Time.timeScale;
            TimerTestKit.SetUp();
            _driver = new TimerDriver();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = _originalTimeScale;
            TimerTestKit.TearDown();
        }

        #endregion

        #region 冻结逻辑轴

        /// <summary>
        /// 逻辑轴冻结（<c>timeScale = 0</c>）时，受时间缩放影响的定时器不动，墙钟轴照常。
        /// <para><b>被冻结的定时器仍算「在计时」</b>：冻结的是时间轴而不是定时器的存在，恢复后它会从
        /// 冻结点续上。这条与「暂停中的音频仍算在播」同源。</para>
        /// </summary>
        [Test]
        public void FrozenLogicalAxis_StopsScaledTimersOnly()
        {
            int scaled = 0;
            int unscaled = 0;
            TimerHandle scaledHandle = TimerManager.Every(0.05f, () => scaled++);
            TimerManager.Every(0.05f, () => unscaled++, UpdateTimeMode.Unscaled);

            _driver.Advance(4);
            int scaledBefore = scaled;

            for (int i = 0; i < 30; i++)
                _driver.AdvanceFrozen();

            Assert.AreEqual(scaledBefore, scaled, "逻辑轴冻结期间，Scaled 定时器一次都不该触发");
            Assert.Greater(unscaled, scaledBefore, "墙钟轴不受冻结影响");
            Assert.IsTrue(scaledHandle.IsActive, "被冻结的定时器仍算在计时——它只是暂时走不动");
        }

        #endregion

        #region 暂停 / 恢复（不追赶）

        /// <summary>
        /// <c>UpdateManager.Pause()</c> → <c>Resume()</c>：<b>恢复不追赶</b>。
        /// <para>恢复后首帧只重锚时间基准（delta 记 0），剩余量与暂停前逐字相同；随后再走满剩余时长
        /// 才触发。若实现改成「把整段暂停时长一次性补上」，暂停越久、恢复瞬间越容易连发——正是
        /// Update 自己明确避免的那种跳变。</para>
        /// </summary>
        [Test]
        public void PauseAndResume_DoesNotCatchUp()
        {
            int fired = 0;
            TimerHandle handle = TimerManager.After(0.2f, () => fired++);

            _driver.Advance(6);
            float before = handle.Remaining;
            Assert.Greater(before, 0f);
            Assert.Less(before, 0.2f);

            UpdateManager.Pause();
            _driver.Advance(60); // 暂停期间推进整整一秒

            Assert.AreEqual(0, fired);
            Assert.AreEqual(before, handle.Remaining, 1e-4f, "暂停期间剩余量一动不动");

            UpdateManager.Resume();
            _driver.Advance(1); // 恢复后首帧只重锚

            Assert.AreEqual(0, fired, "恢复的首帧不得追赶");
            Assert.AreEqual(before, handle.Remaining, 0.02f, "重锚只把基准推到此刻，不改变剩余量");

            _driver.Advance(12);

            Assert.AreEqual(1, fired, "再走满剩余时长之后才触发");
        }

        #endregion

        #region 时间缩放（快慢）

        /// <summary>
        /// <c>timeScale = 0.5</c> 时逻辑轴的 <c>deltaTime</c> 减半，于是 1 秒的定时器要走 2 真实秒。
        /// <para>这正是「Scaled」二字的含义，也是本模块不另开时间口径的收益：使用方按 Update 的
        /// 口径理解即可，不需要再学一套。</para>
        /// </summary>
        [Test]
        public void ScaledTimer_FollowsTimeScale()
        {
            int fired = 0;
            TimerManager.After(1f, () => fired++);

            _driver.Step = 0.5f / 60f; // 每帧推进半格
            _driver.Advance(120);      // 真实 1 秒 = 逻辑 0.5 秒

            Assert.AreEqual(0, fired, "逻辑时间才走了半秒，1 秒的定时器不该触发");

            _driver.Advance(121);      // 逻辑时间跨过 1 秒

            Assert.AreEqual(1, fired);
        }

        #endregion

        #region 两条轴各自独立

        /// <summary>
        /// 暂停期间墙钟轴上的定时器照常按自己的时钟推进——两条轴各有独立的逻辑时钟，
        /// 冻结一条不会让另一条的截止时刻漂移。
        /// </summary>
        /// <summary>
        /// 取 0.2 秒是刻意的：选档公式给出 <c>Tier0</c>（0.2 秒不足一个 Tier1 周期的 8 倍），
        /// 于是逻辑时钟逐帧前进、断言可以精确到帧。换成 1 秒就会落到 Tier2——每四帧才派发一次，
        /// 剩余量按 4/60 一跳，断言只能放宽到一个档位周期。
        /// </summary>
        [Test]
        public void UnscaledTimers_KeepTheirOwnClockWhilePaused()
        {
            TimerHandle scaledHandle = TimerManager.After(0.2f, () => { });
            TimerHandle unscaledHandle = TimerManager.After(0.2f, () => { }, UpdateTimeMode.Unscaled);

            _driver.Advance(2); // 首帧只锚定，其后一帧推进 1/60 秒

            float scaledBefore = scaledHandle.Remaining;
            float unscaledBefore = unscaledHandle.Remaining;
            Assert.AreEqual(scaledBefore, unscaledBefore, 1e-3f, "两条轴起步一致");

            UpdateManager.Pause();
            _driver.Advance(6);

            Assert.AreEqual(scaledBefore, scaledHandle.Remaining, 1e-4f, "逻辑轴被冻住，剩余量不动");
            Assert.AreEqual(unscaledBefore - 6f / 60f, unscaledHandle.Remaining, 1e-3f,
                "墙钟轴照常走：冻结逻辑轴不会让它的截止时刻漂移");
        }

        #endregion
    }
}
