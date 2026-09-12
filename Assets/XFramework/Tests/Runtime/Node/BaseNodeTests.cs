using System;
using System.Threading;
using NUnit.Framework;
using XFramework.XNode;

namespace XFramework.XNode.Tests
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
                // 节点经池复用，派生类状态必须在此复位（AwakeInternal 只复位框架自身字段）。
                // 本 fixture 的 CreateNode() 走 NodeFactory，拿到的可能是上一个用例用过的实例，
                // 不复位则计数跨用例累积——Destroy 那两个用例曾因此恒红。
                // 注意 InitArg 刻意不在此复位：OnInit 先于 OnAwake 调用，在这里清会抹掉刚传入的参数。
                AwakeCallCount = 0;
                StartCallCount = 0;
                DestroyCallCount = 0;

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

        /// <summary>暴露 <see cref="BaseNode.Get{T}"/>（protected）以便测试服务解析。</summary>
        private sealed class ServiceConsumerNode : BaseNode
        {
            public T Resolve<T>() where T : IBaseNode => Get<T>();
        }

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
            // 原用例加了 2 个子节点却断言 ChildCount == 1，且注释自承「通过手动方式验证深度和父子关系」
            // ——它从未测过服务解析。这里写出真正的用例：BaseNode.Get<T>() 是 protected，
            // 故用一个暴露它的测试节点来调。
            var root = RootNode.Create();

            // 必须经 AddNode：InvokeAddChild 直接挂树、不写 root 的类型缓存，
            // 而 Get<T> 正是沿父链查各 EntityNode 的类型缓存
            var service = root.AddNode<ServiceNode>();
            var leaf = root.AddNode<ServiceConsumerNode>();

            Assert.AreSame(service, leaf.Resolve<ServiceNode>(), "叶节点应沿父链解析到父节点上的服务");
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