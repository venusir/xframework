using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework.XConfig
{
    /// <summary>
    /// JSON 格式配置加载器。
    /// <para>通过 <see cref="XFramework.XAsset.AssetManager"/> 加载 TextAsset，使用 <see cref="JsonUtility"/> 反序列化。</para>
    /// <para>同时支持 Table（JSON 数组）和 Global（单个 JSON 对象）。</para>
    /// </summary>
    internal sealed class JsonLoader : IConfigLoader
    {
        #region IConfigLoader

        async UniTask<ConfigTable<T>> IConfigLoader.LoadTableAsync<T, TKey>(string assetPath)
        {
            var text = await LoadTextAsync(assetPath);
            return ParseTable<T, TKey>(text, assetPath);
        }

        async UniTask<T> IConfigLoader.LoadGlobalAsync<T>(string assetPath)
        {
            var text = await LoadTextAsync(assetPath);
            return ParseGlobal<T>(text, assetPath);
        }

        #endregion

        #region Internal — 解析段（CONF-012）

        /// <summary>
        /// 把 JSON 文本解析成 <see cref="ConfigTable{T}"/>。
        /// <para><b>为什么抽出来</b>（照 <see cref="CsvLoader.ParseTable{T, TKey}"/> 先例）：测试进程里
        /// <c>AssetManager</c> 从未初始化，真实 Loader 路径会在 <c>ConfigTextLoader</c> 抛错——解析段与
        /// 资源加载解耦后才能直接单测；JSON 正路径此前正是零覆盖，CONF-001 因此长期存活。</para>
        /// </summary>
        internal static ConfigTable<T> ParseTable<T, TKey>(string text, string assetPath)
            where T : IConfigRow<TKey>, new()
        {
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
                return new ConfigTable<T>(dict);
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

        /// <summary>把 JSON 文本解析成 Global 配置对象（解析段，CONF-012）。</summary>
        internal static T ParseGlobal<T>(string text, string assetPath) where T : class, new()
        {
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

        /// <summary>
        /// 经 <see cref="XFramework.XAsset.AssetManager"/> 加载 TextAsset 并返回文本内容(共享助手 <see cref="ConfigTextLoader"/>)。</summary>
        private static UniTask<string> LoadTextAsync(string assetPath)
        {
            return ConfigTextLoader.LoadTextAsync(assetPath);
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
            // 反射填充：赋值发生在 JsonUtility.FromJson 内部，编译器看不到 → CS0649 是假阳性
#pragma warning disable CS0649
            public List<T> Items;
#pragma warning restore CS0649
        }

        #endregion
    }
}