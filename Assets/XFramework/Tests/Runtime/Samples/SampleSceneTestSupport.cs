using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using XFramework.Architecture.Tests;

namespace XFramework.Samples.Tests
{
    /// <summary>
    /// 示例场景守卫的共享工具：纯文件读取，不运行示例、不引用示例类型。
    /// <para>测试壳只共享 <c>Assets</c>，读不到仓库的 <c>Tools/</c>；因此这里只扫
    /// <c>Assets/XFramework/**</c> 与 <c>Library/PackageCache</c>。</para>
    /// </summary>
    internal static class SampleSceneTestSupport
    {
        #region Regex

        private static readonly Regex ScriptGuidRegex = new Regex(
            "m_Script:\\s*\\{[^}]*guid:\\s*([0-9a-fA-F]{32})", RegexOptions.Compiled);

        #endregion

        #region Paths

        internal static string CoreRoot => Path.Combine(Application.dataPath, "XFramework");

        internal static string SampleRoot(string sample)
        {
            return Path.Combine(CoreRoot, "Samples~", sample);
        }

        #endregion

        #region Meta

        /// <summary>示例目录下每个文件/目录都必须有同名 <c>.meta</c>（<c>.sample.json</c> 与根目录除外）。</summary>
        internal static void AssertAllEntriesHaveMeta(string sampleRoot)
        {
            var violations = new List<string>();

            foreach (var dir in Directory.GetDirectories(sampleRoot, "*", SearchOption.AllDirectories))
            {
                if (!File.Exists(dir + ".meta"))
                    violations.Add($"目录缺 .meta：{dir}");
            }

            foreach (var file in Directory.GetFiles(sampleRoot, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(file);
                if (name.EndsWith(".meta", StringComparison.Ordinal) || name == ".sample.json")
                    continue;

                if (!File.Exists(file + ".meta"))
                    violations.Add($"文件缺 .meta：{file}");
            }

            Assert.IsEmpty(violations, string.Join("\n", violations));
        }

        #endregion

        #region Script GUIDs

        /// <summary>场景引用的脚本 GUID 必须能解析到已知脚本（全零 GUID 除外）。</summary>
        internal static void AssertSceneScriptGuidsResolve(string sceneFile, Dictionary<string, string> map)
        {
            var violations = new List<string>();
            foreach (Match match in ScriptGuidRegex.Matches(File.ReadAllText(sceneFile)))
            {
                var guid = match.Groups[1].Value;
                if (guid == "00000000000000000000000000000000")
                    continue;

                if (!map.ContainsKey(guid))
                    violations.Add($"{Path.GetFileName(sceneFile)}: m_Script guid '{guid}' 解析不到脚本");
            }

            Assert.IsEmpty(violations, string.Join("\n", violations));
        }

        /// <summary>收集 <c>Assets/XFramework</c> 与相关包缓存里的全部脚本 GUID。</summary>
        internal static Dictionary<string, string> BuildScriptGuidMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AddMetaGuids(map, CoreRoot);

            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var packageCache = Path.Combine(projectRoot, "Library/PackageCache");
            Assert.IsTrue(Directory.Exists(packageCache), $"找不到 Library/PackageCache：{packageCache}");

            foreach (var pattern in new[]
                     {
                         "com.unity.ugui*", "com.unity.inputsystem*", "com.unity.cinemachine*",
                     })
            {
                foreach (var dir in Directory.GetDirectories(packageCache, pattern))
                    AddMetaGuids(map, dir);
            }

            return map;
        }

        internal static string ReadScriptGuid(string metaFile)
        {
            if (!File.Exists(metaFile))
                return null;

            foreach (var line in File.ReadLines(metaFile))
            {
                if (line.StartsWith("guid:", StringComparison.Ordinal))
                    return line.Substring("guid:".Length).Trim();
            }

            return null;
        }

        /// <summary>从包缓存里按脚本文件名找 GUID（<paramref name="scriptFileName"/> 不含扩展名）。</summary>
        internal static string FindPackageScriptGuid(string packagePattern, string scriptFileName)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var packageCache = Path.Combine(projectRoot, "Library/PackageCache");
            Assert.IsTrue(Directory.Exists(packageCache), $"找不到 Library/PackageCache：{packageCache}");

            foreach (var dir in Directory.GetDirectories(packageCache, packagePattern))
            {
                var metas = Directory.GetFiles(dir, scriptFileName + ".cs.meta", SearchOption.AllDirectories);
                if (metas.Length > 0)
                    return ReadScriptGuid(metas[0]);
            }

