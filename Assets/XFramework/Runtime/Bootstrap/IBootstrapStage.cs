using XFramework.XPipeline;

namespace XFramework.XBootstrap
{

    /// <summary>
    /// 启动引导阶段：一个模块把自己的初始化与反向清理都交给框架启动流程时实现此接口。
    /// <para>它在 <see cref="IPhaseStage"/> 的相位契约之上只补一件事——<b>清理</b>：
    /// <see cref="IPhaseStage.ExecuteAsync"/> 负责初始化，<see cref="Shutdown"/> 负责反向清理。
    /// 执行、相位分组、并行、进度聚合、失败即停、取消传播全部由 Pipeline 模块提供，本接口不再引入第二套执行契约。</para>
    /// <para>登记经 <see cref="Bootstrap.Register"/>；<see cref="Bootstrap.RunAsync"/> 按相位分组装配并运行管线；
    /// <see cref="Bootstrap.Shutdown"/> 按<b>登记顺序的逆序</b>调用各阶段的 <see cref="Shutdown"/>。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// using System.Threading;
    /// using Cysharp.Threading.Tasks;
    /// using XFramework.XBootstrap;
    /// using XFramework.XPipeline;
    ///
    /// public sealed class MyServiceBootstrapStage : IBootstrapStage
    /// {
    ///     public int Phase => 10;              // 晚于框架内置相位，早于业务区间
    ///     public string Name => GetType().Name;
    ///     public float Weight => 1f;
    ///
    ///     public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken ct)
    ///     {
    ///         context.SetDescription("Initializing MyService...");
    ///         await MyService.InitializeAsync(ct);
    ///         context.SetProgress(1f);
    ///         context.SetState(PipelineStageState.Completed);
    ///     }
    ///
    ///     public void Shutdown() => MyService.Destroy();
    /// }
    ///
    /// Bootstrap.Register(new MyServiceBootstrapStage());
    /// </code>
    /// </example>
    public interface IBootstrapStage : IPhaseStage
    {
        /// <summary>
        /// 反向清理本阶段初始化的服务。由 <see cref="Bootstrap.Shutdown"/> 逆序调用。
        /// <para><b>为什么是同步的：</b>框架内置的四个模块（Asset / Data / Save / Localization）的清理入口
        /// 都是同步 <c>void</c>，且调用点通常是没有 await 机会的 <c>OnDestroy</c>。
        /// 若将来某个模块确实需要异步清理，再为它单独扩展接口。</para>
        /// <para>实现应可重复调用（幂等）；抛出的异常由 <see cref="Bootstrap.Shutdown"/> 隔离，
        /// 不会阻断其它阶段的清理。</para>
        /// </summary>
        void Shutdown();
    }
}
