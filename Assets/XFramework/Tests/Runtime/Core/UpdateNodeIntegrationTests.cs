using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XCore;
using XFramework.XUpdate;

namespace XFramework.XUpdate.Tests
{
    /// <summary>
    /// Integration tests for <see cref="UpdateNode"/> with a full node tree.
    /// Uses <see cref="UnityTest"/> to run the node lifecycle (Start) properly.
    /// </summary>
    public class UpdateNodeIntegrationTests
    {
        private RootNode _root;
        private UpdateNode _updateNode;

        [SetUp]
        public void SetUp()
        {
            _root = RootNode.Create();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                _root.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator UpdateNode_AutoRegistersIUpdateableChildren()
        {
            // Arrange: Add UpdateNode first
            _updateNode = _root.AddNode<UpdateNode>();
            yield return null; // let Start() run

            // Add a child that implements IUpdateable
            var child = _root.AddNode<TestUpdateLeaf>();
            yield return null; // let child Start() run

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
            yield return null; // let Start() run

            // Add a child after UpdateNode is started
            var child = _root.AddNode<TestUpdateLeaf>();
            yield return null; // let child Start() run

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
            yield return null; // child starts

            _updateNode = _root.AddNode<UpdateNode>();
            yield return null; // UpdateNode starts and scans existing children

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