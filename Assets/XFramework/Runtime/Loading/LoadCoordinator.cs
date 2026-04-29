using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework
{
    #region Enums

    /// <summary>
    /// 加载状态。
    /// </summary>
    public enum LoadState
    {
        /// <summary>等待中。</summary>
        Pending,

        /// <summary>加载中。</summary>
        Loading,

        /// <summary>已完成。</summary>
        Completed,

        /// <summary>失败。</summary>
        Failed
    }

    #endregion

    #region Interfaces

    /// <summary>
    /// 可加载接口。实现此接口的对象可被 <see cref="ILoadCoordinator"/> 统一调度执行。
    /// </summary>
    public interface ILoadable
    {
        /// <summary>当前加载进度，取值范围 0.0 ~ 1.0。</summary>
        float Progress { get; }

        /// <summary>当前加载阶段的描述文字。</summary>
        string Description { get; }

        /// <summary>当前加载状态。</summary>
        LoadState State { get; }

        /// <summary>
        /// 加载权重。影响此任务在总进度中的占比。
        /// <para>所有任务的权重之和作为分母，单个任务的权重作为分子。</para>
        /// </summary>
        float Weight { get; }

        /// <summary>
        /// 异步加载任务。加载过程中应持续更新 <see cref="Progress"/> 和 <see cref="State"/>。
        /// </summary>
        UniTask LoadAsync();
    }

    /// <summary>
    /// 加载任务收集器。供 <see cref="ILoadableProvider"/> 装载任务用，仅暴露 <see cref="AddLoadable"/>。
    /// <para>防止 provider 误调用 <see cref="ILoadCoordinator"/> 的其他方法。</para>
    /// </summary>
    public interface ILoadCollector
    {
        /// <summary>装载一个加载任务。</summary>
        void AddLoadable(ILoadable loadable);
    }

    /// <summary>
    /// 加载任务提供者接口。节点实现此接口，通过 <see cref="MountLoadables"/> 向 <see cref="ILoadCoordinator"/> 装载加载任务。
    /// <para>节点本身不实现 <see cref="ILoadable"/>，而是提供一组纯 C# 的加载器，不破坏节点的继承结构。</para>
    /// </summary>
    public interface ILoadableProvider
    {
        /// <summary>
        /// 向 <paramref name="collector"/> 装载加载任务。
        /// <para>通过 <c>collector.AddLoadable(...)</c> 添加纯 C# 的 <see cref="LoadableTask"/> 对象。</para>
        /// </summary>
        /// <param name="collector">加载任务收集器。</param>
        void MountLoadables(ILoadCollector collector);
    }

    /// <summary>
    /// 加载协调器接口。对外暴露的加载调度入口，隐藏 <see cref="LoadCoordinator"/> 实现。
    /// <para>通过 <see cref="AddProvider"/> 注册 <see cref="ILoadableProvider"/>，调用 <see cref="LoadAsync"/> 统一调度。</para>
    /// </summary>
    public interface ILoadCoordinator
    {
        /// <summary>是否正在加载中。</summary>
        bool IsLoading { get; }

        /// <summary>当前总体进度，取值范围 0.0 ~ 1.0。</summary>
        float Progress { get; }

        /// <summary>当前加载阶段的描述文字。</summary>
        string Description { get; }

        /// <summary>加载进度变更事件。参数为 (整体进度 0~1, 当前描述文字)。</summary>
        event Action<float, string> OnProgressUpdate;

        /// <summary>全部加载完成事件。</summary>
        event Action OnLoadCompleted;

        /// <summary>加载失败事件。参数为失败原因描述。</summary>
        event Action<string> OnLoadFailed;

        /// <summary>
        /// 注册一个 <see cref="ILoadableProvider"/>，其提供的加载任务将在 <see cref="LoadAsync"/> 时被装载。
        /// </summary>
        void AddProvider(ILoadableProvider provider);

        /// <summary>
        /// 执行加载。装载所有已注册的 <see cref="ILoadableProvider"/> 提供的加载任务并统一调度。
        /// </summary>
        UniTask LoadAsync();

        /// <summary>
        /// 销毁加载器，清理内部状态和事件订阅。
        /// <para>调用后不应再使用此实例。</para>
        /// </summary>
        void Destroy();
    }

    #endregion

    #region LoadableTask

    /// <summary>
    /// 加载任务基类。实现 <see cref="ILoadable"/> 并提供属性默认实现。
    /// <para>纯 C# 类，不继承节点。子类只需重写 <see cref="LoadAsync"/> 实现具体加载逻辑。</para>
    /// <para>自动管理 State 流转：Pending → Loading → Completed / Failed。</para>
    /// </summary>
    public abstract class LoadableTask : ILoadable
    {
        #region Public Properties

        /// <summary>当前加载进度，取值范围 0.0 ~ 1.0。</summary>
        public float Progress { get; private set; }

        /// <summary>当前加载阶段的描述文字。</summary>
        public string Description { get; private set; }

        /// <summary>当前加载状态。</summary>
        public LoadState State { get; private set; } = LoadState.Pending;

        /// <summary>
        /// 加载权重。子类可在构造函数中修改此值。
        /// </summary>
        public float Weight { get; protected set; } = 1f;

        #endregion

        #region ILoadable Implementation

        async UniTask ILoadable.LoadAsync()
        {
            SetState(LoadState.Loading);

            try
            {
                await LoadAsync();

                if (State != LoadState.Failed)
                {
                    SetProgress(1f);
                    SetState(LoadState.Completed);
                }
            }
            catch (Exception ex)
            {
                SetDescription($"加载异常: {ex.Message}");
                SetState(LoadState.Failed);
            }
        }

        #endregion

        #region Protected Methods

        /// <summary>
        /// 子类在此实现具体的加载逻辑。
        /// <para>加载过程中应调用 <see cref="SetProgress"/>、<see cref="SetDescription"/> 更新进度和描述。</para>
        /// <para>如果加载失败，可调用 <see cref="SetState"/>(<see cref="LoadState.Failed"/>) 标记失败，或直接抛出异常由基类统一处理。</para>
        /// </summary>
        protected abstract UniTask LoadAsync();

        /// <summary>设置当前加载进度，会自动 clamp 到 0~1 范围。</summary>
        protected void SetProgress(float progress)
        {
            Progress = Mathf.Clamp01(progress);
        }

        /// <summary>设置当前加载阶段的描述文字。</summary>
        protected void SetDescription(string description)
        {
            Description = description;
        }

        /// <summary>设置当前加载状态。</summary>
        protected void SetState(LoadState state)
        {
            State = state;
        }

        #endregion
    }

    #endregion

    #region LoadCoordinator

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

        public event Action<float, string> OnProgressUpdate;
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
                    bool allDone = true;
                    bool anyFailed = false;

                    for (int i = 0; i < _loadables.Count; i++)
                    {
                        var loadable = _loadables[i];
                        float p = loadable.Progress;
                        weightedProgress += p * loadable.Weight;

                        if (loadable.State == LoadState.Failed)
                        {
                            anyFailed = true;
                            currentDesc = loadable.Description;
                        }

                        if (p < 1f || loadable.State != LoadState.Completed)
                        {
                            allDone = false;
                            currentDesc = loadable.Description;
                        }
                    }

                    float overallProgress = Mathf.Clamp01(weightedProgress / totalWeight);
                    Progress = overallProgress;
                    Description = currentDesc ?? "Completed";

                    OnProgressUpdate?.Invoke(Progress, Description);

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
                OnProgressUpdate?.Invoke(Progress, Description);
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

    #endregion
}
