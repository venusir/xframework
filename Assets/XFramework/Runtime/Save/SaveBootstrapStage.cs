using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XBootstrap;
using XFramework.XPipeline;

namespace XFramework.XSave
{

    /// <summary>
    /// <see cref="SaveManager"/> 的引导阶段：初始化门面并跑一次启动恢复扫描。
    /// <para>Phase = <see cref="BootstrapPhases.Save"/>（4）。<b>必须晚于 Data</b>——本模块**运行期**经
    /// <c>DataManager</c> 收集 / 应用数据块快照；<b>本阶段自身不碰 Data</b>（恢复扫描只用 <c>FileManager</c>）。
    /// 这是内置相位里唯一的硬性模块间依赖。</para>
    /// </summary>
    public sealed class SaveBootstrapStage : IBootstrapStage
    {
        #region Private Fields

        private readonly SaveOptions _options;

        /// <summary>
        /// 本阶段是否<b>确实</b>完成了初始化。<see cref="Shutdown"/> 据此决定要不要注销——
        /// 「本阶段初始化的服务才由本阶段清理」，已初始化早退路径不该把使用方注入的实现一起注销。
        /// </summary>
        private bool _initializedByThisStage;

        #endregion

        #region Construction

        /// <summary>构造引导阶段。</summary>
        /// <param name="options">存档选项。<c>null</c> 表示用默认值（存档格式版本 1）。</param>
        public SaveBootstrapStage(SaveOptions options = null)
        {
            _options = options;
        }

        #endregion

        #region Internal

        /// <summary>本阶段携带的选项（测试缝，用于断言字段面真的注入了）。</summary>
        internal SaveOptions Options => _options;

        #endregion

        #region IBootstrapStage

        /// <summary>Phase = <see cref="BootstrapPhases.Save"/>（4）。晚于 Data，确保快照能力已就绪。</summary>
        public int Phase => BootstrapPhases.Save;

        /// <inheritdoc/>
        public string Name => GetType().Name;

        /// <inheritdoc/>
        public float Weight => 1f;

        /// <summary>
        /// 初始化门面并 await 启动恢复扫描。
        /// <para>恢复扫描会在**每次读侧车**时切回主线程（`TryReadSidecarAsync` 入口，不是只有重建侧车
        /// 时才切）；<see cref="PipelineStageContext"/> 有越线程写入断言，因此写上下文之前必须切回主线程。</para>
        /// </summary>
        public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            context.SetDescription("Initializing Save Manager...");

            bool wasInitialized = SaveManager.IsInitialized;
            SaveManager.Initialize(null, _options);

            // 只在**本次**完成初始化时置位；重跑时 wasInitialized 为真，绝不能把上一轮置的 true
            // 覆写成 false——否则 Shutdown 不再注销第一次由本阶段初始化的门面（SAVE-005）。
            if (!wasInitialized)
                _initializedByThisStage = true;

            await SaveManager.RecoverAsync(cancellationToken);

            // PipelineStageContext 有越线程写入检测（编辑器下会报 LogError），恢复扫描结束后必须切回主线程再写
            await UniTask.SwitchToMainThread(cancellationToken);

            context.SetProgress(1f);
            context.SetState(PipelineStageState.Completed);
        }

        /// <summary>
        /// 反向清理：<b>只注销本阶段自己初始化的那份</b>——已初始化早退路径不碰使用方注入的实现。幂等。
        /// </summary>
        public void Shutdown()
        {
            if (!_initializedByThisStage)
                return;

            _initializedByThisStage = false;
            SaveManager.Shutdown();
        }

        #endregion
    }
}
