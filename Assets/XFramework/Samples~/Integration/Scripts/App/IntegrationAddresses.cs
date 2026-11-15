namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 示例用到的全部 YooAsset 地址（唯一真相）。
    /// <para>所有资源文件名都带 <c>Integration.</c> 前缀，配合收集器的
    /// <c>AddressByFileName</c> 规则（地址 = 文件名去扩展名），因此这里的常量与磁盘上的
    /// 文件名一一对应。新增资源时先加常量，再放同名文件。</para>
    /// </summary>
    public static class IntegrationAddresses
    {
        /// <summary>主菜单面板预制体。</summary>
        public const string MainMenu = "Integration.MainMenu";

        /// <summary>设置面板预制体。</summary>
        public const string Settings = "Integration.Settings";

        /// <summary>语言面板预制体。</summary>
        public const string Language = "Integration.Language";

        /// <summary>确认对话框预制体。</summary>
        public const string Confirm = "Integration.Confirm";

        /// <summary>模式选择面板预制体。</summary>
        public const string ModeSelect = "Integration.ModeSelect";

        /// <summary>玩法面板预制体。</summary>
        public const string GamePanel = "Integration.GamePanel";

        /// <summary>结算面板预制体。</summary>
        public const string Result = "Integration.Result";

        /// <summary>暂停面板预制体。</summary>
        public const string Pause = "Integration.Pause";

        /// <summary>加载面板预制体。</summary>
        public const string Loading = "Integration.Loading";

        /// <summary>游戏场景（Additive 加载）。</summary>
        public const string GameScene = "Integration.Game";

        /// <summary>模式表（Config JSON 表）。</summary>
        public const string GameModes = "Integration.GameModes";

        /// <summary>全局规则（Config JSON 对象）。</summary>
        public const string Rules = "Integration.Rules";

        /// <summary>示例自己的启动配置资产（场景直接引用；收集器按文件名给它地址）。</summary>
        public const string BootstrapProfile = "Integration.BootstrapProfile";

        /// <summary>背景音乐。</summary>
        public const string Bgm = "Integration.Bgm";

        /// <summary>按钮点击音。</summary>
        public const string Click = "Integration.Click";

        /// <summary>命中/成功音。</summary>
        public const string Correct = "Integration.Correct";

        /// <summary>失误音。</summary>
        public const string Wrong = "Integration.Wrong";

        /// <summary>
        /// 语言表地址模板。<c>{0}</c> 为语言标识（如 <c>zh_Hans</c> / <c>en</c>）。
        /// <para>交给 <c>LocalizationManager.LanguageAssetPath</c>，切换语言时由框架按模板拼地址。</para>
        /// </summary>
        public const string LanguagePathFormat = "Integration.Lang_{0}";
    }
}
