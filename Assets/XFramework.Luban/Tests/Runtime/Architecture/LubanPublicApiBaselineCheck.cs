using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;

namespace XFramework.XConfig.Luban.Tests
{
    /// <summary>
    /// Luban 扩展包的公开 API 基线检查器（Runtime / Editor 两条 fixture 共用）。
    /// <para>与核心 <c>PublicApiBaselineCheck</c> 保持同一套语义：**行集合**比较、基线缺失即失败、
    /// 更新模式写盘后按设计判失败。扩展包不能引用核心测试程序集，故这里是一份紧凑副本——
    /// 差异只在失败消息与「不落 `.diff` 文件」（扩展包公开面小，控制台输出足够）。</para>
    /// </summary>
    internal static class LubanPublicApiBaselineCheck
    {
        #region Constants

        internal const string UpdateEnvVar = "XFW_UPDATE_LUBAN_API_BASELINE";

        private const int MaxReportedPerSection = 50;

        #endregion

        #region Verify

        internal static void Verify(
            Assembly assembly,
            string baselinePath,
            string header,
            string displayName)
        {
            SortedSet<string> current = LubanPublicApiSnapshot.Capture(assembly);

            if (Environment.GetEnvironmentVariable(UpdateEnvVar) == "1")
            {
                RunUpdateMode(assembly, current, baselinePath, header, displayName);
                return;
            }

            Assert.IsTrue(File.Exists(baselinePath),
                $"[Luban 公开 API 基线] {displayName} 基线文件不存在：{baselinePath}\n" +
                "基线缺失按失败处理——不静默重建。首次生成：pwsh -File Tools/run-tests.ps1 -UpdateLubanApiBaseline");

            SortedSet<string> previous = Parse(File.ReadAllText(baselinePath));
            Assert.IsNotEmpty(previous,
                $"[Luban 公开 API 基线] {displayName} 基线文件为空：{baselinePath}\n" +
                "空文件与「渲染器坏了」无法区分，按失败处理。删掉它并显式重建。");

            ReportDifferences(previous, current, baselinePath, displayName, "与基线不一致");
        }

        #endregion

        #region 更新模式

        private static void RunUpdateMode(
            Assembly assembly,
            SortedSet<string> current,
            string baselinePath,
            string header,
            string displayName)
        {
            SortedSet<string> previous = File.Exists(baselinePath)
                ? Parse(File.ReadAllText(baselinePath))
                : null;

            if (previous != null && previous.SetEquals(current))
            {
                TestContext.WriteLine(
                    $"[Luban 公开 API 基线] {displayName} 更新模式：内容与旧基线一致（{current.Count} 行），未写盘。");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath));
            File.WriteAllText(baselinePath, LubanPublicApiSnapshot.Render(assembly, header), new UTF8Encoding(false));

            Assert.Fail(
                $"[Luban 公开 API 基线] {displayName} 已重写 {baselinePath}（{current.Count} 行）——" +
                "**这一步按设计判失败**。请审阅公开面变化，确认无误后连同 .meta 一起提交，" +
                "再跑一次不带 -UpdateLubanApiBaseline 的运行取绿灯。");
        }

        #endregion

        #region 解析与差异

        private static SortedSet<string> Parse(string text)
        {
            var lines = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#')
                    continue;
                lines.Add(line);
            }

            return lines;
        }

        private static void ReportDifferences(
            SortedSet<string> previous,
            SortedSet<string> current,
            string baselinePath,
            string displayName,
            string lead)
        {
            var removed = Difference(previous, current);
            var added = Difference(current, previous);

            if (removed.Count == 0 && added.Count == 0)
            {
                TestContext.WriteLine($"[Luban 公开 API 基线] {displayName} 与基线一致（{current.Count} 行）。");
                return;
            }

            var sb = new StringBuilder();
            sb.Append("[Luban 公开 API 基线] ").Append(displayName).Append(' ').Append(lead).Append("：")
              .Append(removed.Count + added.Count)
              .Append(" 处变化——「已消失/已变更」").Append(removed.Count).Append(" 处（破坏性），")
              .Append("「新增」").Append(added.Count).Append(" 处（源兼容）。\n\n");
            sb.Append("基线：").Append(baselinePath).Append("（").Append(current.Count).Append(" 行）\n\n");

            AppendSection(sb, "── 已消失 / 已变更（破坏性）", removed);
            AppendSection(sb, "── 新增（源兼容）", added);

            sb.Append("更新：pwsh -File Tools/run-tests.ps1 -UpdateLubanApiBaseline（写盘后按设计判失败）。");

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
                sb.Append("  …（其余 ").Append(lines.Count - shown).Append(" 条未列出）\n");

            sb.Append('\n');
        }

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
