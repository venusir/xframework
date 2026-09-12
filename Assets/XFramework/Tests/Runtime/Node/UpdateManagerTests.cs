using NUnit.Framework;
using XFramework.XUpdate;

namespace XFramework.XUpdate.Tests
{
    /// <summary>
    /// <see cref="UpdateManager"/> 的生命周期语义测试。
    /// <para>核心是锁定「<see cref="UpdateManager.Clear"/> 不是终态」——修复前它叫 <c>Destroy</c>
    /// 且会置单向闩锁并丢弃调度器，调用一次即让同一 play 会话内后续所有注册静默失效，
    /// 与它自己「主要用于单元测试隔离」的文档自相矛盾。</para>
    /// </summary>
    [TestFixture]
    public class UpdateManagerTests
    {
        #region Test Doubles

        private sealed class TestUpdateable : IUpdateable
        {
            public int UpdateCallCount;
            public UpdateLOD NextLOD = UpdateLOD.Frame1;

            public void OnEnable() { }

            public void OnDisable() { }

            public UpdateLOD OnUpdate(float deltaTime, float time)
            {
                UpdateCallCount++;
                return NextLOD;
            }
        }

        #endregion

        #region Fixture

        [SetUp]
        public void SetUp()
        {
            // 本 fixture 依赖 AutoInit 已运行（RuntimeInitializeOnLoadMethod / InitializeOnLoadMethod）。
            // 显式断言而非静默失败：若调度器不可用，下面的用例会以难以诊断的方式全红
            Assert.IsTrue(UpdateManager.IsInitialized, "AutoInit 未运行，调度器不可用");
            UpdateManager.Clear();
        }

        [TearDown]
        public void TearDown() => UpdateManager.Clear();

        #endregion

        #region Clear 的语义

        [Test]
        public void Clear_RemovesAllRegistrations()
        {
            UpdateManager.Register(new TestUpdateable(), depth: 0);
            Assert.AreEqual(1, UpdateManager.TotalCount);

            UpdateManager.Clear();

            Assert.AreEqual(0, UpdateManager.TotalCount);
        }

        [Test]
        public void Clear_ThenRegister_StillWorks()
        {
            // 修复前：清空后 _shutdown 恒为 true，Register 开头的守卫直接 return，
            // 「隔离」用的调用反而把管理器废掉了
            var node = new TestUpdateable();

            UpdateManager.Clear();
            UpdateManager.Register(node, depth: 0);

            Assert.AreEqual(1, UpdateManager.TotalCount, "清空后仍可继续注册");
        }

        [Test]
        public void Clear_ThenTick_StillDispatches()
        {
            var node = new TestUpdateable();

            UpdateManager.Clear();
            UpdateManager.Register(node, depth: 0);
            UpdateManager.Tick(time: 1.0f);

            Assert.AreEqual(1, node.UpdateCallCount, "清空后 Tick 仍能派发到已注册对象");
        }

        [Test]
        public void Clear_CalledTwice_DoesNotThrow()
        {
            UpdateManager.Register(new TestUpdateable(), depth: 0);

            Assert.DoesNotThrow(() =>
            {
                UpdateManager.Clear();
                UpdateManager.Clear();
            });
            Assert.AreEqual(0, UpdateManager.TotalCount);
        }

        [Test]
        public void Clear_KeepsManagerInitialized()
        {
            UpdateManager.Clear();

            Assert.IsTrue(UpdateManager.IsInitialized, "Clear 是「清空注册」而非「销毁」，管理器仍可用");
        }

        #endregion

        #region 注册与注销

        [Test]
        public void Register_NullNode_IsIgnored()
        {
            Assert.DoesNotThrow(() => UpdateManager.Register(null, depth: 0));
            Assert.AreEqual(0, UpdateManager.TotalCount);
        }

        [Test]
        public void Unregister_RemovesFromCount()
        {
            var node = new TestUpdateable();
            UpdateManager.Register(node, depth: 0);
            Assert.AreEqual(1, UpdateManager.TotalCount);

            UpdateManager.Unregister(node);

            Assert.AreEqual(0, UpdateManager.TotalCount);
        }

        [Test]
        public void Disable_ExcludesFromTotalCount_EnableRestores()
        {
            var node = new TestUpdateable();
            UpdateManager.Register(node, depth: 0);

            UpdateManager.Disable(node);
            Assert.AreEqual(0, UpdateManager.TotalCount, "禁用对象不计入总数");
            Assert.IsFalse(UpdateManager.IsEnabled(node));

            UpdateManager.Enable(node);
            Assert.AreEqual(1, UpdateManager.TotalCount);
            Assert.IsTrue(UpdateManager.IsEnabled(node));
        }

        #endregion
    }
}
