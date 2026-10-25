using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using XFramework.XConfig;
using XFramework.XConfig.Luban;

namespace XFramework.XConfig.Luban.Tests
{
    /// <summary>
    /// <see cref="LubanConfig"/> 门面与内部管线的生命周期、失败与回滚用例。
    /// </summary>
    [TestFixture]
    public sealed class LubanConfigTests
    {
        [SetUp]
        public void SetUp()
        {
            LubanConfig.Destroy();
            ConfigManager.Destroy();
        }

        [TearDown]
        public void TearDown()
        {
            LubanConfig.Destroy();
            ConfigManager.Destroy();
        }

        [Test]
        public async Task InitializeAsync_LoadsCreatesAndRegisters()
        {
            var provider = new FakeLubanProvider();
            provider.Data["demo_tbitem"] = new byte[] { 1, 2, 3 };
            var adapter = new FakeLubanAdapter();

            await LubanConfig.InitializeForTestsAsync(adapter, provider);

            Assert.That(LubanConfig.IsInitialized, Is.True);
            Assert.That(provider.LoadCalls, Is.EqualTo(1));
            Assert.That(adapter.CreateCalls, Is.EqualTo(1));
            Assert.That(adapter.RegisterCalls, Is.EqualTo(1));
            Assert.That(adapter.LastLoadedBytes, Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(provider.LastFiles[0].Location,
                Is.EqualTo("configs/demo_tbitem.bytes"));
            Assert.That(LubanConfig.Tables, Is.SameAs(adapter.LastTables));
            Assert.That(LubanConfig.GetTables<FakeTables>(), Is.SameAs(adapter.LastTables));
            Assert.That(LubanConfig.TryGetTables<FakeTables>(out var tables), Is.True);
            Assert.That(tables, Is.SameAs(adapter.LastTables));
        }

        [Test]
        public async Task InitializeAsync_UsesCustomLocationResolver()
        {
            var provider = new FakeLubanProvider();
            provider.Data["demo_tbitem"] = new byte[] { 1 };
            var adapter = new FakeLubanAdapter();
            var options = new LubanOptions
            {
                LocationResolver = name => "custom/" + name + ".bytes",
            };

            await LubanConfig.InitializeForTestsAsync(adapter, provider, options);

            Assert.That(provider.LastFiles[0].Location, Is.EqualTo("custom/demo_tbitem.bytes"));
        }

        [Test]
        public async Task InitializeAsync_AutoInitializesConfigManager()
        {
            Assert.That(ConfigManager.IsInitialized, Is.False);

            var provider = new FakeLubanProvider();
            provider.Data["demo_tbitem"] = new byte[] { 1 };

            await LubanConfig.InitializeForTestsAsync(
                new FakeLubanAdapter(), provider);

            Assert.That(ConfigManager.IsInitialized, Is.True);
        }

        [Test]
        public async Task InitializeAsync_RepeatedCall_WarnsAndIgnores()
        {
            var provider = new FakeLubanProvider();
            provider.Data["demo_tbitem"] = new byte[] { 1 };
            var adapter = new FakeLubanAdapter();

            await LubanConfig.InitializeForTestsAsync(adapter, provider);
            await LubanConfig.InitializeForTestsAsync(adapter, provider);

            Assert.That(adapter.CreateCalls, Is.EqualTo(1));
            Assert.That(adapter.RegisterCalls, Is.EqualTo(1));
        }

        [Test]
        public void InitializeAsync_NullAdapter_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => LubanConfig.InitializeForTestsAsync(null, new FakeLubanProvider()));
        }

        [Test]
        public async Task InitializeAsync_InvalidExtension_Throws()
        {
            var adapter = new FakeLubanAdapter { DataFileExtension = "bytes" };

            await AssertLubanExceptionAsync(() =>
                LubanConfig.InitializeForTestsAsync(adapter, new FakeLubanProvider()));
        }

