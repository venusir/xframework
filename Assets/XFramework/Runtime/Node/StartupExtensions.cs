using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XPipeline;

namespace XFramework.XNode
{

    /// <summary>
    /// 节点树启动扩展方法。
    /// <para>提供 <see cref="IParentNode"/> 的启动管线:收集 → 相位分组执行 → 启动,由通用管线
    /// (<see cref="XFramework.XPipeline"/>)装配并运行——阶段编排/进度聚合/失败传播归管线,
    /// 相位分组由 <see cref="Pipeline.BuildPhaseGroups"/> 承担(依赖方向:Node → Pipeline)。</para>
    /// <para>树内任意实现 <see cref="IPhaseStage"/> 的节点均为相位阶段:同相位并行执行、相位升序串行;
    /// 相位号数值含义为模块约定(框架内置引导相位 0 Asset / 3 Data / 4 Save,见 Pipeline 模块 README)。</para>
    /// </summary>
    public static class StartupExtensions
    {
        /// <summary>
        /// 启动节点树。组装并运行预置启动管线,依次执行收集、相位阶段、启动:
        /// <para>1. 收集(<see cref="NodeCollectStage"/>):"Scanning nodes..." 描述阶段,
        /// 收集已在 <see cref="BuildStartupPipeline(IParentNode)"/> 装配期完成(运行前快照)。</para>
        /// <para>2. 相位阶段:按 <see cref="IPhaseStage.Phase"/> 分组,每组一个 <see cref="ParallelStage"/>
        /// (组内并行、组间由管线串行;阶段权重 = 组内声明权重之和)。</para>
        /// <para>3. 启动(<see cref="NodeStartStage"/>):递归启动所有节点的 <see cref="BaseNode.OnStart"/>。</para>
        /// <para>进度序列保持兼容:0 → "Scanning nodes..." → 相位阶段 0~1 → "Starting nodes..." → 1。</para>
        /// </summary>
        /// <param name="root">节点树的根节点。</param>
        /// <param name="progress">可选的进度报告回调,用于接收启动各阶段的进度快照。</param>
        public static async UniTask StartupAsync(this IParentNode root, IProgress<PipelineProgress> progress = null)
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
        /// 组装预置启动管线:收集 → 相位阶段 → 启动。
        /// <para>装配期同步收集全部 <see cref="IPhaseStage"/> 节点(运行前快照,树在装配后变更不收录),
        /// 经 <see cref="Pipeline.BuildPhaseGroups"/> 按相位分组,每组一个 <see cref="ParallelStage"/>
        /// (组间串行由管线编排)。返回的管线可直接 <see cref="IPipeline.RunAsync"/> 运行,
        /// 亦可作为基础追加自定义阶段(瞬时阶段建议 Weight = 0)。调用方负责 <see cref="IPipeline.Destroy"/>。</para>
        /// </summary>
        /// <param name="root">节点树的根节点。</param>
        public static IPipeline BuildStartupPipeline(this IParentNode root)
        {
            var pipeline = Pipeline.Create();

            // 装配期同步收集快照(无相位阶段 → 空列表警告,不阻塞启动阶段,与旧行为一致)
            var phases = CollectPhaseStages(root);
            if (phases.Count == 0)
                Debug.LogWarning("[Startup] no phase stages found.");

            pipeline.AddStage(new NodeCollectStage());

            // 相位分组装配:同相位并行、相位升序串行(组名默认 "Phase-{0}")
            var groups = Pipeline.BuildPhaseGroups(phases);
            for (int i = 0; i < groups.Count; i++)
            {
                pipeline.AddStage(groups[i]);
            }

            pipeline.AddStage(new NodeStartStage(root));
            return pipeline;
        }

        /// <summary>
        /// 从指定根节点开始,递归收集所有实现了 <see cref="IPhaseStage"/> 的节点。
        /// <para>收集结果用于 <see cref="BuildStartupPipeline(IParentNode)"/> 按相位分组装配并行阶段。</para>
        /// </summary>
        /// <param name="root">搜索的起始节点。</param>
        /// <returns>收集到的相位阶段列表(层级优先序)。</returns>
        public static List<IPhaseStage> CollectPhaseStages(this IParentNode root)
        {
            var result = new List<IPhaseStage>();
            if (root == null)
                return result;

            CollectPhaseStagesRecursive(root, result);
            return result;
        }

        #region Private Methods

        /// <summary>递归收集实现 <see cref="IPhaseStage"/> 的节点到列表。</summary>
        private static void CollectPhaseStagesRecursive(IParentNode node, List<IPhaseStage> result)
        {
            for (int i = 0; i < node.ChildCount; i++)
            {
                var child = node[i];

                if (child is IPhaseStage stage)
                {
                    result.Add(stage);
                }

                if (child is IParentNode childParent)
                {
                    CollectPhaseStagesRecursive(childParent, result);
                }
            }
        }

        #endregion
    }
}
