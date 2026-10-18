using System;
using System.Collections.Generic;
using NUnit.Framework;
using XFramework.XEvent;

namespace XFramework.XReactive.Tests
{
    /// <summary>
    /// 响应式列表测试。
    /// <para>契约:变更逐条通知且载荷按 Kind 取用、通知时列表已完成变更、无变化不发通知
    /// (空表 Clear / 等值替换 / 元素少于 2 的 Sort)、Clear 与 Sort 发一条 Reset、
    /// 订阅不做立即回调(与 ReactiveProperty 相反)、派发 LIFO、退订同轮生效、
    /// 回调内改动的重入后果(嵌套那一轮先跑完 / 索引过期的载荷)、遍历中改动使枚举器失效、
    /// Dispose 后读宽容写抛、构造的静默装载、越界与 null 参数、
    /// 经 IReactiveList&lt;T&gt; 接口使用的不变量、诊断面(SubscriptionCount / ToString)、
    /// 以及订阅不泄漏(SubscriptionTracker mark-diff,政策 b:本 fixture 结构自清理)。</para>
    /// </summary>
    [TestFixture]
    public class ReactiveListTests
    {
        private int _mark;

        [SetUp]
        public void SetUp()
        {
            // 约束 2:跟踪器必须可复位——缺了它,跟踪器自己就会成为泄漏源
            SubscriptionTracker.Reset();
            _mark = SubscriptionTracker.LiveCount;
        }

        [TearDown]
        public void TearDown()
        {
            Assert.AreEqual(_mark, SubscriptionTracker.LiveCount,
                "本用例泄漏了订阅(ReactiveList 或其订阅句柄未释放)");
            SubscriptionTracker.Reset();
        }

        #region 变更载荷 — Kind 与字段

        [Test]
        public void Add_EmitsAddWithIndexAndItem()
        {
            using var list = new ReactiveList<string>();
            var changes = new List<ReactiveListChange<string>>();
            using var sub = list.Subscribe(changes.Add);

            list.Add("a");
            list.Add("b");

            Assert.AreEqual(2, changes.Count, "两次 Add 两条通知");
            Assert.AreEqual(ReactiveCollectionChangeKind.Add, changes[0].Kind);
            Assert.AreEqual(0, changes[0].Index, "Add 的 Index 为追加后的下标");
            Assert.AreEqual("a", changes[0].Item);
            Assert.AreEqual(1, changes[1].Index);
            Assert.AreEqual("b", changes[1].Item, "载荷带被追加的元素");
        }

        [Test]
        public void Insert_EmitsAddAtIndex()
        {
            using var list = new ReactiveList<string>(new[] { "a" });
            var changes = new List<ReactiveListChange<string>>();
            using var sub = list.Subscribe(changes.Add);

            list.Insert(0, "z");

            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(ReactiveCollectionChangeKind.Add, changes[0].Kind, "插入与追加同属 Add");
            Assert.AreEqual(0, changes[0].Index);
            Assert.AreEqual("z", changes[0].Item);
            CollectionAssert.AreEqual(new[] { "z", "a" }, list, "元素已插入");
        }

        [Test]
        public void IndexerSet_EmitsReplaceWithOldItem()
        {
            using var list = new ReactiveList<string>(new[] { "a" });
            var changes = new List<ReactiveListChange<string>>();
            using var sub = list.Subscribe(changes.Add);

            list[0] = "b";

            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(ReactiveCollectionChangeKind.Replace, changes[0].Kind);
            Assert.AreEqual(0, changes[0].Index);
            Assert.AreEqual("b", changes[0].Item, "Item 是新值");
            Assert.AreEqual("a", changes[0].OldItem, "OldItem 是旧值——仅 Replace 有意义");
        }

        [Test]
        public void Remove_ExistingItem_EmitsRemoveWithIndexAndItem()
        {
            using var list = new ReactiveList<string>(new[] { "a", "b" });
            var changes = new List<ReactiveListChange<string>>();
            using var sub = list.Subscribe(changes.Add);

            bool removed = list.Remove("b");

            Assert.IsTrue(removed);
            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(ReactiveCollectionChangeKind.Remove, changes[0].Kind);
            Assert.AreEqual(1, changes[0].Index);
            Assert.AreEqual("b", changes[0].Item, "载荷带被移除的元素");
        }

