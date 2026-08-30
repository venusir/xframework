using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XPipeline;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// <see cref="IPipelineStage"/> 假实现:可配置 Name/Weight/是否挂起/是否抛异常/写进度,用于管线调度语义测试。
    /// <para>挂起用 <see cref="Gate"/> 直接 <c>new UniTaskCompletionSource()</c>——本包版本续体默认内联执行,
    /// TrySetResult 同步续体(EditMode 无 PlayerLoop 泵,依赖此特性推进)。</para>
    /// </summary>
    internal sealed class FakeStage : IPipelineStage
    {
        /// <summary>阶段名。</summary>
        public string Name { get; set; } = "fake";

        /// <summary>阶段权重。</summary>
        public float Weight { get; set; } = 1f;

        /// <summary>执行时是否抛异常,模拟以异常报告失败。</summary>
        public bool ThrowOnExecute;

        /// <summary>执行时是否抛 OperationCanceledException,模拟阶段主动取消。</summary>
        public bool ThrowCanceled;

        /// <summary>非空时挂起直到测试放行,用于模拟长阶段与串行时序。</summary>
        public UniTaskCompletionSource Gate;

        /// <summary>挂起期间报告的进度值。</summary>
        public float ProgressValue;

        /// <summary>被调度执行次数(验证串行序与中断)。</summary>
        public int ExecuteCount;

        /// <summary>最近一次收到的 CancellationToken(验证取消传播)。</summary>
        public CancellationToken LastToken;

        /// <summary>外部注入的执行序记录表(验证串行执行)。</summary>
        public List<string> ExecutionLog;

        public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            ExecuteCount++;
            LastToken = cancellationToken;
            ExecutionLog?.Add(Name);
            context.SetDescription(Name);
            context.SetProgress(ProgressValue);

            if (Gate != null)
                await Gate.Task.AttachExternalCancellation(cancellationToken);

            if (ThrowCanceled)
                throw new OperationCanceledException(cancellationToken);

            if (ThrowOnExecute)
                throw new InvalidOperationException("boom");

            context.SetProgress(1f);
        }
    }
}
