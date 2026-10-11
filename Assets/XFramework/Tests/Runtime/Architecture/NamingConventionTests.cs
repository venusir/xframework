using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace XFramework.Architecture.Tests
{
    /// <summary>
    /// 缩写大小写守卫：**两字母缩写必须全大写**（`UI` / `IO`，不写 `Ui` / `Io`）。规则正文见
    /// `CLAUDE.md` 的「编码规范 → 缩写大小写」。
    /// <para>
    /// <b>为什么需要它</b>：这条约定此前是**空白**，评审没有判据可依，于是
    /// <c>IUiHudProvider</c>（同命名空间全是 <c>IUIManager</c> / <c>IUITipProvider</c>）与
    /// <c>UiDiagnosticPanel</c>（同级 20 个面板全是 <c>XxxDiagnosticPanel</c>）跟周围不一致长了很久，
    /// 直到 2026-10-11 一次性统一。同那轮的 <c>ModuleBoundaryTests</c> 一样，把「靠人眼发现」
    /// 变成每次全量门禁都会跑的自查。
    /// </para>
    /// <para>
    /// <b>放 PlayMode 而不是 EditMode</b>：<c>Documentation/Workflow.md</c> §二 明写带 <c>-Filter</c>
    /// 且未指定平台时只跑 PlayMode，EditMode 侧的守卫会被最常见的开发回路静默跳过——这条教训来自
    /// 同目录的 <c>AutoInitTests</c>。本用例纯扫源码文本、不需要播放模式，放这里**纯粹是为了不被跳过**。
    /// </para>
    /// <para>
    /// <b>拦不住什么</b>（照 <c>ModuleBoundaryTests</c> 交代射程的同一口径）：
    /// ① 只管 <c>.cs</c>，<b>不管 <c>.md</c></b>——包内 README 与 CHANGELOG 里的拼写仍靠评审
    /// （CHANGELOG 里提到旧名字是**有意保留的历史**，本就不该拦）；
    /// ② 只管 <c>Ui</c> / <c>Io</c> 这两种两字母缩写，别的缩写没有写进规则就不拦；
    /// ③ 扫描前剥掉了注释，故**文档注释里写错的缩写不在射程内**——`<see cref="IUiXxx"/>` 漏网时
    /// 只能靠编译器的 CS1574（该类型不存在）或人眼。
    /// </para>
    /// </summary>
    [TestFixture]
    public class NamingConventionTests
    {
        #region 源码扫描

        /// <summary>
        /// 两字母缩写的错误写法：<c>Ui</c> / <c>Io</c> 之后<b>不接小写字母</b>。
        /// <para><b>为什么不用 <c>\bUi[A-Z]</c></b>：<c>IUiHud</c> 里的 <c>Ui</c> 前面是字母 <c>I</c>，
        /// <c>\b</c> 匹配不上——而本次要抓的正是这一种。也不用「后接大写」作唯一条件：
        /// <c>AudioChannels.Ui</c> 这种**独立出现**的同样要抓。</para>
        /// <para>用 <c>(?![a-z])</c> 而不是 <c>(?![A-Za-z])</c>：前者能抓到 <c>BuildUi</c> 这类结尾形态，
        /// 而 <c>Uint</c> / <c>Ios</c> 这种后接小写字母的正常词不会被误伤。</para>
        /// <para><b>为什么写成 <c>[U]i</c> 这种怪样子</b>：本守卫会扫到自己，而模式的字面量里就含
        /// 它要找的那两个字母——直接写出来，它会在自己的定义处命中自己（实测红过一次）。
        /// <c>[U]i</c> 与它要匹配的 <c>Ui</c> 完全等价，但**字面量本身不再是连续的 <c>Ui</c>**，
        /// 于是自洽。**刻意不给本文件开豁免**——豁免是把守卫挖出一个洞，
        /// 而这个写法只是让定义与规则不冲突。</para>
        /// </summary>
        private static readonly Regex WrongAcronymCase =
            new Regex(@"(?:[U]i|[I]o)(?![a-z])", RegexOptions.Multiline);

        [Test]
        public void TwoLetterAcronyms_AreAllCaps()
        {
            var packageRoot = Path.Combine(Application.dataPath, "XFramework");
            Assert.IsTrue(Directory.Exists(packageRoot),
                $"找不到包目录：{packageRoot}（测试壳的 Assets 是否为 junction?）");

            var violations = new List<string>();

            foreach (var file in Directory.GetFiles(packageRoot, "*.cs", SearchOption.AllDirectories))
            {
                var relative = file.Substring(packageRoot.Length + 1).Replace('\\', '/');
                var lines = ReadCodeOnly(file).Split('\n');

                for (int i = 0; i < lines.Length; i++)
                {
                    var match = WrongAcronymCase.Match(lines[i]);
                    if (!match.Success)
                        continue;

                    violations.Add($"{relative}:{i + 1}  `{match.Value}` 应为 `{match.Value.ToUpperInvariant()}`" +
                        $"  ←  {lines[i].Trim()}");
                }
            }

            Assert.IsEmpty(violations,
                "以下位置用了两字母缩写的错误大小写。规则：两字母缩写全大写（`UI` / `IO`），" +
                "三字母及以上按单词（`Bgm` / `Json`）。改名时注意同步改文件名与 .meta：\n  " +
                string.Join("\n  ", violations));
        }

        /// <summary>
        /// 读出「只有代码」的文本：注释行按原行数置空，故行号仍与源文件一致。
        /// <para>与 <c>ModuleBoundaryTests.ReadCodeOnly</c> 同形（两处副本；若出现第三处就该抽出来了）。</para>
        /// </summary>
        private static string ReadCodeOnly(string file)
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                    trimmed.StartsWith("*", StringComparison.Ordinal) ||
                    trimmed.StartsWith("/*", StringComparison.Ordinal))
                {
                    lines[i] = string.Empty;
                }
            }
            return string.Join("\n", lines);
        }

        #endregion
    }
}
