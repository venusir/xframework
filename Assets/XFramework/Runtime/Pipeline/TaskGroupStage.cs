using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework.XPipeline
{

    /// <summary>
    /// 任务组阶段:一个 Phase 的并行任务组调度器(加载是管线的一种应用)。
    /// <para>作为 <see cref="IPipelineStage"/> 接入通用管线:组内任务并行执行,组间串行由管线编排承担,
    /// 失败/取消传播与阶段级进度聚合归管线,本类只负责组内任务调度与任务级进度聚合。</para>
    /// <para>进度经 <see cref="LoadProgress"/> 写后通知(<see cref="LoadProgress.OnChanged"/>)事件驱动聚合,
    /// 无帧泵轮询——任务写进度即被感知并同步转发阶段上下文(门铃模型,与管线一致)。</para>
    /// <para>阶段权重 = 组内任务数(装配期确定);任务级权重(<see cref="LoadProgress.SetWeight"/>)影响组内聚合。</para>
    /// </summary>
    internal sealed class TaskGroupStage : IPipelineStage
    {
        #region Public API

        /// <summary>构造任务组阶段。</summary>
        /// <param name="tasks">本组加载任务(同一 Phase)。</param>
        /// <param name="phase">阶段号,用于命名与排序诊断。</param>
        public TaskGroupStage(IReadOnlyList<ILoadable> tasks, int phase)
        {
            _tasks = new List<ILoadable>(tasks);
            _name = $"Load-{phase}";
            _weight = _tasks.Count;
            _contexts = new LoadProgress[_tasks.Count];
            _lastStates = new LoadState[_tasks.Count];
        }

        #endregion

        #region IPipelineStage(显式实现)

        string IPipelineStage.Name => _name;

        float IPipelineStage.Weight => _weight;

        /// <summary>
        /// 以管线阶段形态执行本组任务:并行启动全部任务,经 LoadProgress 门铃事件驱动聚合,
        /// 沉降(WhenAll)后收敛到成功/失败/取消三路互斥终局。
        /// </summary>
        async UniTask IPipelineStage.ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            _stageCtx = context;
            _failed = false;
            _failDescription = null;
            _failTaskName = null;
            _lastOverall = -1f;
            _lastDesc = null;
            _startTime = Time.realtimeSinceStartup;

            // 链接外部取消令牌:任一方取消,组内任务收到已取消的 token
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cts = cts;

            // 先装配全部任务上下文(聚合遍历时数组必须完整——首个任务同步写进度即触发聚合),
            // 再并行启动任务;每个 LoadProgress 注入门铃回调,写进度即同步聚合(无需帧泵轮询)
            for (int i = 0; i < _tasks.Count; i++)
            {
                _contexts[i] = new LoadProgress
                {
                    Name = _tasks[i].GetType().Name,
                    OnChanged = OnTaskChanged,
                };
            }

            var wrappers = new List<UniTask>(_tasks.Count);
            for (int i = 0; i < _tasks.Count; i++)
            {
                wrappers.Add(RunTask(_tasks[i], _contexts[i], cts.Token));
            }

            // 沉降:RunTask 已吞掉全部异常/取消,WhenAll 正常返回,保证本阶段返回时无在途任务
            await UniTask.WhenAll(wrappers);
            _cts = null;

            if (_failed)
            {
                Debug.LogError($"[Loader] Load failed: {_failTaskName} ({Time.realtimeSinceStartup - _startTime:F2}s): {_failDescription}");
                context.SetState(PipelineStageState.Failed);
            }
            else if (cancellationToken.IsCancellationRequested)
            {
                // 取消:上抛使管线走取消路径(阶段保持当前状态),不误入完成路径
                throw new OperationCanceledException(cancellationToken);
            }
            // 成功:正常返回,由管线契约兜底补置 Completed
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// LoadProgress 写后通知入口(门铃)。聚合广播发生在任务代码栈内,广播中任务再次写入会递归,
        /// 用 <see cref="_aggregating"/> + <see cref="_dirty"/> 延迟重聚合;聚合整体 try/catch,
        /// 订阅者(UI)异常不得冒泡进任务栈把无辜任务打成 Failed。
        /// </summary>
        private void OnTaskChanged(LoadProgress changed)
        {
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

        /// <summary>
        /// 任务级聚合:加权 Σ(w·p)/Σ(w) + 阈值节流(≥1% || 描述变化 || 状态变化)后转发阶段上下文;
        /// 失败检测立即取消兄弟任务(中断在途加载)。
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

                for (int i = 0; i < _tasks.Count; i++)
                {
                    var ctx = _contexts[i];

                    switch (ctx.State)
                    {
                        case LoadState.Completed:
                            completedCount++;
                            weightSum += ctx.Weight;
                            weightedSum += ctx.Weight;
                            break;
                        case LoadState.Failed:
                            failedCount++;
                            anyFailed = true;
                            // 失败任务不计入进度(权重移出分子与分母,进度略回退,语义正确)
                            currentDesc = ctx.Description;
                            currentTaskName = ctx.Name;
                            if (_failDescription == null)
                            {
                                _failDescription = ctx.Description;
                                _failTaskName = ctx.Name;
                            }
                            break;
                        case LoadState.Loading:
                            weightSum += ctx.Weight;
                            weightedSum += ctx.Weight * ctx.Progress;
                            currentDesc = ctx.Description;
                            currentTaskName = ctx.Name;
                            break;
                        default:
                            break;
                    }
                }

                float overallProgress = weightSum > 0f ? weightedSum / weightSum : 0f;

                if (anyFailed)
                {
                    _failed = true;
                    // 失败时描述/任务名取失败任务(诊断优先,避免被兄弟任务描述覆盖)
                    if (_failDescription != null)
                        currentDesc = _failDescription;
                    if (_failTaskName != null)
                        currentTaskName = _failTaskName;
                }

                // 阈值节流:总体进度变化 ≥1% || 描述变化 || 任一任务状态变化(每帧路径零 LINQ)
                bool dirty = Mathf.Abs(overallProgress - _lastOverall) >= 0.01f;
                if (!dirty && currentDesc != _lastDesc)
                    dirty = true;
                if (!dirty)
                {
                    for (int i = 0; i < _tasks.Count; i++)
                    {
                        if (_contexts[i].State != _lastStates[i])
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
                    for (int i = 0; i < _tasks.Count; i++) _lastStates[i] = _contexts[i].State;
                }

                if (anyFailed)
                    _cts?.Cancel(); // 中断兄弟任务,经 WhenAll 沉降
            }
            catch (Exception ex)
            {
                // 防御:聚合/广播链上的异常(如订阅者)隔离在此,不冒泡进任务栈
                Debug.LogError($"[Loader] progress aggregation failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 包装执行单个加载任务:统一状态机写入、异常与取消捕获,保证任务必然收敛到终态。
        /// <para>正常返回但未写终态(如未调用 SetState 的实现)时自动补置为 <see cref="LoadState.Completed"/>,进度视为 1f;</para>
        /// <para>抛出的异常置为 <see cref="LoadState.Failed"/> 并写入描述,经聚合报告;</para>
        /// <para>抛出 <see cref="OperationCanceledException"/> 视为取消,保持当前状态,由阶段统一走取消路径。</para>
        /// </summary>
        private static async UniTask RunTask(ILoadable loadable, LoadProgress ctx, CancellationToken cancellationToken)
        {
            ctx.SetState(LoadState.Loading);

            try
            {
                await loadable.LoadAsync(ctx, cancellationToken);

                // 契约兜底:正常返回但状态仍停留在 Pending/Loading → 视为完成
                if (ctx.State == LoadState.Pending || ctx.State == LoadState.Loading)
                {
                    ctx.SetProgress(1f);
                    ctx.SetState(LoadState.Completed);
                }
            }
            catch (OperationCanceledException)
            {
                // 取消:保持当前状态,由阶段统一走取消路径,不视为失败
            }
            catch (Exception ex)
            {
                // 先写描述再置状态:状态写入触发聚合时描述已是异常消息(事件驱动取最新值)
                ctx.SetDescription(ex.Message);
                ctx.SetState(LoadState.Failed);
            }
        }

        #endregion

        #region Private Fields

        readonly List<ILoadable> _tasks;
        readonly string _name;
        readonly float _weight;
        readonly LoadProgress[] _contexts;
        readonly LoadState[] _lastStates;

        /// <summary>阶段上下文(由管线注入),聚合广播的转发目标。</summary>
        internal PipelineStageContext _stageCtx;

        /// <summary>本次执行的链接取消源(失败时经其中断兄弟任务)。</summary>
        CancellationTokenSource _cts;

        /// <summary>失败终局信息(仅首个失败任务,保持诊断一致性)。</summary>
        bool _failed;
        string _failDescription;
        string _failTaskName;

        /// <summary>本次加载启动时刻(真实秒),用于失败日志的耗时统计。</summary>
        float _startTime;

        /// <summary>上一帧广播快照:组进度 + 描述 + 各任务状态(阈值节流,首帧 -1 保证必广播)。</summary>
        float _lastOverall = -1f;
        string _lastDesc;

        /// <summary>聚合重入保护:聚合广播中任务再次写入时置 dirty,外层收尾再聚合。</summary>
        bool _aggregating;
        bool _dirty;

        #endregion
    }
}
