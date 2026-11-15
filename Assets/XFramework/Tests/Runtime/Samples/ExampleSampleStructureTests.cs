using System.IO;
using NUnit.Framework;

namespace XFramework.Samples.Tests
{
    /// <summary>
    /// <c>Samples~/Example</c> 的常驻结构守卫：示例资产不参与 Unity 编译、场景引用也没有编译器兜底，
    /// 本 fixture 用纯文件读取把最容易漂移的约束钉住。
    /// <para>它<b>不</b>运行示例本身，也不读取仓库的 <c>Tools/</c>（测试壳不可达）。</para>
    /// </summary>
    [TestFixture]
    public class ExampleSampleStructureTests
    {
        #region Paths

        private static string SampleRoot => SampleSceneTestSupport.SampleRoot("Example");

        private static string SceneFile => Path.Combine(SampleRoot, "Example.unity");

        #endregion

        #region Tests

        [Test]
        public void RequiredFiles_Exist()
        {
            Assert.IsTrue(Directory.Exists(SampleRoot), $"找不到示例目录：{SampleRoot}");

            var required = new[]
            {
                ".sample.json",
                "README.md",
                "Example.unity",
                "ExampleSampleHub.cs",
                "ReactiveSample.cs",
                "MessageSample.cs",
                "UISample.cs",
                "AudioSample.cs",
                "TimerSample.cs",
                "DiagnosticsSample.cs",
                "SampleLogCategories.cs",
            };

            foreach (var relative in required)
                Assert.IsTrue(File.Exists(Path.Combine(SampleRoot, relative)), $"缺少示例文件：{relative}");
        }

        [Test]
        public void AllEntries_HaveMeta()
        {
            SampleSceneTestSupport.AssertAllEntriesHaveMeta(SampleRoot);
        }

        [Test]
        public void Scene_ScriptGuidsResolve()
        {
            var map = SampleSceneTestSupport.BuildScriptGuidMap();
            SampleSceneTestSupport.AssertSceneScriptGuidsResolve(SceneFile, map);
        }

        [Test]
        public void Scene_ContainsExpectedComponents()
        {
            foreach (var script in new[]
                     {
                         "ExampleSampleHub", "ReactiveSample", "MessageSample", "TimerSample", "AudioSample",
                     })
            {
                SampleSceneTestSupport.AssertSceneContainsScriptGuid(
                    SceneFile,
                    Path.Combine(SampleRoot, script + ".cs.meta"),
                    $"场景没有引用 {script}。");
            }
        }

        [Test]
        public void Scene_WiresHubReferences()
        {
            SampleSceneTestSupport.AssertSerializedReferenceNonZero(SceneFile, "_timer");
            SampleSceneTestSupport.AssertSerializedReferenceNonZero(SceneFile, "_audio");
        }

        [Test]
        public void PackageManifest_RegistersSample()
        {
            SampleSceneTestSupport.AssertPackageRegistersSample("Samples~/Example");
            SampleSceneTestSupport.AssertSampleJsonHasDisplayNameAndDescription(SampleRoot);
        }

        [Test]
        public void Readme_MentionsSceneAndPlay()
        {
            SampleSceneTestSupport.AssertReadmeMentions(SampleRoot, "Example.unity");
        }

        [Test]
        public void Sources_AvoidConditionalCompilationAndUnityEditor()
        {
            SampleSceneTestSupport.AssertSampleSourcesAvoidConditionalCompilationAndUnityEditor(SampleRoot);
        }

        [Test]
        public void SampleText_DoesNotReferenceDocumentation()
        {
            SampleSceneTestSupport.AssertSampleTextDoesNotReferenceDocumentation(SampleRoot);
        }

        #endregion
    }
}
