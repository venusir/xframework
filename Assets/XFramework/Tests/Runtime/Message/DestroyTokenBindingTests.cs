using System;
using System.Threading;
using Cysharp.Threading.Tasks;
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
            var subscriber = new TokenOwningSubscriber();            subscriber.Destroy();

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

        [Test]
        public void SubscribeBuffered_KeyedWithFilter_DestroyTokenOwner_UnsubscribesOnCancel()
        {
            var subscriber = new TokenOwningSubscriber();

            // 先发一条通过过滤的，用于验证「订阅即重放最近一条」在带过滤的键值重载上同样生效
            MessageManager.Publish("Score", new TestMessage { Value = 15 });

            subscriber.SubscribeBuffered<string, TestMessage>("Score", msg => msg.Value > 10, subscriber.Handle);
            Assert.AreEqual(1, subscriber.ReceivedCount, "缓冲订阅应立即重放最近一条通过过滤的消息");

            subscriber.Destroy();
            MessageManager.Publish("Score", new TestMessage { Value = 25 });
            Assert.AreEqual(1, subscriber.ReceivedCount, "令牌取消后应自动退订");
        }

        [Test]
        public void SubscribeBuffered_KeyedWithFilter_FilterAppliesToReplayAndLive()
        {
            // 与静态版 SubscribeBuffered_WithKeyAndFilter_ReplayAndLiveBothFiltered 同一组断言，
            // 改经扩展面复跑：证明它转发到的是「带过滤」的那个重载，而不是相邻同形的无过滤重载
            MessageManager.Publish("Score", new TestMessage { Value = 5 });
            MessageManager.Publish("Score", new TestMessage { Value = 15 });

            var subscriber = new TokenOwningSubscriber();
            subscriber.SubscribeBuffered<string, TestMessage>("Score", msg => msg.Value > 10, subscriber.Handle);

            Assert.AreEqual(1, subscriber.ReceivedCount, "重放同样走过滤:5 被拦、15 通过");

            MessageManager.Publish("Score", new TestMessage { Value = 25 });
            MessageManager.Publish("Score", new TestMessage { Value = 1 });

            Assert.AreEqual(2, subscriber.ReceivedCount, "实时同样走过滤:25 通过、1 被拦");
        }

        [Test]
        public void DisposeSubscription_AlsoDisposesCancellationRegistration()
        {
            // MSG-004：长寿命 IDestroyCancellationToken 所有者反复订阅/退订时，登记必须随句柄回收，
            // 否则 CancellationTokenSource 上的 registration 会持续累积，直到令牌取消。
            using var cts = new CancellationTokenSource();
            var subscriber = new RawTokenSubscriber(cts.Token);
            var probe = new CountingDisposable();

            var handle = MessageManager.TryBindToDestroy(subscriber, probe);
            Assert.IsNotNull(handle, "绑定时必须返回可释放的合并句柄");

            handle.Dispose();
            Assert.AreEqual(1, probe.DisposeCount, "句柄释放应释放订阅");

            cts.Cancel();
            Assert.AreEqual(1, probe.DisposeCount,
                "令牌取消时不得再次释放已释放的订阅——登记必须随句柄一起回收（MSG-004）");
        }

        [Test]
        public void Subscribe_AfterOwnerDisposed_DoesNotThrow()
        {            // 钉住随包 XML 示例的修正形状：令牌必须在构造时缓存。
            // 旧示例的 `=> _cts.Token` 在 Dispose 之后再订阅会抛 ObjectDisposedException——
            // 改前必红：把本类的 getter 换回旧形状，本用例即红。
            var subscriber = new DisposedTokenSourceSubscriber();
            subscriber.Dispose();

            Assert.DoesNotThrow(() => subscriber.Subscribe<TestMessage>(subscriber.Handle),
                "对象销毁后再订阅不应抛 ObjectDisposedException——令牌应已缓存且处于取消态");

            MessageManager.Publish(new TestMessage { Value = 1 });
            Assert.AreEqual(0, subscriber.ReceivedCount, "令牌已取消，订阅应被立即释放");
        }

        #region 扩展 SubscribeAsync 四个重载（MSG-008）

        [Test]
        public void SubscribeAsync_Extension_Plain_BindsToDestroyToken()
        {
            var subscriber = new TokenOwningSubscriber();
            subscriber.SubscribeAsync<TestMessage>((msg, ct) =>
            {
                subscriber.Handle(msg);
                return UniTask.CompletedTask;
            });

            MessageManager.Publish(new TestMessage { Value = 1 });
            Assert.AreEqual(1, subscriber.ReceivedCount, "前提：异步订阅应正常投递");

            subscriber.Destroy();
            MessageManager.Publish(new TestMessage { Value = 2 });
            Assert.AreEqual(1, subscriber.ReceivedCount, "令牌取消后应自动退订");
        }

        [Test]
        public void SubscribeAsync_Extension_Filtered_BindsToDestroyToken()
        {
            var subscriber = new TokenOwningSubscriber();
            subscriber.SubscribeAsync<TestMessage>(
                m => m.Value > 10,
                (msg, ct) =>
                {
                    subscriber.Handle(msg);
                    return UniTask.CompletedTask;
                });

            MessageManager.Publish(new TestMessage { Value = 1 });
            Assert.AreEqual(0, subscriber.ReceivedCount, "过滤条件外不得投递");

            MessageManager.Publish(new TestMessage { Value = 11 });
            Assert.AreEqual(1, subscriber.ReceivedCount);

            subscriber.Destroy();
            MessageManager.Publish(new TestMessage { Value = 12 });
            Assert.AreEqual(1, subscriber.ReceivedCount, "令牌取消后应自动退订");
        }

        [Test]
        public void SubscribeAsync_Extension_Keyed_BindsToDestroyToken()
        {
            var subscriber = new TokenOwningSubscriber();
            subscriber.SubscribeAsync<string, TestMessage>(
                "k",
                (msg, ct) =>
                {
                    subscriber.Handle(msg);
                    return UniTask.CompletedTask;
                });

            MessageManager.Publish("k", new TestMessage { Value = 1 });
            Assert.AreEqual(1, subscriber.ReceivedCount, "前提：键值订阅应正常投递");

            subscriber.Destroy();
            MessageManager.Publish("k", new TestMessage { Value = 2 });
            Assert.AreEqual(1, subscriber.ReceivedCount, "令牌取消后应自动退订");
        }

        [Test]
        public void SubscribeAsync_Extension_KeyedFiltered_BindsToDestroyToken()
        {
            var subscriber = new TokenOwningSubscriber();
            subscriber.SubscribeAsync<string, TestMessage>(
                "k",
                m => m.Value > 10,
                (msg, ct) =>
                {
                    subscriber.Handle(msg);
                    return UniTask.CompletedTask;
                });

            MessageManager.Publish("k", new TestMessage { Value = 1 });
            Assert.AreEqual(0, subscriber.ReceivedCount, "过滤条件外不得投递");

            MessageManager.Publish("k", new TestMessage { Value = 11 });
            Assert.AreEqual(1, subscriber.ReceivedCount);

            subscriber.Destroy();
            MessageManager.Publish("k", new TestMessage { Value = 12 });
            Assert.AreEqual(1, subscriber.ReceivedCount, "令牌取消后应自动退订");
        }

        #endregion

        /// <summary>按修正后的 XML 示例实现：令牌在构造时缓存，<see cref="Dispose"/> 后可安全读取。</summary>
        private sealed class DisposedTokenSourceSubscriber : IMessageSubscriber, IDestroyCancellationToken
        {
            private readonly CancellationTokenSource _cts = new CancellationTokenSource();
            private readonly CancellationToken _token;

            public DisposedTokenSourceSubscriber()
            {
                _token = _cts.Token;
            }

            public int ReceivedCount { get; private set; }

            public CancellationToken DestroyCancellationToken => _token;

            public void Dispose()
            {
                _cts.Cancel();
                _cts.Dispose();
            }

            public void Handle(TestMessage message) => ReceivedCount++;
        }

        private sealed class PlainSubscriber : IMessageSubscriber
        {
            public int ReceivedCount { get; private set; }

            public void Handle(TestMessage message) => ReceivedCount++;
        }

        /// <summary>只提供销毁令牌的订阅者；用于直接调用 <c>TryBindToDestroy</c> 的白盒用例。</summary>
        private sealed class RawTokenSubscriber : IDestroyCancellationToken
        {
            private readonly CancellationToken _token;

            public RawTokenSubscriber(CancellationToken token) => _token = token;

            public CancellationToken DestroyCancellationToken => _token;
        }

        /// <summary>计数式句柄：记录 Dispose 次数，用于观察登记是否在取消时二次释放。</summary>
        private sealed class CountingDisposable : IDisposable
        {
            public int DisposeCount;

            public void Dispose() => DisposeCount++;
        }
    }
}
