using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework.XPipeline
{

    /// <summary>
    /// 并行阶段:组内子阶段并行执行,组间串行由管线编排承担。
    /// <para>为每个子阶段创建子上下文(接收方 = 共享聚合器 <see cref="StageAggregator"/>),
    /// 子阶段写入即触发组内加权聚合 Σ(w·p)/Σ(w) + 阈值节流(≥1% || 描述 || 状态变化),
    /// 收敛后转发主上下文;失败任一子阶段 → 聚合器立即取消其余兄弟 → 沉降(WhenAll)后置 Failed;
    /// 取消 → 上抛 <see cref="OperationCanceledException"/> 走管线取消路径。</para>
    /// <para>阶段权重 = Σ子阶段声明权重(构造期固定);子阶段运行期对子上下文权重的改写仅影响组内聚合,
    /// 不泄漏到管线级。相位分组编排以「每相位一个并行阶段」形态接入,组间串行由管线承担。</para>
    /// </summary>
    public sealed class ParallelStage : IPipelineStage
    {
        #region Public API

        /// <summary>构造并行阶段。</summary>
        /// <param name="stages">子阶段列表。null 或空抛 <see cref="ArgumentException"/>。</param>
        /// <param name="name">阶段名称(进度描述)。相位分组装配按 nameFormat 生成(如 "Phase-0"),默认 "Parallel"。</param>
        public ParallelStage(IReadOnlyList<IPipelineStage> stages, string name = "Parallel")
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
        /// 以管线阶段形态执行本组子阶段:并行启动全部子阶段,经子上下文写入事件驱动聚合,
        /// 沉降(WhenAll)后收敛到成功/失败/取消三路互斥终局。
        /// <para>契约兜底(正常返回未写终态 → 补 Completed)由管线负责,本类只处理组内收敛。</para>
        /// </summary>
        public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            _stageCtx = context;
            _startTime = Time.realtimeSinceStartup;

            // 链接外部取消令牌:任一方取消,组内子阶段收到已取消的 token
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // 复位聚合器并注入转发目标与链接取消源(失败时经聚合器中断兄弟子阶段)
            _aggregator.Reset(context, cts);

            // 先装配全部子上下文(聚合遍历时数组必须完整——首个子阶段同步写进度即触发聚合),
            // 再并行启动。子上下文直写 Executing(不经 SetState):与管线「执行即执行中」契约一致
            // (阶段只写进度/描述,不写状态的子阶段进度同样参与聚合),且零广播——避免凭空增加 0 进度广播。
            for (int i = 0; i < _children.Count; i++)
            {
                _aggregator.ChildContexts[i] = new PipelineStageContext
                {
                    Owner = _aggregator,
                    Name = _children[i].Name,
                    Weight = _children[i].Weight,
                    State = PipelineStageState.Executing,
                };
            }

            var wrappers = new List<UniTask<bool>>(_children.Count);
            for (int i = 0; i < _children.Count; i++)
            {
                wrappers.Add(StageExecution.RunStageAsync(_children[i], _aggregator.ChildContexts[i], cts.Token));
            }

            // 沉降:RunStageAsync 已吞掉全部异常/OCE,WhenAll 正常返回,保证本阶段返回时无在途子阶段
            var results = await UniTask.WhenAll(wrappers);

            // 沉降后释放取消源引用(终局路径在途写入误 Cancel 已释放取消源的防御,对应原 _cts = null 时机)
            _aggregator.ReleaseLinkedCts();

            // 任一子阶段以取消结束(第三方子阶段自抛 OCE)→ 组走取消路径
            bool anyChildCancelled = false;
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i])
                {
                    anyChildCancelled = true;
                    break;
                }
            }

            if (_aggregator.AnyFailed)
            {
                Debug.LogError($"[Pipeline] Parallel stage failed: {_aggregator.FailTaskName} ({Time.realtimeSinceStartup - _startTime:F2}s): {_aggregator.FailDescription}");
                _stageCtx.SetState(PipelineStageState.Failed);
            }
            else if (cancellationToken.IsCancellationRequested || anyChildCancelled)
            {
                // 取消:上抛使管线走取消路径(阶段保持当前状态),不误入完成路径
                throw new OperationCanceledException(cancellationToken);
            }
            // 成功:正常返回,由管线契约兜底补置 Completed

            // 迟写防护:沉降后子阶段 fire-and-forget 写入直接忽略
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
