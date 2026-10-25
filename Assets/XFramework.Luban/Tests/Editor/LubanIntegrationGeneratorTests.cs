using System;
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
            UnityEngine.Object.DestroyImmediate(_settings);
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

        [Test]
        public void Generate_BinCodeWithJsonTarget_ReportsLoaderMismatch()
        {
            string root = CreateProject();
            try
            {
                CopyCodeFixture("Bin", root);
                _settings.CodeTarget = LubanCodeTarget.CsSimpleJson;
                _settings.DataTarget = LubanDataTarget.Json;

                var result = LubanIntegrationGenerator.Generate(_settings, root, write: false);

                Assert.That(result.Succeeded, Is.False);
                Assert.That(string.Join("\n", result.Errors), Does.Contain("loader"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void Generate_JsonCodeWithBinTarget_ReportsLoaderMismatch()
        {
            string root = CreateProject();
            try
            {
                CopyCodeFixture("Json", root);
                _settings.CodeTarget = LubanCodeTarget.CsBin;
                _settings.DataTarget = LubanDataTarget.Bin;

                var result = LubanIntegrationGenerator.Generate(_settings, root, write: false);

                Assert.That(result.Succeeded, Is.False);
                Assert.That(string.Join("\n", result.Errors), Does.Contain("loader"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void Generate_MissingDataFile_ReportsError()
        {
            string root = CreateProject();
            try
            {
                CopyCodeFixture("Bin", root);
                string dataDir = Path.Combine(root, "Assets", "Gen", "Data");
                Directory.CreateDirectory(dataDir);
                File.WriteAllBytes(Path.Combine(dataDir, "demo_tbitem.bytes"), new byte[] { 1 });

                var result = LubanIntegrationGenerator.Generate(_settings, root, write: false);

                Assert.That(result.Succeeded, Is.False);
                string errors = string.Join("\n", result.Errors);
                Assert.That(errors, Does.Contain("TbGlobal"));
                Assert.That(errors, Does.Contain("demo_tbglobal.bytes"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void Generate_MissingDataDirectory_AddsWarning()
        {
            string root = CreateProject();
            try
            {
                CopyCodeFixture("Bin", root);

                var result = LubanIntegrationGenerator.Generate(_settings, root, write: false);

                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.Warnings, Has.Some.Contains("数据输出目录不存在"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void Generate_WriteTrue_EmitsAllFilesAndManifest()
        {
            // R4-LBN-004：此前所有用例 write:false，Writer/Emitter 各自独立测，没有「解析 → 校验 →
            // 发射 → 写盘」的端到端覆盖。
            string root = CreateProject();
            try
            {
                CopyCodeFixture("Bin", root);
                string dataDir = Path.Combine(root, "Assets", "Gen", "Data");
                Directory.CreateDirectory(dataDir);
                // 为 fixture 里的每张 Tb* 表补一个数据文件（写盘校验要求全部存在）
                string[] tableFiles = Directory.GetFiles(
                    Path.Combine(root, "Assets", "Gen", "Code"), "Tb*.cs", SearchOption.AllDirectories);
                Assert.That(tableFiles, Is.Not.Empty, "fixture 应包含 Tb* 表");
                for (int i = 0; i < tableFiles.Length; i++)
                {
                    string table = Path.GetFileNameWithoutExtension(tableFiles[i]).ToLowerInvariant();
                    File.WriteAllBytes(Path.Combine(dataDir, "demo_" + table + ".bytes"), new byte[] { 1 });
                }

                var result = LubanIntegrationGenerator.Generate(_settings, root, write: true);

                Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Errors));
                Assert.That(result.Files, Is.Not.Empty);

                string integration = Path.Combine(root, "Assets", "Gen", "Integration");
                Assert.That(Directory.Exists(integration), Is.True, "接入代码目录应被创建");
                Assert.That(File.Exists(Path.Combine(integration, result.AdapterPath)), Is.True,
                    "适配器应写盘");
                Assert.That(File.Exists(Path.Combine(
                        integration, LubanIntegrationWriter.ManifestFileName)), Is.True,
                    "manifest 应写盘");

                for (int i = 0; i < result.Files.Count; i++)
                {
                    Assert.That(File.Exists(Path.Combine(integration, result.Files[i])), Is.True,
                        "声明的生成文件必须实际落盘：" + result.Files[i]);
                }
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static string CreateProject()
        {
            string root = Path.Combine(
                Path.GetTempPath(), "xfw-luban-gen-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private void CopyCodeFixture(string fixture, string projectRoot)
        {
            string source = Path.Combine(LubanTestPaths.FixturesRoot, fixture);
            string destination = Path.Combine(projectRoot, "Assets", "Gen", "Code");
            Directory.CreateDirectory(destination);

            string[] files = Directory.GetFiles(source, "*.cs", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string relative = files[i].Substring(source.Length + 1);
                string target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(files[i], target, true);
            }

            _settings.CodeOutputDir = "Assets/Gen/Code";
            _settings.DataOutputDir = "Assets/Gen/Data";
            _settings.IntegrationOutputDir = "Assets/Gen/Integration";
            _settings.TablesClassName = "Tables";
            _settings.AdapterClassName = "TestLubanConfigAdapter";
            _settings.AdapterNamespace = "Tests.Generated";
        }
    }
}
