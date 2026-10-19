using System;
using System.Collections.Generic;
using System.Reflection;
using XFramework.XLog;

namespace XFramework.XConfig
{
    /// <summary>
    /// 从 <see cref="IConfigRow{TKey}"/> 实现中提取主键类型（即接口的 <c>TKey</c>）的工具类。
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
            Type found = null;

            // GetInterfaces() 已包含从基类继承的接口，故无需再单独遍历 BaseType
            // （旧实现的那段循环是死代码，R3-CONF-010）。
            foreach (var iface in rowType.GetInterfaces())
            {
                if (!iface.IsGenericType || iface.GetGenericTypeDefinition() != typeof(IConfigRow<>))
                    continue;

                var candidate = iface.GetGenericArguments()[0];
                if (found == null)
                {
                    found = candidate;
                    continue;
                }

                // 多接口实现时静默取第一个会让主键类型取决于接口枚举顺序（CLR 不保证）；
                // 告警后仍取第一个，与既有行为一致（R3-CONF-010）。
                if (found != candidate)
                {
                    LogManager.Warning(LogCategories.Config,
                        "行类型 '{0}' 实现了多个 IConfigRow<>（{1} 与 {2}），取第一个。",
                        rowType.Name, found, candidate);
                }
            }

            if (found != null)
                return found;

            throw new ConfigException(
                $"Type '{rowType.FullName}' does not implement IConfigRow<TKey>. " +
                $"All Table row types must implement IConfigRow<TKey> (e.g., IConfigRow<int>, IConfigRow<string>).");
        }
    }
}