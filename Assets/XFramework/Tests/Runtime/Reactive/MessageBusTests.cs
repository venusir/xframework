using System;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using XFramework.XReactive;

namespace XFramework.XReactive.Tests
{
    /// <summary>
    /// Tests for <see cref="MessageBus"/> static API.
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
            MessageBus.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            MessageBus.Clear();
        }

        [Test]
        public void Publish_Subscribe_ReceivesMessage()
        {
            TestMessage received = null;
            var disposable = MessageBus.Subscribe<TestMessage>(msg => received = msg);

            var msg = new TestMessage { Value = 42 };
            MessageBus.Publish(msg);

            Assert.IsNotNull(received);
            Assert.AreEqual(42, received.Value);

            disposable.Dispose();
        }

        [Test]
        public void Subscribe_Disposed_NoLongerReceives()
        {
            var callCount = 0;
            var disposable = MessageBus.Subscribe<TestMessage>(_ => callCount++);

            MessageBus.Publish(new TestMessage { Value = 1 });
            Assert.AreEqual(1, callCount);

            disposable.Dispose();

            MessageBus.Publish(new TestMessage { Value = 2 });
            Assert.AreEqual(1, callCount);
        }

        [Test]
        public void Subscribe_WithFilter_OnlyReceivesMatchingMessages()
        {
            var received = 0;
            var disposable = MessageBus.Subscribe<TestMessage>(
                msg => msg.Value > 10,
                msg => received = msg.Value
            );

            MessageBus.Publish(new TestMessage { Value = 5 });
            Assert.AreEqual(0, received);

            MessageBus.Publish(new TestMessage { Value = 15 });
            Assert.AreEqual(15, received);

            disposable.Dispose();
        }

        [Test]
        public void Publish_WithKey_KeyedSubscriberReceives()
        {
            TestMessage receivedDefault = null;
            TestMessage receivedKeyed = null;

            var disposable1 = MessageBus.Subscribe<int, TestMessage>(0, msg => receivedDefault = msg);
            var disposable2 = MessageBus.Subscribe<int, TestMessage>(1, msg => receivedKeyed = msg);

            MessageBus.Publish(1, new TestMessage { Value = 99 });

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

            var disp0 = MessageBus.Subscribe<int, TestMessage>(0, _ => callCount0++);
            var disp1 = MessageBus.Subscribe<int, TestMessage>(1, _ => callCount1++);

            MessageBus.Publish(0, new TestMessage());
            Assert.AreEqual(1, callCount0);
            Assert.AreEqual(0, callCount1);

            MessageBus.Publish(1, new TestMessage());
            Assert.AreEqual(1, callCount0);
            Assert.AreEqual(1, callCount1);

            disp0.Dispose();
            disp1.Dispose();
        }

        [Test]
        public void Subscribe_WithKeyAndFilter_Works()
        {
            TestMessage received = null;
            var disposable = MessageBus.Subscribe<int, TestMessage>(
                0,
                msg => msg.Value > 10,
                msg => received = msg
            );

            MessageBus.Publish(0, new TestMessage { Value = 5 });
            Assert.IsNull(received);

            MessageBus.Publish(0, new TestMessage { Value = 20 });
            Assert.IsNotNull(received);
            Assert.AreEqual(20, received.Value);

            disposable.Dispose();
        }

        [Test]
        public void SubscribeBuffered_NewSubscriberGetsLastMessage()
        {
            // Publish first
            MessageBus.Publish(new TestMessage { Value = 77 });

            // Subscribe with buffer should immediately receive last message
            TestMessage received = null;
            var disposable = MessageBus.SubscribeBuffered<TestMessage>(msg => received = msg);

            Assert.IsNotNull(received);
            Assert.AreEqual(77, received.Value);

            disposable.Dispose();
        }

        [Test]
        public void SubscribeBuffered_WithKey_NewSubscriberGetsLastKeyedMessage()
        {
            // Publish keyed messages
            MessageBus.Publish(0, new TestMessage { Value = 10 });
            MessageBus.Publish(1, new TestMessage { Value = 20 });

            // Subscribe buffered for key 0
            TestMessage received = null;
            var disposable = MessageBus.SubscribeBuffered<int, TestMessage>(0, msg => received = msg);

            Assert.IsNotNull(received);
            Assert.AreEqual(10, received.Value);

            disposable.Dispose();
        }

        [Test]
        public void SubscribeBuffered_WithFilter_UsesFilter()
        {
            MessageBus.Publish(new TestMessage { Value = 100 });
            MessageBus.Publish(new TestMessage { Value = 5 });

            // SubscribeBuffered with filter should replay last (Value=5) but filter rejects it
            TestMessage received = null;
            var disposable = MessageBus.SubscribeBuffered<TestMessage>(
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
            MessageBus.Publish(new TestMessage { Value = 1 });

            var callCount = 0;
            TestMessage last = null;
            var disposable = MessageBus.SubscribeBuffered<TestMessage>(msg =>
            {
                callCount++;
                last = msg;
            });

            // Should receive replayed message
            Assert.AreEqual(1, callCount);
            Assert.AreEqual(1, last.Value);

            // New messages
            MessageBus.Publish(new TestMessage { Value = 2 });
            Assert.AreEqual(2, callCount);
            Assert.AreEqual(2, last.Value);

            disposable.Dispose();
        }

        [Test]
        public void Publish_NoSubscribers_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => MessageBus.Publish(new TestMessage { Value = 1 }));
        }

        [Test]
        public void Publish_Keyed_NoSubscribers_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => MessageBus.Publish(0, new TestMessage { Value = 1 }));
        }

        [Test]
        public void DifferentMessageTypes_Independent()
        {
            TestMessage testMsgReceived = null;
            AnotherMessage anotherMsgReceived = null;

            var disp1 = MessageBus.Subscribe<TestMessage>(msg => testMsgReceived = msg);
            var disp2 = MessageBus.Subscribe<AnotherMessage>(msg => anotherMsgReceived = msg);

            MessageBus.Publish(new TestMessage { Value = 10 });

            Assert.IsNotNull(testMsgReceived);
            Assert.AreEqual(10, testMsgReceived.Value);
            Assert.IsNull(anotherMsgReceived);

            MessageBus.Publish(new AnotherMessage { Text = "hello" });

            Assert.IsNotNull(anotherMsgReceived);
            Assert.AreEqual("hello", anotherMsgReceived.Text);

            disp1.Dispose();
            disp2.Dispose();
        }

        [Test]
        public void Clear_RemovesAllSubscriptions()
        {
            var callCount = 0;
            MessageBus.Subscribe<TestMessage>(_ => callCount++);

            MessageBus.Publish(new TestMessage());
            Assert.AreEqual(1, callCount);

            MessageBus.Clear();

            MessageBus.Publish(new TestMessage());
            // After Clear, the old broker is replaced; subscriptions are gone
            Assert.AreEqual(1, callCount);
        }

        [Test]
        public void Clear_ResetsRequestHandlers()
        {
            MessageBus.Register<TestRequest, TestResponse>(req =>
                UniTask.FromResult(new TestResponse { Result = req.Input * 2 }));

            MessageBus.Clear();

            Assert.Throws<InvalidOperationException>(() =>
                MessageBus.RequestAsync<TestRequest, TestResponse>(new TestRequest { Input = 5 }).GetAwaiter().GetResult());
        }

        [Test]
        public void Register_RequestAsync_ReturnsResponse()
        {
            MessageBus.Register<TestRequest, TestResponse>(req =>
                UniTask.FromResult(new TestResponse { Result = req.Input * 2 }));

            var response = MessageBus.RequestAsync<TestRequest, TestResponse>(
                new TestRequest { Input = 21 }).GetAwaiter().GetResult();

            Assert.IsNotNull(response);
            Assert.AreEqual(42, response.Result);
        }

        [Test]
        public void Register_DuplicateHandler_Throws()
        {
            MessageBus.Register<TestRequest, TestResponse>(req =>
                UniTask.FromResult(new TestResponse()));

            Assert.Throws<InvalidOperationException>(() =>
                MessageBus.Register<TestRequest, TestResponse>(req =>
                    UniTask.FromResult(new TestResponse())));
        }

        [Test]
        public void RequestAsync_NoHandlerRegistered_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                MessageBus.RequestAsync<TestRequest, TestResponse>(new TestRequest()).GetAwaiter().GetResult());
        }

        [Test]
        public void Register_AfterClear_Works()
        {
            MessageBus.Register<TestRequest, TestResponse>(req =>
                UniTask.FromResult(new TestResponse { Result = 1 }));

            MessageBus.Clear();

            // Should allow registering again after Clear
            Assert.DoesNotThrow(() =>
                MessageBus.Register<TestRequest, TestResponse>(req =>
                    UniTask.FromResult(new TestResponse { Result = 2 })));

            var response = MessageBus.RequestAsync<TestRequest, TestResponse>(
                new TestRequest()).GetAwaiter().GetResult();
            Assert.AreEqual(2, response.Result);
        }

        [Test]
        public void AddFilter_BlocksMatchingMessage()
        {
            TestMessage received = null;
            var disposable = MessageBus.Subscribe<TestMessage>(msg => received = msg);

            // Add filter that blocks messages with Value < 0
            MessageBus.AddFilter<TestMessage>(new BlockNegativeFilter());

            MessageBus.Publish(new TestMessage { Value = -1 });
            Assert.IsNull(received);

            MessageBus.Publish(new TestMessage { Value = 10 });
            Assert.IsNotNull(received);
            Assert.AreEqual(10, received.Value);

            disposable.Dispose();
        }

        [Test]
        public void AddFilter_MultipleFilters_ChainCorrectly()
        {
            var received = false;
            var disposable = MessageBus.Subscribe<TestMessage>(_ => received = true);

            // Add two filters: one blocks positive, one blocks negative
            MessageBus.AddFilter<TestMessage>(new BlockPositiveFilter());
            MessageBus.AddFilter<TestMessage>(new BlockNegativeFilter());

            // BlockPositiveFilter blocks positive, BlockNegativeFilter blocks negative
            // A message with Value=0 should pass through both
            MessageBus.Publish(new TestMessage { Value = 0 });
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