            Assert.Fail($"在 {packagePattern} 下找不到 {scriptFileName}.meta");
            return null;
        }

        internal static void AssertSceneContainsScriptGuid(string sceneFile, string scriptMetaFile, string message)
        {
            var guid = ReadScriptGuid(scriptMetaFile);
            Assert.IsNotNull(guid, $"{scriptMetaFile} 没有 guid。");
            StringAssert.Contains(guid, File.ReadAllText(sceneFile), message);
        }

        internal static void AssertSceneContainsGuid(string sceneFile, string guid, string message)
        {
            Assert.IsNotNull(guid, message + "（GUID 为空）");
            StringAssert.Contains(guid, File.ReadAllText(sceneFile), message);
        }

        internal static int CountOccurrences(string haystack, string needle)
        {
            int count = 0;
            int index = 0;
            while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }

        /// <summary>场景里的序列化引用（如 <c>_timer: {fileID: ...}</c>）必须存在且非 0。</summary>
        internal static void AssertSerializedReferenceNonZero(string sceneFile, string propertyName)
        {
            var match = Regex.Match(File.ReadAllText(sceneFile),
                propertyName + ":\\s*\\{fileID:\\s*(\\d+)");
            Assert.IsTrue(match.Success, $"场景缺少序列化字段 {propertyName}。");
            Assert.AreNotEqual("0", match.Groups[1].Value, $"{propertyName} 的引用为空。");
        }

        private static void AddMetaGuids(Dictionary<string, string> map, string root)
        {
            foreach (var meta in Directory.GetFiles(root, "*.cs.meta", SearchOption.AllDirectories))
            {
                var guid = ReadScriptGuid(meta);
                if (!string.IsNullOrEmpty(guid))
                    map[guid] = meta;
            }
        }

        #endregion

        #region Registration / Docs

        internal static void AssertPackageRegistersSample(string samplePath)
        {
            var manifest = File.ReadAllText(Path.Combine(CoreRoot, "package.json"));
            StringAssert.Contains($"\"path\": \"{samplePath}\"", manifest,
                $"package.json 的 samples 数组没有登记 {samplePath}。");
        }

        internal static void AssertSampleJsonHasDisplayNameAndDescription(string sampleRoot)
        {
            var sample = File.ReadAllText(Path.Combine(sampleRoot, ".sample.json"));
            StringAssert.Contains("\"displayName\"", sample);
            StringAssert.Contains("\"description\"", sample);
        }

        internal static void AssertReadmeMentions(string sampleRoot, string sceneName)
        {
            var readme = File.ReadAllText(Path.Combine(sampleRoot, "README.md"));
            StringAssert.Contains(sceneName, readme, $"README 没有提到 {sceneName}。");
            StringAssert.Contains("Play", readme, "README 没有说明按 Play 运行。");
        }

        #endregion

        #region Source Constraints

        internal static void AssertSampleSourcesAvoidConditionalCompilationAndUnityEditor(string sampleRoot)
        {
            var violations = new List<string>();

            foreach (var file in Directory.GetFiles(sampleRoot, "*.cs", SearchOption.AllDirectories))
            {
                var code = SourceScanner.ReadCodeOnly(file);

                foreach (var line in code.Split('\n'))
                {
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("#if", StringComparison.Ordinal) ||
                        trimmed.StartsWith("#elif", StringComparison.Ordinal) ||
                        trimmed.StartsWith("#else", StringComparison.Ordinal) ||
                        trimmed.StartsWith("#endif", StringComparison.Ordinal))
                    {
                        violations.Add($"{Path.GetFileName(file)}: 示例不允许条件编译（check-samples 不定义符号）");
                    }
                }

                if (code.Contains("UnityEditor", StringComparison.Ordinal))
                    violations.Add($"{Path.GetFileName(file)}: 示例不允许引用 UnityEditor");
            }

            Assert.IsEmpty(violations, string.Join("\n", violations));
        }

        internal static void AssertSampleTextDoesNotReferenceDocumentation(string sampleRoot)
        {
            var violations = new List<string>();

            foreach (var pattern in new[] { "*.cs", "*.md" })
            {
                foreach (var file in Directory.GetFiles(sampleRoot, pattern, SearchOption.AllDirectories))
                {
                    if (File.ReadAllText(file).Contains("Documentation" + "/", StringComparison.Ordinal))
                        violations.Add($"{Path.GetFileName(file)}: 包内文本不得引用维护向文档目录");
                }
            }

            Assert.IsEmpty(violations, string.Join("\n", violations));
        }

        #endregion
    }
}
