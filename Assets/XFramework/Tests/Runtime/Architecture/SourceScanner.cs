using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace XFramework.Architecture.Tests
{
    /// <summary>
    /// 源码文本扫描的共享工具：定位包根、枚举 <c>.cs</c>、剥注释行。
    /// <para><b>为什么抽出来</b>：「剥注释只留代码」此前在 <c>NamingConventionTests</c> 与
    /// <c>ModuleBoundaryTests</c> 各有一份，前者当场留了话——「两处副本；若出现第三处就该抽出来了」。
    /// 本轮的坏尺子守卫正是第三处，且它与 <c>NamingConventionTests</c> 同在 Runtime 测试程序集，
    /// 于是按那条纪律抽到这里。<c>ModuleBoundaryTests</c> 在 Editor 测试程序集，两个程序集互不可见，
    /// 它那份**保持独立**（与 <c>AutoInitTests</c> 类注释里「两个测试程序集互不可见，两份真相不如不写」
    /// 同一口径）。</para>
    /// </summary>
    internal static class SourceScanner
    {
        #region 定位

        /// <summary>
        /// 包根。用 <c>Application.dataPath</c> 拼——测试壳的 Assets 是指回仓库的 junction，
        /// 因此这里拿到的就是真源码。
        /// </summary>
        internal static string PackageRoot => Path.Combine(Application.dataPath, "XFramework");

        /// <summary>
        /// 枚举包内全部 <c>.cs</c>（递归；含 <c>Samples~</c>——它虽不被 Unity 编译，但仍在包内，
        /// 且它只许用公开 API，正是最该被守卫盯住的样本）。
        /// </summary>
        internal static IEnumerable<string> EnumerateSources()
        {
            Assert.IsTrue(Directory.Exists(PackageRoot),
                $"找不到包目录：{PackageRoot}（测试壳的 Assets 是否为 junction?）");

            return Directory.GetFiles(PackageRoot, "*.cs", SearchOption.AllDirectories);
        }

        /// <summary>把绝对路径转成包内相对路径（统一正斜杠，供失败消息里定位）。</summary>
        internal static string RelativePath(string file)
            => file.Substring(PackageRoot.Length + 1).Replace('\\', '/');

        /// <summary>
        /// 模块全集 = <c>Runtime/</c> 的直接子目录（与 <c>ModuleBoundaryTests</c> 同一枚举口径）。
        /// <para><b>为什么在这里</b>：「模块是什么」是 <c>ReadmeTableTests</c>（四张表）与
        /// <c>ReadmeSectionTests</c>（已知限制小节）共同的输入，同一个问题的两份答案是漂移的起点——
        /// 按 <c>SourceScanner</c> 的既有判据（出现第三处副本就该抽出来），这里一次抽成唯一实现。
        /// 目录不存在时记一条违规并返回 <c>null</c>（调用方继续收集其它违规后再断言）。</para>
        /// </summary>
        internal static HashSet<string> CollectRuntimeModules(List<string> violations)
        {
            string runtimeRoot = Path.Combine(PackageRoot, "Runtime");
            if (!Directory.Exists(runtimeRoot))
            {
                violations.Add($"找不到 Runtime 目录：{runtimeRoot}——测试壳的 Assets 是否为 junction?");
                return null;
            }

            var modules = new HashSet<string>(StringComparer.Ordinal);
            foreach (var directory in Directory.GetDirectories(runtimeRoot))
            {
                modules.Add(Path.GetFileName(directory));
            }

            return modules;
        }

        /// <summary>排序副本：集合迭代顺序不稳定，违规消息要可复现。</summary>
        internal static List<string> Sorted(IEnumerable<string> source)
        {
            var list = new List<string>(source);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        #endregion

        #region 只读代码

        /// <summary>
        /// 读出「只有代码」的文本：注释行按原行数置空，故行号仍与源文件一致。
        /// <para><b>剥的是行首</b>（<c>//</c> / <c>*</c> / <c>/*</c>）：行尾注释不在射程内——
        /// 想在注释里解释某个被禁用的 API，写成独立注释行即可；写在代码行尾会误报。</para>
        /// </summary>
        internal static string ReadCodeOnly(string file)
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
