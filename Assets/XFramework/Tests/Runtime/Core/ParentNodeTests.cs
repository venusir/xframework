using System;
using System.Collections.Generic;
using NUnit.Framework;
using XFramework.XCore;

namespace XFramework.XCore.Tests
{
    [TestFixture]
    public class ParentNodeTests
    {
        #region Test Helpers

        /// <summary>
        /// 可公开添加子节点的测试用 ParentNode。
        /// </summary>
        private class TestParentNode : ParentNode
        {
            public new void AddChild(BaseNode node, bool deferStart = false)
            {
                base.AddChild(node, deferStart);
            }

            public new void RemoveChild(BaseNode node, bool fromChild = false)
            {
                base.RemoveChild(node, fromChild);
            }
        }

        private sealed class TestLeaf : BaseNode { }

        private sealed class CustomLeaf : BaseNode
        {
            public bool CustomStarted { get; private set; }
            protected override void OnStart()
            {
                CustomStarted = true;
            }
        }

        private sealed class CustomParent : TestParentNode
        {
            public new void Awake() => base.Awake();

            public int ChildAddedCallCount { get; private set; }
            public int ChildRemovedCallCount { get; private set; }
            public BaseNode LastAddedNode { get; private set; }
            public BaseNode LastRemovedNode { get; private set; }

            protected override void OnChildAdded(BaseNode node)
            {
                base.OnChildAdded(node);
                ChildAddedCallCount++;
                LastAddedNode = node;
            }

            protected override void OnChildRemoved(BaseNode node, bool fromChild)
            {
                base.OnChildRemoved(node, fromChild);
                ChildRemovedCallCount++;
                LastRemovedNode = node;
            }
        }

        #endregion

        #region Add/Remove Basic

        [SetUp]
        public void SetUp()
        {
            // 每个测试前清理 AppDomain 级别的静态状态
            // NodePool 等静态管理器不在测试范围内
        }

        [Test]
        public void AddChild_IncreasesChildCount()
        {
            var parent = CreateParent();
            var child = new TestLeaf();
            parent.AddChild(child);
            Assert.AreEqual(1, parent.ChildCount);
            parent.Destroy();
        }

        [Test]
        public void AddChild_Null_DoesNotChangeCount()
        {
            var parent = CreateParent();
            parent.AddChild(null);
            Assert.AreEqual(0, parent.ChildCount);
            parent.Destroy();
        }

        [Test]
        public void RemoveChild_DecreasesChildCount()
        {
            var parent = CreateParent();
            var child = new TestLeaf();
            parent.AddChild(child);
            parent.RemoveChild(child);
            Assert.AreEqual(0, parent.ChildCount);
            parent.Destroy();
        }

        [Test]
        public void RemoveChild_UnknownNode_DoesNotThrow()
        {
            var parent = CreateParent();
            Assert.DoesNotThrow(() => parent.RemoveChild(new TestLeaf()));
            parent.Destroy();
        }

        [Test]
        public void AddChild_Duplicate_DoesNotAdd()
        {
            var parent = CreateParent();
            var child = new TestLeaf();
            parent.AddChild(child);
            parent.AddChild(child);
            Assert.AreEqual(1, parent.ChildCount);
            parent.Destroy();
        }

        [Test]
        public void AddChild_AwakesChild()
        {
            var parent = CreateParent();
            var child = new TestLeaf();
            parent.AddChild(child);
            // Awake 调用后 Depth 不应为默认值 - 验证 Awake 被调用
            Assert.AreEqual(1, child.Depth);
            parent.Destroy();
        }

        [Test]
        public void DestroyedNode_AddChild_Warns()
        {
            var parent = CreateParent();
            var child = new TestLeaf();
            child.Destroy();
            // 已销毁节点添加到父节点应该触发警告但不抛出异常
            Assert.DoesNotThrow(() => parent.AddChild(child));
            Assert.AreEqual(0, parent.ChildCount);
            parent.Destroy();
        }

        #endregion

        #region Events

