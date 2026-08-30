using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XLoader;
using XFramework.XPipeline;

namespace XFramework.XNode
{

    /// <summary>
    /// 节点树装载阶段:递归扫描节点树,收集实现了 <see cref="ILoadable"/> 的节点到加载器。
    /// <para>Weight = 0(瞬时阶段,不占进度);正常返回由管线契约兜底置完成。</para>
    /// </summary>
    internal sealed class NodeLoadableCollectStage : IPipelineStage
    {
        readonly IParentNode _root;
        readonly ILoader _loader;

        public NodeLoadableCollectStage(IParentNode root, ILoader loader)
        {
            _root = root;
            _loader = loader;
        }

        public string Name => "Collect";

        public float Weight => 0f;

        public UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            context.SetDescription("Scanning nodes...");
            _root.CollectLoadables(_loader);
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

    /// <summary>
    /// 加载器回收阶段:销毁加载器,清理资源。
    /// <para>Weight = 0(瞬时阶段,不占进度)。</para>
    /// </summary>
    internal sealed class NodeDisposeStage : IPipelineStage
    {
        readonly ILoader _loader;

        public NodeDisposeStage(ILoader loader)
        {
            _loader = loader;
        }

        public string Name => "Dispose";

        public float Weight => 0f;

        public UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            context.SetDescription("Disposing...");
            _loader.Destroy();
            context.SetProgress(1f);
            return UniTask.CompletedTask;
        }
    }
}
