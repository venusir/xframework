using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using XFramework.XLock;

namespace XFramework.Architecture.Tests
{
    /// <summary>
    /// 包 README 四张手改清单表的**机械守卫**。
    /// <para><b>为什么需要它</b>：四张表全靠人手同步，实测已漏过一整轮——ReactiveList 建成那轮三张表全漏，
    /// 跨一轮才被补（2026-10-11 的逐表核对是一次性的）。本 fixture 把那次人工核对变成每次全量门禁都会跑的
    /// 用例：「新模块 / 新功能上线要同步表」从此不靠记性。</para>
    /// <para><b>四张表与守法</b>：①「模块索引」「可替换的实现一览」（末行 <c>Bootstrap / Pipeline</c> 合并行
    /// 拆开）「可配置项一览」的模块集与 <c>Runtime/</c> 直接子目录**双向相等**（模块全集的定义在目录，
    /// 不在这三张表）；②「模块索引」的文档链接逐行落盘校验；③「模块索引」的命名空间列与 Runtime 程序集
    /// 命名空间**双向覆盖**——前向按约定推导（File 的历史命名 <c>XFileManager</c> 登记在
    /// <see cref="NamespaceExceptions"/>），反向抓程序集里不属于任何模块的游离命名空间；④「主要功能」不是
    /// 模块粒度（功能亮点 + 表下显式豁免句），按**登记表**守：<see cref="FeatureTableRegistry"/>
    /// （模块 → 触发词）与 <see cref="FeatureTableExemptions"/>（模块 → 豁免理由）必须与模块全集双向相等，
    /// **未登记即红**——与 <c>AutoInitTests</c> 的族清单同一形状；⑤ 包 README 里带 <c>#锚点</c> 的
    /// <c>.md</c> 链接必须指向目标文件里存在的 <c>##</c> 标题（2026-10-17 加：守「可替换的实现一览」的
    /// 「实现方入口」锚点与既有的 Tip 锚点）。</para>
    /// <para><b>放 PlayMode 而不是 EditMode</b>：本仓的开发回路里，带 <c>-Filter</c>
    /// 且未指定平台时只跑 PlayMode，EditMode 侧的守卫会被这个回路静默跳过（同 <c>AutoInitTests</c>）。
    /// 读 README 只需文件系统路径（测试壳的 Assets 是指回仓库的 junction），不需要 Unity 资产 API，
    /// 故留在 Runtime 测试程序集。</para>
    /// <para><b>本守卫守不到什么</b>：① **行内说明文字的陈旧**——ReactiveList 那轮的另一半正是既有行的
    /// 说明没更新，集合相等看不见；② 四张表**顺序刻意各异**（分层序 / 替换面序 / 配置面序），一律不比较；
    /// ③ 行内容正确性（职责、说明是否属实）仍靠评审；④ 其余 <c>.md</c> 表不在射程——「路径与目录」
    /// 「框架约定的名字」仍靠评审，「第三方依赖」表由 Editor 侧 <c>DependencyDeclarationTests</c> 另守
    /// （两个测试程序集互不可见，不跨引）；⑤ 两条解析前提「每个节只有一张表」「单元格内无转义竖线」
    /// 当前成立，被破坏时列数校验会报「格式坏」而不是静默误解析；⑥ **锚点指向的语义**——只校验
    /// 「目标文件里存在规范化后同名的 <c>##</c> 标题」，不校验那一节的内容是不是链接想指的（内容正确性
    /// 仍靠评审）。</para>
    /// <para><b>新模块 / 新例外来这里登记</b>：<see cref="NamespaceExceptions"/>（命名空间不符合
    /// <c>XFramework.X&lt;模块&gt;</c> 推导的模块）、<see cref="FeatureTableRegistry"/>（上功能表的模块及其
    /// 触发词）、<see cref="FeatureTableExemptions"/>（刻意不上功能表的模块及理由）。</para>
    /// </summary>
    [TestFixture]
    public class ReadmeTableTests
    {
        #region 锚点与登记处

        private const string FeatureHeading = "主要功能";
        private const string ModuleIndexHeading = "模块索引";
        private const string ReplaceableHeading = "可替换的实现一览";
        private const string ConfigHeading = "可配置项一览";

