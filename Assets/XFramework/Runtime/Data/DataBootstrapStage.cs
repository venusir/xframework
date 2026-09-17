using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XBootstrap;
using XFramework.XPipeline;

namespace XFramework.XData
{

    /// <summary>
    /// <see cref="DataManager"/> 的引导阶段。Phase = 3，晚于 Asset(0)、早于 Save(4)。
    /// <para>同步瞬时阶段：无取消窗口（没有可取消的 await），故忽略 <c>cancellationToken</c>。</para>
    /// </summary>
    public sealed class DataBootstrapStage : IBootstrapStage
    {
        #region IBootstrapStage

        /// <summary>Phase = 3。晚于 Asset(0)、早于 Save(4)，确保依赖的模块已就绪。</summary>
        public int Phase => 3;

        /// <inheritdoc/>
        public string Name => GetType().Name;

        /// <inheritdoc/>
        public float Weight => 1f;

        /// <summary>注入默认的 <see cref="DataManagerImpl"/> 完成初始化。</summary>
        public UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            context.SetDescription("Initializing Data Manager...");

            DataManager.Initialize(new DataManagerImpl());

            context.SetProgress(1f);
            context.SetState(PipelineStageState.Completed);
            return UniTask.CompletedTask;
        }

        /// <inheritdoc/>
        public void Shutdown()
        {
            DataManager.Shutdown();
        }

        #endregion
    }
}
