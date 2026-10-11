using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using XFramework.XEvent;

namespace XFramework.XReactive.Tests
{
    /// <summary>
    /// 响应式字典测试。
    /// <para>契约:变更逐条通知且载荷按 Kind 取用、索引器 upsert(新键 Add / 旧键 Replace / 等值静默且不替换实例)、
    /// 通知时字典已完成变更、无变化不发通知(等值 upsert / 空表 Clear)、Clear 发一条 Reset 且 Key 是 default、
    /// Add 重复键抛、Remove 未命中静默、键比较器注入与键空间(含 Lock 事故形状)、
    /// 订阅不做立即回调(与 ReactiveProperty 相反)、派发 LIFO、退订同轮生效、
    /// 回调内改动的重入后果(嵌套先跑完 / 负载的键已被移除)、遍历中改动使枚举器失效、
    /// Dispose 后读宽容写抛、构造的静默装载与 null/越界参数、
    /// 经接口使用的不变量、遍历**只锁内容一致性**(顺序不作承诺)、诊断面、
    /// 以及订阅不泄漏(SubscriptionTracker mark-diff,政策 b:本 fixture 结构自清理)。</para>
    /// </summary>
    [TestFixture]
    public class ReactiveDictionaryTests
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
                "本用例泄漏了订阅(ReactiveDictionary 或其订阅句柄未释放)");
            SubscriptionTracker.Reset();
        }

        #region 测试替身

        private static KeyValuePair<TKey, TValue> KV<TKey, TValue>(TKey key, TValue value)
            => new KeyValuePair<TKey, TValue>(key, value);

        /// <summary>按 Id 重写相等性的键——用于复现 Lock 审计里那类「两个不同实例静默合并」的形状。</summary>
        private sealed class IdKey
        {
            public readonly int Id;

            public IdKey(int id) => Id = id;

            public override bool Equals(object obj) => obj is IdKey other && other.Id == Id;

            public override int GetHashCode() => Id;
        }

        /// <summary>引用相等比较器(本 API 级别没有 BCL 的 <c>ReferenceEqualityComparer</c>)。</summary>
        private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
        {
            public static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();

            public bool Equals(T x, T y) => ReferenceEquals(x, y);

            public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
        }

        /// <summary>按值相等但可区分实例的载荷类型——用于锁「等值赋值不替换实例」。</summary>
        private sealed class Box
        {
            public readonly int V;

            public Box(int v) => V = v;

            public override bool Equals(object obj) => obj is Box other && other.V == V;

            public override int GetHashCode() => V;
        }

        #endregion

        #region 变更载荷 — Kind 与字段

        [Test]
        public void Add_NewKey_EmitsAddWithKeyAndValue()
        {
            using var dict = new ReactiveDictionary<string, int>();
            var changes = new List<ReactiveDictionaryChange<string, int>>();
            using var sub = dict.Subscribe(changes.Add);

            dict.Add("hp", 100);

            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(ReactiveCollectionChangeKind.Add, changes[0].Kind);
            Assert.AreEqual("hp", changes[0].Key);
            Assert.AreEqual(100, changes[0].Value);
        }

        [Test]
        public void IndexerSet_MissingKey_EmitsAdd()
        {
            using var dict = new ReactiveDictionary<string, int>();
            var changes = new List<ReactiveDictionaryChange<string, int>>();
            using var sub = dict.Subscribe(changes.Add);

            dict["hp"] = 100;

            Assert.AreEqual(1, changes.Count, "索引器写新键 = upsert 的新增分支,与 Add 同语义");
            Assert.AreEqual(ReactiveCollectionChangeKind.Add, changes[0].Kind);
            Assert.AreEqual("hp", changes[0].Key);
            Assert.AreEqual(100, changes[0].Value);
        }

        [Test]
        public void IndexerSet_ExistingKey_EmitsReplaceWithOldValue()
        {
            using var dict = new ReactiveDictionary<string, int>(new[] { KV("hp", 100) });
            var changes = new List<ReactiveDictionaryChange<string, int>>();
            using var sub = dict.Subscribe(changes.Add);

            dict["hp"] = 80;

            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(ReactiveCollectionChangeKind.Replace, changes[0].Kind);
            Assert.AreEqual("hp", changes[0].Key);
            Assert.AreEqual(80, changes[0].Value, "Item 是新值");
            Assert.AreEqual(100, changes[0].OldValue, "OldValue 是旧值——仅 Replace 有意义");
        }

        [Test]
        public void Remove_ExistingKey_EmitsRemoveWithRemovedValue_ReturnsTrue()
        {
            using var dict = new ReactiveDictionary<string, int>(new[] { KV("hp", 100) });
            var changes = new List<ReactiveDictionaryChange<string, int>>();
            using var sub = dict.Subscribe(changes.Add);

            bool removed = dict.Remove("hp");

            Assert.IsTrue(removed);
            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(ReactiveCollectionChangeKind.Remove, changes[0].Kind);
            Assert.AreEqual("hp", changes[0].Key);
            Assert.AreEqual(100, changes[0].Value, "载荷带被移除的值");
        }

        [Test]
        public void Remove_MissingKey_NotNotified_ReturnsFalse()
        {
            using var dict = new ReactiveDictionary<string, int>(new[] { KV("hp", 100) });
            var changes = new List<ReactiveDictionaryChange<string, int>>();
            using var sub = dict.Subscribe(changes.Add);

            bool removed = dict.Remove("nope");

            Assert.IsFalse(removed, "未命中返回 false");
            Assert.AreEqual(0, changes.Count, "未命中什么都不发生、不发通知");
        }

        [Test]
        public void Clear_EmitsSingleReset_WithDefaultKey()
        {
            using var dict = new ReactiveDictionary<string, int>(new[] { KV("a", 1), KV("b", 2) });
            var changes = new List<ReactiveDictionaryChange<string, int>>();
            using var sub = dict.Subscribe(changes.Add);

            dict.Clear();

            Assert.AreEqual(1, changes.Count, "清空只发一条——不是逐键 Remove");
            Assert.AreEqual(ReactiveCollectionChangeKind.Reset, changes[0].Kind);
            Assert.AreEqual(default(string), changes[0].Key,
                "Reset 的 Key 是 default(TKey)——TKey 没有通用非法值,不给哨兵(与列表 Index=-1 的差别)");
            Assert.AreEqual(0, dict.Count);
        }

        #endregion

        #region 无变化不发通知

        [Test]
        public void Clear_OnEmptyDictionary_NotNotified()
        {
            using var dict = new ReactiveDictionary<string, int>();
            var changes = new List<ReactiveDictionaryChange<string, int>>();
            using var sub = dict.Subscribe(changes.Add);

            dict.Clear();

            Assert.AreEqual(0, changes.Count, "空表清空不发通知(无变化即无通知)");
        }

        [Test]
        public void IndexerSet_EqualValue_NotNotified_AndKeepsOldInstance()
        {
            using var dict = new ReactiveDictionary<string, Box>();
            var oldBox = new Box(1);
            dict["k"] = oldBox;
            var changes = new List<ReactiveDictionaryChange<string, Box>>();
            using var sub = dict.Subscribe(changes.Add);

            dict["k"] = new Box(1);   // 等值(Box 按值重写了 Equals)

            Assert.AreEqual(0, changes.Count,
                "与当前值相等的索引器赋值不发通知(去重语义,比较器为 EqualityComparer<TValue>.Default)");
            Assert.AreSame(oldBox, dict["k"],
                "等值赋值连实例都不替换——新实例被丢弃(与列表索引器同法)");
        }

        #endregion

        #region 通知时机

        [Test]
        public void Notification_DictionaryAlreadyMutated_HandlerSeesNewState()
        {
            using var dict = new ReactiveDictionary<string, int>();
            var observed = new List<string>();
            using var sub = dict.Subscribe(ch => observed.Add($"{ch.Key}|{dict[ch.Key]}|{dict.Count}"));

            dict["x"] = 7;

            CollectionAssert.AreEqual(new[] { "x|7|1" }, observed,
                "通知时字典已完成变更:回调里按键读到的就是新值(重读字典是唯一正确姿势)");
        }

        #endregion

        #region 键比较器与键空间

        [Test]
        public void Constructor_WithComparer_IgnoreCase_KeyEqualsCaseInsensitive()
        {
            using var dict = new ReactiveDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var changes = new List<ReactiveDictionaryChange<string, int>>();
            using var sub = dict.Subscribe(changes.Add);

            dict["Health"] = 1;
            dict["health"] = 2;   // 同一个键 → Replace,不是新增

            Assert.AreEqual(1, dict.Count, "大小写不同视为同一个键(注入的比较器生效)");
            Assert.AreEqual(ReactiveCollectionChangeKind.Replace, changes[1].Kind);
            Assert.IsTrue(dict.ContainsKey("HEALTH"));
        }

        [Test]
        public void DefaultComparer_KeyOverridingEquals_MergesDistinctInstances()
        {
            using var dict = new ReactiveDictionary<IdKey, int>();
            var changes = new List<ReactiveDictionaryChange<IdKey, int>>();
            using var sub = dict.Subscribe(changes.Add);

            dict[new IdKey(1)] = 10;
            dict[new IdKey(1)] = 20;   // 不同实例、按 Equals 相等

            Assert.AreEqual(1, dict.Count,
                "默认比较器走 Equals:两个「不同实例但相等」的键会合并成一个——" +
                "这正是 Lock 审计里那类事故的形状;引用类型键要按实例区分,请注入引用比较器");
            Assert.AreEqual(ReactiveCollectionChangeKind.Replace, changes[1].Kind);
        }

        [Test]
        public void ReferenceComparer_KeepsDistinctInstancesApart()
        {
            using var dict = new ReactiveDictionary<IdKey, int>(ReferenceComparer<IdKey>.Instance);
            var changes = new List<ReactiveDictionaryChange<IdKey, int>>();
            using var sub = dict.Subscribe(changes.Add);

            dict[new IdKey(1)] = 10;
            dict[new IdKey(1)] = 20;

            Assert.AreEqual(2, dict.Count, "注入引用比较器后,两个实例是两个键——事故形状的逃生口");
            Assert.AreEqual(ReactiveCollectionChangeKind.Add, changes[1].Kind);
        }

        [Test]
        public void Comparer_ReturnsInjectedOrDefault()
        {
            using var plain = new ReactiveDictionary<string, int>();
            Assert.AreSame(EqualityComparer<string>.Default, plain.Comparer, "未注入时为默认比较器");

            using var custom = new ReactiveDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Assert.AreSame(StringComparer.OrdinalIgnoreCase, custom.Comparer, "注入后原样返回");
        }

        [Test]
        public void Constructor_WithItemsAndComparer_BothApply()
        {
            using var dict = new ReactiveDictionary<string, int>(
                new[] { KV("Health", 1) }, StringComparer.OrdinalIgnoreCase);
            var changes = new List<ReactiveDictionaryChange<string, int>>();
            using var sub = dict.Subscribe(changes.Add);

            dict["health"] = 2;

            Assert.AreEqual(1, dict.Count, "装载与比较器都要生效");
            Assert.AreEqual(ReactiveCollectionChangeKind.Replace, changes[0].Kind);
        }

        [Test]
        public void NullKey_Throws()
        {
            using var dict = new ReactiveDictionary<string, int>();

            Assert.Throws<ArgumentNullException>(() => dict[null] = 1, "null 键比较器也救不了");
            Assert.Throws<ArgumentNullException>(() => { var _ = dict[null]; });
            Assert.Throws<ArgumentNullException>(() => dict.Add(null, 1));
            Assert.Throws<ArgumentNullException>(() => dict.Remove(null));
        }

        #endregion

        #region 订阅语义

        [Test]
        public void Subscribe_DoesNotCallbackImmediately()
        {
            using var dict = new ReactiveDictionary<string, int>(new[] { KV("a", 1) });
            int calls = 0;
            using var sub = dict.Subscribe(_ => calls++);

            Assert.AreEqual(0, calls,
                "订阅不做立即回调——与 ReactiveProperty<T>.Subscribe 相反;初始化=订阅后立即读一次字典");

            dict["b"] = 2;

            Assert.AreEqual(1, calls, "变更照常投递");
        }

        [Test]
        public void Subscribe_NullHandler_Throws()
        {
            using var dict = new ReactiveDictionary<string, int>();

            Assert.Throws<ArgumentNullException>(() => dict.Subscribe(null), "handler 不可为 null");
        }

        [Test]
        public void Dispatch_OrderIsLifo_LaterSubscriberReceivesFirst()
        {
            using var dict = new ReactiveDictionary<string, int>();
            var order = new List<string>();
            using var a = dict.Subscribe(_ => order.Add("A"));
            using var b = dict.Subscribe(_ => order.Add("B"));

            dict["k"] = 1;

            CollectionAssert.AreEqual(new[] { "B", "A" }, order,
                "派发是 LIFO:后订阅的先收到(引擎语义,别围绕先后次序设计)");
        }

        [Test]
        public void Unsubscribe_ThenMutate_StopsDelivery()
        {
            using var dict = new ReactiveDictionary<string, int>();
            var keys = new List<string>();
            var sub = dict.Subscribe(ch => keys.Add(ch.Key));

            dict["a"] = 1;
            sub.Dispose();
            dict["b"] = 2;

            CollectionAssert.AreEqual(new[] { "a" }, keys, "退订后不再收到投递");
        }

        [Test]
        public void UnsubscribeInsideHandler_TakesEffectSameRound()
        {
            using var dict = new ReactiveDictionary<string, int>();
            var order = new List<string>();
            IDisposable a = null;
            using var subA = a = dict.Subscribe(_ => order.Add("A"));
            using var subB = dict.Subscribe(_ => { order.Add("B"); a.Dispose(); });

            dict["k"] = 1;

            CollectionAssert.AreEqual(new[] { "B" }, order,
                "退订在同一轮派发内立即生效:B 先跑(B 是后订阅者)并退掉 A,A 本轮起就不再被调用");
        }

        [Test]
        public void Subscribe_AfterDispose_Throws()
        {
            var dict = new ReactiveDictionary<string, int>();
            dict.Dispose();

            Assert.Throws<ObjectDisposedException>(() => dict.Subscribe(_ => { }),
                "已释放的字典不可再订阅");
        }

        #endregion

        #region 重入与遍历

        [Test]
        public void ReentrantMutation_NestedRoundCompletesFirst()
        {
            using var dict = new ReactiveDictionary<string, int>(new[] { KV("a", 1) });
            var received = new List<string>();
            // A 先订阅 → 后收到;B 后订阅 → 先收到,并在收到键 "b" 时写入键 "c"
            using var subA = dict.Subscribe(ch => received.Add(ch.Key));
            using var subB = dict.Subscribe(ch =>
            {
                if (ch.Kind == ReactiveCollectionChangeKind.Add && ch.Key == "b")
                    dict["c"] = 3;
            });

            dict["b"] = 2;

            CollectionAssert.AreEqual(new[] { "c", "b" }, received,
                "回调内改动会嵌套派发:嵌套那一轮先跑完(所以 A 先收到 c),外层随后才把 b 投给剩余订阅者");
        }

        [Test]
        public void ReentrantMutation_LaterSubscriberReceivesPayloadForRemovedKey()
        {
            using var dict = new ReactiveDictionary<string, int>(new[] { KV("a", 1) });
            var payloads = new List<ReactiveDictionaryChange<string, int>>();
            using var subA = dict.Subscribe(payloads.Add);
            using var subB = dict.Subscribe(ch =>
            {
                if (ch.Kind == ReactiveCollectionChangeKind.Add && ch.Key == "b")
                    dict.Remove("b");   // 嵌套轮里把刚加的键又删了
            });

            dict["b"] = 2;

            Assert.AreEqual(2, payloads.Count);
            Assert.AreEqual(ReactiveCollectionChangeKind.Remove, payloads[0].Kind);
            Assert.AreEqual("b", payloads[0].Key, "嵌套那一轮先到(移除)");
            Assert.AreEqual(ReactiveCollectionChangeKind.Add, payloads[1].Kind);
            Assert.AreEqual("b", payloads[1].Key, "外层载荷随后到,但那个键已经被嵌套轮移除了");
            Assert.IsFalse(dict.ContainsKey("b"),
                "键是稳定的,条目不是:照载荷的 Key 去查可能已不存在——要状态就重读字典");
        }

        [Test]
        public void EnumeratingWhileMutated_InvalidatesEnumerator()
        {
            using var dict = new ReactiveDictionary<string, int>(new[] { KV("a", 1), KV("b", 2) });
            using var e = dict.GetEnumerator();
            Assert.IsTrue(e.MoveNext());

            dict["c"] = 3;

            Assert.Throws<InvalidOperationException>(() => e.MoveNext(),
                "遍历中改动使枚举器失效——回调里遍历本字典在他人改动时不安全");
        }

        [Test]
        public void EnumeratingKeysWhileMutated_InvalidatesEnumerator()
        {
            using var dict = new ReactiveDictionary<string, int>(new[] { KV("a", 1), KV("b", 2) });
            using var e = dict.Keys.GetEnumerator();
            Assert.IsTrue(e.MoveNext());

            dict.Remove("a");

            Assert.Throws<InvalidOperationException>(() => e.MoveNext(),
                "Keys / Values 是活视图:改动同样使其枚举器失效");
        }

        [Test]
        public void SubscribeInsideHandler_IsSafe()
        {
            using var dict = new ReactiveDictionary<string, int>();
            int lateCalls = 0;
            bool subscribed = false;
            using var sub = dict.Subscribe(_ =>
            {
                if (subscribed)
                    return;
                subscribed = true;
                dict.Subscribe(_ => lateCalls++);   // 回调内订阅:不抛、不破坏本轮
            });

            dict["a"] = 1;
            dict["b"] = 2;

            // 不锁「是否收到当前这一轮」(引擎快照语义,不是本类型的契约),只锁安全性与后续投递
            Assert.GreaterOrEqual(lateCalls, 1, "回调内订阅是安全的:新订阅者此后能收到投递");
        }

        #endregion

        #region Dispose 语义

        [Test]
        public void Dispose_ThenRead_ReturnsLastState()
        {
            var dict = new ReactiveDictionary<string, int>(new[] { KV("a", 1), KV("b", 2) });
            dict.Dispose();

            Assert.AreEqual(2, dict.Count, "Dispose 后读取 Count 仍返回最后状态(宽容读取)");
            Assert.AreEqual(1, dict["a"], "索引器读取不抛");
            Assert.IsTrue(dict.TryGetValue("b", out var v) && v == 2);

            var keys = new List<string>();
            foreach (var k in dict.Keys)
                keys.Add(k);
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, keys, "枚举仍返回最后状态");
        }

        [Test]
        public void Dispose_ThenWrite_Throws()
        {
            var dict = new ReactiveDictionary<string, int>(new[] { KV("a", 1) });
            dict.Dispose();

            Assert.Throws<ObjectDisposedException>(() => dict["b"] = 2, "索引器写入是写路径,照抛");
            Assert.Throws<ObjectDisposedException>(() => dict.Add("b", 2));
            Assert.Throws<ObjectDisposedException>(() => dict.Remove("a"));
            Assert.Throws<ObjectDisposedException>(() => dict.Clear());
        }

        [Test]
        public void Dispose_IsIdempotent_AndCancelsSubscriptions()
        {
            var dict = new ReactiveDictionary<string, int>();
            using var sub = dict.Subscribe(_ => { });
            Assert.AreEqual(1, dict.SubscriptionCount);

            dict.Dispose();
            dict.Dispose();

            Assert.AreEqual(0, dict.SubscriptionCount, "Dispose 终止流并清空订阅(幂等,二次调用无害)");
        }

        #endregion

        #region 构造与参数

        [Test]
        public void Constructor_WithItems_SilentlyLoads()
        {
            var source = new[] { KV("a", 1), KV("b", 2) };
            using var dict = new ReactiveDictionary<string, int>(source);
            var changes = new List<ReactiveDictionaryChange<string, int>>();
            using var sub = dict.Subscribe(changes.Add);

            Assert.AreEqual(2, dict.Count, "元素已装载");
            Assert.AreEqual(0, changes.Count,
                "装载不发通知——批量装载走构造(逐项 Add 则是逐项通知);订阅后也没有立即回调");
        }

        [Test]
        public void Constructor_WithNullItems_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => new ReactiveDictionary<string, int>((IEnumerable<KeyValuePair<string, int>>)null));
        }

        [Test]
        public void Constructor_WithCapacity_IsEmpty()
        {
            using var dict = new ReactiveDictionary<string, int>(16);

            Assert.AreEqual(0, dict.Count, "容量构造只是预分配,字典为空");
        }

        [Test]
        public void Add_DuplicateKey_Throws_WithoutPartialState()
        {
            using var dict = new ReactiveDictionary<string, int>(new[] { KV("hp", 100) });
            var changes = new List<ReactiveDictionaryChange<string, int>>();
            using var sub = dict.Subscribe(changes.Add);

            Assert.Throws<ArgumentException>(() => dict.Add("hp", 999),
                "重复键抛 ArgumentException(BCL 镜像)——「存在就覆盖」是索引器的语义,两者刻意分开");

            Assert.AreEqual(100, dict["hp"], "未覆盖");
            Assert.AreEqual(0, changes.Count, "不留下半完成状态、不发通知");
        }

        [Test]
        public void Indexer_GetMissingKey_ThrowsKeyNotFoundException()
        {
            using var dict = new ReactiveDictionary<string, int>();

            Assert.Throws<KeyNotFoundException>(() => { var _ = dict["nope"]; },
                "索引器 get 缺键抛 KeyNotFoundException(IReadOnlyDictionary 语义);不想抛用 TryGetValue");
            Assert.IsFalse(dict.TryGetValue("nope", out _), "TryGetValue 不抛");
        }

        #endregion

        #region 接口面与遍历内容

        [Test]
        public void Interface_ReadOnlyView_Works()
        {
            using var dict = new ReactiveDictionary<string, int>();
            IReactiveDictionary<string, int> view = dict;

            dict["a"] = 1;

            Assert.AreEqual(1, view.Count, "经接口读 Count");
            Assert.AreEqual(1, view["a"], "经接口读索引器");
            Assert.IsTrue(view.ContainsKey("a"));

            int calls = 0;
            using var sub = view.Subscribe(_ => calls++);
            dict["b"] = 2;
            Assert.AreEqual(1, calls, "经接口订阅同样收到变更");
        }

        [Test]
        public void Interface_ReadOnlyDictionaryUpcast_Works()
        {
            using var dict = new ReactiveDictionary<string, int>(new[] { KV("a", 1) });
            IReadOnlyDictionary<string, int> readOnly = dict;

            Assert.AreEqual(1, readOnly.Count);
            Assert.IsTrue(readOnly.TryGetValue("a", out var v) && v == 1,
                "IReactiveDictionary<TKey,TValue> : IReadOnlyDictionary<TKey,TValue>,可直接交给收它的 API");
        }

        [Test]
        public void KeysValues_ConsistentWithLookup_NoOrderAsserted()
        {
            using var dict = new ReactiveDictionary<string, int>();
            dict["a"] = 1;
            dict["b"] = 2;
            dict["c"] = 3;

            CollectionAssert.AreEquivalent(new[] { "a", "b", "c" }, dict.Keys,
                "只锁内容一致性——遍历顺序不作承诺,不得断言顺序");
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, dict.Values, "同理");
        }

        #endregion

        #region 诊断面

        [Test]
        public void SubscriptionCount_ReflectsLiveSubscriptions()
        {
            using var dict = new ReactiveDictionary<string, int>();
            Assert.AreEqual(0, dict.SubscriptionCount);

            var a = dict.Subscribe(_ => { });
            var b = dict.Subscribe(_ => { });
            Assert.AreEqual(2, dict.SubscriptionCount, "两条订阅");

            a.Dispose();
            Assert.AreEqual(1, dict.SubscriptionCount, "退订递减");

            b.Dispose();
            Assert.AreEqual(0, dict.SubscriptionCount, "全部退订后归零");
        }

        [Test]
        public void ToString_ShowsTypeAndCount()
        {
            var dict = new ReactiveDictionary<string, int>(new[] { KV("a", 1), KV("b", 2), KV("c", 3) });

            Assert.AreEqual("ReactiveDictionary<String, Int32>(Count=3)", dict.ToString(),
                "诊断展示含类型与键值对数量");

            dict.Dispose();
            Assert.AreEqual("ReactiveDictionary<String, Int32>(Count=3)", dict.ToString(),
                "宽容读取:已释放仍展示最后状态");
        }

        #endregion
    }
}
