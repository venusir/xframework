using System.Collections;

namespace XFramework.XConfig
{
    /// <summary>
    /// <see cref="ConfigTable{T}"/> 的非泛型接口，暴露内部字典供框架反射路径访问。
    /// <para>第三方无需实现此接口，仅在通过 <see cref="IConfigManager.RegisterTable(System.Type, IConfigTable)"/> 反射注入时使用。</para>
    /// </summary>
    public interface IConfigTable
    {
        /// <summary>
        /// 内部字典（<c>Dictionary&lt;TKey, T&gt;</c>，存储为 <see cref="IDictionary"/>）。
        /// <para><b>只读使用</b>：返回的是可变引用（零反射注入路径的代价）。修改它会使
        /// <c>Count</c> / <c>GetAll</c>（构造期快照）与 <c>Get</c> / <c>TryGet</c>（实时字典）
        /// 互相脱钩——查到的行数与按主键查到的内容将不再对应。</para>
        /// </summary>
        IDictionary Data { get; }
    }
}