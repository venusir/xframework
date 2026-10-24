using System.IO;
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
            _settings.IntegrationOutputDir = "../Integration";

            Assert.That(LubanSettingsValidator.Validate(_settings), Is.Not.Empty);
        }

        [Test]
        public void Validate_OutputOutsideAssets_ReportsError()
        {
            _settings.DataOutputDir = "Config/Data";

            Assert.That(LubanSettingsValidator.Validate(_settings), Is.Not.Empty);
        }

        [Test]
        public void Validate_CodeOutputExactlyAtProtectedRoot_ReportsError()
        {
            _settings.CodeOutputDir = "Assets/XFramework";

            Assert.That(LubanSettingsValidator.Validate(_settings), Is.Not.Empty);
        }

        [Test]
        public void Validate_IntegrationOutputExactlyAtProtectedRoot_ReportsError()
        {
            _settings.IntegrationOutputDir = "Assets/XFramework.Luban";

            Assert.That(LubanSettingsValidator.Validate(_settings), Is.Not.Empty);
        }

        [Test]
        public void Validate_IntegrationInsideCodeOutput_ReportsError()
        {
            _settings.IntegrationOutputDir = "Assets/Generated/LubanCode/Integration";

            Assert.That(LubanSettingsValidator.Validate(_settings), Is.Not.Empty);
        }

        [Test]
        public void Validate_CodeAndDataOutputSame_ReportsError()
        {
            _settings.DataOutputDir = _settings.CodeOutputDir;

            Assert.That(LubanSettingsValidator.Validate(_settings, Path.GetTempPath()), Is.Not.Empty);
        }

        [Test]
        public void Validate_CodeInsideDataOutput_ReportsError()
        {
            _settings.CodeOutputDir = "Assets/Generated/LubanData/Code";

            Assert.That(LubanSettingsValidator.Validate(_settings, Path.GetTempPath()), Is.Not.Empty);
        }

        [Test]
        public void Validate_DataInsideCodeOutput_ReportsError()
        {
            _settings.DataOutputDir = "Assets/Generated/LubanCode/Data";

            Assert.That(LubanSettingsValidator.Validate(_settings, Path.GetTempPath()), Is.Not.Empty);
        }

        [Test]
        public void Validate_LubanDllInsideCodeOutput_ReportsError()
        {
            _settings.LubanDllPath = "Assets/Generated/LubanCode/Luban.dll";

            Assert.That(LubanSettingsValidator.Validate(_settings, Path.GetTempPath()), Is.Not.Empty);
        }

        [Test]
        public void Validate_ConfInsideDataOutput_ReportsError()
        {
            _settings.ConfPath = "Assets/Generated/LubanData/luban.conf";

            Assert.That(LubanSettingsValidator.Validate(_settings, Path.GetTempPath()), Is.Not.Empty);
        }

        [Test]
        public void Validate_DotnetPathInsideDataOutput_ReportsError()
        {
            _settings.DotnetPath = "Assets/Generated/LubanData/dotnet.exe";

            Assert.That(LubanSettingsValidator.Validate(_settings, Path.GetTempPath()), Is.Not.Empty);
        }

        [Test]
        public void Validate_MismatchedTargets_ReportsError()
        {
            _settings.DataTarget = LubanDataTarget.Json;

            Assert.That(LubanSettingsValidator.Validate(_settings), Is.Not.Empty);
        }

        [Test]
        public void Validate_InvalidPathFormat_ReportsErrorInsteadOfThrowing()
        {
            _settings.LubanDllPath = "Tools/Luban\0Bad/Luban.dll";

            Assert.DoesNotThrow(() => LubanSettingsValidator.Validate(_settings, Path.GetTempPath()));
            Assert.That(LubanSettingsValidator.Validate(_settings, Path.GetTempPath()), Is.Not.Empty);
        }

        [Test]
        public void Validate_AbsoluteDllAndConf_NoErrors()
        {
            _settings.LubanDllPath = Path.Combine(Path.GetTempPath(), "Luban", "Luban.dll");
            _settings.ConfPath = Path.Combine(Path.GetTempPath(), "Luban", "luban.conf");

            Assert.That(LubanSettingsValidator.Validate(_settings), Is.Empty);
        }

        [Test]
        public void Validate_AbsoluteOutputDir_StillRejected()
        {
            _settings.CodeOutputDir = Path.Combine(Path.GetTempPath(), "Generated", "LubanCode");

            Assert.That(LubanSettingsValidator.Validate(_settings), Is.Not.Empty);
        }
    }
}
