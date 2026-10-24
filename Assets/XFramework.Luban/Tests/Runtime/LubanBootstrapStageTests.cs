using System;
using System.Threading.Tasks;
using NUnit.Framework;
using XFramework.XBootstrap;
using XFramework.XConfig;
using XFramework.XConfig.Luban;
using XFramework.XPipeline;

namespace XFramework.XConfig.Luban.Tests
{
    /// <summary>
    /// <see cref="LubanBootstrapStage"/> 的相位、名称、权重与空转清理用例。
    /// </summary>
    [TestFixture]
    public sealed class LubanBootstrapStageTests
    {
        [SetUp]
        public void SetUp()
        {
            LubanConfig.DataProviderFactory = null;
            LubanConfig.Destroy();
            ConfigManager.Destroy();
        }

        [TearDown]
        public void TearDown()
        {
            LubanConfig.DataProviderFactory = null;
            LubanConfig.Destroy();
            ConfigManager.Destroy();
        }

        [Test]
        public void Constructor_NullAdapter_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new LubanBootstrapStage(null));
        }

        [Test]
        public void Phase_DefaultsToUserStart()
        {
            var stage = new LubanBootstrapStage(new FakeLubanAdapter());

            Assert.That(stage.Phase, Is.EqualTo(BootstrapPhases.UserStart));
            Assert.That(stage.Name, Is.EqualTo("LubanBootstrapStage"));
            Assert.That(stage.Weight, Is.EqualTo(1f));
        }

        [Test]
        public void Phase_CanBeOverridden()
        {
            var stage = new LubanBootstrapStage(new FakeLubanAdapter(), phase: 91);

            Assert.That(stage.Phase, Is.EqualTo(91));
        }

        [Test]
        public void Shutdown_WhenNeverExecuted_IsSafe()
        {
            var stage = new LubanBootstrapStage(new FakeLubanAdapter());

            Assert.DoesNotThrow(stage.Shutdown);
            Assert.That(LubanConfig.IsInitialized, Is.False);
        }

        [Test]
        public async Task Execute_AfterExternalInitialize_ShutdownKeepsState()
        {
            var provider = new FakeLubanProvider();
            provider.Data["demo_tbitem"] = new byte[] { 1 };
            await LubanConfig.InitializeForTestsAsync(new FakeLubanAdapter(), provider);

            var stage = new LubanBootstrapStage(new FakeLubanAdapter());
            var context = new PipelineStageContext();

            await stage.ExecuteAsync(context, default);

            Assert.That(context.State, Is.EqualTo(PipelineStageState.Completed));
            stage.Shutdown();
            Assert.That(LubanConfig.IsInitialized, Is.True);
        }

        [Test]
        public async Task StageInitialized_ShutdownDestroys()
        {
            var provider = new FakeLubanProvider();
            provider.Data["demo_tbitem"] = new byte[] { 1 };
            LubanConfig.DataProviderFactory = () => provider;
            var adapter = new FakeLubanAdapter();
            var stage = new LubanBootstrapStage(adapter);
            var context = new PipelineStageContext();

            await stage.ExecuteAsync(context, default);

            Assert.That(LubanConfig.IsInitialized, Is.True);
            stage.Shutdown();
            Assert.That(LubanConfig.IsInitialized, Is.False);
            Assert.That(adapter.UnregisterCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task StageInitFails_ShutdownDoesNotDestroyManualInit()
        {
            LubanConfig.DataProviderFactory = () => new FakeLubanProvider
            {
                LoadException = new InvalidOperationException("boom"),
            };
            var stage = new LubanBootstrapStage(new FakeLubanAdapter());
            var context = new PipelineStageContext();

            try
            {
                await stage.ExecuteAsync(context, default);
                Assert.Fail("Expected initialization failure.");
            }
            catch (Exception)
            {
                // expected
            }

            var provider = new FakeLubanProvider();
            provider.Data["demo_tbitem"] = new byte[] { 1 };
            await LubanConfig.InitializeForTestsAsync(new FakeLubanAdapter(), provider);

            stage.Shutdown();

            Assert.That(LubanConfig.IsInitialized, Is.True);
        }
    }
}
