using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using XFramework.XUpdate;

namespace XFramework.XUpdate.Tests
{
    /// <summary>
    /// PlayerLoop 注入驱动的集成测试。
    /// <para><b>本 fixture 刻意不禁用自动驱动</b>（其它 Update 侧 fixture 会禁用，以保证精确计数）：
    /// 它要验证的正是「不手动 Tick 也会被派发」。</para>
    /// </summary>
    public class UpdatePlayerLoopTests
    {
        [SetUp]
        public void SetUp()
        {
            UpdateManager.AutoDriveEnabled = true;
            UpdateManager.AutoInit();
            UpdateManager.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            UpdateManager.AutoDriveEnabled = true;
            UpdateManager.Clear();
        }

        [Test]
        public void AutoDrive_IsInjectedIntoPlayerLoop()
        {
            Assert.IsTrue(UpdateManager.IsDrivingPlayerLoop,
                "驱动系统应已注入 PlayerLoop——注入失败时不会有任何东西每帧调用 Tick");
        }

        [Test]
        public void Injection_DoesNotClobberUniTask()
        {
            // 本框架依赖 UniTask，而它正是靠注入 PlayerLoop 工作的：注入必须基于
            // GetCurrentPlayerLoop 而非 GetDefaultPlayerLoop，否则会把 UniTask 的 runner 冲掉。
            // 该约束从玩法侧很难察觉（异步只是静默不推进），故直接把两套注入并存钉成断言
            Assert.IsTrue(PlayerLoopHelper.IsInjectedUniTaskPlayerLoop(),
                "UniTask 的 PlayerLoop 注入应仍然存在");
            Assert.IsTrue(UpdateManager.IsDrivingPlayerLoop);
        }

        [UnityTest]
        public IEnumerator RegisteredNode_IsDrivenWithoutManualTick()
        {
            var node = new DrivenNode();
            UpdateManager.Register(node, depth: 0);
            Assert.AreEqual(0, node.UpdateCount, "注册本身不派发");

            // 不调用 UpdateManager.Tick：靠注入的 PlayerLoop 驱动
            for (int i = 0; i < 5 && node.UpdateCount == 0; i++)
            {
                yield return null;
            }

            Assert.GreaterOrEqual(node.UpdateCount, 1, "注入的驱动应每帧自动派发");

            UpdateManager.Unregister(node);
            yield break;
        }

        /// <summary>
        /// 供驱动测试使用的最小可更新对象。
        /// </summary>
        private sealed class DrivenNode : IUpdateable
        {
            public int UpdateCount { get; private set; }

            public void OnEnable() { }

            public void OnDisable() { }

            public UpdateLOD OnUpdate(float deltaTime, float time)
            {
                UpdateCount++;
                return UpdateLOD.Frame1;
            }
        }
    }
}
