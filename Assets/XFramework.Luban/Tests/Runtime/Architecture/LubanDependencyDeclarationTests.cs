using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace XFramework.XConfig.Luban.Tests
{
    /// <summary>
    /// 依赖声明守卫：扩展包 README 的「依赖」表与仓库 <c>Packages/manifest.json</c>
    /// 对 <c>com.code-philosophy.luban</c> 的版本/URL 必须逐字一致。
    /// </summary>
    [TestFixture]
    public sealed class LubanDependencyDeclarationTests
    {
        private const string PackageName = "com.code-philosophy.luban";

        [Test]
        public void ReadmeDependencyUrl_MatchesManifest()
        {
            // 仓内治理守卫：消费方的 manifest 可以安装不同版本的 com.code-philosophy.luban，
            // 在 PackageCache/embedded 布局下拿消费方 manifest 比对只会误红。
            if (!Directory.Exists(Path.Combine(Application.dataPath, "XFramework.Luban")))
            {
                Assert.Ignore(
                    "依赖声明守卫只在本仓运行；消费方 manifest 的 Luban 版本可不同于包 README。");
            }

            string packageRoot = LubanRuntimeTestPaths.PackageRoot;
            string readme = File.ReadAllText(Path.Combine(packageRoot, "README.md"));
            string projectRoot = FindProjectRoot();

            string manifest = File.ReadAllText(
                Path.Combine(projectRoot, "Packages", "manifest.json"));

            string readmeUrl = ExtractReadmeUrl(readme);
            string manifestUrl = ExtractManifestUrl(manifest);

            Assert.That(readmeUrl, Is.Not.Null.And.Not.Empty);
            Assert.That(manifestUrl, Is.Not.Null.And.Not.Empty);
            Assert.That(readmeUrl, Is.EqualTo(manifestUrl));
            Assert.That(readmeUrl, Does.Contain("#v1.2.0"));
        }

        private static string FindProjectRoot()
        {
            string current = Application.dataPath;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "Packages", "manifest.json")))
                {
                    return current;
                }

                current = Path.GetDirectoryName(current);
            }

            return Path.GetDirectoryName(Application.dataPath);
        }

        private static string ExtractReadmeUrl(string readme)
        {
            string[] lines = readme.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains(PackageName, StringComparison.Ordinal))
                {
                    continue;
                }

                var match = Regex.Match(lines[i], "`(?<url>https://[^`]+)`");
                if (match.Success)
                {
                    return match.Groups["url"].Value;
                }
            }

            return null;
        }

        private static string ExtractManifestUrl(string manifest)
        {
            var match = Regex.Match(
                manifest,
                "\"" + Regex.Escape(PackageName) + "\"\\s*:\\s*\"(?<url>[^\"]+)\"");
            return match.Success ? match.Groups["url"].Value : null;
        }
    }
}