        [Test]
        public void OnNodeAdded_Fires_WhenChildAdded()
        {
            var parent = CreateParent();
            BaseNode added = null;
            parent.OnNodeAdded += n => added = n;

            var child = new TestLeaf();
            parent.AddChild(child);
            Assert.AreSame(child, added);
            parent.Destroy();
        }

        [Test]
        public void OnNodeRemoved_Fires_WhenChildRemoved()
        {
            var parent = CreateParent();
            var child = new TestLeaf();
            parent.AddChild(child);

            BaseNode removed = null;
            parent.OnNodeRemoved += n => removed = n;
            parent.RemoveChild(child);
            Assert.AreSame(child, removed);
            parent.Destroy();
        }

        [Test]
        public void OnDescendantAdded_Bubbles_FromGrandchild()
        {
            var grandparent = CreateParent();
            var parent = new TestParentNode();
            grandparent.AddChild(parent);

            BaseNode descendantAdded = null;
            grandparent.OnDescendantAdded += n => descendantAdded = n;

            var child = new TestLeaf();
            parent.AddChild(child);
            Assert.AreSame(child, descendantAdded);
            grandparent.Destroy();
        }

        [Test]
        public void OnDescendantRemoved_Bubbles_FromGrandchild()
        {
            var grandparent = CreateParent();
            var parent = new TestParentNode();
            grandparent.AddChild(parent);
            var child = new TestLeaf();
            parent.AddChild(child);

            BaseNode descendantRemoved = null;
            grandparent.OnDescendantRemoved += n => descendantRemoved = n;

            parent.RemoveChild(child);
            Assert.AreSame(child, descendantRemoved);
            grandparent.Destroy();
        }

        [Test]
        public void OnDescendantStarted_Bubbles_FromGrandchild()
        {
            var grandparent = CreateParent();
            var parent = new TestParentNode();
            grandparent.AddChild(parent);
            var child = new CustomLeaf();
            parent.AddChild(child);

            BaseNode descendantStarted = null;
            grandparent.OnDescendantStarted += n => descendantStarted = n;

            // 父节点已 Start，子节点添加后自动 Start
            child.Start();
            Assert.AreSame(child, descendantStarted);
            grandparent.Destroy();
        }

        [Test]
        public void ChildAddedEvent_Fires_ForCustomParent()
        {
            var parent = new CustomParent();
            parent.Awake();
            var child = new TestLeaf();
            parent.AddChild(child);
            Assert.AreEqual(1, parent.ChildAddedCallCount);
            Assert.AreSame(child, parent.LastAddedNode);
            parent.Destroy();
        }

        [Test]
        public void ChildRemovedEvent_Fires_ForCustomParent()
        {
            var parent = new CustomParent();
            parent.Awake();
            var child = new TestLeaf();
            parent.AddChild(child);
            parent.RemoveChild(child);
            Assert.AreEqual(1, parent.ChildRemovedCallCount);
            Assert.AreSame(child, parent.LastRemovedNode);
            parent.Destroy();
        }

        #endregion

        #region Child Querying

        [Test]
        public void GetNode_ReturnsFirstMatch()
        {
            var parent = CreateParent();
            var child = new TestLeaf();
            var custom = new CustomLeaf();
            parent.AddChild(child);
            parent.AddChild(custom);

            Assert.AreSame(child, parent.GetNode<TestLeaf>());
            Assert.AreSame(custom, parent.GetNode<CustomLeaf>());
            parent.Destroy();
        }

        [Test]
        public void GetNode_WithPredicate_FiltersCorrectly()
        {
            var parent = CreateParent();
            // 使用可以反射访问 _started 的方式，简单起见直接创建后验证
            var child = new TestLeaf();
            parent.AddChild(child);

            // 通过 predicate 查找
            var found = parent.GetNode<TestLeaf>(_ => true);
            Assert.AreSame(child, found);

            var notFound = parent.GetNode<TestLeaf>(_ => false);
            Assert.IsNull(notFound);
            parent.Destroy();
        }

