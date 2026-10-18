using System;
using System.Collections.Generic;
using NUnit.Framework;
using XFramework.XUpdate;

namespace XFramework.XUpdate.Tests
{
    /// <summary>
    /// <see cref="UpdateManager.CopyNodes"/>：已注册节点明细投影。
    /// <para>契约面（缓冲区清空、未初始化/空表返回 0、参数防御）与内容面（含已禁用节点）都在本 fixture。
    /// 节点明细的内容形状**不再**假手 <c>UpdateScheduler*Tests</c>——UPD-006 实测那里 0 命中。</para>
    /// </summary>
    [TestFixture]
    public class UpdateDiagnosticsTests
    {
        private sealed class Node : IUpdateable
        {
            public UpdateTier OnUpdate(float deltaTime, float time) => UpdateTier.Tier0;

            public void OnEnable() { }

            public void OnDisable() { }
        }

        /// <summary>回调里做检查的驱动节点（UPD-006 的 pending-Register 用例）。</summary>
        private sealed class ProbeNode : IUpdateable
        {
            private readonly Action _onUpdate;

            public ProbeNode(Action onUpdate) => _onUpdate = onUpdate;

            public UpdateTier OnUpdate(float deltaTime, float time)
            {
                _onUpdate();
                return UpdateTier.Tier0;
            }

            public void OnEnable() { }

            public void OnDisable() { }
        }

        [SetUp]
        public void SetUp()
        {
            UpdateManager.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            UpdateManager.Clear();
        }

        [Test]
        public void CopyNodes_ListsRegisteredNode()
        {
            UpdateManager.AutoInit();
            UpdateManager.Clear();
            UpdateManager.Register(new Node(), 0);

            var buffer = new List<UpdateNodeInfo>();
            int count = UpdateManager.CopyNodes(buffer);

            Assert.AreEqual(buffer.Count, count);
            Assert.AreEqual(1, count);
            Assert.AreEqual(typeof(Node), buffer[0].NodeType);
            Assert.AreEqual(UpdateTiming.Update, buffer[0].Timing);
            Assert.IsTrue(buffer[0].IsEnabled);
        }

        [Test]
        public void CopyNodes_EmptyRegistry_ReturnsZeroAndClearsBuffer()
        {
            var buffer = new List<UpdateNodeInfo> { default };

            int count = UpdateManager.CopyNodes(buffer);
            Assert.AreEqual(0, count);
            Assert.AreEqual(0, buffer.Count);
        }

        [Test]
        public void CopyNodes_NullBuffer_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => UpdateManager.CopyNodes(null));
        }

        [Test]
        public void CopyNodes_IncludesDisabledNodes()
        {
            // UPD-006：CopyNodes 的「含已禁用」分支此前零覆盖
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            var node = new Node();
            UpdateManager.Register(node, 0);
            UpdateManager.Disable(node);

            var buffer = new List<UpdateNodeInfo>();
            int count = UpdateManager.CopyNodes(buffer);

            Assert.AreEqual(1, count, "禁用节点仍在管理集合里，诊断面应包含它");
            Assert.IsFalse(buffer[0].IsEnabled, "禁用节点应标记 IsEnabled=false");
            Assert.AreEqual(typeof(Node), buffer[0].NodeType);
        }

        [Test]
        public void IsRegistered_DuringDispatch_SeesPendingRegister()
        {
            // UPD-006：IsRegistered 的 pending-Register 分支此前零覆盖——
            // 派发期间 Register 尚未落表，查询必须回溯待处理操作。
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            var late = new Node();
            bool? seen = null;
            var host = new ProbeNode(() =>
            {
                UpdateManager.Register(late, 0);
                seen = UpdateManager.IsRegistered(late);
            });
            UpdateManager.Register(host, 0);

            UpdateManager.Tick(new UpdateClock(1d, 1d));

            Assert.AreEqual(true, seen, "派发期间的 Register 尚未落表，IsRegistered 必须回溯待处理操作");
        }

        [Test]
        public void UpdateTier_Max_MatchesEnumMaximum()
        {
            // UPD-006：新增档位忘改 Max 会被静默钳到旧上限——这里用枚举最大值机械锁定
            int max = 0;
            foreach (UpdateTier tier in Enum.GetValues(typeof(UpdateTier)))
                max = Math.Max(max, (int)tier);

            Assert.AreEqual(max, (int)UpdateTier.Max,
                "UpdateTier.Max 必须等于枚举最大值（新增档位请同步改 Max）");
        }
    }
}
