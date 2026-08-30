using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XLoader
{

    /// <summary>
    /// 加载状态(任务级)。与管线阶段状态 <see cref="XFramework.XPipeline.PipelineStageState"/> 分层并存:
    /// 任务级状态由 Loader 内部调度产生(组内并行任务),阶段级状态由管线编排产生(加载阶段整体)。
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

    /// <summary>
    /// 可加载接口。节点实现此接口后，可在启动管线的加载阶段被 <see cref="ILoader"/> 统一调度执行。
    /// <para>通过 <see cref="LoadProgress"/> 参数报告进度、描述和状态。</para>
    /// </summary>
    public interface ILoadable
    {
        /// <summary>
        /// 加载阶段号。相同值的节点并行执行，不同值的节点按值从小到大串行执行。
        /// </summary>
        int Phase { get; }

        /// <summary>
        /// 异步加载任务。加载过程中应通过 <paramref name="progress"/> 更新进度和状态。
        /// <para>通过 <paramref name="cancellationToken"/> 可取消正在运行的任务。</para>
        /// <para>契约：正常返回时若状态仍为 <see cref="LoadState.Pending"/> 或 <see cref="LoadState.Loading"/>，
        /// Loader 将自动补置为 <see cref="LoadState.Completed"/>（进度视为 1f），不会阻塞调度。</para>
        /// <para>契约：抛出的异常将被 Loader 捕获并置为 <see cref="LoadState.Failed"/>，经 <see cref="ILoader.OnLoadFailed"/> 报告；
        /// 抛出 <see cref="System.OperationCanceledException"/> 视为取消，不报告失败。</para>
        /// </summary>
        UniTask LoadAsync(LoadProgress progress, CancellationToken cancellationToken);
    }
}