        /// <summary>
        /// 命名空间例外登记处：模块名 → 期望命名空间根。默认推导是 <c>XFramework.X&lt;模块&gt;</c>，
        /// 不符合推导的必须登记在这里（否则命名空间用例前向、反向都会红）。
        /// </summary>
        private static readonly Dictionary<string, string> NamespaceExceptions =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // File 是历史命名：模块目录叫 Runtime/File、门面叫 FileManager，命名空间沿用 XFileManager。
            { "File", "XFramework.XFileManager" },
        };

        /// <summary>
        /// 功能表登记处：上「## 主要功能」表的模块 → 该模块在表里出现的**触发词**（主类型名，实际逐字
        /// 出现在表的数据格里）。新增上表模块时来这里登记；不上表的模块去
        /// <see cref="FeatureTableExemptions"/> 登记豁免与理由。
        /// </summary>
        private static readonly (string Module, string Trigger)[] FeatureTableRegistry =
        {
            ("Update", "UpdateManager"),
            ("Pipeline", "Pipeline"),
            ("Bootstrap", "Bootstrap"),
            ("Pool", "PoolManager"),
            ("UI", "UIManager"),
            ("Config", "ConfigManager"),
            ("Message", "MessageManager"),
            ("Lock", "LockManager"),
            ("Reactive", "ReactiveProperty"),
            ("Event", "EventStream"),
            ("Input", "InputManager"),
            ("Audio", "AudioManager"),
            ("Timer", "TimerManager"),
            ("Log", "LogManager"),
            ("Scene", "SceneManager"),
        };

        /// <summary>
        /// 功能表豁免登记处：刻意不上「## 主要功能」表的模块 → 理由（会进违规消息）。豁免的真相在这里；
        /// README 表下的豁免句必须把这些模块名列出来（守卫校验名字在不在，不改措辞）。
        /// </summary>
        private static readonly (string Module, string Reason)[] FeatureTableExemptions =
        {
            ("Asset", "基础设施：第三方主要经 Bootstrap / DefaultGameLauncher 接触它"),
            ("Save", "基础设施：经 Bootstrap 装配，用法在 Save 的 README"),
            ("Data", "底层设施：经 Bootstrap 装配"),
            ("Settings", "设置类型由业务定义，没有统一入口"),
            ("Localization", "可选装配件（「启动引导」一行已写明 Localization 可选）"),
            ("File", "更底层的文件域 / Provider 抽象，主要由 Save / Settings 消费"),
            ("Serialize", "内部复用件：供 Data / Save / Config 复用，无独立用法面"),
            ("Diagnostics", "Editor 侧工具面，非运行时亮点"),
        };

        #endregion

        #region 用例

