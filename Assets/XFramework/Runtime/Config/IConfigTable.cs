using System.Collections;

namespace XFramework.XConfig
{
    /// <summary>
    /// <see cref="ConfigTable{T}"/> 的非泛型接口，暴露内部字典供框架反射路径访问。
    /// <para>第三方无需实现此接口，仅在通过 <see cref="IConfigManager.RegisterTable(System.Type, IConfigTable)"/> 反射注入时使用。</para>
    /// </summary>
    public interface IConfigTable
    {
        /// <summary>内部字典（Dictionary{TKey, T}，存储为 <see cref="IDictionary"/>）。</summary>
        IDictionary Data { get; }
    }
}