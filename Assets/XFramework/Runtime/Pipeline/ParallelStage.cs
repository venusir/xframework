using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework.XPipeline
{

    /// <summary>
    /// 并行阶段:组内子阶段并行执行,组间串行由管线编排承担。
    /// <para>为每个子阶段创建子上下文(接收方 = 本阶段的子聚合器),子阶段写入即触发组内加权聚合
    /// Σ(w·p)/Σ(w) + 阈值节流(≥1% || 描述 || 状态变化),收敛后转发主上下文;
    /// 失败任一子阶段 → 立即取消其余兄弟 → 沉降(WhenAll)后置 Failed;取消 → 上抛
    /// <see cref="OperationCanceledException"/> 走管线取消路径。</para>
    /// <para>阶段权重 = Σ子阶段声明权重(构造期固定);子阶段运行期对子上下文权重的改写仅影响组内聚合,
    /// 不泄漏到管线级。加载应用以「每 Phase 一个并行阶段」形态接入,组间串行由管线承担。</para>
    /// </summary>
    public sealed class ParallelStage : IPipelineStage, IStageContextSink
    {
        #region Public API

        /// <summary>构造并行阶段。</summary>
        /// <param name="stages">子阶段列表。null 或空抛 <see cref="ArgumentException"/>。</param>
        /// <param name="name">阶段名称(进度描述)。加载应用装配 "Load-{phase}",默认 "Parallel"。</param>
        public ParallelStage(IReadOnlyList<IPipelineStage> stages, string name = "Parallel")
        {
            if (stages == null || stages.Count == 0)
                throw new ArgumentException("stages must not be null or empty.", nameof(stages));

            _children = new List<IPipelineStage>(stages);
            _name = name;

            // 阶段权重 = Σ子阶段声明权重,构造期缓存;运行期子上下文权重改写不参与(防任务权重泄漏到管线级)
            for (int i = 0; i < _children.Count; i++) _weight += _children[i].Weight;

            _childCtxs = new PipelineStageContext[_children.Count];
            _lastStates = new PipelineStageState[_children.Count];
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
            _settled = false;
            _failed = false;
            _failDescription = null;
            _failTaskName = null;
            _lastOverall = -1f;
            _lastDesc = null;
            _startTime = Time.realtimeSinceStartup;

            // 链接外部取消令牌:任一方取消,组内子阶段收到已取消的 token
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cts = cts;

            // 先装配全部子上下文(聚合遍历时数组必须完整——首个子阶段同步写进度即触发聚合),
            // 再并行启动。子上下文直写 Executing(不经 SetState):与管线「执行即执行中」契约一致
            // (阶段只写进度/描述,不写状态的子阶段进度同样参与聚合),且零广播——避免凭空增加 0 进度广播。
            for (int i = 0; i < _children.Count; i++)
            {
                _childCtxs[i] = new PipelineStageContext
                {
                    Owner = this,
                    Name = _children[i].Name,
                    Weight = _children[i].Weight,
                    State = PipelineStageState.Executing,
                };
            }

            var wrappers = new List<UniTask<bool>>(_children.Count);
            for (int i = 0; i < _children.Count; i++)
            {
                wrappers.Add(StageExecution.RunStageAsync(_children[i], _childCtxs[i], cts.Token));
            }

            // 沉降:RunStageAsync 已吞掉全部异常/OCE,WhenAll 正常返回,保证本阶段返回时无在途子阶段
            var results = await UniTask.WhenAll(wrappers);
            _cts = null;

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

            if (_failed)
            {
                Debug.LogError($"[Pipeline] Parallel stage failed: {_failTaskName} ({Time.realtimeSinceStartup - _startTime:F2}s): {_failDescription}");
                _stageCtx.SetState(PipelineStageState.Failed);
            }
            else if (cancellationToken.IsCancellationRequested || anyChildCancelled)
            {
                // 取消:上抛使管线走取消路径(阶段保持当前状态),不误入完成路径
                throw new OperationCanceledException(cancellationToken);
            }
            // 成功:正常返回,由管线契约兜底补置 Completed

            // 迟写防护:沉降后子阶段 fire-and-forget 写入直接忽略
            _settled = true;
        }

        #endregion

        #region IStageContextSink(显式实现)

        /// <summary>
        /// 子上下文写入通知入口(门铃)。聚合广播发生在子阶段写入栈内,广播中子阶段再次写入会递归,
        /// 用 <see cref="_aggregating"/> + <see cref="_dirty"/> 延迟重聚合;聚合整体 try/catch,
        /// 订阅者(UI)异常不得冒泡进子阶段栈把无辜子阶段打成 Failed。
        /// </summary>
        void IStageContextSink.OnStageContextChanged(PipelineStageContext changed)
        {
            if (_settled) return; // 迟写防护:沉降后子阶段再写 → 忽略

            if (_aggregating)
            {
                _dirty = true;
                return;
            }

            _aggregating = true;
            try
            {
                Aggregate();
            }
            finally
            {
                _aggregating = false;
            }

            if (_dirty)
            {
                _dirty = false;
                Aggregate();
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 组内聚合:加权 Σ(w·p)/Σ(w) + 阈值节流(≥1% || 描述变化 || 状态变化)后转发主上下文;
        /// 失败检测立即取消兄弟子阶段(中断在途执行)。
        /// </summary>
        private void Aggregate()
        {
            try
            {
                bool anyFailed = false;
                string currentDesc = null;
                string currentTaskName = null;
                float weightSum = 0f;
                float weightedSum = 0f;
                int completedCount = 0;
                int failedCount = 0;

                for (int i = 0; i < _children.Count; i++)
                {
                    var ctx = _childCtxs[i];

                    switch (ctx.State)
                    {
                        case PipelineStageState.Completed:
                            completedCount++;
                            weightSum += ctx.Weight;
                            weightedSum += ctx.Weight;
                            break;
                        case PipelineStageState.Failed:
                            failedCount++;
                            anyFailed = true;
                            // 失败子阶段不计入进度(权重移出分子与分母,进度略回退,语义正确)
                            currentDesc = ctx.Description;
                            currentTaskName = ctx.CurrentTaskName ?? ctx.Name;
                            if (_failDescription == null)
                            {
                                _failDescription = ctx.Description;
                                _failTaskName = ctx.CurrentTaskName ?? ctx.Name;
                            }
                            break;
                        case PipelineStageState.Executing:
                            weightSum += ctx.Weight;
                            weightedSum += ctx.Weight * ctx.Progress;
                            currentDesc = ctx.Description;
                            currentTaskName = ctx.CurrentTaskName ?? ctx.Name;
                            break;
                        default:
                            break;
                    }
                }

                float overallProgress = weightSum > 0f ? weightedSum / weightSum : 0f;

                if (anyFailed)
                {
                    _failed = true;
                    // 失败时描述/任务名取失败子阶段(诊断优先,避免被兄弟描述覆盖)
                    if (_failDescription != null)
                        currentDesc = _failDescription;
                    if (_failTaskName != null)
                        currentTaskName = _failTaskName;
                }

                // 阈值节流:总体进度变化 ≥1% || 描述变化 || 任一子阶段状态变化(每帧路径零 LINQ)
                bool dirty = Mathf.Abs(overallProgress - _lastOverall) >= 0.01f;
                if (!dirty && currentDesc != _lastDesc)
                    dirty = true;
                if (!dirty)
                {
                    for (int i = 0; i < _children.Count; i++)
                    {
                        if (_childCtxs[i].State != _lastStates[i])
                        {
                            dirty = true;
                            break;
                        }
                    }
                }

                if (dirty)
                {
                    _stageCtx.SetProgress(overallProgress);
                    _stageCtx.SetDescription(currentDesc ?? "Completed");
                    _stageCtx.CurrentTaskName = currentTaskName;

                    _lastOverall = overallProgress;
                    _lastDesc = currentDesc ?? "Completed";
                    for (int i = 0; i < _children.Count; i++) _lastStates[i] = _childCtxs[i].State;
                }

                if (anyFailed)
                    _cts?.Cancel(); // 中断兄弟子阶段,经 WhenAll 沉降
            }
            catch (Exception ex)
            {
                // 防御:聚合/广播链上的异常(如订阅者)隔离在此,不冒泡进子阶段栈
                Debug.LogError($"[Pipeline] progress aggregation failed: {ex.Message}");
            }
        }

        #endregion

        #region Private Fields

        readonly List<IPipelineStage> _children;
        readonly string _name;
        readonly float _weight;
        readonly PipelineStageContext[] _childCtxs;
        readonly PipelineStageState[] _lastStates;

        /// <summary>主阶段上下文(由管线注入),聚合广播的转发目标。沉降后保持引用供诊断/测试读取。</summary>
        internal PipelineStageContext _stageCtx;

        /// <summary>本次执行已沉降标志(迟写防护:沉降后子阶段再写经门铃直接忽略)。</summary>
        bool _settled;

        /// <summary>本次执行的链接取消源(失败时经其中断兄弟子阶段)。</summary>
        CancellationTokenSource _cts;

        /// <summary>失败终局信息(仅首个失败子阶段,保持诊断一致性)。</summary>
        bool _failed;
        string _failDescription;
        string _failTaskName;

        /// <summary>本次执行启动时刻(真实秒),用于失败日志的耗时统计。</summary>
        float _startTime;

        /// <summary>上一帧广播快照:组进度 + 描述 + 各子阶段状态(阈值节流,首帧 -1 保证必广播)。</summary>
        float _lastOverall = -1f;
        string _lastDesc;

        /// <summary>聚合重入保护:聚合广播中子阶段再次写入时置 dirty,外层收尾再聚合。</summary>
        bool _aggregating;
        bool _dirty;

        #endregion
    }
}
