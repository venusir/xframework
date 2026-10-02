namespace XFramework.XConfig
{
    /// <summary>
    /// <see cref="ConfigLoadedInfo"/> 的加载种类：表（多行、有主键）还是全局单例配置。
    /// </summary>
    public enum ConfigLoadKind
    {
        /// <summary>配置表：有主键类型，按行存储。</summary>
        Table = 0,

        /// <summary>全局配置：整个类型一个实例。</summary>
        Global = 1,
    }
}
