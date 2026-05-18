using System;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using XFramework.XReactive;

namespace XFramework.XReactive.Tests
{
    /// <summary>
    /// Tests for <see cref="MessageManager"/> static API.
    /// Covers publish/subscribe, keyed messages, buffered subscriptions,
    /// filters, async handling, and request/response pattern.
    /// </summary>
    [TestFixture]
    public class MessageBusTests
    {
        private sealed class TestMessage
        {
            public int Value { get; set; }
        }

        private sealed class AnotherMessage
        {
            public string Text { get; set; }
        }

        private sealed class TestRequest
        {
            public int Input { get; set; }
        }

        private sealed class TestResponse
        {
            public int Result { get; set; }
        }

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

        [Test]
        public void Publish_Subscribe_ReceivesMessage()
        {
            TestMessage received = null;
            var disposable = MessageManager.Subscribe<TestMessage>(msg => received = msg);

            var msg = new TestMessage { Value = 42 };
            MessageManager.Publish(msg);

            Assert.IsNotNull(received);
            Assert.AreEqual(42, received.Value);

            disposable.Dispose();
        }

        [Test]
        public void Subscribe_Disposed_NoLongerReceives()
        {
            var callCount = 0;
            var disposable = MessageManager.Subscribe<TestMessage>(_ => callCount++);

            MessageManager.Publish(new TestMessage { Value = 1 });
            Assert.AreEqual(1, callCount);

            disposable.Dispose();

            MessageManager.Publish(new TestMessage { Value = 2 });
            Assert.AreEqual(1, callCount);
        }

        [Test]
        public void Subscribe_WithFilter_OnlyReceivesMatchingMessages()
        {
            var received = 0;
            var disposable = MessageManager.Subscribe<TestMessage>(
                msg => msg.Value > 10,
                msg => received = msg.Value
            );

            MessageManager.Publish(new TestMessage { Value = 5 });
            Assert.AreEqual(0, received);

            MessageManager.Publish(new TestMessage { Value = 15 });
            Assert.AreEqual(15, received);

            disposable.Dispose();
        }

        [Test]
        public void Publish_WithKey_KeyedSubscriberReceives()
        {
            TestMessage receivedDefault = null;
            TestMessage receivedKeyed = null;

            var disposable1 = MessageManager.Subscribe<int, TestMessage>(0, msg => receivedDefault = msg);
            var disposable2 = MessageManager.Subscribe<int, TestMessage>(1, msg => receivedKeyed = msg);

            MessageManager.Publish(1, new TestMessage { Value = 99 });

            Assert.IsNull(receivedDefault);
            Assert.IsNotNull(receivedKeyed);
            Assert.AreEqual(99, receivedKeyed.Value);

            disposable1.Dispose();
            disposable2.Dispose();
        }

        [Test]
        public void Publish_WithKey_OnlyMatchingKeyReceives()
        {
            var callCount0 = 0;
            var callCount1 = 0;

            var disp0 = MessageManager.Subscribe<int, TestMessage>(0, _ => callCount0++);
            var disp1 = MessageManager.Subscribe<int, TestMessage>(1, _ => callCount1++);

            MessageManager.Publish(0, new TestMessage());
            Assert.AreEqual(1, callCount0);
            Assert.AreEqual(0, callCount1);

            MessageManager.Publish(1, new TestMessage());
            Assert.AreEqual(1, callCount0);
            Assert.AreEqual(1, callCount1);

            disp0.Dispose();
            disp1.Dispose();
        }

        [Test]
        public void Subscribe_WithKeyAndFilter_Works()
        {
            TestMessage received = null;
            var disposable = MessageManager.Subscribe<int, TestMessage>(
                0,
                msg => msg.Value > 10,
                msg => received = msg
            );

            MessageManager.Publish(0, new TestMessage { Value = 5 });
            Assert.IsNull(received);

            MessageManager.Publish(0, new TestMessage { Value = 20 });
            Assert.IsNotNull(received);
            Assert.AreEqual(20, received.Value);

            disposable.Dispose();
        }

