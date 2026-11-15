using UnityEngine;
using XFramework.XAsset;
using XFramework.XBootstrap;
using XFramework.XData;
using XFramework.XInput;
using XFramework.XLocalization;
using XFramework.XLog;
using XFramework.XSave;
using XFramework.XUI;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 示例启动器：框架只提供 <see cref="GameLauncher"/> 生命周期底座，字段面与装配都由使用方写。
    /// <para><b>Inspector 字段</b>：<c>_profile</c>（<see cref="IntegrationBootstrapProfile"/>——Asset 包名/模式、
    /// Save 版本、Input 配置、默认语言与语言表）与 <c>_uiRoot</c>。</para>
    /// <para>示例刻意不覆写 <c>Start</c>/<c>OnDestroy</c>：启动、取消与逆序清理都由
    /// <see cref="GameLauncher"/> 底座承担；业务流程等 <see cref="Bootstrap.WaitUntilReadyAsync"/>
    /// 的启动就绪信号（不再自造「启动完成」阶段）。</para>
    /// </summary>
    public sealed class IntegrationLauncher : GameLauncher
    {
        #region Configuration

        [Tooltip("示例自己的启动配置资产；替换它可做环境变体（例如 EditorSimulate / Host 两套）。")]
        [SerializeField] private IntegrationBootstrapProfile _profile;

        [Tooltip("UI 根节点（场景里的 Canvas 根）。留空则不登记 UI 阶段。")]
        [SerializeField] private Transform _uiRoot;

        #endregion

        #region Extension Points

        /// <inheritdoc/>
        protected override void ConfigureStages()
        {
            if (_profile == null)
            {
                LogManager.Error(LogCategories.GameLauncher,
                    "IntegrationLauncher 未赋 IntegrationBootstrapProfile：启动不会登记任何阶段。");
                return;
            }

            // 数据 → 阶段：字段怎么落到 options 由使用方决定（这里用示例 Profile 的形状）。
            // 构造入口在各阶段自己的模块里——框架不提供中央目录。
            Bootstrap.Register(new AssetBootstrapStage(new AssetInitOptions
            {
                PackageName = string.IsNullOrEmpty(_profile.AssetPackageName) ? null : _profile.AssetPackageName,
                PlayMode = _profile.PlayMode,
                AutoReclaimOnLowMemory = _profile.AutoReclaimOnLowMemory,
            }));
            Bootstrap.Register(new DataBootstrapStage());
            Bootstrap.Register(new SaveBootstrapStage(new SaveOptions { CurrentVersion = _profile.SaveVersion }));

            if (_uiRoot != null)
                Bootstrap.Register(new UIBootstrapStage(_uiRoot));
            if (_profile.InputOptions != null)
                Bootstrap.Register(new InputBootstrapStage(_profile.InputOptions.ToOptions()));
            if (!string.IsNullOrEmpty(_profile.DefaultLanguage))
                Bootstrap.Register(new LocalizationBootstrapStage(_profile.DefaultLanguage, _profile.LanguageTable));

            // 示例自己的阶段：Settings(90) / Config(90) 与 Localization(90) 并行。
            Bootstrap.Register(new GameSettingsBootstrapStage());
            Bootstrap.Register(new GameConfigBootstrapStage());
        }

        #endregion
    }
}
