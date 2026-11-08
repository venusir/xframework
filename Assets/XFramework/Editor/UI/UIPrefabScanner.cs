using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace XFramework.Editor.UI
{
    /// <summary>
    /// 扫描指定目录下的 UI 候选预制体并生成报告。
    /// <para>唯一使用 <c>AssetDatabase</c> 的普通代码；地址查询经 <see cref="IUIPrefabAddressProvider"/> 注入，
    /// 因此扫描逻辑可以在 EditMode 测试里用假 provider 驱动。</para>
    /// </summary>
    internal static class UIPrefabScanner
    {
        #region Public

        /// <summary>
        /// 扫描进度回调。
        /// </summary>
        /// <param name="progress">0–1 的进度。</param>
        /// <param name="description">当前处理的对象。</param>
        /// <returns>false 表示请求取消。</returns>
        internal delegate bool ProgressCallback(float progress, string description);

        /// <summary>
        /// 扫描 UI 候选预制体并逐条跑规则。
        /// </summary>
        /// <param name="folders">扫描目录；空集合或 null 时回退到 <c>Assets</c>。选中文件时取其所在目录。</param>
        /// <param name="checkAddresses">是否执行 YooAsset 地址检查。</param>
        /// <param name="addressProvider">地址目录来源；为 null 时跳过地址检查。</param>
        /// <param name="progress">进度回调；返回 false 可取消。</param>
        /// <returns>扫描报告。</returns>
        public static UIPrefabValidationReport Scan(
            IReadOnlyList<string> folders,
            bool checkAddresses,
            IUIPrefabAddressProvider addressProvider,
            ProgressCallback progress = null)
        {
            var report = new UIPrefabValidationReport();
            var catalog = ResolveAddressCatalog(checkAddresses, addressProvider, report);

            var searchFolders = NormalizeFolders(folders);
            var guids = AssetDatabase.FindAssets("t:Prefab", searchFolders);
            var paths = new List<string>(guids?.Length ?? 0);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (guids != null)
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (!string.IsNullOrEmpty(path) && seen.Add(path))
                        paths.Add(path);
                }
            }

            paths.Sort(StringComparer.OrdinalIgnoreCase);

            var total = paths.Count;
            var completed = 0;
            var cancelled = false;

            for (int i = 0; i < total; i++)
            {
                var path = paths[i];

                if (progress != null
                    && !progress(total == 0 ? 1f : completed / (float)total, path))
                {
                    report.AddNotice(
                        UIPrefabIssueSeverity.Warning,
                        $"扫描已取消（已处理 {completed}/{total} 个预制体）。");
                    cancelled = true;
                    break;
                }

                completed++;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    continue;

                var assetType = PrefabUtility.GetPrefabAssetType(prefab);
                if (assetType == PrefabAssetType.Model
                    || assetType == PrefabAssetType.MissingAsset
                    || assetType == PrefabAssetType.NotAPrefab)
                {
                    continue;
                }

                if (!UIPrefabCandidateDetector.IsCandidate(prefab))
                    continue;

                var entry = new UIPrefabValidationEntry(path, AssetDatabase.AssetPathToGUID(path));

                try
                {
                    IReadOnlyList<UIPrefabAddressEntry> addresses = null;
                    if (catalog.IsAvailable)
                        catalog.TryGet(entry.AssetGuid, entry.AssetPath, out addresses);

                    if (addresses != null)
                        entry.Addresses.AddRange(addresses);

                    var issues = UIPrefabValidator.Validate(prefab, addresses, catalog.IsAvailable);
                    entry.Issues.AddRange(issues);
                }
                catch (Exception e)
                {
                    report.AddNotice(
                        UIPrefabIssueSeverity.Error,
                        $"校验 '{path}' 时抛出异常，已跳过该预制体：{e.Message}");
                }

                report.AddEntry(entry);
            }

            if (!cancelled && progress != null)
                progress(1f, "完成");

            report.SortEntries();
            return report;
        }

        /// <summary>
        /// 执行地址查询并把配置状态写入报告通知。抽成独立方法便于测试四种状态。
        /// </summary>
        /// <param name="checkAddresses">是否启用地址检查。</param>
        /// <param name="addressProvider">地址目录来源。</param>
        /// <param name="report">目标报告。</param>
        /// <returns>地址目录；不可用时 <see cref="UIPrefabAddressCatalog.IsAvailable"/> 为 false。</returns>
        internal static UIPrefabAddressCatalog ResolveAddressCatalog(
            bool checkAddresses,
            IUIPrefabAddressProvider addressProvider,
            UIPrefabValidationReport report)
        {
            if (!checkAddresses)
            {
                report.AddNotice(UIPrefabIssueSeverity.Info, "地址检查未启用，已跳过 YooAsset 采集校验。");
                return UIPrefabAddressCatalog.NotConfigured("地址检查未启用。");
            }

            if (addressProvider == null)
            {
                report.AddNotice(UIPrefabIssueSeverity.Warning, "没有提供地址目录来源，已跳过 YooAsset 采集校验。");
                return UIPrefabAddressCatalog.NotConfigured("没有提供地址目录来源。");
            }

            UIPrefabAddressCatalog catalog;
            try
            {
                catalog = addressProvider.Query();
            }
            catch (Exception e)
            {
                report.AddNotice(
                    UIPrefabIssueSeverity.Error,
                    $"读取地址目录时抛出异常，已跳过 YooAsset 采集校验：{e.Message}");
                return UIPrefabAddressCatalog.Failed(e.Message);
            }

            if (catalog == null)
            {
                report.AddNotice(UIPrefabIssueSeverity.Error, "地址目录来源返回了 null，已跳过 YooAsset 采集校验。");
                return UIPrefabAddressCatalog.Failed("地址目录来源返回 null。");
            }

            switch (catalog.Status)
            {
                case UIPrefabAddressStatus.NotConfigured:
                    report.AddNotice(UIPrefabIssueSeverity.Info, catalog.Message);
                    break;

                case UIPrefabAddressStatus.MultipleConfigs:
                case UIPrefabAddressStatus.Failed:
                    report.AddNotice(UIPrefabIssueSeverity.Error, catalog.Message);
                    break;
            }

            return catalog;
        }

        #endregion

        #region Helpers

        private static string[] NormalizeFolders(IReadOnlyList<string> folders)
        {
            var result = new List<string>(folders?.Count ?? 1);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (folders != null)
            {
                for (int i = 0; i < folders.Count; i++)
                {
                    var folder = NormalizeFolder(folders[i]);
                    if (!string.IsNullOrEmpty(folder) && seen.Add(folder))
                        result.Add(folder);
                }
            }

            if (result.Count == 0)
                result.Add("Assets");

            return result.ToArray();
        }

        private static string NormalizeFolder(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var normalized = path.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(normalized))
                return normalized;

            // 选中的是文件时取所在目录；只对非法目录走这一步，避免每个文件夹都去加载资产。
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(normalized) == null)
                return null;

            var directory = Path.GetDirectoryName(normalized);
            if (string.IsNullOrEmpty(directory))
                return null;

            directory = directory.Replace('\\', '/');
            return AssetDatabase.IsValidFolder(directory) ? directory : null;
        }

        #endregion
    }
}
