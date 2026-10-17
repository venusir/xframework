using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace XFramework.XCamera.Cinemachine.Tests
{
    /// <summary>
    /// 扩展包的依赖与文档自洽守卫：Cinemachine 3.1.7 在 `package.json` / README / 开发 manifest 三处一致；
    /// 扩展 README 声明的核心版本与核心 `package.json` 一致；`Runtime/README.md` 的五节必答存在；
    /// 示例路径存在；扩展包内的 `.cs` / `.md` 不指向包外的维护向文档。
    /// <para>本守卫取代核心包 `<c>PackageBoundaryTests</c>` 对扩展包的覆盖面（核心门禁只扫核心包）。</para>
    /// </summary>
    [TestFixture]
    public class CinemachineDependencyTests
    {
        private const string CinemachineVersion = "3.1.7";

        // 拆分写法：守卫自身不应命中「包内不指向包外」的扫描。
        private static readonly string RepoDocsMarker = "Documentation" + "/";

        private static string PackageRoot => Path.Combine(Application.dataPath, "XFramework.Cinemachine");

        private static string CoreRoot => Path.Combine(Application.dataPath, "XFramework");

        private static string RepositoryRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        [Test]
        public void CinemachineVersion_IsTheSameInPackageReadmeAndManifest()
        {
            var pair = new Regex(
                "\"com\\.unity\\.cinemachine\"\\s*:\\s*\"" + Regex.Escape(CinemachineVersion) + "\"");
            var violations = new List<string>();

            foreach (var file in new[]
                     {
                         Path.Combine(PackageRoot, "package.json"),
                         Path.Combine(PackageRoot, "README.md"),
                         Path.Combine(RepositoryRoot, "Packages", "manifest.json"),
                     })
            {
                if (!pair.IsMatch(File.ReadAllText(file)))
                    violations.Add(Relative(file));
            }

            Assert.IsEmpty(violations,
                "Cinemachine " + CinemachineVersion + " 必须在扩展 package.json、扩展 README、开发 manifest 三处逐字一致：" +
                "\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void Readme_DeclaresTheCoreVersionItDependsOn()
        {
            var coreVersion = ReadCoreVersion();
            var readme = File.ReadAllText(Path.Combine(PackageRoot, "README.md"));

            StringAssert.Contains(coreVersion, readme,
                "扩展 README 必须写明兼容的核心包版本（当前 " + coreVersion + "）——扩展不在 package.json 里声明核心依赖" +
                "（UPM 不支持 Git URL 依赖），README 是那条约束的落点。");
        }

        [Test]
        public void RuntimeReadme_HasTheFiveRequiredSections()
        {
            var readme = File.ReadAllText(Path.Combine(PackageRoot, "Runtime", "README.md"));
            var missing = new List<string>();

            foreach (var heading in new[] { "## 概述", "## 快速使用", "## 模块结构", "## 线程契约", "## 已知限制" })
            {
                if (readme.IndexOf(heading, StringComparison.Ordinal) < 0)
                    missing.Add(heading);
            }

            Assert.IsEmpty(missing, "Runtime/README.md 缺少必答节：\n  " + string.Join("\n  ", missing));
        }

        [Test]
        public void Samples_PathResolvesToAnExistingDirectory()
        {
            var packageJson = File.ReadAllText(Path.Combine(PackageRoot, "package.json"));
            var violations = new List<string>();

            foreach (Match sample in Regex.Matches(packageJson, "\"path\"\\s*:\\s*\"([^\"]+)\""))
            {
                var relative = sample.Groups[1].Value;
                var full = Path.Combine(PackageRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(full))
                    violations.Add("package.json 声明的示例路径不存在：" + relative);
            }

            Assert.IsEmpty(violations, "示例声明与包内目录对不上：\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void PackageText_DoesNotReferenceRepoDocumentation()
        {
            var violations = new List<string>();
            foreach (var file in Directory.GetFiles(PackageRoot, "*", SearchOption.AllDirectories))
            {
                var extension = Path.GetExtension(file);
                if (!string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (File.ReadAllText(file).IndexOf(RepoDocsMarker, StringComparison.Ordinal) >= 0)
                    violations.Add(Relative(file));
            }

            Assert.IsEmpty(violations,
                "扩展包内的 .cs / .md 不得指向包外的维护向文档（" + RepoDocsMarker + "）：\n  " +
                string.Join("\n  ", violations));
        }

        private static string ReadCoreVersion()
        {
            var json = File.ReadAllText(Path.Combine(CoreRoot, "package.json"));
            var match = Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]+)\"");
            Assert.IsTrue(match.Success, "核心 package.json 找不到 version 字段");
            return match.Groups[1].Value;
        }

        private static string Relative(string path)
            => Path.GetRelativePath(PackageRoot, path);
    }
}
