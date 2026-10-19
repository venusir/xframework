using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XAsset;

namespace XFramework.XConfig
{
    /// <summary>
    /// ScriptableObject 格式配置加载器。
    /// <para>通过 <see cref="AssetManager"/> 加载 SO 资源。</para>
    /// <para>Table 类型：资产必须继承 <see cref="ScriptableObjectTable{TRow,TKey}"/>
    /// （它是公开基类，承载行列表）；普通 <see cref="ScriptableObject"/> 会得到明确的
    /// <see cref="ConfigException"/>，而不是「AssetManager 未初始化」的误归因。</para>
    /// <para>Global 类型：直接加载目标类型的 SO。</para>
    /// </summary>
    internal sealed class ScriptableObjectLoader : IConfigLoader
    {
        #region IConfigLoader

        async UniTask<ConfigTable<T>> IConfigLoader.LoadTableAsync<T, TKey>(string assetPath)
        {
            // 按最宽类型加载再自检：直接按 ScriptableObjectTable<T,TKey> 加载时，普通 SO 会
            // 在 AssetManager 侧表现为「资源不存在/类型不匹配」，错误消息把排查方向带偏（R3-CONF-002）。
            var asset = await LoadSOAsync<ScriptableObject>(assetPath);
            return BuildTable<T, TKey>(asset, assetPath);
        }

        async UniTask<T> IConfigLoader.LoadGlobalAsync<T>(string assetPath)
        {
            var obj = await LoadSOAsync<ScriptableObject>(assetPath);
            if (obj is T config)
                return config;
            throw new ConfigException(
                $"Failed to load Global SO config '{typeof(T).Name}' from '{assetPath}': loaded object is not of expected type.");
        }

        #endregion

        #region Internal

        /// <summary>
        /// 从已加载的 SO 资产构建 Table 并校验形状（测试缝：不依赖 AssetManager）。
        /// </summary>
        internal static ConfigTable<T> BuildTable<T, TKey>(ScriptableObject asset, string assetPath)
            where T : IConfigRow<TKey>
        {
            if (asset is not ScriptableObjectTable<T, TKey> tableAsset)
            {
                throw new ConfigException(
                    $"SO Table asset '{assetPath}' must inherit ScriptableObjectTable<{typeof(T).Name}, {typeof(TKey).Name}>; " +
                    $"got '{asset?.GetType().Name ?? "null"}'.");
            }

            if (tableAsset.Items == null || tableAsset.Items.Count == 0)
                throw new ConfigException(
                    $"SO Table '{typeof(T).Name}' from '{assetPath}' contains no items.");

            var dict = new Dictionary<TKey, T>(tableAsset.Items.Count);
            foreach (var item in tableAsset.Items)
            {
                if (dict.ContainsKey(item.Id))
                    throw new ConfigException(
                        $"Duplicate Id '{item.Id}' found in SO Table '{typeof(T).Name}' from '{assetPath}'.");
                dict[item.Id] = item;
            }
            return new ConfigTable<T>(dict);
        }

        private static async UniTask<T> LoadSOAsync<T>(string assetPath) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(assetPath))
                throw new ConfigException("Asset path cannot be null or empty.");

            var handle = await AssetManager.LoadAsync<T>(assetPath);
            if (handle.Asset == null)
                throw new ConfigException(
                    $"Failed to load SO asset '{assetPath}' for type '{typeof(T).Name}'. Ensure AssetManager is initialized and the asset exists.");
            try
            {
                return handle.Asset;
            }
            finally
            {
                handle.Dispose();
            }
        }

        #endregion
    }

    /// <summary>
    /// SO Table 包装类，承载 <typeparamref name="TRow"/> 列表。
    /// <para>第三方创建 SO 资源时需继承此类，将具体配置行填入 <see cref="Items"/> 列表。</para>
    /// </summary>
    /// <typeparam name="TRow">配置行类型，需实现 <see cref="IConfigRow{TKey}"/>。</typeparam>
    /// <typeparam name="TKey">配置行主键类型。</typeparam>
    public abstract class ScriptableObjectTable<TRow, TKey> : ScriptableObject where TRow : IConfigRow<TKey>
    {
        /// <summary>
        /// 配置行列表。在 Editor 中编辑此列表。
        /// </summary>
        [field: SerializeField]
        public List<TRow> Items { get; private set; }
    }
}