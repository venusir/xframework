using System;
using NUnit.Framework;
using XFramework.XBootstrap;
using XFramework.XConfig;
using XFramework.XConfig.Luban;

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
    }
}
