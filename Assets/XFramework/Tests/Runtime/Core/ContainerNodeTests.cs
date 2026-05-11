using NUnit.Framework;
using XFramework.XCore;

namespace XFramework.XCore.Tests
{
    [TestFixture]
    public class ContainerNodeTests
    {
        #region Test Helpers

        private sealed class TestContainer : ContainerNode
        {
            public new void Awake() => base.Awake();
            public new void Start() => base.Start();
            public new void Destroy() => base.Destroy();
        }

        private sealed class TestLeaf : BaseNode { }

        #endregion

        #region AddNode

        [Test]
        public void AddNode_AddsChild()
        {
            var container = CreateContainer();
            var child = new TestLeaf();
            container.AddNode(child);
            Assert.AreEqual(1, container.ChildCount);
            container.Destroy();
        }

        [Test]
        public void AddNode_Null_DoesNotAdd()
        {
            var container = CreateContainer();
            container.AddNode(null);
            Assert.AreEqual(0, container.ChildCount);
            container.Destroy();
        }

        #endregion

        #region RemoveNode

        [Test]
        public void RemoveNode_RemovesAndDestroysChild()
        {
            var container = CreateContainer();
            var child = new TestLeaf();
            container.AddNode(child);
            container.RemoveNode(child);
            Assert.AreEqual(0, container.ChildCount);
            // child 被销毁
            container.Destroy();
        }

        [Test]
        public void RemoveNode_Null_DoesNotThrow()
        {
            var container = CreateContainer();
            Assert.DoesNotThrow(() => container.RemoveNode(null));
            container.Destroy();
        }

        [Test]
        public void RemoveNode_Unknown_DoesNotThrow()
        {
            var container = CreateContainer();
            Assert.DoesNotThrow(() => container.RemoveNode(new TestLeaf()));
            container.Destroy();
        }

        #endregion

        #region Lifecycle

        [Test]
        public void Container_Destroy_DestroysChildren()
        {
            var container = CreateContainer();
            var child = new TestLeaf();
            container.AddNode(child);

            container.Destroy();
            // Child 应该被销毁
            Assert.AreEqual(0, container.ChildCount);
        }

        #endregion

        #region Private Helpers

        private static TestContainer CreateContainer()
        {
            var container = new TestContainer();
            container.Awake();
            return container;
        }

        #endregion
    }
}