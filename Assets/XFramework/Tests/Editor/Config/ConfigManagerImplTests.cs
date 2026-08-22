using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
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
            public string Name;
        }

        private class TestGlobalConfig
        {
            public int Value;
        }

        private static ConfigTable<TestItemRow> MakeTable()
        {
            return new ConfigTable<TestItemRow>(
                new Dictionary<int, TestItemRow> { [1] = new TestItemRow { Id = 1 } });
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
    }
}
