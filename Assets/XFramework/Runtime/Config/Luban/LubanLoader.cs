using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using XFramework.XAsset;

namespace XFramework.XConfig
{
    /// <summary>
    /// Luban 二进制格式配置加载器。
    /// <para>加载 Luban 生成的完整 Tables 实例，通过反射提取各 Tb 表的 DataList，
    /// 逐行写入 <see cref="ConfigManagerImpl"/> 的 _tables 字典。</para>
    /// <para>以独立程序集存在：如果第三方项目不使用 Luban，删除 <c>Config/Luban/</c> 目录即可。</para>
    /// <para>使用前需在项目中安装 Luban 工具链并使用 XFramework 提供的模板生成实现 <see cref="IConfigRow"/> 的类型。</para>
    /// </summary>
    public sealed class LubanLoader : IConfigLoader
    {
        #region Constants

        /// <summary>Luban ByteBuf 类型名。</summary>
        private const string ByteBufTypeName = "Bright.Serialization.ByteBuf";

        #endregion

        #region IConfigLoader — Table / Global（标记为不支持）


        UniTask<Dictionary<int, T>> IConfigLoader.LoadTableAsync<T>(string assetPath)
        {
            throw new ConfigException(
                $"{nameof(LubanLoader)} is designed to load full Luban Tables via {nameof(IConfigLoader.LoadTablesAsync)}. " +
                $"Use {nameof(ConfigManager)}.LoadAsync{{TTables}}(...) instead of PreloadTableAsync with {nameof(ConfigFormat)}.Luban.");
        }

        UniTask<T> IConfigLoader.LoadGlobalAsync<T>(string assetPath)
        {
            throw new ConfigException(
                $"{nameof(LubanLoader)} does not support Global config loading. " +
                $"Use {nameof(ConfigFormat)}.Json or {nameof(ConfigFormat)}.ScriptableObject for single Global configs.");
        }

        #endregion

        #region IConfigLoader — Tables

        /// <summary>
        /// 加载 Luban 生成的 Tables 完整配置。
        /// </summary>
        /// <typeparam name="TTables">Luban 生成的 Tables 类型。</typeparam>
        /// <param name="assetPath">Tables 二进制文件的资源路径。</param>
        /// <returns>Key = Row 类型, Value = Dictionary<int, Row> 的中间结果。</returns>
        async UniTask<Dictionary<Type, object>> IConfigLoader.LoadTablesAsync<TTables>(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                throw new ConfigException("Asset path cannot be null or empty for Luban Tables.");

            var bytes = await LoadBytesAsync(assetPath);
            object byteBuf;
            try
            {
                byteBuf = CreateByteBuf(bytes);
            }
            catch (ConfigException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ConfigException(
                    $"Failed to create Luban ByteBuf from '{assetPath}': {ex.Message}. " +
                    "Ensure Bright.Serialization is referenced and the asset is a valid Luban binary.", ex);
            }

            // 构造 Tables 实例：Luban 原生反序列化（含嵌套 bean ✅）
            TTables tables;
            try
            {
                tables = (TTables)Activator.CreateInstance(typeof(TTables), new object[] { byteBuf });
            }
            catch (Exception ex)
            {
                throw new ConfigException(
                    $"Failed to construct Luban Tables '{typeof(TTables).Name}' from '{assetPath}': {ex.Message}. " +
                    "Ensure the Tables type has a constructor accepting ByteBuf and the binary data is compatible.", ex);
            }

            if (tables == null)
                throw new ConfigException(
                    $"Luban Tables '{typeof(TTables).Name}' from '{assetPath}' constructed to null.");

            // 反射遍历 Tables 的所有属性，提取每张 Tb 表
            var result = new Dictionary<Type, object>();
            var tablesType = typeof(TTables);
            var properties = tablesType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                // 跳过非表属性（如索引器、string 类型等）
                if (prop.PropertyType.IsGenericType)
                    continue; // 通常是 List<Something> 之类，跳过
                if (prop.PropertyType == typeof(string))
                    continue;

                var tbInstance = prop.GetValue(tables);
                if (tbInstance == null)
                    continue;

                var tbType = tbInstance.GetType();

                // 获取 TbXxx 类型的 DataList 属性（List<RowType> 或 IReadOnlyList<RowType>）
                var dataListProp = tbType.GetProperty("DataList", BindingFlags.Public | BindingFlags.Instance);
                if (dataListProp == null)
                    continue;

                var dataList = dataListProp.GetValue(tbInstance);
                if (dataList == null)
                    continue;

                // 检查是否为 IEnumerable（List / IReadOnlyList 都实现）
                if (dataList is not System.Collections.IEnumerable enumerable)
                    continue;

                // 获取 Row 类型
                Type rowType = null;
                var listType = dataList.GetType();
                if (listType.IsGenericType)
                {
                    var genericArgs = listType.GetGenericArguments();
                    if (genericArgs.Length == 1)
                        rowType = genericArgs[0];
                }

                if (rowType == null)
                    continue;

                // 仅处理实现了 IConfigRow 的 row 类型（模板注入后必定实现）
                if (!typeof(IConfigRow).IsAssignableFrom(rowType))
                    continue;

                // 构建 Dictionary<int, T>
                var idProp = rowType.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
                if (idProp == null)
                    continue;

                var dictType = typeof(Dictionary<,>).MakeGenericType(typeof(int), rowType);
                var dict = (System.Collections.IDictionary)Activator.CreateInstance(dictType);
                if (dict == null)
                    continue;

                foreach (var item in enumerable)
                {
                    if (item == null)
                        continue;
                    var rowId = (int)idProp.GetValue(item);
                    dict[rowId] = item;
                }

                result[rowType] = dict;
            }

            return result;
        }

        #endregion

        #region Internal — ByteBuf Reflection

        /// <summary>
        /// 通过反射创建 Luban 的 <c>ByteBuf</c> 实例。
        /// </summary>
        private static object CreateByteBuf(byte[] data)
        {
            var byteBufType = Type.GetType($"{ByteBufTypeName}, Bright.Serialization");
            if (byteBufType == null)
                throw new ConfigException(
                    $"Luban ByteBuf type '{ByteBufTypeName}' not found. " +
                    "Please install the Luban package ('Bright.Serialization') to use ConfigFormat.Luban. See README for details.");

            try
            {
                return Activator.CreateInstance(byteBufType, new object[] { data });
            }
            catch (Exception ex)
            {
                throw new ConfigException(
                    $"Failed to create Luban ByteBuf: {ex.Message}. " +
                    "Ensure Luban is installed and the ByteBuf constructor accepts byte[].", ex);
            }
        }

        #endregion

        #region Internal — Load Bytes

        private static async UniTask<byte[]> LoadBytesAsync(string assetPath)
        {
            var handle = await AssetManager.LoadAsync<UnityEngine.TextAsset>(assetPath);
            if (handle.Asset == null)
                throw new ConfigException(
                    $"Failed to load binary asset '{assetPath}'. " +
                    "Ensure AssetManager is initialized and the .bytes file exists in the YooAsset package.");

            try
            {
                var bytes = handle.Asset.bytes;
                if (bytes == null || bytes.Length == 0)
                    throw new ConfigException(
                        $"Loaded asset '{assetPath}' contains zero bytes.");
                return bytes;
            }
            finally
            {
                handle.Dispose();
            }
        }

        #endregion
    }
}