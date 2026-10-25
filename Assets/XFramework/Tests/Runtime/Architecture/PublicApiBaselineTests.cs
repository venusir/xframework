using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace XFramework.Architecture.Tests
{
    /// <summary>
    /// 公开 API 基线守卫：Runtime 程序集的公开面**不许悄悄变**——变了而基线未变即红。
    /// <para><b>它不判断变更该不该做</b>（那是人的事，且按 <c>CLAUDE.md</c> 的「提交拆分与授权」，
    /// 公开 API 变更与破坏性变更**须单独请用户确认**）；它只负责让「变了」这件事不可能滑过去，
    /// 并把「该做什么」写进失败消息——失败消息是唯一一个一定会被人读到的位置。</para>
    /// <para><b>为什么放 Runtime 侧</b>：<c>Workflow.md</c> §二 明写带 <c>-Filter</c> 且未指定平台时
    /// 只跑 PlayMode；放 <c>Tests/Editor/</c> 会被最常见的开发回路静默跳过（与 <c>AutoInitTests</c> /
    /// <c>NamingConventionTests</c> 同一条教训）。</para>
    /// <para><b>为什么不查 Editor 程序集</b>：Runtime 测试程序集不引用它（asmdef 实测），看不到就查不了；
    /// 要查必须另写一个 EditMode fixture。本轮范围裁定为「只查 Runtime」——它占公开面的绝大部分
    /// （具体类型/成员数以 <c>PublicApiBaseline.txt</c> 为准；不在此写固定数，避免每次公开面增长都要改注释）。</para>
    /// <para><b>为什么基线可以被反写、以及反写的入口只有一个</b>：这是本仓首次出现「测试在显式开关下
    /// 写仓库文件」。开关是环境变量 <c>XFW_UPDATE_API_BASELINE</c>（只认 <c>"1"</c>），只由
    /// <c>pwsh -File Tools/run-tests.ps1 -UpdateApiBaseline</c> 设置；更新模式**写完后一律判失败**并打印
    /// 相对旧基线的差异——绿灯只能由随后一次不带开关的重跑给出。理由：若更新模式写完就绿，唯一的依靠是
    /// 「人记得 <c>git diff</c>」，而这道守卫存在的理由恰恰是「人会忘」。</para>
    /// <para><b>为什么基线缺失即失败</b>：能静默重建的守卫等于没有守卫——首次生成那一次「人来审」不可省。</para>
    /// <para><b>没有 SetUp/TearDown 是有意的</b>（照 <c>LogFacadeCompletenessTests</c> 先例）：纯反射，
    /// 不 Configure、不 Shutdown、不写任何静态字段，故没有需要复位的东西。</para>
    /// </summary>
    [TestFixture]
    public class PublicApiBaselineTests
    {
        #region Constants

        /// <summary>更新模式的唯一入口。<b>只认 <c>"1"</c></b>：写仓库文件是危险动作，
        /// <c>=0</c> 这种「看起来是关」的写法绝不能开启它。</summary>
        private const string UpdateEnvVar = "XFW_UPDATE_API_BASELINE";

        /// <summary>射程内的程序集——断言一次，防将来有人顺手换成 Editor 程序集。</summary>
        private const string ExpectedAssemblyName = "Venusy609.Xframework";

        /// <summary>失败消息里每段最多列多少条（一次命名空间迁移可能是几百行，全文灌进控制台没人看）。</summary>
        private const int MaxReportedPerSection = 50;

        /// <summary>
        /// 基线文件。用 <see cref="SourceScanner.PackageRoot"/>——它是「测试壳的 Assets 是 junction、
        /// 故 <c>Application.dataPath/XFramework</c> 即真包根」这条推论的唯一定义处。
        /// </summary>
        private static string BaselinePath => Path.Combine(
            SourceScanner.PackageRoot, "Tests", "Runtime", "Architecture", "PublicApiBaseline.txt");

        /// <summary>完整差异的落盘位置（测试运行目录，已被 .gitignore 忽略）。</summary>
        private static string DiffPath => Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "TestResults", "PublicApiBaseline.diff"));

        #endregion

        #region Test

        [Test]
        public void PublicApi_MatchesBaseline()
        {
            Assembly assembly = typeof(XFramework.XLog.LogManager).Assembly;
            Assert.AreEqual(ExpectedAssemblyName, assembly.GetName().Name,
                "本 fixture 只守 Runtime 程序集的公开面——Editor 程序集要么换程序集、要么另写 EditMode fixture");

            SortedSet<string> current = PublicApiSnapshot.Capture(assembly);

            if (Environment.GetEnvironmentVariable(UpdateEnvVar) == "1")
            {
                RunUpdateMode(assembly, current);
                return;
            }

            Assert.IsTrue(File.Exists(BaselinePath),
                $"[公开 API 基线] 基线文件不存在：{BaselinePath}\n" +
                "基线缺失按失败处理——**不静默重建**（能静默重建的守卫等于没有守卫，首次生成那一次" +
                "「人来审」是不可省的一步）。\n" +
                "首次生成请显式运行：pwsh -File Tools/run-tests.ps1 -UpdateApiBaseline\n" +
                "生成后审阅内容，连同它的 .meta 一起提交；`git status --short` 核一眼未跟踪文件。");

            string text = File.ReadAllText(BaselinePath);
            Assert.IsNotEmpty(PublicApiSnapshot.Parse(text),
                $"[公开 API 基线] 基线文件为空：{BaselinePath}\n" +
                "空文件与「渲染器坏了、什么都没渲染出来」无法区分，故按失败处理。\n" +
                "删掉它并显式重建：pwsh -File Tools/run-tests.ps1 -UpdateApiBaseline");

            ReportDifferences(PublicApiSnapshot.Parse(text), current, BaselinePath, "与基线不一致");
        }

        #endregion

        #region 更新模式

        /// <summary>
        /// 更新模式：写盘 → **一律判失败**（内容与旧基线一致时除外）。
        /// <para><b>不做临时文件 + 原子替换</b>：写坏了，下一次运行必然与刚渲染的内容不一致 → 红；
        /// 而写盘只可能发生在显式开关下，且更新模式必然紧跟一次不带开关的复核跑——多一段代码换一个
        /// 已被流程覆盖的边界，不划算。</para>
        /// </summary>
        private static void RunUpdateMode(Assembly assembly, SortedSet<string> current)
        {
            string rendered = PublicApiSnapshot.Render(assembly);
            SortedSet<string> previous = File.Exists(BaselinePath)
                ? PublicApiSnapshot.Parse(File.ReadAllText(BaselinePath))
                : null;

            if (previous != null && previous.SetEquals(current))
            {
                TestContext.WriteLine($"[公开 API 基线] 更新模式：内容与旧基线一致（{current.Count} 行），未写盘。");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(BaselinePath));
            File.WriteAllText(BaselinePath, rendered, new UTF8Encoding(false));

            if (previous == null)
            {
                Assert.Fail(
                    $"[公开 API 基线] 首次生成（{current.Count} 行）：{BaselinePath}\n" +
                    "**这一步按设计判失败**——请人工审阅这份公开面（有没有本该 internal 的类型、" +
                    "有没有可变的 public static 字段、protected 是否都在给第三方继承的底座上），" +
                    "确认无误后连同它的 .meta 一起提交，再跑一次不带 -UpdateApiBaseline 的运行取绿灯。");
            }

            ReportDifferences(previous, current, BaselinePath, "已按更新模式写盘（这一步按设计判失败）");
        }

        #endregion

        #region 差异与报告

        /// <summary>两段差异各写一行清单，然后判失败——<b>检查与更新共用这一条出口</b>，两态的措辞只差开头一句。</summary>
        private static void ReportDifferences(SortedSet<string> previous, SortedSet<string> current,
            string baselinePath, string lead)
        {
            var removed = Difference(previous, current);
            var added = Difference(current, previous);

            if (removed.Count == 0 && added.Count == 0)
            {
                TestContext.WriteLine($"[公开 API 基线] 与基线一致（{current.Count} 行）。");
                return;
            }

            WriteFullDiff(removed, added);

            var sb = new StringBuilder();
            sb.Append("[公开 API 基线] ").Append(lead).Append("：").Append(removed.Count + added.Count)
              .Append(" 处变化——「已消失/已变更」").Append(removed.Count).Append(" 处（破坏性），")
              .Append("「新增」").Append(added.Count).Append(" 处（源兼容）。\n\n");
            sb.Append("基线：").Append(baselinePath).Append("（").Append(current.Count).Append(" 行）\n");
            sb.Append("完整差异：").Append(DiffPath).Append("\n\n");

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
        private static void WriteFullDiff(List<string> removed, List<string> added)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DiffPath));

                var sb = new StringBuilder();
                sb.Append("# 公开 API 基线差异（完整清单）\n");
                sb.Append("# 「-」= 已消失/已变更（破坏性）；「+」= 新增（源兼容）\n\n");
                foreach (var line in removed)
                    sb.Append("- ").Append(line).Append('\n');
                foreach (var line in added)
                    sb.Append("+ ").Append(line).Append('\n');

                File.WriteAllText(DiffPath, sb.ToString(), new UTF8Encoding(false));
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