        [Test]
        public void Remove_MissingItem_NotNotified_ReturnsFalse()
        {
            using var list = new ReactiveList<string>(new[] { "a" });
            var changes = new List<ReactiveListChange<string>>();
            using var sub = list.Subscribe(changes.Add);

            bool removed = list.Remove("nope");

            Assert.IsFalse(removed, "未命中返回 false");
            Assert.AreEqual(0, changes.Count, "未命中什么都不发生、不发通知");
        }

        [Test]
        public void RemoveAt_EmitsRemoveAtIndex()
        {
            using var list = new ReactiveList<string>(new[] { "a", "b" });
            var changes = new List<ReactiveListChange<string>>();
            using var sub = list.Subscribe(changes.Add);

            list.RemoveAt(0);

            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(ReactiveCollectionChangeKind.Remove, changes[0].Kind);
            Assert.AreEqual(0, changes[0].Index);
            Assert.AreEqual("a", changes[0].Item);
        }

        [Test]
        public void Clear_EmitsSingleReset()
        {
            using var list = new ReactiveList<string>(new[] { "a", "b", "c" });
            var changes = new List<ReactiveListChange<string>>();
            using var sub = list.Subscribe(changes.Add);

            list.Clear();

            Assert.AreEqual(1, changes.Count, "清空只发一条——不是逐项 Remove");
            Assert.AreEqual(ReactiveCollectionChangeKind.Reset, changes[0].Kind);
            Assert.AreEqual(-1, changes[0].Index, "Reset 的 Index 为 -1(无意义)");
            Assert.AreEqual(0, list.Count);
        }

        [Test]
        public void Sort_EmitsSingleReset()
        {
            using var list = new ReactiveList<int>(new[] { 3, 1, 2 });
            var changes = new List<ReactiveListChange<int>>();
            using var sub = list.Subscribe(changes.Add);

            list.Sort();

            Assert.AreEqual(1, changes.Count, "整表操作发一条 Reset,不逐项通知");
            Assert.AreEqual(ReactiveCollectionChangeKind.Reset, changes[0].Kind);
            Assert.AreEqual(-1, changes[0].Index);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, list, "已排序");
        }

        #endregion

        #region 无变化不发通知

        [Test]
        public void Clear_OnEmptyList_NotNotified()
        {
            using var list = new ReactiveList<int>();
            var changes = new List<ReactiveListChange<int>>();
            using var sub = list.Subscribe(changes.Add);

            list.Clear();

            Assert.AreEqual(0, changes.Count, "空表 Clear 不发通知(无变化即无通知)");
        }

        [Test]
        public void IndexerSet_SameValue_NotNotified()
        {
            using var list = new ReactiveList<string>(new[] { "a" });
            var changes = new List<ReactiveListChange<string>>();
            using var sub = list.Subscribe(changes.Add);

            list[0] = "a";

            Assert.AreEqual(0, changes.Count,
                "与当前元素相等的索引器赋值不发通知(去重语义,比较器为 EqualityComparer<T>.Default)");
        }

        [Test]
        public void Sort_WithFewerThanTwoItems_NotNotified()
        {
            using var list = new ReactiveList<int>();
            var changes = new List<ReactiveListChange<int>>();
            using var sub = list.Subscribe(changes.Add);

            list.Sort();     // 空表
            list.Add(1);
            list.Sort();     // 单元素

            Assert.AreEqual(1, changes.Count, "两次 Sort 都静默——changes 里只有那条 Add");
            Assert.AreEqual(ReactiveCollectionChangeKind.Add, changes[0].Kind);
        }

        [Test]
        public void Sort_WithComparer_UsesProvidedOrdering()
        {
            // RCT-008：Sort(comparer) 参数此前零覆盖——只测了默认升序，传错比较器不会被发现
            using var list = new ReactiveList<int>(new[] { 3, 1, 2 });
            var changes = new List<ReactiveListChange<int>>();
            using var sub = list.Subscribe(changes.Add);

            list.Sort(Comparer<int>.Create((a, b) => b.CompareTo(a)));

            CollectionAssert.AreEqual(new[] { 3, 2, 1 }, list, "应使用传入的比较器，而不是默认升序");
            Assert.AreEqual(1, changes.Count, "整表操作只发一条 Reset");
            Assert.AreEqual(ReactiveCollectionChangeKind.Reset, changes[0].Kind);
        }

