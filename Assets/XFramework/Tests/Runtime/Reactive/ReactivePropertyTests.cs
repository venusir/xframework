using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace XFramework.XReactive.Tests
{
    /// <summary>
    /// 响应式属性测试。
    /// <para>契约:订阅立即回调当前值、相同值去重、Dispose 后写/订阅抛 ObjectDisposedException 而读取宽容、
    /// ReadOnly 映射语义、接口链编程、订阅立即回调抛异常时订阅必须被清理(不泄漏)、
    /// TryWriteValue 的失效判定与 actual 回传、派发顺序 LIFO 与重入写入留下的陈旧值、
    /// 构造/空参/只读类型的 Dispose 矩阵与两条接口不变量、以及诊断面(SubscriptionCount / ToString)。</para>
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

        /// <summary>自定义相等比较器：容差内的浮点变化不再触发通知。</summary>
        [Test]
        public void ValueSet_WithToleranceComparer_DedupsWithinTolerance()
        {
            var rp = new ReactiveProperty<float>(1f, new ToleranceComparer(0.01f));
            var calls = new List<float>();
            rp.Subscribe(calls.Add);

            rp.Value = 1.005f;   // 容差内 → 去重
            rp.Value = 1.5f;     // 超容差 → 通知

            CollectionAssert.AreEqual(new[] { 1f, 1.5f }, calls,
                "比较器决定「相同」——容差内的浮点变化应被去重");
        }

        /// <summary>上一条的对照：默认比较器没有容差语义，同一情形会通知。</summary>
        [Test]
        public void ValueSet_WithoutComparer_NotifiesWithinTolerance()
        {
            var rp = new ReactiveProperty<float>(1f);
            var calls = new List<float>();
            rp.Subscribe(calls.Add);

            rp.Value = 1.005f;

            CollectionAssert.AreEqual(new[] { 1f, 1.005f }, calls, "默认比较器逐位比较，没有容差");
        }

        /// <summary>派生值的去重同样按注入的比较器（<c>Select</c> 的第三个参数）。</summary>
        [Test]
        public void Select_WithComparer_DedupsDerivedValue()
        {
            var source = new ReactiveProperty<float>(1f);
            var derived = source.Select(v => v, new ToleranceComparer(0.01f));
            try
            {
                var calls = new List<float>();
                derived.Subscribe(calls.Add);

                source.Value = 1.004f;   // 源通知；派生值按容差去重
                source.Value = 1.9f;     // 超容差 → 派生值通知

                CollectionAssert.AreEqual(new[] { 1f, 1.9f }, calls,
                    "派生值的去重应按 Select 注入的比较器");
            }
            finally
            {
                derived.Dispose();
            }
        }

        /// <summary>只被 <c>Equals</c> 用到的容差比较器（框架不把它当字典键，散列随意）。</summary>
        private sealed class ToleranceComparer : IEqualityComparer<float>
        {
            private readonly float _tolerance;

            public ToleranceComparer(float tolerance)
            {
                _tolerance = tolerance;
            }

            public bool Equals(float x, float y) => Math.Abs(x - y) <= _tolerance;

            public int GetHashCode(float obj) => 0;
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
            int actual = -1;
            Assert.DoesNotThrow(() => written = rp.TryWriteValue(2, out actual));
            Assert.IsFalse(written, "目标已失效时返回 false 而不是抛异常");
            Assert.AreEqual(0, actual, "失败时 actual 为 default——接口文档明确承诺这一点");
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
        public void ReadOnly_Subscribe_NullHandler_Throws()
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

        #region 派发顺序与重入（README「派发顺序与重入」节记录的就是这里）

        [Test]
        public void Dispatch_OrderIsLifo_LaterSubscriberReceivesFirst()
        {
            var rp = new ReactiveProperty<int>(0);
            var order = new List<string>();

            rp.Subscribe(_ => order.Add("A"));
            rp.Subscribe(_ => order.Add("B"));

            order.Clear();   // 丢掉订阅时的立即回调
            rp.Value = 1;

            CollectionAssert.AreEqual(new[] { "B", "A" }, order,
                "派发顺序是 LIFO:后订阅者先收到。靠订阅次序决定先后是错的");
        }

        [Test]
        public void ReentrantWrite_LaterSubscriberEndsOnStaleValue()
        {
            var rp = new ReactiveProperty<int>(0);
            var aSeen = new List<int>();
            bool written = false;

            // A 先订阅(快照里靠后),B 后订阅(快照里靠前,故先收到)
            rp.Subscribe(v => aSeen.Add(v));
            rp.Subscribe(v =>
            {
                if (v == 1 && !written)
                {
                    written = true;
                    rp.Value = 2;   // 回调内写入:嵌套派发先跑完
                }
            });

            rp.Value = 1;

            CollectionAssert.AreEqual(new[] { 0, 2, 1 }, aSeen,
                "A 先经嵌套派发收到 2,又被外层循环投回 1,最终停在 Value 已不再是的值上,且不会再有通知纠正它。" +
                "这正是 README 劝退「回调内写入」的原因;若哪天改掉了重入语义,请同步改 README");
            Assert.AreEqual(2, rp.Value, "而 Value 实际是 2——A 观察到的是陈旧值");
        }

        #endregion

        #region 契约补口（构造 / 空参 / 只读类型的 Dispose 矩阵 / 接口不变量）

        [Test]
        public void Subscribe_NullHandler_Throws()
        {
            var rp = new ReactiveProperty<int>(1);

            Assert.Throws<ArgumentNullException>(() => rp.Subscribe(null));
        }

        [Test]
        public void ParameterlessConstructor_UsesTypeDefault()
        {
            Assert.AreEqual(0, new ReactiveProperty<int>().Value, "值类型取 default");
            Assert.IsNull(new ReactiveProperty<string>().Value, "引用类型取 null");
        }

        [Test]
        public void Select_NullSelector_Throws()
        {
            var rp = new ReactiveProperty<int>(1);

            Assert.Throws<ArgumentNullException>(() =>
                ReactivePropertyExtensions.Select<int, int>(rp, null));
        }

        [Test]
        public void Select_DisposedSource_Throws()
        {
            var rp = new ReactiveProperty<int>(1);
            rp.Dispose();

            // 取值是宽容的(读得到),但订阅不是——所以派生会抛。这条此前既无测试也无文档
            Assert.Throws<ObjectDisposedException>(() => rp.Select(x => x * 2),
                "源已释放时派生要抛:读得到不代表订得到");
        }

        [Test]
        public void ReadOnly_Dispose_ThenSubscribe_Throws()
        {
            var rp = new ReactiveProperty<int>(1);
            var readOnly = rp.Select(x => x * 2);
            readOnly.Dispose();

            Assert.Throws<ObjectDisposedException>(() => readOnly.Subscribe(_ => { }),
                "派生值已释放后再订阅抛 ObjectDisposedException");
        }

        [Test]
        public void ReadOnly_Dispose_Twice_DoesNotThrow()
        {
            var rp = new ReactiveProperty<int>(1);
            var readOnly = rp.Select(x => x * 2);
            readOnly.Dispose();

            Assert.DoesNotThrow(() => readOnly.Dispose(), "重复 Dispose 幂等（与可写类型对称）");
        }

        [Test]
        public void Interface_ValueHasNoSetter()
        {
            var value = typeof(IReactiveProperty<int>).GetProperty(nameof(IReactiveProperty<int>.Value));

            Assert.IsNotNull(value);
            Assert.IsFalse(value.CanWrite,
                "只读接口不得暴露 setter——写值只能经具体类型或 IReactivePropertyWriter<T>");
        }

        [Test]
        public void ReadOnly_DoesNotImplementWriterCapability()
        {
            Assert.IsFalse(
                typeof(IReactivePropertyWriter<int>).IsAssignableFrom(typeof(ReadOnlyReactiveProperty<int>)),
                "派生值不得获得写入能力——「只读视图」的定位靠这一点保证");
        }

        #endregion

        #region 诊断面

        [Test]
        public void SubscriptionCount_TracksLiveSubscriptions()
        {
            var rp = new ReactiveProperty<int>(1);
            Assert.AreEqual(0, rp.SubscriptionCount, "初始无订阅");

            var a = rp.Subscribe(_ => { });
            rp.Subscribe(_ => { });
            Assert.AreEqual(2, rp.SubscriptionCount, "两次订阅");

            a.Dispose();
            Assert.AreEqual(1, rp.SubscriptionCount, "退订后递减");

            rp.Dispose();
            Assert.AreEqual(0, rp.SubscriptionCount, "Dispose 后清零");
        }

        [Test]
        public void SubscriptionCount_ImmediateCallbackThrows_DoesNotLeak()
        {
            var rp = new ReactiveProperty<int>(1);

            Assert.Throws<InvalidOperationException>(() =>
                rp.Subscribe(_ => throw new InvalidOperationException("首次回调抛异常")));

            Assert.AreEqual(0, rp.SubscriptionCount,
                "立即回调抛异常后订阅必须已清理——订阅数是最直接的观察方式");
        }

        [Test]
        public void SubscriptionCount_ExposesDerivedValueLeak()
        {
            var rp = new ReactiveProperty<int>(1);
            Assert.AreEqual(0, rp.SubscriptionCount);

            // 就地丢弃 Select 的返回值——README 明确劝退的写法
            rp.Select(x => x * 2);

            Assert.AreEqual(1, rp.SubscriptionCount,
                "派生值在构造时就订阅源,丢弃返回值即留下一个不会退订的订阅;这条把文档的告诫变成可观察的事实");
        }

        [Test]
        public void ToString_ShowsTypeAndValue()
        {
            var rp = new ReactiveProperty<int>(50);
            StringAssert.Contains("Int32", rp.ToString(), "展示类型");
            StringAssert.Contains("50", rp.ToString(), "展示当前值");

            var readOnly = rp.Select(x => $"v{x}");
            StringAssert.Contains("String", readOnly.ToString());
            StringAssert.Contains("v50", readOnly.ToString(), "展示当前映射值");
        }

        [Test]
        public void ToString_AfterDispose_ShowsLastValueWithoutThrowing()
        {
            var rp = new ReactiveProperty<int>(7);
            rp.Dispose();

            StringAssert.Contains("7", rp.ToString(), "宽容读取:已释放后仍展示最后持有的值");
        }

        #endregion
    }
}
