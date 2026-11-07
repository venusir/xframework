using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace XFramework.XAudio.Fmod.Tests
{
    /// <summary>
    /// 扩展包契约：程序集名、唯一公开类型、provider/缝的可见性、package.json 与 Runtime asmdef 的依赖边界，
    /// 以及仓库不提交会触发自动安装的 settings 资产。
    /// </summary>
    [TestFixture]
    public class FmodPackageContractTests
    {
        private const string ExpectedAssemblyName = "Venusy609.Xframework.Fmod";

        [Test]
        public void AssemblyName_IsStable()
        {
            Assert.AreEqual(ExpectedAssemblyName, typeof(FmodAudioSettings).Assembly.GetName().Name);
        }

        /// <summary>
        /// <see cref="FmodAudioSettings"/> 是唯一公开类型，且只作为配置资产——除编译器生成的 public 构造函数外
        /// 不得有 public 成员（控制面只在 AudioManager）。
        /// </summary>
        [Test]
        public void FmodAudioSettings_IsConfigAssetWithoutPublicApi()
        {
            Assert.IsTrue(typeof(FmodAudioSettings).IsPublic);

            var unexpected = new System.Collections.Generic.List<string>();
            foreach (var member in typeof(FmodAudioSettings).GetMembers(
                         BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                         BindingFlags.DeclaredOnly))
            {
                if (member is ConstructorInfo)
                    continue;

                unexpected.Add(member.Name);
            }

            Assert.IsEmpty(unexpected,
                "FmodAudioSettings 是配置资产，不应有 public 成员：\n  " +
                string.Join("\n  ", unexpected));
        }

        [Test]
        public void ProviderAndSeams_AreInternal()
        {
            Assert.IsFalse(typeof(FmodAudioProvider).IsPublic);
            Assert.IsFalse(typeof(IFmodEventBackend).IsPublic);
            Assert.IsFalse(typeof(FmodProviderOptions).IsPublic);
            Assert.IsFalse(typeof(FmodEventSlotTable).IsPublic);
            Assert.IsFalse(typeof(FmodInstaller).IsPublic);
        }

        [Test]
        public void PackageJson_IsExpected()
        {
            string json = FmodTestPaths.ReadText("package.json");
            if (json == null)
                Assert.Ignore("找不到 XFramework.Fmod 包根；跳过 package.json 契约。");

            StringAssert.Contains("\"name\": \"com.venusy609.xframework.fmod\"", json);
            StringAssert.Contains("\"version\": \"0.1.0\"", json);
            StringAssert.Contains("\"unity\": \"6000.3\"", json);
            StringAssert.Contains("\"dependencies\": {}", json);
        }

        [Test]
        public void RuntimeAsmdef_ReferencesCoreAndUniTaskOnly()
        {
            string asmdef = FmodTestPaths.ReadText(
                "Runtime/Venusy609.Xframework.Fmod.asmdef");
            if (asmdef == null)
                Assert.Ignore("找不到 XFramework.Fmod 包根；跳过 asmdef 契约。");

            StringAssert.Contains("\"Venusy609.Xframework\"", asmdef);
            StringAssert.Contains("\"UniTask\"", asmdef);
            StringAssert.DoesNotContain("FMODUnity", asmdef);
            StringAssert.DoesNotContain("\"FMOD\"", asmdef);
        }

        /// <summary>
        /// 仓库里存在该资产会触发 Phase 2 的自动安装，污染有 SDK 的机器上的全量测试。
        /// 消费方布局（UPM）不适用，跳过。
        /// </summary>
        [Test]
        public void Repository_DoesNotCommitFmodSettingsAsset()
        {
            string repoPackage = Path.Combine(Application.dataPath, "XFramework.Fmod");
            if (!Directory.Exists(repoPackage))
                Assert.Ignore("消费方布局；跳过仓库 settings 资产检查。");

            string asset = Path.Combine(Application.dataPath, "Resources", "FmodAudioSettings.asset");
            Assert.IsFalse(File.Exists(asset),
                "仓库不得提交 Assets/Resources/FmodAudioSettings.asset：Phase 2 的自动安装会读取它，" +
                "在有 FMOD SDK 的机器上污染全量测试。");
        }
    }
}
