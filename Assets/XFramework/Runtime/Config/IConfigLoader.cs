using Cysharp.Threading.Tasks;

namespace XFramework.XConfig
{
    /// <summary>
    /// 配置加载器统一接口，由 XFramework 内部 Loader 实现。
    /// <para>第三方可自行实现此接口，配合 <see cref="ConfigFormat"/> 自定义枚举值扩展新的配置格式。</para>
    /// </summary>
    public interface IConfigLoader
    {
        /// <summary>加载 Table 类型的配置数据（多行，按 Id 索引）。</summary>
        /// <typeparam name="T">实现 <see cref="IConfigRow{TKey}"/> 的配置行类型。</typeparam>
        /// <typeparam name="TKey">配置行主键类型。</typeparam>
        /// <param name="assetPath">资源路径。</param>
        /// <returns>按 Id 索引的字典。</returns>
        UniTask<System.Collections.Generic.Dictionary<TKey, T>> LoadTableAsync<T, TKey>(string assetPath)
            where T : IConfigRow<TKey>, new();

        /// <summary>加载 Global 类型的配置数据（单份，全局唯一）。</summary>
        /// <typeparam name="T">配置类型，必须为 class。</typeparam>
        /// <param name="assetPath">资源路径。</param>
        /// <returns>配置单例对象。</returns>
        UniTask<T> LoadGlobalAsync<T>(string assetPath)
            where T : class, new();
    }
}