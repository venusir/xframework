using Cysharp.Threading.Tasks;

namespace XFramework.XConfig
{
    /// <summary>
    /// 配置加载器统一接口，由 <see cref="JsonLoader"/>、<see cref="ScriptableObjectLoader"/>、
    /// LubanLoader 分别实现。
    /// <para>虽然公开可见，但不应由第三方直接实现。仅 XFramework 内部各 Loader 可实现此接口。</para>
    /// </summary>
    public interface IConfigLoader
    {
        /// <summary>加载 Table 类型的配置数据（多行，按 Id 索引）。</summary>
        /// <typeparam name="T">实现 <see cref="IConfigRow"/> 的配置行类型。</typeparam>
        /// <param name="assetPath">资源路径。</param>
        /// <returns>按 Id 索引的字典。</returns>
        UniTask<System.Collections.Generic.Dictionary<int, T>> LoadTableAsync<T>(string assetPath)
            where T : IConfigRow, new();

        /// <summary>加载 Global 类型的配置数据（单份，全局唯一）。</summary>
        /// <typeparam name="T">配置类型，必须为 class。</typeparam>
        /// <param name="assetPath">资源路径。</param>
        /// <returns>配置单例对象。</returns>
        UniTask<T> LoadGlobalAsync<T>(string assetPath)
            where T : class, new();

        /// <summary>
        /// 加载 Luban Tables 类型配置，通过反射提取所有表数据。
        /// <para>仅 <see cref="LubanLoader"/> 实现此方法。JSON / ScriptableObject Loader 调用会抛出
        /// <see cref="ConfigException"/>。</para>
        /// </summary>
        /// <typeparam name="TTables">Luban 生成的 Tables 类型。</typeparam>
        /// <param name="assetPath">资源路径。</param>
        /// <returns>
        /// 由框架内部消费的中间结果：Key = 各 Row 类型, Value = Dictionary<int, Row类型>
        /// （以 object 形式存储，由 <see cref="ConfigManagerImpl"/> 写入 _tables 字典）。
        /// </returns>
        UniTask<System.Collections.Generic.Dictionary<System.Type, object>> LoadTablesAsync<TTables>(string assetPath)
            where TTables : class, new();
    }
}