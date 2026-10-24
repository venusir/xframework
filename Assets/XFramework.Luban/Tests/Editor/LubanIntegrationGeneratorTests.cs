using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace XFramework.XConfig.Luban.Editor.Tests
{
    /// <summary>
    /// <see cref="LubanIntegrationGenerator"/> 的错误路径用例。
    /// </summary>
    [TestFixture]
    public sealed class LubanIntegrationGeneratorTests
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
        public void Generate_InvalidPathFormat_ReturnsErrorInsteadOfThrowing()
        {
            _settings.CodeOutputDir = "Assets/Gen\0Bad";

            LubanIntegrationResult result = null;
            Assert.DoesNotThrow(() => result = LubanIntegrationGenerator.Generate(
                _settings, Path.GetTempPath(), write: false));

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Errors, Is.Not.Empty);
        }
    }
}
