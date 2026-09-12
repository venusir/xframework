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
            _updateNode.Tick(time: Time.time);
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
            _updateNode.Tick(time: Time.time);
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
            _updateNode.Tick(time: Time.time);
            Assert.AreEqual(1, child.OnUpdateCallCount);

            // Remove child
            _root.RemoveNode(child);
            yield return null;

            // Second tick: child should NOT be updated
            _updateNode.Tick(time: Time.time + 1.0f);
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
            _updateNode.Tick(time: Time.time);
            Assert.AreEqual(1, child.OnUpdateCallCount);

            yield break;
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
                return UpdateLOD.Frame1;
            }
        }
    }
}