using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XLog;

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
        /// <param name="name">阶段名称(进度描述)。相位分组装配按 nameFormat 生成(如 "Phase-0"),默认 "Parallel";
        /// null 抛 <see cref="ArgumentNullException"/>(与 <see cref="Pipeline.BuildPhaseGroups"/> 对 nameFormat 的防御一致)。</param>
        /// <param name="timeoutSeconds">与 <paramref name="stages"/> 同序的子阶段超时(秒;0/负值/非有限值 = 不启用),
        /// 可为 null(全部不启用)。非 null 时长度必须与 <paramref name="stages"/> 相同。
        /// <para>超时只报告失败并取消令牌，<b>不响应取消的子阶段仍会跑完</b>——触发后本组不等待其沉降，直接走失败终局。</para></param>
        public ParallelStage(IReadOnlyList<IPipelineStage> stages, string name = "Parallel",
                             IReadOnlyList<float> timeoutSeconds = null)
        {
            if (stages == null || stages.Count == 0)
                throw new ArgumentException("stages must not be null or empty.", nameof(stages));
            if (name == null)
                throw new ArgumentNullException(nameof(name));
            if (timeoutSeconds != null && timeoutSeconds.Count != stages.Count)
                throw new ArgumentException("timeoutSeconds must match stages count.", nameof(timeoutSeconds));

            _children = new List<IPipelineStage>(stages);
            _name = name;
            _childTimeouts = new List<float>(_children.Count);

            // 阶段权重 = Σ子阶段声明权重,构造期缓存;运行期子上下文权重改写不参与(防任务权重泄漏到管线级)
            for (int i = 0; i < _children.Count; i++)
            {
                // 元素 null 与 AddStage(null) 同形防御（R3-PIPE-001）：否则会在读 Weight 时抛裸 NRE。
                if (_children[i] == null)
                    throw new ArgumentNullException(nameof(stages), $"stages[{i}] is null.");
                _weight += _children[i].Weight;

                // 非有限值按「不启用」归一（与 PipelineImpl.AddStage 同一口径）：+∞ 会让
                // TimeSpan.FromSeconds 溢出，而调用方显然不是想「永远等」。
                float timeout = timeoutSeconds == null ? 0f : timeoutSeconds[i];
                _childTimeouts.Add(float.IsNaN(timeout) || float.IsInfinity(timeout) || timeout <= 0f
                    ? 0f
                    : timeout);
            }

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

            // 取消路径也要沉降（R3-PIPE-003）：Settle 放 finally，否则 throw OCE 会跳过它，
            // 子阶段迟到的 fire-and-forget 写入仍可能聚合/广播。
            try
            {

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
                wrappers.Add(RunChildAsync(i, cts));
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
                LogManager.Error(LogCategories.Pipeline, "Parallel stage failed: {0} ({1:F2}s): {2}",
                    _aggregator.FailTaskName, Time.realtimeSinceStartup - _startTime, _aggregator.FailDescription);
                // 异常对象拷进容器自己的上下文再置失败：管线终局日志据此把用户子阶段的堆栈
                // 带进 JSONL 的 exc 字段（上面那一行的文案保持纯文本）
                _stageCtx.FailureException = _aggregator.FailException;
                _stageCtx.SetState(PipelineStageState.Failed);
            }
            else if (cancellationToken.IsCancellationRequested || anyChildCancelled)
            {
                // 取消:上抛使管线走取消路径(阶段保持当前状态),不误入完成路径
                throw new OperationCanceledException(cancellationToken);
            }
            else
            {
                // 成功:显式写终态（R4-PIPE-001）。StageExecution 的契约兜底在 finally 之后，
                // 而 finally 会先清 Owner；嵌套容器的完成/进度写入就再也通知不到父聚合器。
                _stageCtx.SetProgress(1f);
                _stageCtx.SetState(PipelineStageState.Completed);
            }

            // 迟写防护由 finally 统一执行（R3-PIPE-003）
            }
            finally
            {
                _aggregator.Settle();
                // 解除对上一轮管线（Owner = PipelineImpl）的引用（R3-PIPE-004）：长寿命容器跨管线复用时
                // 会把上一轮管线与订阅者闭包一直钉住。上下文本身保留——诊断与用例仍可读终态。
                if (_stageCtx != null)
                    _stageCtx.Owner = null;
            }
        }

        #endregion

        #region Private Methods

        /// <summary>执行单个子阶段：无超时直接走共享包装，有超时则与计时任务竞速。</summary>
        private UniTask<bool> RunChildAsync(int index, CancellationTokenSource groupCts)
        {
            float timeout = _childTimeouts[index];
            if (timeout <= 0f)
                return StageExecution.RunStageAsync(_children[index], _aggregator.ChildContexts[index], groupCts.Token);

            return RunChildWithTimeoutAsync(index, timeout, groupCts);
        }

        /// <summary>
        /// 带超时的子阶段执行：与计时任务竞速。
        /// <para>超时获胜时只标记子上下文 <see cref="PipelineStageState.Failed"/> 并<b>不等待在途子阶段沉降</b>
        /// （挂死任务不得阻塞组）；聚合器在状态写入时取消其余兄弟。被放弃任务的后续上下文写入由
        /// <see cref="StageAggregator.Settle"/> 关闸挡掉。</para>
        /// <para>返回 true 表示以取消结束（外部取消赢得竞速），与 <see cref="StageExecution"/> 的约定一致。</para>
        /// </summary>
        private async UniTask<bool> RunChildWithTimeoutAsync(int index, float timeoutSeconds,
                                                             CancellationTokenSource groupCts)
        {
            IPipelineStage child = _children[index];
            PipelineStageContext childContext = _aggregator.ChildContexts[index];

            // 独立计时源与组取消联动：外部取消经链接同步传播给计时任务。
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(groupCts.Token);
            var stageTask = StageExecution.RunStageAsync(child, childContext, groupCts.Token);
            var timeoutTask = TimeoutTaskFactory(timeoutSeconds, timeoutCts.Token);

            // WhenAny 内部观测全部任务，被放弃的在途任务无未观察异常
            var (raceCancelled, race) = await UniTask.WhenAny<bool>(stageTask, timeoutTask)
                .SuppressCancellationThrow();

            if (raceCancelled)
            {
                // 竞速期间外部取消：交回调用方统一走取消路径
                return true;
            }

            if (race.hasResultLeft)
            {
                // 子阶段先结束：终止悬挂的计时任务
                timeoutCts.Cancel();
                return race.result;
            }

            // 超时获胜：置子上下文失败（聚合器据此取消其余兄弟），不等待在途子阶段沉降
            childContext.SetDescription($"Stage '{child.Name}' timed out after {timeoutSeconds}s");
            childContext.SetState(PipelineStageState.Failed);
            return false;
        }

        #endregion

        #region Private Fields

        readonly List<IPipelineStage> _children;
        readonly List<float> _childTimeouts;
        readonly string _name;
        readonly float _weight;
        readonly StageAggregator _aggregator;

        /// <summary>
        /// 子阶段超时计时任务工厂（测试缝，internal）：生产路径为真实墙钟延时
        /// （<see cref="DelayType.Realtime"/>，不受 timeScale 影响）；EditMode 阻塞式测试注入确定性计时器
        /// （真实延时由 PlayerLoop 泵送，阻塞测试中永不触发）。与 <see cref="PipelineImpl"/> 的同名缝同形。
        /// </summary>
        internal Func<float, CancellationToken, UniTask> TimeoutTaskFactory =
            (seconds, token) => UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.Realtime, cancellationToken: token);

        /// <summary>与 <see cref="_children"/> 同序的子阶段超时（测试缝，只读）。</summary>
        internal IReadOnlyList<float> ChildTimeouts => _childTimeouts;

        /// <summary>主阶段上下文(由管线注入),聚合广播的转发目标。沉降后保持引用供诊断/测试读取。</summary>
        internal PipelineStageContext _stageCtx;

        /// <summary>本次执行启动时刻(真实秒),用于失败日志的耗时统计。</summary>
        float _startTime;

        #endregion
    }
}