        [Test]
        public void ModuleSets_EqualRuntimeDirectories()
        {
            var violations = new List<string>();
            var modules = SourceScanner.CollectRuntimeModules(violations);

            if (modules != null)
            {
                CheckModuleSet(ModuleIndexHeading, 4, modules, violations);
                CheckModuleSet(ReplaceableHeading, 3, modules, violations);
                CheckModuleSet(ConfigHeading, 3, modules, violations);
            }

            Assert.IsEmpty(violations,
                "包 README 的清单表与 Runtime 目录不一致：\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void DocumentationLinks_PointToExistingModuleReadmes()
        {
            var violations = new List<string>();
            var rows = ReadTableRows(ModuleIndexHeading, 4, violations);

            if (rows != null)
            {
                foreach (var row in rows)
                {
                    var modules = ParseModuleCell(row.Cells[0]);
                    if (modules.Count != 1)
                    {
                        violations.Add($"README.md:{row.Number} 的「模块索引」行列出了 {modules.Count} 个模块" +
                                       "——本表每行一个模块（链接按行取），合并行只出现在「可替换的实现一览」");
                        continue;
                    }

                    string module = modules[0];
                    string cell = row.Cells[3];
                    string target = ExtractLinkTarget(cell);

                    if (target == null)
                    {
                        violations.Add($"表「模块索引」的 {module} 行文档格没有 markdown 链接：{cell}" +
                                       $"——应为 [README](Runtime/{module}/README.md)");
                        continue;
                    }

                    string expected = $"Runtime/{module}/README.md";
                    if (!string.Equals(target, expected, StringComparison.Ordinal))
                    {
                        violations.Add($"表「模块索引」的 {module} 行文档链接指向 {target}，应为 {expected}" +
                                       "——复制行时最容易只改模块名、不改链接");
                    }

                    string full = Path.Combine(SourceScanner.PackageRoot,
                        target.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(full))
                    {
                        violations.Add($"表「模块索引」的 {module} 行链接目标落盘不存在：{target}");
                    }
                }
            }

            Assert.IsEmpty(violations,
                "包 README 的模块文档链接指不到真文件：\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void AnchorLinks_PointToExistingHeadings()
        {
            var violations = new List<string>();
            var lines = ReadReadmeLines(violations);
            if (lines == null) return;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int searchFrom = 0;
                while (true)
                {
                    int open = line.IndexOf("](", searchFrom, StringComparison.Ordinal);
                    if (open < 0) break;

                    int close = line.IndexOf(')', open + 2);
                    if (close < 0) break;

                    string target = line.Substring(open + 2, close - open - 2).Trim();
                    searchFrom = close + 1;

                    int hash = target.IndexOf('#');
                    if (hash <= 0) continue;

                    string pathPart = target.Substring(0, hash);
                    string fragment = target.Substring(hash + 1);
                    if (fragment.Length == 0) continue;
                    if (!pathPart.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;
                    if (pathPart.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        pathPart.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string full = Path.Combine(SourceScanner.PackageRoot,
                        pathPart.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(full))
                    {
                        violations.Add($"README.md:{i + 1} 的锚点链接目标不存在：{target}");
                        continue;
                    }

                    string expected = Uri.UnescapeDataString(fragment);
                    if (!HasLevelTwoHeading(full, expected))
                    {
                        violations.Add($"README.md:{i + 1} 的锚点链接 {target} 找不到对应标题" +
                                       $"——目标文件里没有规范化后等于「{expected}」的 ## 标题");
                    }
                }
            }

            Assert.IsEmpty(violations,
                "包 README 的节锚点失效（链接指向的标题不存在或被改名）：\n  " +
                string.Join("\n  ", violations));
        }

        [Test]
        public void Namespaces_MatchTheRuntimeAssemblyBothWays()
        {
            var violations = new List<string>();
            var rows = ReadTableRows(ModuleIndexHeading, 4, violations);
            var modules = SourceScanner.CollectRuntimeModules(violations);
            var assemblyNamespaces = CollectAssemblyNamespaces();

            // 前向：表里每一行的命名空间必须等于按约定推导的根，且真的存在于程序集
            if (rows != null)
            {
                foreach (var row in rows)
                {
                    var names = ParseModuleCell(row.Cells[0]);
                    if (names.Count != 1)
                    {
                        violations.Add($"README.md:{row.Number} 的「模块索引」行列出了 {names.Count} 个模块" +
                                       "——本表每行一个模块（命名空间按行取）");
                        continue;
                    }

                    string module = names[0];
                    string actual = row.Cells[1].Trim().Trim('`').Trim();
                    string expected = ExpectedNamespaceRoot(module);

                    if (!string.Equals(actual, expected, StringComparison.Ordinal))
                    {
                        violations.Add($"表「模块索引」的 {module} 行命名空间写的是 {actual}，按约定应为 {expected}" +
                                       (NamespaceExceptions.ContainsKey(module)
                                           ? "（该例外登记在 ReadmeTableTests.NamespaceExceptions）"
                                           : "——新例外必须去 ReadmeTableTests.NamespaceExceptions 登记，否则这里会红"));
                    }

                    if (!assemblyNamespaces.Contains(actual))
                    {
                        violations.Add($"表「模块索引」的 {module} 行写的命名空间 {actual} 在 Runtime 程序集里不存在" +
                                       "——是名字改了，还是模块没按约定声明命名空间？");
                    }
                }
            }

            // 反向：程序集里的每个 XFramework.* 命名空间都要被某个模块的根覆盖（等于或以其加点为前缀）。
            // 这一条用约定推导根、不用表里的格值，与前向互相独立——互换两行的命名空间也抓得住。
            if (modules != null)
            {
                foreach (var ns in SourceScanner.Sorted(assemblyNamespaces))
                {
                    bool covered = false;
                    foreach (var module in modules)
                    {
                        string root = ExpectedNamespaceRoot(module);
                        if (string.Equals(ns, root, StringComparison.Ordinal) ||
                            ns.StartsWith(root + ".", StringComparison.Ordinal))
                        {
                            covered = true;
                            break;
                        }
                    }

                    if (!covered)
                    {
                        violations.Add($"Runtime 程序集里的命名空间 {ns} 不属于任何 Runtime 模块——" +
                                       "新模块请建 Runtime/<模块>/ 并同步四张表；游离类型请归到所属模块的命名空间");
                    }
                }
            }

            Assert.IsEmpty(violations,
                "包 README 的命名空间列与 Runtime 程序集对不上：\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void FeatureTable_EveryRuntimeModuleIsRegisteredOrExempt()
        {
            var violations = new List<string>();
            var modules = SourceScanner.CollectRuntimeModules(violations);
            var section = ReadSection(FeatureHeading, violations);
            var rows = section != null ? ReadTableRows(section, FeatureHeading, 2, violations) : null;

            // ① 登记处自身一致：无重复、两处不相交
            var registered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in FeatureTableRegistry)
            {
                if (!registered.Add(entry.Module))
                {
                    violations.Add($"FeatureTableRegistry 重复登记模块 {entry.Module}");
                }
            }

            var exempt = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in FeatureTableExemptions)
            {
                if (!exempt.Add(entry.Module))
                {
                    violations.Add($"FeatureTableExemptions 重复登记模块 {entry.Module}");
                }
            }

            foreach (var module in SourceScanner.Sorted(registered))
            {
                if (exempt.Contains(module))
                {
                    violations.Add($"{module} 同时出现在 FeatureTableRegistry 与 FeatureTableExemptions" +
                                   "——一个模块要么上功能表、要么豁免，两处只能占一处");
                }
            }

            // ② 登记处 ↔ Runtime 目录：未登记即红；改名/删除后未同步也红
            if (modules != null)
            {
                foreach (var module in SourceScanner.Sorted(modules))
                {
                    if (!registered.Contains(module) && !exempt.Contains(module))
                    {
                        violations.Add($"Runtime/ 下的模块 {module} 未在功能表登记处登记——两条路：" +
                                       "① 去 README「## 主要功能」加一行，并在 ReadmeTableTests.FeatureTableRegistry " +
                                       "登记它的触发词；② 在 ReadmeTableTests.FeatureTableExemptions 登记豁免并写明理由");
                    }
                }

                foreach (var module in SourceScanner.Sorted(registered))
                {
                    if (!modules.Contains(module))
                    {
                        violations.Add($"FeatureTableRegistry 登记了 {module}，但 Runtime/ 下没有该目录" +
                                       "——模块改名/删除后请同步登记处");
                    }
                }

                foreach (var module in SourceScanner.Sorted(exempt))
                {
                    if (!modules.Contains(module))
                    {
                        violations.Add($"FeatureTableExemptions 登记了 {module}，但 Runtime/ 下没有该目录" +
                                       "——模块改名/删除后请同步登记处");
                    }
                }
            }

            // ③ 触发词必须出现在「## 主要功能」节的表格数据格里。必须限定在节内：UpdateManager 等词
            //    在 README 别处（快速开始、可替换的实现一览）也出现，全文件搜索会让这条断言失效。
            if (rows != null)
            {
                var cells = new List<string>();
                foreach (var row in rows)
                {
                    foreach (var cell in row.Cells) cells.Add(cell);
                }

                string tableText = string.Join("\n", cells);
                foreach (var entry in FeatureTableRegistry)
                {
                    if (!tableText.Contains(entry.Trigger))
                    {
                        violations.Add($"FeatureTableRegistry 说 {entry.Module} 由「{entry.Trigger}」代表上功能表，" +
                                       "但「## 主要功能」节的表格里找不到这个词——行被删/改写时请同步登记处，" +
                                       "或把该模块移进豁免表");
                    }
                }
            }

            // ④ 豁免句：豁免模块对读者唯一的解释就在表下那句引用里，名字消失即红（纯措辞变化不红）。
            //    它只在「名字被删」时报警，是刻意留的窄口——理由的正文仍归 README 作者。
            if (section != null)
            {
                var quote = new List<string>();
                foreach (var line in section)
                {
                    if (line.Text.TrimStart().StartsWith(">", StringComparison.Ordinal)) quote.Add(line.Text);
                }

                string quoteText = string.Join("\n", quote);
                foreach (var entry in FeatureTableExemptions)
                {
                    if (!quoteText.Contains(entry.Module))
                    {
                        violations.Add($"「## 主要功能」的豁免句（> 引用行）没有列出豁免模块 {entry.Module}" +
                                       $"（登记的理由：{entry.Reason}）——豁免的真相在 ReadmeTableTests.FeatureTableExemptions；" +
                                       "有意改写句子时请在同一个提交里同步两处");
                    }
                }
            }

            Assert.IsEmpty(violations,
                "功能表登记处与 README / Runtime 目录对不上：\n  " + string.Join("\n  ", violations));
        }

        #endregion

        #region 表解析

        private static string[] ReadReadmeLines(List<string> violations)
        {
            string path = Path.Combine(SourceScanner.PackageRoot, "README.md");
            if (!File.Exists(path))
            {
                violations.Add($"找不到 {path}——本守卫按仓库布局定位（测试壳把 Assets junction 过来，这里应当存在）");
                return null;
            }

            return File.ReadAllLines(path);
        }

        /// <summary>
        /// 按 <c>## 标题</c> 锚定读出一节（返回节内每行与其 1 基行号，供失败消息定位）。
        /// **不按行号锚定**：README 随时增删，行号必漂移。
        /// </summary>
        private static List<(string Text, int Number)> ReadSection(string heading, List<string> violations)
        {
            var lines = ReadReadmeLines(violations);
            if (lines == null) return null;

            int start = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("## ", StringComparison.Ordinal) &&
                    string.Equals(lines[i].Substring(3).Trim(), heading, StringComparison.Ordinal))
                {
                    start = i;
                    break;
                }
            }

            if (start < 0)
            {
                violations.Add($"README.md 找不到「## {heading}」节——本守卫按标题锚定，标题被改会失明；" +
                               "请恢复标题，或同步 ReadmeTableTests 的节标题常量");
                return null;
            }

            var section = new List<(string, int)>();
            for (int i = start + 1; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("## ", StringComparison.Ordinal)) break;
                section.Add((lines[i], i + 1));
            }

            return section;
        }

        private static List<(string[] Cells, int Number)> ReadTableRows(
            string heading, int expectedColumns, List<string> violations)
        {
            var section = ReadSection(heading, violations);
            if (section == null) return null;

            return ReadTableRows(section, heading, expectedColumns, violations);
        }

        /// <summary>
        /// 取节内**第一个连续表格块**的数据行（跳过表头与分隔行，分隔行按内容识别以便表头缺失时不吞数据行）。
        /// 列数不符的行报「格式坏」而不是按位置硬取——不静默误解析。
        /// </summary>
        private static List<(string[] Cells, int Number)> ReadTableRows(
            List<(string Text, int Number)> section, string heading, int expectedColumns,
            List<string> violations)
        {
            int start = -1;
            for (int i = 0; i < section.Count; i++)
            {
                if (IsTableLine(section[i].Text))
                {
                    start = i;
                    break;
                }
            }

            if (start < 0)
            {
                violations.Add($"README.md 的「## {heading}」节里找不到表格——节被改写会失明；" +
                               "请恢复表格，或同步 ReadmeTableTests");
                return null;
            }

            int end = start;
            while (end < section.Count && IsTableLine(section[end].Text)) end++;

            var rows = new List<(string[], int)>();
            bool headerSkipped = false;
            for (int i = start; i < end; i++)
            {
                var cells = SplitRow(section[i].Text);
                if (IsSeparatorRow(cells)) continue;

                if (!headerSkipped)
                {
                    headerSkipped = true;
                    continue;
                }

                if (cells.Length != expectedColumns)
                {
                    violations.Add($"README.md:{section[i].Number} 的「{heading}」表格行有 {cells.Length} 列，" +
                                   $"期望 {expectedColumns} 列——表格格式坏了（本守卫按列位置取值，不猜）");
                    continue;
                }

                rows.Add((cells, section[i].Number));
            }

            return rows;
        }

        private static bool IsTableLine(string line)
            => line.TrimStart().StartsWith("|", StringComparison.Ordinal);

        private static string[] SplitRow(string line)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("|", StringComparison.Ordinal)) trimmed = trimmed.Substring(1);
            if (trimmed.EndsWith("|", StringComparison.Ordinal)) trimmed = trimmed.Substring(0, trimmed.Length - 1);

            var cells = trimmed.Split('|');
            for (int i = 0; i < cells.Length; i++) cells[i] = cells[i].Trim();
            return cells;
        }

