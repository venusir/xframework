using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XUpdate;

namespace XFramework.XUpdate.Tests
{
    /// <summary>
    /// 手动驱动（不依赖 PlayerLoop 注入）的语义测试。
    /// <para><b>核心：</b>无参 <see cref="UpdateManager.Tick()"/> 必须与自动驱动等价。注入失败时 README
    /// 指引使用方自行每帧驱动，而原先唯一可用的是 <c>Tick(time)</c>——它只有一条时间源，既表达不出
    /// <c>timeScale = 0</c> 的冻结，也给不出独立的墙钟时刻，于是那条自救路径上的行为与自动驱动不一致。</para>
    /// <para>本 fixture 关闭自动驱动以保证确定性，并会临时改动 <see cref="Time.timeScale"/>：
    /// 收尾必须复位，否则 PlayMode 下所有 fixture 共享一个 player 实例，污染会一路传下去。</para>
    /// </summary>
    [TestFixture]
    public class UpdateManualDriveTests
    {
        #region Test Doubles

        private sealed class CountingUpdateable : IUpdateable
        {
            public int UpdateCount { get; private set; }
            public float LastDelta { get; private set; }

            /// <summary>最近一次派发拿到的时刻——用于区分「这条轴取的是哪个时间源」。</summary>
            public float LastTime { get; private set; }

            public void OnEnable() { }

            public void OnDisable() { }

            public UpdateTier OnUpdate(float deltaTime, float time)
            {
                UpdateCount++;
                LastDelta = deltaTime;
                LastTime = time;
                return UpdateTier.Tier0;
            }
        }

        private sealed class CountingLateUpdateable : ILateUpdateable
        {
            public int LateCount { get; private set; }

            public void OnEnable() { }

            public void OnDisable() { }

            public UpdateTier OnLateUpdate(float deltaTime, float time)
            {
                LateCount++;
                return UpdateTier.Tier0;
            }
        }

        #endregion

        #region Fixture

        private float _originalTimeScale;

        [SetUp]
        public void SetUp()
        {
            _originalTimeScale = Time.timeScale;
            UpdateManager.AutoDriveEnabled = false;
            UpdateManager.AutoInit();
            UpdateManager.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = _originalTimeScale;
            UpdateManager.AutoDriveEnabled = true;
            UpdateManager.AutoInit();
            UpdateManager.Clear();
        }

        #endregion

        #region 无参重载（与自动驱动等价）

        [Test]
        public void Tick_DrivesBothVariableStepTimings()
        {
            var update = new CountingUpdateable();
            var late = new CountingLateUpdateable();
            UpdateManager.Register(update, order: 0);
            UpdateManager.RegisterLate(late, order: 0);

            UpdateManager.Tick();

            Assert.AreEqual(1, update.UpdateCount, "无参 Tick 应驱动 Update 时机");
            Assert.AreEqual(1, late.LateCount, "无参 Tick 也应驱动 LateUpdate 时机");
        }

        [Test]
        public void Tick_TimeScaleZero_FreezesLogicalAxisOnly()
        {
            // 暂停菜单、UI 动画这类逻辑正是靠「逻辑轴冻结、墙钟轴照常」活着。注入失败时若按 README
            // 旧指引改用 Tick(time)，这条语义整个失效——用户看不到任何报错，只看到暂停后动画也停了
            var scaled = new CountingUpdateable();
            var unscaled = new CountingUpdateable();
            UpdateManager.Register(scaled, order: 0);
            UpdateManager.Register(unscaled, order: 0, timeMode: UpdateTimeMode.Unscaled);

            Time.timeScale = 0f;
            UpdateManager.Tick();

            Assert.AreEqual(0, scaled.UpdateCount, "timeScale = 0 时逻辑轴不派发");
            Assert.AreEqual(1, unscaled.UpdateCount, "墙钟轴不受 timeScale 影响");
        }

        [Test]
        public void Tick_Tier0NodeDeltaIsRealElapsedTime()
        {
            var node = new CountingUpdateable();
            UpdateManager.Register(node, order: 0);

            UpdateManager.Tick();
            Assert.AreEqual(0f, node.LastDelta, 1e-6f, "首帧只锚定，delta 记 0（与自动驱动一致）");
            Assert.AreEqual(1, node.UpdateCount);
        }

        [Test]
        public void TickWithClock_DrivesBothAxesAndLateTiming()
        {
            // 门面的 Tick(UpdateClock) 此前没有被用例覆盖：两条轴各拿自己的时刻（这是它相对于
            // Tick(time) 的全部意义），注册在 LateUpdate 的节点也由同一次 Tick 驱动
            var scaled = new CountingUpdateable();
            var unscaled = new CountingUpdateable();
            var late = new CountingLateUpdateable();
            UpdateManager.Register(scaled, order: 0);
            UpdateManager.Register(unscaled, order: 0, timeMode: UpdateTimeMode.Unscaled);
            UpdateManager.RegisterLate(late, order: 0);

            UpdateManager.Tick(new UpdateClock(time: 3.0, unscaledTime: 7.0));

            Assert.AreEqual(1, scaled.UpdateCount);
            Assert.AreEqual(1, unscaled.UpdateCount);
            Assert.AreEqual(1, late.LateCount, "LateUpdate 时机由同一次 Tick 驱动");
            Assert.AreEqual(3f, scaled.LastTime, 1e-6f, "逻辑轴取 Time");
            Assert.AreEqual(7f, unscaled.LastTime, 1e-6f, "墙钟轴取 UnscaledTime");
        }

        [UnityTest]
        public IEnumerator AutoDriveDisabled_InjectionDoesNotDispatch()
        {
            // AutoDriveEnabled 是给测试用的开关（精确计数断言会被自动驱动打乱），它的守卫
            // 此前没有用例：本 fixture 关掉它、不手动 Tick，靠 PlayerLoop 自动跑几帧，
            // 派发次数必须一动不动
            var node = new CountingUpdateable();
            UpdateManager.Register(node, order: 0);

            for (int i = 0; i < 3; i++)
            {
                yield return null;
            }

            Assert.AreEqual(0, node.UpdateCount, "AutoDriveEnabled = false 时注入的驱动不得派发");
        }

        #endregion

        #region 单时间源重载的既有语义（本 fixture 锁住，免得被「顺手统一」掉）

        [Test]
        public void TickWithSingleTimeSource_DoesNotSeeFreeze()
        {
            // Tick(time) 自带时刻、不读 timeScale，这是它的定位（测试与确定性回放），不是缺陷：
            // 于是 timeScale = 0 时逻辑轴照常派发。手动驱动请用无参 Tick()——两条重载的差异在
            // XML doc 与 README 里都写明，本用例把它变成可执行的事实
            var scaled = new CountingUpdateable();
            UpdateManager.Register(scaled, order: 0);

            Time.timeScale = 0f;
            UpdateManager.Tick(time: 5.0f);

            Assert.AreEqual(1, scaled.UpdateCount, "单时间源重载不读 timeScale，故不冻结");
        }

        #endregion
    }
}
