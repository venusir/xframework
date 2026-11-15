using System.IO;
using NUnit.Framework;

namespace XFramework.Samples.Tests
{
    /// <summary>
    /// <c>Samples~/Cinemachine</c> 的常驻结构守卫：场景引用没有编译器兜底，本 fixture 用纯文件读取
    /// 钉住脚本 GUID、Cinemachine 组件与序列化引用。
    /// <para>它<b>不</b>运行示例本身，也不读取仓库的 <c>Tools/</c>（测试壳不可达）。</para>
    /// </summary>
    [TestFixture]
    public class CinemachineSampleStructureTests
    {
        #region Paths

        private static string SampleRoot => SampleSceneTestSupport.SampleRoot("Cinemachine");

        private static string SceneFile => Path.Combine(SampleRoot, "Cinemachine.unity");

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
                "Cinemachine.unity",
                "CameraSample.cs",
                "CinemachineSampleOverlay.cs",
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
        public void Scene_ContainsSampleComponents()
        {
            SampleSceneTestSupport.AssertSceneContainsScriptGuid(
                SceneFile, Path.Combine(SampleRoot, "CameraSample.cs.meta"), "场景没有引用 CameraSample。");
            SampleSceneTestSupport.AssertSceneContainsScriptGuid(
                SceneFile, Path.Combine(SampleRoot, "CinemachineSampleOverlay.cs.meta"),
                "场景没有引用 CinemachineSampleOverlay。");
        }

        [Test]
        public void Scene_ContainsCinemachineComponents()
        {
            var text = File.ReadAllText(SceneFile);

            var cameraGuid = SampleSceneTestSupport.FindPackageScriptGuid(
                "com.unity.cinemachine*", "CinemachineCamera");
            var brainGuid = SampleSceneTestSupport.FindPackageScriptGuid(
                "com.unity.cinemachine*", "CinemachineBrain");
            var impulseGuid = SampleSceneTestSupport.FindPackageScriptGuid(
                "com.unity.cinemachine*", "CinemachineImpulseListener");

            SampleSceneTestSupport.AssertSceneContainsGuid(SceneFile, brainGuid, "场景没有 CinemachineBrain。");
            Assert.GreaterOrEqual(
                SampleSceneTestSupport.CountOccurrences(text, cameraGuid), 2,
                "场景应至少有两台 CinemachineCamera。");
            Assert.GreaterOrEqual(
                SampleSceneTestSupport.CountOccurrences(text, impulseGuid), 2,
                "两台 vcam 都应带 CinemachineImpulseListener（震屏需要）。");
        }

        [Test]
        public void Scene_WiresSampleAndOverlayReferences()
        {
            foreach (var property in new[] { "_brain", "_first", "_second", "_follow", "_sample", "_target" })
                SampleSceneTestSupport.AssertSerializedReferenceNonZero(SceneFile, property);
        }

        [Test]
        public void PackageManifest_RegistersSample()
        {
            SampleSceneTestSupport.AssertPackageRegistersSample("Samples~/Cinemachine");
            SampleSceneTestSupport.AssertSampleJsonHasDisplayNameAndDescription(SampleRoot);
        }

        [Test]
        public void Readme_MentionsSceneAndPlay()
        {
            SampleSceneTestSupport.AssertReadmeMentions(SampleRoot, "Cinemachine.unity");
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
