using System;

namespace XFramework.XConfig.Luban
{
    /// <summary>
    /// Luban 配置加载选项。
    /// </summary>
    public sealed class LubanOptions
    {
        /// <summary>
        /// 数据资源根路径（AssetManager location 前缀）。默认 <c>"configs"</c>。
        /// <para>默认 location 规则为 <c>&lt;DataRoot&gt;/&lt;表名&gt;&lt;扩展名&gt;</c>，
        /// 例如 <c>configs/demo_tbitem.bytes</c>。该路径必须与使用方的 YooAsset collector 地址一致。</para>
        /// <para><see cref="DataRoot"/> 原样参与拼接：不归一化反斜杠、首斜杠或空白。
        /// 路径规则特殊时请改用 <see cref="LocationResolver"/>。</para>
        /// </summary>
        public string DataRoot { get; set; } = "configs";

        /// <summary>
        /// 数据加载方式。默认 <see cref="LubanDataLoadMode.TextAsset"/>。
        /// </summary>
        public LubanDataLoadMode LoadMode { get; set; } = LubanDataLoadMode.TextAsset;

        /// <summary>
        /// 自定义 location 解析器：输入表逻辑名，返回完整 location。
        /// <para>为 null 时使用 <see cref="DataRoot"/> + 表名 + 扩展名的默认规则。
        /// 用于 collector 地址规则与默认约定不一致的项目。</para>
        /// </summary>
        public Func<string, string> LocationResolver { get; set; }
    }
}
