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
    /// <c>Samples~/Integration</c> 的常驻结构守卫：示例资产不参与 Unity 编译、场景/预制体引用
    /// 也没有编译器兜底，本 fixture 用纯文件读取把几条最容易漂移的约束钉住。
    /// <para><b>它守什么</b>：必需文件、<c>.meta</c> 完整性、<c>package.json</c> 登记、地址常量与
    /// 资源文件名一一对应、代码字面量都是登记过的地址、prefab/scene 的脚本 GUID 能解析、
    /// 面板预制体根脚本继承自面板基类、示例源码不写条件编译与编辑器 API、包内文本不引用
    /// 维护向文档目录。</para>
    /// <para>它<b>不</b>运行示例本身；运行期验收由一次性导入探针承担。</para>
    /// </summary>
    [TestFixture]
    public class IntegrationSampleStructureTests
    {
        #region Paths

        private static string AssetRoot => Application.dataPath;

        private static string CoreRoot => Path.Combine(AssetRoot, "XFramework");

        private static string SampleRoot => Path.Combine(CoreRoot, "Samples~/Integration");

        private static string PrefabRoot => Path.Combine(SampleRoot, "Prefabs");

        private static string SceneRoot => Path.Combine(SampleRoot, "Scenes");

        #endregion

        #region Regex

        private static readonly Regex AddressConstRegex = new Regex(
            "public\\s+const\\s+string\\s+\\w+\\s*=\\s*\"([^\"]+)\"", RegexOptions.Compiled);

        private static readonly Regex AddressLiteralRegex = new Regex(
            "\"(Integration\\.[^\"]+)\"", RegexOptions.Compiled);

        private static readonly Regex ScriptGuidRegex = new Regex(
            "m_Script:\\s*\\{[^}]*guid:\\s*([0-9a-fA-F]{32})", RegexOptions.Compiled);

        #endregion

        #region Tests

        [Test]
        public void RequiredFiles_Exist()
        {
            Assert.IsTrue(Directory.Exists(SampleRoot), $"找不到示例目录：{SampleRoot}");

            var required = new[]
            {
                ".sample.json",
                "README.md",
                "Scripts/Bootstrap/IntegrationLauncher.cs",
                "Scripts/App/AppFlowController.cs",
                "Scripts/Game/GameSceneController.cs",
                "Scripts/Diagnostics/IntegrationDiagnosticPanel.cs",
                "Prefabs/Integration.MainMenu.prefab",
                "Prefabs/Integration.Pause.prefab",
                "Prefabs/Integration.Loading.prefab",
                "Scenes/Integration.Boot.unity",
                "Scenes/Integration.Game.unity",
                "Audio/Integration.Bgm.wav",
                "Data/Integration.GameModes.json",
                "Data/Integration.Rules.json",
                "Data/Integration.InputOptions.asset",
            };

            foreach (var relative in required)
            {
                var path = Path.Combine(SampleRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                Assert.IsTrue(File.Exists(path), $"缺少示例文件：{relative}");
            }
        }

        [Test]
        public void AllEntries_HaveMeta()
        {
            var violations = new List<string>();

            foreach (var dir in Directory.GetDirectories(SampleRoot, "*", SearchOption.AllDirectories))
            {
                if (!File.Exists(dir + ".meta"))
                    violations.Add($"目录缺 .meta：{Relative(dir)}");
            }

            foreach (var file in Directory.GetFiles(SampleRoot, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(file);
                if (name.EndsWith(".meta", StringComparison.Ordinal) || name == ".sample.json")
                    continue;

                if (!File.Exists(file + ".meta"))
                    violations.Add($"文件缺 .meta：{Relative(file)}");
            }

            Assert.IsEmpty(violations, string.Join("\n", violations));
        }

        [Test]
        public void PackageManifest_RegistersSample()
        {
            var manifest = File.ReadAllText(Path.Combine(CoreRoot, "package.json"));
            StringAssert.Contains("\"path\": \"Samples~/Integration\"", manifest,
                "package.json 的 samples 数组没有登记 Samples~/Integration。");

            var sample = File.ReadAllText(Path.Combine(SampleRoot, ".sample.json"));
            StringAssert.Contains("\"displayName\"", sample);
            StringAssert.Contains("\"description\"", sample);
        }

        [Test]
        public void AddressConstants_MatchExactlyOneAsset()
        {
            var addresses = CollectAddressConstants();
            Assert.IsNotEmpty(addresses, "IntegrationAddresses.cs 里没有解析到地址常量。");

            var assetFiles = Directory.GetFiles(SampleRoot, "*", SearchOption.AllDirectories);

            foreach (var address in addresses)
            {
                // 模板（如 Integration.Lang_{0}）不参与一一对应检查。
                if (address.Contains("{0}", StringComparison.Ordinal))
                    continue;

                var matches = new List<string>();
                foreach (var file in assetFiles)
                {
                    if (Path.GetFileName(file).EndsWith(".meta", StringComparison.Ordinal))
                        continue;

                    if (Path.GetFileNameWithoutExtension(file) == address)
                        matches.Add(file);
                }

                Assert.AreEqual(1, matches.Count,
                    $"地址 '{address}' 应恰好对应一个资源文件，实际 {matches.Count} 个：" +
                    string.Join(", ", matches.ConvertAll(Relative)));
            }
        }

        [Test]
        public void CodeStringLiterals_AreRegisteredAddresses()
        {
            var addresses = new HashSet<string>(CollectAddressConstants(), StringComparer.Ordinal);
            var violations = new List<string>();

            foreach (var file in Directory.GetFiles(SampleRoot, "*.cs", SearchOption.AllDirectories))
            {
                var code = SourceScanner.ReadCodeOnly(file);
                foreach (Match match in AddressLiteralRegex.Matches(code))
                {
                    var literal = match.Groups[1].Value;
                    if (literal.Contains("{0}", StringComparison.Ordinal))
                        continue;

                    if (!addresses.Contains(literal))
                        violations.Add($"{Relative(file)}: 字面量 '{literal}' 不在 IntegrationAddresses 里");
                }
            }

            Assert.IsEmpty(violations, string.Join("\n", violations));
        }

        [Test]
        public void PrefabsAndScenes_ScriptGuidsResolve()
        {
            var map = BuildScriptGuidMap();
            var violations = new List<string>();

            foreach (var file in Directory.GetFiles(PrefabRoot, "*.prefab"))
                CheckScriptGuids(file, map, violations);

            foreach (var file in Directory.GetFiles(SceneRoot, "*.unity"))
                CheckScriptGuids(file, map, violations);

            Assert.IsEmpty(violations, string.Join("\n", violations));
        }

        [Test]
        public void PanelPrefabs_RootScriptsDeriveFromPanelBase()
        {
            var map = BuildScriptGuidMap();
            var violations = new List<string>();

            foreach (var prefab in Directory.GetFiles(PrefabRoot, "*.prefab"))
            {
                var sampleScripts = new List<string>();
                foreach (Match match in ScriptGuidRegex.Matches(File.ReadAllText(prefab)))
                {
                    var guid = match.Groups[1].Value;
                    if (map.TryGetValue(guid, out var meta) && IsUnder(meta, SampleRoot))
                    {
                        sampleScripts.Add(meta);
                    }
                }

                if (sampleScripts.Count != 1)
                {
                    violations.Add($"{Path.GetFileName(prefab)}: 示例脚本数量应为 1，实际 {sampleScripts.Count}");
                    continue;
                }

                var scriptPath = sampleScripts[0].Substring(0, sampleScripts[0].Length - ".meta".Length);
                var code = File.ReadAllText(scriptPath);
                if (!code.Contains(": UIPanelBase", StringComparison.Ordinal) &&
                    !code.Contains(": UIDialogPanel<", StringComparison.Ordinal) &&
                    !code.Contains(": UITipItem", StringComparison.Ordinal))
                {
                    violations.Add($"{Path.GetFileName(prefab)}: 根脚本 {Path.GetFileName(scriptPath)} 未继承面板基类");
                }
            }

            Assert.IsEmpty(violations, string.Join("\n", violations));
        }

        [Test]
        public void Scenes_ReferenceExpectedControllers()
        {
            var bootScene = Path.Combine(SceneRoot, "Integration.Boot.unity");
            var gameScene = Path.Combine(SceneRoot, "Integration.Game.unity");

            var launcherGuid = ReadScriptGuid(Path.Combine(SampleRoot, "Scripts/Bootstrap/IntegrationLauncher.cs.meta"));
            var controllerGuid = ReadScriptGuid(Path.Combine(SampleRoot, "Scripts/Game/GameSceneController.cs.meta"));

            Assert.IsNotNull(launcherGuid, "IntegrationLauncher.cs.meta 没有 guid。");
            Assert.IsNotNull(controllerGuid, "GameSceneController.cs.meta 没有 guid。");

            StringAssert.Contains(launcherGuid, File.ReadAllText(bootScene),
                "Boot 场景没有引用 IntegrationLauncher。");
            StringAssert.Contains(controllerGuid, File.ReadAllText(gameScene),
                "Game 场景没有引用 GameSceneController。");
        }

        [Test]
        public void SampleSources_DoNotUseConditionalCompilationOrUnityEditor()
        {
            var violations = new List<string>();

            foreach (var file in Directory.GetFiles(SampleRoot, "*.cs", SearchOption.AllDirectories))
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
                        violations.Add($"{Relative(file)}: 示例不允许条件编译（check-samples 不定义符号）");
                    }
                }

                if (code.Contains("UnityEditor", StringComparison.Ordinal))
                    violations.Add($"{Relative(file)}: 示例不允许引用 UnityEditor");
            }

            Assert.IsEmpty(violations, string.Join("\n", violations));
        }

        [Test]
        public void SampleText_DoesNotReferenceDocumentation()
        {
            var violations = new List<string>();

            foreach (var pattern in new[] { "*.cs", "*.md" })
            {
                foreach (var file in Directory.GetFiles(SampleRoot, pattern, SearchOption.AllDirectories))
                {
                    if (File.ReadAllText(file).Contains("Documentation" + "/", StringComparison.Ordinal))
                        violations.Add($"{Relative(file)}: 包内文本不得引用维护向文档目录");
                }
            }

            Assert.IsEmpty(violations, string.Join("\n", violations));
        }

        #endregion

        #region Helpers

        private static List<string> CollectAddressConstants()
        {
            var file = Path.Combine(SampleRoot, "Scripts/App/IntegrationAddresses.cs");
            Assert.IsTrue(File.Exists(file), $"找不到地址文件：{file}");

            var result = new List<string>();
            foreach (Match match in AddressConstRegex.Matches(SourceScanner.ReadCodeOnly(file)))
                result.Add(match.Groups[1].Value);

            return result;
        }

        private static Dictionary<string, string> BuildScriptGuidMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AddMetaGuids(map, CoreRoot);

            var projectRoot = Path.GetDirectoryName(AssetRoot);
            var packageCache = Path.Combine(projectRoot, "Library/PackageCache");
            if (!Directory.Exists(packageCache))
            {
                Assert.Fail($"找不到 Library/PackageCache：{packageCache}");
                return map;
            }

            foreach (var pattern in new[] { "com.unity.ugui*", "com.unity.inputsystem*", "com.unity.cinemachine*" })
            {
                foreach (var dir in Directory.GetDirectories(packageCache, pattern))
                    AddMetaGuids(map, dir);
            }

            return map;
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

        private static string ReadScriptGuid(string metaFile)
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

        private static void CheckScriptGuids(string file, Dictionary<string, string> map, List<string> violations)
        {
            foreach (Match match in ScriptGuidRegex.Matches(File.ReadAllText(file)))
            {
                var guid = match.Groups[1].Value;
                if (guid == "00000000000000000000000000000000")
                    continue;

                if (!map.ContainsKey(guid))
                    violations.Add($"{Relative(file)}: m_Script guid '{guid}' 解析不到脚本");
            }
        }

        private static string Relative(string path)
        {
            return IsUnder(path, SampleRoot)
                ? path.Substring(SampleRoot.Length).TrimStart('\\', '/').Replace('\\', '/')
                : path;
        }

        /// <summary>路径前缀判定：先统一分隔符，避免 <c>Path.Combine</c> 与 <c>Directory.GetFiles</c> 的混用误伤。</summary>
        private static bool IsUnder(string path, string root)
        {
            return path.Replace('/', '\\')
                .StartsWith(root.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
