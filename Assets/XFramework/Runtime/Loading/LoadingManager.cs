using System;
using System.Collections.Generic;
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
    /// 可加载接口。实现此接口的对象可被 <see cref="LoadingManager"/> 统一调度执行。
    /// <para>不强制要求实现者必须是 <see cref="BaseNode"/>，可以是任何纯 C# 对象。</para>
    /// </summary>
    public interface ILoadable
    {
        /// <summary>
        /// 当前加载进度，取值范围 0.0 ~ 1.0。
        /// </summary>
        float Progress { get; }

        /// <summary>
        /// 当前加载阶段的描述文字。
        /// <para>例如 "正在加载配置..."、"正在初始化资源..."。</para>
        /// </summary>
        string Description { get; }

        /// <summary>
        /// 当前加载状态。
        /// </summary>
        LoadState State { get; }

        /// <summary>
        /// 加载权重。影响此任务在总进度中的占比。
        /// <para>所有任务的权重之和作为分母，单个任务的权重作为分子。</para>
        /// </summary>
        float Weight { get; }

        /// <summary>
        /// 异步加载任务。
        /// <para>加载过程中应持续更新 <see cref="Progress"/> 和 <see cref="State"/>。</para>
        /// </summary>
        /// <returns>加载任务。</returns>
        UniTask LoadAsync();
    }

    #endregion

    #region LoadingManager

    /// <summary>
    /// 加载管理器。负责统一调度一批 <see cref="ILoadable"/> 任务，并行执行并汇报进度。
    /// </summary>
    public class LoadingManager : LeafNode
    {
        #region Public Properties

        /// <summary>
        /// 是否正在加载中。
        /// </summary>
        public bool IsLoading { get; private set; }

        /// <summary>
        /// 当前总体进度，取值范围 0.0 ~ 1.0。
        /// </summary>
        public float Progress { get; private set; }

        /// <summary>
        /// 当前加载阶段的描述文字。
        /// </summary>
        public string Description { get; private set; }

        #endregion

        #region Events

        /// <summary>
        /// 加载进度变更事件。参数为 (整体进度 0~1, 当前描述文字)。
        /// </summary>
        public event Action<float, string> OnProgressUpdate;

        /// <summary>
        /// 全部加载完成事件。
        /// </summary>
        public event Action OnLoadCompleted;

        /// <summary>
        /// 加载失败事件。参数为失败原因描述。
        /// </summary>
        public event Action<string> OnLoadFailed;

        #endregion

        #region Public Methods

        /// <summary>
        /// 统一的加载入口。接收一批 <see cref="ILoadable"/> 任务，并行执行并汇报进度。
        /// <para>如果正在加载中，会直接返回并输出警告。</para>
        /// </summary>
        /// <param name="loadables">待执行的加载任务列表。</param>
        /// <param name="phaseDescriptions">
        /// 可选：加载阶段描述列表。如果提供，会在加载过程中按阶段切换描述文字。
        /// <para>例如 ["正在初始化资源...", "正在加载场景...", "正在准备数据..."]。</para>
        /// </param>
        /// <returns>加载任务。</returns>
        public async UniTask ExecuteLoadAsync(IList<ILoadable> loadables, IList<string> phaseDescriptions = null)
        {
            if (loadables == null || loadables.Count == 0)
            {
                Debug.LogWarning("LoadingManager.ExecuteLoadAsync: loadables is null or empty.");
                OnLoadCompleted?.Invoke();
                return;
            }

            if (IsLoading)
            {
                Debug.LogWarning("LoadingManager.ExecuteLoadAsync: already loading, ignore this call.");
                return;
            }

            IsLoading = true;
            Progress = 0f;
            Description = phaseDescriptions?[0] ?? "加载中...";

            try
            {
                // 1. 计算总权重
                float totalWeight = 0f;
                for (int i = 0; i < loadables.Count; i++)
                {
                    totalWeight += loadables[i].Weight;
                }

                if (totalWeight <= 0f)
                {
                    totalWeight = loadables.Count;
                }

                // 2. 同时启动所有加载任务
                UniTask[] tasks = new UniTask[loadables.Count];
                for (int i = 0; i < loadables.Count; i++)
                {
                    tasks[i] = loadables[i].LoadAsync();
                }

                // 3. 每帧轮询进度，直到全部加载完成
                while (true)
                {
                    float weightedProgress = 0f;
                    string currentDesc = null;
                    bool allDone = true;
                    bool anyFailed = false;

                    for (int i = 0; i < loadables.Count; i++)
                    {
                        var loadable = loadables[i];
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
                    Description = currentDesc ?? "加载完成";

                    // 如果提供了阶段描述，根据进度切换描述
                    if (phaseDescriptions != null && phaseDescriptions.Count > 0)
                    {
                        int phaseIndex = Mathf.FloorToInt(overallProgress * phaseDescriptions.Count);
                        phaseIndex = Mathf.Clamp(phaseIndex, 0, phaseDescriptions.Count - 1);
                        Description = phaseDescriptions[phaseIndex];
                    }

                    OnProgressUpdate?.Invoke(Progress, Description);

                    if (anyFailed)
                    {
                        OnLoadFailed?.Invoke($"加载失败: {currentDesc}");
                        return;
                    }

                    if (allDone)
                        break;

                    // 等待下一帧
                    await UniTask.Yield(PlayerLoopTiming.Update);
                }

                // 4. 等待所有加载任务完成（确保所有协程已结束）
                await UniTask.WhenAll(tasks);

                // 5. 全部加载完成
                Progress = 1f;
                Description = "加载完成";
                OnProgressUpdate?.Invoke(Progress, Description);
                OnLoadCompleted?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"LoadingManager.ExecuteLoadAsync failed: {ex.Message}\n{ex.StackTrace}");
                OnLoadFailed?.Invoke($"加载异常: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        #endregion
    }

    #endregion
}
