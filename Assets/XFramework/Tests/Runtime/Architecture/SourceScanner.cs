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
