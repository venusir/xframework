using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
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

        [UnityTest]
        public IEnumerator LateUpdateTiming_RunsAfterUpdateTiming()
        {
            // LateUpdate 时机的意义就是「本帧所有 Update 都跑完了」（跟随移动目标、相机跟随）。
            // 用同一对象在两个时机上的回调顺序钉住这条：注入点分别落在 PlayerLoop 的
            // Update.ScriptRunBehaviourUpdate 与 PreLateUpdate.ScriptRunBehaviourLateUpdate
            var node = new BothTimingsNode();
            UpdateManager.Register(node, depth: 0);
            UpdateManager.RegisterLate(node, depth: 0);

            for (int i = 0; i < 5 && node.Sequence.Count < 4; i++)
            {
                yield return null;
            }

            UpdateManager.Unregister(node);

            // 注册发生在测试续体里、可能晚于本帧的 Update 驱动，故不假设序列从 "update" 开头——
            // 只要求「某个 update 之后紧跟的是 late」
            int firstUpdate = node.Sequence.IndexOf("update");
            Assert.GreaterOrEqual(firstUpdate, 0, "Update 时机应被驱动");
            Assert.GreaterOrEqual(node.Sequence.Count, firstUpdate + 2, "LateUpdate 时机应被驱动");
            Assert.AreEqual("late", node.Sequence[firstUpdate + 1], "LateUpdate 时机必须排在 Update 之后");
        }

        [UnityTest]
        public IEnumerator FixedTiming_IsDrivenByPlayerLoop()
        {
            var node = new FixedDrivenNode();
            UpdateManager.RegisterFixed(node, depth: 0);

            // 不手动 Tick：靠注入到 FixedUpdate 阶段的驱动。
            // 必须等 WaitForFixedUpdate 而不是 yield return null——批处理下帧率极高，
            // 若干帧可能还凑不满一个固定步（0.02s），那样会误判成「驱动没生效」
            for (int i = 0; i < 10 && node.FixedCount == 0; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.GreaterOrEqual(node.FixedCount, 1, "固定步长时机应被注入的驱动推进");

            UpdateManager.Unregister(node);
            yield break;
        }

        /// <summary>
        /// 只实现固定步长时机的测试对象。
        /// </summary>
        private sealed class FixedDrivenNode : IFixedUpdateable
        {
            public int FixedCount { get; private set; }

            public void OnEnable() { }

            public void OnDisable() { }

            public UpdateTier OnFixedUpdate(float deltaTime, float fixedTime)
            {
                FixedCount++;
                return UpdateTier.Tier0;
            }
        }

        /// <summary>
        /// 同时挂在两个时机上的测试对象，按调用顺序记录序列。
        /// </summary>
        private sealed class BothTimingsNode : IUpdateable, ILateUpdateable
        {
            public System.Collections.Generic.List<string> Sequence { get; } =
                new System.Collections.Generic.List<string>(8);

            public void OnEnable() { }

            public void OnDisable() { }

            public UpdateTier OnUpdate(float deltaTime, float time)
            {
                Sequence.Add("update");
                return UpdateTier.Tier0;
            }

            public UpdateTier OnLateUpdate(float deltaTime, float time)
            {
                Sequence.Add("late");
                return UpdateTier.Tier0;
            }
        }

        /// <summary>
        /// 供驱动测试使用的最小可更新对象。
        /// </summary>
        private sealed class DrivenNode : IUpdateable
        {
            public int UpdateCount { get; private set; }

            public void OnEnable() { }

            public void OnDisable() { }

            public UpdateTier OnUpdate(float deltaTime, float time)
            {
                UpdateCount++;
                return UpdateTier.Tier0;
            }
        }
    }
}
