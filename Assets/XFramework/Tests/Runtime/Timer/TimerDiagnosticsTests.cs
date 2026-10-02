using System;
using System.Collections.Generic;
using NUnit.Framework;
using XFramework.XTimer;

namespace XFramework.XTimer.Tests
{
    /// <summary>
    /// <see cref="TimerManager.CopyActiveTimers"/>：占用槽位的定时器明细。
    /// <para>TimerManager 是静态服务，SetUp/TearDown 都 <c>CancelAll</c> 复位。</para>
    /// </summary>
    [TestFixture]
    public class TimerDiagnosticsTests
    {
        [SetUp]
        public void SetUp()
        {
            TimerManager.CancelAll();
        }

        [TearDown]
        public void TearDown()
        {
            TimerManager.CancelAll();
        }

        [Test]
        public void CopyActiveTimers_ListsRunningTimer()
        {
            TimerManager.After(5f, () => { });

            var buffer = new List<TimerInfo>();
            int count = TimerManager.CopyActiveTimers(buffer);

            Assert.AreEqual(buffer.Count, count);
            Assert.AreEqual(1, count);

            var info = buffer[0];
            Assert.IsTrue(info.IsRunning);
            Assert.IsFalse(info.IsRepeating);
            Assert.AreEqual(5f, info.Duration, 0.001f);
            Assert.Greater(info.Remaining, 0f);
            Assert.GreaterOrEqual(info.Slot, 0);
            Assert.IsNotNull(info.CallbackName);
        }

        [Test]
        public void CopyActiveTimers_ReportsStoppedButAllocatedSlot()
        {
            var handle = TimerManager.After(5f, () => { });
            handle.Stop();

            var buffer = new List<TimerInfo>();
            TimerManager.CopyActiveTimers(buffer);

            Assert.AreEqual(1, buffer.Count, "停表但未释放的槽位仍应可见——那正是「停掉的定时器占着槽位」的唯一可见面");
            Assert.IsFalse(buffer[0].IsRunning);
            Assert.AreEqual(0f, buffer[0].Remaining, 0.001f);
        }

        [Test]
        public void CopyActiveTimers_ClearsBufferFirst()
        {
            var buffer = new List<TimerInfo>();
            TimerManager.After(1f, () => { });
            TimerManager.CopyActiveTimers(buffer);
            Assert.AreEqual(1, buffer.Count);

            buffer.Add(default);
            TimerManager.CancelAll();
            TimerManager.CopyActiveTimers(buffer);

            Assert.AreEqual(0, buffer.Count);
        }

        [Test]
        public void CopyActiveTimers_NullBuffer_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => TimerManager.CopyActiveTimers(null));
        }
    }
}
