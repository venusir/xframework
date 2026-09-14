using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XNode;
using XFramework.XUpdate;

namespace XFramework.XUpdate.Tests
{
    /// <summary>
    /// <see cref="UpdateNode"/> 与真实节点树的集成测试。
    /// <para><b>注意：</b>节点树是纯 C#，没有任何机制会自动启动它——<see cref="RootNode.Create"/>
    /// 只做 Awake，<c>Start</c> 必须在 <see cref="SetUp"/> 里显式调用，
    /// 否则 <see cref="UpdateNode.OnStart"/> 永不执行，子节点也就永远不会被注册进调度器。</para>
    /// </summary>
    public class UpdateNodeIntegrationTests
    {
        private RootNode _root;
        private UpdateNode _updateNode;

        [SetUp]
        public void SetUp()
        {
            // 关掉 PlayerLoop 自动驱动：本 fixture 是 [UnityTest]，yield 期间自动驱动会额外派发，
            // 断言里的精确计数会被打乱。手动 Tick 的时机才是这些用例要验证的东西
            UpdateManager.AutoDriveEnabled = false;

            _root = RootNode.Create();

            // 根节点先 Start 后，AddChild 会自动 Start 新加入的子节点（ParentNode.AddChild 的
            // `if (Started && !deferStart)` 分支），与生产环境的用法一致
            _root.Start();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                _root.Dispose();
            }

            // 调度器是静态的，用例间必须复位，否则上一个用例的注册会留到下一个用例继续被派发。
            // UpdateManager.Clear 能安全地这么用，正是因为它已不再置单向闩锁
            UpdateManager.Clear();

            // 静态开关同理必须还原（PlayMode 下所有 fixture 共享同一个 player）
            UpdateManager.AutoDriveEnabled = true;
        }

        [UnityTest]
        public IEnumerator UpdateNode_AutoRegistersIUpdateableChildren()
        {
            // Arrange: Add UpdateNode first
            _updateNode = _root.AddNode<UpdateNode>();
            yield return null;

            // Add a child that implements IUpdateable
            var child = _root.AddNode<TestUpdateLeaf>();
            yield return null;

            // The child should be registered in the scheduler
            // Tick the scheduler and verify child.OnUpdate was called
            UpdateManager.Tick(time: Time.time);
            Assert.AreEqual(1, child.OnUpdateCallCount);

            yield break;
        }

        [UnityTest]
        public IEnumerator UpdateNode_RegistersOnDescendantStarted()
        {
            // Arrange: Add UpdateNode
            _updateNode = _root.AddNode<UpdateNode>();
            yield return null;

            // Add a child after UpdateNode is started
            var child = _root.AddNode<TestUpdateLeaf>();
            yield return null;

            // Child should be auto-registered
            UpdateManager.Tick(time: Time.time);
            Assert.AreEqual(1, child.OnUpdateCallCount);

            yield break;
        }

        [UnityTest]
        public IEnumerator UpdateNode_UnregistersOnDescendantRemoved()
        {
            // Arrange
            _updateNode = _root.AddNode<UpdateNode>();
            yield return null;

            var child = _root.AddNode<TestUpdateLeaf>();
            yield return null;

            // First tick: child should be updated
            UpdateManager.Tick(time: Time.time);
            Assert.AreEqual(1, child.OnUpdateCallCount);

            // Remove child
            _root.RemoveNode(child);
            yield return null;

            // Second tick: child should NOT be updated
            UpdateManager.Tick(time: Time.time + 1.0f);
            Assert.AreEqual(1, child.OnUpdateCallCount);

            yield break;
        }

        [UnityTest]
        public IEnumerator AddChild_BeforeUpdateNode_StillRegistered()
        {
            // Arrange: Add child first, then UpdateNode
            var child = _root.AddNode<TestUpdateLeaf>();
            yield return null;

            _updateNode = _root.AddNode<UpdateNode>();
            yield return null;

            // Child should be registered from OnStart scanning
            UpdateManager.Tick(time: Time.time);
            Assert.AreEqual(1, child.OnUpdateCallCount);

            yield break;
        }

        [UnityTest]
        public IEnumerator UpdateNode_DeclaredUnscaledAxis_KeepsUpdatingWhilePaused()
        {
            _updateNode = _root.AddNode<UpdateNode>();
            yield return null;

            var scaled = _root.AddNode<TestUpdateLeaf>();
            var unscaled = _root.AddNode<UnscaledUpdateLeaf>();
            yield return null;

            UpdateManager.Tick(time: Time.time);
            Assert.AreEqual(1, scaled.OnUpdateCallCount);
            Assert.AreEqual(1, unscaled.OnUpdateCallCount);

            // 暂停逻辑轴：声明了墙钟轴的节点应继续被派发
            UpdateManager.Pause();
            UpdateManager.Tick(time: Time.time + 1f);

            Assert.AreEqual(1, scaled.OnUpdateCallCount, "未声明时间轴的节点走逻辑轴，暂停即停");
            Assert.AreEqual(2, unscaled.OnUpdateCallCount, "声明墙钟轴的节点在暂停期间照常运行");

            UpdateManager.Resume();
            yield break;
        }

