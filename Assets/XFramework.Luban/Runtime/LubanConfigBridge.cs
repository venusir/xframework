using System;
using System.Collections.Generic;
using XFramework.XLog;

namespace XFramework.XConfig.Luban
{
    /// <summary>
    /// 生成适配器调用的注册桥接：把 Luban 已反序列化的表转成 <see cref="ConfigTable{T}"/>
    /// 并注册进 <see cref="ConfigManager"/>。
    /// <para>本类不引用任何 Luban 类型；行类型必须实现 <see cref="IConfigRow{TKey}"/>
    /// （由 Editor 工具生成的行接口实现保证，编译期检查）。</para>
    /// </summary>
    public static class LubanConfigBridge
    {
        /// <summary>
        /// 注册一张 map 表。若 <paramref name="dataMap"/> 的运行期类型就是
        /// <see cref="Dictionary{TKey, TValue}"/>（含派生），直接复用（零拷贝）；否则复制一份。
        /// </summary>
        /// <typeparam name="T">行类型。</typeparam>
        /// <typeparam name="TKey">主键类型。</typeparam>
        /// <param name="dataMap">Luban 表对象的 DataMap。</param>
        /// <exception cref="ArgumentNullException"><paramref name="dataMap"/> 为 null。</exception>
        public static void RegisterTable<T, TKey>(IReadOnlyDictionary<TKey, T> dataMap)
            where T : IConfigRow<TKey>
        {
            if (dataMap == null)
                throw new ArgumentNullException(nameof(dataMap));

            if (dataMap is Dictionary<TKey, T> dictionary)
            {
                ConfigManager.RegisterTable(new ConfigTable<T>(dictionary));
                return;
            }

            LogManager.Info(LubanLog.Category,
                "Table '{0}' DataMap is '{1}'; copying into a Dictionary.",
                typeof(T).Name, dataMap.GetType().Name);

            var copy = new Dictionary<TKey, T>(dataMap.Count);
            foreach (var pair in dataMap)
            {
                copy.Add(pair.Key, pair.Value);
            }

            ConfigManager.RegisterTable(new ConfigTable<T>(copy));
        }

        /// <summary>
        /// 注册一张带索引的 list 表（Luban 的 <c>mode="list"</c> + <c>index</c>）。
        /// <para>这类表没有公开 <c>DataMap</c>，从公开 <c>DataList</c> + 键选择器重建字典。</para>
        /// </summary>
        /// <typeparam name="T">行类型。</typeparam>
        /// <typeparam name="TKey">主键类型（联合索引用 <see cref="ValueTuple"/>）。</typeparam>
        /// <param name="rows">Luban 表对象的 DataList。</param>
        /// <param name="keySelector">从行取主键。</param>
        /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
        /// <exception cref="ArgumentException">主键重复；消息带行类型与重复键。</exception>
        public static void RegisterTable<T, TKey>(IReadOnlyList<T> rows, Func<T, TKey> keySelector)
            where T : IConfigRow<TKey>
        {
            if (rows == null)
                throw new ArgumentNullException(nameof(rows));
            if (keySelector == null)
                throw new ArgumentNullException(nameof(keySelector));

            var dictionary = new Dictionary<TKey, T>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var key = keySelector(row);
                try
                {
                    dictionary.Add(key, row);
                }
                catch (ArgumentException ex)
                {
                    throw new ArgumentException(
                        "[LubanConfig] Table for row type '" + typeof(T).FullName +
                        "' contains a duplicate key '" + key + "'.", ex);
                }
            }

            ConfigManager.RegisterTable(new ConfigTable<T>(dictionary));
        }

        /// <summary>
        /// 注册单行表（Luban 的 <c>mode="one"</c>）为 Global 配置。
        /// </summary>
        /// <typeparam name="T">配置类型。</typeparam>
        /// <param name="config">单行表暴露的 Data。</param>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> 为 null。</exception>
        public static void RegisterGlobal<T>(T config) where T : class
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            ConfigManager.RegisterGlobal(config);
        }

        /// <summary>
        /// 从 <see cref="ConfigManager"/> 卸载指定类型；ConfigManager 未初始化时安全空转。
        /// <para>即使该类型从未加载，也会触发一次 <see cref="ConfigManager.ConfigChanged"/>
        /// （核心 <c>ConfigManager.Unload</c> 的既有语义）。</para>
        /// </summary>
        /// <typeparam name="T">行类型或 Global 类型。</typeparam>
        public static void Unload<T>()
        {
            if (ConfigManager.IsInitialized)
            {
                ConfigManager.Unload<T>();
            }
        }
    }
}
