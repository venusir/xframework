using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;

namespace XFramework.XConfig
{
    /// <summary>
    /// CSV 格式配置加载器。
    /// <para>第一行为表头（字段名），后续行为数据。</para>
    /// <para>通过反射对行类型 <typeparamref name="T"/> 的公共字段/属性顺序赋值，
    ///     仅限基础类型（int/float/string/bool/enum），不做递归子对象反序列化。</para>
    /// <para>主键由 <see cref="IConfigRow{TKey}.Id"/> 自动提取，
    ///     CSV 中不需要显式包含 Id 列（若包含则同样被赋值）。</para>
    /// </summary>
    internal sealed class CsvLoader : IConfigLoader
    {
        #region IConfigLoader

        async UniTask<ConfigTable<T>> IConfigLoader.LoadTableAsync<T, TKey>(string assetPath)
        {
            var text = await LoadTextAsync(assetPath);
            try
            {
                var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length < 2)
                    throw new ConfigException(
                        $"CSV file '{assetPath}' must contain at least a header row and one data row.");

                var headers = lines[0].Split(',');
                var members = MapColumns(headers, GetColumnMap(typeof(T)), typeof(T), assetPath);

                var dict = new Dictionary<TKey, T>(lines.Length - 1);
                for (int i = 1; i < lines.Length; i++)
                {
                    var values = lines[i].Split(',');
                    if (values.Length != headers.Length)
                        throw new ConfigException(
                            $"Row {i} in CSV '{assetPath}' has {values.Length} columns, expected {headers.Length}.");

                    var item = new T();
                    for (int j = 0; j < members.Length; j++)
                    {
                        SetMemberValue(members[j], item, values[j], assetPath, i + 1, headers[j]);
                    }
                    var key = item.Id;
                    if (dict.ContainsKey(key))
                        throw new ConfigException(
                            $"Duplicate Id '{key}' found in Table '{typeof(T).Name}' from '{assetPath}'.");
                    dict[key] = item;
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
                    $"Failed to parse CSV Table '{typeof(T).Name}' from '{assetPath}': {ex.Message}", ex);
            }
        }

