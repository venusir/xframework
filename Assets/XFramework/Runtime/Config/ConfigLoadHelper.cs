using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;

namespace XFramework.XConfig
{
    /// <summary>
    /// 泛型委托缓存，消除 <c>InvokeLoader</c> 中繁琐的 AsTask/Result 反射调用。
    /// </summary>
    internal static class ConfigLoadHelper
    {
        /// <summary>
        /// (rowType, keyType) → 强类型加载委托的缓存。
        /// </summary>
        private static readonly Dictionary<(Type, Type), Func<IConfigLoader, string, UniTask<object>>> Cache = new();

        /// <summary>
        /// 通过缓存的强类型委托调用 <c>loader.LoadTableAsync<T, TKey></c>，
        /// 返回装箱后的 <c>ConfigTable<T></c>。
        /// </summary>
        internal static async UniTask<object> InvokeAsync(
            IConfigLoader loader, Type rowType, Type keyType, string assetPath)
        {
            if (!Cache.TryGetValue((rowType, keyType), out var func))
            {
                func = CreateDelegate(rowType, keyType);
                Cache[(rowType, keyType)] = func;
            }

            return await func(loader, assetPath);
        }

        /// <summary>
        /// 基于类型参数构造 <c>LoadDelegate<T, TKey>.Delegate</c>。
        /// </summary>
        private static Func<IConfigLoader, string, UniTask<object>> CreateDelegate(Type rowType, Type keyType)
        {
            var delegateType = typeof(LoadDelegate<,>).MakeGenericType(rowType, keyType);
            var field = delegateType.GetField(
                "Delegate", BindingFlags.Static | BindingFlags.NonPublic);
            return (Func<IConfigLoader, string, UniTask<object>>)field.GetValue(null);
        }
    }

    /// <summary>
    /// 泛型静态委托持有类。利用 C# 编译器的原生 <c>await</c> 完成结构体到装箱的转换，
    /// 避免运行时通过 <c>AsTask</c> / <c>Result</c> 属性反射。
    /// </summary>
    /// <typeparam name="T">配置行类型。</typeparam>
    /// <typeparam name="TKey">主键类型。</typeparam>
    internal static class LoadDelegate<T, TKey>
        where T : IConfigRow<TKey>, new()
    {
        /// <summary>
        /// 强类型加载委托。内部通过 <c>WrapAsync</c> 借助编译器的 <c>await</c> 装箱，零额外反射。
        /// </summary>
        internal static readonly Func<IConfigLoader, string, UniTask<object>> Delegate =
            (loader, assetPath) => WrapAsync(loader, assetPath);

        /// <summary>
        /// 调用 <c>LoadTableAsync</c> 并利用编译器自动装箱 <c>ConfigTable<T></c> → <c>object</c>。
        /// </summary>
        private static async UniTask<object> WrapAsync(IConfigLoader loader, string assetPath)
        {
            return await loader.LoadTableAsync<T, TKey>(assetPath);
        }
    }
}