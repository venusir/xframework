using System;
using System.Collections.Generic;
using UnityEditor;
using YooAsset.Editor;

namespace XFramework.XUI.Authoring.Editor
{
    internal enum UIAuthoringAddressStatus
    {
        Available = 0,
        NotConfigured = 1,
        MultipleConfigs = 2,
        Failed = 3,
    }

    /// <summary>
    /// 声明式动作目标的地址目录：YooAsset address → asset path。
    /// </summary>
    internal sealed class UIAuthoringAddressCatalog
    {
        internal UIAuthoringAddressStatus Status;
        internal readonly Dictionary<string, string> AddressToPath =
            new Dictionary<string, string>(StringComparer.Ordinal);

        internal bool IsAvailable => Status == UIAuthoringAddressStatus.Available;

        internal bool TryGetAssetPath(string address, out string assetPath)
        {
            assetPath = null;
            return !string.IsNullOrEmpty(address) && AddressToPath.TryGetValue(address, out assetPath);
        }
    }

    internal interface IUIAuthoringAddressProvider
    {
        UIAuthoringAddressCatalog Query();
    }

    /// <summary>
    /// 用 <c>AssetDatabase</c> 定位唯一的 <c>AssetBundleCollectorSetting</c>，逐包
    /// <c>BeginCollect(simulateBuild:true, useAssetDependencyDB:false)</c> 收集主资源地址。
    /// <para>刻意不使用 <c>AssetBundleCollectorSettingData</c> / <c>SettingLoader</c>：它们在缺配置时会
    /// 自动创建 <c>Assets/AssetBundleCollectorSetting.asset</c>，只读校验器不应留下资产。</para>
    /// </summary>
    internal sealed class YooAssetUIAuthoringAddressProvider : IUIAuthoringAddressProvider
    {
        private const string CollectorSettingTypeName = "AssetBundleCollectorSetting";

        public UIAuthoringAddressCatalog Query()
        {
            var catalog = new UIAuthoringAddressCatalog();
            string[] guids = AssetDatabase.FindAssets("t:" + CollectorSettingTypeName);
            if (guids.Length == 0)
            {
                catalog.Status = UIAuthoringAddressStatus.NotConfigured;
                return catalog;
            }

            if (guids.Length > 1)
            {
                catalog.Status = UIAuthoringAddressStatus.MultipleConfigs;
                return catalog;
            }

            var setting = AssetDatabase.LoadAssetAtPath<AssetBundleCollectorSetting>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
            if (setting == null)
            {
                catalog.Status = UIAuthoringAddressStatus.Failed;
                return catalog;
            }

            try
            {
                for (int i = 0; i < setting.Packages.Count; i++)
                {
                    AssetBundleCollectorPackage package = setting.Packages[i];
                    if (package == null || string.IsNullOrEmpty(package.PackageName))
                        continue;

                    CollectResult result = setting.BeginCollect(
                        package.PackageName,
                        simulateBuild: true,
                        useAssetDependencyDB: false);

                    for (int j = 0; j < result.CollectAssets.Count; j++)
                    {
                        CollectAssetInfo asset = result.CollectAssets[j];
                        if (!string.IsNullOrEmpty(asset.Address))
                            catalog.AddressToPath[asset.Address] = asset.AssetInfo.AssetPath;
                    }
                }

                catalog.Status = UIAuthoringAddressStatus.Available;
            }
            catch
            {
                catalog.AddressToPath.Clear();
                catalog.Status = UIAuthoringAddressStatus.Failed;
            }

            return catalog;
        }
    }
}
