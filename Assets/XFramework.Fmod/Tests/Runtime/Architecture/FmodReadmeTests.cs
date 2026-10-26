using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace XFramework.XAudio.Fmod.Tests
{
    /// <summary>
    /// 扩展包 README 的结构守卫：必答节 + <c>## 模块结构</c> 的 Runtime 文件树与磁盘双向一致。
    /// </summary>
    [TestFixture]
    public class FmodReadmeTests
    {
        private static readonly string[] RequiredSections =
        {
            "概述", "快速使用", "模块结构", "线程契约", "已知限制",
        };

        [Test]
        public void Readme_HasRequiredSections()
        {
            string readme = FmodTestPaths.ReadText("README.md");
            if (readme == null)
                Assert.Ignore("找不到 XFramework.Fmod 包根；跳过 README 结构守卫。");

            var sections = new HashSet<string>();
            foreach (Match match in Regex.Matches(readme, @"^##\s+(?<name>.+?)\s*$", RegexOptions.Multiline))
                sections.Add(match.Groups["name"].Value);

            var missing = new List<string>();
            foreach (string required in RequiredSections)
            {
                if (!sections.Contains(required))
                    missing.Add(required);
            }

            Assert.IsEmpty(missing,
                "README 缺少必答节（## 级）：\n  " + string.Join("\n  ", missing));
        }

        [Test]
        public void Readme_ModuleTreeMatchesDisk()
        {
            string root = FmodTestPaths.PackageRoot;
            string readme = FmodTestPaths.ReadText("README.md");
            if (root == null || readme == null)
                Assert.Ignore("找不到 XFramework.Fmod 包根；跳过模块树守卫。");

            var fromReadme = ReadRuntimeTree(readme);
            var fromDisk = new HashSet<string>(System.StringComparer.Ordinal);
            string runtime = Path.Combine(root, "Runtime");
            foreach (string file in Directory.GetFiles(runtime, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".meta", System.StringComparison.Ordinal))
                    continue;

                fromDisk.Add(Path.GetFileName(file));
            }

            Assert.IsNotEmpty(fromReadme, "README 的 ## 模块结构 未解析出 Runtime 文件");
            CollectionAssert.AreEquivalent(fromDisk, fromReadme,
                "README 的 ## 模块结构 与 Runtime/ 磁盘文件不一致（漏收或残影）");
        }

        private static HashSet<string> ReadRuntimeTree(string readme)
        {
            var files = new HashSet<string>(System.StringComparer.Ordinal);
            int section = readme.IndexOf("## 模块结构", System.StringComparison.Ordinal);
            if (section < 0)
                return files;

            int fenceStart = readme.IndexOf("```", section, System.StringComparison.Ordinal);
            if (fenceStart < 0)
                return files;

            int fenceEnd = readme.IndexOf("```", fenceStart + 3, System.StringComparison.Ordinal);
            if (fenceEnd < 0)
                return files;

            string block = readme.Substring(fenceStart + 3, fenceEnd - fenceStart - 3);
            foreach (Match match in Regex.Matches(block, @"(?<name>[A-Za-z0-9_.]+\.(?:cs|asmdef))"))
                files.Add(match.Groups["name"].Value);

            return files;
        }
    }
}