        /// <summary>分隔行（<c>|---|---|</c>）：单元格只含 <c>-</c>、<c>:</c> 与空白，且至少一个 <c>-</c>。</summary>
        private static bool IsSeparatorRow(string[] cells)
        {
            if (cells.Length == 0) return false;

            foreach (var cell in cells)
            {
                bool hasDash = false;
                foreach (char c in cell)
                {
                    if (c == '-')
                    {
                        hasDash = true;
                        continue;
                    }

                    if (c != ':' && c != ' ') return false;
                }

                if (!hasDash) return false;
            }

            return true;
        }

        /// <summary>剥加粗并按 <c>/</c> 拆分（「可替换的实现一览」末行 <c>Bootstrap / Pipeline</c> 由此展开为两个模块）。</summary>
        private static List<string> ParseModuleCell(string cell)
        {
            var modules = new List<string>();
            foreach (var part in cell.Trim().Trim('*').Split('/'))
            {
                string name = part.Trim().Trim('*').Trim();
                if (name.Length > 0) modules.Add(name);
            }

            return modules;
        }

        /// <summary>取 markdown 链接 <c>[文本](目标)</c> 的目标部分；没有链接返回 null。</summary>
        private static string ExtractLinkTarget(string cell)
        {
            int open = cell.IndexOf("](", StringComparison.Ordinal);
            if (open < 0) return null;

            int close = cell.LastIndexOf(')');
            if (close <= open + 2) return null;

            return cell.Substring(open + 2, close - open - 2).Trim();
        }

