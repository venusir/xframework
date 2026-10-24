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
