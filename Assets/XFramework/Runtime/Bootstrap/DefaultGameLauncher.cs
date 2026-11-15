using UnityEngine;

namespace XFramework.XBootstrap
{

    /// <summary>
    /// 开箱默认的启动器：<see cref="GameLauncher"/> 的生命周期底座 + 一个 Profile 资产 + 场景 UI 根。
    /// <para><b>三种用法</b>：① 直接挂本组件（零配置起步——默认三件 Asset / Data / Save + UI Root 非空时的
    /// UI 阶段）；② 填 <c>_profile</c>（Asset / Save / Input / Localization 的数据值从资产来，可做环境变体）；
    /// ③ 继承本类覆写 <see cref="ConfigureStages"/>——服务实例与自定义阶段从这里进（Profile 只装数据）。
    /// <b>要完全自控</b>（Inspector 里一个字段都不要）请改继承 <see cref="GameLauncher"/> 并实现
    /// <see cref="ConfigureStages"/>。</para>
    /// <para><b>破坏性迁移（2026-11-15）</b>：原先 7 个 Inspector 数据字段已移入 <see cref="BootstrapProfile"/>；
    /// 旧场景的值不会自动迁移——新建 Profile 资产、复制旧值、赋给 <c>_profile</c>。字段面现在是
    /// <c>_profile</c> + <c>_uiRoot</c> 两个。</para>
    /// </summary>
    public class DefaultGameLauncher : GameLauncher
    {
        #region Configuration

        [Tooltip("启动配置资产：Asset 包名/运行模式/低内存回收、Save 版本、Input 配置资产、默认语言与语言表。" +
                 "留空 = 零配置（默认三件 + UI Root 非空时的 UI 阶段）。" +
                 "服务实例（RemoteServices / CryptoProvider / 自定义 provider）装不进 Profile，请覆写 ConfigureStages。")]
        [SerializeField] private BootstrapProfile _profile;

        [Header("UI")]
        [Tooltip("UI 根节点（场景里的 Canvas 根）。留空则不登记 UI 引导阶段——" +
                 "用 UIRootNode 自动初始化，或自行调 UIManager.Initialize。")]
        [SerializeField] private Transform _uiRoot;

        #endregion

        #region Extension Points

        /// <summary>
        /// 登记启动阶段。默认实现分两条路径：
        /// <list type="bullet">
        /// <item><b><c>_profile</c> 非空</b>：交给 <see cref="BootstrapStages.FromProfile"/>——显式登记
        /// Asset / Data / Save，再按值条件登记 UI / Input / Localization。</item>
        /// <item><b><c>_profile</c> 为空</b>：<see cref="Bootstrap.RegisterDefaults"/>（Asset / Data / Save），
        /// 再加 <c>_uiRoot</c> 非空时的 UI 阶段——与不填任何字段时一致。</item>
        /// </list>
        /// <para><b>覆写</b>：调 <c>base.ConfigureStages()</c> 之后追加或替换（服务实例类配置在这里注入，
        /// 例：先 <see cref="Bootstrap.Unregister{T}()"/> 掉内置的 Asset 阶段，再
        /// <see cref="Bootstrap.Register(IBootstrapStage)"/> 一个带 <c>RemoteServices</c> / <c>DecryptionServices</c> 的）；
        /// 不调 <c>base</c> 则完全不登记默认组合，登记什么自己说了算。</para>
        /// <para><b>外部组件的 Awake 不适合作替换点</b>：本组件在自己的 Awake 里无条件登记默认阶段，
        /// 与其它组件 Awake 的先后由 Unity 决定；外部先跑时会被本组件稍后的登记覆盖或形成同相位双份。
        /// 要替换内置阶段，请放在本覆写里（与 <c>base</c> 同一调用栈，顺序确定）。</para>
        /// <para>时机：<c>Awake</c> 内、<c>Start</c> 跑管线之前。登记先后不影响执行顺序
        /// （执行序由相位决定），但整张表必须在 <see cref="Bootstrap.RunAsync"/> 之前定稿。</para>
        /// </summary>
        protected override void ConfigureStages()
        {
            if (_profile != null)
            {
                BootstrapStages.FromProfile(_profile, _uiRoot);
                return;
            }

            // 零配置路径：默认三件 + 场景 UI 根非空时的 UI 阶段。
            // Input / Localization 只有 Profile 或子类能提供——它们需要资产/语言数据，不属于零配置默认组合。
            Bootstrap.RegisterDefaults();

            if (_uiRoot != null)
            {
                Bootstrap.Register(BootstrapStages.UI(_uiRoot));
            }
        }

        #endregion
    }
}
