using System;

namespace XFramework.XPipeline
{
    /// <summary>
    /// 管线全局进度快照。<see cref="IPipeline.OnProgressUpdate"/> 的事件载荷。
    /// <para>实现 <see cref="IProgress{PipelineProgress}"/>,可直接作为进度回调传递给下游 API。</para>
    /// </summary>
    public class PipelineProgress : IProgress<PipelineProgress>
    {
        #region 全局级(管线填充,只读)

        /// <summary>全局进度,0~1。</summary>
        public float OverallProgress { get; internal set; }

        /// <summary>当前阶段描述文字。</summary>
        public string Description { get; internal set; }

        /// <summary>当前阶段名称。</summary>
        public string CurrentStageName { get; internal set; }

        /// <summary>阶段内当前任务名称(加载阶段转发,可空)。</summary>
        public string CurrentTaskName { get; internal set; }

        /// <summary>总阶段数。</summary>
        public int TotalStageCount { get; internal set; }

        /// <summary>已完成阶段数。</summary>
        public int CompletedStageCount { get; internal set; }

        /// <summary>失败阶段数。</summary>
        public int FailedStageCount { get; internal set; }

        #endregion

        #region IProgress<PipelineProgress>

        void IProgress<PipelineProgress>.Report(PipelineProgress value)
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
