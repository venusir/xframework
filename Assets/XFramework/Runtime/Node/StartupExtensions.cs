using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XPipeline;

namespace XFramework.XNode
{

    /// <summary>
    /// 节点树启动扩展方法。
    /// <para>提供 <see cref="IParentNode"/> 的启动管线:装载 → 加载 → 启动,由通用管线
    /// (<see cref="XFramework.XPipeline"/>)装配并运行——阶段编排/进度聚合/失败传播归管线,
    /// 加载由 <see cref="LoadableStage"/> 单任务适配 + <see cref="ParallelStage"/> 并行阶段承担
    /// (依赖方向:Node → Pipeline)。</para>
    /// </summary>
    public static class StartupExtensions
    {
        /// <summary>
        /// 启动节点树。组装并运行预置启动管线,依次执行装载、加载、启动:
        /// <para>1. 装载(<see cref="NodeLoadableCollectStage"/>):"Scanning nodes..." 描述阶段,
        /// 收集已在 <see cref="BuildStartupPipeline(IParentNode)"/> 装配期完成(运行前快照)。</para>
        /// <para>2. 加载:按 <see cref="ILoadable.Phase"/> 分组,每组一个 <see cref="ParallelStage"/>
        /// (组内并行、组间由管线串行;阶段权重 = 组内任务数)。</para>
        /// <para>3. 启动(<see cref="NodeStartStage"/>):递归启动所有节点的 <see cref="BaseNode.OnStart"/>。</para>
        /// <para>进度序列保持兼容:0 → "Scanning nodes..." → 加载 0~1 → "Starting nodes..." → 1。</para>
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
        /// 组装预置启动管线:装载 → 加载 → 启动。
        /// <para>装配期同步收集全部 <see cref="ILoadable"/> 节点(运行前快照,树在装配后变更不收录),
        /// 按 <see cref="ILoadable.Phase"/> 分组,每组一个 <see cref="ParallelStage"/>
        /// (Weight = 组内任务数,组间串行由管线编排)。返回的管线可直接 <see cref="IPipeline.RunAsync"/> 运行,
        /// 亦可作为基础追加自定义阶段(瞬时阶段建议 Weight = 0)。调用方负责 <see cref="IPipeline.Destroy"/>。</para>
        /// </summary>
        /// <param name="root">节点树的根节点。</param>
        public static IPipeline BuildStartupPipeline(this IParentNode root)
        {
            var pipeline = Pipeline.Create();

            // 装配期同步收集快照(无 ILoadable → 空加载列表警告,与旧行为一致)
            var loadables = root.CollectLoadables();
            if (loadables.Count == 0)
                Debug.LogWarning("[Loader] no loadable tasks found.");

            pipeline.AddStage(new NodeLoadableCollectStage());

            // 按 Phase 分组,Phase 升序装配(每组一个并行阶段,组间串行由管线承担)
            var groups = new Dictionary<int, List<ILoadable>>();
            for (int i = 0; i < loadables.Count; i++)
            {
                var loadable = loadables[i];
                if (!groups.TryGetValue(loadable.Phase, out var list))
                {
                    list = new List<ILoadable>();
                    groups.Add(loadable.Phase, list);
                }
                list.Add(loadable);
            }

            var phases = new List<int>(groups.Keys);
            phases.Sort();
            for (int i = 0; i < phases.Count; i++)
            {
                int phase = phases[i];
                var list = groups[phase];
                var stages = new IPipelineStage[list.Count];
                for (int j = 0; j < list.Count; j++)
                    stages[j] = new LoadableStage(list[j]);
                pipeline.AddStage(new ParallelStage(stages, $"Load-{phase}"));
            }

            pipeline.AddStage(new NodeStartStage(root));
            return pipeline;
        }

        /// <summary>
        /// 从指定根节点开始,递归收集所有实现了 <see cref="ILoadable"/> 的节点。
        /// <para>收集结果用于 <see cref="BuildStartupPipeline(IParentNode)"/> 按 Phase 分组装配加载并行阶段。</para>
        /// </summary>
        /// <param name="root">搜索的起始节点。</param>
        /// <returns>收集到的可加载节点列表(层级优先序)。</returns>
        public static List<ILoadable> CollectLoadables(this IParentNode root)
        {
            var result = new List<ILoadable>();
            if (root == null)
                return result;

            CollectLoadablesRecursive(root, result);
            return result;
        }

        #region Private Methods

        /// <summary>递归收集实现 <see cref="ILoadable"/> 的节点到列表。</summary>
        private static void CollectLoadablesRecursive(IParentNode node, List<ILoadable> result)
        {
            for (int i = 0; i < node.ChildCount; i++)
            {
                var child = node[i];

                if (child is ILoadable loadable)
                {
                    result.Add(loadable);
                }

                if (child is IParentNode childParent)
                {
                    CollectLoadablesRecursive(childParent, result);
                }
            }
        }

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
