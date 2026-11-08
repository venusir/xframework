using System.IO;
using NUnit.Framework;

namespace XFramework.XUI.Authoring.Tests
{
    /// <summary>
    /// 扩展包 Runtime 程序集的公开 API 基线。扩展包不在核心
    /// <c>PublicApiBaselineTests</c> 射程内，由包内守卫补上。
    /// <para>更新：<c>pwsh -File Tools/run-tests.ps1 -UpdateUIAuthoringApiBaseline</c>
    /// （同一次更新覆盖 Runtime + Editor 两组；写盘后按设计判失败）。</para>
    /// </summary>
    [TestFixture]
    public sealed class UIAuthoringPublicApiBaselineTests
    {
        private const string ExpectedAssemblyName = "Venusy609.Xframework.UI.Authoring";

        private static string BaselinePath => Path.Combine(
            UIAuthoringTestPaths.PackageRoot,
            "Tests", "Runtime", "Architecture", "UIAuthoringPublicApiBaseline.txt");

        [Test]
        public void AssemblyName_IsStable()
        {
            Assert.That(typeof(UIDeclarativePanel).Assembly.GetName().Name,
                Is.EqualTo(ExpectedAssemblyName));
        }

        [Test]
        public void PublicApi_MatchesBaseline()
        {
            UIAuthoringPublicApiBaselineCheck.Verify(
                typeof(UIDeclarativePanel).Assembly,
                BaselinePath,
                UIAuthoringPublicApiSnapshot.RuntimeHeader,
                "Runtime");
        }
    }
}
