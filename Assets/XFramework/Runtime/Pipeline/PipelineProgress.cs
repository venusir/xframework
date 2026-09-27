using System;

namespace XFramework.XPipeline
{
    /// <summary>
    /// 管线全局进度快照。<see cref="IPipeline.OnProgressUpdate"/> 的事件载荷。
    /// <para>实现 <see cref="IProgress{PipelineProgress}"/>,可直接作为进度回调传递给下游 API;
    /// 也可当「快照持有者」用:<c>var holder = new PipelineProgress(); await Bootstrap.RunAsync(holder);</c>
    /// 之后读它即是最近一次快照——<c>Report</c> 全量覆盖七个字段。</para>
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

        /// <summary>阶段内当前任务名称(阶段经 <see cref="PipelineStageContext.SetCurrentTaskName"/> 上报;容器转发时未上报则回落子阶段名;可空)。</summary>
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

            // 全量覆盖七个字段,不做「非空才抄」的挑拣:本实现的用途是把自身当作可持续读的
            // 快照持有者(见类型文档),挑字段会让持有者看到半份快照,且描述只能改不能清
            OverallProgress = value.OverallProgress;
            Description = value.Description;
            CurrentStageName = value.CurrentStageName;
            CurrentTaskName = value.CurrentTaskName;
            TotalStageCount = value.TotalStageCount;
            CompletedStageCount = value.CompletedStageCount;
            FailedStageCount = value.FailedStageCount;
        }

        #endregion
    }
}