        [Test]
        public void GetNodes_Recursive_CollectsAllDescendants()
        {
            var grandparent = CreateParent();
            var parent = new TestParentNode();
            grandparent.AddChild(parent);
            parent.AddChild(new TestLeaf());
            parent.AddChild(new CustomLeaf());

            var results = new List<BaseNode>();
            grandparent.GetNodes(results, recursive: true);
            Assert.AreEqual(2, results.Count);
            grandparent.Destroy();
        }

        [Test]
        public void GetNodes_NonRecursive_OnlyDirectChildren()
        {
            var grandparent = CreateParent();
            var parent = new TestParentNode();
            grandparent.AddChild(parent);
            parent.AddChild(new TestLeaf());

            var results = new List<BaseNode>();
            grandparent.GetNodes(results, recursive: false);
            Assert.AreEqual(1, results.Count);
            Assert.IsInstanceOf<TestParentNode>(results[0]);
            grandparent.Destroy();
        }

        [Test]
        public void GetNodes_WithPredicate_Filters()
        {
            var parent = CreateParent();
            parent.AddChild(new TestLeaf());
            parent.AddChild(new CustomLeaf());

            var leaves = new List<TestLeaf>();
            parent.GetNodes(leaves, recursive: false, predicate: _ => true);
            Assert.AreEqual(1, leaves.Count);

            parent.Destroy();
        }

        [Test]
        public void ForEach_IteratesAllChildren()
        {
            var parent = CreateParent();
            parent.AddChild(new TestLeaf());
            parent.AddChild(new CustomLeaf());

            var visited = new List<BaseNode>();
            parent.ForEach(n => visited.Add(n));
            Assert.AreEqual(2, visited.Count);
            parent.Destroy();
        }

        [Test]
        public void ForEach_Recursive_VisitsDescendants()
        {
            var grandparent = CreateParent();
            var parent = new TestParentNode();
            grandparent.AddChild(parent);
            parent.AddChild(new TestLeaf());

            var visited = new List<BaseNode>();
            grandparent.ForEach(n => visited.Add(n), recursive: true);
            Assert.AreEqual(2, visited.Count);
            grandparent.Destroy();
        }

        [Test]
        public void ForEach_NullCallback_Throws()
        {
            var parent = CreateParent();
            Assert.Throws<ArgumentNullException>(() => parent.ForEach(null));
            parent.Destroy();
        }

        #endregion

        #region AutoStart

        [Test]
        public void Child_AutoStarts_WhenParentAlreadyStarted()
        {
            var parent = CreateParent();
            parent.Start();

            var child = new CustomLeaf();
            parent.AddChild(child);
            Assert.IsTrue(child.CustomStarted);
            parent.Destroy();
        }

        [Test]
        public void Child_DeferStart_DoesNotAutoStart()
        {
            var parent = CreateParent();
            parent.Start();

            var child = new CustomLeaf();
            parent.AddChild(child, deferStart: true);
            Assert.IsFalse(child.CustomStarted);

            // 手动 Start
            child.Start();
            Assert.IsTrue(child.CustomStarted);
            parent.Destroy();
        }

        [Test]
        public void Child_NoAutoStart_WhenParentNotStarted()
        {
            var parent = CreateParent();
            var child = new CustomLeaf();
            parent.AddChild(child);
            Assert.IsFalse(child.CustomStarted);
            parent.Destroy();
        }

        #endregion

        #region Destroy Propagation

        [Test]
        public void Destroy_RecursivelyDestroysChildren()
        {
            var parent = CreateParent();
            var child = new TestLeaf();
            parent.AddChild(child);

            parent.Destroy();
            Assert.IsTrue(IsDestroyed(child));
        }

        [Test]
        public void Destroy_RemovesAllChildren()
        {
            var parent = CreateParent();
            parent.AddChild(new TestLeaf());
            parent.AddChild(new CustomLeaf());

            parent.Destroy();
            Assert.AreEqual(0, parent.ChildCount);
        }

        #endregion

        #region Private Helpers

        private static TestParentNode CreateParent()
        {
            var node = new TestParentNode();
            node.Awake();
            return node;
        }

        private static bool IsDestroyed(BaseNode node)
        {
            return node.GetType().GetProperty("Destroyed",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(node) is true;
        }

        #endregion
    }
}