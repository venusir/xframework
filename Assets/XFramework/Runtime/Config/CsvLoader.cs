using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XAsset;

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
                var members = GetWriteableMembers(typeof(T));
                if (members.Length != headers.Length)
                    throw new ConfigException(
                        $"CSV header count ({headers.Length}) does not match field/property count ({members.Length}) " +
                        $"of type '{typeof(T).Name}'. Headers: [{string.Join(", ", headers)}]");

                var dict = new Dictionary<TKey, T>(lines.Length - 1);
                for (int i = 1; i < lines.Length; i++)
                {
                    var values = lines[i].Split(',');
                    if (values.Length != members.Length)
                        throw new ConfigException(
                            $"Row {i} in CSV '{assetPath}' has {values.Length} columns, expected {members.Length}.");

                    var item = new T();
                    for (int j = 0; j < members.Length; j++)
                    {
                        SetMemberValue(members[j], item, values[j]);
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
                var members = GetWriteableMembers(typeof(T));

                // Global：取最后一行数据（与 Table 不同，Global 只有一个实例）
                var dataLine = lines[lines.Length - 1];
                var values = dataLine.Split(',');
                if (values.Length != members.Length)
                    throw new ConfigException(
                        $"CSV row in '{assetPath}' has {values.Length} columns, expected {members.Length} (fields of '{typeof(T).Name}').");

                for (int j = 0; j < members.Length; j++)
                {
                    SetMemberValue(members[j], config, values[j]);
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

        /// <summary>
        /// 获取类型 T 的公共可写成员（字段和属性），按声明顺序排列。
        /// <para>仅基础类型可直接赋值，不递归处理子对象。</para>
        /// </summary>
        private static IWriteableMember[] GetWriteableMembers(Type type)
        {
            var list = new List<IWriteableMember>();

            // 获取公共可写属性（PlatformNotSupportedException-proof：逐个 TryGet）
            foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                // 跳过索引器
                if (prop.GetIndexParameters().Length > 0) continue;
                if (!prop.CanWrite) continue;
                if (prop.SetMethod == null) continue;
                // IConfigRow.Id 已经被属性处理器处理，但这里保留以维持列顺序
                list.Add(new PropertyMember(prop));
            }

            // 获取公共字段
            foreach (var field in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                list.Add(new FieldMember(field));
            }

            return list.ToArray();
        }

        /// <summary>
        /// 将 CSV 单元格值设置到目标对象的指定成员上。
        /// <para>尝试将单元格原始字符串基于成员数据类型转成对应的对象值。</para>
        /// </summary>
        private static void SetMemberValue(IWriteableMember member, object target, string rawValue)
        {
            var memberType = member.Type;
            object value;

            if (memberType == typeof(int))
                value = int.TryParse(rawValue, out var i) ? i : 0;
            else if (memberType == typeof(float))
                value = float.TryParse(rawValue, out var f) ? f : 0f;
            else if (memberType == typeof(double))
                value = double.TryParse(rawValue, out var d) ? d : 0.0;
            else if (memberType == typeof(bool))
                value = bool.TryParse(rawValue, out var b) && b;
            else if (memberType == typeof(string))
                value = rawValue ?? string.Empty;
            else if (memberType.IsEnum)
            {
                try
                {
                    value = Enum.Parse(memberType, rawValue, false);
                }
                catch
                {
                    value = 0;
                }
            }
            else
            {
                // 不支持的类型，赋默认值
                value = memberType.IsValueType ? Activator.CreateInstance(memberType) : null;
            }

            member.SetValue(target, value);
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