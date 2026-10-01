namespace XFramework.XLog
{
    /// <summary>
    /// 框架内置的日志分类。**新增框架日志时来这里加一行**，不要在调用点裸写字符串。
    /// <para><b>它同时是第三方「框架有哪些分类」的发现入口</b>：想单独关掉某模块的诊断日志，用这里的句柄调
    /// <c>LogManager.SetCategoryLevel</c>；想加自己的分类，用 <see cref="LogCategory.Get"/>。</para>
    /// <para><b>分类名与迁移前的 <c>[前缀]</c> 一一对应</b>（逐字保留），因此同一模块内历史遗留的多个标签
    /// 各是一个分类：<c>Config</c> 与 <c>ConfigManager</c> 是两个，<c>AssetManager</c> 与
    /// <c>YooAssetManager</c> 是两个。<b>这是有意的</b>——迁移承诺控制台文本逐字不变。</para>
    /// <para>分类名是纯字符串，本类<b>不引用任何模块</b>——「日志模块零模块依赖」因此成立；
    /// 给模块 X 加分类只需在这里追加一行。</para>
    /// <para><b>不含</b>两类名字：Pool 的泛型标签（<c>Pool&lt;T&gt;</c> 等，由模块内的泛型静态缓存注册，
    /// 见 <c>Pool</c> 模块）与使用方自己的分类（Samples 与第三方各自注册）。</para>
    /// </summary>
    public static class LogCategories
    {
        #region 框架核心与 Editor 工具

        /// <summary>Editor 工具（依赖安装器等）自身的日志。</summary>
        public static readonly LogCategory XFramework = LogCategory.Get("XFramework");

        #endregion

        #region Asset / Audio / Bootstrap

        /// <summary>资源管理器门面与实现。</summary>
        public static readonly LogCategory AssetManager = LogCategory.Get("AssetManager");

        /// <summary>YooAsset 适配层——与 <see cref="AssetManager"/> 分开是有意的：信息量更大，也便于单独排查。</summary>
        public static readonly LogCategory YooAssetManager = LogCategory.Get("YooAssetManager");

        /// <summary>音频模块。</summary>
        public static readonly LogCategory Audio = LogCategory.Get("Audio");

        /// <summary>引导流程（登记表与管线装配）。</summary>
        public static readonly LogCategory Bootstrap = LogCategory.Get("Bootstrap");

        /// <summary>游戏启动入口。</summary>
        public static readonly LogCategory GameLauncher = LogCategory.Get("GameLauncher");

        #endregion

        #region Config / Data / Save / Settings

        /// <summary>配置模块实现。</summary>
        public static readonly LogCategory Config = LogCategory.Get("Config");

        /// <summary>配置模块门面。</summary>
        public static readonly LogCategory ConfigManager = LogCategory.Get("ConfigManager");

        /// <summary>数据模块。</summary>
        public static readonly LogCategory Data = LogCategory.Get("Data");

        /// <summary>存档模块。</summary>
        public static readonly LogCategory Save = LogCategory.Get("Save");

        /// <summary>设置模块。</summary>
        public static readonly LogCategory SettingsManager = LogCategory.Get("SettingsManager");

        #endregion

        #region Event / File / Input / Localization

        /// <summary>事件引擎。</summary>
        public static readonly LogCategory Event = LogCategory.Get("Event");

        /// <summary>文件模块。</summary>
        public static readonly LogCategory FileManager = LogCategory.Get("FileManager");

        /// <summary>输入模块。</summary>
        public static readonly LogCategory Input = LogCategory.Get("Input");

        /// <summary>本地化模块门面。</summary>
        public static readonly LogCategory LocalizationManager = LogCategory.Get("LocalizationManager");

        /// <summary>本地化引导阶段。</summary>
        public static readonly LogCategory LocalizationBootstrapStage = LogCategory.Get("LocalizationBootstrapStage");

        /// <summary>语言资源加载器。</summary>
        public static readonly LogCategory LanguageAssetLoader = LogCategory.Get("LanguageAssetLoader");

        #endregion

        #region Lock / Message / Pipeline / Pool / Reactive / Serialize

        /// <summary>锁模块。</summary>
        public static readonly LogCategory Lock = LogCategory.Get("Lock");

        /// <summary>消息模块。</summary>
        public static readonly LogCategory Message = LogCategory.Get("Message");

        /// <summary>管线基础设施。</summary>
        public static readonly LogCategory Pipeline = LogCategory.Get("Pipeline");

        /// <summary>对象池管理器（泛型池 <c>Pool&lt;T&gt;</c> 等由 Pool 模块的泛型缓存注册，不在此列）。</summary>
        public static readonly LogCategory PoolManager = LogCategory.Get("PoolManager");

        /// <summary>StringBuilder 池。</summary>
        public static readonly LogCategory StringBuilderPool = LogCategory.Get("StringBuilderPool");

        /// <summary>响应式属性。</summary>
        public static readonly LogCategory Reactive = LogCategory.Get("Reactive");

        /// <summary>序列化模块。</summary>
        public static readonly LogCategory XSerialize = LogCategory.Get("XSerialize");

        #endregion

        #region Timer / UI / Update

        /// <summary>定时器模块。</summary>
        public static readonly LogCategory Timer = LogCategory.Get("Timer");

        /// <summary>UI 模块门面与实现。</summary>
        public static readonly LogCategory UIManager = LogCategory.Get("UIManager");

        /// <summary>HUD 管理器。</summary>
        public static readonly LogCategory UIHudManager = LogCategory.Get("UIHudManager");

        /// <summary>默认面板控制器。</summary>
        public static readonly LogCategory UIDefaultController = LogCategory.Get("UIDefaultController");

        /// <summary>面板基类。</summary>
        public static readonly LogCategory UIPanelBase = LogCategory.Get("UIPanelBase");

        /// <summary>面板绑定。</summary>
        public static readonly LogCategory UIPanelBinding = LogCategory.Get("UIPanelBinding");

        /// <summary>提示管理器。</summary>
        public static readonly LogCategory UITipManager = LogCategory.Get("UITipManager");

        /// <summary>提示条目。</summary>
        public static readonly LogCategory UITipItem = LogCategory.Get("UITipItem");

        /// <summary>更新调度器。</summary>
        public static readonly LogCategory UpdateScheduler = LogCategory.Get("UpdateScheduler");

        /// <summary>Update 模块门面。</summary>
        public static readonly LogCategory Update = LogCategory.Get("Update");

        #endregion
    }
}
