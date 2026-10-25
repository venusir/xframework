using System.IO;
using NUnit.Framework;

namespace XFramework.XConfig.Luban.Tests
{
    /// <summary>
    /// 扩展包 Runtime 程序集的公开 API 基线。扩展包不在核心
    /// <c>PublicApiBaselineTests</c> 射程内，由包内守卫补上。
    /// <para>更新：<c>pwsh -File Tools/run-tests.ps1 -UpdateLubanApiBaseline</c>
    /// （同一次更新覆盖 Runtime + Editor 两组；写盘后按设计判失败）。</para>
    /// </summary>
    [TestFixture]
    public sealed class LubanPublicApiBaselineTests
    {
        private const string ExpectedAssemblyName = "Venusy609.Xframework.Luban";

        private static string BaselinePath => Path.Combine(
            LubanRuntimeTestPaths.PackageRoot,
            "Tests", "Runtime", "Architecture", "LubanPublicApiBaseline.txt");

        [Test]
        public void AssemblyName_IsStable()
        {
            Assert.That(typeof(LubanConfig).Assembly.GetName().Name,
                Is.EqualTo(ExpectedAssemblyName));
        }

        [Test]
        public void PublicApi_MatchesBaseline()
        {
            LubanPublicApiBaselineCheck.Verify(
                typeof(LubanConfig).Assembly,
                BaselinePath,
                LubanPublicApiSnapshot.RuntimeHeader,
                "Runtime");
        }
    }
}
