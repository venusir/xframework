using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XMessage;

namespace XFramework.XMessage.Tests
{
    /// <summary>
    /// Tests for parameter guarding, filter management, and keyed-channel isolation.
    /// <para>
    /// 键值隔离一节锁定的是:同一消息类型可配多种 Key 类型,二者不得互相串道
    /// (历史上的实现只以消息类型为键,会把后者的存储强转成前者的类型并抛 InvalidCastException)。
    /// </para>
    /// </summary>
    [TestFixture]
    public class MessageApiSurfaceTests
    {
        #region Test Doubles

        private sealed class TestMessage
        {
            public int Value { get; set; }
        }

        /// <summary>拦截负值的全局过滤器。</summary>
        private sealed class BlockNegativeFilter : IMessageFilter<TestMessage>
        {
            public void Invoke(TestMessage message, Action<TestMessage> next)
            {
                if (message.Value >= 0) next(message);
            }
        }

        /// <summary>总是抛异常的全局过滤器,用于验证异常隔离与日志前缀。</summary>
        private sealed class ThrowingFilter : IMessageFilter<TestMessage>
        {
            public void Invoke(TestMessage message, Action<TestMessage> next)
                => throw new InvalidOperationException("filter boom");
        }

        private sealed class TestRequest
        {
            public int Input { get; set; }
        }

        private sealed class TestResponse
        {
            public int Result { get; set; }
        }

        private sealed class AnotherResponse
        {
            public int Result { get; set; }
        }

        /// <summary>未注册处理器的请求类型,用于验证 HasHandler 的按类型隔离。</summary>
        private sealed class AnotherRequest
        {
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

        #region 参数判空

        [Test]
        public void Subscribe_NullHandler_Throws()
        {
            var ex = Assert.Throws<ArgumentNullException>(
                () => MessageManager.Subscribe<TestMessage>((Action<TestMessage>)null));
            Assert.AreEqual("handler", ex.ParamName, "参数名应指向 handler 而非底层事件流的 onNext");
        }

        [Test]
        public void Subscribe_WithFilter_NullArguments_Throw()
        {
            // 首参必须显式转型:null 字面量在静态重载 (Predicate, Action) 与
            // 扩展方法的静态调用形式 (IMessageSubscriber, Action) 之间无法裁决(CS0121)
            Assert.Throws<ArgumentNullException>(
                () => MessageManager.Subscribe<TestMessage>((Predicate<TestMessage>)null, _ => { }));
            Assert.Throws<ArgumentNullException>(
                () => MessageManager.Subscribe<TestMessage>(_ => true, null));
        }

        [Test]
        public void SubscribeKeyed_NullHandler_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => MessageManager.Subscribe<string, TestMessage>("k", (Action<TestMessage>)null));
            Assert.Throws<ArgumentNullException>(
                () => MessageManager.Subscribe<string, TestMessage>("k", null, _ => { }));
            Assert.Throws<ArgumentNullException>(
                () => MessageManager.Subscribe<string, TestMessage>("k", _ => true, null));
        }

