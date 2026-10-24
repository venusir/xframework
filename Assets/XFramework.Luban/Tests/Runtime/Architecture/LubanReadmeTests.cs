using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace XFramework.XConfig.Luban.Tests
{
    /// <summary>
    /// 包 README 的结构与文件树守卫：扩展包不在核心 check-docs 射程内，
    /// 这层由包内测试补齐（与核心 ReadmeSectionTests 同型，但只覆盖本包）。
    /// </summary>
    [TestFixture]
    public sealed class LubanReadmeTests
    {
        private static readonly string[] RequiredSections =
        {
            "概述",
            "快速使用",
            "模块结构",
            "线程契约",
            "已知限制",
        };

        [Test]
        public void Readme_HasRequiredSections()
        {
            var headings = FindHeadings(ReadReadme());

            for (int i = 0; i < RequiredSections.Length; i++)
            {
                Assert.That(headings, Does.Contain(RequiredSections[i]),
                    "README 缺少 ## " + RequiredSections[i]);
            }
        }

        [Test]
        public void Readme_ModuleTreeMatchesDisk()
        {
            var tree = ParseModuleTree(ReadReadme());

            Assert.That(tree.Runtime, Is.EquivalentTo(EnumerateCs("Runtime")));
            Assert.That(tree.Editor, Is.EquivalentTo(EnumerateCs("Editor")));
        }

        private static string ReadReadme()
        {
            return File.ReadAllText(
                Path.Combine(LubanRuntimeTestPaths.PackageRoot, "README.md"));
        }

        private static List<string> FindHeadings(string readme)
        {
            var headings = new List<string>();
            var matches = Regex.Matches(readme, @"^##\s+(?<name>.+?)\s*$",
                RegexOptions.Multiline);
            for (int i = 0; i < matches.Count; i++)
            {
                headings.Add(matches[i].Groups["name"].Value.Trim());
            }

            return headings;
        }

        private static List<string> EnumerateCs(string section)
        {
            string root = Path.Combine(LubanRuntimeTestPaths.PackageRoot, section);
            string[] files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
            var names = new List<string>(files.Length);
            for (int i = 0; i < files.Length; i++)
            {
                names.Add(Path.GetFileName(files[i]));
            }

            return names;
        }

        private static (List<string> Runtime, List<string> Editor) ParseModuleTree(string readme)
        {
            string[] lines = readme.Replace("\r\n", "\n").Split('\n');
            int start = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() == "## 模块结构")
                {
                    start = i + 1;
                    break;
                }
            }

            Assert.That(start, Is.GreaterThan(0), "README 缺少 ## 模块结构");

            int fenceStart = -1;
            for (int i = start; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("## ", StringComparison.Ordinal))
                {
                    break;
                }

                if (lines[i].StartsWith("```", StringComparison.Ordinal))
                {
                    fenceStart = i + 1;
                    break;
                }
            }

            Assert.That(fenceStart, Is.GreaterThan(0), "模块结构下缺少代码块");

            var runtime = new List<string>();
            var editor = new List<string>();
            string section = null;
            for (int i = fenceStart; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    break;
                }

                bool topLevel = line.Length > 0 &&
                                !char.IsWhiteSpace(line[0]) &&
                                (line[0] == '├' || line[0] == '└');
                string name = FirstToken(line);
                if (topLevel)
                {
                    section = name.EndsWith("/", StringComparison.Ordinal)
                        ? name.TrimEnd('/')
                        : null;
                    continue;
                }

                if (section == null || !name.EndsWith(".cs", StringComparison.Ordinal))
                {
                    continue;
                }

                if (section == "Runtime")
                {
                    runtime.Add(name);
                }
                else if (section == "Editor")
                {
                    editor.Add(name);
                }
            }

            return (runtime, editor);
        }

        private static string FirstToken(string line)
        {
            string name = line.Trim();
            while (name.Length > 0 &&
                   (name[0] == '│' || name[0] == '├' || name[0] == '└' ||
                    name[0] == '─' || name[0] == ' '))
            {
                name = name.Substring(1);
            }

            int space = name.IndexOf(' ');
            return space >= 0 ? name.Substring(0, space) : name;
        }
    }
}
