using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XPipeline
{

    /// <summary>
    /// 阶段执行共享包装(内部)。统一状态机写入、异常与取消捕获,保证阶段必然收敛到终态。
    /// <para>管线实现与并行阶段共用同一执行语义:契约兜底(正常返回未写终态 → 补 Completed)、
    /// 取消(OCE → 返回 true,不视为失败)、异常 → 置 Failed 并写描述。</para>
    /// </summary>
    internal static class StageExecution
    {
        /// <summary>
        /// 包装执行单个阶段。
        /// <para>返回 true 表示阶段以取消结束(抛出 <see cref="OperationCanceledException"/>),
        /// 由调用方统一走取消路径。</para>
        /// </summary>
        internal static async UniTask<bool> RunStageAsync(IPipelineStage stage, PipelineStageContext ctx, CancellationToken cancellationToken)
        {
            try
            {
                await stage.ExecuteAsync(ctx, cancellationToken);

                // 契约兜底:正常返回但状态仍停留在 Pending/Executing → 视为完成
                if (ctx.State == PipelineStageState.Pending || ctx.State == PipelineStageState.Executing)
                {
                    ctx.SetProgress(1f);
                    ctx.SetState(PipelineStageState.Completed);
                }
                return false;
            }
            catch (OperationCanceledException)
            {
                // 取消:保持当前状态,由调用方统一走取消路径,不视为失败
                return true;
            }
            catch (Exception ex)
            {
                // 先写描述再置状态:状态写入触发聚合时描述已是异常消息(事件驱动取最新值)
                ctx.SetDescription(ex.Message);
                ctx.SetState(PipelineStageState.Failed);
                return false;
            }
        }
    }
}
