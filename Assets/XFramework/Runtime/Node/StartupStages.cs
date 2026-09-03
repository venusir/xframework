using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XPipeline;

namespace XFramework.XNode
{

    /// <summary>
    /// 节点收集描述阶段。
    /// <para>收集已提前至 <see cref="StartupExtensions.BuildStartupPipeline(IParentNode)"/> 装配期完成(运行前快照),
    /// 本阶段仅广播 "Scanning nodes..." 描述,保持启动进度序列兼容。</para>
    /// <para>Weight = 0(瞬时阶段,不占进度);正常返回由管线契约兜底置完成。</para>
    /// </summary>
    internal sealed class NodeCollectStage : IPipelineStage
    {
        public string Name => "Collect";

        public float Weight => 0f;

        public UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            context.SetDescription("Scanning nodes...");
            context.SetProgress(1f);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>
    /// 节点树启动阶段:递归启动所有节点的 <see cref="BaseNode.OnStart"/>。
    /// <para>Weight = 0(瞬时阶段,不占进度)。</para>
    /// </summary>
    internal sealed class NodeStartStage : IPipelineStage
    {
        readonly IParentNode _root;

        public NodeStartStage(IParentNode root)
        {
            _root = root;
        }

        public string Name => "Start";

        public float Weight => 0f;

        public UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            context.SetDescription("Starting nodes...");
            if (_root is BaseNode baseNode)
                baseNode.Start();
            context.SetProgress(1f);
            return UniTask.CompletedTask;
        }
    }
}
