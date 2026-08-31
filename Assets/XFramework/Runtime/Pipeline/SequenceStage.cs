using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework.XPipeline
{

    /// <summary>
    /// 串行阶段:组内子阶段依次执行(前一完成才启动下一),组间并行/串行由父容器决定。
    /// <para>与 <see cref="ParallelStage"/> 对称的串行容器:并行组内存在先后依赖的子段
    /// (如「组内先 A 后 B、与 C 并行」)经 <see cref="ParallelStage"/> 嵌入本阶段表达;
    /// 任意串并行组合 = 串行/并行容器嵌套树(顶层管线固定串行)。</para>
    /// <para>为每个子阶段创建子上下文(接收方 = 共享聚合器 <see cref="StageAggregator"/>),未开始子阶段保持
    /// Pending(不占进度)——子段切换进度回落与管线「阶段切换回落属预期」一致;子阶段启动时置 Executing
    /// 触发组内加权聚合 Σ(w·p)/Σ(w) + 阈值节流后转发主上下文。失败任一子阶段 → 停止后续子阶段并置 Failed;
    /// 取消 → 上抛 <see cref="OperationCanceledException"/> 走管线取消路径。</para>
    /// <para>阶段权重 = Σ子阶段声明权重(构造期固定);子阶段运行期对子上下文权重的改写仅影响组内聚合,
    /// 不泄漏到管线级。</para>
    /// </summary>
    public sealed class SequenceStage : IPipelineStage
    {
        #region Public API

        /// <summary>构造串行阶段。</summary>
        /// <param name="stages">子阶段列表。null 或空抛 <see cref="ArgumentException"/>。</param>
        /// <param name="name">阶段名称(进度描述)。默认 "Sequence"。</param>
        public SequenceStage(IReadOnlyList<IPipelineStage> stages, string name = "Sequence")
        {
            if (stages == null || stages.Count == 0)
                throw new ArgumentException("stages must not be null or empty.", nameof(stages));

            _children = new List<IPipelineStage>(stages);
            _name = name;

            // 阶段权重 = Σ子阶段声明权重,构造期缓存;运行期子上下文权重改写不参与(防任务权重泄漏到管线级)
            for (int i = 0; i < _children.Count; i++) _weight += _children[i].Weight;

            _aggregator = new StageAggregator(_children.Count);
        }

        /// <summary>阶段名称(进度描述)。</summary>
        public string Name => _name;

        /// <summary>阶段权重 = Σ子阶段声明权重(构造期计算并缓存,运行期不变)。</summary>
        public float Weight => _weight;

        #endregion

        #region IPipelineStage

        /// <summary>
        /// 以管线阶段形态执行本组子阶段:依次启动子阶段,经子上下文写入事件驱动聚合,
        /// 收敛到成功/失败/取消三路互斥终局。
        /// <para>契约兜底(正常返回未写终态 → 补 Completed)由管线负责,本类只处理组内收敛。</para>
        /// </summary>
        public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            _stageCtx = context;
            _startTime = Time.realtimeSinceStartup;

            // 复位聚合器并注入转发目标(串行无在途兄弟,不注入链接取消源)
            _aggregator.Reset(context, null);

            // 装配全部子上下文:State 留默认 Pending(直写字段零广播)——未开始子阶段不占进度,
            // 子段切换进度回落与管线「阶段切换回落属预期」一致(Pending 移出聚合分母)
            for (int i = 0; i < _children.Count; i++)
            {
                _aggregator.ChildContexts[i] = new PipelineStageContext
                {
                    Owner = _aggregator,
                    Name = _children[i].Name,
                    Weight = _children[i].Weight,
                };
            }

            for (int i = 0; i < _children.Count; i++)
            {
                // 阶段边界取消检查:取消显式走取消终局,不启动后续子阶段
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationToken);

                var childCtx = _aggregator.ChildContexts[i];

                // 启动子阶段(写入即触发组内聚合,未开始子阶段仍 Pending 不占进度)
                childCtx.SetState(PipelineStageState.Executing);

                // 子阶段经共享包装统一执行(异常/取消捕获 + 契约兜底),返回是否以取消结束
                bool childCancelled = await StageExecution.RunStageAsync(_children[i], childCtx, cancellationToken);
                if (childCancelled)
                {
                    // 子阶段取消:上抛使管线走取消路径(阶段保持当前状态),不误入完成路径
                    throw new OperationCanceledException(cancellationToken);
                }

                if (childCtx.State == PipelineStageState.Failed)
                {
                    // 组失败:停止后续子阶段(诊断信息由聚合器从失败子阶段捕获)
                    Debug.LogError($"[Pipeline] Sequence stage failed: {_aggregator.FailTaskName} ({Time.realtimeSinceStartup - _startTime:F2}s): {_aggregator.FailDescription}");
                    _stageCtx.SetState(PipelineStageState.Failed);
                    break;
                }
            }

            // 成功:正常返回,由管线契约兜底补置 Completed

            // 迟写防护:执行结束后子阶段 fire-and-forget 写入直接忽略
            _aggregator.Settle();
        }

        #endregion

        #region Private Fields

        readonly List<IPipelineStage> _children;
        readonly string _name;
        readonly float _weight;
        readonly StageAggregator _aggregator;

        /// <summary>主阶段上下文(由管线注入),聚合广播的转发目标。沉降后保持引用供诊断/测试读取。</summary>
        internal PipelineStageContext _stageCtx;

        /// <summary>本次执行启动时刻(真实秒),用于失败日志的耗时统计。</summary>
        float _startTime;

        #endregion
    }
}