        #endregion

        #region 通知时机

        [Test]
        public void Notification_ListAlreadyMutated_HandlerSeesNewState()
        {
            using var list = new ReactiveList<string>();
            var observed = new List<string>();
            using var sub = list.Subscribe(ch => observed.Add($"{ch.Index}|{list[ch.Index]}|{list.Count}"));

            list.Add("x");

            CollectionAssert.AreEqual(new[] { "0|x|1" }, observed,
                "通知时列表已完成变更:回调里按载荷索引读到的就是新元素(重读列表是唯一正确姿势)");
        }

        #endregion

        #region 订阅语义

        [Test]
        public void Subscribe_DoesNotCallbackImmediately()
        {
            using var list = new ReactiveList<int>(new[] { 1, 2, 3 });
            int calls = 0;
            using var sub = list.Subscribe(_ => calls++);

            Assert.AreEqual(0, calls,
                "订阅不做立即回调——与 ReactiveProperty<T>.Subscribe 相反;初始化=订阅后立即读一次列表");

            list.Add(4);

            Assert.AreEqual(1, calls, "变更照常投递");
        }

        [Test]
        public void Subscribe_NullHandler_Throws()
        {
            using var list = new ReactiveList<int>();

            Assert.Throws<ArgumentNullException>(() => list.Subscribe(null), "handler 不可为 null");
        }

        [Test]
        public void Dispatch_OrderIsLifo_LaterSubscriberReceivesFirst()
        {
            using var list = new ReactiveList<int>();
            var order = new List<string>();
            using var a = list.Subscribe(_ => order.Add("A"));
            using var b = list.Subscribe(_ => order.Add("B"));

            list.Add(1);

            CollectionAssert.AreEqual(new[] { "B", "A" }, order,
                "派发是 LIFO:后订阅的先收到(引擎语义,别围绕先后次序设计)");
        }

        [Test]
        public void Unsubscribe_ThenMutate_StopsDelivery()
        {
            using var list = new ReactiveList<int>();
            var items = new List<int>();
            var sub = list.Subscribe(ch => items.Add(ch.Item));

            list.Add(1);
            sub.Dispose();
            list.Add(2);

            CollectionAssert.AreEqual(new[] { 1 }, items, "退订后不再收到投递");
        }

        [Test]
        public void UnsubscribeInsideHandler_TakesEffectSameRound()
        {
            using var list = new ReactiveList<int>();
            var order = new List<string>();
            IDisposable a = null;
            using var subA = a = list.Subscribe(_ => order.Add("A"));
            using var subB = list.Subscribe(_ => { order.Add("B"); a.Dispose(); });

            list.Add(1);

            CollectionAssert.AreEqual(new[] { "B" }, order,
                "退订在同一轮派发内立即生效:B 先跑(B 是后订阅者)并退掉 A,A 本轮起就不再被调用");
        }

        [Test]
        public void Subscribe_AfterDispose_Throws()
        {
            var list = new ReactiveList<int>();
            list.Dispose();

            Assert.Throws<ObjectDisposedException>(() => list.Subscribe(_ => { }),
                "已释放的列表不可再订阅");
        }

        #endregion

        #region 重入与遍历

        [Test]
        public void ReentrantMutation_NestedRoundCompletesFirst()
        {
            using var list = new ReactiveList<string>(new[] { "a" });
            var received = new List<string>();
            // A 先订阅 → 后收到;B 后订阅 → 先收到,并在收到 "b" 时追加 "c"
            using var subA = list.Subscribe(ch => received.Add(ch.Item));
            using var subB = list.Subscribe(ch =>
            {
                if (ch.Kind == ReactiveCollectionChangeKind.Add && ch.Item == "b")
                    list.Add("c");
            });

            list.Add("b");

            CollectionAssert.AreEqual(new[] { "c", "b" }, received,
                "回调内改动会嵌套派发:嵌套那一轮先跑完(所以 A 先收到 c),外层随后才把 b 投给剩余订阅者");
        }

