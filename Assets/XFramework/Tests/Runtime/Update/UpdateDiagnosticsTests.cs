using System;
using System.Collections.Generic;
using NUnit.Framework;
using XFramework.XUpdate;

namespace XFramework.XUpdate.Tests
{
    /// <summary>
    /// <see cref="UpdateManager.CopyNodes"/>：已注册节点明细投影。
    /// <para>本 fixture 只锁**契约面**（缓冲区清空、未初始化/空表返回 0、参数防御）——
    /// 节点明细的内容由各调度器自身决定，那部分的形状由 <c>UpdateScheduler*Tests</c> 覆盖。</para>
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
    }
}
