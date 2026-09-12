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

        /// <summary>
        /// 是否启用自动保存（去抖）。默认关闭——本模块的既有取舍是「调用方显式保存」，
        /// 保持默认不写盘、行为可预期。
        /// <para>开启后：<see cref="ISettingsManager{T}.IsDirty"/> 为真、且连续
        /// <see cref="AutoSaveDelay"/> 秒没有新的改动时，自动调用一次 <c>Save</c>。</para>
        /// <para>与 <see cref="SaveOnQuit"/> 建议成对开启，否则退出时最后一段改动仍会丢。</para>
        /// </summary>
        public bool AutoSave;

        /// <summary>
        /// 自动保存的去抖窗口（秒）。仅在 <see cref="AutoSave"/> 开启时有意义。
        /// <para>窗口从<b>最后一次改动</b>起算，因此拖动滑条期间不会写盘，松手静默该时长后写一次。</para>
        /// </summary>
        public float AutoSaveDelay = 0.5f;

        /// <summary>
        /// 应用退出时若仍有未提交改动则写盘（兜底）。默认关闭。
        /// <para><b>无法用 Test Runner 验证：</b>Unity 的 <c>Application.quitting</c> 在编辑器中不触发，
        /// 该行为只能在构建产物里确认。</para>
        /// <para><b>不覆盖的场景：</b>移动端切后台后被系统杀死——那需要 <c>OnApplicationPause</c>，
        /// 框架无法在静态服务里收到该回调，须由业务自行在暂停时调用 <c>Save</c>。</para>
        /// </summary>
        public bool SaveOnQuit;
    }
}
