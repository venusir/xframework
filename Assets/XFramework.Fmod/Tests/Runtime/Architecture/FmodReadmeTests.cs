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
                string relative = Path.GetRelativePath(runtime, file).Replace('\\', '/');
                if (relative.EndsWith(".meta", System.StringComparison.Ordinal) ||
                    HasIgnoredSegment(relative))
                {
                    continue;
                }

                fromDisk.Add("Runtime/" + relative);
            }

            Assert.IsNotEmpty(fromReadme, "README 的 ## 模块结构 未解析出 Runtime 文件");
            CollectionAssert.AreEquivalent(fromDisk, fromReadme,
                "README 的 ## 模块结构 与 Runtime/ 磁盘文件不一致（漏收或残影）");
        }

        [Test]
        public void ReadRuntimeTree_PreservesNestedDirectories()
        {
            const string readme = "## 模块结构\n\n```text\n" +
                                  "Runtime/\n" +
                                  "├── A.cs\n" +
                                  "└── Sub/\n" +
                                  "    └── A.cs\n" +
                                  "```\n";

            HashSet<string> paths = ReadRuntimeTree(readme);
            CollectionAssert.AreEquivalent(
                new[] { "Runtime/A.cs", "Runtime/Sub/A.cs" }, paths);
        }

        private static bool HasIgnoredSegment(string relativePath)
        {
            string[] segments = relativePath.Split('/');
            foreach (string segment in segments)
            {
                if (segment.StartsWith(".", System.StringComparison.Ordinal) ||
                    segment.EndsWith("~", System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
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
            var directories = new List<string>();
            string[] lines = block.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                if (line.Length == 0)
                    continue;

                int prefix = 0;
                while (prefix < line.Length && IsTreeChar(line[prefix]))
                    prefix++;

                string rest = line.Substring(prefix);
                int comment = rest.IndexOf('#');
                if (comment >= 0)
                    rest = rest.Substring(0, comment);

                rest = rest.Trim();
                if (rest.Length == 0)
                    continue;

                int depth = prefix / 4;
                if (rest.EndsWith("/", System.StringComparison.Ordinal))
                {
                    while (directories.Count > depth)
                        directories.RemoveAt(directories.Count - 1);
                    while (directories.Count < depth)
                        directories.Add(string.Empty);

                    directories.Add(rest.TrimEnd('/'));
                    continue;
                }

                if (!rest.EndsWith(".cs", System.StringComparison.Ordinal) &&
                    !rest.EndsWith(".asmdef", System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (depth == 0 || directories.Count < depth)
                    continue;

                var path = new System.Text.StringBuilder();
                for (int d = 0; d < depth; d++)
                {
                    if (d > 0)
                        path.Append('/');
                    path.Append(directories[d]);
                }

                path.Append('/');
                path.Append(rest);
                files.Add(path.ToString());
            }

            return files;
        }

        private static bool IsTreeChar(char value)
        {
            return value == ' ' || value == '│' || value == '├' || value == '└' || value == '─';
        }
    }
}