        async UniTask<T> IConfigLoader.LoadGlobalAsync<T>(string assetPath)
        {
            var text = await LoadTextAsync(assetPath);
            try
            {
                var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 0)
                    throw new ConfigException(
                        $"CSV file '{assetPath}' is empty.");

                var config = new T();
                var headers = lines[0].Split(',');
                var members = MapColumns(headers, GetColumnMap(typeof(T)), typeof(T), assetPath);

                // Global：取最后一行数据（与 Table 不同，Global 只有一个实例）
                var dataLine = lines[lines.Length - 1];
                var values = dataLine.Split(',');
                if (values.Length != headers.Length)
                    throw new ConfigException(
                        $"CSV row in '{assetPath}' has {values.Length} columns, expected {headers.Length} (header of '{typeof(T).Name}').");

                for (int j = 0; j < members.Length; j++)
                {
                    SetMemberValue(members[j], config, values[j], assetPath, lines.Length, headers[j]);
                }
                return config;
            }
            catch (ConfigException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ConfigException(
                    $"Failed to parse CSV Global config '{typeof(T).Name}' from '{assetPath}': {ex.Message}", ex);
            }
        }

        #endregion

        #region Internal

        /// <summary>
        /// 经 <see cref="AssetManager"/> 加载 TextAsset 并返回文本内容(共享助手 <see cref="ConfigTextLoader"/>)。</summary>
        private static UniTask<string> LoadTextAsync(string assetPath)
        {
            return ConfigTextLoader.LoadTextAsync(assetPath);
        }

        /// <summary>
        /// 按列名索引类型 T 的公共可写成员（字段和属性），结果按类型缓存。
        /// <para>C# 同一类型内字段与属性不可同名，名称索引无冲突；仅基础类型可直接赋值，不递归处理子对象。</para>
        /// </summary>
        private static Dictionary<string, IWriteableMember> GetColumnMap(Type type)
        {
            if (ColumnMapCache.TryGetValue(type, out var map))
                return map;

            var dict = new Dictionary<string, IWriteableMember>();
            // 获取公共可写属性（跳过索引器）；IConfigRow.Id 等隐式接口实现属性同样收录，header 含 Id 列时赋值
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length > 0) continue;
                if (!prop.CanWrite) continue;
                if (prop.SetMethod == null) continue;
                dict[prop.Name] = new PropertyMember(prop);
            }
            // 获取公共字段
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                dict[field.Name] = new FieldMember(field);
            }
            ColumnMapCache[type] = dict;
            return dict;
        }

        /// <summary>列名 → 可写成员映射缓存（每行类型仅反射一次）。</summary>
        private static readonly Dictionary<Type, Dictionary<string, IWriteableMember>> ColumnMapCache = new();

        /// <summary>
        /// 将 header 列名映射为成员列表（按 header 顺序）。
        /// <para>列名在类型中不存在或 header 重复时抛 <see cref="ConfigException"/>，明确暴露配置/类型不一致。</para>
        /// </summary>
        private static IWriteableMember[] MapColumns(
            string[] headers, Dictionary<string, IWriteableMember> columnMap, Type rowType, string assetPath)
        {
            var members = new IWriteableMember[headers.Length];
            var seen = new HashSet<string>();
            for (int j = 0; j < headers.Length; j++)
            {
                var col = headers[j];
                if (!seen.Add(col))
                    throw new ConfigException(
                        $"Duplicate column '{col}' in CSV '{assetPath}'.");
                if (!columnMap.TryGetValue(col, out var member))
                    throw new ConfigException(
                        $"Column '{col}' in CSV '{assetPath}' does not exist on type '{rowType.Name}'. " +
                        $"Available columns: [{string.Join(", ", columnMap.Keys)}].");
                members[j] = member;
            }
            return members;
        }

        /// <summary>
        /// 将 CSV 单元格值设置到目标对象的指定成员上。
        /// <para>空单元格赋类型默认值（CSV 空列常见）；非空解析失败抛 <see cref="ConfigException"/>，
        /// 消息带路径/行号/列名/原始值，避免配置错误被静默吞掉。</para>
        /// </summary>
        private static void SetMemberValue(IWriteableMember member, object target, string rawValue,
            string assetPath, int row, string columnName)
        {
            var memberType = member.Type;
            object value;

            if (string.IsNullOrEmpty(rawValue))
            {
                // 空单元格：赋类型默认值
                value = memberType.IsValueType ? Activator.CreateInstance(memberType) : null;
            }
            else if (memberType == typeof(int))
            {
                if (!int.TryParse(rawValue, out var i))
                    throw ParseError(assetPath, row, columnName, rawValue, memberType);
                value = i;
            }
            else if (memberType == typeof(float))
            {
                if (!float.TryParse(rawValue, out var f))
                    throw ParseError(assetPath, row, columnName, rawValue, memberType);
                value = f;
            }
            else if (memberType == typeof(double))
            {
                if (!double.TryParse(rawValue, out var d))
                    throw ParseError(assetPath, row, columnName, rawValue, memberType);
                value = d;
            }
            else if (memberType == typeof(bool))
            {
                if (!bool.TryParse(rawValue, out var b))
                    throw ParseError(assetPath, row, columnName, rawValue, memberType);
                value = b;
            }
            else if (memberType == typeof(string))
            {
                value = rawValue;
            }
            else if (memberType.IsEnum)
            {
                try
                {
                    value = Enum.Parse(memberType, rawValue, false);
                }
                catch
                {
                    throw ParseError(assetPath, row, columnName, rawValue, memberType);
                }
            }
            else
            {
                // 不支持的类型：明确报错而非静默赋默认值，暴露数据/类型定义问题
                throw new ConfigException(
                    $"CSV column '{columnName}' in '{assetPath}' row {row} targets unsupported member type '{memberType.Name}'.");
            }

            member.SetValue(target, value);
        }

        /// <summary>构造解析失败异常（带路径/行号/列名/原始值上下文）。</summary>
        private static ConfigException ParseError(
            string assetPath, int row, string columnName, string rawValue, Type memberType)
        {
            return new ConfigException(
                $"CSV parse failed in '{assetPath}' row {row}, column '{columnName}': " +
                $"cannot convert '{rawValue}' to {memberType.Name}.");
        }

        #endregion

        #region Nested Types

        /// <summary>
        /// 可写成员的抽象，统一处理字段和属性。
        /// </summary>
        private interface IWriteableMember
        {
            Type Type { get; }
            void SetValue(object target, object value);
        }

        private readonly struct FieldMember : IWriteableMember
        {
            private readonly System.Reflection.FieldInfo _field;
            public Type Type => _field.FieldType;
            public FieldMember(System.Reflection.FieldInfo field) => _field = field;
            public void SetValue(object target, object value) => _field.SetValue(target, value);
        }

        private readonly struct PropertyMember : IWriteableMember
        {
            private readonly System.Reflection.PropertyInfo _prop;
            public Type Type => _prop.PropertyType;
            public PropertyMember(System.Reflection.PropertyInfo prop) => _prop = prop;
            public void SetValue(object target, object value) => _prop.SetValue(target, value);
        }

        #endregion
    }
}