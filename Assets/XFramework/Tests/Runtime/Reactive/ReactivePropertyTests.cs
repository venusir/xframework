using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace XFramework.XReactive.Tests
{
    /// <summary>
    /// 响应式属性测试(移除 R3 依赖计划 Phase 3)。
    /// <para>契约来源:R3BehaviorProbeTests 实测的 R3 行为(订阅立即回调、相同值去重、Dispose 后抛 ODE)。</para>
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

        #region ReactiveProperty — Dispose 语义

        [Test]
        public void Dispose_ThenAccessValue_Throws()
        {
            var rp = new ReactiveProperty<int>(1);
            rp.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _ = rp.Value, "Dispose 后访问 Value 抛 ObjectDisposedException");
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

        #endregion
    }
}
