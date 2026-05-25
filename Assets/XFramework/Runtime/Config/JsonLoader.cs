using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XAsset;

namespace XFramework.XConfig
{
    /// <summary>
    /// JSON 格式配置加载器。
    /// <para>通过 <see cref="AssetManager"/> 加载 TextAsset，使用 <see cref="JsonUtility"/> 反序列化。</para>
    /// <para>同时支持 Table（JSON 数组）和 Global（单个 JSON 对象）。</para>
    /// </summary>
    internal sealed class JsonLoader : IConfigLoader
    {
        #region IConfigLoader

        async UniTask<Dictionary<TKey, T>> IConfigLoader.LoadTableAsync<T, TKey>(string assetPath)
        {
            var text = await LoadTextAsync(assetPath);
            try
            {
                var list = JsonUtility.FromJson<ListWrapper<T>>(text);
                if (list?.Items == null)
                    throw new ConfigException(
                        $"Failed to deserialize Table '{typeof(T).Name}' from '{assetPath}': result is null.");
                var dict = new Dictionary<TKey, T>(list.Items.Count);
                foreach (var item in list.Items)
                {
                    if (dict.ContainsKey(item.Id))
                        throw new ConfigException(
                            $"Duplicate Id '{item.Id}' found in Table '{typeof(T).Name}' from '{assetPath}'.");
                    dict[item.Id] = item;
                }
                return dict;
            }
            catch (ConfigException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ConfigException(
                    $"Failed to deserialize JSON Table '{typeof(T).Name}' from '{assetPath}': {ex.Message}", ex);
            }
        }

        async UniTask<T> IConfigLoader.LoadGlobalAsync<T>(string assetPath)
        {
            var text = await LoadTextAsync(assetPath);
            try
            {
                var config = JsonUtility.FromJson<T>(text);
                if (config == null)
                    throw new ConfigException(
                        $"Failed to deserialize Global config '{typeof(T).Name}' from '{assetPath}': result is null.");
                return config;
            }
            catch (ConfigException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ConfigException(
                    $"Failed to deserialize JSON Global config '{typeof(T).Name}' from '{assetPath}': {ex.Message}", ex);
            }
        }

        #endregion

        #region Internal

        private static async UniTask<string> LoadTextAsync(string assetPath)
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

        #endregion

        #region Nested Types

        /// <summary>
        /// JSON 数组包装类，用于 <see cref="JsonUtility.FromJson{T}"/> 解析数组。
        /// <para><c>JsonUtility</c> 不能直接解析顶层数组，需通过包装类。</para>
        /// </summary>
        [Serializable]
        private sealed class ListWrapper<T>
        {
            public List<T> Items;
        }

        #endregion
    }
}