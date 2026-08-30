using UnityEngine;

namespace XFramework.XPipeline
{
    /// <summary>
    /// 管线阶段执行上下文。由 <see cref="IPipeline"/> 装配时创建并注入阶段。
    /// <para>双层结构:阶段写面(<see cref="Progress"/>/<see cref="Description"/>/<see cref="State"/> + SetXxx)供阶段在
    /// <see cref="IPipelineStage.ExecuteAsync"/> 内写入,写入即同步触发管线级聚合与广播;
    /// 全局读面由管线填充,供 UI 读取当前运行状态。</para>
    /// </summary>
    public sealed class PipelineStageContext
    {
        #region Private Fields

        /// <summary>归属管线实现。阶段写入时同步触发聚合与广播(零闭包引用)。</summary>
        internal PipelineImpl Owner;

        #endregion

        #region 阶段级(阶段写入)

        /// <summary>阶段名称。由管线装配时设置。</summary>
        public string Name { get; internal set; }

        /// <summary>阶段权重。由管线装配时设置。</summary>
        public float Weight { get; internal set; } = 1f;

        /// <summary>阶段进度,0~1。</summary>
        public float Progress { get; private set; }

        /// <summary>阶段描述文字。</summary>
        public string Description { get; internal set; }

        /// <summary>阶段状态。</summary>
        public PipelineStageState State { get; private set; } = PipelineStageState.Pending;

        /// <summary>阶段内当前任务名称(由加载阶段等转发,可空)。</summary>
        public string CurrentTaskName { get; internal set; }

        /// <summary>设置阶段进度,自动 clamp 0~1,并触发管线聚合广播。</summary>
        public void SetProgress(float value)
        {
            Progress = Mathf.Clamp01(value);
            Owner?.OnStageContextChanged(this);
        }

        /// <summary>设置阶段描述,并触发管线聚合广播。</summary>
        public void SetDescription(string description)
        {
            Description = description;
            Owner?.OnStageContextChanged(this);
        }

        /// <summary>设置阶段状态,并触发管线聚合广播。</summary>
        public void SetState(PipelineStageState state)
        {
            State = state;
            Owner?.OnStageContextChanged(this);
        }

        #endregion

        #region 全局级(管线填充,只读)

        /// <summary>全局进度,0~1。</summary>
        public float OverallProgress { get; internal set; }

        /// <summary>当前阶段名称。</summary>
        public string CurrentStageName { get; internal set; }

        /// <summary>总阶段数。</summary>
        public int TotalStageCount { get; internal set; }

        /// <summary>已完成阶段数。</summary>
        public int CompletedStageCount { get; internal set; }

        /// <summary>失败阶段数。</summary>
        public int FailedStageCount { get; internal set; }

        #endregion
    }
}
