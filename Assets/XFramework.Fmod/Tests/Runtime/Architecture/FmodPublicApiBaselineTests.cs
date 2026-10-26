using System.IO;
using NUnit.Framework;

namespace XFramework.XAudio.Fmod.Tests
{
    /// <summary>
    /// 扩展包 Runtime 程序集的公开 API 基线。扩展包不在核心 <c>PublicApiBaselineTests</c> 射程内，
    /// 由包内守卫补上。
    /// <para>更新：<c>pwsh -File Tools/run-tests.ps1 -UpdateFmodApiBaseline</c>（写盘后按设计判失败）。</para>
    /// </summary>
    [TestFixture]
    public sealed class FmodPublicApiBaselineTests
    {
        private const string ExpectedAssemblyName = "Venusy609.Xframework.Fmod";

        private static string BaselinePath
        {
            get
            {
                string root = FmodTestPaths.PackageRoot;
                return root == null
                    ? null
                    : Path.Combine(root, "Tests", "Runtime", "Architecture", "FmodPublicApiBaseline.txt");
            }
        }

        [Test]
        public void AssemblyName_IsStable()
        {
            Assert.That(typeof(FmodAudioSettings).Assembly.GetName().Name,
                Is.EqualTo(ExpectedAssemblyName));
        }

        [Test]
        public void PublicApi_MatchesBaseline()
        {
            string path = BaselinePath;
            if (path == null)
                Assert.Ignore("找不到 XFramework.Fmod 包根；跳过公开 API 基线。");

            FmodPublicApiBaselineCheck.Verify(
                typeof(FmodAudioSettings).Assembly,
                path,
                FmodPublicApiSnapshot.RuntimeHeader,
                "Fmod Runtime");
        }
    }
}