        [Test]
        public async Task InitializeAsync_DuplicateTableNames_Throws()
        {
            var adapter = new FakeLubanAdapter
            {
                TableNames = new[] { "demo_tbitem", "demo_tbitem" },
            };

            await AssertLubanExceptionAsync(() =>
                LubanConfig.InitializeForTestsAsync(adapter, new FakeLubanProvider()));
        }

        [Test]
        public async Task InitializeAsync_MissingData_Throws()
        {
            var adapter = new FakeLubanAdapter();

            await AssertLubanExceptionAsync(() =>
                LubanConfig.InitializeForTestsAsync(adapter, new FakeLubanProvider()));
        }

        [Test]
        public async Task InitializeAsync_ProviderFailure_Propagates()
        {
            var provider = new FakeLubanProvider
            {
                LoadException = new LubanConfigException("boom"),
            };

            try
            {
                await LubanConfig.InitializeForTestsAsync(new FakeLubanAdapter(), provider);
                Assert.Fail("Expected LubanConfigException.");
            }
            catch (LubanConfigException ex)
            {
                Assert.That(ex.Message, Is.EqualTo("boom"));
            }
        }

        [Test]
        public async Task InitializeAsync_ProviderLevelFailure_MessageIncludesFirstTableAndLocation()
        {
            // R4-LBN-006：provider 级失败（整批加载抛异常）此前只带内层消息，没有表名/location。
            var inner = new InvalidOperationException("AssetManager 尚未初始化");
            var provider = new FakeLubanProvider { LoadException = inner };
            provider.Data["demo_tbitem"] = new byte[] { 1 };
            var adapter = new FakeLubanAdapter();

            try
            {
                await LubanConfig.InitializeForTestsAsync(adapter, provider);
                Assert.Fail("Expected LubanConfigException.");
            }
            catch (LubanConfigException ex)
            {
                Assert.That(provider.LastFiles, Is.Not.Empty);
                StringAssert.Contains(provider.LastFiles[0].TableName, ex.Message);
                StringAssert.Contains(provider.LastFiles[0].Location, ex.Message);
                Assert.That(ex.InnerException, Is.SameAs(inner));
            }
        }

        [Test]
        public async Task InitializeAsync_ProviderFailure_MessagePreservesInner()
        {
            var inner = new InvalidOperationException("AssetManager 尚未初始化，请先调用 InitializeAsync");
            var provider = new FakeLubanProvider { LoadException = inner };

            try
            {
                await LubanConfig.InitializeForTestsAsync(new FakeLubanAdapter(), provider);
                Assert.Fail("Expected LubanConfigException.");
            }
            catch (LubanConfigException ex)
            {
                Assert.That(ex.Message, Does.Contain(inner.Message));
                Assert.That(ex.InnerException, Is.SameAs(inner));
            }
        }

        [Test]
        public async Task InitializeAsync_Cancellation_Propagates()
        {
            var provider = new FakeLubanProvider { ThrowCancellation = true };

            try
            {
                await LubanConfig.InitializeForTestsAsync(new FakeLubanAdapter(), provider);
                Assert.Fail("Expected OperationCanceledException.");
            }
            catch (OperationCanceledException)
            {
                // expected
            }
        }

        [Test]
        public async Task InitializeAsync_RegistrationFailure_RollsBack()
        {
            var provider = new FakeLubanProvider();
            provider.Data["demo_tbitem"] = new byte[] { 1 };
            var adapter = new FakeLubanAdapter
            {
                RegisterHandler = _ => throw new InvalidOperationException("register failed"),
            };

            await AssertLubanExceptionAsync(() =>
                LubanConfig.InitializeForTestsAsync(adapter, provider));

            Assert.That(adapter.UnregisterCalls, Is.EqualTo(1));
            Assert.That(LubanConfig.IsInitialized, Is.False);
        }

