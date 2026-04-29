using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// 加载协调器。纯 C# 类，作为加载任务的调度器。
    /// <para>通过 <see cref="ILoadCoordinator"/> 接口对外暴露，外部不可直接访问此类。</para>
    /// </summary>
    class LoadCoordinator : ILoadCoordinator, ILoadCollector
    {
        #region ILoadCoordinator Properties

        public bool IsLoading { get; private set; }
        public float Progress { get; private set; }
        public string Description { get; private set; }

        #endregion

        #region ILoadCoordinator Events

        public event Action<LoadProgressSnapshot> OnProgressUpdate;
        public event Action OnLoadCompleted;
        public event Action<string> OnLoadFailed;

        #endregion

        #region ILoadCollector (显式接口实现)

        void ILoadCollector.AddLoadable(ILoadable loadable)
        {
            if (loadable != null && !_loadables.Contains(loadable))
            {
                _loadables.Add(loadable);
            }
        }

        #endregion

        #region ILoadCoordinator Methods

        public void AddProvider(ILoadableProvider provider)
        {
            if (provider != null && !_providers.Contains(provider))
            {
                _providers.Add(provider);
            }
        }

        public async UniTask LoadAsync()
        {
            if (IsLoading)
            {
                Debug.LogWarning("LoadCoordinator.LoadAsync: already loading, ignore this call.");
                return;
            }

            // 1. 装载阶段：从所有 provider 中解析出加载任务
            _loadables.Clear();
            for (int i = 0; i < _providers.Count; i++)
            {
                _providers[i].MountLoadables(this);
            }

            if (_loadables.Count == 0)
            {
                Debug.LogWarning("LoadCoordinator.LoadAsync: no loadable tasks found.");
                OnLoadCompleted?.Invoke();
                return;
            }

            IsLoading = true;
            Progress = 0f;
            Description = "Loading...";

            // 创建 CancellationTokenSource 用于失败时取消其他任务
            using var cts = new CancellationTokenSource();

            try
            {
                // 2. 计算总权重
                float totalWeight = 0f;
                for (int i = 0; i < _loadables.Count; i++)
                {
                    totalWeight += _loadables[i].Weight;
                }

                if (totalWeight <= 0f)
                {
                    totalWeight = _loadables.Count;
                }

                // 3. 同时启动所有加载任务
                UniTask[] tasks = new UniTask[_loadables.Count];
                for (int i = 0; i < _loadables.Count; i++)
                {
                    tasks[i] = _loadables[i].LoadAsync();
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

                    for (int i = 0; i < _loadables.Count; i++)
                    {
                        var loadable = _loadables[i];
                        float p = loadable.Progress;
                        weightedProgress += p * loadable.Weight;

                        switch (loadable.State)
                        {
                            case LoadState.Completed:
                                completedCount++;
                                break;
                            case LoadState.Failed:
                                failedCount++;
                                anyFailed = true;
                                currentDesc = loadable.Description;
                                currentTaskName = loadable.Name;
                                break;
                            case LoadState.Loading:
                                allDone = false;
                                currentDesc = loadable.Description;
                                currentTaskName = loadable.Name;
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
                        TotalTaskCount = _loadables.Count,
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
                    TotalTaskCount = _loadables.Count,
                    CompletedCount = _loadables.Count,
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
                Debug.LogError($"LoadCoordinator.LoadAsync failed: {ex.Message}\n{ex.StackTrace}");
                OnLoadFailed?.Invoke($"Exception: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void Destroy()
        {
            _loadables.Clear();
            _providers.Clear();

            OnProgressUpdate = null;
            OnLoadCompleted = null;
            OnLoadFailed = null;

            IsLoading = false;
            Progress = 0f;
            Description = null;
        }

        #endregion

        #region Private Fields

        /// <summary>
        /// 加载任务列表。生命周期：
        /// <para>1. 装载阶段：由 <see cref="ILoadCollector.AddLoadable"/> 填充。</para>
        /// <para>2. 执行阶段：直接用于进度轮询。</para>
        /// </summary>
        List<ILoadable> _loadables = new List<ILoadable>();

        /// <summary>已注册的 <see cref="ILoadableProvider"/> 列表。</summary>
        List<ILoadableProvider> _providers = new List<ILoadableProvider>();

        #endregion
    }
}
