using System;
using UnityEngine;

namespace XFramework.XLoader
{

    /// <summary>
    /// 加载进度。作为 <see cref="ILoadable.LoadAsync"/> 的参数传入，提供进度/状态/权重的读写能力。
    /// <para>由调度者在装载时创建并注入，节点在加载过程中通过此对象报告进度。每次写入
    /// (SetWeight/SetProgress/SetDescription/SetState)后同步触发 <see cref="OnChanged"/> 通知调度者,
    /// 供事件驱动聚合使用——调度者无需轮询即可感知任务进度变化。</para>
    /// <para>独立使用场景(如 AssetManager 初始化进度)不注入回调,<see cref="OnChanged"/> 为 null,
    /// 写入仅落字段,行为与无通知时代完全一致。</para>
    /// <para>全局级字段(OverallProgress 等)由调度者在聚合时填充,供 UI 读取当前加载状态的全部信息。</para>
    /// <para>实现 <see cref="System.IProgress{LoadProgress}"/>，可直接作为进度回调传递给下游 API。</para>
    /// </summary>
    public class LoadProgress : System.IProgress<LoadProgress>
    {
        /// <summary>
        /// 写后通知回调(调度者注入)。每次写入(SetWeight/SetProgress/SetDescription/SetState)后同步触发,
        /// 事件驱动聚合的「门铃」——任务写进度即被调度者感知,无需帧泵轮询。
        /// </summary>
        internal Action<LoadProgress> OnChanged;

        #region 任务级（节点写入）

        /// <summary>任务名称。由 Loader 在装载时设置。</summary>
        public string Name { get; internal set; }

        /// <summary>加载权重。默认 1f;节点在 <see cref="ILoadable.LoadAsync"/> 开头经 <see cref="SetWeight"/> 设置,
        /// Loader 组内按权重加权聚合进度。</summary>
        public float Weight { get; internal set; } = 1f;

        /// <summary>
        /// 设置加载权重。必须在 <see cref="ILoadable.LoadAsync"/> 首行同步调用(首次 await 之前),
        /// 保证聚合首轮即可读到正确权重;调度者以组内加权聚合进度,默认 1f。
        /// </summary>
        /// <param name="value">权重值,下限 0.01(clamp,防除零)。</param>
        public void SetWeight(float value)
        {
            Weight = Mathf.Max(0.01f, value);
            OnChanged?.Invoke(this);
        }

        /// <summary>当前加载进度，取值范围 0.0 ~ 1.0。</summary>
        public float Progress { get; private set; }

        /// <summary>当前加载阶段的描述文字。</summary>
        public string Description { get; internal set; }

        /// <summary>当前加载状态。</summary>
        public LoadState State { get; private set; } = LoadState.Pending;

        /// <summary>设置当前加载进度，会自动 clamp 到 0~1 范围。</summary>
        public void SetProgress(float value)
        {
            Progress = Mathf.Clamp01(value);
            OnChanged?.Invoke(this);
        }

        /// <summary>设置当前加载阶段的描述文字。</summary>
        public void SetDescription(string description)
        {
            Description = description;
            OnChanged?.Invoke(this);
        }

        /// <summary>设置当前加载状态。</summary>
        public void SetState(LoadState state)
        {
            State = state;
            OnChanged?.Invoke(this);
        }

        #endregion

        #region 全局级（Loader 轮询时填充，只读）

        /// <summary>总体加载进度，取值范围 0.0 ~ 1.0。</summary>
        public float OverallProgress { get; internal set; }

        /// <summary>设置总体加载进度，自动 clamp 到 0~1 范围。</summary>
        public void SetOverallProgress(float value)
        {
            OverallProgress = Mathf.Clamp01(value);
        }

        /// <summary>当前正在执行的任务名称。</summary>
        public string CurrentTaskName { get; internal set; }

        /// <summary>总任务数。</summary>
        public int TotalTaskCount { get; internal set; }

        /// <summary>已完成的任务数。</summary>
        public int CompletedCount { get; internal set; }

        /// <summary>失败的任务数。</summary>
        public int FailedCount { get; internal set; }

        #endregion

        #region IProgress<LoadProgress>

        void System.IProgress<LoadProgress>.Report(LoadProgress value)
        {
            if (value == null) return;

            OverallProgress = value.OverallProgress;

            if (!string.IsNullOrEmpty(value.Description))
            {
                Description = value.Description;
            }
        }

        #endregion
    }
}