        [Test]
        public async Task Destroy_UnregistersAndClears()
        {
            var provider = new FakeLubanProvider();
            provider.Data["demo_tbitem"] = new byte[] { 1 };
            var adapter = new FakeLubanAdapter();

            await LubanConfig.InitializeForTestsAsync(adapter, provider);
            LubanConfig.Destroy();

            Assert.That(adapter.UnregisterCalls, Is.EqualTo(1));
            Assert.That(LubanConfig.IsInitialized, Is.False);
            Assert.Throws<InvalidOperationException>(() => _ = LubanConfig.Tables);
        }

        [Test]
        public async Task Destroy_ConfigManagerDestroyed_SkipsUnregister()
        {
            var provider = new FakeLubanProvider();
            provider.Data["demo_tbitem"] = new byte[] { 1 };
            var adapter = new FakeLubanAdapter();

            await LubanConfig.InitializeForTestsAsync(adapter, provider);
            ConfigManager.Destroy();
            LubanConfig.Destroy();

            Assert.That(adapter.UnregisterCalls, Is.EqualTo(0));
            Assert.That(LubanConfig.IsInitialized, Is.False);
        }

        [Test]
        public async Task GetTables_WrongType_Throws()
        {
            var provider = new FakeLubanProvider();
            provider.Data["demo_tbitem"] = new byte[] { 1 };
            await LubanConfig.InitializeForTestsAsync(new FakeLubanAdapter(), provider);

            Assert.Throws<InvalidOperationException>(() => LubanConfig.GetTables<FakeGlobal>());
        }

        [Test]
        public void TryGetTables_NotInitialized_ReturnsFalse()
        {
            Assert.That(LubanConfig.TryGetTables<FakeTables>(out var tables), Is.False);
            Assert.That(tables, Is.Null);
        }

        [Test]
        public async Task SessionReset_ClearsAndAllowsReinitialize()
        {
            var provider = new FakeLubanProvider();
            provider.Data["demo_tbitem"] = new byte[] { 1 };
            var adapter = new FakeLubanAdapter();

            await LubanConfig.InitializeForTestsAsync(adapter, provider);
            LubanConfig.ResetForNewSession();

            Assert.That(LubanConfig.IsInitialized, Is.False);

            await LubanConfig.InitializeForTestsAsync(adapter, provider);
            Assert.That(LubanConfig.IsInitialized, Is.True);
            Assert.That(adapter.CreateCalls, Is.EqualTo(2));
        }

        [Test]
        public async Task InitializeAsync_ReportsProgress()
        {
            var provider = new FakeLubanProvider();
            provider.Data["demo_tbitem"] = new byte[] { 1 };
            var progress = new ListProgress();

            await LubanConfig.InitializeForTestsAsync(
                new FakeLubanAdapter(), provider, progress: progress);

            Assert.That(progress.Reports.Count, Is.GreaterThan(0));
            Assert.That(progress.Reports[progress.Reports.Count - 1].Progress, Is.EqualTo(1f));
        }

