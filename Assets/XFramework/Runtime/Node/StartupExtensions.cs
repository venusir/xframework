using System;
using Cysharp.Threading.Tasks;
using XFramework.XLoader;
using XFramework.XPipeline;

namespace XFramework.XNode
{

    /// <summary>
    /// 节点树启动扩展方法。
    /// <para>提供 <see cref="IParentNode"/> 的启动管线:装载 → 加载 → 启动 → 回收,由通用管线
    /// (<see cref="XFramework.XPipeline"/>)装配并运行——阶段编排/进度聚合/失败传播归管线,
    /// 加载调度由 Loader 模块的 <see cref="XLoader.ILoader"/> 提供(依赖方向:Node → Loader → Pipeline)。</para>
    /// </summary>
    public static class StartupExtensions
    {
        /// <summary>
        /// 启动节点树。组装并运行预置启动管线,依次执行装载、加载、启动、回收四个阶段:
        /// <para>1. 装载(<see cref="NodeLoadableCollectStage"/>):扫描节点树,收集实现了 <see cref="ILoadable"/> 的节点。</para>
        /// <para>2. 加载(Loader 阶段):按 Phase 分组调度所有加载任务。</para>
        /// <para>3. 启动(<see cref="NodeStartStage"/>):递归启动所有节点的 <see cref="BaseNode.OnStart"/>。</para>
        /// <para>4. 回收(<see cref="NodeDisposeStage"/>):销毁加载器,清理资源。</para>
        /// <para>预置阶段权重 (0,1,0,0):全局进度恒等于加载阶段进度,与旧行为序列一致
        /// (0 → "Scanning nodes..." → 加载 0~1 → "Starting nodes..." → 1)。</para>
        /// </summary>
        /// <param name="root">节点树的根节点。</param>
        /// <param name="progress">可选的进度报告回调,用于接收启动各阶段的进度快照。</param>
        public static async UniTask StartupAsync(this IParentNode root, IProgress<LoadProgress> progress = null)
        {
            if (root == null)
                return;

            var pipeline = BuildStartupPipeline(root);

            if (progress != null)
                pipeline.OnProgressUpdate += p => progress.Report(ToLoadProgress(p));

            await pipeline.RunAsync();
            pipeline.Destroy();
        }

        /// <summary>
        /// 启动节点树(管线进度重载)。与 <see cref="StartupAsync(IParentNode, IProgress{LoadProgress})"/> 行为一致,
        /// 进度回调直接接收管线级快照 <see cref="PipelineProgress"/>。
        /// <para>注意:传 <c>null</c> 字面量时与旧重载存在重载歧义,请以无参调用或显式指定接口类型的方式规避。</para>
        /// </summary>
        /// <param name="root">节点树的根节点。</param>
        /// <param name="progress">可选的进度报告回调。</param>
        public static async UniTask StartupAsync(this IParentNode root, IProgress<PipelineProgress> progress)
        {
            if (root == null)
                return;

            var pipeline = BuildStartupPipeline(root);

            if (progress != null)
                pipeline.OnProgressUpdate += p => progress.Report(p);

            await pipeline.RunAsync();
            pipeline.Destroy();
        }

        /// <summary>
        /// 组装预置启动管线:装载 → 加载 → 启动 → 回收。
        /// <para>返回的管线可直接 <see cref="IPipeline.RunAsync"/> 运行,亦可作为基础追加自定义阶段
        /// (阶段权重建议:主进度阶段 Weight = 1,瞬时阶段 Weight = 0)。调用方负责 <see cref="IPipeline.Destroy"/>。</para>
        /// </summary>
        /// <param name="root">节点树的根节点。</param>
        public static IPipeline BuildStartupPipeline(this IParentNode root)
        {
            var loader = new Loader();

            var pipeline = Pipeline.Create();
            pipeline.AddStage(new NodeLoadableCollectStage(root, loader));
            pipeline.AddStage(loader);
            pipeline.AddStage(new NodeStartStage(root));
            pipeline.AddStage(new NodeDisposeStage(loader));
            return pipeline;
        }

        /// <summary>
        /// 从指定根节点开始，递归查找所有实现了 <see cref="ILoadable"/> 的节点并注册到 <see cref="ILoader"/>。
        /// <para>注册后，<see cref="ILoader.LoadAsync"/> 时会统一调度这些节点的加载任务。</para>
        /// </summary>
        /// <param name="root">搜索的起始节点。</param>
        /// <param name="loader">加载器实例。</param>
        public static void CollectLoadables(this IParentNode root, ILoader loader)
        {
            if (root == null || loader == null)
                return;

            for (int i = 0; i < root.ChildCount; i++)
            {
                var child = root[i];

                if (child is ILoadable loadable)
                {
                    loader.AddLoadable(loadable);
                }

                if (child is IParentNode childParent)
                {
                    CollectLoadables(childParent, loader);
                }
            }
        }

        #region Private Methods

        /// <summary>管线进度快照 → LoadProgress 兼容映射(旧进度回调重载使用,保真映射当前任务名)。</summary>
        private static LoadProgress ToLoadProgress(PipelineProgress p)
        {
            return new LoadProgress
            {
                OverallProgress = p.OverallProgress,
                Description = p.Description,
                CurrentTaskName = p.CurrentTaskName,
            };
        }

        #endregion
    }
}
