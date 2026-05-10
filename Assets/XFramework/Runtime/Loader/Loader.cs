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

        public event Action<LoadContext> OnProgressUpdate;
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

        public async UniTask LoadAsync()
        {
            if (IsLoading)
            {
                Debug.LogWarning("Loader.LoadAsync: already loading, ignore this call.");
                return;
            }

            if (_entries.Count == 0)
            {
                Debug.LogWarning("Loader.LoadAsync: no loadable tasks found.");
                OnLoadCompleted?.Invoke();
                return;
            }

            IsLoading = true;

            // 创建 CancellationTokenSource 用于失败时取消其他任务
            var cts = new CancellationTokenSource();

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
                    if (cts.Token.IsCancellationRequested)
                        break;

                    var tasks = group.ToList();
                    int taskCount = tasks.Count;

                    // 为当前 Phase 的每个任务创建临时 Context
                    var phaseContexts = tasks.Select(t => new LoadContext
                    {
                        Name = t.GetType().Name,
                    }).ToList();

                    // 启动所有任务
                    var loadTasks = new UniTask[taskCount];
                    for (int i = 0; i < taskCount; i++)
                    {
                        var ctx = phaseContexts[i];
                        ctx.SetState(LoadState.Loading);
                        loadTasks[i] = tasks[i].LoadAsync(ctx, cts.Token);
                    }

                    // 轮询阶段：每帧检查进度，直到全部完成或失败
                    while (!cts.Token.IsCancellationRequested)
                    {
                        bool allDone = true;
                        bool anyFailed = false;
                        string currentDesc = null;
                        string currentTaskName = null;
                        float totalProgress = 0f;
                        int completedCount = 0;
                        int failedCount = 0;

                        for (int i = 0; i < taskCount; i++)
                        {
                            var ctx = phaseContexts[i];

                            switch (ctx.State)
                            {
                                case LoadState.Completed:
                                    completedCount++;
                                    totalProgress += 1f;
                                    break;
                                case LoadState.Failed:
                                    failedCount++;
                                    anyFailed = true;
                                    totalProgress += 1f;
                                    currentDesc = ctx.Description;
                                    currentTaskName = ctx.Name;
                                    break;
                                case LoadState.Loading:
                                    allDone = false;
                                    totalProgress += ctx.Progress;
                                    currentDesc = ctx.Description;
                                    currentTaskName = ctx.Name;
                                    break;
                                default:
                                    allDone = false;
                                    break;
                            }
                        }

                        // 计算总体进度
                        float phaseProgress = taskCount > 0 ? totalProgress / taskCount : 1f;
                        float overallProgress = (completedGroups + phaseProgress) / totalGroups;

                        // 填充共享 Context 并广播
                        _context.OverallProgress = overallProgress;
                        _context.Description = currentDesc ?? "Completed";
                        _context.CurrentTaskName = currentTaskName;
                        _context.TotalTaskCount = _entries.Count;
                        _context.CompletedCount = completedCount;
                        _context.FailedCount = failedCount;
                        OnProgressUpdate?.Invoke(_context);

                        if (anyFailed)
                        {
                            cts.Cancel();
                            OnLoadFailed?.Invoke($"Failed: {currentDesc}");
                            return;
                        }

                        if (allDone)
                            break;

                        await UniTask.Yield(PlayerLoopTiming.Update);
                    }

                    completedGroups++;
                }

                // 3. 完成
                _context.OverallProgress = 1f;
                _context.Description = "Completed";
                _context.CurrentTaskName = null;
                _context.TotalTaskCount = _entries.Count;
                _context.CompletedCount = _entries.Count;
                _context.FailedCount = 0;
                OnProgressUpdate?.Invoke(_context);
                OnLoadCompleted?.Invoke();
            }
            catch (OperationCanceledException)
            {
                // 取消操作是预期的，不做额外处理
            }
            catch (Exception ex)
            {
                Debug.LogError($"Loader.LoadAsync failed: {ex.Message}\n{ex.StackTrace}");
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

        #region Private Fields

        readonly List<ILoadable> _entries = new List<ILoadable>();
        readonly LoadContext _context = new LoadContext();

        #endregion
    }
}
