using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XBootstrap;
using XFramework.XPipeline;

namespace XFramework.XSave
{

    /// <summary>
    /// <see cref="SaveManager"/> 的引导阶段：初始化门面并跑一次启动恢复扫描。
    /// <para>Phase = 4。<b>必须晚于 Data(3)</b>——恢复扫描要用 <c>DataManager.CreateSnapshot</c> 回滚数据块，
    /// 这是内置相位里唯一的硬性模块间依赖。</para>
    /// </summary>
    public sealed class SaveBootstrapStage : IBootstrapStage
    {
        #region Private Fields

        private readonly SaveOptions _options;

        #endregion

        #region Construction

        /// <summary>构造引导阶段。</summary>
        /// <param name="options">存档选项。<c>null</c> 表示用默认值（存档格式版本 1）。</param>
        public SaveBootstrapStage(SaveOptions options = null)
        {
            _options = options;
        }

        #endregion

        #region IBootstrapStage

        /// <summary>Phase = 4。晚于 Data(3)，确保快照能力已就绪。</summary>
        public int Phase => 4;

        /// <inheritdoc/>
        public string Name => GetType().Name;

        /// <inheritdoc/>
        public float Weight => 1f;

        /// <summary>
        /// 初始化门面并 await 启动恢复扫描。
        /// <para>恢复扫描本身不切回主线程（见 <see cref="SaveManager.RecoverAsync"/> 的线程约定），
        /// 但 <see cref="PipelineStageContext"/> 有越线程写入断言，因此写上下文之前必须切回主线程。</para>
        /// </summary>
        public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            context.SetDescription("Initializing Save Manager...");

            SaveManager.Initialize(null, _options);

            await SaveManager.RecoverAsync(cancellationToken);

            // PipelineStageContext 有越线程写入检测（编辑器下会报 LogError），恢复扫描结束后必须切回主线程再写
            await UniTask.SwitchToMainThread(cancellationToken);

            context.SetProgress(1f);
            context.SetState(PipelineStageState.Completed);
        }

        /// <inheritdoc/>
        public void Shutdown()
        {
            SaveManager.Shutdown();
        }

        #endregion
    }
}
