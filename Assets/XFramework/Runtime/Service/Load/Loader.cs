using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// 加载器。纯 C# 类，作为加载任务的调度器。
    /// <para>通过 <see cref="ILoader"/> 接口对外暴露，外部不可直接访问此类。</para>
    /// </summary>
    class Loader : ILoader
    {
        #region ILoader Properties

        public bool IsLoading { get; private set; }
        public float Progress { get; private set; }
        public string Description { get; private set; }

        #endregion

        #region ILoader Events

        public event Action<LoadProgressSnapshot> OnProgressUpdate;
        public event Action OnLoadCompleted;
        public event Action<string> OnLoadFailed;

        #endregion

        #region ILoader Methods

        public void AddLoadable(ILoadable loadable, string name = null, float weight = 1f)
        {
            if (loadable == null) return;

            // 避免重复添加
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Loadable == loadable)
                    return;
            }

            _entries.Add(new LoadEntry
            {
                Loadable = loadable,
                Context = new LoadContext
                {
                    Name = name ?? loadable.GetType().Name,
                    Weight = Mathf.Max(0.01f, weight),
                }
            });
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
            Progress = 0f;
            Description = "Loading...";

            // 创建 CancellationTokenSource 用于失败时取消其他任务
            var cts = new CancellationTokenSource();

            try
            {
                // 1. 计算总权重
                float totalWeight = 0f;
                for (int i = 0; i < _entries.Count; i++)
                {
                    totalWeight += _entries[i].Context.Weight;
                }

                if (totalWeight <= 0f)
                {
                    totalWeight = _entries.Count;
                }

                // 2. 将所有任务标记为 Loading 状态
                for (int i = 0; i < _entries.Count; i++)
                {
                    _entries[i].Context.SetState(LoadState.Loading);
                }

                // 3. 同时启动所有加载任务，传入 Context 和 CancellationToken
                UniTask[] tasks = new UniTask[_entries.Count];
                for (int i = 0; i < _entries.Count; i++)
                {
                    var entry = _entries[i];
                    tasks[i] = entry.Loadable.LoadAsync(entry.Context, cts.Token);
                }

                // 4. 轮询阶段：每帧检查进度，直到全部完成或失败
                while (!cts.Token.IsCancellationRequested)
                {
                    float weightedProgress = 0f;
                    string currentDesc = null;
                    string currentTaskName = null;
                    bool allDone = true;
                    bool anyFailed = false;
                    int completedCount = 0;
                    int failedCount = 0;

                    for (int i = 0; i < _entries.Count; i++)
                    {
                        var ctx = _entries[i].Context;
                        float p = ctx.Progress;
                        weightedProgress += p * ctx.Weight;

                        switch (ctx.State)
                        {
                            case LoadState.Completed:
                                completedCount++;
                                break;
                            case LoadState.Failed:
                                failedCount++;
                                anyFailed = true;
                                currentDesc = ctx.Description;
                                currentTaskName = ctx.Name;
                                break;
                            case LoadState.Loading:
                                allDone = false;
                                currentDesc = ctx.Description;
                                currentTaskName = ctx.Name;
                                break;
                            default:
                                allDone = false;
                                break;
                        }
                    }

                    float overallProgress = Mathf.Clamp01(weightedProgress / totalWeight);
                    Progress = overallProgress;
                    Description = currentDesc ?? "Completed";

                    // 生成快照并广播
                    var snapshot = new LoadProgressSnapshot
                    {
                        OverallProgress = Progress,
                        Description = Description,
                        CurrentTaskName = currentTaskName,
                        TotalTaskCount = _entries.Count,
                        CompletedCount = completedCount,
                        FailedCount = failedCount,
                    };
                    OnProgressUpdate?.Invoke(snapshot);

                    if (anyFailed)
                    {
                        // 取消其他仍在运行的任务
                        cts.Cancel();
                        OnLoadFailed?.Invoke($"Failed: {currentDesc}");
                        return;
                    }

                    if (allDone)
                        break;

                    await UniTask.Yield(PlayerLoopTiming.Update);
                }

                // 5. 完成
                Progress = 1f;
                Description = "Completed";
                var finalSnapshot = new LoadProgressSnapshot
                {
                    OverallProgress = 1f,
                    Description = "Completed",
                    CurrentTaskName = null,
                    TotalTaskCount = _entries.Count,
                    CompletedCount = _entries.Count,
                    FailedCount = 0,
                };
                OnProgressUpdate?.Invoke(finalSnapshot);
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
            Progress = 0f;
            Description = null;
        }

        #endregion

        #region Private Types

        /// <summary>
        /// 加载条目。将 <see cref="ILoadable"/> 与 <see cref="LoadContext"/> 配对存储。
        /// </summary>
        private struct LoadEntry
        {
            public ILoadable Loadable;
            public LoadContext Context;
        }

        #endregion

        #region Private Fields

        List<LoadEntry> _entries = new List<LoadEntry>();

        #endregion
    }
}
