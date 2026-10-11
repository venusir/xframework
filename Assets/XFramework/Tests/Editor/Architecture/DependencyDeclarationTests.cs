using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Venusy609.Xframework.Editor;

namespace XFramework.Architecture.Tests
{
    /// <summary>
    /// 依赖声明的**多落点一致守卫**：第三方依赖的版本/URL 散在三处——
    /// <c>Editor/XFrameworkDependencyInstaller.cs</c> 的一键安装清单、
    /// <c>README.md</c> 的「第三方依赖」表（对外承诺的真相）、
    /// <c>Packages/manifest.json</c>（本仓实际编译与测试的版本）——任一处漂移，读者拿到的事实就不同。
    /// <para>
    /// <b>为什么需要它</b>：安装器注释自承「这张清单的真相来源是 README 的『第三方依赖』表，两处必须
    /// 一起长」——但没有一处地方要求新改动去核对。实际后果已发生：安装器写出的 Git URL **不带 <c>#tag</c>**，
    /// 而 README 与 manifest 都钉了 tag；不钉修订时包管理器取上游默认分支，YooAsset 3.x 把
    /// <c>RawFileHandle</c> 等类型移进默认不编译的兼容层，消费方会撞上 51 处 <c>CS0246/CS0234</c>
    /// （README 自身的实测记录）。本 fixture 把这条约定变成每次全量门禁都会跑的自查。
    /// </para>
    /// <para>
    /// <b>为什么断言「逐字出现」而不是解析 JSON</b>：这里要守的是「同一个人看到的三个地方说的是同一件事」，
    /// 字符串逐字相等就是那个「同一件事」；解析成对象再比会放过「值相同但写法不同」之外的漂移，
    /// 也放过 README 这个非结构化落点（它是表格 + 代码块，没有 schema 可依）。
    /// </para>
    /// </summary>
    [TestFixture]
    public class DependencyDeclarationTests
    {
        #region 包根定位

        /// <summary>
        /// 包根 = <c>Assets/XFramework</c>。刻意沿用既有守卫的先例
        /// （<c>ModuleBoundaryTests</c> / <c>NamingConventionTests</c> 同款），不引入第二种定位方式。
        /// </summary>
        private static string PackageRoot => Path.Combine(Application.dataPath, "XFramework");

        /// <summary>仓库根 = <c>Assets</c> 的上一级（测试壳把 Packages 也 junction 过来，故这里可用）。</summary>
        private static string RepositoryRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        #endregion

        [Test]
        public void GitUrls_ArePinnedToARevision()
        {
            var violations = new List<string>();

            foreach (var kvp in ReadRequiredDependencies())
            {
                string packageId = kvp.Key;
                string value = kvp.Value;

                if (value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    int hash = value.IndexOf('#');
                    if (hash < 0)
                    {
                        violations.Add($"{packageId} 的 Git URL 没有钉修订：{value}" +
                                       "——不钉修订时包管理器取上游默认分支，Asset 模块会在消费方直接编译不过");
                        continue;
                    }

                    if (hash == value.Length - 1)
                        violations.Add($"{packageId} 的 Git URL 以 # 结尾，修订为空：{value}");

                    int pathQuery = value.IndexOf("?path=", StringComparison.Ordinal);
                    if (pathQuery >= 0 && pathQuery > hash)
                    {
                        violations.Add($"{packageId} 的 ?path= 写在 # 之后：{value}" +
                                       "——顺序反了 Unity 解析不出来（README 有记录）");
                    }
                }
                else if (value.IndexOf('#') >= 0)
                {
                    violations.Add($"{packageId} 是注册表包（写版本号），不应带 # 修订：{value}");
                }
            }

            Assert.IsEmpty(violations, "依赖清单里有未钉修订的 Git URL：\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void Dependencies_AppearVerbatimInReadmeAndManifest()
        {
            string readme = ReadRequiredFile(Path.Combine(PackageRoot, "README.md"));
            string manifest = ReadRequiredFile(Path.Combine(RepositoryRoot, "Packages", "manifest.json"));
            var violations = new List<string>();

            foreach (var kvp in ReadRequiredDependencies())
            {
                string packageId = kvp.Key;
                string value = kvp.Value;

                // 允许键值之间的空白不同（表格与 JSON 代码块的排版不同），但「谁配谁」必须逐字对上。
                var pair = new Regex($"\"{Regex.Escape(packageId)}\"\\s*:\\s*\"{Regex.Escape(value)}\"");

                if (!pair.IsMatch(readme))
                    violations.Add($"README.md 的「第三方依赖」区没有 {packageId} → {value}——" +
                                   "两处必须一起长：改这里也要改 Editor/XFrameworkDependencyInstaller.cs");

                if (!pair.IsMatch(manifest))
                    violations.Add($"Packages/manifest.json 没有 {packageId} → {value}——" +
                                   "开发工程编译与测试的必须是同一套版本，否则 README 承诺的版本没被真正验证过");
            }

            Assert.IsEmpty(violations, "依赖声明的三处落点不一致：\n  " + string.Join("\n  ", violations));
        }

        #region 反射与文件读取

        /// <summary>
        /// 读安装器的私有清单。<b>刻意反射而不改可见性</b>：它是实现细节，为测试开口子会把
        /// 「清单是不是唯一真相」这件事交给可写面。
        /// </summary>
        private static Dictionary<string, string> ReadRequiredDependencies()
        {
            var field = typeof(XFrameworkDependencyInstaller).GetField(
                "RequiredUPMDependencies", BindingFlags.Static | BindingFlags.NonPublic);

            Assert.IsNotNull(field,
                "XFrameworkDependencyInstaller.RequiredUPMDependencies 不存在（改名了？）——" +
                "本守卫按名字读它，改名时请同步这里");

            var table = field.GetValue(null) as Dictionary<string, string>;
            Assert.IsNotNull(table, "RequiredUPMDependencies 不再是 Dictionary<string, string>");

            return table;
        }

        private static string ReadRequiredFile(string path)
        {
            Assert.IsTrue(File.Exists(path),
                $"找不到 {path}——本守卫按仓库布局定位（测试壳把 Assets/Packages 一起 junction，故这里应当存在）");

            return File.ReadAllText(path);
        }

        #endregion
    }
}
