namespace XFramework.XSettings
{
    /// <summary>
    /// 设置管理器选项。形态对齐 <c>SaveOptions</c>：<c>sealed class</c> + 在 XML 注释里写明陷阱。
    /// </summary>
    public sealed class SettingsOptions
    {
        /// <summary>
        /// 持久化格式版本。
        /// <para><b>默认 0 表示不启用版本化</b>，落盘内容就是设置对象本身，JSON 最干净
        /// （<c>{"audio":{"masterVolume":0.5}}</c>）。</para>
        /// <para>设为 <c>&gt;= 1</c> 后落盘内容变为版本信封
        /// <c>{"Version":1,"Data":{...}}</c>，加载时可经 <see cref="ISettingsMigrator{T}"/> 迁移旧版本，
        /// 并对「数据版本高于本版本」整份拒绝。</para>
        /// <para><b>陷阱：</b>启用版本化会改变落盘格式，此前按无版本格式写下的文件将无法被识别
        /// （解析出的信封载荷为空），会回退默认值。切换前后需自行处理存量数据。</para>
        /// </summary>
        public int CurrentVersion;
    }
}
