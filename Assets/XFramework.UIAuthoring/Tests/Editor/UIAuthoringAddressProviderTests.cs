using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace XFramework.XUI.Authoring.Editor.Tests
{
    [TestFixture]
    public sealed class UIAuthoringAddressProviderTests
    {
        private const string TempFolder = "Assets/__XfwUIAuthoringAddressTests__";

        [SetUp]
        public void SetUp()
        {
            DeleteTempFolder();
        }

        [TearDown]
        public void TearDown()
        {
            DeleteTempFolder();
        }

        [Test]
        public void ProviderSource_DoesNotUseStaticSettingFacade()
        {
            string path = Path.Combine(
                Application.dataPath,
                "XFramework.UIAuthoring/Editor/UIAuthoringAddressProvider.cs");
            if (!File.Exists(path))
                Assert.Ignore("扩展包不在 Assets 布局下，跳过源码守卫。");

            string source = ReadCodeOnly(path);
            Assert.That(source.Contains("AssetBundleCollectorSettingData"), Is.False);
            Assert.That(source.Contains("SettingLoader"), Is.False);
        }

        /// <summary>
        /// 读出「只有代码」的文本：行首注释置空，故允许在独立注释行里说明被禁用的 API。
        /// 与核心测试程序集的 <c>SourceScanner.ReadCodeOnly</c> 同口径（两个测试程序集互不可见）。
        /// </summary>
        private static string ReadCodeOnly(string path)
        {
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//", System.StringComparison.Ordinal)
                    || trimmed.StartsWith("*", System.StringComparison.Ordinal)
                    || trimmed.StartsWith("/*", System.StringComparison.Ordinal))
                {
                    lines[i] = string.Empty;
                }
            }
            return string.Join("\n", lines);
        }

        [Test]
        public void Query_WithCollectorSetting_CollectsAddress()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.CreateFolder("Assets", "__XfwUIAuthoringAddressTests__");

            var go = new GameObject("Panel", typeof(RectTransform));
            string prefabPath = TempFolder + "/Panel.prefab";
            PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();

            var setting = ScriptableObject.CreateInstance<AssetBundleCollectorSetting>();
            var package = new AssetBundleCollectorPackage
            {
                PackageName = "Probe",
                EnableAddressable = true,
            };
            var group = new AssetBundleCollectorGroup { GroupName = "ProbeGroup" };
            group.Collectors.Add(new AssetBundleCollector
            {
                CollectPath = TempFolder,
                CollectorType = ECollectorType.MainAssetCollector,
                AddressRuleName = nameof(AddressByFileName),
                PackRuleName = nameof(PackDirectory),
                FilterRuleName = nameof(CollectAll),
            });
            package.Groups.Add(group);
            setting.Packages.Add(package);
            string settingPath = TempFolder + "/AssetBundleCollectorSetting.asset";
            AssetDatabase.CreateAsset(setting, settingPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            UIAuthoringAddressCatalog catalog = new YooAssetUIAuthoringAddressProvider().Query();

            Assert.That(catalog.Status, Is.EqualTo(UIAuthoringAddressStatus.Available));
            Assert.That(catalog.AddressToPath.ContainsKey("Panel"), Is.True);
        }

        private static void DeleteTempFolder()
        {
            if (AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.DeleteAsset(TempFolder);
            if (Directory.Exists(TempFolder))
                Directory.Delete(TempFolder, true);
        }
    }
}
