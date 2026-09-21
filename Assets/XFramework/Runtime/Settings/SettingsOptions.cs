namespace XFramework.XSettings
{
    /// <summary>
    /// 设置管理器选项。形态对齐 <c>SaveOptions</c>：<c>sealed class</c> + 在 XML 注释里写明陷阱。
    /// <para><b>本对象的字段在 <c>Initialize</c> 时一次性读取，之后修改不再生效。</b>
    /// 保持可变是为了对象初始化器语法好用，但管理器只取值并快照、不保留引用——因此初始化后
    /// 再改这些字段不会有任何效果（不是「部分生效」那种难以察觉的状态）。理由是其中两项的
    /// 共享后果严重：<see cref="SaveOnQuit"/> 中途翻转会让释放逻辑按与订阅时不同的判据决定是否
    /// 退订，<see cref="CurrentVersion"/> 中途改会让上下半场写出的落盘格式不同。</para>
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
        /// 而它是 MonoBehaviour 消息，静态服务收不到。要覆盖请开 <see cref="SaveOnPause"/>。</para>
        /// </summary>
        public bool SaveOnQuit;

        /// <summary>
        /// 应用切到后台时若仍有未提交改动则写盘（兜底）。默认关闭。
        /// <para><b>补的是 <see cref="SaveOnQuit"/> 最大的那个洞：</b>移动端被切到后台后，系统可能在
        /// 任何时刻直接杀掉进程——此时 <c>OnApplicationQuit</c> 根本不会触发，于是「退出时兜底」
        /// 在这些设备上等于不存在。三方资料里这是移动端最主要的设置丢失路径。</para>
        /// <para>开启后框架会自持一个隐藏的常驻宿主接收 <c>OnApplicationPause</c>（仅本选项开启时创建，
        /// 全部释放后销毁，关闭则零开销）。它刻意不挂到 <c>GameLauncher</c> 上——后者自己的文档写明
        /// 「是可选件、不是框架的必需入口」，把落盘挂在一个可缺席的组件上会让本选项的承诺落空。</para>
        /// <para><b>仅覆盖「进入后台」：</b>恢复前台时不写盘，因为那没有新的丢失风险。</para>
        /// </summary>
        public bool SaveOnPause;
    }
}
