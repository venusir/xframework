#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace XFramework.XConfig.Luban.Tests
{
    /// <summary>
    /// 扩展包 **Editor 程序集** `Venusy609.Xframework.Luban.Editor` 的公开 API 基线。
    /// <para><b>为什么在 Runtime 测试程序集里用 <c>#if UNITY_EDITOR</c></b>：编译期看不到 Editor
    /// 程序集（asmdef 无引用），但 PlayMode 测试跑在编辑器进程里，运行时能按名取到它；放 Runtime 侧
    /// 还沿用了核心 <c>PublicApiBaselineTests</c> 的同一条理由——带 <c>-Filter</c> 且未指定平台时
    /// 只跑 PlayMode，放 <c>Tests/Editor/</c> 会被最常见的开发回路静默跳过。播放器构建里这段整体被编掉。</para>
    /// <para>更新：<c>pwsh -File Tools/run-tests.ps1 -UpdateLubanApiBaseline</c></para>
    /// </summary>
    [TestFixture]
    public sealed class LubanEditorPublicApiBaselineTests
    {
        private const string ExpectedAssemblyName = "Venusy609.Xframework.Luban.Editor";

        private static string BaselinePath => Path.Combine(
            LubanRuntimeTestPaths.PackageRoot,
            "Tests", "Runtime", "Architecture", "LubanEditorPublicApiBaseline.txt");

        [Test]
        public void EditorPublicApi_MatchesBaseline()
        {
            Assembly assembly = LoadAssembly(ExpectedAssemblyName);
            LubanPublicApiBaselineCheck.Verify(
                assembly,
                BaselinePath,
                LubanPublicApiSnapshot.EditorHeader,
                "Editor");
        }

        /// <summary>
        /// 按名取程序集：先扫当前 AppDomain（Unity 已在进程内加载它时没有重复加载的副作用），
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
                    $"[Luban 公开 API 基线] 找不到程序集 {name}：{e.Message}\n" +
                    "Editor 基线必须在编辑器进程内运行（PlayMode 测试跑在编辑器里；播放器构建会整体编掉本 fixture）。");
                return null;
            }
        }
    }
}
#endif
