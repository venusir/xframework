using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework.XLoader
{

    /// <summary>
    /// 加载器。纯 C# 类，作为加载任务的调度器。
    /// <para>通过 <see cref="ILoader"/> 接口对外暴露，外部不可直接访问此类。</para>
    /// <para>按 <see cref="ILoadable.Phase"/> 分组调度：相同 Phase 并行，不同 Phase 串行。</para>
    /// </summary>
    class Loader : ILoader
    {
        #region ILoader Properties

        public bool IsLoading { get; private set; }

        #endregion

        #region ILoader Events

        public event Action<LoadProgress> OnProgressUpdate;
        public event Action OnLoadCompleted;
        public event Action<string> OnLoadFailed;

        #endregion

        #region ILoader Methods

        public void AddLoadable(ILoadable loadable)
        {
            if (loadable == null) return;

            // 避免重复添加
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i] == loadable)
                    return;
            }

            _entries.Add(loadable);
        }

        /// <summary>
        /// 执行加载。按 <see cref="ILoadable.Phase"/> 分组调度所有已注册的加载任务。
        /// </summary>
        /// <param name="cancellationToken">取消令牌:取消后当前组任务收到已取消的 token,尚未开始的后续组不再执行,
        /// 触发 <see cref="OnLoadFailed"/>("Load cancelled."),不触发 <see cref="OnLoadCompleted"/>。</param>
        public async UniTask LoadAsync(CancellationToken cancellationToken = default)
        {
            if (IsLoading)
            {
                Debug.LogWarning("[Loader] LoadAsync: already loading, ignore this call.");
                return;
            }

            if (_entries.Count == 0)
            {
                Debug.LogWarning("[Loader] LoadAsync: no loadable tasks found.");
                OnLoadCompleted?.Invoke();
                return;
            }

            IsLoading = true;
            _startTime = Time.realtimeSinceStartup;

            // 链接外部取消令牌与内部失败取消源:任一方取消,全链路收到已取消的 token
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // 累积所有任务的包装任务,结束时等待沉降,保证 LoadAsync 返回后无在途任务
            var wrappers = new List<UniTask>(_entries.Count);

            // 终局标志:失败/取消/完成三路互斥,取消与失败绝不落入完成块
            bool failed = false;
            bool cancelled = false;
            string failDescription = null;
            string failTaskName = null;

            try
            {
                // 1. 按 Phase 分组并排序
                var groups = _entries
                    .GroupBy(e => e.Phase)
                    .OrderBy(g => g.Key)
                    .ToList();

                int totalGroups = groups.Count;
                int completedGroups = 0;

                // 2. 逐 Phase 执行
                foreach (var group in groups)
                {
                    // 组间取消检查:取消显式走取消终局,不落入完成块
                    if (cts.Token.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }

                    var tasks = group.ToList();
                    int taskCount = tasks.Count;

                    // 为当前 Phase 的每个任务创建临时 Context,任务经包装函数统一执行
                    // (包装函数负责状态机写入与异常捕获,保证任务必然收敛到终态)
                    var phaseContexts = new LoadProgress[taskCount];
                    for (int i = 0; i < taskCount; i++)
                    {
                        phaseContexts[i] = new LoadProgress
                        {
                            Name = tasks[i].GetType().Name,
                        };
                        wrappers.Add(RunTask(tasks[i], phaseContexts[i], cts.Token));
                    }

                    // 上一帧广播快照:各任务状态数组 + 总体进度 + 描述(阈值节流,首帧 lastOverall=-1 保证必广播)
                    var lastStates = new LoadState[taskCount];
                    float lastOverall = -1f;
                    string lastDesc = null;

                    // 轮询阶段:每帧检查进度,直到全部完成或失败
                    while (!cts.Token.IsCancellationRequested)
                    {
                        bool allDone = true;
                        bool anyFailed = false;
                        string currentDesc = null;
                        string currentTaskName = null;
                        float weightSum = 0f;
                        float weightedSum = 0f;
                        int completedCount = 0;
                        int failedCount = 0;

                        for (int i = 0; i < taskCount; i++)
                        {
                            var ctx = phaseContexts[i];

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
                                    if (failDescription == null)
                                    {
                                        failDescription = ctx.Description;
                                        failTaskName = ctx.Name;
                                    }
                                    break;
                                case LoadState.Loading:
                                    allDone = false;
                                    weightSum += ctx.Weight;
                                    weightedSum += ctx.Weight * ctx.Progress;
                                    currentDesc = ctx.Description;
                                    currentTaskName = ctx.Name;
                                    break;
                                default:
                                    allDone = false;
                                    break;
                            }
                        }

                        // 组内加权聚合 Σ(w·p)/Σ(w);组间等权均摊(跨组加权需预知后续组权重,串行调度下不可行;
                        // 全部权重为 1f 时与旧算术平均完全一致)
                        float phaseProgress = weightSum > 0f ? weightedSum / weightSum : 0f;
                        float overallProgress = (completedGroups + phaseProgress) / totalGroups;

                        // 阈值广播:总体进度变化 ≥1%、任一任务状态变化、描述变化三者其一才广播(每帧路径零 LINQ)
                        bool dirty = Mathf.Abs(overallProgress - lastOverall) >= 0.01f;
                        if (!dirty)
                        {
                            for (int i = 0; i < taskCount; i++)
                            {
                                if (phaseContexts[i].State != lastStates[i])
                                {
                                    dirty = true;
                                    break;
                                }
                            }
                        }
                        if (!dirty && currentDesc != lastDesc)
                            dirty = true;

                        if (dirty)
                        {
                            _context.OverallProgress = overallProgress;
                            _context.Description = currentDesc ?? "Completed";
                            _context.CurrentTaskName = currentTaskName;
                            _context.TotalTaskCount = _entries.Count;
                            _context.CompletedCount = completedCount;
                            _context.FailedCount = failedCount;

                            lastOverall = overallProgress;
                            lastDesc = currentDesc ?? "Completed";
                            for (int i = 0; i < taskCount; i++) lastStates[i] = phaseContexts[i].State;

                            OnProgressUpdate?.Invoke(_context);
                        }

                        if (anyFailed)
                        {
                            failed = true;
                            cts.Cancel();
                            break;
                        }

                        if (allDone)
                            break;

                        await _framePump();
                    }

                    if (failed)
                        break;

                    if (cts.Token.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }

                    completedGroups++;
                }

                // 3. 终局:取消 / 失败 / 完成三路互斥(先广播快照,再触发事件)
                if (cancelled)
                {
                    OnProgressUpdate?.Invoke(_context);
                    OnLoadFailed?.Invoke("Load cancelled.");
                    Debug.LogWarning("[Loader] Load cancelled.");
                }
                else if (failed)
                {
                    OnProgressUpdate?.Invoke(_context);
                    OnLoadFailed?.Invoke($"Failed: {failDescription}");
                    Debug.LogError($"[Loader] Load failed: {failTaskName} ({Time.realtimeSinceStartup - _startTime:F2}s): {failDescription}");
                }
                else
                {
                    // 先等待全部任务沉降,保证 OnLoadCompleted 时无在途任务
                    try { await UniTask.WhenAll(wrappers); }
                    catch (OperationCanceledException) { }
                    catch (Exception) { }

                    _context.OverallProgress = 1f;
                    _context.Description = "Completed";
                    _context.CurrentTaskName = null;
                    _context.TotalTaskCount = _entries.Count;
                    _context.CompletedCount = _entries.Count;
                    _context.FailedCount = 0;
                    OnProgressUpdate?.Invoke(_context);
                    OnLoadCompleted?.Invoke();
                }

                // 失败/取消收尾:等待全部在途任务沉降(RunTask 已吞掉全部异常,WhenAll 不会 fault)
                if (failed || cancelled)
                {
                    try { await UniTask.WhenAll(wrappers); }
                    catch (OperationCanceledException) { }
                    catch (Exception) { }
                }
            }
            catch (OperationCanceledException)
            {
                // 防御分支:理论上不可达(RunTask 已吞掉全部 OCE);语义统一为取消,绝不静默
                OnLoadFailed?.Invoke("Load cancelled.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Loader] LoadAsync failed: {ex.Message}\n{ex.StackTrace}");
                OnLoadFailed?.Invoke($"Exception: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void Destroy()
        {
            _entries.Clear();

            OnProgressUpdate = null;
            OnLoadCompleted = null;
            OnLoadFailed = null;

            IsLoading = false;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 包装执行单个加载任务:统一状态机写入、异常与取消捕获,保证任务必然收敛到终态。
        /// <para>正常返回但未写终态(如未调用 SetState 的实现)时自动补置为 <see cref="LoadState.Completed"/>,进度视为 1f;</para>
        /// <para>抛出的异常置为 <see cref="LoadState.Failed"/> 并写入描述,经 <see cref="OnLoadFailed"/> 报告;</para>
        /// <para>抛出 <see cref="OperationCanceledException"/> 视为取消,保持当前状态,由 Loader 统一走取消路径。</para>
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
                // 取消:保持当前状态,由 Loader 统一走取消路径,不视为失败
            }
            catch (Exception ex)
            {
                ctx.SetState(LoadState.Failed);
                ctx.SetDescription(ex.Message);
            }
        }

        #endregion

        #region Private Fields

        readonly List<ILoadable> _entries = new List<ILoadable>();
        readonly LoadProgress _context = new LoadProgress();

        /// <summary>本次加载启动时刻(真实秒),用于失败日志的耗时统计。</summary>
        float _startTime;

        /// <summary>
        /// 帧泵注入缝:每轮询一帧后等待一次。默认等待下一帧 Update;
        /// 测试可替换为手动泵,在 EditMode(无 PlayerLoop 泵)环境中确定性驱动轮询。
        /// </summary>
        internal Func<UniTask> _framePump = () => UniTask.Yield(PlayerLoopTiming.Update, CancellationToken.None);

        #endregion
    }
}
