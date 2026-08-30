using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XPipeline
{
    /// <summary>
    /// 管线接口。通用阶段编排器:阶段按添加顺序串行执行,进度加权聚合广播,失败/取消传播。
    /// <para>通过 <see cref="Pipeline.Create"/> 创建实例,装配阶段后调用 <see cref="RunAsync"/> 执行;实例即用即弃。</para>
    /// </summary>
    public interface IPipeline
    {
        /// <summary>是否正在运行中。</summary>
        bool IsRunning { get; }

        /// <summary>进度变更事件。阈值节流(变化 ≥1% 或状态/描述变化)后广播。</summary>
        event Action<PipelineProgress> OnProgressUpdate;

        /// <summary>全部阶段完成事件。</summary>
        event Action OnCompleted;

        /// <summary>管线失败或取消事件。参数为原因描述。</summary>
        event Action<string> OnFailed;

        /// <summary>
        /// 追加阶段。装配期调用;重复添加同一实例被忽略。
        /// </summary>
        void AddStage(IPipelineStage stage);

        /// <summary>
        /// 运行管线。阶段按添加顺序串行执行。
        /// </summary>
        /// <param name="cancellationToken">取消令牌:取消后当前阶段收到已取消的 token,尚未开始的阶段不再执行,
        /// 触发 <see cref="OnFailed"/>("Pipeline cancelled."),不触发 <see cref="OnCompleted"/>。</param>
        UniTask RunAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 销毁管线,清理内部状态与事件订阅。
        /// <para>调用后不应再使用此实例。</para>
        /// </summary>
        void Destroy();
    }
}
