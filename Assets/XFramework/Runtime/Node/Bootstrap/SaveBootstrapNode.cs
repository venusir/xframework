using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XPipeline;
using XFramework.XSave;

namespace XFramework.XNode
{
    /// <summary>
    /// Save 模块的节点树桥梁。挂载在 <see cref="ServiceInitializerNode"/> 下，
    /// 作为 <see cref="IPhaseStage"/> 在启动管线的相位分组中初始化 <see cref="XSave.SaveManager"/> 静态门面。
    /// </summary>
    internal sealed class SaveBootstrapNode : LeafNode, IPhaseStage
    {
        #region IPhaseStage

        /// <summary>Phase = 4。晚于 Data(3)，确保快照能力已就绪。</summary>
        public int Phase => 4;

        public string Name => GetType().Name;

        public float Weight => 1f;

        public UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            context.SetDescription("Initializing Save Manager...");

            SaveManager.Initialize();

            context.SetProgress(1f);
            context.SetState(PipelineStageState.Completed);
            return UniTask.CompletedTask;
        }

        #endregion

        #region Lifecycle

        protected override void OnDestroy()
        {
            SaveManager.Shutdown();
            base.OnDestroy();
        }

        #endregion
    }
}
