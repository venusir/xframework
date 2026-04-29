using Cysharp.Threading.Tasks;

namespace XFramework
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
    /// 可加载接口。实现此接口的对象可被 <see cref="ILoadCoordinator"/> 统一调度执行。
    /// </summary>
    public interface ILoadable
    {
        /// <summary>任务名称。静态标识，用于 UI 显示当前正在执行的任务。</summary>
        string Name { get; }

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
}
