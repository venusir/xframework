using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using XFramework.XMessage;
using XFramework.XNode;

// 命名空间刻意不放在 XFramework.XNode / XFramework.XMessage 之下。
//
// 扩展方法查找从最内层命名空间向外逐层进行，并在第一个「有候选」的层级停止：
// 若本文件位于 XFramework.XNode.* 之下，XNode 层级已能找到 NodeExtensions.Subscribe，
// 查找会就此停止而根本不去看另一个命名空间的候选——那样即使重载决议本身有问题也测不出来。
//
// 用户代码通常写在 Game.Xxx 这类中立命名空间里、并在文件顶部同时 using 两个模块，
// 本文件正是复刻该场景：两个扩展方法集在同一层级出现，重载决议必须唯一选出节点版。
namespace XFramework.Tests
{
    /// <summary>
    /// Tests for node message subscription (NodeExtensions.Subscribe / SubscribeAsync / SubscribeBuffered).
    /// <para>
    /// 本文件同时承担<b>编译期回归守卫</b>：一旦
    /// <c>NodeExtensions.Subscribe(this BaseNode, ...)</c> 与
    /// <c>MessageManager.Subscribe(this IMessageSubscriber, ...)</c> 的二义性回归
    /// （例如移除 BaseNode 上的 IMessageSubscriber 实现），本文件将直接编译失败。
    /// </para>
    /// </summary>
    [TestFixture]
    public class NodeMessageSubscribeTests
    {
        #region Test Doubles

        private sealed class TestNode : BaseNode { }

        private struct StructMessage
        {
            public int Value;
        }

        private sealed class ClassMessage
        {
            public int Value { get; set; }
        }

        #endregion

        [SetUp]
        public void SetUp()
        {
            MessageManager.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            MessageManager.Clear();
        }

        #region 同步订阅

        [Test]
        public void NodeSubscribe_StructMessage_ReceivesMessage()
        {
            var node = CreateNode();
            try
            {
                var received = new List<int>();
                node.Subscribe<StructMessage>(m => received.Add(m.Value));

                MessageManager.Publish(new StructMessage { Value = 42 });

                CollectionAssert.AreEqual(new[] { 42 }, received,
                    "struct 消息可用(历史上该扩展带 where TMessage : class 约束,框架内置消息全部编译不过)");
            }
            finally
            {
                node.Destroy();
            }
        }

        [Test]
        public void NodeSubscribe_ClassMessage_ResolvesToNodeOverloadAndBindsLifecycle()
        {
            var node = CreateNode();

            var received = new List<int>();
            node.Subscribe<ClassMessage>(m => received.Add(m.Value));

            MessageManager.Publish(new ClassMessage { Value = 9 });
            CollectionAssert.AreEqual(new[] { 9 }, received);

            node.Destroy();

            MessageManager.Publish(new ClassMessage { Value = 10 });
            Assert.AreEqual(1, received.Count,
                "收到消息且节点销毁后自动退订,证明解析到的是带生命周期绑定的节点版重载");
        }

        [Test]
        public void NodeSubscribe_WithFilter_Works()
        {
            var node = CreateNode();
            try
            {
                var received = new List<int>();
                node.Subscribe<StructMessage>(m => m.Value > 10, m => received.Add(m.Value));

                MessageManager.Publish(new StructMessage { Value = 5 });
                MessageManager.Publish(new StructMessage { Value = 15 });

                CollectionAssert.AreEqual(new[] { 15 }, received, "订阅级过滤条件生效");
            }
            finally
            {
                node.Destroy();
            }
        }

        [Test]
        public void NodeSubscribeBuffered_ReplaysLastMessage_AndBindsLifecycle()
        {
            var node = CreateNode();

            // 先发布、后订阅:缓冲订阅应立刻重放最近一条
            MessageManager.Publish(new StructMessage { Value = 5 });

            var received = new List<int>();
            node.SubscribeBuffered<StructMessage>(m => received.Add(m.Value));

            CollectionAssert.AreEqual(new[] { 5 }, received, "新订阅者应立即收到最近一次发布的消息");

            MessageManager.Publish(new StructMessage { Value = 6 });
            CollectionAssert.AreEqual(new[] { 5, 6 }, received, "重放之后继续接收实时消息");

            // 销毁后不再收到消息 —— 这同时证明解析到的是节点版,而非 MessageManager 上不绑生命的同名扩展
            node.Destroy();

            MessageManager.Publish(new StructMessage { Value = 7 });
            CollectionAssert.AreEqual(new[] { 5, 6 }, received, "节点销毁后缓冲订阅自动取消");
        }

