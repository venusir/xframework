using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework.XPipeline
{
    /// <summary>
    /// 管线门面。提供 <see cref="IPipeline"/> 实例的创建入口(实例即用即弃,非全局单例),
    /// 以及按相位分组装配的编排助手。
    /// </summary>
    public static class Pipeline
    {
        /// <summary>创建管线实例。</summary>
        public static IPipeline Create() => new PipelineImpl();

        /// <summary>
        /// 按相位分组装配阶段清单:输入按 <see cref="IPhaseStage.Phase"/> 升序分组,
        /// 每组装配为一个 <see cref="ParallelStage"/>(组内并行、组内保持输入顺序,组间由管线串行)。
        /// <para>装配结果可直接逐个 <see cref="IPipeline.AddStage(IPipelineStage)"/> 添加执行;
        /// 同相位并行、相位升序串行的声明式编排即「每相位一个并行阶段」的约定装配
        /// (如节点树 StartupAsync 预置管线按相位分组调度)。</para>
        /// </summary>
        /// <param name="stages">相位阶段列表。null 抛 <see cref="ArgumentNullException"/>;元素不得为 null;
        /// 空列表返回空清单。</param>
        /// <param name="nameFormat">相位组名格式(相位号作为格式化参数),默认 "Phase-{0}"。
        /// null 抛 <see cref="ArgumentNullException"/>。</param>
        /// <returns>按相位升序排列的 <see cref="ParallelStage"/> 清单,每组一个。</returns>
        public static IReadOnlyList<IPipelineStage> BuildPhaseGroups(
            IReadOnlyList<IPhaseStage> stages, string nameFormat = "Phase-{0}")
        {
            if (stages == null)
                throw new ArgumentNullException(nameof(stages));
            if (nameFormat == null)
                throw new ArgumentNullException(nameof(nameFormat));

            // 按相位分组(组内保持输入顺序,装配期一次遍历,零 LINQ)
            var groups = new Dictionary<int, List<IPhaseStage>>();
            for (int i = 0; i < stages.Count; i++)
            {
                var stage = stages[i];
                if (stage == null)
                    throw new ArgumentException("stages must not contain null.", nameof(stages));

                if (!groups.TryGetValue(stage.Phase, out var list))
                {
                    list = new List<IPhaseStage>();
                    groups.Add(stage.Phase, list);
                }
                list.Add(stage);
            }

            // 相位升序,每相位一个并行阶段
            var phases = new List<int>(groups.Keys);
            phases.Sort();

            var result = new List<IPipelineStage>(groups.Count);
            for (int i = 0; i < phases.Count; i++)
            {
                int phase = phases[i];
                result.Add(new ParallelStage(groups[phase], string.Format(nameFormat, phase)));
            }
            return result;
        }
    }

    /// <summary>
    /// 管线实现:阶段串行编排 + 进度加权聚合 + 失败/取消传播。
    /// <para>与 Loader(任务级调度)的差异:阶段经 <see cref="PipelineStageContext"/> 主动写入(事件驱动),管线不轮询、不持有帧泵;
    /// 阶段串行逐 await,天然保证 <see cref="RunAsync"/> 返回时无在途阶段任务。</para>
    /// </summary>
    internal sealed class PipelineImpl : IPipeline, IStageContextSink
    {
        #region IPipeline Properties

        public bool IsRunning { get; private set; }

        #endregion

        #region IPipeline Events

        public event Action<PipelineProgress> OnProgressUpdate;
        public event Action OnCompleted;
        public event Action OnCancelled;
        public event Action<string> OnFailed;

        #endregion

        #region Private Fields

        readonly List<IPipelineStage> _stages = new List<IPipelineStage>();

        /// <summary>与 <see cref="_stages"/> 同序的超时配置(秒,0/负值/NaN = 不启用)。</summary>
        readonly List<float> _stageTimeouts = new List<float>();

        /// <summary>
        /// 超时计时任务工厂(测试缝,internal):生产路径为真实墙钟延时(<see cref="DelayType.Realtime"/>,不受 timeScale 影响);
        /// EditMode 测试注入确定性计时器(阻塞式测试中真实延时由 PlayerLoop 泵送,永不触发)。
        /// </summary>
        internal Func<float, CancellationToken, UniTask> TimeoutTaskFactory =
            (seconds, token) => UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.Realtime, cancellationToken: token);

        /// <summary>当前运行装配的阶段上下文(事件驱动聚合的读取源)。</summary>
        PipelineStageContext[] _contexts;

        /// <summary>上一帧广播快照:全局进度 + 描述 + 各阶段状态(阈值节流,首帧 -1 保证必广播)。</summary>
        float _lastOverall = -1f;
        string _lastDesc;
        PipelineStageState[] _lastStates;

        /// <summary>最近一次广播快照。</summary>
        float _overall;
        string _description;
        string _currentStageName;
        string _currentTaskName;
        int _completedStageCount;
        int _failedStageCount;

        #endregion

        #region IPipeline Methods

        public void AddStage(IPipelineStage stage)
        {
            AddStage(stage, 0f);
        }

        public void AddStage(IPipelineStage stage, float timeoutSeconds)
        {
            if (stage == null) return;

            // 避免重复添加(超时在首次添加时一并固化,平行列表同序)
            for (int i = 0; i < _stages.Count; i++)
            {
                if (_stages[i] == stage)
                    return;
            }

            _stages.Add(stage);
            _stageTimeouts.Add(timeoutSeconds);
        }

        public async UniTask RunAsync(CancellationToken cancellationToken = default)
        {
            if (IsRunning)
            {
                Debug.LogWarning("[Pipeline] RunAsync: already running, ignore this call.");
                return;
            }

            if (_stages.Count == 0)
            {
                Debug.LogWarning("[Pipeline] RunAsync: no stages found.");
                OnCompleted?.Invoke();
                return;
            }

            IsRunning = true;

            // 链接外部取消令牌:任一方取消,当前阶段收到已取消的 token
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // 装配阶段上下文(运行期一次分配)
            _contexts = new PipelineStageContext[_stages.Count];
            _lastStates = new PipelineStageState[_stages.Count];
            for (int i = 0; i < _stages.Count; i++)
            {
                _contexts[i] = new PipelineStageContext
                {
                    Owner = this,
                    Name = _stages[i].Name,
                    Weight = _stages[i].Weight,
                };
            }

            // 终局标志:失败/取消/完成三路互斥,取消与失败绝不落入完成块
            bool failed = false;
            bool cancelled = false;
            string failDescription = null;

            try
            {
                for (int i = 0; i < _stages.Count; i++)
                {
                    // 阶段边界取消检查:取消显式走取消终局,不落入完成块
                    if (cts.Token.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }

                    var ctx = _contexts[i];

                    // 顶层阶段计时:System.Diagnostics 全限定(避免与 UnityEngine.Debug 冲突)
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    Debug.Log($"[Pipeline] Stage '{_stages[i].Name}' start");
                    ctx.SetState(PipelineStageState.Executing);

                    // 阶段经共享包装统一执行(异常/取消捕获 + 契约兜底),返回是否以取消结束
                    var stageTask = StageExecution.RunStageAsync(_stages[i], ctx, cts.Token);

                    bool stageCancelled;
                    float timeoutSeconds = _stageTimeouts[i];
                    if (timeoutSeconds > 0f)
                    {
                        // 超时竞速:独立超时 CTS 与链内取消联动(外部取消经链接同步传播给计时任务);
                        // WhenAny 内部观测全部任务,被放弃的在途任务无未观察异常
                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                        var timeoutTask = TimeoutTaskFactory(timeoutSeconds, timeoutCts.Token);

                        var (raceCancelled, race) = await UniTask.WhenAny<bool>(stageTask, timeoutTask).SuppressCancellationThrow();
                        if (raceCancelled)
                        {
                            // 竞速期间外部取消:统一走取消终局
                            Debug.Log($"[Pipeline] Stage '{_stages[i].Name}' cancelled in {sw.Elapsed.TotalMilliseconds:F0}ms");
                            cancelled = true;
                            break;
                        }

                        if (!race.hasResultLeft)
                        {
                            // 超时获胜:中断在途阶段但不等待其沉降(挂死任务不得阻塞管线,断行后终局块与 finally
                            // 之间无 await,被放弃任务无覆写终局广播的窗口)
                            cts.Cancel();
                            ctx.SetDescription($"Stage '{_stages[i].Name}' timed out after {timeoutSeconds}s");
                            ctx.SetState(PipelineStageState.Failed);
                            Debug.Log($"[Pipeline] Stage '{_stages[i].Name}' timed out in {sw.Elapsed.TotalMilliseconds:F0}ms");
                            failed = true;
                            failDescription = ctx.Description;
                            break;
                        }

                        // 阶段先完成:终止悬挂的计时任务
                        timeoutCts.Cancel();
                        stageCancelled = race.result;
                    }
                    else
                    {
                        stageCancelled = await stageTask;
                    }

                    if (stageCancelled)
                    {
                        Debug.Log($"[Pipeline] Stage '{_stages[i].Name}' cancelled in {sw.Elapsed.TotalMilliseconds:F0}ms");
                        cancelled = true;
                        break;
                    }

                    if (ctx.State == PipelineStageState.Failed)
                    {
                        Debug.Log($"[Pipeline] Stage '{_stages[i].Name}' failed in {sw.Elapsed.TotalMilliseconds:F0}ms");
                        failed = true;
                        failDescription = ctx.Description;
                        break;
                    }

                    Debug.Log($"[Pipeline] Stage '{_stages[i].Name}' completed in {sw.Elapsed.TotalMilliseconds:F0}ms");
                }

                // 终局:取消 / 失败 / 完成三路互斥(先重算快照广播,再触发事件)
                if (cancelled)
                {
                    RecalculateSnapshot();
                    Broadcast();
                    OnCancelled?.Invoke();
                    Debug.LogWarning("[Pipeline] Pipeline cancelled.");
                }
                else if (failed)
                {
                    RecalculateSnapshot();
                    Broadcast();
                    OnFailed?.Invoke($"Failed: {failDescription}");
                    Debug.LogError($"[Pipeline] Pipeline failed: {failDescription}");
                }
                else
                {
                    // 完成:全局进度补满并广播(完成语义与各阶段权重无关)
                    _overall = 1f;
                    _description = "Completed";
                    _currentStageName = null;
                    _currentTaskName = null;
                    _completedStageCount = _stages.Count;
                    _failedStageCount = 0;
                    Broadcast();
                    OnCompleted?.Invoke();
                }
            }
            catch (OperationCanceledException)
            {
                // 防御分支:理论上不可达(RunStage 已吞掉全部 OCE);语义统一为取消,绝不静默
                OnCancelled?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Pipeline] RunAsync failed: {ex.Message}\n{ex.StackTrace}");
                OnFailed?.Invoke($"Exception: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
                _contexts = null;
                _lastStates = null;
                _lastOverall = -1f;
                _lastDesc = null;
            }
        }

        public void Destroy()
        {
            _stages.Clear();
            _stageTimeouts.Clear();

            OnProgressUpdate = null;
            OnCompleted = null;
            OnCancelled = null;
            OnFailed = null;

            IsRunning = false;
            _contexts = null;
            _lastStates = null;
        }

        #endregion

        #region Private Methods

        /// <summary>加权聚合快照(只读结构,零 GC):供事件驱动聚合与终局重算共用。</summary>
        private readonly struct StageSnapshot
        {
            /// <summary>全局加权进度 Σ(w·p)/Σ(w)。</summary>
            public readonly float Overall;

            /// <summary>当前执行阶段的描述。</summary>
            public readonly string Description;

            /// <summary>当前执行阶段的名称。</summary>
            public readonly string CurrentStageName;

            /// <summary>当前执行阶段的任务名。</summary>
            public readonly string CurrentTaskName;

            /// <summary>已完成阶段数。</summary>
            public readonly int CompletedCount;

            /// <summary>已失败阶段数。</summary>
            public readonly int FailedCount;

            public StageSnapshot(float overall, string description, string stageName, string taskName, int completed, int failed)
            {
                Overall = overall;
                Description = description;
                CurrentStageName = stageName;
                CurrentTaskName = taskName;
                CompletedCount = completed;
                FailedCount = failed;
            }
        }

        /// <summary>
        /// 计算当前聚合快照:加权 Σ(w·p)/Σ(w),已完成阶段记 w、执行中记 w·p,
        /// 失败阶段权重移出分子与分母、Weight=0 阶段不占进度(每帧路径零 LINQ)。
        /// </summary>
        private StageSnapshot AggregateContexts()
        {
            float weightSum = 0f;
            float weightedSum = 0f;
            int completedCount = 0;
            int failedCount = 0;
            string currentDesc = null;
            string currentStageName = null;
            string currentTaskName = null;

            for (int i = 0; i < _contexts.Length; i++)
            {
                var ctx = _contexts[i];

                switch (ctx.State)
                {
                    case PipelineStageState.Completed:
                        completedCount++;
                        weightSum += ctx.Weight;
                        weightedSum += ctx.Weight;
                        break;
                    case PipelineStageState.Failed:
                        failedCount++;
                        break;
                    case PipelineStageState.Executing:
                        weightSum += ctx.Weight;
                        weightedSum += ctx.Weight * ctx.Progress;
                        currentDesc = ctx.Description;
                        currentStageName = ctx.Name;
                        currentTaskName = ctx.CurrentTaskName;
                        break;
                    default:
                        break;
                }
            }

            return new StageSnapshot(
                weightSum > 0f ? weightedSum / weightSum : 0f,
                currentDesc, currentStageName, currentTaskName,
                completedCount, failedCount);
        }

        /// <summary>
        /// 阶段写入触发的聚合入口(<see cref="IStageContextSink"/> 实现,事件驱动,零闭包):
        /// 加权聚合 Σ(w·p)/Σ(w),阈值节流后广播。
        /// <para>已完成阶段记 w,执行中记 w·p;失败阶段权重移出分子与分母;Weight=0 阶段不占进度。</para>
        /// </summary>
        public void OnStageContextChanged(PipelineStageContext changed)
        {
            if (!IsRunning) return;

            StageSnapshot snap = AggregateContexts();

            // 阈值节流:总体进度变化 ≥1% || 描述变化 || 任一阶段状态变化(每帧路径零 LINQ)
            bool dirty = Mathf.Abs(snap.Overall - _lastOverall) >= 0.01f;
            if (!dirty && snap.Description != _lastDesc)
                dirty = true;
            if (!dirty)
            {
                for (int i = 0; i < _contexts.Length; i++)
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
                _overall = snap.Overall;
                _description = snap.Description;
                _currentStageName = snap.CurrentStageName;
                _currentTaskName = snap.CurrentTaskName;
                _completedStageCount = snap.CompletedCount;
                _failedStageCount = snap.FailedCount;

                _lastOverall = snap.Overall;
                _lastDesc = snap.Description;
                for (int i = 0; i < _contexts.Length; i++) _lastStates[i] = _contexts[i].State;

                Broadcast();
            }
        }

        /// <summary>无条件重算最新聚合快照(终局广播前调用,不受节流限制)。</summary>
        private void RecalculateSnapshot()
        {
            if (_contexts == null) return;

            StageSnapshot snap = AggregateContexts();

            _overall = snap.Overall;
            _description = snap.Description;
            _currentStageName = snap.CurrentStageName;
            _currentTaskName = snap.CurrentTaskName;
            _completedStageCount = snap.CompletedCount;
            _failedStageCount = snap.FailedCount;
        }

        /// <summary>构造进度快照并广播。</summary>
        private void Broadcast()
        {
            var progress = new PipelineProgress
            {
                OverallProgress = _overall,
                Description = _description ?? "Completed",
                CurrentStageName = _currentStageName,
                CurrentTaskName = _currentTaskName,
                TotalStageCount = _stages.Count,
                CompletedStageCount = _completedStageCount,
                FailedStageCount = _failedStageCount,
            };
            OnProgressUpdate?.Invoke(progress);
        }

        #endregion
    }
}
