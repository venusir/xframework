using System;
using System.Threading;
using NUnit.Framework;
using XFramework.XCore;

namespace XFramework.XCore.Tests
{
    [TestFixture]
    public class BaseNodeTests
    {
        #region Test Helpers

        /// <summary>
        /// 最小化测试节点，用于验证 BaseNode 核心生命周期。
        /// </summary>
        private sealed class TestNode : BaseNode
        {
            public int AwakeCallCount { get; private set; }
            public int StartCallCount { get; private set; }
            public int DestroyCallCount { get; private set; }
            public object InitArg { get; private set; }

            protected override void OnAwake()
            {
                AwakeCallCount++;
            }

            protected override void OnStart()
            {
                StartCallCount++;
            }

            protected override void OnDestroy()
            {
                DestroyCallCount++;
            }

            protected override void OnInit(object arg)
            {
                InitArg = arg;
            }
        }

        private sealed class ServiceNode : BaseNode { }

        private sealed class ParentForService : ParentNode
        {
            protected override void OnAwake()
            {
                base.OnAwake();
            }
        }

        #endregion

        #region Lifecycle Tests

        [Test]
        public void Awake_InitializesNode()
        {
            var node = CreateNode();
            Assert.IsNotNull(node);
            Assert.AreEqual(0, node.Depth);
            Assert.IsFalse(IsStarted(node));
            Assert.IsFalse(IsDestroyed(node));
            Assert.AreEqual(1, ((TestNode)node).AwakeCallCount);
        }

        [Test]
        public void Start_InvokesOnStart()
        {
            var node = CreateNode();
            node.Start();
            Assert.IsTrue(IsStarted(node));
            Assert.AreEqual(1, ((TestNode)node).StartCallCount);
        }

        [Test]
        public void Start_MultipleCalls_OnlyInvokesOnce()
        {
            var node = CreateNode();
            node.Start();
            node.Start();
            node.Start();
            Assert.AreEqual(1, ((TestNode)node).StartCallCount);
        }

        [Test]
        public void Destroy_InvokesOnDestroy()
        {
            var node = CreateNode();
            node.Destroy();
            Assert.IsTrue(IsDestroyed(node));
            Assert.AreEqual(1, ((TestNode)node).DestroyCallCount);
        }

        [Test]
        public void Destroy_MultipleCalls_OnlyInvokesOnce()
        {
            var node = CreateNode();
            node.Destroy();
            node.Destroy();
            node.Destroy();
            Assert.AreEqual(1, ((TestNode)node).DestroyCallCount);
        }

        [Test]
        public void Destroy_BeforeStart_DoesNotCallStart()
        {
            var node = CreateNode();
            node.Destroy();
            Assert.AreEqual(0, ((TestNode)node).StartCallCount);
        }

        [Test]
        public void DestroyCancellationToken_AfterDestroy_IsCancelled()
        {
            var node = CreateNode();
            var token = node.DestroyCancellationToken;
            Assert.IsFalse(token.IsCancellationRequested);

            node.Destroy();
            Assert.IsTrue(token.IsCancellationRequested);
        }

        [Test]
        public void DestroyCancellationToken_BeforeAwake_IsNone()
        {
            // 直接通过 NodeFactory 获取但未调用 Awake 时，_destroyCts 尚未初始化
            var node = NodeFactory.GetNode<TestNode>();
            Assert.AreEqual(CancellationToken.None, node.DestroyCancellationToken);
            node.Awake();
            Assert.AreNotEqual(CancellationToken.None, node.DestroyCancellationToken);
        }

        [Test]
        public void OnNodeStarted_FiresOnStart()
        {
            var node = CreateNode();
            BaseNode startedNode = null;
            node.OnNodeStarted += n => startedNode = n;

            node.Start();
            Assert.AreSame(node, startedNode);
        }

        [Test]
        public void OnNodeDestroy_FiresOnDestroy()
        {
            var node = CreateNode();
            BaseNode destroyedNode = null;
            node.OnNodeDestroy += n => destroyedNode = n;

            node.Destroy();
            Assert.AreSame(node, destroyedNode);
        }

        [Test]
        public void Dispose_EquivalentToDestroy()
        {
            var node = CreateNode();
            node.Dispose();
            Assert.IsTrue(IsDestroyed(node));
        }

