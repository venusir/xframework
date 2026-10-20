using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using XFramework.XLog;

namespace XFramework.Architecture.Tests
{
    /// <summary>
    /// 内置日志分类守卫：Runtime 生产代码里的 <c>LogCategory.Get("字面量")</c> 必须已在
    /// <see cref="LogCategories"/> 中登记。规则正文见 <see cref="LogCategories"/> 的类注释——
    /// 「新增框架日志时来这里加一行，不要在调用点裸写字符串」。
    /// <para>
    /// <b>为什么需要它</b>：<c>UIViewBase</c> / <c>ViewModelBase</c> 曾长期裸写在自己文件里，
    /// 绕过内置分类表——第三方想按分类静音时，发现入口里根本没有它们。这条守卫把「靠人眼核对」
    /// 变成每次全量门禁都会跑的自查。
    /// </para>
    /// <para>
    /// <b>拦不住什么</b>（照 <c>ModuleBoundaryTests</c> 的同一口径）：① 只扫 <c>Runtime/</c>，
    /// 不扫 Editor / Samples / Tests；② 只认**字符串字面量**——Pool 的泛型分类
    /// （<c>$"Pool&lt;{typeof(T).Name}&gt;"</c>）与 Unity 捕获路径的 <c>condition.Substring(...)</c>
    /// 是运行时算的，不参与；③ 注释里的提及不在射程内（<c>ReadCodeOnly</c> 会剥整行注释）。
    /// </para>
    /// <para>
    /// <b>放 PlayMode 而不是 EditMode</b>：与同目录的 <c>NamingConventionTests</c> 同理——
    /// 带 <c>-Filter</c> 且未指定平台时只跑 PlayMode，放 EditMode 会被开发回路静默跳过。
    /// </para>
    /// </summary>
    [TestFixture]
    public class LogCategoryConventionTests
    {
        /// <summary>匹配 <c>LogCategory.Get("名字")</c>；插值与非字面量参数不会命中。</summary>
        private static readonly Regex LiteralGet =
            new Regex(@"LogCategory\s*\.\s*Get\s*\(\s*""([^""]*)""\s*\)", RegexOptions.Multiline);

        [Test]
        public void RuntimeLiteralCategories_AreDeclaredInLogCategories()
        {
            string runtimeRoot = Path.Combine(SourceScanner.PackageRoot, "Runtime");
            Assert.IsTrue(Directory.Exists(runtimeRoot),
                $"找不到 Runtime 目录：{runtimeRoot}（测试壳的 Assets 是否为 junction?）");

            HashSet<string> declared = CollectDeclaredNames();
            string self = Path.GetFullPath(Path.Combine(runtimeRoot, "Log", "LogCategories.cs"));
            var violations = new List<string>();

            foreach (string file in SourceScanner.Sorted(
                Directory.GetFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories)))
            {
                if (string.Equals(Path.GetFullPath(file), self, StringComparison.OrdinalIgnoreCase))
                    continue;

                string code = SourceScanner.ReadCodeOnly(file);
                foreach (Match match in LiteralGet.Matches(code))
                {
                    string name = match.Groups[1].Value;
                    if (declared.Contains(name))
                        continue;

                    violations.Add(
                        $"{SourceScanner.RelativePath(file)}:{LineOf(code, match.Index)}  `{name}` 未在 LogCategories 声明" +
                        "  ←  请到 Runtime/Log/LogCategories.cs 加一行，不要在调用点裸写字符串");
                }
            }

            Assert.IsEmpty(violations,
                "以下位置使用了未登记的内置日志分类。规则：框架运行时的分类必须在 LogCategories 中" +
                "有对应字段；第三方自定义分类用 LogCategory.Get 并自行管理（不在本守卫射程内）：\n  " +
                string.Join("\n  ", violations));
        }

        /// <summary>从 <see cref="LogCategories"/> 的公开静态字段收集合法分类名。</summary>
        private static HashSet<string> CollectDeclaredNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (FieldInfo field in typeof(LogCategories).GetFields(
                BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(LogCategory))
                    continue;

                var category = (LogCategory)field.GetValue(null);
                names.Add(category.Name);
            }

            return names;
        }

        /// <summary>把字符下标换算成 1 起的行号（匹配可能跨行）。</summary>
        private static int LineOf(string text, int index)
        {
            int line = 1;
            for (int i = 0; i < index && i < text.Length; i++)
            {
                if (text[i] == '\n')
                    line++;
            }

            return line;
        }
    }
}
