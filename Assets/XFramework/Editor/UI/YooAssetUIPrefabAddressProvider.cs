using System;
using System.Text;
using UnityEditor;
using YooAsset.Editor;

namespace XFramework.Editor.UI
{
    /// <summary>
    /// 经 YooAsset 采集配置建立地址目录。
    /// <para><b>只读</b>：只定位、加载工程内唯一的采集配置，不在工程里创建或修改任何资产。
    /// 找不到配置时返回 <see cref="UIPrefabAddressStatus.NotConfigured"/>，地址检查自动跳过。</para>
    /// <para><b>按「能按地址加载」取集合</b>：逐包裹以模拟构建方式收集，只收主资源采集器；
    /// 只被静态/依赖采集器收录的预制体本来就不能通过代码按地址加载，规则 008 会如实报告。</para>
    /// </summary>
    internal sealed class YooAssetUIPrefabAddressProvider : IUIPrefabAddressProvider
    {
        #region Constants

        private const string CollectorSettingTypeName = "AssetBundleCollectorSetting";

        #endregion

        #region IUIPrefabAddressProvider

        /// <inheritdoc/>
        public UIPrefabAddressCatalog Query()
        {
            var guids = AssetDatabase.FindAssets($"t:{CollectorSettingTypeName}");

            if (guids == null || guids.Length == 0)
            {
                return UIPrefabAddressCatalog.NotConfigured(
                    "未找到 AssetBundleCollectorSetting，地址检查已跳过。若项目尚未接入 YooAsset，可忽略本条。");
            }

            if (guids.Length > 1)
            {
                var builder = new StringBuilder();
                builder.AppendLine("工程内存在多份 AssetBundleCollectorSetting，无法判断该用哪一份，地址检查已跳过：");
                for (int i = 0; i < guids.Length; i++)
                    builder.AppendLine(AssetDatabase.GUIDToAssetPath(guids[i]));

                return UIPrefabAddressCatalog.MultipleConfigs(builder.ToString());
            }

            var settingPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            var setting = AssetDatabase.LoadAssetAtPath<AssetBundleCollectorSetting>(settingPath);
            if (setting == null)
            {
                return UIPrefabAddressCatalog.Failed(
                    $"无法加载采集配置 '{settingPath}'，地址检查已跳过。");
            }

            var catalog = UIPrefabAddressCatalog.CreateAvailable();

            for (int i = 0; i < setting.Packages.Count; i++)
            {
                var package = setting.Packages[i];
                if (package == null || string.IsNullOrEmpty(package.PackageName))
                    continue;

                try
                {
                    // simulateBuild: true 会忽略静态/依赖收集器与依赖计算：
                    // 既避免为校验器做全量依赖收集，又正好留下「能按地址加载」的主资源。
                    var result = setting.BeginCollect(
                        package.PackageName,
                        simulateBuild: true,
                        useAssetDependencyDB: false);

                    var collectAssets = result?.CollectAssets;
                    if (collectAssets == null)
                        continue;

                    for (int j = 0; j < collectAssets.Count; j++)
                    {
                        var asset = collectAssets[j];
                        if (asset?.AssetInfo == null)
                            continue;

                        catalog.Add(new UIPrefabAddressEntry(
                            package.PackageName,
                            asset.Address,
                            asset.AssetInfo.AssetPath,
                            asset.AssetInfo.AssetGUID));
                    }
                }
                catch (Exception e)
                {
                    // 任一包裹失败就整次放弃：部分包裹的结果会让 008 大面积误报，
                    // 而「无法判断」比「误报一堆」诚实。
                    return UIPrefabAddressCatalog.Failed(
                        $"采集包裹 '{package.PackageName}' 失败，地址检查已跳过：{e.Message}");
                }
            }

            return catalog;
        }

        #endregion
    }
}
