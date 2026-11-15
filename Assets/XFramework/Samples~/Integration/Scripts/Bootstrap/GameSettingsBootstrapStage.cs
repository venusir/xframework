using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XBootstrap;
using XFramework.XFileManager;
using XFramework.XPipeline;
using XFramework.XSettings;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 自定义启动阶段：初始化 Settings 模块。
    /// <para><b>为什么示例自己写一个</b>：框架只提供内置的 Asset / Data / Save / UI / Input / Localization 阶段；
    /// Settings 没有内置阶段，需要每个项目按自己的路径与选项接线——
    /// 本类就是那个接线示例（实现 <see cref="IBootstrapStage"/>，Phase 取 <see cref="BootstrapPhases.UserStart"/>）。</para>
    /// <para>它与内置的 Localization 阶段同相位（90）并行：两者没有相互依赖；设置只碰本地文件，
    /// 语言表的加载由 Localization 阶段自己完成。</para>
    /// </summary>
    public sealed class GameSettingsBootstrapStage : IBootstrapStage
    {
        #region Constants

        private const string SettingsRelativePath = "integration/settings.json";

        #endregion

        private bool _initializedByThisStage;

        /// <inheritdoc/>
        public int Phase => BootstrapPhases.UserStart;

        /// <inheritdoc/>
        public string Name => GetType().Name;

        /// <inheritdoc/>
        public float Weight => 1f;

        /// <inheritdoc/>
        public UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            context.SetDescription("Loading settings...");

            // 显式路径：JsonFileStore 直接走 System.IO，不依赖 Asset/File 的初始化顺序。
            // 用 FileManager 取 AppData 的物理路径，和 LastRunStore 落在同一个目录下。
            var path = FileManager.GetPhysicalPath(FileDomain.AppData, SettingsRelativePath);

            SettingsManager.Initialize<IntegrationSettings>(path, options: new SettingsOptions
            {
                CurrentVersion = 1,
                AutoSave = true,
                SaveOnQuit = true,
            });

            // 钩子只能在拿到管理器之后注册；注册完再 Load 一次，让首次读出的数据也过校验。
            SettingsManager.SetValidator(new IntegrationSettingsValidator());
            SettingsManager.SetMigrator(new IntegrationSettingsMigrator());
            SettingsManager.Load<IntegrationSettings>();

            IntegrationSettingRefs.EnsureCreated();
            _initializedByThisStage = true;

            context.SetProgress(1f);
            context.SetState(PipelineStageState.Completed);
            return UniTask.CompletedTask;
        }

        /// <inheritdoc/>
        public void Shutdown()
        {
            // 只销毁本阶段初始化成功的那一份；启动中途失败时安全空转。
            if (!_initializedByThisStage)
                return;

            _initializedByThisStage = false;
            SettingsManager.Destroy();
        }
    }
}
