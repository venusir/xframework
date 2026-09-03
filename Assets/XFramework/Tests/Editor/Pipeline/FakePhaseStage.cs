using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XPipeline;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// <see cref="IPhaseStage"/> 假实现:可配置 Phase/Name/Weight/是否挂起/写进度,用于相位分组装配测试。
    /// <para>挂起语义与 <see cref="FakeStage"/> 一致:Gate 直接 <c>new UniTaskCompletionSource()</c>——
    /// 本包版本续体默认内联执行,TrySetResult 同步续体(EditMode 无 PlayerLoop 泵,依赖此特性推进)。</para>
    /// </summary>
    internal sealed class FakePhaseStage : IPhaseStage
    {
        /// <summary>相位号。</summary>
        public int Phase { get; set; }

        /// <summary>阶段名。</summary>
        public string Name { get; set; } = "phase";

        /// <summary>阶段权重。</summary>
        public float Weight { get; set; } = 1f;

        /// <summary>非空时挂起直到测试放行,用于验证并行启动与相位串行时序。</summary>
        public UniTaskCompletionSource Gate;

        /// <summary>挂起期间报告的进度值。</summary>
        public float ProgressValue;

        /// <summary>被调度执行次数(验证串行序与中断)。</summary>
        public int ExecuteCount;

        /// <summary>最近一次收到的 CancellationToken(验证取消传播)。</summary>
        public CancellationToken LastToken;

        /// <summary>外部注入的执行序记录表(验证相位串行与组内并行启动)。</summary>
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

            context.SetProgress(1f);
        }
    }
}
