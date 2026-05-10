using UnityEngine;

namespace XFramework.XLoad
{

    /// <summary>
    /// 加载上下文。作为 <see cref="ILoadable.LoadAsync"/> 的参数传入，提供进度/状态/权重的读写能力。
    /// <para>由 <see cref="ILoader"/> 在装载时创建并注入，节点在加载过程中通过此对象报告进度。</para>
    /// <para>全局级字段由 Loader 在每帧轮询时填充，供 UI 读取当前加载状态的全部信息。</para>
    /// </summary>
    public class LoadContext
    {
        #region 任务级（节点写入）

        /// <summary>任务名称。由 Loader 在装载时设置。</summary>
        public string Name { get; internal set; }

        /// <summary>加载权重。由 Loader 在装载时设置。</summary>
        public float Weight { get; internal set; } = 1f;

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
        }

        /// <summary>设置当前加载阶段的描述文字。</summary>
        public void SetDescription(string description)
        {
            Description = description;
        }

        /// <summary>设置当前加载状态。</summary>
        public void SetState(LoadState state)
        {
            State = state;
        }

        #endregion

        #region 全局级（Loader 轮询时填充，只读）

        /// <summary>总体加载进度，取值范围 0.0 ~ 1.0。</summary>
        public float OverallProgress { get; internal set; }

        /// <summary>当前正在执行的任务名称。</summary>
        public string CurrentTaskName { get; internal set; }

        /// <summary>总任务数。</summary>
        public int TotalTaskCount { get; internal set; }

        /// <summary>已完成的任务数。</summary>
        public int CompletedCount { get; internal set; }

        /// <summary>失败的任务数。</summary>
        public int FailedCount { get; internal set; }

        #endregion
    }
}
