using System;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XUpdate;

namespace XFramework.XTimer.Tests
{
    /// <summary>
    /// 创建 / 停止 / 重开 / 释放 / 全停的生命周期语义，以及驱动器随活跃数进退调度的行为。
    /// <para><b>驱动器退出调度是本模块「装了不用不花钱」的实现方式</b>：一个正在计时的定时器都没有时，
    /// 它把自己从 Update 注册表里摘掉；下一次创建再挂回去。反过来，若哪天它退不掉，框架就会永远多出
    /// 每帧一次的空扫尾——本 fixture 把这条变成可执行的约束。</para>
    /// </summary>
    [TestFixture]
    public class TimerLifecycleTests
    {
        #region Fixture

        private static readonly Action Noop = () => { };

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

        #region 创建与驱动器注册

        [Test]
        public void Create_RegistersTickerOnTheUsedAxisOnly()
        {
            TimerHandle handle = TimerManager.After(1f, Noop);

            Assert.IsTrue(handle.IsActive);
            Assert.AreEqual(1, TimerManager.ActiveCount);
            Assert.IsTrue(TimerManager.IsTickerRegistered(UpdateTimeMode.Scaled));
            Assert.IsFalse(TimerManager.IsTickerRegistered(UpdateTimeMode.Unscaled),
                "只用了逻辑轴，墙钟轴的驱动器不该被建起来");

            handle.Dispose();
        }

        [Test]
        public void Create_Unscaled_RegistersTheOtherAxis()
        {
            TimerHandle handle = TimerManager.After(1f, Noop, UpdateTimeMode.Unscaled);

            Assert.IsTrue(TimerManager.IsTickerRegistered(UpdateTimeMode.Unscaled));
            Assert.IsFalse(TimerManager.IsTickerRegistered(UpdateTimeMode.Scaled));

            handle.Dispose();
        }

        [Test]
        public void Remaining_RightAfterCreate_IsTheDelayItself()
        {
            TimerHandle handle = TimerManager.After(1f, Noop);

            Assert.AreEqual(1f, handle.Remaining, 1e-3f,
                "还没被扫到的槽位用时长作答——「创建后立刻读冷却读条」拿到的应是满格");

            handle.Dispose();
        }

        #endregion

        #region 停止 / 重开 / 释放

        [Test]
        public void Stop_KeepsHandleAliveButTakesItOutOfTheCount()
        {
            TimerHandle handle = TimerManager.After(1f, Noop);

            Assert.IsTrue(handle.Stop());
            Assert.IsFalse(handle.IsActive);
            Assert.AreEqual(0f, handle.Remaining);
            Assert.AreEqual(0, TimerManager.ActiveCount);
            Assert.IsFalse(TimerManager.IsTickerRegistered(UpdateTimeMode.Scaled));
            Assert.IsFalse(handle.Stop(), "重复 Stop 是安全的空操作");

            Assert.IsTrue(handle.Restart(), "停表保留了槽位，因此可以重开");
            Assert.IsTrue(handle.IsActive);
            Assert.AreEqual(1f, handle.Remaining, 1e-3f);
            Assert.IsTrue(TimerManager.IsTickerRegistered(UpdateTimeMode.Scaled));
        }

        [Test]
        public void Dispose_ReleasesSlotAndInvalidatesHandle()
        {
            TimerHandle handle = TimerManager.After(1f, Noop);

            handle.Dispose();

            Assert.IsFalse(handle.IsActive);
            Assert.AreEqual(0, TimerManager.ActiveCount);
            Assert.IsFalse(TimerManager.IsTickerRegistered(UpdateTimeMode.Scaled));
            Assert.IsFalse(handle.Stop(), "已释放的句柄再也停不动任何东西");
            Assert.IsFalse(handle.Restart(), "已释放的槽位不会被重开");
        }

        [Test]
        public void OneShot_StopsCountingAfterItFires()
        {
            int count = 0;
            TimerHandle handle = TimerManager.After(0.1f, () => count++);

            _driver.Advance(12);

            Assert.AreEqual(1, count);
            Assert.IsFalse(handle.IsActive, "一次性触发即释放槽位，句柄随之失效");
            Assert.AreEqual(0, TimerManager.ActiveCount);
            Assert.IsFalse(TimerManager.IsTickerRegistered(UpdateTimeMode.Scaled));
        }

        [Test]
        public void Slots_AreReusedAcrossManyCreateDisposeCycles()
        {
            for (int i = 0; i < 100; i++)
                TimerManager.After(1f, Noop).Dispose();

            Assert.AreEqual(0, TimerManager.ActiveCount);

            // 复用过的槽位仍要能正常触发（代际判定不能把新定时器一起挡掉）
            int fired = 0;
            TimerManager.After(0.05f, () => fired++);
            _driver.Advance(6);

            Assert.AreEqual(1, fired);
        }

