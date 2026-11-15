using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace XFramework.DevTools
{
    /// <summary>
    /// 仓库内开发用的 YooAsset 采集设置工具。
    /// <para><b>为什么放在包外</b>：<c>AssetBundleCollectorSetting</c> 是项目级资产，且本工具只服务本仓
    /// 的开发回路；<c>Assets/DevTools/</c> 不随 UPM 包（<c>?path=Assets/XFramework</c>）发布。</para>
    /// <para>两条路径：<see cref="CreateCollectorSetting"/> 是验收脚本的严格版（已存在则拒绝）；
    /// <see cref="EnsureCollectorSetting"/> 是菜单的幂等版（复用已有配置，只补示例所需的采集器）。</para>
    /// </summary>
    public static class IntegrationCollectorSetup
    {
        #region Constants

        private const string SettingPath = "Assets/AssetBundleCollectorSetting.asset";
        private const string CollectPath = "Assets/Samples/Integration";
        private const string DevGroupName = "IntegrationDev";
        private const string DevPackageDesc = "XFramework dev (temporary)";
        private const string DefaultPackageName = "DefaultPackage";

        #endregion

        #region Public API

        /// <summary>创建采集设置；已存在任一份则失败（YooAsset 全工程只允许一份）。</summary>
        /// <remarks>供 <c>Tools/verify-integration-sample.ps1</c> 的 batchmode <c>-executeMethod</c> 调用。</remarks>
        public static void CreateCollectorSetting()
        {
            if (File.Exists(SettingPath) ||
                AssetDatabase.LoadAssetAtPath<AssetBundleCollectorSetting>(SettingPath) != null)
            {
                throw new InvalidOperationException(
                    $"已存在采集设置：{SettingPath}。验收脚本不会覆盖已有配置。");
            }

            if (!AssetDatabase.IsValidFolder(CollectPath))
                throw new InvalidOperationException($"找不到导入副本：{CollectPath}");

            var setting = ScriptableObject.CreateInstance<AssetBundleCollectorSetting>();

            var package = new AssetBundleCollectorPackage
            {
                PackageName = DefaultPackageName,
                PackageDesc = DevPackageDesc,
                EnableAddressable = true,
            };

            var group = new AssetBundleCollectorGroup
            {
                GroupName = DevGroupName,
            };

            group.Collectors.Add(CreateCollector());
            package.Groups.Add(group);
            setting.Packages.Add(package);

            AssetDatabase.CreateAsset(setting, SettingPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[XFramework DevTools] 临时采集设置已创建。");
        }

        /// <summary>幂等地保证采集设置里存在指向 Integration 导入副本的采集器。</summary>
        /// <remarks>菜单导入示例后调用：已有设置则复用，只补/校验示例所需的采集器与地址规则。</remarks>
        public static void EnsureCollectorSetting()
        {
            if (!AssetDatabase.IsValidFolder(CollectPath))
                throw new InvalidOperationException($"找不到导入副本：{CollectPath}。请先导入 Integration 示例。");

            var setting = AssetDatabase.LoadAssetAtPath<AssetBundleCollectorSetting>(SettingPath);
            if (setting == null)
            {
                setting = ScriptableObject.CreateInstance<AssetBundleCollectorSetting>();
                AssetDatabase.CreateAsset(setting, SettingPath);
            }

            var package = FindPackage(setting, DefaultPackageName);
            if (package == null)
            {
                package = new AssetBundleCollectorPackage
                {
                    PackageName = DefaultPackageName,
                    PackageDesc = DevPackageDesc,
                    EnableAddressable = true,
                };
                setting.Packages.Add(package);
            }
            else if (!package.EnableAddressable)
            {
                package.EnableAddressable = true;
            }

            foreach (var group in package.Groups)
            {
                foreach (var collector in group.Collectors)
                {
                    if (!string.Equals(collector.CollectPath, CollectPath, StringComparison.Ordinal))
                        continue;

                    if (!IsDevCollector(collector))
                    {
                        Debug.LogWarning(
                            $"[XFramework DevTools] {CollectPath} 已有一个规则不同的采集器；" +
                            "沿用现有规则，若地址解析失败请检查 Address/Pack/Filter Rule。");
                    }

                    EditorUtility.SetDirty(setting);
                    AssetDatabase.SaveAssets();
                    return;
                }
            }

            var devGroup = FindGroup(package, DevGroupName);
            if (devGroup == null)
            {
                devGroup = new AssetBundleCollectorGroup { GroupName = DevGroupName };
                package.Groups.Add(devGroup);
            }

            devGroup.Collectors.Add(CreateCollector());
            EditorUtility.SetDirty(setting);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[XFramework DevTools] 已为 Integration 示例补齐 YooAsset 采集器。");
        }

        /// <summary>移除本工具创建的开发采集组；不碰用户已有的其它采集配置。</summary>
        public static void RemoveDevCollectorGroup()
        {
            var setting = AssetDatabase.LoadAssetAtPath<AssetBundleCollectorSetting>(SettingPath);
            if (setting == null)
                return;

            for (int i = setting.Packages.Count - 1; i >= 0; i--)
            {
                var package = setting.Packages[i];

                for (int j = package.Groups.Count - 1; j >= 0; j--)
                {
                    if (package.Groups[j].GroupName == DevGroupName)
                        package.Groups.RemoveAt(j);
                }

                // 只删本工具创建、且已被清空的包；有其它组的包原样保留。
                if (package.PackageDesc == DevPackageDesc && package.Groups.Count == 0)
                    setting.Packages.RemoveAt(i);
            }

            if (setting.Packages.Count == 0)
            {
                AssetDatabase.DeleteAsset(SettingPath);
            }
            else
            {
                EditorUtility.SetDirty(setting);
                AssetDatabase.SaveAssets();
            }

            AssetDatabase.Refresh();
            Debug.Log("[XFramework DevTools] 已移除开发采集组。");
        }

        #endregion

        #region Helpers

        private static AssetBundleCollector CreateCollector()
        {
            return new AssetBundleCollector
            {
                CollectPath = CollectPath,
                CollectorGUID = AssetDatabase.AssetPathToGUID(CollectPath),
                CollectorType = ECollectorType.MainAssetCollector,
                AddressRuleName = nameof(AddressByFileName),
                PackRuleName = nameof(PackDirectory),
                FilterRuleName = nameof(CollectAll),
            };
        }

        private static bool IsDevCollector(AssetBundleCollector collector)
        {
            return collector.CollectorType == ECollectorType.MainAssetCollector
                   && collector.AddressRuleName == nameof(AddressByFileName)
                   && collector.PackRuleName == nameof(PackDirectory)
                   && collector.FilterRuleName == nameof(CollectAll);
        }

        private static AssetBundleCollectorPackage FindPackage(AssetBundleCollectorSetting setting, string name)
        {
            foreach (var package in setting.Packages)
            {
                if (package.PackageName == name)
                    return package;
            }

            return null;
        }

        private static AssetBundleCollectorGroup FindGroup(AssetBundleCollectorPackage package, string name)
        {
            foreach (var group in package.Groups)
            {
                if (group.GroupName == name)
                    return group;
            }

            return null;
        }

        #endregion
    }
}
