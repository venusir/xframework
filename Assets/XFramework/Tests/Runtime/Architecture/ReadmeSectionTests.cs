using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace XFramework.Architecture.Tests
{
    /// <summary>
    /// 模块 README 的「已知限制」小节**存在性守卫**。
    /// <para><b>为什么需要它</b>：该小节此前缺了 5 个模块（Event / Message / Pipeline / Pool / Serialize），
    /// 而这五个恰是第三方最容易踩坑的底层件；补写是一次性的，不守卫就会再缺——与「包 README 四张表」
    /// 当初的处境同形（那四张表实测漏过一整轮，补完才加的守卫）。</para>
    /// <para><b>守法</b>：模块全集取自 <c>Runtime/</c> 的直接子目录（<see cref="SourceScanner.CollectRuntimeModules"/>，
    /// 与 <c>ReadmeTableTests</c> 同一份定义——「模块是什么」只能有一个答案），每个模块的
    /// <c>README.md</c> 必须含一条 <c>##</c> 或 <c>###</c> 级、标题里带「已知限制」的标题行。
    /// **Reactive 以两个集合级小节给出，天然满足，故没有例外表**；确实不需要该小节的模块去
    /// <see cref="SectionExemptions"/> 登记理由——与 <c>FeatureTableExemptions</c> 同一形状，未登记即红。</para>
    /// <para><b>只认 ## 与 ###</b>：<c>####</c> 及更深是条目级标题，不构成模块级的边界声明
    /// （小节埋在别的节里不算数，Reactive 那种两条并列的集合级小节算）。</para>
    /// <para><b>放 PlayMode 而不是 EditMode</b>：本仓的开发回路里，带 <c>-Filter</c>
    /// 且未指定平台时只跑 PlayMode，EditMode 侧的守卫会被这个回路静默跳过（同 <c>ReadmeTableTests</c>、
    /// <c>AutoInitTests</c>）。读 README 只需文件系统路径，不需要 Unity 资产 API。</para>
    /// <para><b>本守卫守不到什么</b>：① **小节里写了什么**——它能证明「有这一节」，证明不了条目属实、完整、
    /// 不陈旧（ReadmeTableTests 的「行内说明文字陈旧」是同一条边界）；② 条目的详略与措辞；③ 其余小节
    /// （设计取舍 / 接口承诺 / 线程契约）不在射程。</para>
    /// </summary>
    [TestFixture]
    public class ReadmeSectionTests
    {
        #region 登记处

        /// <summary>小节标题里必须出现的名字（各模块的实际标题一律是 <c>## 已知限制</c>）。</summary>
        private const string KnownLimitationsHeading = "已知限制";

        /// <summary>
        /// 豁免登记处：刻意不写该小节的模块 → 理由（会进违规消息）。**当前为空**——23 个模块都有该小节；
        /// 将来若某个模块确实无边界可写，来这里登记（连同理由），而不是让守卫红着或把它删掉。
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

                    if (!HasHeading(File.ReadAllLines(path), KnownLimitationsHeading))
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

        #endregion

        #region 工具

        /// <summary>标题行里是否出现关键字：只认 <c>##</c> 与 <c>###</c>（更深的层级是条目级标题）。</summary>
        private static bool HasHeading(string[] lines, string keyword)
        {
            foreach (var line in lines)
            {
                string trimmed = line.TrimStart();
                if (!trimmed.StartsWith("#", StringComparison.Ordinal)) continue;

                int level = 0;
                while (level < trimmed.Length && trimmed[level] == '#') level++;
                if (level < 2 || level > 3) continue;

                if (trimmed.IndexOf(keyword, StringComparison.Ordinal) >= 0) return true;
            }

            return false;
        }

        #endregion
    }
}