        #endregion

        #region CancelAll

        [Test]
        public void CancelAll_ReleasesEverythingAndLetsTheModuleRestart()
        {
            TimerHandle running = TimerManager.After(5f, Noop);
            TimerHandle unscaled = TimerManager.Every(5f, Noop, UpdateTimeMode.Unscaled);
            TimerHandle stopped = TimerManager.After(5f, Noop);
            stopped.Stop();

            Assert.AreEqual(2, TimerManager.CancelAll(), "已停止的不计入返回值，但同样被释放");
            Assert.AreEqual(0, TimerManager.ActiveCount);
            Assert.IsFalse(running.IsActive);
            Assert.IsFalse(unscaled.IsActive);
            Assert.IsFalse(stopped.IsActive, "已停止的槽位也被一并释放");
            Assert.IsFalse(stopped.Restart(), "全停之后连重开都不行——那是彻底复位");
            Assert.IsFalse(TimerManager.IsTickerRegistered(UpdateTimeMode.Scaled));
            Assert.IsFalse(TimerManager.IsTickerRegistered(UpdateTimeMode.Unscaled));

            Assert.AreEqual(0, TimerManager.CancelAll(), "重复全停是安全的");

            int fired = 0;
            TimerManager.After(0.05f, () => fired++);
            _driver.Advance(6);
            Assert.AreEqual(1, fired, "彻底复位之后仍可继续创建定时器");
        }

        #endregion

        #region 参数防御

        [Test]
        public void NullCallback_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => TimerManager.After(1f, (Action)null));
            Assert.Throws<ArgumentNullException>(() => TimerManager.Every(1f, (Action)null));
            Assert.Throws<ArgumentNullException>(() => TimerManager.After(1f, (Action<int>)null, 1));
            Assert.Throws<ArgumentNullException>(() => TimerManager.Every(1f, (Action<int>)null, 1));
        }

        [Test]
        public void InvalidDuration_Throws()
        {
            // 一次性允许 0（「下一拍」），但不允许负、NaN、无穷
            Assert.DoesNotThrow(() => TimerManager.After(0f, Noop).Dispose());
            Assert.Throws<ArgumentOutOfRangeException>(() => TimerManager.After(-0.01f, Noop));
            Assert.Throws<ArgumentOutOfRangeException>(() => TimerManager.After(float.NaN, Noop));
            Assert.Throws<ArgumentOutOfRangeException>(() => TimerManager.After(float.PositiveInfinity, Noop));

            // 固定间隔必须为正
            Assert.Throws<ArgumentOutOfRangeException>(() => TimerManager.Every(0f, Noop));
            Assert.Throws<ArgumentOutOfRangeException>(() => TimerManager.Every(-1f, Noop));
            Assert.Throws<ArgumentOutOfRangeException>(() => TimerManager.Every(float.NaN, Noop));
            Assert.Throws<ArgumentOutOfRangeException>(() => TimerManager.Every(float.PositiveInfinity, Noop));
        }

        [Test]
        public void InvalidTimeMode_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => TimerManager.After(1f, Noop, (UpdateTimeMode)2));
            Assert.Throws<ArgumentOutOfRangeException>(() => TimerManager.After(1f, Noop, (UpdateTimeMode)(-1)));
        }

        #endregion

        #region 创建时令牌已被取消

        /// <summary>
        /// 令牌已取消时创建：<b>告警并返回默认句柄，不抛也不同步回调</b>。
        /// <para>抛异常会让「随对象销毁自动停表」这类统一收尾代码不得不处处包 try；
        /// 同步回调则会制造「本方法还没返回就进了用户代码」的重入。两者都更糟。</para>
        /// </summary>
        [Test]
        public void Create_WithAlreadyCancelledToken_ReturnsDefaultAndWarns()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            LogAssert.Expect(LogType.Warning, "[Timer] 取消令牌在创建前已被取消，本次创建被忽略并返回默认句柄。");
            TimerHandle handle = TimerManager.After(1f, Noop, cancellationToken: cts.Token);

            Assert.IsTrue(handle.IsDefault);
            Assert.AreEqual(0, TimerManager.ActiveCount);
            Assert.IsFalse(TimerManager.IsTickerRegistered(UpdateTimeMode.Scaled),
                "被忽略的创建不该把驱动器拉起来");
        }

        #endregion
    }
}
