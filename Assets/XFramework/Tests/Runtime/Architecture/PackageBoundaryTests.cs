using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace XFramework.Architecture.Tests
{
    /// <summary>
    /// 「**包内不指向包外**」的机械守卫：包内 `.cs` 的任何文本（**含注释与字符串**）
    /// 不得出现 `Documentation/`——它在仓库根、不随包发布，第三方装了包打不开它。
    /// <para><b>为什么需要它</b>：这条规则此前只写在 `CLAUDE.md` 的「文档分层」里、且只声明管 README，
    /// 于是**源码注释**这一路长期无人管：2026-10-15 两轮清出 22 处（1 处 README + 7 处引用当时将删的
    /// 评估报告 + 15 处引用其余维护向文档），其中 **2 处落在 Runtime 生产代码的 XML 注释里**——
    /// 那些注释随包发布，第三方在 IDE 里直接读到死指针。手工发现 22 次之后，这条该由机器来拦。</para>
    /// <para><b>守法</b>：扫 <see cref="SourceScanner.EnumerateSources"/> 的全部 `.cs`（含 `Samples~`——
    /// 它虽不被 Unity 编译，但仍在包内），原文出现 `Documentation/` 即记违规。
    /// **扫原文而不是 <see cref="SourceScanner.ReadCodeOnly"/> 剥注释后的文本**：要抓的恰恰是注释与
    /// 消息串里的指路，剥掉注释等于把射程裁成零。</para>
    /// <para><b>为什么本文件自己不算违规</b>：守法的描述里必须出现那个字符串（否则没法说明在查什么），
    /// 故 <see cref="SelfExemption"/> 把自己排除——这是豁免登记处里的唯一常驻成员，理由写在常量上。</para>
    /// <para><b>放 PlayMode 而不是 EditMode</b>：本仓的开发回路里，带 <c>-Filter</c>
    /// 且未指定平台时只跑 PlayMode，EditMode 侧的守卫会被这个回路静默跳过（同 <c>ReadmeTableTests</c>、
    /// <c>ReadmeSectionTests</c>、<c>NamingConventionTests</c>）。读文本只需文件系统路径。</para>
    /// <para><b>本守卫守不到什么</b>：① 它只认 `Documentation/` 这一个记号——写成别的形式指路
    /// （「见仓库的维护向文档」）它看不见；② 它不判「这条自足表述是否把结论写对了」，只判「有没有指路」。</para>
    /// </summary>
    [TestFixture]
    public class PackageBoundaryTests
    {
        #region 登记处

        /// <summary>被查的记号：包根之外那份维护向文档目录的路径片段。</summary>
        private const string RepoDocsMarker = "Documentation/";

        /// <summary>
        /// 本文件自己的相对路径（守法描述里必须写出记号，故排除自身）。
        /// </summary>
        private const string SelfExemption = "Tests/Runtime/Architecture/PackageBoundaryTests.cs";

        /// <summary>
        /// 豁免登记处：包内确实需要出现该记号的 `.cs`（相对路径 → 理由，会进违规消息）。
        /// **当前为空**——将来若某个文件非写它不可（例如又一份守卫的守法描述），来这里登记理由，
        /// 而不是让守卫红着或把它删掉。
        /// </summary>
        private static readonly (string File, string Reason)[] FileExemptions =
        {
        };

        #endregion

        #region 用例

        [Test]
        public void PackageSources_DoNotReferenceRepoDocumentation()
        {
            var violations = new List<string>();
            var exempt = new HashSet<string>(StringComparer.Ordinal) { SelfExemption };

            foreach (var entry in FileExemptions)
            {
                if (!exempt.Add(entry.File))
                {
                    violations.Add($"FileExemptions 重复登记 {entry.File}");
                }
            }

            foreach (var file in SourceScanner.EnumerateSources())
            {
                string relative = SourceScanner.RelativePath(file);
                if (exempt.Contains(relative)) continue;

                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].IndexOf(RepoDocsMarker, StringComparison.Ordinal) < 0) continue;

                    violations.Add($"{relative}:{i + 1} 出现「{RepoDocsMarker}」——包内不指向包外" +
                                   "（第三方装了包打不开 `Documentation/`）。改成**自足表述**：把结论内联，" +
                                   "不给出处路径；确实非指不可就去 PackageBoundaryTests.FileExemptions 登记理由");
                }
            }

            Assert.IsEmpty(violations,
                "包内源码指向了包外的维护向文档（`Documentation/` 不随包发布）：\n  " +
                string.Join("\n  ", violations));
        }

        #endregion
    }
}
