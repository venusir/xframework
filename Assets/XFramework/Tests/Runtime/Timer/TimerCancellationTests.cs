using System;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XUpdate;

namespace XFramework.XTimer.Tests
{
    /// <summary>
    /// 取消令牌：取消即自动停表并回收槽位，可直接传 MonoBehaviour 的 <c>destroyCancellationToken</c>。
    /// <para><b>实现上没有调 <c>CancellationToken.Register</c></b>：令牌随槽位存储，在扫尾与各查询点上直接读
    /// <c>IsCancellationRequested</c>。于是「不传令牌时零额外分配」是结构性的（连 <c>Register</c> 自身的
    /// 那次分配都不存在），也不必处理「令牌回调在别的线程上跑、而活表只能主线程改」这条竞态。</para>
    /// <para>本 fixture 锁的是它对使用方的<b>承诺</b>：句柄的 <c>IsActive</c> 立刻为 false、回调不再触发、
    /// 槽位最终被回收、以及重开之后令牌依旧管用。</para>
    /// </summary>
    [TestFixture]
    public class TimerCancellationTests
    {
        #region Test Doubles

        /// <summary>只为拿一个真实的 <c>destroyCancellationToken</c>。</summary>
        private sealed class Host : MonoBehaviour
        {
        }

        #endregion

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

        #region 运行中被取消

        [Test]
        public void CancellingWhileRunning_DeactivatesImmediatelyAndReclaimsTheSlot()
        {
            using var cts = new CancellationTokenSource();
            int fired = 0;
            TimerHandle handle = TimerManager.After(1f, () => fired++, cancellationToken: cts.Token);

            Assert.IsTrue(handle.IsActive);

            cts.Cancel();

            // 还没有任何派发趟经过——「立刻」指的是查询层面，不是「等下一拍扫尾」
            Assert.IsFalse(handle.IsActive, "令牌一取消，句柄就该报不在计时");
            Assert.AreEqual(0f, handle.Remaining);

            _driver.Advance(90); // 1.5 秒

            Assert.AreEqual(0, fired, "已取消的定时器不得触发");
            Assert.AreEqual(0, TimerManager.ActiveCount, "槽位应在下一趟扫尾里被回收");
            Assert.IsFalse(TimerManager.IsTickerRegistered(UpdateTimeMode.Scaled), "回收干净后驱动器退出调度");
        }

        [Test]
        public void CancellingWhileRunning_MakesStopAndRestartNoOps()
        {
            using var cts = new CancellationTokenSource();
            TimerHandle handle = TimerManager.After(1f, Noop, cancellationToken: cts.Token);
            cts.Cancel();

            Assert.IsFalse(handle.Stop(), "已经不算在计时的东西停不动");
            Assert.IsFalse(handle.Restart(), "令牌已取消，重开也活不过来");
        }

        /// <summary>
        /// <c>Stop</c> 不解除令牌约束：停表只是暂停计时，令牌仍随槽位保存着。
        /// </summary>
        [Test]
        public void Stop_KeepsTheTokenBinding()
        {
            using var cts = new CancellationTokenSource();
            TimerHandle handle = TimerManager.After(1f, Noop, cancellationToken: cts.Token);
            handle.Stop();

            cts.Cancel();

            Assert.IsFalse(handle.Restart(), "停表期间令牌被取消，重开必须失败");
        }

        /// <summary>
        /// <c>Restart</c> 同样不解除约束——它的契约是「用原时长重新起算」，不是「换一条命」。
        /// </summary>
        [Test]
        public void Restart_KeepsTheTokenBinding()
        {
            using var cts = new CancellationTokenSource();
            TimerHandle handle = TimerManager.Every(0.05f, Noop, cancellationToken: cts.Token);

            _driver.Advance(6);
            Assert.IsTrue(handle.Restart(), "令牌还在，重开应当成功");

            cts.Cancel();

            Assert.IsFalse(handle.IsActive);
        }

        #endregion

        #region 生命周期绑定

        /// <summary>
        /// 直接传 <c>MonoBehaviour.destroyCancellationToken</c>：对象销毁即定时器消失，
        /// 不需要调用方在 <c>OnDestroy</c> 里逐个 <c>Dispose</c>。
        /// <para>这是框架「订阅随生命周期自动取消」那条约定在定时器上的形态。</para>
        /// </summary>
        [Test]
        public void DestroyCancellationToken_StopsTheTimerWithItsOwner()
        {
            var go = new GameObject("timer-cancellation-host");
            var host = go.AddComponent<Host>();

            int fired = 0;
            TimerHandle handle = TimerManager.After(0.05f, () => fired++,
                cancellationToken: host.destroyCancellationToken);

            Assert.IsTrue(handle.IsActive);

            UnityEngine.Object.DestroyImmediate(go);

            Assert.IsFalse(handle.IsActive, "宿主销毁后定时器立刻失效");

            _driver.Advance(10);

            Assert.AreEqual(0, fired, "宿主销毁后回调不得再跑——它多半会摸到已销毁的成员");
        }

        #endregion
    }
}
