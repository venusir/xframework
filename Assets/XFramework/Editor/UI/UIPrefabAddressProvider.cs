using System;
using System.Collections.Generic;

namespace XFramework.Editor.UI
{
    /// <summary>
    /// 地址检查状态。
    /// </summary>
    internal enum UIPrefabAddressStatus
    {
        /// <summary>采集配置可用，地址目录已建立。</summary>
        Available,

        /// <summary>工程内没有 YooAsset 采集配置，地址检查被跳过。</summary>
        NotConfigured,

        /// <summary>工程内有多份采集配置，无法判断该用哪一份。</summary>
        MultipleConfigs,

        /// <summary>采集过程抛异常，地址检查未完成。</summary>
        Failed,
    }

    /// <summary>
    /// 预制体地址目录的来源。真实实现见 <see cref="YooAssetUIPrefabAddressProvider"/>；测试注入假实现。
    /// </summary>
    internal interface IUIPrefabAddressProvider
    {
        /// <summary>
        /// 查询「可按地址加载」的预制体目录。
        /// </summary>
        UIPrefabAddressCatalog Query();
    }

    /// <summary>
    /// 一个「可按地址加载」的预制体采集结果。
    /// </summary>
    internal sealed class UIPrefabAddressEntry
    {
        /// <summary>
        /// 创建一个采集结果。
        /// </summary>
        /// <param name="packageName">YooAsset 包裹名。</param>
        /// <param name="address">可寻址地址；关闭可寻址时为空。</param>
        /// <param name="assetPath">资源路径。</param>
        /// <param name="assetGuid">资源 GUID。</param>
        public UIPrefabAddressEntry(string packageName, string address, string assetPath, string assetGuid)
        {
            PackageName = packageName ?? string.Empty;
            Address = address ?? string.Empty;
            AssetPath = NormalizePath(assetPath);
            AssetGuid = assetGuid ?? string.Empty;
        }

        /// <summary>YooAsset 包裹名。</summary>
        public string PackageName { get; }

        /// <summary>可寻址地址；关闭可寻址时为空，此时仍可按 <see cref="AssetPath"/> 加载。</summary>
        public string Address { get; }

        /// <summary>资源路径，统一使用正斜杠。</summary>
        public string AssetPath { get; }

        /// <summary>资源 GUID。</summary>
        public string AssetGuid { get; }

        /// <summary>是否有可寻址地址。</summary>
        public bool HasAddress => !string.IsNullOrEmpty(Address);

        /// <summary>把路径统一为正斜杠形式。</summary>
        /// <param name="path">原始路径。</param>
        /// <returns>正斜杠路径；输入为空时返回空字符串。</returns>
        public static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
        }
    }

    /// <summary>
    /// 地址查询结果。
    /// <para><b>真实实现为什么必须绕开 YooAsset 的静态配置门面</b>：YooAsset 的静态配置入口在找不到
    /// <c>AssetBundleCollectorSetting</c> 时会自动创建 <c>Assets/AssetBundleCollectorSetting.asset</c>。
    /// 一个只读的校验器不应在项目里留下资产，因此真实实现只用 <c>AssetDatabase.FindAssets</c>
    /// 定位唯一配置，找不到就返回 <see cref="UIPrefabAddressStatus.NotConfigured"/>。</para>
    /// </summary>
    internal sealed class UIPrefabAddressCatalog
    {
        private readonly Dictionary<string, List<UIPrefabAddressEntry>> _byGuid;
        private readonly Dictionary<string, List<UIPrefabAddressEntry>> _byPath;

        private UIPrefabAddressCatalog(UIPrefabAddressStatus status, string message)
        {
            Status = status;
            Message = message ?? string.Empty;
            _byGuid = new Dictionary<string, List<UIPrefabAddressEntry>>(StringComparer.OrdinalIgnoreCase);
            _byPath = new Dictionary<string, List<UIPrefabAddressEntry>>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>查询状态。</summary>
        public UIPrefabAddressStatus Status { get; }

        /// <summary>状态说明；<see cref="UIPrefabAddressStatus.Available"/> 时为空。</summary>
        public string Message { get; }

        /// <summary>地址目录是否可用；只有可用时才会执行规则 008。</summary>
        public bool IsAvailable => Status == UIPrefabAddressStatus.Available;

        /// <summary>创建一个可用的空目录。</summary>
        public static UIPrefabAddressCatalog CreateAvailable()
        {
            return new UIPrefabAddressCatalog(UIPrefabAddressStatus.Available, string.Empty);
        }

        /// <summary>创建一个「未配置」结果。</summary>
        /// <param name="message">说明。</param>
        public static UIPrefabAddressCatalog NotConfigured(string message)
        {
            return new UIPrefabAddressCatalog(UIPrefabAddressStatus.NotConfigured, message);
        }

        /// <summary>创建一个「多份配置」结果。</summary>
        /// <param name="message">说明。</param>
        public static UIPrefabAddressCatalog MultipleConfigs(string message)
        {
            return new UIPrefabAddressCatalog(UIPrefabAddressStatus.MultipleConfigs, message);
        }

        /// <summary>创建一个「采集失败」结果。</summary>
        /// <param name="message">说明。</param>
        public static UIPrefabAddressCatalog Failed(string message)
        {
            return new UIPrefabAddressCatalog(UIPrefabAddressStatus.Failed, message);
        }

        /// <summary>
        /// 登记一个采集结果。GUID 与路径都会作为键，任一命中即可查到。
        /// </summary>
        /// <param name="entry">采集结果；为 null 时忽略。</param>
        public void Add(UIPrefabAddressEntry entry)
        {
            if (entry == null)
                return;

            if (!string.IsNullOrEmpty(entry.AssetGuid))
                AddTo(_byGuid, entry.AssetGuid, entry);

            if (!string.IsNullOrEmpty(entry.AssetPath))
                AddTo(_byPath, entry.AssetPath, entry);
        }

        /// <summary>
        /// 按 GUID 或资源路径查询采集结果。
        /// </summary>
        /// <param name="assetGuid">资源 GUID，可为空。</param>
        /// <param name="assetPath">资源路径，可为空。</param>
        /// <param name="entries">命中的采集结果；未命中时为空数组。</param>
        /// <returns>是否命中。</returns>
        public bool TryGet(string assetGuid, string assetPath, out IReadOnlyList<UIPrefabAddressEntry> entries)
        {
            if (!string.IsNullOrEmpty(assetGuid)
                && _byGuid.TryGetValue(assetGuid, out var byGuid))
            {
                entries = byGuid;
                return true;
            }

            var normalized = UIPrefabAddressEntry.NormalizePath(assetPath);
            if (!string.IsNullOrEmpty(normalized)
                && _byPath.TryGetValue(normalized, out var byPath))
            {
                entries = byPath;
                return true;
            }

            entries = Array.Empty<UIPrefabAddressEntry>();
            return false;
        }

        private static void AddTo(
            Dictionary<string, List<UIPrefabAddressEntry>> map,
            string key,
            UIPrefabAddressEntry entry)
        {
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<UIPrefabAddressEntry>(1);
                map[key] = list;
            }

            list.Add(entry);
        }
    }
}
