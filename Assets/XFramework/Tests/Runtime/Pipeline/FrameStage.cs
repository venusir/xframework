using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XPipeline;

namespace XFramework.XPipeline.Tests
{
    /// <summary>
    /// <b>跨帧</b>假阶段：逐帧 <c>await UniTask.Yield()</c>，每帧写一次进度；也可以选择永不完成。
    /// <para><b>为什么另有一份而不是复用 Editor 树的 <c>FakeStage</c></b>：两个测试程序集互不可见
    /// （与 <c>FakeConfigLoader</c> / <c>FakeAssetManager</c> 同一先例），而且用途本就不同——
    /// 那份靠 <c>Gate</c> 在**同一个时间片内**放行（EditMode 无 PlayerLoop 泵，只能这样推），
    /// 这一份的核心恰是「真的让出到下一帧」。</para>
    /// </summary>
    internal sealed class FrameStage : IPipelineStage
    {
        /// <summary>阶段名。</summary>
        public string Name { get; set; } = "frame";

        /// <summary>阶段权重。</summary>
        public float Weight { get; set; } = 1f;

        /// <summary>要让出多少帧（<see cref="Gate"/> 非空时本项被忽略）。</summary>
        public int Frames;

        /// <summary>
        /// 非空时改走它挂起，且<b>故意不挂令牌</b>——用来模拟「不响应取消的挂起阶段」，
        /// 供真实墙钟超时用例断言「挂死任务不得阻塞管线」。
        /// </summary>
        public UniTaskCompletionSource Gate;

        /// <summary>被调度执行次数（验证中断后后续阶段不再执行）。</summary>
        public int ExecuteCount;

        public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            ExecuteCount++;
            context.SetDescription(Name);

            if (Gate != null)
            {
                await Gate.Task; // 不响应取消：超时路径要能放弃在途任务
                return;
            }

            for (int i = 0; i < Frames; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                context.SetProgress((i + 1f) / Frames);
                await UniTask.Yield();
            }

            context.SetProgress(1f);
        }
    }
}