        [UnityTest]
        public IEnumerator RegisterUpdateExtension_HonoursDeclaredTimeMode()
        {
            // 手动注册（不走 UpdateNode）同样应尊重节点声明的时间轴
            var leaf = _root.AddNode<UnscaledUpdateLeaf>();
            yield return null;

            leaf.RegisterUpdate();
            UpdateManager.Pause();
            UpdateManager.Tick(time: Time.time);

            Assert.AreEqual(1, leaf.OnUpdateCallCount);

            UpdateManager.Resume();
            yield break;
        }

        [UnityTest]
        public IEnumerator UpdateNode_AutoRegistersLateUpdateableChildren()
        {
            _updateNode = _root.AddNode<UpdateNode>();
            yield return null;

            var late = _root.AddNode<LateUpdateLeaf>();
            yield return null;

            UpdateManager.Tick(Time.time);

            Assert.AreEqual(1, late.OnLateUpdateCallCount, "只实现 ILateUpdateable 的节点应被自动登记到延迟时机");
            Assert.AreEqual(1, UpdateManager.TotalCount, "只登记一次");
        }

        /// <summary>
        /// 声明墙钟时间轴的测试叶节点：暂停期间仍应收到 OnUpdate。
        /// </summary>
        private sealed class UnscaledUpdateLeaf : LeafNode, IUpdateable, IUpdateTimeMode
        {
            public int OnUpdateCallCount { get; private set; }

            public UpdateTimeMode TimeMode => UpdateTimeMode.Unscaled;

            protected override void OnAwake()
            {
                base.OnAwake();

                // 与 TestUpdateLeaf 同理：节点经池复用，计数必须在 OnAwake 复位
                OnUpdateCallCount = 0;
            }

            public void OnEnable() { }

            public void OnDisable() { }

            public UpdateLOD OnUpdate(float deltaTime, float time)
            {
                OnUpdateCallCount++;
                return UpdateLOD.Tier0;
            }
        }

        [UnityTest]
        public IEnumerator UpdateNode_AutoRegistersFixedUpdateableChildren()
        {
            _updateNode = _root.AddNode<UpdateNode>();
            yield return null;

            var fixedLeaf = _root.AddNode<FixedUpdateLeaf>();
            yield return null;

            UpdateManager.Tick(Time.time);
            Assert.AreEqual(0, fixedLeaf.OnFixedUpdateCallCount, "变步长 Tick 不该驱动固定步长时机");

            UpdateManager.TickFixed(Time.fixedTime);
            Assert.AreEqual(1, fixedLeaf.OnFixedUpdateCallCount, "应被自动登记到固定步长时机");
        }

        /// <summary>
        /// 只实现固定步长时机的测试叶节点。
        /// </summary>
        private sealed class FixedUpdateLeaf : LeafNode, IFixedUpdateable
        {
            public int OnFixedUpdateCallCount { get; private set; }

            protected override void OnAwake()
            {
                base.OnAwake();

                // 节点经池复用，计数必须在 OnAwake 复位
                OnFixedUpdateCallCount = 0;
            }

            public void OnEnable() { }

            public void OnDisable() { }

            public UpdateLOD OnFixedUpdate(float deltaTime, float fixedTime)
            {
                OnFixedUpdateCallCount++;
                return UpdateLOD.Tier0;
            }
        }

        /// <summary>
        /// 只实现延迟更新时机的测试叶节点。
        /// </summary>
        private sealed class LateUpdateLeaf : LeafNode, ILateUpdateable
        {
            public int OnLateUpdateCallCount { get; private set; }

            protected override void OnAwake()
            {
                base.OnAwake();

                // 节点经池复用，计数必须在 OnAwake 复位
                OnLateUpdateCallCount = 0;
            }

            public void OnEnable() { }

            public void OnDisable() { }

            public UpdateLOD OnLateUpdate(float deltaTime, float time)
            {
                OnLateUpdateCallCount++;
                return UpdateLOD.Tier0;
            }
        }

        /// <summary>
        /// Test leaf node implementing IUpdateable for integration tests.
        /// </summary>
        private sealed class TestUpdateLeaf : LeafNode, IUpdateable
        {
            public int OnUpdateCallCount { get; private set; }

            protected override void OnAwake()
            {
                base.OnAwake();

                // 节点经池复用，派生类状态必须在 OnAwake 复位（见 Node README 的契约）——
                // 否则计数跨用例累积，四个用例里后面几个会拿到前面留下的基数
                OnUpdateCallCount = 0;
            }

            public void OnEnable() { }

            public void OnDisable() { }

            public UpdateLOD OnUpdate(float deltaTime, float time)
            {
                OnUpdateCallCount++;
                return UpdateLOD.Tier0;
            }
        }
    }
}