        [Test]
        public void SubscribeBuffered_NewSubscriberGetsLastMessage()
        {
            // Publish first
            MessageManager.Publish(new TestMessage { Value = 77 });

            // Subscribe with buffer should immediately receive last message
            TestMessage received = null;
            var disposable = MessageManager.SubscribeBuffered<TestMessage>(msg => received = msg);

            Assert.IsNotNull(received);
            Assert.AreEqual(77, received.Value);

            disposable.Dispose();
        }

        [Test]
        public void SubscribeBuffered_WithKey_NewSubscriberGetsLastKeyedMessage()
        {
            // Publish keyed messages
            MessageManager.Publish(0, new TestMessage { Value = 10 });
            MessageManager.Publish(1, new TestMessage { Value = 20 });

            // Subscribe buffered for key 0
            TestMessage received = null;
            var disposable = MessageManager.SubscribeBuffered<int, TestMessage>(0, msg => received = msg);

            Assert.IsNotNull(received);
            Assert.AreEqual(10, received.Value);

            disposable.Dispose();
        }

        [Test]
        public void SubscribeBuffered_WithFilter_UsesFilter()
        {
            MessageManager.Publish(new TestMessage { Value = 100 });
            MessageManager.Publish(new TestMessage { Value = 5 });

            // SubscribeBuffered with filter should replay last (Value=5) but filter rejects it
            TestMessage received = null;
            var disposable = MessageManager.SubscribeBuffered<TestMessage>(
                msg => msg.Value > 50,
                msg => received = msg
            );

            // The buffered replay fires before the filter, so the handler sees the last message.
            // With the implementation approach, it filters in the Subscribe lambda.
            // Last published was Value=5, which should not pass filter.
            Assert.IsNull(received);

            disposable.Dispose();
        }

        [Test]
        public void SubscribeBuffered_ReplayThenNewMessages()
        {
            MessageManager.Publish(new TestMessage { Value = 1 });

            var callCount = 0;
            TestMessage last = null;
            var disposable = MessageManager.SubscribeBuffered<TestMessage>(msg =>
            {
                callCount++;
                last = msg;
            });

            // Should receive replayed message
            Assert.AreEqual(1, callCount);
            Assert.AreEqual(1, last.Value);

            // New messages
            MessageManager.Publish(new TestMessage { Value = 2 });
            Assert.AreEqual(2, callCount);
            Assert.AreEqual(2, last.Value);

            disposable.Dispose();
        }

