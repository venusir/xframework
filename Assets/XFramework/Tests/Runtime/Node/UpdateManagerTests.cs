using System.Reflection;
using NUnit.Framework;
using UnityEngine;
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
            // 显式重建而非依赖 AutoInit 已经跑过：退出播放会把调度器置空（OnQuitting），
            // 而关闭域重载时静态字段不会复位，上一次会话的状态会残留到本次
            UpdateManager.AutoInit();
            Assert.IsTrue(UpdateManager.IsInitialized, "AutoInit 未生效，调度器不可用");
            UpdateManager.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            // 复原门面：下面有用例会把它置空，而 PlayMode 下所有 fixture 共享同一个 player
            UpdateManager.AutoInit();
            UpdateManager.Clear();
        }

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

        #region 自动生命周期（关闭域重载场景）

        [Test]
        public void AutoInit_HasRuntimeInitializeOnLoadMethod()
        {
            // 缺陷的核心不在方法的逻辑，而在它「什么时候被调用」：编辑器分支只挂
            // [InitializeOnLoadMethod] 时，关闭域重载后进入播放不会重新加载程序集，
            // 该方法再也不会执行——测试无法复现「进入播放两次」，只能直接锁定特性本身。
            var method = typeof(UpdateManager).GetMethod("AutoInit",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            Assert.IsNotNull(method, "AutoInit 应存在（internal 可见）");
            Assert.IsNotNull(method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>(),
                "AutoInit 必须挂 RuntimeInitializeOnLoadMethod，否则关闭域重载时不会再执行");
        }

        [Test]
        public void OnQuitting_ThenAutoInit_RebuildsScheduler()
        {
            // 关闭域重载时进入播放不重新加载程序集，[InitializeOnLoadMethod] 不再执行；
            // 旧实现又在此处置了不可逆闩锁，于是第二次进入播放后 IsInitialized 恒为 false，
            // Tick/Register/Enable/Disable/ProcessImmediate 全部静默 no-op
            var node = new TestUpdateable();
            UpdateManager.Register(node, depth: 0);

            UpdateManager.OnQuitting();
            Assert.IsFalse(UpdateManager.IsInitialized, "退出后调度器被释放");

            UpdateManager.AutoInit();
            Assert.IsTrue(UpdateManager.IsInitialized, "重建后必须可用");

            UpdateManager.Register(node, depth: 0);
            UpdateManager.Tick(time: 1.0f);
            Assert.AreEqual(1, node.UpdateCallCount, "重建后的调度器能正常派发");
        }

        [Test]
        public void AutoInit_CalledTwice_KeepsRegistrations()
        {
            // 开启域重载时进入播放会先后触发 InitializeOnLoadMethod 与 RuntimeInitializeOnLoadMethod，
            // 两次 AutoInit 之间可能夹着其它模块的注册（如 InputManager 的帧驱动）——
            // 无条件 new 会把它们所在的调度器整个换掉
            var node = new TestUpdateable();
            UpdateManager.Register(node, depth: 0);

            UpdateManager.AutoInit();

            Assert.AreEqual(1, UpdateManager.TotalCount, "重复 AutoInit 不应清掉已注册对象");
            UpdateManager.Tick(time: 1.0f);
            Assert.AreEqual(1, node.UpdateCallCount);
        }

        [Test]
        public void AfterQuitting_AllApiIsNoOp_UntilAutoInit()
        {
            // 宽容语义：退出流程中的调用静默返回而不抛异常——Tick 每帧都被调用，抛异常会打崩游戏循环
            var node = new TestUpdateable();
            UpdateManager.OnQuitting();

            Assert.DoesNotThrow(() =>
            {
                UpdateManager.Register(node, depth: 0);
                UpdateManager.Tick(time: 1.0f);
                UpdateManager.Unregister(node);
                UpdateManager.Enable(node);
                UpdateManager.Disable(node);
                UpdateManager.ProcessImmediate(node, 0.5f, 1.0f);
            });

            Assert.IsFalse(UpdateManager.IsEnabled(node));
            Assert.AreEqual(0, UpdateManager.TotalCount);
            Assert.AreEqual(0, UpdateManager.GetCount(UpdateLOD.Frame1));
            Assert.AreEqual(0, node.UpdateCallCount);

            UpdateManager.AutoInit();
            UpdateManager.Register(node, depth: 0);
            UpdateManager.Tick(time: 2.0f);
            Assert.AreEqual(1, node.UpdateCallCount, "重建后恢复派发");
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
