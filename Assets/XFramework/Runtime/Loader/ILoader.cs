using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XPipeline;

namespace XFramework.XLoader
{

    /// <summary>
    /// 加载阶段接口。对外暴露的加载调度入口,隐藏 <see cref="Loader"/> 实现。
    /// <para>加载阶段同时实现 <see cref="IPipelineStage"/>(Name="Load"),在通用管线中承担加载阶段的职责;
    /// 本接口保留直接以 ILoader 形态独立使用的能力(进度/失败/取消语义一致)。</para>
    /// <para>通过 <see cref="AddLoadable"/> 注册实现了 <see cref="ILoadable"/> 的节点，调用 <see cref="LoadAsync"/> 统一调度。</para>
    /// </summary>
    public interface ILoader
    {
        /// <summary>是否正在加载中。</summary>
        bool IsLoading { get; }

        /// <summary>加载进度变更事件。每帧轮询时触发，传递当前进度快照。</summary>
        event Action<LoadProgress> OnProgressUpdate;

        /// <summary>全部加载完成事件。</summary>
        event Action OnLoadCompleted;

        /// <summary>加载失败事件。参数为失败原因描述。</summary>
        event Action<string> OnLoadFailed;

        /// <summary>
        /// 注册一个实现了 <see cref="ILoadable"/> 的加载任务。
        /// </summary>
        void AddLoadable(ILoadable loadable);

        /// <summary>
        /// 执行加载。按 Phase 分组调度所有已注册的加载任务。
        /// </summary>
        /// <param name="cancellationToken">取消令牌:取消后当前组任务收到已取消的 token,尚未开始的后续组不再执行,
        /// 触发 <see cref="OnLoadFailed"/>("Load cancelled."),不触发 <see cref="OnLoadCompleted"/>;
        /// 以阶段形态运行时,取消经 ExecuteAsync 上抛给管线统一走取消路径。</param>
        UniTask LoadAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 销毁加载器，清理内部状态和事件订阅。
        /// <para>调用后不应再使用此实例。</para>
        /// </summary>
        void Destroy();
    }
}