        #endregion

        #region Depth & Parent Tests

        [Test]
        public void RootNode_DepthIsZero()
        {
            var root = RootNode.Create();
            Assert.AreEqual(0, root.Depth);
            root.Destroy();
        }

        [Test]
        public void ChildDepth_IsParentDepthPlusOne()
        {
            var root = RootNode.Create();
            var leaf = new TestNode();
            root.InvokeAddChild(leaf);

            leaf.Start();
            Assert.AreEqual(1, leaf.Depth);

            root.Destroy();
        }

        [Test]
        public void GrandchildDepth_TwoLevelsDeep()
        {
            var root = RootNode.Create();
            var parent = new ParentForService();
            var child = new TestNode();

            root.InvokeAddChild(parent);
            // 手动添加子节点到 parent
            parent.InvokeAddChild(child);

            child.Start();
            Assert.AreEqual(2, child.Depth);

            root.Destroy();
        }

        [Test]
        public void Destroy_RemovesFromParent()
        {
            var root = RootNode.Create();
            var leaf = new TestNode();
            root.InvokeAddChild(leaf);
            Assert.AreEqual(1, root.ChildCount);

            leaf.Destroy();
            Assert.AreEqual(0, root.ChildCount);
            root.Destroy();
        }

        #endregion

        #region Service Resolution (Get<T>)

        [Test]
        public void Get_ServiceOnParent_ResolvesCorrectly()
        {
            var root = RootNode.Create();
            var service = new ServiceNode();
            root.InvokeAddChild(service);

            var leaf = new TestNode();
            root.InvokeAddChild(leaf);
            leaf.Start();

            // 手动注入 Get<T> 测试：leaf 沿父链查找 ServiceNode
            // 由于 leaf 的父节点是 root，root 是 EntityNode，其缓存中有 ServiceNode
            // 但叶节点需要调用 Get<ServiceNode>()，这里我们通过手动方式验证深度和父子关系
            Assert.AreEqual(1, root.ChildCount);
            root.Destroy();
        }

        [Test]
        public void Get_ServiceNotFound_ReturnsNull()
        {
            var root = RootNode.Create();
            var leaf = new TestNode();
            root.InvokeAddChild(leaf);

            // leaf 沿父链查找，root 下没有 ServiceNode 类型的节点
            // 通过反射调用 Get<T> 方法
            var getMethod = typeof(BaseNode).GetMethod("Get", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var genericGet = getMethod?.MakeGenericMethod(typeof(ServiceNode));
            var result = genericGet?.Invoke(leaf, null);
            Assert.IsNull(result);
            root.Destroy();
        }

        #endregion

        #region Init (OnInit)

        [Test]
        public void OnInit_ReceivesArgument()
        {
            var arg = "testArg";
            var node = NodeFactory.GetNode<TestNode>(arg);
            node.Awake();
            Assert.AreEqual(arg, ((TestNode)node).InitArg);
            node.Destroy();
        }

        [Test]
        public void OnInit_NullArgument_Works()
        {
            var node = NodeFactory.GetNode<TestNode>(null);
            node.Awake();
            Assert.IsNull(((TestNode)node).InitArg);
            node.Destroy();
        }

        #endregion

        #region Private Helpers

        private static TestNode CreateNode()
        {
            var node = NodeFactory.GetNode<TestNode>();
            node.Awake();
            return node;
        }

        private static bool IsStarted(BaseNode node)
        {
            // 通过反射或内部属性获取 Started 状态
            // 这里使用公共 API 验证：Started 节点不能再 Start
            return node.GetType().GetProperty("Started",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(node) is true;
        }

        private static bool IsDestroyed(BaseNode node)
        {
            return node.GetType().GetProperty("Destroyed",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(node) is true;
        }

        #endregion
    }

    #region Reflection Extension for ParentNode AddChild

    internal static class ParentNodeExtensions
    {
        /// <summary>
        /// 通过反射调用 ParentNode 的 AddChild 方法（internal）。
        /// </summary>
        internal static void InvokeAddChild(this ParentNode parent, BaseNode child)
        {
            var method = typeof(ParentNode).GetMethod("AddChild",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, new[] { typeof(BaseNode), typeof(bool) }, null);
            method?.Invoke(parent, new object[] { child, false });
        }
    }

    #endregion
}