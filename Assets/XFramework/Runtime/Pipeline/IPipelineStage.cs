using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XPipeline
{
    /// <summary>管线阶段状态。</summary>
    public enum PipelineStageState
    {
        /// <summary>等待中。</summary>
        Pending,

        /// <summary>执行中。</summary>
        Executing,

        /// <summary>已完成。</summary>
        Completed,

        /// <summary>已失败。</summary>
        Failed,
    }

    /// <summary>
    /// 管线阶段。由 <see cref="IPipeline"/> 按添加顺序串行调度执行。
    /// <para>阶段经 <see cref="ExecuteAsync"/> 中的 <see cref="PipelineStageContext"/> 主动写入进度/状态/描述,
    /// 写入点同步触发管线级加权聚合与广播(事件驱动,非轮询)。</para>
    /// </summary>
    public interface IPipelineStage
    {
        /// <summary>阶段名称。用于进度描述与日志。</summary>
        string Name { get; }

        /// <summary>阶段权重,参与全局进度加权聚合。默认 1f;设为 0 表示该阶段不占进度(如瞬时阶段)。</summary>
        float Weight { get; }

        /// <summary>
        /// 执行阶段。由管线串行调用。
        /// <para>契约:正常返回时若状态仍为 <see cref="PipelineStageState.Pending"/> 或 <see cref="PipelineStageState.Executing"/>,
        /// 管线自动补置为 <see cref="PipelineStageState.Completed"/>(进度视为 1f),不会阻塞调度。</para>
        /// <para>契约:抛出的异常将被管线置为 <see cref="PipelineStageState.Failed"/> 并写入描述,
        /// 经 <see cref="IPipeline.OnFailed"/> 报告,后续阶段不再执行。</para>
        /// <para>契约:抛出 <see cref="System.OperationCanceledException"/> 视为取消,不报告失败,后续阶段不再执行。</para>
        /// </summary>
        UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken);
    }
}
