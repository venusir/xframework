using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;

namespace XFramework.Architecture.Tests
{
    /// <summary>
    /// 公开 API 基线守卫的共用检查器：抓取 → 更新或比对 → 报告差异。
    /// <para><b>为什么抽出来</b>：Runtime 与 Editor 两条 fixture 只有程序集、基线路径、header 与显示名不同，
    /// 更新模式 / 差异报告 / `.diff` 落盘三件事完全相同。两份副本会在下一次改文案时立刻漂移。</para>
    /// <para><b>更新模式仍然只写盘、永不给绿灯</b>：写完后一律判失败并打印相对旧基线的差异——
    /// 绿灯只能由随后一次不带开关的重跑给出。</para>
    /// </summary>
    internal static class PublicApiBaselineCheck
    {
        #region Constants

        /// <summary>更新模式的唯一入口。<b>只认 <c>"1"</c></b>：写仓库文件是危险动作，
        /// <c>=0</c> 这种「看起来是关」的写法绝不能开启它。</summary>
        internal const string UpdateEnvVar = "XFW_UPDATE_API_BASELINE";

        /// <summary>失败消息里每段最多列多少条（一次命名空间迁移可能是几百行，全文灌进控制台没人看）。</summary>
        private const int MaxReportedPerSection = 50;

        #endregion

        #region Verify

        /// <summary>检查一个程序集的公开面是否与基线一致；更新模式下写盘后按设计判失败。</summary>
        /// <param name="assembly">要检查的程序集。</param>
        /// <param name="baselinePath">基线文件绝对路径。</param>
        /// <param name="diffPath">完整差异的落盘位置（测试运行目录，已被 .gitignore 忽略）。</param>
        /// <param name="header">基线文件头部（自动生成，含射程与处置说明）。</param>
        /// <param name="displayName">失败消息里的程序集称谓（如「Runtime」「Editor」）。</param>
        internal static void Verify(
            Assembly assembly,
            string baselinePath,
            string diffPath,
            string header,
            string displayName)
        {
            SortedSet<string> current = PublicApiSnapshot.Capture(assembly);

            if (Environment.GetEnvironmentVariable(UpdateEnvVar) == "1")
            {
                RunUpdateMode(assembly, current, baselinePath, diffPath, header, displayName);
                return;
            }

            Assert.IsTrue(File.Exists(baselinePath),
                $"[公开 API 基线] {displayName} 基线文件不存在：{baselinePath}\n" +
                "基线缺失按失败处理——**不静默重建**（能静默重建的守卫等于没有守卫，首次生成那一次" +
                "「人来审」是不可省的一步）。\n" +
                "首次生成请显式运行：pwsh -File Tools/run-tests.ps1 -UpdateApiBaseline\n" +
                "生成后审阅内容，连同它的 .meta 一起提交；`git status --short` 核一眼未跟踪文件。");

            string text = File.ReadAllText(baselinePath);
            Assert.IsNotEmpty(PublicApiSnapshot.Parse(text),
                $"[公开 API 基线] {displayName} 基线文件为空：{baselinePath}\n" +
                "空文件与「渲染器坏了、什么都没渲染出来」无法区分，故按失败处理。\n" +
                "删掉它并显式重建：pwsh -File Tools/run-tests.ps1 -UpdateApiBaseline");

            ReportDifferences(PublicApiSnapshot.Parse(text), current, baselinePath, diffPath, displayName, "与基线不一致");
        }

        #endregion

        #region 更新模式

        /// <summary>
        /// 更新模式：写盘 → **一律判失败**（内容与旧基线一致时除外）。
        /// <para><b>不做临时文件 + 原子替换</b>：写坏了，下一次运行必然与刚渲染的内容不一致 → 红；
        /// 而写盘只可能发生在显式开关下，且更新模式必然紧跟一次不带开关的复核跑——多一段代码换一个
        /// 已被流程覆盖的边界，不划算。</para>
        /// </summary>
        private static void RunUpdateMode(
            Assembly assembly,
            SortedSet<string> current,
            string baselinePath,
            string diffPath,
            string header,
            string displayName)
        {
            string rendered = PublicApiSnapshot.Render(assembly, header);
            SortedSet<string> previous = File.Exists(baselinePath)
                ? PublicApiSnapshot.Parse(File.ReadAllText(baselinePath))
                : null;

            if (previous != null && previous.SetEquals(current))
            {
                TestContext.WriteLine(
                    $"[公开 API 基线] {displayName} 更新模式：内容与旧基线一致（{current.Count} 行），未写盘。");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath));
            File.WriteAllText(baselinePath, rendered, new UTF8Encoding(false));

            if (previous == null)
            {
                Assert.Fail(
                    $"[公开 API 基线] {displayName} 首次生成（{current.Count} 行）：{baselinePath}\n" +
                    "**这一步按设计判失败**——请人工审阅这份公开面（有没有本该 internal 的类型、" +
                    "有没有可变的 public static 字段、protected 是否都在给第三方继承的底座上），" +
                    "确认无误后连同它的 .meta 一起提交，再跑一次不带 -UpdateApiBaseline 的运行取绿灯。");
            }