        [Test]
        public void ReentrantMutation_LaterSubscriberReceivesStaleIndexPayload()
        {
            using var list = new ReactiveList<string>(new[] { "a" });
            var payloads = new List<ReactiveListChange<string>>();
            using var subA = list.Subscribe(payloads.Add);
            using var subB = list.Subscribe(ch =>
            {
                if (ch.Kind == ReactiveCollectionChangeKind.Add && ch.Item == "b")
                    list.Insert(0, "x");     // 位移了后面所有元素
            });

            list.Add("b");

            Assert.AreEqual(2, payloads.Count);
            Assert.AreEqual(0, payloads[0].Index);
            Assert.AreEqual("x", payloads[0].Item, "嵌套那一轮先到");
            Assert.AreEqual(1, payloads[1].Index);
            Assert.AreEqual("b", payloads[1].Item, "外层载荷随后到,但它的索引说的是旧世界");
            Assert.AreNotEqual("b", list[payloads[1].Index],
                "索引只在当次通知内有效:此刻 list[1] 已被 B 的插入位移——照索引取元素会取错");
        }

        [Test]
        public void EnumeratingWhileMutated_InvalidatesEnumerator()
        {
            using var list = new ReactiveList<int>(new[] { 1, 2 });
            using var e = list.GetEnumerator();
            Assert.IsTrue(e.MoveNext());

            list.Add(3);

            Assert.Throws<InvalidOperationException>(() => e.MoveNext(),
                "遍历中改动使枚举器失效——回调里遍历本列表在他人改动时不安全,热路径与回调里请用索引 for");
        }

        [Test]
        public void SubscribeInsideHandler_IsSafe()
        {
            using var list = new ReactiveList<int>();
            int lateCalls = 0;
            bool subscribed = false;
            using var sub = list.Subscribe(_ =>
            {
                if (subscribed)
                    return;
                subscribed = true;
                list.Subscribe(_ => lateCalls++);   // 回调内订阅:不抛、不破坏本轮
            });

            list.Add(1);
            list.Add(2);

            // 不锁「是否收到当前这一轮」(引擎快照语义,不是本类型的契约),只锁安全性与后续投递
            Assert.GreaterOrEqual(lateCalls, 1, "回调内订阅是安全的:新订阅者此后能收到投递");
        }

        #endregion

        #region Dispose 语义

        [Test]
        public void Dispose_ThenRead_ReturnsLastState()
        {
            var list = new ReactiveList<int>(new[] { 1, 2 });
            list.Dispose();

            Assert.AreEqual(2, list.Count, "Dispose 后读取 Count 仍返回最后状态(宽容读取)");
            Assert.AreEqual(1, list[0], "索引器读取不抛");

            var seen = new List<int>();
            foreach (var x in list)
                seen.Add(x);
            CollectionAssert.AreEqual(new[] { 1, 2 }, seen, "枚举仍返回最后状态");
        }

        [Test]
        public void Dispose_ThenWrite_Throws()
        {
            var list = new ReactiveList<int>(new[] { 1 });
            list.Dispose();

            Assert.Throws<ObjectDisposedException>(() => list.Add(2));
            Assert.Throws<ObjectDisposedException>(() => list.Insert(0, 2));
            Assert.Throws<ObjectDisposedException>(() => list.Remove(1));
            Assert.Throws<ObjectDisposedException>(() => list.RemoveAt(0));
            Assert.Throws<ObjectDisposedException>(() => list.Clear());
            Assert.Throws<ObjectDisposedException>(() => list.Sort());
            Assert.Throws<ObjectDisposedException>(() => list[0] = 2, "索引器写入是写路径,照抛");
        }

        [Test]
        public void Dispose_IsIdempotent_AndCancelsSubscriptions()
        {
            var list = new ReactiveList<int>();
            using var sub = list.Subscribe(_ => { });
            Assert.AreEqual(1, list.SubscriptionCount);

            list.Dispose();
            list.Dispose();

            Assert.AreEqual(0, list.SubscriptionCount, "Dispose 终止流并清空订阅(幂等,二次调用无害)");
        }

