#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace XFramework.Architecture.Tests
{
    /// <summary>
    /// 公开 API 基线守卫：**Editor 程序集** `Venusy609.Xframework.Editor` 的公开面不许悄悄变。
    /// <para><b>为什么在 Runtime 测试程序集里用 <c>#if UNITY_EDITOR</c></b>：编译期看不到 Editor 程序集
    /// （asmdef 无引用），但 PlayMode 测试跑在编辑器进程里，运行时能按名取到它；而放 Runtime 侧还沿用了
    /// <see cref="PublicApiBaselineTests"/> 的同一条理由——带 <c>-Filter</c> 且未指定平台时只跑 PlayMode，
    /// 放 <c>Tests/Editor/</c> 会被最常见的开发回路静默跳过。播放器构建里这段整体被编掉。
    /// <para><b>与 Runtime 基线的分工</b>：两条基线各自独立、共用 <see cref="PublicApiBaselineCheck"/> 与
    /// <see cref="PublicApiSnapshot"/>；Editor 面**不承诺**运行时语义化兼容，但它变了必须被人看见。</para>
    /// <para>更新：<c>pwsh -File Tools/run-tests.ps1 -UpdateApiBaseline</c>
    /// （同一次更新覆盖 Runtime + Editor 两组；写盘后按设计判失败）。</para>
    /// </summary>
    [TestFixture]
    public class EditorPublicApiBaselineTests
    {
        #region Constants

        private const string ExpectedAssemblyName = "Venusy609.Xframework.Editor";

        private const string Header =
            "# XFramework Editor 程序集公开 API 基线（自动生成，勿手工编辑）\n" +
            "#\n" +
            "# 生成：Tests/Runtime/Architecture/EditorPublicApiBaselineTests.cs\n" +
            "# 更新：pwsh -File Tools/run-tests.ps1 -UpdateApiBaseline\n" +
            "# 比较：按 \\n 拆行、去行尾 \\r，忽略 '#' 开头的行——行尾漂移不算变化；其余任何一行的增删都是公开面变化。\n" +
            "#\n" +
            "# 本文件任何非注释行变化都意味着 Editor 公开 API 变化，按顺序处置：\n" +
            "#   1) 公开 API 变更 / 破坏性变更：按 CLAUDE.md「提交拆分与授权」单独请用户确认；\n" +
            "#   2) 破坏性变更：在 Assets/XFramework/CHANGELOG.md 的「破坏性变更迁移表」补一行（旧写法 → 新写法 → 说明）；\n" +
            "#   3) 更新基线（-UpdateApiBaseline），随后不带开关重跑一次取绿灯；本文件与其 .meta 必须一起提交。\n" +
            "#\n" +
            "# 射程：Editor 程序集 Venusy609.Xframework.Editor（只反映编辑器编译产物）。\n" +
            "# Runtime 程序集 Venusy609.Xframework 另见同目录的 PublicApiBaseline.txt。\n";

        private static string BaselinePath => Path.Combine(
            SourceScanner.PackageRoot, "Tests", "Runtime", "Architecture", "EditorPublicApiBaseline.txt");

        private static string DiffPath => Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "TestResults", "EditorPublicApiBaseline.diff"));

        #endregion

        #region Test

        [Test]
        public void EditorPublicApi_MatchesBaseline()
        {
            Assembly assembly = LoadAssembly(ExpectedAssemblyName);
            PublicApiBaselineCheck.Verify(
                assembly, BaselinePath, DiffPath, Header, "Editor");
        }

        /// <summary>
        /// 按名取程序集：**先扫当前 AppDomain**（Unity 已在进程内加载它时没有重复加载的副作用），
        /// 扫不到再显式加载；两者都失败即环境不对（例如被当成播放器测试跑）。
        /// </summary>
        private static Assembly LoadAssembly(string name)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(assembly.GetName().Name, name, StringComparison.Ordinal))
                    return assembly;
            }

            try
            {
                return Assembly.Load(name);
            }
            catch (Exception e)
            {
                Assert.Fail(
                    $"[公开 API 基线] 找不到程序集 {name}：{e.Message}\n" +
                    "Editor 基线必须在编辑器进程内运行（PlayMode 测试跑在编辑器里；播放器构建会整体编掉本 fixture）。");
                return null;
            }
        }

        #endregion
    }
}
#endif
