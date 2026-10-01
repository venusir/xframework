using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XUpdate;

namespace XFramework.XTimer.Tests
{
    /// <summary>
    /// 回调里的重入：创建、停自己、重开自己、释放自己、全停、抛异常。
    /// <para><b>这是本模块最容易出「静默错杀」的地方</b>：扫尾是一趟线性遍历，而回调可以在走访期间
    /// 释放并重建槽位——同一个下标很可能已经易主。收尾的每一步都必须用代际（+「是否仍已分配」）
    /// 重新确认目标，否则「停掉一个已经抛异常的定时器」会顺带把回调里刚建好的那个也放掉。</para>
    /// </summary>
    [TestFixture]
    public class TimerReentrancyTests
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

        #region 创建

        [Test]
        public void Callback_CanCreateAnotherTimer()
        {
            int inner = 0;
            TimerManager.After(0.05f, () => TimerManager.After(0.05f, () => inner++));

            _driver.Advance(20);

            Assert.AreEqual(1, inner);
        }

        /// <summary>
        /// 一次性回调里立刻新建定时器会<b>拿到刚释放的那个槽位</b>（自由链头就是它自己）。
        /// 本趟收尾必须靠代际认出「这下标已经易主」，不能拿旧下标继续做文章。
        /// </summary>
        [Test]
        public void OneShotCallback_CanReuseItsOwnSlot()
        {
            int second = 0;
            TimerManager.After(0.05f, () => TimerManager.After(0.05f, () => second++));

            _driver.Advance(20);

            Assert.AreEqual(1, second, "回调里重建的定时器被本趟收尾误杀了——代际守卫缺失");
        }

        #endregion

        #region 停 / 重开 / 释放自己

        [Test]
        public void RepeatingCallback_CanStopItself()
        {
            int count = 0;
            TimerHandle self = default;
            self = TimerManager.Every(0.05f, () =>
            {
                count++;
                self.Stop();
            });

            _driver.Advance(40);

            Assert.AreEqual(1, count);
            Assert.IsFalse(self.IsActive);
            Assert.IsTrue(self.Restart(), "自停之后槽位仍在，可以重开");
        }

        [Test]
        public void RepeatingCallback_CanRestartItself()
        {
            int count = 0;
            TimerHandle self = default;
            self = TimerManager.Every(0.05f, () =>
            {
                count++;
                if (count == 3)
                    self.Restart();
            });

            _driver.Advance(80);

            Assert.Greater(count, 3, "重开之后必须继续触发——本趟写在回调之前的「下一拍」不能把它顶掉");
        }

        [Test]
        public void OneShotCallback_SeesItsOwnHandleAlreadyInvalid()
        {
            bool activeInside = true;
            TimerHandle self = default;
            self = TimerManager.After(0.05f, () => activeInside = self.IsActive);

            _driver.Advance(10);

            Assert.IsFalse(activeInside, "一次性定时器先释放槽位再回调，回调里看到的自己是已失效句柄");
            Assert.IsFalse(self.IsActive);
        }

        /// <summary>
        /// 回调里全停。
        /// <para><b>同一趟扫尾里已经被走访过的定时器仍会触发</b>——全停释放的是剩余槽位，不是「撤销本趟」。
        /// 槽位下标由自由链决定（LIFO，历次释放会反转顺序），因此不能假设「谁先建谁先被走访」；
        /// 这条用例断言的是真正的承诺：<b>全停之后不再有新的触发</b>。</para>
        /// </summary>
        [Test]
        public void Callback_CanCancelAll()
        {
            bool cancelled = false;
            int other = 0;

            TimerManager.After(0.05f, () =>
            {
                cancelled = true;
                TimerManager.CancelAll();
            });
            TimerManager.After(0.05f, () => other++);

            _driver.Advance(20);

            Assert.IsTrue(cancelled);
            Assert.LessOrEqual(other, 1, "全停之前已被走访的至多再触发一次");

            int afterCancel = other;
            _driver.Advance(20);

            Assert.AreEqual(afterCancel, other, "全停之后不应再有任何触发");
            Assert.AreEqual(0, TimerManager.ActiveCount);
        }

        #endregion

        #region 异常隔离

        [Test]
        public void ThrowingCallback_StopsOnlyThatTimer()
        {
            int other = 0;
            TimerManager.After(0.05f, () => throw new InvalidOperationException("boom"));
            TimerManager.Every(0.05f, () => other++);

            LogAssert.Expect(LogType.Error, new Regex(@"\[Timer\] 定时器回调抛出异常"));
            _driver.Advance(20);

            Assert.GreaterOrEqual(other, 1, "同一条轴上的其它定时器必须照常触发");
            Assert.IsTrue(TimerManager.IsTickerRegistered(UpdateTimeMode.Scaled),
                "驱动器绝不能把异常漏给 Update——那会被判成坏节点并永久注销，等于该轴全部定时器静默死亡");
        }

        [Test]
        public void ThrowingRepeatingCallback_IsStoppedInsteadOfSpamming()
        {
            int bad = 0;
            TimerHandle handle = TimerManager.Every(0.05f, () =>
            {
                bad++;
                throw new InvalidOperationException("boom");
            });

            LogAssert.Expect(LogType.Error, new Regex(@"\[Timer\] 定时器回调抛出异常"));
            _driver.Advance(40);

            Assert.AreEqual(1, bad, "抛异常的固定间隔定时器必须被停掉，否则就是每拍一条日志");
            Assert.IsFalse(handle.IsActive);
        }

        /// <summary>
        /// 「异常即停表」的收尾必须在<b>代际仍然相符</b>时才执行。
        /// <para>回调里先释放自己、再建一个新定时器（必然复用同一槽位）、然后抛异常——这是收尾最容易
        /// 误伤他人的一步：少了代际判定，新定时器会被当成「刚才那个抛异常的」一并释放掉，
        /// 而症状是「刚建的定时器莫名其妙不触发」，极难定位。</para>
        /// </summary>
        [Test]
        public void ThrowingCallback_DoesNotKillTheTimerItJustCreated()
        {
            int second = 0;
            TimerHandle self = default;
            self = TimerManager.Every(0.05f, () =>
            {
                self.Dispose();
                TimerManager.After(0.05f, () => second++);
                throw new InvalidOperationException("boom");
            });

            LogAssert.Expect(LogType.Error, new Regex(@"\[Timer\] 定时器回调抛出异常"));
            _driver.Advance(20);

            Assert.AreEqual(1, second, "收尾的「异常即释放」把回调里刚建好的定时器一起放掉了——代际守卫缺失");
        }

        #endregion
    }
}
