using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XPipeline
{
    /// <summary>管线终局状态。</summary>
    public enum PipelineStatus
    {
        /// <summary>未运行(尚未运行过,或已 <see cref="IPipeline.Destroy"/>)。</summary>
        Idle,

        /// <summary>运行中。</summary>
        Running,

        /// <summary>全部阶段完成(空阶段列表按完成处理,与它照常触发 <see cref="IPipeline.OnCompleted"/> 一致)。</summary>
        Completed,

        /// <summary>阶段失败,原因见 <see cref="IPipeline.FailureReason"/>。</summary>
        Failed,

        /// <summary>取消(外部 token 取消或阶段自抛 <see cref="System.OperationCanceledException"/>)。</summary>
        Cancelled,
    }

    /// <summary>
    /// 管线接口。通用阶段编排器:阶段按添加顺序串行执行,进度加权聚合广播,失败/取消传播。
    /// <para>通过 <see cref="Pipeline.Create"/> 创建实例,装配阶段后调用 <see cref="RunAsync"/> 执行;实例即用即弃。</para>
    /// <para>订阅者异常隔离:本接口四个事件的订阅者抛出的异常一律记 <c>[Pipeline]</c> LogError 后继续,
    /// 不影响阶段状态与终局——订阅方(如 UI)的 bug 不得把管线里的阶段打成失败,也不得把完成/取消改报成失败。</para>
    /// </summary>
    public interface IPipeline
    {
        /// <summary>是否正在运行中。等价于 <see cref="Status"/> == <see cref="PipelineStatus.Running"/>,保留独立成员是因为运行中判定是布尔语义的常用读法。</summary>
        bool IsRunning { get; }

        /// <summary>
        /// 终局状态。运行结束后<b>保持</b>终局值(不回落 <see cref="PipelineStatus.Idle"/>),故可在
        /// <see cref="RunAsync"/> 返回后直接读取——不必订阅终局事件再自己记账;仅 <see cref="Destroy"/> 回落 Idle。
        /// <para>落位先于终局事件:订阅者在事件回调里读到的已是本次结果。</para>
        /// </summary>
        PipelineStatus Status { get; }

        /// <summary>失败原因。<see cref="Status"/> 为 <see cref="PipelineStatus.Failed"/> 时非空(文案与 <see cref="OnFailed"/> 的实参一致),其余为 null。</summary>
        string FailureReason { get; }

        /// <summary>进度变更事件。阈值节流(变化 ≥1% 或状态/描述变化)后广播。</summary>
        event Action<PipelineProgress> OnProgressUpdate;

        /// <summary>全部阶段完成事件。</summary>
        event Action OnCompleted;

        /// <summary>
        /// 管线取消事件。外部 token 取消、阶段自行抛 <see cref="System.OperationCanceledException"/>、
        /// 并行组取消传播均触发;不携带原因(日志已记录)。触发后不触发 <see cref="OnCompleted"/> 与 <see cref="OnFailed"/>。
        /// </summary>
        event Action OnCancelled;

        /// <summary>管线失败事件。参数为原因描述;取消不触发本事件(见 <see cref="OnCancelled"/>)。</summary>
        event Action<string> OnFailed;

        /// <summary>
        /// 追加阶段。装配期调用;重复添加同一实例被忽略;运行中调用打 <c>[Pipeline]</c> 警告并忽略。
        /// </summary>
        void AddStage(IPipelineStage stage);

        /// <summary>
        /// 追加阶段并设置超时(秒,0/负值/NaN 表示不启用)。装配期调用;重复添加同一实例被忽略;
        /// 运行中调用打 <c>[Pipeline]</c> 警告并忽略。
        /// <para>超时语义:超时触发时取消当前阶段运行并置 <see cref="PipelineStageState.Failed"/>(描述含超时信息),
        /// 经 <see cref="OnFailed"/> 报告,后续阶段不再执行;不响应取消的挂起阶段不阻塞管线(在途任务被放弃,
        /// 其后续上下文写入被忽略)。</para>
        /// </summary>
        void AddStage(IPipelineStage stage, float timeoutSeconds);

        /// <summary>
        /// 运行管线。阶段按添加顺序串行执行。
        /// </summary>
        /// <param name="cancellationToken">取消令牌:取消后当前阶段收到已取消的 token,尚未开始的阶段不再执行,
        /// 触发 <see cref="OnCancelled"/>,不触发 <see cref="OnCompleted"/> 与 <see cref="OnFailed"/>。</param>
        UniTask RunAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 销毁管线:清空装配的阶段、事件订阅与终局状态(回落 <see cref="PipelineStatus.Idle"/>)。
        /// <para>调用后不应再使用此实例。本方法<b>不中断在途运行</b>——运行中调用请先取消传给
        /// <see cref="RunAsync"/> 的 token;被放弃的那次运行此后不再广播(其门铃已随运行结束关闭)。</para>
        /// </summary>
        void Destroy();
    }
}
