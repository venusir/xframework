using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace XFramework.XReactive.Tests
{
    /// <summary>
    /// 响应式属性测试。
    /// <para>契约:订阅立即回调当前值、相同值去重、Dispose 后写/订阅抛 ObjectDisposedException 而读取宽容、
    /// ReadOnly 映射语义、接口链编程、订阅立即回调抛异常时订阅必须被清理(不泄漏)、
    /// TryWriteValue 的失效判定与 actual 回传。</para>
    /// </summary>
    [TestFixture]
    public class ReactivePropertyTests
    {
        #region ReactiveProperty — 订阅与通知

        [Test]
        public void Subscribe_ImmediatelyCallbacksCurrentValue()
        {
            var rp = new ReactiveProperty<int>(10);
            var calls = new List<int>();

            rp.Subscribe(calls.Add);

            CollectionAssert.AreEqual(new[] { 10 }, calls, "订阅时立即回调当前值");
        }

        [Test]
        public void ValueSet_DifferentValue_NotifiesSubscribers()
        {
            var rp = new ReactiveProperty<int>(1);
            var calls = new List<int>();
            rp.Subscribe(calls.Add);

            rp.Value = 2;

            CollectionAssert.AreEqual(new[] { 1, 2 }, calls, "订阅立即回调 + 设置不同值通知");
        }

        [Test]
        public void ValueSet_SameValue_NotNotified()
        {
            var rp = new ReactiveProperty<int>(1);
            var calls = new List<int>();
            rp.Subscribe(calls.Add);

            rp.Value = 1;

            CollectionAssert.AreEqual(new[] { 1 }, calls, "设置相同值不通知(去重语义)");
        }

        [Test]
        public void Unsubscribe_StopsNotifications()
        {
            var rp = new ReactiveProperty<int>(1);
            var calls = new List<int>();
            var handle = rp.Subscribe(calls.Add);

            handle.Dispose();
            rp.Value = 2;

            CollectionAssert.AreEqual(new[] { 1 }, calls, "退订后不再收到通知");
        }

        #endregion

        #region 订阅立即回调异常 — 订阅必须被清理

        [Test]
        public void Subscribe_ImmediateCallbackThrows_SubscriptionIsReleased()
        {
            var rp = new ReactiveProperty<int>(1);
            int invocations = 0;

            Action<int> handler = _ =>
            {
                invocations++;
                if (invocations == 1)
                    throw new InvalidOperationException("首次回调抛异常");
            };

            Assert.Throws<InvalidOperationException>(() => rp.Subscribe(handler),
                "立即回调抛异常应原样上抛");

            rp.Value = 2;

            Assert.AreEqual(1, invocations,
                "回调抛异常后订阅必须已被清理;若泄漏,写值会再次触发该处理器");
        }

        [Test]
        public void Subscribe_ImmediateCallbackThrows_ThenRetry_Succeeds()
        {
            var rp = new ReactiveProperty<int>(1);

            Assert.Throws<InvalidOperationException>(() =>
                rp.Subscribe(_ => throw new InvalidOperationException("首次回调抛异常")));

            var calls = new List<int>();
            using var handle = rp.Subscribe(calls.Add);

            rp.Value = 2;

            CollectionAssert.AreEqual(new[] { 1, 2 }, calls, "泄漏清理后重新订阅应完全正常");
        }

        [Test]
        public void ReadOnly_Subscribe_ImmediateCallbackThrows_SubscriptionIsReleased()
        {
            var rp = new ReactiveProperty<int>(1);
            var readOnly = rp.Select(x => x * 2);
            int invocations = 0;

            Action<int> handler = _ =>
            {
                invocations++;
                if (invocations == 1)
                    throw new InvalidOperationException("首次回调抛异常");
            };

            Assert.Throws<InvalidOperationException>(() => readOnly.Subscribe(handler),
                "立即回调抛异常应原样上抛");

            rp.Value = 5;

            Assert.AreEqual(1, invocations,
                "回调抛异常后订阅必须已被清理;若泄漏,源变化会再次触发该处理器");
        }

        #endregion

        #region ReactiveProperty — Dispose 语义

        [Test]
        public void Dispose_ThenAccessValue_ReturnsLastValue()
        {
            var rp = new ReactiveProperty<int>(1);
            rp.Value = 7;
            rp.Dispose();

            Assert.AreEqual(7, rp.Value, "Dispose 后读取 Value 仍返回最后持有的值(宽容读取),不抛异常");
        }

        [Test]
        public void Dispose_ThenSetValue_Throws()
        {
            var rp = new ReactiveProperty<int>(1);
            rp.Dispose();

            Assert.Throws<ObjectDisposedException>(() => rp.Value = 2, "Dispose 后设置 Value 抛 ObjectDisposedException");
        }

        [Test]
        public void Dispose_ThenSubscribe_Throws()
        {
            var rp = new ReactiveProperty<int>(1);
            rp.Dispose();

            Assert.Throws<ObjectDisposedException>(() => rp.Subscribe(_ => { }), "Dispose 后 Subscribe 抛 ObjectDisposedException");
        }

        [Test]
        public void Dispose_Twice_DoesNotThrow()
        {
            var rp = new ReactiveProperty<int>(1);
            rp.Dispose();

            Assert.DoesNotThrow(() => rp.Dispose(), "重复 Dispose 幂等");
        }

        #endregion

        #region TryWriteValue — 写入契约

        [Test]
        public void TryWriteValue_LiveTarget_WritesAndReturnsActual()
        {
            var rp = new ReactiveProperty<int>(1);

            Assert.IsTrue(rp.TryWriteValue(2, out var actual), "存活目标写入应成功");
            Assert.AreEqual(2, actual, "回传写入后目标持有的值");
            Assert.AreEqual(2, rp.Value);
        }

        [Test]
        public void TryWriteValue_DisposedTarget_ReturnsFalseWithoutThrowing()
        {
            var rp = new ReactiveProperty<int>(1);
            rp.Dispose();

            bool written = true;
            Assert.DoesNotThrow(() => written = rp.TryWriteValue(2, out _));
            Assert.IsFalse(written, "目标已失效时返回 false 而不是抛异常");
        }

        [Test]
        public void TryWriteValue_TargetDisposedDuringDispatch_ReturnsTrueAndActual()
        {
            var rp = new ReactiveProperty<int>(0);

            // 跳过订阅时的立即回调，只在真正的变化通知里释放
            int calls = 0;
            rp.Subscribe(_ =>
            {
                if (++calls > 1)
                    rp.Dispose();
            });

            bool written = false;
            int actual = -1;

            Assert.DoesNotThrow(() => written = rp.TryWriteValue(5, out actual),
                "写入派发中目标被释放，不该把异常抛进调用方");

            Assert.IsTrue(written, "本次写入确实已经发生，应返回 true");
            Assert.AreEqual(5, actual, "回传的是本次写入的值，调用方无需再读 Value");
        }

        #endregion

        #region IReactiveProperty 接口链 — 面向接口编程

        [Test]
        public void Interface_SubscribeReadOnlyView_Works()
        {
            // 接口 Value 无 setter:作为只读视图暴露(写值仍经具体类型)
            var rp = new ReactiveProperty<int>(5);
            IReactiveProperty<int> view = rp;

            var calls = new List<int>();
            view.Subscribe(calls.Add);

            CollectionAssert.AreEqual(new[] { 5 }, calls, "经接口订阅立即回调当前值");

            rp.Value = 6;
            Assert.AreEqual(6, view.Value, "经接口读值保持最新");
            CollectionAssert.AreEqual(new[] { 5, 6 }, calls, "具体类型写值触发,接口订阅者收到通知");
        }

        [Test]
        public void Interface_SameValue_NotNotified()
        {
            var rp = new ReactiveProperty<int>(1);
            IReactiveProperty<int> view = rp;
            var calls = new List<int>();
            view.Subscribe(calls.Add);

            rp.Value = 1;

            CollectionAssert.AreEqual(new[] { 1 }, calls, "相同值不通知(去重语义经接口一致)");
        }

        #endregion

        #region ReadOnlyReactiveProperty — 派生值语义

        [Test]
        public void ReadOnly_Subscribe_ImmediatelyCallbacksCurrentMappedValue()
        {
            var rp = new ReactiveProperty<int>(10);
            var readOnly = rp.Select(x => x * 2);
            var calls = new List<int>();

            readOnly.Subscribe(calls.Add);

            CollectionAssert.AreEqual(new[] { 20 }, calls, "订阅时立即回调当前映射值(UI 初始绑定依赖)");
        }

        [Test]
        public void ReadOnly_SourceChange_PropagatesAlongMapping()
        {
            var rp = new ReactiveProperty<int>(1);
            var readOnly = rp.Select(x => x * 2);
            var calls = new List<int>();
            readOnly.Subscribe(calls.Add);

            rp.Value = 5;

            CollectionAssert.AreEqual(new[] { 2, 10 }, calls, "源值变化沿映射链传播");
            Assert.AreEqual(10, readOnly.Value, "Value 保持最新映射值");
        }

        [Test]
        public void ReadOnly_MappingResultDedup_NotNotified()
        {
            var rp = new ReactiveProperty<int>(2);
            var readOnly = rp.Select(x => x % 3); // 2 % 3 == 2; 5 % 3 == 2(映射结果相同)
            var calls = new List<int>();
            readOnly.Subscribe(calls.Add);

            rp.Value = 5;

            CollectionAssert.AreEqual(new[] { 2 }, calls, "映射结果与当前值相同不通知");
        }

        [Test]
        public void ReadOnly_Dispose_StopsUpdates()
        {
            var rp = new ReactiveProperty<int>(1);
            var readOnly = rp.Select(x => x * 2);
            var calls = new List<int>();
            readOnly.Subscribe(calls.Add);

            readOnly.Dispose();
            rp.Value = 5;

            CollectionAssert.AreEqual(new[] { 2 }, calls, "Dispose 后源变化不再推送");
            Assert.AreEqual(2, readOnly.Value, "Dispose 后 Value 保持最后映射值");
        }

        [Test]
        public void ReadOnly_Subscribe_NullOnNext_Throws()
        {
            var rp = new ReactiveProperty<int>(1);
            var readOnly = rp.Select(x => x * 2);

            Assert.Throws<ArgumentNullException>(() => readOnly.Subscribe(null));
        }

        [Test]
        public void Select_NullSource_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => ReactivePropertyExtensions.Select<int, int>(null, x => x));
        }

        [Test]
        public void ReadOnly_IsAssignableToIReactiveProperty()
        {
            var rp = new ReactiveProperty<int>(10);
            IReactiveProperty<int> view = rp.Select(x => x * 2);
            var calls = new List<int>();

            view.Subscribe(calls.Add);
            rp.Value = 5;

            CollectionAssert.AreEqual(new[] { 20, 10 }, calls,
                "派生值必须可作为 IReactiveProperty<T> 使用——绑定 API 的接收者正是该接口");
        }

        [Test]
        public void ReadOnly_SelectChaining_Works()
        {
            var rp = new ReactiveProperty<int>(1);
            var chained = rp.Select(x => x * 2).Select(x => $"v{x}");
            var calls = new List<string>();

            chained.Subscribe(calls.Add);
            rp.Value = 3;

            CollectionAssert.AreEqual(new[] { "v2", "v6" }, calls, "派生值可继续 Select 做链式映射");
        }

        #endregion
    }
}
