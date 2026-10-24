using NUnit.Framework;
using UnityEngine;

namespace XFramework.XConfig.Luban.Editor.Tests
{
    /// <summary>
    /// <see cref="LubanSettingsValidator"/> 的结构校验用例。
    /// </summary>
    [TestFixture]
    public sealed class LubanSettingsValidatorTests
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
            _settings.GlueOutputDir = "Assets/Generated/LubanGlue";
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
        public void Validate_ValidSettings_NoErrors()
        {
            Assert.That(LubanSettingsValidator.Validate(_settings), Is.Empty);
        }

        [Test]
        public void Validate_MissingRequiredFields_ReportsErrors()
        {
            _settings.LubanDllPath = "";
            _settings.CodeOutputDir = "";
            _settings.AdapterClassName = "";

            Assert.That(LubanSettingsValidator.Validate(_settings), Is.Not.Empty);
        }

        [Test]
        public void Validate_ForbiddenPaths_ReportErrors()
        {
            _settings.CodeOutputDir = "Packages/Generated";
            _settings.DataOutputDir = "Assets/XFramework/Data";
            _settings.GlueOutputDir = "../Glue";

            Assert.That(LubanSettingsValidator.Validate(_settings), Is.Not.Empty);
        }

        [Test]
        public void Validate_OutputOutsideAssets_ReportsError()
        {
            _settings.DataOutputDir = "Config/Data";

            Assert.That(LubanSettingsValidator.Validate(_settings), Is.Not.Empty);
        }

        [Test]
        public void Validate_GlueInsideCodeOutput_ReportsError()
        {
            _settings.GlueOutputDir = "Assets/Generated/LubanCode/Glue";

            Assert.That(LubanSettingsValidator.Validate(_settings), Is.Not.Empty);
        }

        [Test]
        public void Validate_MismatchedTargets_ReportsError()
        {
            _settings.DataTarget = LubanDataTarget.Json;

            Assert.That(LubanSettingsValidator.Validate(_settings), Is.Not.Empty);
        }
    }
}
