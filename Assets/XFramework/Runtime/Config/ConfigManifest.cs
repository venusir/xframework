using System;
using System.Collections.Generic;

namespace XFramework.XConfig
{
    /// <summary>
    /// 配置加载清单，通过声明式 API 预先描述需要加载的配置及其资源路径和分组。
    /// <para>配合 <see cref="ConfigManager.PreloadGroupAsync"/> / <see cref="ConfigManager.PreloadAllAsync"/> 实现批量加载。</para>
    /// <para>纯 C# 类，不依赖 ScriptableObject，方便版本控制和代码生成。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// var manifest = new ConfigManifest();
    /// manifest.AddTable<ItemRow>("config/items", "Core");
    /// manifest.AddTable<SkillRow>("config/skills", "Combat");
    /// manifest.AddGlobal<GameConfig>("config/game", "Core");
    /// 
    /// // 按分组加载
    /// await ConfigManager.PreloadGroupAsync("Core", manifest);
    /// </code>
    /// </example>
    public sealed class ConfigManifest
    {
        internal readonly List<ConfigManifestEntry> Entries = new();

        /// <summary>
        /// 注册 Table 类型配置。
        /// </summary>
        /// <typeparam name="T">配置行类型，需实现 <see cref="IConfigRow{TKey}"/> 并有无参构造函数。</typeparam>
        /// <param name="assetPath">资源路径（YooAsset 地址）。</param>
        /// <param name="group">分组名（可选），用于分组预加载。</param>
        /// <param name="format">配置格式，默认 <see cref="ConfigFormat.Json"/>。</param>
        public void AddTable<T>(string assetPath, string group = null,
            ConfigFormat format = ConfigFormat.Json)
            where T : IConfigRow, new()
        {
            Entries.Add(new ConfigManifestEntry(typeof(T), assetPath, group, isTable: true, format));
        }

        /// <summary>
        /// 注册 Global 类型配置。
        /// </summary>
        /// <typeparam name="T">配置类型，必须为 class 并有无参构造函数。</typeparam>
        /// <param name="assetPath">资源路径（YooAsset 地址）。</param>
        /// <param name="group">分组名（可选），用于分组预加载。</param>
        public void AddGlobal<T>(string assetPath, string group = null)
            where T : class, new()
        {
            Entries.Add(new ConfigManifestEntry(typeof(T), assetPath, group, isTable: false, ConfigFormat.Json));
        }
    }

    /// <summary>
    /// 清单条目（内部使用）。
    /// </summary>
    internal readonly struct ConfigManifestEntry
    {
        public readonly Type RowType;
        public readonly string AssetPath;
        public readonly string Group;
        public readonly bool IsTable;
        public readonly ConfigFormat Format;

        public ConfigManifestEntry(Type rowType, string assetPath, string group, bool isTable, ConfigFormat format)
        {
            RowType = rowType;
            AssetPath = assetPath;
            Group = group;
            IsTable = isTable;
            Format = format;
        }
    }
}