        [Test]
        public void NodeSubscribeBuffered_NullArguments_Throw()
        {
            var node = CreateNode();
            try
            {
                Assert.Throws<ArgumentNullException>(
                    () => NodeExtensions.SubscribeBuffered<StructMessage>((BaseNode)null, _ => { }));
                Assert.Throws<ArgumentNullException>(
                    () => node.SubscribeBuffered<StructMessage>((Action<StructMessage>)null));
            }
            finally
            {
                node.Destroy();
            }
        }

        #endregion

        #region 生命周期绑定

        [Test]
        public void NodeSubscribe_NodeDestroyed_Unsubscribes()
        {
            var node = CreateNode();
            var received = new List<int>();
            node.Subscribe<StructMessage>(m => received.Add(m.Value));

            MessageManager.Publish(new StructMessage { Value = 1 });
            Assert.AreEqual(1, received.Count);

            node.Destroy();

            MessageManager.Publish(new StructMessage { Value = 2 });
            Assert.AreEqual(1, received.Count, "节点销毁后订阅自动取消");
        }

        [Test]
        public void NodeSubscribe_NodeAlreadyDestroyed_DisposesImmediately()
        {
            var node = CreateNode();
            node.Destroy();

            var received = new List<int>();
            Assert.DoesNotThrow(() => node.Subscribe<StructMessage>(m => received.Add(m.Value)),
                "已销毁节点上订阅不应抛异常");

            MessageManager.Publish(new StructMessage { Value = 1 });
            Assert.AreEqual(0, received.Count, "订阅立即释放,不再收到消息");
        }

        #endregion

        #region 异步订阅

        [Test]
        public void NodeSubscribeAsync_ReceivesMessage()
        {
            var node = CreateNode();
            try
            {
                var received = new List<int>();
                node.SubscribeAsync<StructMessage>((m, ct) =>
                {
                    received.Add(m.Value);
                    return UniTask.CompletedTask;
                });

                MessageManager.Publish(new StructMessage { Value = 7 });

                CollectionAssert.AreEqual(new[] { 7 }, received, "同步 Publish 以 fire-and-forget 触发节点异步订阅");
            }
            finally
            {
                node.Destroy();
            }
        }

        [Test]
        public void NodeSubscribeAsync_NodeDestroyed_CancelsHandlerToken()
        {
            var node = CreateNode();

            CancellationToken observed = default;
            node.SubscribeAsync<StructMessage>((m, ct) =>
            {
                observed = ct;
                return UniTask.CompletedTask;
            });

            MessageManager.Publish(new StructMessage { Value = 1 });
            Assert.IsTrue(observed.CanBeCanceled, "处理器应收到订阅令牌");
            Assert.IsFalse(observed.IsCancellationRequested, "节点存活时令牌未取消");

            node.Destroy();

            Assert.IsTrue(observed.IsCancellationRequested, "节点销毁应取消异步处理器收到的令牌");
        }

        [Test]
        public void NodeSubscribe_NullArguments_Throw()
        {
            var node = CreateNode();
            try
            {
                Assert.Throws<ArgumentNullException>(
                    () => NodeExtensions.Subscribe<StructMessage>((BaseNode)null, _ => { }));
                Assert.Throws<ArgumentNullException>(
                    () => node.Subscribe<StructMessage>((Action<StructMessage>)null));
                Assert.Throws<ArgumentNullException>(
                    () => node.SubscribeAsync<StructMessage>(null));
            }
            finally
            {
                node.Destroy();
            }
        }

        #endregion

        #region Private Helpers

        private static TestNode CreateNode()
        {
            var node = NodeFactory.GetNode<TestNode>();
            node.Awake();
            return node;
        }

        #endregion
    }
}
