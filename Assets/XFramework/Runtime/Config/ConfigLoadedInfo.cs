using System;

namespace XFramework.XConfig
{
    /// <summary>
    /// 一项已加载配置（诊断用只读快照）。
    /// <para><b>回答什么问题</b>：这个项目到底加载了哪些配置表/全局配置、各自多少行、从哪个资源路径来的。
    /// 此前只有「按类型问」的 <c>IsLoaded&lt;T&gt;()</c>——不知道类型就问不出来。</para>
    /// <para><b>怎么取</b>：<see cref="ConfigManager.CopyLoaded(System.Collections.Generic.List{ConfigLoadedInfo})"/>。
    /// 只对内置实现成立：整体替换 <see cref="IConfigManager"/> 后回读为空。</para>
    /// </summary>
    public readonly struct ConfigLoadedInfo
    {
        /// <summary>配置类型（表的行类型，或全局配置的类型）。</summary>
        public readonly Type ConfigType;

        /// <summary>加载种类。</summary>
        public readonly ConfigLoadKind Kind;

        /// <summary>条目数：表是行数，全局配置恒为 1。</summary>
        public readonly int EntryCount;

        /// <summary>
        /// 首次加载时使用的资源路径；<b>可能为 null</b>——经 <c>RegisterTable</c> / <c>RegisterGlobal</c>
        /// 直接登记的配置没有经过加载路径。
        /// </summary>
        public readonly string AssetPath;

        internal ConfigLoadedInfo(Type configType, ConfigLoadKind kind, int entryCount, string assetPath)
        {
            ConfigType = configType;
            Kind = kind;
            EntryCount = entryCount;
            AssetPath = assetPath;
        }

        /// <summary>单行摘要，形如 <c>ItemConfig(表, 128 行)</c>。</summary>
        public override string ToString()
            => $"{ConfigType?.Name}({Kind}, {EntryCount} 条)";
    }
}
