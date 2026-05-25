using System;
using System.Collections.Generic;
using System.Reflection;

namespace XFramework.XConfig
{
    /// <summary>
    /// 从 <see cref="IConfigRow{TKey}"/> 实现中提取主键类型 <typeparamref name="TKey"/> 的工具类。
    /// <para>提取结果按行类型缓存，每表仅反射一次。内部使用。</para>
    /// </summary>
    internal static class ConfigTypeHelper
    {
        /// <summary>
        /// rowType → keyType 缓存，避免多次反射。
        /// </summary>
        private static readonly Dictionary<Type, Type> KeyTypeCache = new();

        /// <summary>
        /// 从实现 <see cref="IConfigRow{TKey}"/> 的类型中提取 TKey。
        /// <para>当前泛型实例化会按 T 分别触发此方法（仅一次），后续查缓存零开销。</para>
        /// </summary>
        internal static Type GetKeyType(Type rowType)
        {
            if (!KeyTypeCache.TryGetValue(rowType, out var keyType))
            {
                keyType = ExtractKeyType(rowType);
                KeyTypeCache[rowType] = keyType;
            }
            return keyType;
        }

        private static Type ExtractKeyType(Type rowType)
        {
            // 遍历接口查找 IConfigRow<T>
            foreach (var iface in rowType.GetInterfaces())
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IConfigRow<>))
                {
                    return iface.GetGenericArguments()[0];
                }
            }

            // 检查基类是否继承 IConfigRow<T>
            var baseType = rowType.BaseType;
            while (baseType != null && baseType != typeof(object))
            {
                foreach (var iface in baseType.GetInterfaces())
                {
                    if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IConfigRow<>))
                    {
                        var key = iface.GetGenericArguments()[0];
                        KeyTypeCache[rowType] = key;
                        return key;
                    }
                }
                baseType = baseType.BaseType;
            }

            throw new ConfigException(
                $"Type '{rowType.FullName}' does not implement IConfigRow<TKey>. " +
                $"All Table row types must implement IConfigRow<TKey> (e.g., IConfigRow<int>, IConfigRow<string>).");
        }
    }
}