            ReportDifferences(previous, current, baselinePath, diffPath, displayName,
                "已按更新模式写盘（这一步按设计判失败）");
        }

        #endregion

        #region 差异与报告

        /// <summary>两段差异各写一行清单，然后判失败——<b>检查与更新共用这一条出口</b>，两态的措辞只差开头一句。</summary>
        private static void ReportDifferences(
            SortedSet<string> previous,
            SortedSet<string> current,
            string baselinePath,
            string diffPath,
            string displayName,
            string lead)
        {
            var removed = Difference(previous, current);
            var added = Difference(current, previous);

            if (removed.Count == 0 && added.Count == 0)
            {
                TestContext.WriteLine($"[公开 API 基线] {displayName} 与基线一致（{current.Count} 行）。");
                return;
            }

            WriteFullDiff(diffPath, removed, added);

            var sb = new StringBuilder();
            sb.Append("[公开 API 基线] ").Append(displayName).Append(' ').Append(lead).Append("：")
              .Append(removed.Count + added.Count)
              .Append(" 处变化——「已消失/已变更」").Append(removed.Count).Append(" 处（破坏性），")
              .Append("「新增」").Append(added.Count).Append(" 处（源兼容）。\n\n");
            sb.Append("基线：").Append(baselinePath).Append("（").Append(current.Count).Append(" 行）\n");
            sb.Append("完整差异：").Append(diffPath).Append("\n\n");

            AppendSection(sb, "── 已消失 / 已变更（破坏性·依赖旧写法的第三方会编译不过）", removed);
            AppendSection(sb, "── 新增（源兼容）", added);

            sb.Append("\n─────────────────────────────────────────────────────────────────\n");
            sb.Append("这是一道「公开面不许悄悄变」的锁，不是待办清单。出现差异只有两种处置：\n\n");
            sb.Append("  若**有意**（公开 API 变更 / 破坏性变更）——按顺序做三件事：\n");
            sb.Append("    1) 按 CLAUDE.md「提交拆分与授权」**单独请用户确认**：这两类不属于「计划批准即授权」；\n");
            sb.Append("    2) 在 Assets/XFramework/CHANGELOG.md 的「破坏性变更迁移表」补一行（旧写法 → 新写法 → 说明）；\n");
            sb.Append("       源兼容的新增归入对应的 ### Added / ### Changed。\n");
            sb.Append("    3) 更新基线并复核：pwsh -File Tools/run-tests.ps1 -UpdateApiBaseline\n");
            sb.Append("       更新模式**只写盘、只报差异、永不给绿灯**——绿灯由随后一次不带开关的重跑给出。\n\n");
            sb.Append("  若**无意**：改回代码。不要手工编辑基线——手工改的基线只会让下一次红得更晚。\n");
            sb.Append("列表上限每段 ").Append(MaxReportedPerSection).Append(" 条；完整清单见上面的 .diff 文件。");

            Assert.Fail(sb.ToString());
        }

        private static void AppendSection(StringBuilder sb, string title, List<string> lines)
        {
            sb.Append(title).Append(' ').Append(lines.Count).Append(" 处 ──\n");

            if (lines.Count == 0)
            {
                sb.Append("  （无）\n\n");
                return;
            }

            int shown = Math.Min(lines.Count, MaxReportedPerSection);
            for (int i = 0; i < shown; i++)
                sb.Append("  ").Append(lines[i]).Append('\n');

            if (shown < lines.Count)
                sb.Append("  …（其余 ").Append(lines.Count - shown).Append(" 条见 .diff 文件）\n");

            sb.Append('\n');
        }

        /// <summary>完整差异落盘（含两段全部条目，不截断）。目录由 <c>run-tests.ps1</c> 每次创建，亦已 gitignore。</summary>
        private static void WriteFullDiff(string diffPath, List<string> removed, List<string> added)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(diffPath));

                var sb = new StringBuilder();
                sb.Append("# 公开 API 基线差异（完整清单）\n");
                sb.Append("# 「-」= 已消失/已变更（破坏性）；「+」= 新增（源兼容）\n\n");
                foreach (var line in removed)
                    sb.Append("- ").Append(line).Append('\n');
                foreach (var line in added)
                    sb.Append("+ ").Append(line).Append('\n');

                File.WriteAllText(diffPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                // 差异落盘只是便于查看，写不出来不该把失败消息本身也带走
                TestContext.WriteLine($"[公开 API 基线] 差异文件写入失败（不影响判定）：{e.Message}");
            }
        }

        /// <summary>集合差（排序输出，便于阅读）。手写循环：本仓的全量路径不用 LINQ。</summary>
        private static List<string> Difference(SortedSet<string> left, SortedSet<string> right)
        {
            var result = new List<string>();
            foreach (var line in left)
            {
                if (!right.Contains(line))
                    result.Add(line);
            }
            return result;
        }

        #endregion
    }
}
