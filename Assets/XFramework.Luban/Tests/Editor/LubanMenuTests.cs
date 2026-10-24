using System;
using System.IO;
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
            UnityEngine.Object.DestroyImmediate(_settings);
        }

        [Test]
        public void ValidateForRun_InvalidPathFormat_ReturnsErrorsInsteadOfThrowing()
        {
            _settings.LubanDllPath = "Tools/Luban\0Bad/Luban.dll";

            Assert.DoesNotThrow(() => LubanMenu.ValidateForRun(_settings, out _));
            Assert.That(LubanMenu.ValidateForRun(_settings, out _), Is.Not.Empty);
        }

        [Test]
        public void ValidateForRun_UsesProjectRoot_NotCurrentDirectory()
        {
            string tempRoot = Path.Combine(
                Path.GetTempPath(), "xfw-luban-a5-" + Guid.NewGuid().ToString("N"));
            string oldCurrentDirectory = Directory.GetCurrentDirectory();
            Directory.CreateDirectory(tempRoot);
            try
            {
                string dll = Path.Combine(tempRoot, "Luban.dll");
                string conf = Path.Combine(tempRoot, "luban.conf");
                File.WriteAllText(dll, string.Empty);
                File.WriteAllText(conf, string.Empty);
                _settings.LubanDllPath = dll;
                _settings.ConfPath = conf;

                Directory.SetCurrentDirectory(tempRoot);
                var errors = LubanMenu.ValidateForRun(_settings, out string projectRoot);

                Assert.That(errors, Is.Empty);
                Assert.That(projectRoot, Is.EqualTo(Path.GetDirectoryName(Application.dataPath)));
            }
            finally
            {
                Directory.SetCurrentDirectory(oldCurrentDirectory);
                Directory.Delete(tempRoot, true);
            }
        }

        [Test]
        public void DescribeTimeout_UsesGivenMilliseconds()
        {
            Assert.That(LubanMenu.DescribeTimeout(90000), Does.Contain("90 秒"));
        }

        [Test]
        public void ShowDialog_InBatchMode_DoesNotThrow()
        {
            Assert.That(Application.isBatchMode, Is.True);
            Assert.DoesNotThrow(() => LubanMenu.ShowDialog("标题", "消息"));
        }
    }
}
