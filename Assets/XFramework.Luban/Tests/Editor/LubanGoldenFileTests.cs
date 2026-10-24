using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace XFramework.XConfig.Luban.Editor.Tests
{
    /// <summary>
    /// golden-file 比对：用当前 emitter 重新生成，与签入的接入代码逐字比较。
    /// <para>设置环境变量 <c>XFW_UPDATE_LUBAN_GOLDEN=1</c>（通常经
    /// <c>run-tests.ps1 -UpdateLubanGolden</c>）时写盘并有意失败。</para>
    /// <para>比对前只归一化 CRLF→LF 与剥离 UTF-8 BOM，不 trim 空白。</para>
    /// </summary>
    [TestFixture]
    public sealed class LubanGoldenFileTests
    {
        private const string UpdateEnvVar = "XFW_UPDATE_LUBAN_GOLDEN";

        [Test]
        public void BinFixtures_MatchCommittedIntegration()
        {
            VerifyGolden(
                Path.Combine(LubanTestPaths.FixturesRoot, "Bin"),
                Path.Combine(
                    LubanTestPaths.PackageRoot,
                    "Tests", "Runtime", "Fixtures", "Generated", "Integration"),
                "GeneratedLubanConfigAdapter",
                "XFramework.XConfig.Luban.Tests.Generated",
                LubanCodeTarget.CsBin,
                LubanDataTarget.Bin);
        }

        [Test]
        public void SampleJson_MatchesCommittedIntegration()
        {
            string packageRoot = LubanTestPaths.PackageRoot;
            VerifyGolden(
                Path.Combine(packageRoot, "Samples~", "Luban", "Generated", "Code"),
                Path.Combine(packageRoot, "Samples~", "Luban", "Generated", "Integration"),
                "LubanSampleConfigAdapter",
                "XFramework.Luban.Sample.Generated",
                LubanCodeTarget.CsSimpleJson,
                LubanDataTarget.Json);
        }

        private static void VerifyGolden(
            string codeDirectory,
            string integrationDirectory,
            string adapterClassName,
            string adapterNamespace,
            LubanCodeTarget codeTarget,
            LubanDataTarget dataTarget)
        {
            var model = LubanCodeParser.Parse(codeDirectory, "Tables");
            var settings = ScriptableObject.CreateInstance<LubanGeneratorSettings>();
            try
            {
                settings.AdapterClassName = adapterClassName;
                settings.AdapterNamespace = adapterNamespace;
                settings.CodeTarget = codeTarget;
                settings.DataTarget = dataTarget;
                settings.GenerateRowInterfaces = true;

                var files = LubanIntegrationEmitter.Emit(model, settings);

                if (Environment.GetEnvironmentVariable(UpdateEnvVar) == "1")
                {
                    WriteGolden(integrationDirectory, files);
                    Assert.Fail(
                        "已更新 golden 文件，请审阅 git diff 后重跑（不带 -UpdateLubanGolden）。");
                }

                var committed = ReadCommitted(integrationDirectory);

                Assert.That(committed.Keys, Is.EquivalentTo(files.Keys));
                foreach (var pair in files)
                {
                    Assert.That(committed[pair.Key], Is.EqualTo(Normalize(pair.Value)), pair.Key);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        private static Dictionary<string, string> ReadCommitted(string root)
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            string[] paths = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
            for (int i = 0; i < paths.Length; i++)
            {
                string relative = paths[i]
                    .Substring(root.Length + 1)
                    .Replace('\\', '/');
                files[relative] = Normalize(File.ReadAllText(paths[i], Encoding.UTF8));
            }

            return files;
        }

        private static void WriteGolden(
            string root,
            IReadOnlyDictionary<string, string> files)
        {
            foreach (var pair in files)
            {
                string path = Path.Combine(
                    root, pair.Key.Replace('/', Path.DirectorySeparatorChar));
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, pair.Value, new UTF8Encoding(false));
            }
        }

        private static string Normalize(string text)
        {
            if (text.Length > 0 && text[0] == '﻿')
            {
                text = text.Substring(1);
            }

            return text.Replace("\r\n", "\n");
        }
    }
}