        #endregion

        #region 构造与参数

        [Test]
        public void Constructor_WithItems_SilentlyLoads()
        {
            var source = new List<int> { 1, 2, 3 };
            using var list = new ReactiveList<int>(source);
            var changes = new List<ReactiveListChange<int>>();
            using var sub = list.Subscribe(changes.Add);

            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, list, "元素已装载");
            Assert.AreEqual(0, changes.Count,
                "装载不发通知——批量装载走构造(逐项 Add 则是逐项通知);订阅后也没有立即回调");
        }

        [Test]
        public void Constructor_WithNullItems_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ReactiveList<int>((IEnumerable<int>)null));
        }

        [Test]
        public void Constructor_WithCapacity_IsEmpty()
        {
            using var list = new ReactiveList<int>(16);

            Assert.AreEqual(0, list.Count, "容量构造只是预分配,列表为空");
        }

        [Test]
        public void Indexer_OutOfRange_Throws()
        {
            using var list = new ReactiveList<int>(new[] { 1 });

            Assert.Throws<ArgumentOutOfRangeException>(() => { var _ = list[5]; });
            Assert.Throws<ArgumentOutOfRangeException>(() => list[5] = 9);
        }

        [Test]
        public void RemoveAt_OutOfRange_Throws_WithoutPartialState()
        {
            using var list = new ReactiveList<int>(new[] { 1, 2 });
            var changes = new List<ReactiveListChange<int>>();
            using var sub = list.Subscribe(changes.Add);

            Assert.Throws<ArgumentOutOfRangeException>(() => list.RemoveAt(5));

            CollectionAssert.AreEqual(new[] { 1, 2 }, list, "越界不留下半完成状态");
            Assert.AreEqual(0, changes.Count, "越界不发通知");
        }

        #endregion

        #region 接口面

        [Test]
        public void Interface_ReadOnlyView_Works()
        {
            using var list = new ReactiveList<int>();
            IReactiveList<int> view = list;

            list.Add(7);

            Assert.AreEqual(1, view.Count, "经接口读 Count");
            Assert.AreEqual(7, view[0], "经接口读索引器");

            int calls = 0;
            using var sub = view.Subscribe(_ => calls++);
            list.Add(8);
            Assert.AreEqual(1, calls, "经接口订阅同样收到变更");

            var seen = new List<int>();
            foreach (var x in view)
                seen.Add(x);
            CollectionAssert.AreEqual(new[] { 7, 8 }, seen, "经接口枚举");
        }

        [Test]
        public void Interface_ReadOnlyListUpcast_Works()
        {
            using var list = new ReactiveList<int>(new[] { 1, 2 });
            IReadOnlyList<int> readOnly = list;

            Assert.AreEqual(2, readOnly.Count);
            Assert.AreEqual(1, readOnly[0], "IReactiveList<T> : IReadOnlyList<T>,可直接交给收只读列表的 API");
        }

        #endregion

        #region 诊断面

        [Test]
        public void SubscriptionCount_ReflectsLiveSubscriptions()
        {
            using var list = new ReactiveList<int>();
            Assert.AreEqual(0, list.SubscriptionCount);

            var a = list.Subscribe(_ => { });
            var b = list.Subscribe(_ => { });
            Assert.AreEqual(2, list.SubscriptionCount, "两条订阅");

            a.Dispose();
            Assert.AreEqual(1, list.SubscriptionCount, "退订递减");

            b.Dispose();
            Assert.AreEqual(0, list.SubscriptionCount, "全部退订后归零");
        }

        [Test]
        public void ToString_ShowsTypeAndCount()
        {
            var list = new ReactiveList<int>(new[] { 1, 2, 3 });

            Assert.AreEqual("ReactiveList<Int32>(Count=3)", list.ToString(), "诊断展示含类型与元素数");

            list.Dispose();
            Assert.AreEqual("ReactiveList<Int32>(Count=3)", list.ToString(), "宽容读取:已释放仍展示最后状态");
        }

        #endregion
    }
}