        [Test]
        public void Publish_NoSubscribers_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => MessageManager.Publish(new TestMessage { Value = 1 }));
        }

        [Test]
        public void Publish_Keyed_NoSubscribers_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => MessageManager.Publish(0, new TestMessage { Value = 1 }));
        }

        [Test]
        public void DifferentMessageTypes_Independent()
        {
            TestMessage testMsgReceived = null;
            AnotherMessage anotherMsgReceived = null;

            var disp1 = MessageManager.Subscribe<TestMessage>(msg => testMsgReceived = msg);
            var disp2 = MessageManager.Subscribe<AnotherMessage>(msg => anotherMsgReceived = msg);

            MessageManager.Publish(new TestMessage { Value = 10 });

            Assert.IsNotNull(testMsgReceived);
            Assert.AreEqual(10, testMsgReceived.Value);
            Assert.IsNull(anotherMsgReceived);

            MessageManager.Publish(new AnotherMessage { Text = "hello" });

            Assert.IsNotNull(anotherMsgReceived);
            Assert.AreEqual("hello", anotherMsgReceived.Text);

            disp1.Dispose();
            disp2.Dispose();
        }

        [Test]
        public void Clear_RemovesAllSubscriptions()
        {
            var callCount = 0;
            MessageManager.Subscribe<TestMessage>(_ => callCount++);

            MessageManager.Publish(new TestMessage());
            Assert.AreEqual(1, callCount);

            MessageManager.Clear();

            MessageManager.Publish(new TestMessage());
            // After Clear, the old broker is replaced; subscriptions are gone
            Assert.AreEqual(1, callCount);
        }

        [Test]
        public void Clear_ResetsRequestHandlers()
        {
            MessageManager.Register<TestRequest, TestResponse>(req =>
                UniTask.FromResult(new TestResponse { Result = req.Input * 2 }));

            MessageManager.Clear();

            Assert.Throws<InvalidOperationException>(() =>
                MessageManager.RequestAsync<TestRequest, TestResponse>(new TestRequest { Input = 5 }).GetAwaiter().GetResult());
        }

        [Test]
        public void Register_RequestAsync_ReturnsResponse()
        {
            MessageManager.Register<TestRequest, TestResponse>(req =>
                UniTask.FromResult(new TestResponse { Result = req.Input * 2 }));

            var response = MessageManager.RequestAsync<TestRequest, TestResponse>(
                new TestRequest { Input = 21 }).GetAwaiter().GetResult();

            Assert.IsNotNull(response);
            Assert.AreEqual(42, response.Result);
        }

        [Test]
        public void Register_DuplicateHandler_Throws()
        {
            MessageManager.Register<TestRequest, TestResponse>(req =>
                UniTask.FromResult(new TestResponse()));

            Assert.Throws<InvalidOperationException>(() =>
                MessageManager.Register<TestRequest, TestResponse>(req =>
                    UniTask.FromResult(new TestResponse())));
        }

        [Test]
        public void RequestAsync_NoHandlerRegistered_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                MessageManager.RequestAsync<TestRequest, TestResponse>(new TestRequest()).GetAwaiter().GetResult());
        }

        [Test]
        public void Register_AfterClear_Works()
        {
            MessageManager.Register<TestRequest, TestResponse>(req =>
                UniTask.FromResult(new TestResponse { Result = 1 }));

            MessageManager.Clear();

            // Should allow registering again after Clear
            Assert.DoesNotThrow(() =>
                MessageManager.Register<TestRequest, TestResponse>(req =>
                    UniTask.FromResult(new TestResponse { Result = 2 })));

            var response = MessageManager.RequestAsync<TestRequest, TestResponse>(
                new TestRequest()).GetAwaiter().GetResult();
            Assert.AreEqual(2, response.Result);
        }

        [Test]
        public void AddFilter_BlocksMatchingMessage()
        {
            TestMessage received = null;
            var disposable = MessageManager.Subscribe<TestMessage>(msg => received = msg);

            // Add filter that blocks messages with Value < 0
            MessageManager.AddFilter<TestMessage>(new BlockNegativeFilter());

            MessageManager.Publish(new TestMessage { Value = -1 });
            Assert.IsNull(received);

            MessageManager.Publish(new TestMessage { Value = 10 });
            Assert.IsNotNull(received);
            Assert.AreEqual(10, received.Value);

            disposable.Dispose();
        }

        [Test]
        public void AddFilter_MultipleFilters_ChainCorrectly()
        {
            var received = false;
            var disposable = MessageManager.Subscribe<TestMessage>(_ => received = true);

            // Add two filters: one blocks positive, one blocks negative
            MessageManager.AddFilter<TestMessage>(new BlockPositiveFilter());
            MessageManager.AddFilter<TestMessage>(new BlockNegativeFilter());

            // BlockPositiveFilter blocks positive, BlockNegativeFilter blocks negative
            // A message with Value=0 should pass through both
            MessageManager.Publish(new TestMessage { Value = 0 });
            Assert.IsTrue(received);

            disposable.Dispose();
        }

        /// <summary>
        /// Filter that blocks messages where Value < 0.
        /// </summary>
        private sealed class BlockNegativeFilter : IMessageFilter<TestMessage>
        {
            public void Invoke(TestMessage msg, Action<TestMessage> next)
            {
                if (msg.Value >= 0)
                {
                    next(msg);
                }
            }
        }

        /// <summary>
        /// Filter that blocks messages where Value > 0.
        /// </summary>
        private sealed class BlockPositiveFilter : IMessageFilter<TestMessage>
        {
            public void Invoke(TestMessage msg, Action<TestMessage> next)
            {
                if (msg.Value <= 0)
                {
                    next(msg);
                }
            }
        }
    }
}