        [Test]
        public void SubscribeBuffered_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(
                () => MessageManager.SubscribeBuffered<TestMessage>((Action<TestMessage>)null));
            Assert.Throws<ArgumentNullException>(
                () => MessageManager.SubscribeBuffered<TestMessage>((Predicate<TestMessage>)null, _ => { }));
            Assert.Throws<ArgumentNullException>(
                () => MessageManager.SubscribeBuffered<string, TestMessage>("k", (Action<TestMessage>)null));
        }

        [Test]
        public void SubscribeAsync_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(
                () => MessageManager.SubscribeAsync<TestMessage>(
                    (Func<TestMessage, CancellationToken, UniTask>)null));
            Assert.Throws<ArgumentNullException>(
                () => MessageManager.SubscribeAsync<TestMessage>(
                    (Predicate<TestMessage>)null, (_, ct) => UniTask.CompletedTask));
            Assert.Throws<ArgumentNullException>(
                () => MessageManager.SubscribeAsync<TestMessage>(_ => true, null));
            Assert.Throws<ArgumentNullException>(
                () => MessageManager.SubscribeAsync<string, TestMessage>("k", (Func<TestMessage, CancellationToken, UniTask>)null));
            Assert.Throws<ArgumentNullException>(
                () => MessageManager.SubscribeAsync<string, TestMessage>("k", null, (_, ct) => UniTask.CompletedTask));
        }

        /// <summary>
        /// 判空必须先于判令牌:若令牌守卫排在前面,传入已取消令牌时会把 null 处理器静默放行,
        /// 破坏「null 参数一律抛 ArgumentNullException」的公开契约。
        /// </summary>
        [Test]
        public void SubscribeAsync_NullHandlerWithCancelledToken_StillThrows()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<ArgumentNullException>(
                () => MessageManager.SubscribeAsync<TestMessage>(
                    (Func<TestMessage, CancellationToken, UniTask>)null, cts.Token),
                "已取消令牌不得吞掉类型级订阅的 null 处理器异常");
            Assert.Throws<ArgumentNullException>(
                () => MessageManager.SubscribeAsync<string, TestMessage>(
                    "k", (Func<TestMessage, CancellationToken, UniTask>)null, cts.Token),
                "已取消令牌不得吞掉键值订阅的 null 处理器异常");
        }

        [Test]
        public void AddFilter_NullFilter_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => MessageManager.AddFilter<TestMessage>(null));
            Assert.Throws<ArgumentNullException>(() => MessageManager.RemoveFilter<TestMessage>(null));
        }

        #endregion

        #region 过滤器管理

        [Test]
        public void RemoveFilter_StopsBlocking_And_SecondCallReturnsFalse()
        {
            var filter = new BlockNegativeFilter();
            MessageManager.AddFilter(filter);

            var received = new List<int>();
            MessageManager.Subscribe<TestMessage>(msg => received.Add(msg.Value));

            MessageManager.Publish(new TestMessage { Value = -1 });
            Assert.AreEqual(0, received.Count, "过滤器应拦截负值");

            Assert.IsTrue(MessageManager.RemoveFilter<TestMessage>(filter), "移除已注册的过滤器应返回 true");

            MessageManager.Publish(new TestMessage { Value = -1 });
            CollectionAssert.AreEqual(new[] { -1 }, received, "移除后消息应放行");

            Assert.IsFalse(MessageManager.RemoveFilter<TestMessage>(filter), "重复移除应返回 false");
        }

        [Test]
        public void ClearFilters_RemovesAllOfType_ReturnsCount()
        {
            MessageManager.AddFilter<TestMessage>(new BlockNegativeFilter());
            MessageManager.AddFilter<TestMessage>(new BlockNegativeFilter());

            Assert.AreEqual(2, MessageManager.ClearFilters<TestMessage>(), "应移除同类型的全部过滤器");
            Assert.AreEqual(0, MessageManager.ClearFilters<TestMessage>(), "已清空后再调用应返回 0");

            var received = new List<int>();
            MessageManager.Subscribe<TestMessage>(msg => received.Add(msg.Value));
            MessageManager.Publish(new TestMessage { Value = -5 });

            CollectionAssert.AreEqual(new[] { -5 }, received, "过滤器清空后负值应放行");
        }

        [Test]
        public void GlobalFilter_Throws_LogsWithPrefix_AndMessageBlocked()
        {
            MessageManager.AddFilter<TestMessage>(new ThrowingFilter());

            var received = new List<int>();
            MessageManager.Subscribe<TestMessage>(msg => received.Add(msg.Value));

            // 过滤器异常经 broker 的 try/catch 兜底,日志须带 [Message] 前缀(与订阅回调日志同形)
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("[Message] Global filter threw exception")));
            MessageManager.Publish(new TestMessage { Value = 1 });
            Assert.AreEqual(0, received.Count, "过滤器抛异常时该条消息被拦截");

            // 过滤器不被移除,后续消息仍会进入过滤器
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("[Message] Global filter threw exception")));
            Assert.DoesNotThrow(() => MessageManager.Publish(new TestMessage { Value = 2 }));
            Assert.AreEqual(0, received.Count);
        }

        #endregion

        #region 请求-响应

        [Test]
        public void Register_NullHandler_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                MessageManager.Register<TestRequest, TestResponse>(null));
        }

        [Test]
        public void Unregister_RemovesHandler_ThenRequestAsyncThrows()
        {
            MessageManager.Register<TestRequest, TestResponse>(
                (req, ct) => UniTask.FromResult(new TestResponse { Result = req.Input }));

            Assert.IsTrue(MessageManager.Unregister<TestRequest, TestResponse>(),
                "移除已注册的处理器应返回 true");
            Assert.IsFalse(MessageManager.Unregister<TestRequest, TestResponse>(),
                "重复移除应返回 false");

            Assert.Throws<InvalidOperationException>(() =>
                MessageManager.RequestAsync<TestRequest, TestResponse>(new TestRequest()).GetAwaiter().GetResult());
        }

        [Test]
        public void Register_SameRequestType_DifferentResponseType_StillThrows()
        {
            MessageManager.Register<TestRequest, TestResponse>(
                (req, ct) => UniTask.FromResult(new TestResponse()));

            // 处理器表的键只取请求类型:换一个响应类型仍算重复注册
            Assert.Throws<InvalidOperationException>(() =>
                MessageManager.Register<TestRequest, AnotherResponse>(
                    (req, ct) => UniTask.FromResult(new AnotherResponse())));
        }

        [Test]
        public void RequestAsync_ForwardsCancellationTokenToHandler()
        {
            // 转发用「令牌同一性」验证:处理器收到的必须就是调用方传入的那个令牌。
            // 不能再用「已取消的令牌」验证——附加外部取消之后,那样的调用会直接抛 OCE
            // (见 RequestAsync_CancelledWhileInFlight_ThrowsOperationCanceled),两者已不可兼得。
            CancellationToken observed = default;
            MessageManager.Register<TestRequest, TestResponse>((req, ct) =>
            {
                observed = ct;
                return UniTask.FromResult(new TestResponse { Result = req.Input });
            });

            // 处理器同步完成,直接取值不会死锁(不涉及依赖 PlayerLoop 的异步等待)
            using var cts = new CancellationTokenSource();

            var response = MessageManager.RequestAsync<TestRequest, TestResponse>(
                new TestRequest { Input = 7 }, cts.Token).GetAwaiter().GetResult();

            Assert.AreEqual(7, response.Result);
            Assert.AreEqual(cts.Token, observed, "处理器应收到调用方原样转发的同一个令牌");
        }

        [Test]
        public void RequestAsync_CancelledWhileInFlight_ThrowsOperationCanceled()
        {
            // 最坏情况:处理器完全不响应令牌(在途等待一个永不完成的网关)。
            // 调用方仍须能靠自己的令牌脱身——这正是附加外部取消存在的理由,缺了它本用例会永久挂起。
            var gate = new UniTaskCompletionSource();
            var handlerEntered = false;
            MessageManager.Register<TestRequest, TestResponse>(async (req, ct) =>
            {
                handlerEntered = true;
                await gate.Task;
                return new TestResponse();
            });

            using var cts = new CancellationTokenSource();

            // async 处理器同步执行到首个 await,故此行返回时处理器已在途
            var task = MessageManager.RequestAsync<TestRequest, TestResponse>(
                new TestRequest(), cts.Token);
            Assert.IsTrue(handlerEntered, "前置:处理器已进入在途等待");

            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult(),
                "处理器不响应令牌时,调用方仍须能取消本次等待");
        }

        [Test]
        public void RequestAsync_DefaultToken_ReachesHandlerAsNotCancelled()
        {
            var observedCancelled = true;
            MessageManager.Register<TestRequest, TestResponse>((req, ct) =>
            {
                observedCancelled = ct.IsCancellationRequested;
                return UniTask.FromResult(new TestResponse());
            });

            MessageManager.RequestAsync<TestRequest, TestResponse>(new TestRequest()).GetAwaiter().GetResult();

            Assert.IsFalse(observedCancelled, "未传令牌时处理器应收到未取消的默认令牌");
        }

        [Test]
        public void HasHandler_TracksRegisterAndUnregister()
        {
            Assert.IsFalse(MessageManager.HasHandler<TestRequest>(), "未注册时应为 false");

            MessageManager.Register<TestRequest, TestResponse>((req, ct) =>
                UniTask.FromResult(new TestResponse()));

            Assert.IsTrue(MessageManager.HasHandler<TestRequest>(), "注册后应为 true");
            Assert.IsFalse(MessageManager.HasHandler<AnotherRequest>(),
                "查询其他请求类型不应受已注册处理器影响");

            MessageManager.Unregister<TestRequest, TestResponse>();

            Assert.IsFalse(MessageManager.HasHandler<TestRequest>(), "注销后应回落为 false");
        }

        [Test]
        public void TryRequestAsync_WithoutHandler_ReturnsFailureWithoutThrowing()
        {
            var result = MessageManager.TryRequestAsync<TestRequest, TestResponse>(new TestRequest())
                .GetAwaiter().GetResult();

            Assert.IsFalse(result.Success, "未注册处理器时应返回失败而非抛异常");
            Assert.IsNull(result.Response, "失败时响应应为 default");
        }

        [Test]
        public void TryRequestAsync_WithHandler_ReturnsResponse()
        {
            MessageManager.Register<TestRequest, TestResponse>((req, ct) =>
                UniTask.FromResult(new TestResponse { Result = req.Input }));

            var result = MessageManager.TryRequestAsync<TestRequest, TestResponse>(
                new TestRequest { Input = 9 }).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success);
            Assert.AreEqual(9, result.Response.Result);
        }

        [Test]
        public void TryRequestAsync_HandlerThrows_PropagatesInsteadOfReportingFailure()
        {
            // 「无处理器」与「处理器内部失败」必须可区分:后者照常上抛,不折算成 Success=false
            MessageManager.Register<TestRequest, TestResponse>((req, ct) =>
                throw new InvalidOperationException("处理器内部失败"));

            Assert.Throws<InvalidOperationException>(() =>
                MessageManager.TryRequestAsync<TestRequest, TestResponse>(new TestRequest())
                    .GetAwaiter().GetResult(),
                "处理器自身的异常不得被折算成失败");
        }

        #endregion

        #region 键值通道的 Key 类型隔离

        [Test]
        public void DifferentKeyTypes_ForSameMessageType_DoNotCollide()
        {
            var byString = new List<int>();
            var byInt = new List<int>();
            MessageManager.Subscribe<string, TestMessage>("Score", msg => byString.Add(msg.Value));
            MessageManager.Subscribe<int, TestMessage>(7, msg => byInt.Add(msg.Value));

            Assert.DoesNotThrow(() =>
            {
                MessageManager.Publish("Score", new TestMessage { Value = 1 });
                MessageManager.Publish(7, new TestMessage { Value = 2 });
                MessageManager.Publish(8, new TestMessage { Value = 3 });
            }, "同一消息类型配不同 Key 类型不得互相干扰");

            CollectionAssert.AreEqual(new[] { 1 }, byString, "string Key 通道只收到自己的消息");
            CollectionAssert.AreEqual(new[] { 2 }, byInt, "int Key 通道只收到匹配 Key 的消息");
        }

        [Test]
        public void DifferentKeyTypes_Buffered_DoNotCollide()
        {
            MessageManager.Publish("Score", new TestMessage { Value = 10 });
            MessageManager.Publish(7, new TestMessage { Value = 20 });

            var fromString = new List<int>();
            var fromInt = new List<int>();
            MessageManager.SubscribeBuffered<string, TestMessage>("Score", msg => fromString.Add(msg.Value));
            MessageManager.SubscribeBuffered<int, TestMessage>(7, msg => fromInt.Add(msg.Value));

            CollectionAssert.AreEqual(new[] { 10 }, fromString, "string Key 重放自己的缓存");
            CollectionAssert.AreEqual(new[] { 20 }, fromInt, "int Key 重放自己的缓存");
        }

        [Test]
        public void EvictBufferedChannels_SpansAllKeyTypes()
        {
            MessageManager.Publish(new TestMessage { Value = 1 });
            MessageManager.Publish("Score", new TestMessage { Value = 2 });
            MessageManager.Publish(7, new TestMessage { Value = 3 });

            Assert.AreEqual(3, MessageManager.EvictBufferedChannels<TestMessage>(),
                "类型级 + 两种 Key 类型的缓冲通道都应被淘汰");
            Assert.AreEqual(0, MessageManager.EvictBufferedChannels<TestMessage>());

            // 两种 Key 类型的存储都应在最后一个键被淘汰后一并摘除(返回值只计淘汰数,不含回收数)
            Assert.AreEqual(0, MessageManager.GetStats().ChannelStoreCount,
                "两种 Key 类型的空存储都应被摘除");
            Assert.AreEqual(0, MessageManager.GetStats().ChannelCount);
        }

        #endregion
    }
}
