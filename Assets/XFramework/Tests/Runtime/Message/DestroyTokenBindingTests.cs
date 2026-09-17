using System;
using System.Threading;
using NUnit.Framework;
using XFramework.XMessage;

namespace XFramework.XMessage.Tests
{
    /// <summary>
    /// 锁定「订阅自动绑定订阅者销毁时机」的契约。
    /// <para>核心场景：<b>非 MonoBehaviour</b> 对象实现 <see cref="IDestroyCancellationToken"/> 后，
    /// 令牌取消即自动退订。这是删除节点系统后，普通 C# 对象获得生命周期绑定的正规途径
    /// （此前只有节点树的 <c>NodeExtensions.Subscribe</c> 提供，而 MessageManager 自己的文档承诺了却没实现）。</para>
    /// </summary>
    [TestFixture]
    public class DestroyTokenBindingTests
    {
        #region Test Doubles

        private sealed class TestMessage
        {
            public int Value { get; set; }
        }

        /// <summary>订阅者 + 销毁令牌持有者。非 MonoBehaviour，正是本组用例要覆盖的形态。</summary>
        private sealed class TokenOwningSubscriber : IMessageSubscriber, IDestroyCancellationToken
        {
            private readonly CancellationTokenSource _cts = new CancellationTokenSource();

            public int ReceivedCount { get; private set; }

            public CancellationToken DestroyCancellationToken => _cts.Token;

            /// <summary>模拟对象销毁：取消令牌。</summary>
            public void Destroy()
            {
                if (!_cts.IsCancellationRequested)
                {
                    _cts.Cancel();
                }
            }

            public void Handle(TestMessage message) => ReceivedCount++;
        }

        #endregion

        #region Setup / Teardown

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

        #endregion

        [Test]
        public void Subscribe_DestroyTokenOwner_UnsubscribesOnCancel()
        {
            var subscriber = new TokenOwningSubscriber();
            subscriber.Subscribe<TestMessage>(subscriber.Handle);

            MessageManager.Publish(new TestMessage { Value = 1 });
            Assert.AreEqual(1, subscriber.ReceivedCount, "销毁前应正常收到消息");

            subscriber.Destroy();
            MessageManager.Publish(new TestMessage { Value = 2 });
            Assert.AreEqual(1, subscriber.ReceivedCount, "令牌取消后应自动退订");
        }

        [Test]
        public void Subscribe_AlreadyCancelled_DisposesImmediately()
        {
            var subscriber = new TokenOwningSubscriber();
            subscriber.Destroy();

            subscriber.Subscribe<TestMessage>(subscriber.Handle);
            MessageManager.Publish(new TestMessage { Value = 1 });

            Assert.AreEqual(0, subscriber.ReceivedCount, "订阅时令牌已取消，应不登记任何订阅");
        }

        [Test]
        public void Subscribe_Keyed_DestroyTokenOwner_UnsubscribesOnCancel()
        {
            var subscriber = new TokenOwningSubscriber();
            subscriber.Subscribe<int, TestMessage>(7, subscriber.Handle);

            MessageManager.Publish(7, new TestMessage { Value = 1 });
            Assert.AreEqual(1, subscriber.ReceivedCount, "销毁前应正常收到该 Key 的消息");

            subscriber.Destroy();
            MessageManager.Publish(7, new TestMessage { Value = 2 });
            Assert.AreEqual(1, subscriber.ReceivedCount, "令牌取消后应自动退订");
        }

        [Test]
        public void SubscribeBuffered_DestroyTokenOwner_UnsubscribesOnCancel()
        {
            var subscriber = new TokenOwningSubscriber();

            // 先发一条，用于验证「订阅即重放最近一条」的缓冲语义确实生效过
            MessageManager.Publish(new TestMessage { Value = 1 });

            subscriber.SubscribeBuffered<TestMessage>(subscriber.Handle);
            Assert.AreEqual(1, subscriber.ReceivedCount, "缓冲订阅应立即重放最近一条消息");

            subscriber.Destroy();
            MessageManager.Publish(new TestMessage { Value = 2 });
            Assert.AreEqual(1, subscriber.ReceivedCount, "令牌取消后应自动退订");
        }

        [Test]
        public void Subscribe_PlainObjectWithoutToken_StaysSubscribed()
        {
            // 对照组：不实现 IDestroyCancellationToken 的订阅者不会被自动退订——
            // 契约边界所在，避免将来有人误以为「任何对象都会自动解绑」
            var subscriber = new PlainSubscriber();
            subscriber.Subscribe<TestMessage>(subscriber.Handle);

            MessageManager.Publish(new TestMessage { Value = 1 });
            MessageManager.Publish(new TestMessage { Value = 2 });

            Assert.AreEqual(2, subscriber.ReceivedCount);
        }

        private sealed class PlainSubscriber : IMessageSubscriber
        {
            public int ReceivedCount { get; private set; }

            public void Handle(TestMessage message) => ReceivedCount++;
        }
    }
}
