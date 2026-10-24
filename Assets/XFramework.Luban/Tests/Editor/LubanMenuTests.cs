using NUnit.Framework;
using UnityEngine;

namespace XFramework.XConfig.Luban.Editor.Tests
{
    /// <summary>
    /// <see cref="LubanMenu"/> 的无 UI 入口用例。
    /// </summary>
    [TestFixture]
    public sealed class LubanMenuTests
    {
        private LubanGeneratorSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<LubanGeneratorSettings>();
            _settings.DotnetPath = "dotnet";
            _settings.LubanDllPath = "Tools/Luban/Luban.dll";
            _settings.ConfPath = "Config/Demo/luban.conf";
            _settings.TargetName = "client";
            _settings.CodeOutputDir = "Assets/Generated/LubanCode";
            _settings.DataOutputDir = "Assets/Generated/LubanData";
            _settings.IntegrationOutputDir = "Assets/Generated/LubanIntegration";
            _settings.CodeTarget = LubanCodeTarget.CsBin;
            _settings.DataTarget = LubanDataTarget.Bin;
            _settings.AdapterNamespace = "XFramework.XConfig.Luban.Generated";
            _settings.AdapterClassName = "GeneratedLubanConfigAdapter";
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_settings);
        }

        [Test]
        public void ValidateForRun_InvalidPathFormat_ReturnsErrorsInsteadOfThrowing()
        {
            _settings.LubanDllPath = "Tools/Luban\0Bad/Luban.dll";

            Assert.DoesNotThrow(() => LubanMenu.ValidateForRun(_settings, out _));
            Assert.That(LubanMenu.ValidateForRun(_settings, out _), Is.Not.Empty);
        }
    }
}
