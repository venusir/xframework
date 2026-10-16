using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace XFramework.Architecture.Tests
{
    /// <summary>
    /// 模块 README 的**必答节存在性守卫**（骨架门禁）。
    /// <para><b>为什么需要它</b>：普查 23 份 README 得到一组对照——唯一被守卫锁死的「已知限制」是 23/23 齐，
    /// 而完全没守卫的「线程契约」有 11 家一个字都没写。**没守卫的约定必然腐烂**，这是仓内自己的数据，
    /// 不是推测。同批还有同一件事的多种写法（`快速使用` 14 家 vs `快速开始` 6 家）与 78 个只出现一次的
    /// `##` 标题，于是先立结构规范（让使用方按同一条路径找东西），再把「必答」那一半机械化。</para>
    /// <para><b>守法</b>：模块全集取自 <c>Runtime/</c> 的直接子目录
    /// （<see cref="SourceScanner.CollectRuntimeModules"/>，与 <c>ReadmeTableTests</c> 同一份定义——
    /// 「模块是什么」只能有一个答案）。**五节必答**由 <see cref="RequiredHeadings"/> 声明；其余按需节不在射程。</para>
    /// <para><b>过渡期用登记表</b>：<see cref="RestructuredModules"/> 逐个登记已重整的模块——为什么不是
    /// 「等 23/23 齐了再一次开闸」，见该字段的注释。登记表当前为空，故此刻对**全部**模块生效的仍只有
    /// 「已知限制」这一节（<see cref="EveryRuntimeModuleReadme_HasKnownLimitationsSection"/>）。</para>
    /// <para><b>只认 ## 与 ###</b>：<c>####</c> 及更深是条目级标题，不构成模块级的边界声明；小节埋在
    /// 别的节底下不算数——Diagnostics 的「线程契约」曾埋在 `## 契约` 下，要提升为 `##`。Reactive 那种
    /// 两条并列的集合级 `### 已知限制` 算（层级上限按节声明，见 <see cref="RequiredHeadings"/>）。</para>
    /// <para><b>放 PlayMode 而不是 EditMode</b>：本仓的开发回路里，带 <c>-Filter</c>
    /// 且未指定平台时只跑 PlayMode，EditMode 侧的守卫会被这个回路静默跳过（同 <c>ReadmeTableTests</c>、
    /// <c>AutoInitTests</c>）。读 README 只需文件系统路径，不需要 Unity 资产 API。</para>
    /// <para><b>本守卫守不到什么</b>：① **节里写了什么**——它能证明「有这一节」，证明不了条目属实、完整、
    /// 不陈旧（<c>ReadmeTableTests</c> 的「行内说明文字陈旧」是同一条边界）；② 必答节的最短合法形态是
    /// 「一句话」，写「见源码」也能过；③ 节的顺序；④ 其余按需节（设计取舍 / 接口承诺 / 行为契约 /
    /// 架构设计 / API 参考…）；⑤ `## 模块结构` 只守存在，**不守它列的文件名对不对**——子集关系在
    /// `Tools/check-docs.ps1` 的报告项里，且只作诊断。**守卫是「有没有」，评审才是「对不对」。**</para>
    /// </summary>
    [TestFixture]
    public class ReadmeSectionTests
    {
        #region 登记处

        /// <summary>「已知限制」的节名（<see cref="RequiredHeadings"/> 与全量用例共用一份字面量）。</summary>
        private const string KnownLimitationsHeading = "已知限制";

        /// <summary>
        /// 必答节表：节名 / 允许的最低与最高标题层级 / 建节指引（会进违规消息，故写成**自足表述**——
        /// 包内不得指向包外的维护向文档，见 `PackageBoundaryTests`）。
        /// <para>层级用两个数而不是一个：「已知限制」允许 <c>###</c>（Reactive 以两条集合级小节给出，
        /// 这一形态自 2026-10-13 起就在守卫的语义里，不得回归）；其余四节要求独立成 <c>##</c>。</para>
        /// <para>四节**不设豁免**：每节的最短合法形态都是一句话、一个三行文件树，任何模块都写得出来；
        /// 给了豁免口子才是假绿的来源（登记理由这件事本身不可机械核验）。「已知限制」保留
        /// <see cref="SectionExemptions"/>。</para>
        /// </summary>
        private static readonly (string Name, int MinLevel, int MaxLevel, string Guide)[] RequiredHeadings =
        {
            ("概述", 2, 2, "一段话 + 命名空间：它是什么、给谁用、与什么容易混淆"),
            ("快速使用", 2, 2, "最小可跑通示例；编号步骤（### 1. / ### 2.）作为子节收在下面；" +
                             "素材在该模块的维护向记录里"),
            ("模块结构", 2, 2, "一个 fenced 文件树：使用方会直接接触的类型所在文件与目录分组，" +
                             "不是全量文件清单"),
            ("线程契约", 2, 2, "最短合法形态是一句话，例如「全部 API 仅在主线程调用」"),
            (KnownLimitationsHeading, 2, 3, "条目式；素材在该模块的维护向记录里"),
        };

        /// <summary>
        /// **已重整模块登记表**——守卫只对表内模块要求五节齐全。
        /// <para><b>为什么不是「全量重整完再把守卫开起来」</b>：仓内「先诊断看曲线、清完存量再纳门」的
        /// 先例（XML 告警码）成立的前提是**清存量在一个提交里完成**；这里是 23 个模块逐次增量推进，
        /// 前提不成立——那会是 23 个提交零机械验证，且没有东西阻止后一个模块的改动把前一个已重整的
        /// 模块结构拆掉。改成登记表后：**「把模块加进本表」与「把它的 README 改到位」必须同一个提交
        /// 完成**，于是每个提交自身是绿的、且自带验证，窗口期为零。</para>
        /// <para><b>退休时点</b>：全量重整完成的那一次提交把本表的用法翻成「作用于全部模块」
        /// （届时它整表删除——终态不留第二份模块清单）。</para>
        /// </summary>
        private static readonly string[] RestructuredModules =
        {
        };

        /// <summary>
        /// 豁免登记处：刻意不写「已知限制」的模块 → 理由（会进违规消息）。**当前为空**——23 个模块都有
        /// 该小节；将来若某个模块确实无边界可写，来这里登记（连同理由），而不是让守卫红着或把它删掉。
        /// </summary>
        private static readonly (string Module, string Reason)[] SectionExemptions =
        {
        };

        #endregion

        #region 用例

        [Test]
        public void EveryRuntimeModuleReadme_HasKnownLimitationsSection()
        {
            var violations = new List<string>();
            var modules = SourceScanner.CollectRuntimeModules(violations);

            var exempt = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in SectionExemptions)
            {
                if (!exempt.Add(entry.Module))
                {
                    violations.Add($"SectionExemptions 重复登记模块 {entry.Module}");
                }
            }

            if (modules != null)
            {
                foreach (var module in SourceScanner.Sorted(modules))
                {
                    if (exempt.Contains(module)) continue;

                    string path = Path.Combine(SourceScanner.PackageRoot, "Runtime", module, "README.md");
                    if (!File.Exists(path))
                    {
                        violations.Add($"Runtime/{module}/ 下没有 README.md——每个模块都要有使用方文档" +
                                       "（模块索引表也按这一条守链接），补文档或从 Runtime/ 移走整个模块目录");
                        continue;
                    }

                    if (!HasHeading(File.ReadAllLines(path), KnownLimitationsHeading, 2, 3))
                    {
                        violations.Add($"Runtime/{module}/README.md 没有「{KnownLimitationsHeading}」小节——" +
                                       "使用方判断「这模块能不能用在我的场景」靠的就是这一节，缺了它只能靠读源码发现边界。" +
                                       "两条路：① 补一节（开场白与条目形态照 Config / Data / File / Save 的既有写法，" +
                                       "素材在该模块的维护向记录里）；② 确实无边界可写就去 " +
                                       "ReadmeSectionTests.SectionExemptions 登记理由");
                    }
                }

                foreach (var entry in SectionExemptions)
                {
                    if (!modules.Contains(entry.Module))
                    {
                        violations.Add($"SectionExemptions 登记了 {entry.Module}，但 Runtime/ 下没有该目录" +
                                       "——模块改名/删除后请同步登记处");
                    }
                }
            }

            Assert.IsEmpty(violations,
                "模块 README 的「已知限制」小节缺失，或豁免登记处与 Runtime 目录对不上：\n  " +
                string.Join("\n  ", violations));
        }

        [Test]
        public void EveryRestructuredModuleReadme_HasRequiredSections()
        {
            var violations = new List<string>();
            var modules = SourceScanner.CollectRuntimeModules(violations);

            var registered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var module in RestructuredModules)
            {
                if (!registered.Add(module))
                {
                    violations.Add($"RestructuredModules 重复登记模块 {module}");
                }
            }

            if (modules != null)
            {
                foreach (var module in SourceScanner.Sorted(RestructuredModules))
                {
                    if (!modules.Contains(module))
                    {
                        violations.Add($"RestructuredModules 登记了 {module}，但 Runtime/ 下没有该目录" +
                                       "——模块改名/删除后请同步登记处");
                        continue;
                    }

                    string path = Path.Combine(SourceScanner.PackageRoot, "Runtime", module, "README.md");
                    if (!File.Exists(path))
                    {
                        violations.Add($"RestructuredModules 登记了 {module}，但 Runtime/{module}/README.md 不存在" +
                                       "——登记表跟着模块改名/删除一起走");
                        continue;
                    }

                    var lines = File.ReadAllLines(path);
                    foreach (var required in RequiredHeadings)
                    {
                        if (HasHeading(lines, required.Name, required.MinLevel, required.MaxLevel)) continue;

                        string levelText = required.MinLevel == required.MaxLevel
                            ? $"第 {required.MinLevel} 级"
                            : $"第 {required.MinLevel}~{required.MaxLevel} 级";
                        violations.Add($"Runtime/{module}/README.md 没有「{required.Name}」小节" +
                                       $"（须是{levelText}标题——埋在别的节底下的不算）：{required.Guide}");
                    }
                }
            }

            Assert.IsEmpty(violations,
                "已登记的模块 README 缺必答小节，或登记表与 Runtime 目录对不上：\n  " +
                string.Join("\n  ", violations));
        }

        #endregion

        #region 工具

        /// <summary>
        /// 标题行里是否出现关键字：只认 <c>#</c> 开头、层级落在 [<paramref name="minLevel"/>,
        /// <paramref name="maxLevel"/>] 的行，关键字按<b>子串</b>匹配（故「快速使用：写一个自己的页签」
        /// 这类带限定词的写法天然通过）。语义与加层级参数前一致，只是把上限变成了参数。
        /// </summary>
        private static bool HasHeading(string[] lines, string keyword, int minLevel, int maxLevel)
        {
            foreach (var line in lines)
            {
                string trimmed = line.TrimStart();
                if (!trimmed.StartsWith("#", StringComparison.Ordinal)) continue;

                int level = 0;
                while (level < trimmed.Length && trimmed[level] == '#') level++;
                if (level < minLevel || level > maxLevel) continue;

                if (trimmed.IndexOf(keyword, StringComparison.Ordinal) >= 0) return true;
            }

            return false;
        }

        #endregion
    }
}
