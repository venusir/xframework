using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XPool;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// 集合池（<see cref="ListPool{T}"/> / <see cref="HashSetPool{T}"/> /
    /// <see cref="DictionaryPool{TKey, TValue}"/> / <see cref="StringBuilderPool"/>）与
    /// <see cref="CollectionPoolManager"/> 测试。
    /// <para>覆盖：Return 自动 Clear、using 形式、Configure 守卫与重建、重复 Configure 不重复注册
    /// （回归）、ClearAll 清闲置且保留池注册。</para>
    /// <para>隔离：闭合泛型池按类型独立，每个用例使用独占元素类型；StringBuilderPool 为
    /// 非泛型单例，断言只取相对基准；静态构造器每类型只执行一次，故注册数断言一律取差值。</para>
    /// </summary>
    class CollectionPoolTests
    {
        /// <summary>Configure 守卫（ListPool）用例专用元素类型。</summary>
        private sealed class GuardList
        {
        }

        /// <summary>Configure 重建用例专用元素类型。</summary>
        private sealed class RebuildList
        {
        }

        /// <summary>ListPool 往返用例专用元素类型。</summary>
        private sealed class RoundtripList
        {
        }

        /// <summary>HashSetPool 往返用例专用元素类型。</summary>
        private sealed class RoundtripHash
        {
        }

        /// <summary>DictionaryPool 往返用例专用元素类型（键）。</summary>
        private sealed class RoundtripDictKey
        {
        }

        /// <summary>ClearAll 用例专用元素类型。</summary>
        private sealed class ClearAllList
        {
        }

        /// <summary>与四个集合池守卫日志全文一致（全角标点）。</summary>
        private static string ActiveConfigureWarning(string poolName, int activeCount) =>
            $"[{poolName}] 已有 {activeCount} 个活跃实例，Configure 已忽略。仅在无活跃实例时生效（首次 Get 前或全部归还后）。";

        [Test]
        public void ListPool_Roundtrip_AutoClearAndReuse()
        {
            var list = ListPool<RoundtripList>.Get();
            list.Add(new RoundtripList());
            list.Add(new RoundtripList());

            ListPool<RoundtripList>.Return(list);

            Assert.That(list.Count, Is.EqualTo(0), "Return 应自动 Clear");
            var again = ListPool<RoundtripList>.Get();
            Assert.AreSame(list, again, "闲置实例应被复用");
            ListPool<RoundtripList>.Return(again);
        }

        [Test]
        public void ListPool_GetPooled_UsingBlockAutoReturns()
        {
            List<RoundtripList> list;
            using (ListPool<RoundtripList>.GetPooled(out list))
            {
                list.Add(new RoundtripList());
            }

            Assert.That(list.Count, Is.EqualTo(0), "using 结束应自动 Clear + 归还");
        }

        [Test]
        public void ListPool_Configure_WithActiveInstance_WarnsAndKeepsOldPool()
        {
            var active = ListPool<GuardList>.Get(); // 活跃 1 个

            LogAssert.Expect(LogType.Warning, ActiveConfigureWarning("ListPool<GuardList>", 1));
            ListPool<GuardList>.Configure(new PoolConfig { PrewarmSize = 5 });

            ListPool<GuardList>.Return(active);
            Assert.AreSame(active, ListPool<GuardList>.Get(), "守卫拒绝后旧池应原样保留");
            ListPool<GuardList>.Return(active);
        }

        [Test]
        public void ListPool_Configure_AfterAllReturned_RebuildsDiscardingIdle()
        {
            var oldItem = ListPool<RebuildList>.Get();
            ListPool<RebuildList>.Return(oldItem); // 全部归还后允许重建

            ListPool<RebuildList>.Configure(new PoolConfig { PrewarmSize = 2 });

            Assert.That(ListPool<RebuildList>.GetPool().CountInactive, Is.EqualTo(2),
                "重建后新池应按配置预热");
            var fresh = ListPool<RebuildList>.Get();
            Assert.AreNotSame(oldItem, fresh, "重建应丢弃旧池的闲置实例");
            ListPool<RebuildList>.Return(fresh);
        }

        [Test]
        public void HashSetPool_Roundtrip_AutoClearAndReuse()
        {
            var set = HashSetPool<RoundtripHash>.Get();
            set.Add(new RoundtripHash());
            set.Add(new RoundtripHash());

            HashSetPool<RoundtripHash>.Return(set);

            Assert.That(set.Count, Is.EqualTo(0), "Return 应自动 Clear");
            var again = HashSetPool<RoundtripHash>.Get();
            Assert.AreSame(set, again);
            HashSetPool<RoundtripHash>.Return(again);
        }

        [Test]
        public void DictionaryPool_Roundtrip_AutoClearAndReuse()
        {
            var dict = DictionaryPool<RoundtripDictKey, int>.Get();
            dict[new RoundtripDictKey()] = 1;
            dict[new RoundtripDictKey()] = 2;

            DictionaryPool<RoundtripDictKey, int>.Return(dict);

            Assert.That(dict.Count, Is.EqualTo(0), "Return 应自动 Clear");
            var again = DictionaryPool<RoundtripDictKey, int>.Get();
            Assert.AreSame(dict, again);
            DictionaryPool<RoundtripDictKey, int>.Return(again);
        }

        [Test]
        public void StringBuilderPool_Return_ClearsContentAndReusable()
        {
            var sb = StringBuilderPool.Get();
            sb.Append("hello").Append(123);

            StringBuilderPool.Return(sb);

            Assert.That(sb.Length, Is.EqualTo(0), "Return 应自动 Clear 内容");
            Assert.AreSame(sb, StringBuilderPool.Get());
            StringBuilderPool.Return(sb);
        }

        [Test]
        public void StringBuilderPool_Configure_WithActiveInstance_WarnsAndKeepsOldPool()
        {
            var sb = StringBuilderPool.Get(); // 活跃 1 个

            LogAssert.Expect(LogType.Warning, ActiveConfigureWarning("StringBuilderPool", 1));
            StringBuilderPool.Configure(new PoolConfig { PrewarmSize = 5 });

            StringBuilderPool.Return(sb);
            Assert.AreSame(sb, StringBuilderPool.Get(), "守卫拒绝后旧池应原样保留");
            StringBuilderPool.Return(sb);
        }

        [Test]
        public void StringBuilderPool_Configure_DoesNotReRegisterClearAction()
        {
            // 回归：旧实现每次 Configure 都重新 Register，注册表线性膨胀
            var sb = StringBuilderPool.Get();
            StringBuilderPool.Return(sb); // 无活跃实例，Configure 放行

            var before = CollectionPoolManager.RegisteredActionCount;
            StringBuilderPool.Configure(new PoolConfig { PrewarmSize = 1 });
            StringBuilderPool.Configure(new PoolConfig { PrewarmSize = 2 });

            Assert.That(CollectionPoolManager.RegisteredActionCount, Is.EqualTo(before),
                "Configure 不应重复注册 Clear 回调");
        }

        [Test]
        public void ClearAll_ClearsIdle_PoolsStayRegisteredAndUsable()
        {
            var list = ListPool<ClearAllList>.Get();
            list.Add(new ClearAllList());
            ListPool<ClearAllList>.Return(list); // 闲置 1
            Assert.That(ListPool<ClearAllList>.GetPool().CountInactive, Is.EqualTo(1));

            CollectionPoolManager.ClearAll();

            Assert.That(ListPool<ClearAllList>.GetPool().CountInactive, Is.EqualTo(0),
                "ClearAll 应清空所有已注册集合池的闲置实例");
            CollectionPoolManager.ClearAll(); // 幂等

            // ClearAll 后池注册保留，归还与再租仍正常工作
            var again = ListPool<ClearAllList>.Get();
            Assert.NotNull(again);
            ListPool<ClearAllList>.Return(again);
            Assert.That(ListPool<ClearAllList>.GetPool().CountInactive, Is.EqualTo(1));
        }
    }
}
