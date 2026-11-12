using System.IO;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace XFramework.Samples.Integration.Editor
{
    /// <summary>
    /// 一次性验收设置：为导入的示例副本创建唯一一份 YooAsset 采集设置。
    /// <para>由 <c>Tools/verify-integration-sample.ps1</c> 复制到临时 Editor 目录后以 batchmode
    /// <c>-executeMethod</c> 调用；运行结束后脚本删除临时目录与这份设置资产。</para>
    /// </summary>
    public static class IntegrationVerifySetup
    {
        private const string SettingPath = "Assets/AssetBundleCollectorSetting.asset";
        private const string CollectPath = "Assets/Samples/Integration";

        /// <summary>创建采集设置；已存在任一份则失败（YooAsset 不允许多份）。</summary>
        public static void CreateCollectorSetting()
        {
            if (File.Exists(SettingPath) ||
                AssetDatabase.LoadAssetAtPath<AssetBundleCollectorSetting>(SettingPath) != null)
            {
                throw new System.InvalidOperationException(
                    $"已存在采集设置：{SettingPath}。验收脚本不会覆盖已有配置。");
            }

            if (!AssetDatabase.IsValidFolder(CollectPath))
                throw new System.InvalidOperationException($"找不到导入副本：{CollectPath}");

            var setting = ScriptableObject.CreateInstance<AssetBundleCollectorSetting>();

            var package = new AssetBundleCollectorPackage
            {
                PackageName = "DefaultPackage",
                PackageDesc = "Integration probe (temporary)",
                EnableAddressable = true,
            };

            var group = new AssetBundleCollectorGroup
            {
                GroupName = "Integration",
            };

            group.Collectors.Add(new AssetBundleCollector
            {
                CollectPath = CollectPath,
                CollectorGUID = AssetDatabase.AssetPathToGUID(CollectPath),
                CollectorType = ECollectorType.MainAssetCollector,
                AddressRuleName = nameof(AddressByFileName),
                PackRuleName = nameof(PackDirectory),
                FilterRuleName = nameof(CollectAll),
            });

            package.Groups.Add(group);
            setting.Packages.Add(package);

            AssetDatabase.CreateAsset(setting, SettingPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[IntegrationVerifySetup] 临时采集设置已创建。");
        }
    }
}