        [Test]
        public async Task InitializeAsync_ConcurrentCalls_LoadAndRegisterOnce()
        {
            var provider = new FakeLubanProvider { GateLoads = true };
            provider.Data["demo_tbitem"] = new byte[] { 1 };
            var adapter = new FakeLubanAdapter();

            var first = LubanConfig.InitializeForTestsAsync(adapter, provider);
            var second = LubanConfig.InitializeForTestsAsync(adapter, provider);
            int pending = provider.PendingLoadCount;

            provider.ReleaseLoads();
            await first;
            await second;

            Assert.That(pending, Is.EqualTo(1));
            Assert.That(provider.LoadCalls, Is.EqualTo(1));
            Assert.That(adapter.CreateCalls, Is.EqualTo(1));
            Assert.That(adapter.RegisterCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task Destroy_DuringLoad_DoesNotRegister()
        {
            var provider = new FakeLubanProvider { GateLoads = true };
            provider.Data["demo_tbitem"] = new byte[] { 1 };
            var adapter = new FakeLubanAdapter();

            var task = LubanConfig.InitializeForTestsAsync(adapter, provider);
            Assert.That(provider.PendingLoadCount, Is.EqualTo(1));

            LubanConfig.Destroy();
            provider.ReleaseLoads();

            bool canceled = false;
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }

            Assert.That(canceled, Is.True);
            Assert.That(adapter.RegisterCalls, Is.EqualTo(0));
            Assert.That(LubanConfig.IsInitialized, Is.False);
        }

        [Test]
        public async Task DestroyDuringLoad_Reinitialize_OldGenerationDoesNotPollute()
        {
            var oldProvider = new FakeLubanProvider { GateLoads = true };
            oldProvider.Data["demo_tbitem"] = new byte[] { 1 };
            var oldAdapter = new FakeLubanAdapter();

            var oldTask = LubanConfig.InitializeForTestsAsync(oldAdapter, oldProvider);
            Assert.That(oldProvider.PendingLoadCount, Is.EqualTo(1));

            LubanConfig.Destroy();

            var newProvider = new FakeLubanProvider();
            newProvider.Data["demo_tbitem"] = new byte[] { 2 };
            var newAdapter = new FakeLubanAdapter();
            await LubanConfig.InitializeForTestsAsync(newAdapter, newProvider);

            oldProvider.ReleaseLoads();

            bool canceled = false;
            try
            {
                await oldTask;
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }

            Assert.That(canceled, Is.True);
            Assert.That(oldAdapter.RegisterCalls, Is.EqualTo(0));
            Assert.That(newAdapter.RegisterCalls, Is.EqualTo(1));
            Assert.That(LubanConfig.GetTables<FakeTables>(), Is.SameAs(newAdapter.LastTables));
        }

        [Test]
        public async Task InitializeAsync_RegistrationCancellation_RollsBack()
        {
            var provider = new FakeLubanProvider();
            provider.Data["demo_tbitem"] = new byte[] { 1 };
            var adapter = new FakeLubanAdapter
            {
                RegisterHandler = _ => throw new OperationCanceledException(),
            };

            try
            {
                await LubanConfig.InitializeForTestsAsync(adapter, provider);
                Assert.Fail("Expected OperationCanceledException.");
            }
            catch (OperationCanceledException)
            {
                // expected
            }

            Assert.That(adapter.UnregisterCalls, Is.EqualTo(1));
            Assert.That(LubanConfig.IsInitialized, Is.False);
        }

        [Test]
        public async Task ConfigChanged_FiresPerTable_BeforeIsInitialized()
        {
            ConfigManager.Destroy();
            ConfigManager.Initialize();

            var adapter = new FakeLubanAdapter
            {
                RegisterHandler = _ =>
                {
                    LubanConfigBridge.RegisterTable(new Dictionary<int, FakeRow>
                    {
                        { 1, new FakeRow(1) },
                    });
                    LubanConfigBridge.RegisterGlobal(new FakeGlobal { Name = "g" });
                },
            };
            var provider = new FakeLubanProvider();
            provider.Data["demo_tbitem"] = new byte[] { 1 };

            var events = new List<Type>();
            var initializedDuringEvents = new List<bool>();
            Action<Type> handler = type =>
            {
                events.Add(type);
                initializedDuringEvents.Add(LubanConfig.IsInitialized);
            };
            ConfigManager.ConfigChanged += handler;
            try
            {
                await LubanConfig.InitializeForTestsAsync(adapter, provider);
            }
            finally
            {
                ConfigManager.ConfigChanged -= handler;
            }

            Assert.That(events, Is.EquivalentTo(new[] { typeof(FakeRow), typeof(FakeGlobal) }));
            Assert.That(initializedDuringEvents, Is.All.False);
            Assert.That(LubanConfig.IsInitialized, Is.True);
        }

        private static async Task AssertLubanExceptionAsync(Func<Cysharp.Threading.Tasks.UniTask> action)
        {
            try
            {
                await action();
                Assert.Fail("Expected LubanConfigException.");
            }
            catch (LubanConfigException)
            {
                // expected
            }
        }
    }
}
