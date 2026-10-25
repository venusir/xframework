using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XConfig;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// <see cref="ConfigManagerImpl"/> 并发共享与校验顺序测试。
    /// <para>通过 TCS 门控任务挂起加载器，模拟三个并发首次预加载（1 创建者 + 2 加入者），
    /// 断言只触发一次加载、加入者经广播拿到同一结果；失败后允许重试。</para>
    /// <para>直接实例化 internal 实现（InternalsVisibleTo 放行），不经静态门面。</para>
    /// </summary>
    class ConfigManagerImplTests
    {
        [Serializable]
        private struct TestItemRow : IConfigRow<int>
        {
            public int Id { get; set; }

            // 反射填充：赋值发生在 CSV 装载器的列赋值里，编译器看不到 → CS0649 是假阳性
#pragma warning disable CS0649
            public string Name;
#pragma warning restore CS0649
        }

        private class TestGlobalConfig
        {
            public int Value;
        }

        /// <summary>与 <see cref="TestItemRow"/> 主键相同、行类型不同的表类型（CONF-008 错配用）。</summary>
        private class OtherItemRow : IConfigRow<int>
        {
            public int Id { get; set; }
        }

        /// <summary>同时可作为 Table 行类型与 Global 配置类型的类（CONF-009 跨身份在途用）。</summary>
        private class HybridRow : IConfigRow<int>
        {
            public int Id { get; set; }
        }

        /// <summary>Data 为 null 的自定义 IConfigTable（CONF-008 防御用）。</summary>
        private sealed class NullDataTable : IConfigTable
        {
            public IDictionary Data => null;
        }

        private static ConfigTable<TestItemRow> MakeTable()
        {
            return new ConfigTable<TestItemRow>(
                new Dictionary<int, TestItemRow> { [1] = new TestItemRow { Id = 1 } });
        }

        private static ConfigTable<HybridRow> MakeHybridTable()
        {
            return new ConfigTable<HybridRow>(
                new Dictionary<int, HybridRow> { [1] = new HybridRow { Id = 1 } });
        }

        [Test]
        public void PreloadTableAsync_ConcurrentCalls_LoadOnce()
        {
            var impl = new ConfigManagerImpl();
            var fake = new FakeConfigLoader();
            var tcs = new UniTaskCompletionSource<ConfigTable<TestItemRow>>();
            fake.SetTableTask(tcs.Task);

            // 三个并发调用：第一个进入加载并挂起（创建者），后两个注册信号等待广播（加入者）
            var t1 = impl.PreloadTableAsync<TestItemRow>("config/items", fake);
            var t2 = impl.PreloadTableAsync<TestItemRow>("config/items", fake);
            var t3 = impl.PreloadTableAsync<TestItemRow>("config/items", fake);

            Assert.AreEqual(1, fake.LoadTableCallCount, "并发调用应共享同一加载任务，loader 只调用一次");

            tcs.TrySetResult(MakeTable());
            var r1 = t1.GetAwaiter().GetResult();
            var r2 = t2.GetAwaiter().GetResult();
            var r3 = t3.GetAwaiter().GetResult();

            Assert.AreSame(r1, r2, "三个调用方应拿到同一缓存包装器");
            Assert.AreSame(r1, r3, "三个调用方应拿到同一缓存包装器");
            Assert.IsTrue(impl.IsLoaded<TestItemRow>());
        }

        [Test]
        public void PreloadTableAsync_ConcurrentDifferentPaths_WarnsAndSharesFirst()
        {
            var impl = new ConfigManagerImpl();
            var fake = new FakeConfigLoader();
            var tcs = new UniTaskCompletionSource<ConfigTable<TestItemRow>>();
            fake.SetTableTask(tcs.Task);

            var t1 = impl.PreloadTableAsync<TestItemRow>("config/a", fake);

            LogAssert.Expect(LogType.Warning, new Regex(
                "在途加载参数不一致.*config/a.*config/b"));
            var t2 = impl.PreloadTableAsync<TestItemRow>("config/b", fake);

            Assert.AreEqual(1, fake.LoadTableCallCount, "参数不同也仍共享首个在途任务");
            tcs.TrySetResult(MakeTable());

            var r1 = t1.GetAwaiter().GetResult();
            var r2 = t2.GetAwaiter().GetResult();
            Assert.AreSame(r1, r2, "加入者拿到的是首个调用方的结果");
        }

        /// <summary>同时实现两个不同主键的 IConfigRow（R3-CONF-010 多接口告警用）。</summary>
        private sealed class MultiKeyRow : IConfigRow<int>, IConfigRow<string>
        {
            int IConfigRow<int>.Id => 0;
            string IConfigRow<string>.Id => "0";
        }

        [Test]
        public void GetKeyType_MultipleIConfigRow_LogsWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex("实现了多个 IConfigRow<>"));

            var keyType = ConfigTypeHelper.GetKeyType(typeof(MultiKeyRow));

            Assert.IsNotNull(keyType, "多接口实现仍取第一个，但必须告警（R3-CONF-010）");
        }

        [Test]
        public void PreloadTableAsync_LoaderReturnsNull_ThrowsNamedMessage()
        {
            var impl = new ConfigManagerImpl();
            var fake = new FakeConfigLoader(); // 未注入任务 → 返回 null 包装器

            var ex = Assert.Throws<ConfigException>(() =>
                impl.PreloadTableAsync<TestItemRow>("config/x", fake).GetAwaiter().GetResult());

            StringAssert.Contains("Loader returned null Table", ex.Message,
                "自定义 Loader 返回 null 必须给出专名消息，而不是 NRE 文本（R3-CONF-007）");
            Assert.IsFalse(impl.IsLoaded<TestItemRow>());
        }

        [Test]
        public void GetTable_NotLoaded_MessageIncludesRequiredAssetPath()
        {
            var impl = new ConfigManagerImpl();

            var ex = Assert.Throws<ConfigException>(() => impl.GetTable<TestItemRow>());

            StringAssert.Contains("PreloadTableAsync<TestItemRow>(\"path\")", ex.Message,
                "修复提示必须带上必填的 assetPath，否则照抄 CS7036（R3-CONF-006）");
        }

        [Test]
        public void PreloadGlobalAsync_ConcurrentCalls_LoadOnce()
        {
            var impl = new ConfigManagerImpl();
            var fake = new FakeConfigLoader();
            var tcs = new UniTaskCompletionSource<TestGlobalConfig>();
            fake.SetGlobalTask(tcs.Task);

            var t1 = impl.PreloadGlobalAsync<TestGlobalConfig>("config/game", fake);
            var t2 = impl.PreloadGlobalAsync<TestGlobalConfig>("config/game", fake);
            var t3 = impl.PreloadGlobalAsync<TestGlobalConfig>("config/game", fake);

            Assert.AreEqual(1, fake.LoadGlobalCallCount, "并发调用应共享同一加载任务，loader 只调用一次");

            tcs.TrySetResult(new TestGlobalConfig { Value = 42 });
            t1.GetAwaiter().GetResult();
            t2.GetAwaiter().GetResult();
            t3.GetAwaiter().GetResult();

            Assert.AreEqual(42, impl.GetGlobal<TestGlobalConfig>().Value);
        }

        /// <summary>
        /// 在途 Table 加载期间传入 null loader：应抛 ConfigException，而不是在拼接告警时 NRE（R4-CONF-001）。
        /// </summary>
        [Test]
        public void PreloadTableAsync_NullLoaderWhileTableInFlight_ThrowsConfigException()
        {
            var impl = new ConfigManagerImpl();
            var fake = new FakeConfigLoader();
            var tcs = new UniTaskCompletionSource<ConfigTable<TestItemRow>>();
            fake.SetTableTask(tcs.Task);

            var first = impl.PreloadTableAsync<TestItemRow>("config/items", fake);
            var second = impl.PreloadTableAsync<TestItemRow>("config/items", (IConfigLoader)null);

            var ex = Assert.Throws<ConfigException>(() => second.GetAwaiter().GetResult());
            StringAssert.Contains("loader cannot be null", ex.Message);
            Assert.AreEqual(1, fake.LoadTableCallCount, "非法请求不得进入第二次加载");

            tcs.TrySetResult(MakeTable());
            first.GetAwaiter().GetResult();
        }

        /// <summary>
        /// 在途 Global 加载期间传入 null loader：应立即失败，而不是静默加入在途等待（R4-CONF-001）。
        /// </summary>
        [Test]
        public void PreloadGlobalAsync_NullLoaderWhileGlobalInFlight_ThrowsConfigException()
        {
            var impl = new ConfigManagerImpl();
            var fake = new FakeConfigLoader();
            var tcs = new UniTaskCompletionSource<TestGlobalConfig>();
            fake.SetGlobalTask(tcs.Task);

            var first = impl.PreloadGlobalAsync<TestGlobalConfig>("config/game", fake);
            try
            {
                var second = impl.PreloadGlobalAsync<TestGlobalConfig>(
                    "config/game", (IConfigLoader)null);

                Assert.IsTrue(second.Status.IsFaulted(),
                    "null loader 应立即失败，而不是加入在途等待");
                var ex = Assert.Throws<ConfigException>(() => second.GetAwaiter().GetResult());
                StringAssert.Contains("loader cannot be null", ex.Message);
                Assert.AreEqual(1, fake.LoadGlobalCallCount, "非法请求不得进入第二次加载");
            }
            finally
            {
                tcs.TrySetResult(new TestGlobalConfig { Value = 42 });
                first.GetAwaiter().GetResult();
            }
        }

        [Test]
        public void PreloadGlobalAsync_LoaderReturnsNull_ThrowsAndDoesNotMarkLoaded()
        {
            // CONF-006：自定义 Loader 返回 null 时不得记为已加载。
            var impl = new ConfigManagerImpl();
            var fake = new FakeConfigLoader();
            fake.SetGlobalTask(UniTask.FromResult<TestGlobalConfig>(null));

            Assert.Throws<ConfigException>(() =>
                impl.PreloadGlobalAsync<TestGlobalConfig>("config/game", fake).GetAwaiter().GetResult());
            Assert.IsFalse(impl.IsLoaded<TestGlobalConfig>(), "null 配置不得记为已加载");
        }

        [Test]
        public void PreloadFailed_AllowsRetry()
        {
            var impl = new ConfigManagerImpl();
            var fake = new FakeConfigLoader();

            var tcs1 = new UniTaskCompletionSource<ConfigTable<TestItemRow>>();
            fake.SetTableTask(tcs1.Task);
            var t1 = impl.PreloadTableAsync<TestItemRow>("config/items", fake);
            var tJoin = impl.PreloadTableAsync<TestItemRow>("config/items", fake); // join 者，与创建者共享同一失败
            tcs1.TrySetException(new InvalidOperationException("load failed"));

            var e1 = Assert.Throws<ConfigException>(() => t1.GetAwaiter().GetResult());
            var e2 = Assert.Throws<ConfigException>(() => tJoin.GetAwaiter().GetResult());
            Assert.AreSame(e1, e2, "创建者与加入者应收到同一异常实例");
            Assert.IsFalse(impl.IsLoaded<TestItemRow>(), "加载失败不应留下已加载状态");

            // 失败后允许重试（进行中任务已在 finally 中清空）
            var tcs2 = new UniTaskCompletionSource<ConfigTable<TestItemRow>>();
            fake.SetTableTask(tcs2.Task);
            var t2 = impl.PreloadTableAsync<TestItemRow>("config/items", fake);

            Assert.AreEqual(2, fake.LoadTableCallCount, "重试应触发第二次加载");
            tcs2.TrySetResult(MakeTable());
            var table = t2.GetAwaiter().GetResult();

            Assert.IsTrue(impl.IsLoaded<TestItemRow>());
            Assert.IsNotNull(table);
        }

        [Test]
        public void PreloadGlobal_LoadedThenEmptyPath_NoThrow()
        {
            var impl = new ConfigManagerImpl();
            var fake = new FakeConfigLoader();
            fake.SetGlobalTask(UniTask.FromResult(new TestGlobalConfig()));
            impl.PreloadGlobalAsync<TestGlobalConfig>("config/game", fake).GetAwaiter().GetResult();

            // 已加载后省略路径直接返回，不抛异常（与 Table 版行为对齐）
            Assert.DoesNotThrow(() =>
                impl.PreloadGlobalAsync<TestGlobalConfig>("").GetAwaiter().GetResult());
        }

        [Test]
        public void RegisterTable_NonGeneric_RowTypeMismatch_Throws()
        {
            // CONF-008：rowType 与 table.Data 的行类型不匹配必须在注册期失败，
            // 否则 IsLoaded/CopyLoaded 报错类型、查询期才炸。
            var impl = new ConfigManagerImpl();
            var table = MakeTable();

            Assert.Throws<ConfigException>(() => impl.RegisterTable(typeof(OtherItemRow), table));
            Assert.IsFalse(impl.IsLoaded<OtherItemRow>(), "校验失败不得留下已加载状态");

            // 负向控制：行类型正确时注册成功
            impl.RegisterTable(typeof(TestItemRow), table);
            Assert.IsTrue(impl.IsLoaded<TestItemRow>());
        }

        [Test]
        public void RegisterTable_NonGeneric_NullData_Throws()
        {
            // CONF-008：自定义 IConfigTable 的 Data 为 null 时不得写入（否则 CopyLoaded 在 Count 上 NRE）
            var impl = new ConfigManagerImpl();

            Assert.Throws<ConfigException>(() => impl.RegisterTable(typeof(TestItemRow), new NullDataTable()));
            Assert.IsFalse(impl.IsLoaded<TestItemRow>());
        }

        [Test]
        public void PreloadTableAsync_JoiningGlobalInFlight_DoesNotThrow()
        {
            // CONF-009：同一 CLR 类型先起 Global、再起 Table，两身份不得互相加入。
            // 改前 Table 会加入 Global 的在途任务，Global 完成后读 _tables[type] 抛 KeyNotFoundException。
            var impl = new ConfigManagerImpl();
            var fake = new FakeConfigLoader();

            var globalTcs = new UniTaskCompletionSource<HybridRow>();
            fake.SetGlobalTask(globalTcs.Task);
            var globalTask = impl.PreloadGlobalAsync<HybridRow>("config/g", fake);

            var tableTcs = new UniTaskCompletionSource<ConfigTable<HybridRow>>();
            fake.SetTableTask(tableTcs.Task);
            var tableTask = impl.PreloadTableAsync<HybridRow>("config/t", fake);

            Assert.AreEqual(1, fake.LoadTableCallCount, "跨身份不得加入 Global 的在途任务");

            tableTcs.TrySetResult(MakeHybridTable());
            Assert.IsNotNull(tableTask.GetAwaiter().GetResult());

            globalTcs.TrySetResult(new HybridRow { Id = 2 });
            globalTask.GetAwaiter().GetResult();
            Assert.IsTrue(impl.IsLoaded<HybridRow>());
        }

        [Test]
        public void PreloadGlobalAsync_JoiningTableInFlight_RegistersGlobal()
        {
            // CONF-009 反方向：Global 不得加入 Table 的在途任务后直接 return（那会漏掉 Global 注册）
            var impl = new ConfigManagerImpl();
            var fake = new FakeConfigLoader();

            var tableTcs = new UniTaskCompletionSource<ConfigTable<HybridRow>>();
            fake.SetTableTask(tableTcs.Task);
            var tableTask = impl.PreloadTableAsync<HybridRow>("config/t", fake);

            var globalTcs = new UniTaskCompletionSource<HybridRow>();
            fake.SetGlobalTask(globalTcs.Task);
            var globalTask = impl.PreloadGlobalAsync<HybridRow>("config/g", fake);

            Assert.AreEqual(1, fake.LoadGlobalCallCount, "跨身份不得加入 Table 的在途任务");

            globalTcs.TrySetResult(new HybridRow { Id = 2 });
            globalTask.GetAwaiter().GetResult();
            Assert.AreEqual(2, impl.GetGlobal<HybridRow>().Id, "Global 必须完成注册");

            tableTcs.TrySetResult(MakeHybridTable());
            tableTask.GetAwaiter().GetResult();
        }

        [Test]
        public void PreloadTableAsync_ThirdCallerJoinsAfterCrossIdentityInFlight_LoadsOnce()
        {
            // R4-CONF-005：在途登记按 Type 单槽时，同类型 Global 会覆盖 Table 的登记；
            // 第三个 Table 调用看不到先前的 Table 在途项，会重复加载。
            var impl = new ConfigManagerImpl();
            var fake = new FakeConfigLoader();

            var tableTcs = new UniTaskCompletionSource<ConfigTable<HybridRow>>();
            fake.SetTableTask(tableTcs.Task);
            var t1 = impl.PreloadTableAsync<HybridRow>("config/t", fake);

            var globalTcs = new UniTaskCompletionSource<HybridRow>();
            fake.SetGlobalTask(globalTcs.Task);
            var globalTask = impl.PreloadGlobalAsync<HybridRow>("config/g", fake);

            Assert.AreEqual(2, impl.InFlightLoadCount, "Table 与 Global 各自占一条在途");

            var t3 = impl.PreloadTableAsync<HybridRow>("config/t", fake);
            Assert.AreEqual(1, fake.LoadTableCallCount, "第三个同身份调用方应加入首个 Table 在途，而不是重新加载");

            tableTcs.TrySetResult(MakeHybridTable());
            var r1 = t1.GetAwaiter().GetResult();
            var r3 = t3.GetAwaiter().GetResult();
            Assert.AreSame(r1, r3, "第三个调用方拿到首个 Table 的结果");

            globalTcs.TrySetResult(new HybridRow { Id = 9 });
            globalTask.GetAwaiter().GetResult();
            Assert.IsTrue(impl.IsLoaded<HybridRow>());
        }

        [Test]
        public void PreloadTableAsync_WaiterUnloadsDuringBroadcast_OtherWaitersStillGetTable()
        {
            // CONF-010：广播同步执行续体。先醒的等待者重入 Unload<T>() 后，
            // 后续等待者不得再读 _tables[type]（改前抛 KeyNotFoundException）。
            var impl = new ConfigManagerImpl();
            var fake = new FakeConfigLoader();
            var tcs = new UniTaskCompletionSource<ConfigTable<TestItemRow>>();
            fake.SetTableTask(tcs.Task);

            var creator = impl.PreloadTableAsync<TestItemRow>("config/items", fake);
            var waiter1 = impl.PreloadTableAsync<TestItemRow>("config/items", fake);
            var waiter2 = impl.PreloadTableAsync<TestItemRow>("config/items", fake);

            // 第一个等待者的续体在广播中重入 Unload；第二个等待者仍应拿到加载结果
            var unload = waiter1.ContinueWith(_ => impl.Unload<TestItemRow>());

            tcs.TrySetResult(MakeTable());

            Assert.IsNotNull(creator.GetAwaiter().GetResult());
            Assert.IsNotNull(waiter2.GetAwaiter().GetResult(), "先醒的等待者卸载后，后续等待者仍应拿到结果");
            unload.GetAwaiter().GetResult();
            Assert.IsFalse(impl.IsLoaded<TestItemRow>(), "卸载发生在加载完成后，数据应被移除");
        }
    }
}
