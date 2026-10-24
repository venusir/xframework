using System;
using System.Collections.Generic;

namespace XFramework.XConfig.Luban
{
    /// <summary>
    /// Luban 生成代码与 XFramework 之间的适配器契约。
    /// <para>本接口由 Editor 工具生成（或使用方手写），<b>包本身不引用任何 Luban 程序集</b>：
    /// Luban 类型只出现在生成适配器所在的使用方程序集里。</para>
    /// <para>生命周期：运行时先读全数据，再在主线程调用 <see cref="CreateTables(Func{string, byte[]})"/>，
    /// 随后调用 <see cref="RegisterToConfigManager(object)"/>；销毁时调用
    /// <see cref="UnregisterFromConfigManager"/>。所有成员都应在主线程被调用。</para>
    /// </summary>
    public interface ILubanConfigAdapter
    {
        /// <summary>
        /// 数据文件扩展名，含前导点（例如 <c>".bytes"</c> 或 <c>".json"</c>）。
        /// </summary>
        string DataFileExtension { get; }

        /// <summary>
        /// 需要加载的表逻辑名（Luban 的 <c>output_data_file</c>，例如 <c>"demo_tbitem"</c>），
        /// 不带目录与扩展名。运行时按此列表逐个构造 location。
        /// </summary>
        IReadOnlyList<string> TableNames { get; }

        /// <summary>
        /// 用已读入内存的数据构造整包 <c>Tables</c> 对象。
        /// <para><paramref name="loadBytes"/> 按 <see cref="TableNames"/> 中的逻辑名返回数据字节；
        /// 数据必须在调用本方法前全部就绪（Luban 的 loader 是同步委托）。</para>
        /// </summary>
        /// <param name="loadBytes">逻辑名 → 数据字节。</param>
        /// <returns>生成的 <c>Tables</c> 实例；不得为 null。</returns>
        object CreateTables(Func<string, byte[]> loadBytes);

        /// <summary>
        /// 把整包 <c>Tables</c> 中的表注册到 <see cref="ConfigManager"/>。
        /// <para>map 表注册为 Table；单行表注册为 Global；带索引的 list 表从 DataList 重建后注册；
        /// 纯列表表无主键，跳过。</para>
        /// </summary>
        /// <param name="tables"><see cref="CreateTables(Func{string, byte[]})"/> 的返回值。</param>
        void RegisterToConfigManager(object tables);

        /// <summary>
        /// 从 <see cref="ConfigManager"/> 卸载本适配器注册过的表。
        /// <para>只应在 <see cref="RegisterToConfigManager(object)"/> 成功后调用；
        /// 若 ConfigManager 已被销毁，调用方会跳过本方法。</para>
        /// </summary>
        void UnregisterFromConfigManager();
    }
}
