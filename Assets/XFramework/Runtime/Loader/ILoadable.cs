using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XLoader
{

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
        /// </summary>
        UniTask LoadAsync(LoadProgress progress, CancellationToken cancellationToken);
    }
}
