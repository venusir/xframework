using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XAsset;

namespace XFramework.XConfig
{
    /// <summary>
    /// 文本配置加载共享助手,供 JsonLoader / CsvLoader 复用。
    /// </summary>
    internal static class ConfigTextLoader
    {
        /// <summary>
        /// 经 <see cref="AssetManager"/> 加载 TextAsset 并返回文本内容。
        /// <para>资源加载失败或文本为空时抛 <see cref="ConfigException"/>。</para>
        /// </summary>
        internal static UniTask<string> LoadTextAsync(string assetPath)
        {
            return LoadTextAsync(assetPath, null);
        }

        /// <summary>
        /// 可注入句柄视图的重载（测试缝，见 <see cref="IConfigAssetLease{T}"/>）。
        /// </summary>
        internal static async UniTask<string> LoadTextAsync(
            string assetPath,
            Func<string, UniTask<IConfigAssetLease<TextAsset>>> leaseLoader)
        {
            if (string.IsNullOrEmpty(assetPath))
                throw new ConfigException("Asset path cannot be null or empty.");

            var lease = leaseLoader != null
                ? await leaseLoader(assetPath)
                : new AssetHandleLease<TextAsset>(await AssetManager.LoadAsync<TextAsset>(assetPath));

            using (lease)
            {
                if (lease.Asset == null)
                    throw ConfigAssetError.Missing("text", nameof(TextAsset), lease, assetPath);

                var text = lease.Asset.text;
                if (string.IsNullOrEmpty(text))
                    throw new ConfigException($"Loaded asset '{assetPath}' contains empty text content.");
                return text;
            }
        }
    }
}