        /// <summary>目标文件是否存在某个 <c>##</c> 标题，其 GitHub 风格锚点等于 <paramref name="anchor"/>。</summary>
        private static bool HasLevelTwoHeading(string path, string anchor)
        {
            foreach (string line in File.ReadAllLines(path))
            {
                string trimmed = line.TrimStart();
                if (!trimmed.StartsWith("## ", StringComparison.Ordinal)) continue;

                if (string.Equals(ToAnchor(trimmed.Substring(3).Trim()), anchor, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// GitHub 风格锚点：小写；去掉行内 markdown 标记与标点；每个空白转一个 <c>-</c>（**不折叠**——
        /// <c>提示 / 浮动</c> 的 ` / ` 会变成 <c>--</c>，与 GitHub 一致）。只覆盖本仓 README 实际出现的形态
        /// （中文标题、全角括号、<c>/</c>、空格），不追求与 GitHub 全量一致。
        /// </summary>
        private static string ToAnchor(string heading)
        {
            var builder = new StringBuilder(heading.Length);
            foreach (char c in heading)
            {
                if (c == '`' || c == '*') continue;

                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
                else if (char.IsWhiteSpace(c))
                {
                    builder.Append('-');
                }
            }

            return builder.ToString();
        }

        #endregion

        #region 采集与工具

        /// <summary>
        /// Runtime 程序集里出现的全部 <c>XFramework</c> 系命名空间。根命名空间 <c>XFramework</c> 本身也在内——
        /// 哪天有类型落在根上，反向检查会要求把它归到某个模块（那是有意为之，不是遗漏）。
        /// </summary>
        private static HashSet<string> CollectAssemblyNamespaces()
        {
            var namespaces = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in typeof(LockManager).Assembly.GetTypes())
            {
                string ns = type.Namespace;
                if (ns == null) continue;

                if (string.Equals(ns, "XFramework", StringComparison.Ordinal) ||
                    ns.StartsWith("XFramework.", StringComparison.Ordinal))
                {
                    namespaces.Add(ns);
                }
            }

            return namespaces;
        }

        private static string ExpectedNamespaceRoot(string module)
            => NamespaceExceptions.TryGetValue(module, out string ns) ? ns : "XFramework.X" + module;

        private static void CheckModuleSet(
            string heading, int columns, HashSet<string> runtimeModules, List<string> violations)
        {
            var rows = ReadTableRows(heading, columns, violations);
            if (rows == null) return;

            var inTable = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                foreach (var module in ParseModuleCell(row.Cells[0]))
                {
                    if (!inTable.Add(module))
                    {
                        violations.Add($"表「{heading}」重复列出模块 {module}（README.md:{row.Number}）");
                    }
                }
            }

            foreach (var module in SourceScanner.Sorted(runtimeModules))
            {
                if (!inTable.Contains(module))
                {
                    violations.Add($"表「{heading}」缺少模块 {module} 的行——Runtime/ 下存在该目录；" +
                                   "新模块上线要同步四张表（本表是其中之一）");
                }
            }

            foreach (var module in SourceScanner.Sorted(inTable))
            {
                if (!runtimeModules.Contains(module))
                {
                    violations.Add($"表「{heading}」有 {module} 行，但 Runtime/ 下没有 {module} 目录" +
                                   "——模块被改名/删除，或该行写错了");
                }
            }
        }

        #endregion
    }
}
