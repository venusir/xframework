using System;
using Cysharp.Threading.Tasks;

namespace XFramework
{
    /// <summary>
    /// 加载任务收集器。供 <see cref="ILoadableProvider"/> 装载任务用，仅暴露 <see cref="AddLoadable"/>。
    /// <para>防止 provider 误调用 <see cref="ILoadCoordinator"/> 的其他方法。</para>
    /// </summary>
    public interface ILoadCollector
    {
        /// <summary>装载一个加载任务。</summary>
        void AddLoadable(ILoadable loadable);
    }

    /// <summary>
    /// 加载任务提供者接口。节点实现此接口，通过 <see cref="MountLoadables"/> 向 <see cref="ILoadCoordinator"/> 装载加载任务。
    /// <para>节点本身不实现 <see cref="ILoadable"/>，而是提供一组纯 C# 的加载器，不破坏节点的继承结构。</para>
    /// </summary>
    public interface ILoadableProvider
    {
        /// <summary>
        /// 向 <paramref name="collector"/> 装载加载任务。
        /// <para>通过 <c>collector.AddLoadable(...)</c> 添加纯 C# 的 <see cref="LoadableTask"/> 对象。</para>
        /// </summary>
        /// <param name="collector">加载任务收集器。</param>
        void MountLoadables(ILoadCollector collector);
    }

    /// <summary>
    /// 加载协调器接口。对外暴露的加载调度入口，隐藏 <see cref="LoadCoordinator"/> 实现。
    /// <para>通过 <see cref="AddProvider"/> 注册 <see cref="ILoadableProvider"/>，调用 <see cref="LoadAsync"/> 统一调度。</para>
    /// </summary>
    public interface ILoadCoordinator
    {
        /// <summary>是否正在加载中。</summary>
        bool IsLoading { get; }

        /// <summary>当前总体进度，取值范围 0.0 ~ 1.0。</summary>
        float Progress { get; }

        /// <summary>当前加载阶段的描述文字。</summary>
        string Description { get; }

        /// <summary>加载进度变更事件。参数为 (整体进度 0~1, 当前描述文字)。</summary>
        event Action<float, string> OnProgressUpdate;

        /// <summary>全部加载完成事件。</summary>
        event Action OnLoadCompleted;

        /// <summary>加载失败事件。参数为失败原因描述。</summary>
        event Action<string> OnLoadFailed;

        /// <summary>
        /// 注册一个 <see cref="ILoadableProvider"/>，其提供的加载任务将在 <see cref="LoadAsync"/> 时被装载。
        /// </summary>
        void AddProvider(ILoadableProvider provider);

        /// <summary>
        /// 执行加载。装载所有已注册的 <see cref="ILoadableProvider"/> 提供的加载任务并统一调度。
        /// </summary>
        UniTask LoadAsync();

        /// <summary>
        /// 销毁加载器，清理内部状态和事件订阅。
        /// <para>调用后不应再使用此实例。</para>
        /// </summary>
        void Destroy();
    }
}
