using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XData;
using XFramework.XPipeline;

namespace XFramework.XNode
{
    /// <summary>
    /// Data 模块的节点树桥梁。挂载在 <see cref="ServiceInitializerNode"/> 下，
    /// 作为 <see cref="IPhaseStage"/> 在启动管线的相位分组中初始化 <see cref="XData.DataManager"/> 静态门面。
    /// </summary>
    internal sealed class GameDataNode : LeafNode, IPhaseStage
    {
        #region IPhaseStage

        /// <summary>Phase = 3。晚于 Asset(0)、早于 Save(4)，确保依赖的模块已就绪。</summary>
        public int Phase => 3;

        public string Name => GetType().Name;

        public float Weight => 1f;

        public UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            context.SetDescription("Initializing Data Manager...");

            var impl = new DataManagerImpl();
            DataManager.Initialize(impl);

            context.SetProgress(1f);
            context.SetState(PipelineStageState.Completed);
            return UniTask.CompletedTask;
        }

        #endregion

        #region Lifecycle

        protected override void OnDestroy()
        {
            DataManager.Shutdown();
            base.OnDestroy();
        }

        #endregion
    }
}
