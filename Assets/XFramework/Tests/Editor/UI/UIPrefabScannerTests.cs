using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using XFramework.XUI;

namespace XFramework.Editor.UI.Tests
{
    /// <summary>
    /// <see cref="UIPrefabScanner"/> 与地址接缝的测试。
    /// <para>地址真实路径依赖工程内的 YooAsset 采集配置，本仓没有该资产；真实 provider 的
    /// 「未配置、跳过」分支与源码守卫在此覆盖，其余状态用假 provider 驱动。</para>
    /// </summary>
    class UIPrefabScannerTests
    {
        private const string TempFolder = "Assets/__XfwUIPrefabValidatorTests__";

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

        #region Address Catalog

        [Test]
        public void ResolveAddressCatalog_Disabled_SkipsQuery()
        {
            var provider = new FakeAddressProvider(UIPrefabAddressCatalog.CreateAvailable());
            var report = new UIPrefabValidationReport();

            var catalog = UIPrefabScanner.ResolveAddressCatalog(false, provider, report);

            Assert.AreEqual(0, provider.QueryCount);
            Assert.IsFalse(catalog.IsAvailable);
            Assert.AreEqual(1, report.Notices.Count);
            Assert.AreEqual(UIPrefabIssueSeverity.Info, report.Notices[0].Severity);
        }

        [Test]
        public void ResolveAddressCatalog_NotConfigured_AddsInfoNotice()
        {
            var provider = new FakeAddressProvider(UIPrefabAddressCatalog.NotConfigured("未配置"));
            var report = new UIPrefabValidationReport();

            var catalog = UIPrefabScanner.ResolveAddressCatalog(true, provider, report);

            Assert.AreEqual(UIPrefabAddressStatus.NotConfigured, catalog.Status);
            Assert.IsFalse(catalog.IsAvailable);
            Assert.AreEqual(1, report.Notices.Count);
            Assert.AreEqual(UIPrefabIssueSeverity.Info, report.Notices[0].Severity);
        }

        [Test]
        public void ResolveAddressCatalog_MultipleConfigs_AddsErrorNotice()
        {
            var provider = new FakeAddressProvider(UIPrefabAddressCatalog.MultipleConfigs("多份配置"));
            var report = new UIPrefabValidationReport();

            var catalog = UIPrefabScanner.ResolveAddressCatalog(true, provider, report);

            Assert.AreEqual(UIPrefabAddressStatus.MultipleConfigs, catalog.Status);
            Assert.AreEqual(1, report.Notices.Count);
            Assert.AreEqual(UIPrefabIssueSeverity.Error, report.Notices[0].Severity);
        }

        [Test]
        public void ResolveAddressCatalog_Failed_AddsErrorNotice()
        {
            var provider = new FakeAddressProvider(UIPrefabAddressCatalog.Failed("采集失败"));
            var report = new UIPrefabValidationReport();

            var catalog = UIPrefabScanner.ResolveAddressCatalog(true, provider, report);

            Assert.AreEqual(UIPrefabAddressStatus.Failed, catalog.Status);
            Assert.AreEqual(1, report.Notices.Count);
            Assert.AreEqual(UIPrefabIssueSeverity.Error, report.Notices[0].Severity);
        }

        [Test]
        public void ResolveAddressCatalog_Available_NoNotice()
        {
            var provider = new FakeAddressProvider(UIPrefabAddressCatalog.CreateAvailable());
            var report = new UIPrefabValidationReport();

            var catalog = UIPrefabScanner.ResolveAddressCatalog(true, provider, report);

            Assert.IsTrue(catalog.IsAvailable);
            Assert.AreEqual(0, report.Notices.Count);
        }

        #endregion

        #region Scan

        [Test]
        public void Scan_TempPrefab_FindsCandidateAndAttachesAddress()
        {
            EnsureTempFolder();

            var prefabPath = TempFolder + "/TestTip.prefab";
            var root = new GameObject("TestTip", typeof(RectTransform));
            root.AddComponent<UITipItem>();

            // 用运行时组件而不是测试程序集里的面板脚本：Editor-only 程序集的 MonoBehaviour
            // 保存进预制体后脚本引用不可靠，加载回来可能只剩缺失脚本。
            var label = new GameObject("Label", typeof(RectTransform));
            label.transform.SetParent(root.transform, false);
            label.AddComponent<Text>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            Assert.IsNotNull(prefab);
            AssetDatabase.Refresh();

            // 中间断言：把「资产没导进来」「类型不对」「候选识别失败」与最终结果分开，
            // 否则失败信息只剩一个 Entries.Count，排查要重新猜。
            var found = AssetDatabase.FindAssets("t:Prefab", new[] { TempFolder });
            Assert.IsNotEmpty(found, $"FindAssets 没找到临时预制体：{prefabPath}");

            var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(loaded, $"LoadAssetAtPath 返回 null：{prefabPath}");
            Assert.AreEqual(
                PrefabAssetType.Regular,
                PrefabUtility.GetPrefabAssetType(loaded),
                "临时预制体的资产类型不是 Regular");
            Assert.IsTrue(
                UIPrefabCandidateDetector.IsCandidate(loaded),
                "临时预制体没有被识别为 UI 候选");

            var guid = AssetDatabase.AssetPathToGUID(prefabPath);
            var catalog = UIPrefabAddressCatalog.CreateAvailable();
            catalog.Add(new UIPrefabAddressEntry("DefaultPackage", "ui/test_tip", prefabPath, guid));

            var report = UIPrefabScanner.Scan(
                new[] { TempFolder },
                checkAddresses: true,
                addressProvider: new FakeAddressProvider(catalog));

            Assert.AreEqual(1, report.Entries.Count);
            Assert.AreEqual(prefabPath, report.Entries[0].AssetPath);
            Assert.AreEqual(1, report.Entries[0].Addresses.Count);
            Assert.AreEqual("DefaultPackage", report.Entries[0].Addresses[0].PackageName);
            Assert.AreEqual("ui/test_tip", report.Entries[0].Addresses[0].Address);

            // 没有 TMP 文本，规则 009 应报告；地址已命中，规则 008 不应报告。
            Assert.AreEqual(1, report.Entries[0].Issues.Count);
            Assert.AreEqual(UIPrefabRuleIds.TipWithoutTmpText, report.Entries[0].Issues[0].RuleId);
        }

        [Test]
        public void YooAssetProvider_DoesNotUseStaticSettingFacade()
        {
            var guids = AssetDatabase.FindAssets("YooAssetUIPrefabAddressProvider t:MonoScript");
            Assert.IsNotEmpty(guids);

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var source = File.ReadAllText(path);

            // YooAsset 的静态配置门面在找不到配置时会自动创建资产；只读校验器不得触碰它们。
            StringAssert.DoesNotContain("AssetBundleCollectorSettingData", source);
            StringAssert.DoesNotContain("SettingLoader", source);
        }

        #endregion

        #region Helpers

        private static void EnsureTempFolder()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.CreateFolder("Assets", "__XfwUIPrefabValidatorTests__");
        }

        private static void DeleteTempFolder()
        {
            if (AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.DeleteAsset(TempFolder);
        }

        #endregion

        #region Test Doubles

        private sealed class FakeAddressProvider : IUIPrefabAddressProvider
        {
            private readonly UIPrefabAddressCatalog _catalog;

            public FakeAddressProvider(UIPrefabAddressCatalog catalog)
            {
                _catalog = catalog;
            }

            public int QueryCount { get; private set; }

            public UIPrefabAddressCatalog Query()
            {
                QueryCount++;
                return _catalog;
            }
        }

        #endregion
    }
}
