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
        internal static async UniTask<string> LoadTextAsync(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                throw new ConfigException("Asset path cannot be null or empty.");

            var handle = await AssetManager.LoadAsync<TextAsset>(assetPath);
            if (handle.Asset == null)
                throw new ConfigException(
                    $"Failed to load asset '{assetPath}'. Ensure AssetManager is initialized and the asset exists in the YooAsset package.");
            try
            {
                var text = handle.Asset.text;
                if (string.IsNullOrEmpty(text))
                    throw new ConfigException($"Loaded asset '{assetPath}' contains empty text content.");
                return text;
            }
            finally
            {
                handle.Dispose();
            }
        }
    }
}
