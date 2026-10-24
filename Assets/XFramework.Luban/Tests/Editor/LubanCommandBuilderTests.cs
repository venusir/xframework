using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace XFramework.XConfig.Luban.Editor.Tests
{
    /// <summary>
    /// <see cref="LubanCommandBuilder"/> 的纯函数用例（不执行进程）。
    /// </summary>
    [TestFixture]
    public sealed class LubanCommandBuilderTests
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
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_settings);
        }

        [Test]
        public void TryBuild_PairedTargets_BuildsCommand()
        {
            bool built = LubanCommandBuilder.TryBuild(
                _settings, Path.GetTempPath(), out var command, out string error);

            Assert.That(built, Is.True, error);
            Assert.That(command.FileName, Is.EqualTo("dotnet"));
            Assert.That(command.Arguments, Does.Contain("-t client"));
            Assert.That(command.Arguments, Does.Contain("-c cs-bin"));
            Assert.That(command.Arguments, Does.Contain("-d bin"));
            Assert.That(command.Arguments, Does.Contain("--conf"));
            Assert.That(command.Arguments, Does.Contain("outputCodeDir="));
            Assert.That(command.Arguments, Does.Contain("outputDataDir="));
            Assert.That(command.WorkingDirectory,
                Is.EqualTo(Path.GetDirectoryName(Path.GetFullPath(
                    Path.Combine(Path.GetTempPath(), _settings.ConfPath)))));
        }

        [Test]
        public void TryBuild_JsonTargets_UsesSimpleJson()
        {
            _settings.CodeTarget = LubanCodeTarget.CsSimpleJson;
            _settings.DataTarget = LubanDataTarget.Json;

            bool built = LubanCommandBuilder.TryBuild(
                _settings, Path.GetTempPath(), out var command, out string error);

            Assert.That(built, Is.True, error);
            Assert.That(command.Arguments, Does.Contain("-c cs-simple-json"));
            Assert.That(command.Arguments, Does.Contain("-d json"));
        }

        [Test]
        public void TryBuild_MismatchedTargets_Fails()
        {
            _settings.DataTarget = LubanDataTarget.Json;

            bool built = LubanCommandBuilder.TryBuild(
                _settings, Path.GetTempPath(), out _, out string error);

            Assert.That(built, Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void TryBuild_AppendsExtraArguments()
        {
            _settings.ExtraArguments = "--strict -v";

            LubanCommandBuilder.TryBuild(
                _settings, Path.GetTempPath(), out var command, out _);

            Assert.That(command.Arguments, Does.EndWith("--strict -v"));
        }

        [Test]
        public void Quote_WindowsAndUnix_Rules()
        {
            Assert.That(LubanCommandBuilder.Quote("plain", true), Is.EqualTo("plain"));
            Assert.That(LubanCommandBuilder.Quote("C:\\A B\\file.dll", true),
                Is.EqualTo("\"C:\\A B\\file.dll\""));
            Assert.That(LubanCommandBuilder.Quote("a\"b", true),
                Is.EqualTo("\"a\\\"b\""));
            Assert.That(LubanCommandBuilder.Quote("a'b", false),
                Is.EqualTo("'a'\\''b'"));
        }
    }
}
