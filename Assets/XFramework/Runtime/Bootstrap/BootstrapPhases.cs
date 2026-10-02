namespace XFramework.XBootstrap
{

    /// <summary>
    /// 框架内置引导阶段的相位号（<c>IPhaseStage.Phase</c> 的取值约定）。
    /// <para>相位语义：同值并行、按值升序串行（见 <c>XFramework.XPipeline</c> 模块 README）。
    /// 使用方自定义阶段从 <see cref="UserStart"/> 起取值；<b>0–89 由框架保留</b>——
    /// 框架新增模块可能占用其中任意值（5–89 目前空闲，但只是现状、不是保留承诺）。</para>
    /// <para>需要精确插进框架阶段之间的场景（如「Asset 之后、Data 之前」），不要赌空闲值：
    /// 用 <see cref="Bootstrap.Unregister{T}()"/> 换掉内置阶段，自己编排那一段顺序。</para>
    /// </summary>
    public static class BootstrapPhases
    {
        /// <summary>资源管理器初始化。最早——本地化、UI 等模块的数据要经 YooAsset 地址加载，必须等它就绪。</summary>
        public const int Asset = 0;

        /// <summary>
        /// 输入模块初始化。晚于 <see cref="Asset"/>（输入资产可能经资源系统加载）。
        /// <para>不在 <see cref="Bootstrap.RegisterDefaults"/> 的组合内——它需要资产；由 <c>GameLauncher</c>
        /// 的字段面或使用方自行构造 <c>InputBootstrapStage</c> 登记。</para>
        /// </summary>
        public const int Input = 1;

        /// <summary>
        /// UI 模块初始化。晚于 <see cref="Asset"/>（面板预制体经地址加载）。
        /// <para>不在 <see cref="Bootstrap.RegisterDefaults"/> 的组合内——它需要场景里的 Canvas 根；由
        /// <c>GameLauncher</c> 的字段面或使用方自行构造 <c>UIBootstrapStage</c> 登记。</para>
        /// </summary>
        public const int UI = 2;

        /// <summary>数据管理器初始化。晚于 <see cref="Asset"/>、早于 <see cref="Save"/>。</summary>
        public const int Data = 3;

        /// <summary>
        /// 存档管理器初始化。硬性晚于 <see cref="Data"/>：恢复扫描要用
        /// <c>DataManager.CreateSnapshot</c> 回滚数据块，这是内置相位里唯一的硬性模块间依赖。
        /// </summary>
        public const int Save = 4;

        /// <summary>本地化数据加载。不在 <see cref="Bootstrap.RegisterDefaults"/> 的组合内，由使用方自行构造并登记。</summary>
        public const int Localization = 90;

        /// <summary>
        /// 使用方自定义阶段的建议起点。
        /// <para>与 <see cref="Localization"/> 同值（同相位并行执行）——不依赖本地化的阶段可直接用；
        /// 依赖本地化已就绪的阶段请取更大的值。</para>
        /// </summary>
        public const int UserStart = 90;
    }
}
