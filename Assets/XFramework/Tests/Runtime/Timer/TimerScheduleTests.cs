using System;
using NUnit.Framework;
using UnityEngine;
using XFramework.XUpdate;

namespace XFramework.XTimer.Tests
{
    /// <summary>
    /// 到期语义：首拍时机、不漂移、跳拍不补发、剩余量读数。
    /// <para><b>本 fixture 是「比 <c>UniTask.Delay</c> 多给什么」里第三条的可执行形式</b>：固定间隔必须落在
    /// 「锚点 + k × interval」的网格上。逐拍累减 <c>deltaTime</c> 的写法同样能通过「数次数」的用例，
    /// 只有把逐拍时刻拿来对网格才验得出来——故 <see cref="TimerFires"/> 记的是时刻而不是次数。</para>
    /// </summary>
    [TestFixture]
    public class TimerScheduleTests
    {
        #region Fixture

        private const float Interval = 0.1f;

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

        #region 首拍

        [Test]
        public void Every_FirstFireIsOneIntervalAway_NotImmediate()
        {
            var fires = new TimerFires(_driver);
            TimerManager.Every(Interval, fires.Record);

            _driver.Advance(5); // 5/60 ≈ 0.083 秒，还没到 0.1
            Assert.AreEqual(0, fires.Count, "首拍必须隔一个 interval，不能立刻触发");

            _driver.Advance(2); // 累计 7/60 ≈ 0.117 秒
            Assert.AreEqual(1, fires.Count);
        }

        /// <summary>
        /// <c>After(0f)</c> 的含义是「下一拍」，而且回调必须发生在 <c>Create</c> <b>返回句柄之后</b>——
        /// 否则「先拿句柄再决定要不要留」这种写法会在还没拿到句柄时就被回调重入。
        /// </summary>
        [Test]
        public void AfterZero_DoesNotFireDuringCreate()
        {
            bool fired = false;
            TimerHandle handle = TimerManager.After(0f, () => fired = true);

            Assert.IsFalse(fired, "创建过程中不得回调");
            Assert.IsTrue(handle.IsActive);

            _driver.Advance(1);

            Assert.IsTrue(fired);
            Assert.IsFalse(handle.IsActive);
        }

        #endregion

        #region 不漂移

        /// <summary>
        /// 连续 400 帧的每一次触发都必须落在「首拍 + k × interval」上。
        /// <para>容差取 1.5 帧：截止时刻的网格是精确的，观测到的触发只能因「第一次跨过截止的那一帧」
        /// 而晚不到一帧。误差若随拍数增长，就说明实现改成了逐拍累加。</para>
        /// </summary>
        [Test]
        public void Every_DoesNotDrift()
        {
            var fires = new TimerFires(_driver);
            TimerManager.Every(Interval, fires.Record);

            _driver.Advance(400); // ≈ 6.67 秒

            Assert.Greater(fires.Count, 50, "驱动了 400 帧，0.1 秒的定时器应触发 60 次以上");

            double tolerance = 1.5 * _driver.Step;
            for (int k = 0; k < fires.Count; k++)
            {
                double expected = k * (double)Interval;
                Assert.AreEqual(expected, fires.Times[k] - fires.Times[0], tolerance,
                    $"第 {k} 拍偏离网格 {expected:0.####} 秒——逐拍累加 deltaTime 的写法会在这里随拍数线性发散");
            }
        }

        /// <summary>
        /// 一帧跨过多个周期时<b>只补发一次</b>，而不是把落下的拍数一次性补齐。
        /// <para>卡顿一秒就让 0.1 秒的定时器连发 10 次，是雪崩型缺陷的经典来源；这条与 Update 的
        /// 「宁可延长也不突发」同源。</para>
        /// </summary>
        [Test]
        public void Every_SkipsMissedTicks_WithoutBurst()
        {
            var fires = new TimerFires(_driver);
            TimerManager.Every(Interval, fires.Record);

            _driver.Advance(1); // 首拍只锚定
            Assert.AreEqual(0, fires.Count);

            _driver.Step = 1f; // 卡顿一秒
            _driver.Advance(1);

            Assert.AreEqual(1, fires.Count, "跨了 10 拍也只补发一次");
        }

        #endregion

        #region 剩余量

        [Test]
        public void Remaining_TracksTheDeadline()
        {
            TimerHandle handle = TimerManager.After(1f, new Action(() => { }));

            _driver.Advance(10); // 第 1 帧只定锚，其后 9 帧各推进 1/60 秒

            Assert.AreEqual(1f - 9f / 60f, handle.Remaining, 0.03f,
                "剩余量随逻辑时钟递减——这就是冷却读条的分母");

            handle.Dispose();
        }

        [Test]
        public void Remaining_ClampsAtZero_InsteadOfGoingNegative()
        {
            TimerHandle handle = TimerManager.After(0.05f, new Action(() => { }));
            _driver.Advance(60); // 早已经触发，槽位也已释放

            Assert.AreEqual(0f, handle.Remaining);
        }

        #endregion

        #region 时间轴

        /// <summary>
        /// 两条轴各走各的时钟：墙钟轴上的定时器不受逻辑轴冻结影响。
        /// </summary>
        [Test]
        public void ScaledAndUnscaled_KeepSeparateClocks()
        {
            var scaledFires = new TimerFires(_driver);
            int unscaledCount = 0;

            TimerManager.Every(Interval, scaledFires.Record);
            TimerManager.Every(Interval, () => unscaledCount++, UpdateTimeMode.Unscaled);

            _driver.Advance(2);
            for (int i = 0; i < 10; i++)
                _driver.AdvanceFrozen();

            Assert.AreEqual(0, scaledFires.Count, "逻辑轴冻结时受 timeScale 影响的定时器不得触发");
            Assert.GreaterOrEqual(unscaledCount, 1, "墙钟轴不受影响");
        }

        #endregion
    }
}
