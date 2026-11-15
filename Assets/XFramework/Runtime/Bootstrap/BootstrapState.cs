namespace XFramework.XBootstrap
{

    /// <summary>
    /// 启动引导的运行状态。由 <see cref="Bootstrap.State"/> 暴露，并在会话复位时回到 <see cref="Idle"/>。
    /// <para><b>它是拉取面，不是历史</b>：只反映最近一次 <see cref="Bootstrap.RunAsync"/> 的结果；
    /// 失败后再次启动会把状态重置为 <see cref="Running"/>，上一次的异常不再影响新运行。</para>
    /// <para><b>等待请用 <see cref="Bootstrap.WaitUntilReadyAsync"/></b>：它把「轮询状态」收敛成可取消的
    /// 等待，并在失败/取消时重抛本次运行的异常。</para>
    /// </summary>
    public enum BootstrapState
    {
        /// <summary>尚未开始，或会话复位后的初始态。</summary>
        Idle,

        /// <summary>一次 <see cref="Bootstrap.RunAsync"/> 正在运行。</summary>
        Running,

        /// <summary>最近一次运行的全部阶段已完成。</summary>
        Completed,

        /// <summary>最近一次运行以失败收场（阶段抛出或管线失败）。</summary>
        Failed,

        /// <summary>最近一次运行被取消（调用方令牌取消，或 <see cref="Bootstrap.Shutdown"/> 中止在途运行）。</summary>
        Cancelled,
    }
}
