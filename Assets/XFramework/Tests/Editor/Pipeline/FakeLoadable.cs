using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XPipeline;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// <see cref="ILoadable"/> 假实现：可配置 Phase、是否写状态、是否抛异常、是否挂起，用于 Loader 调度语义测试。
    /// <para><see cref="WriteState"/> = false 模拟旧 LocalizationBootstrapNode「不写状态」的实现；<see cref="ThrowOnLoad"/> 模拟以异常报告失败的实现。</para>
    /// <para>挂起用 <see cref="Gate"/> 直接 <c>new UniTaskCompletionSource()</c> 即可——本包版本续体默认内联执行，
    /// TrySetResult 同步续体;EditMode 测试环境无 PlayerLoop 泵，依赖此特性推进。</para>
    /// </summary>
    internal sealed class FakeLoadable : ILoadable
    {
        /// <summary>调度阶段号。</summary>
        public int Phase { get; set; }

        /// <summary>是否按契约写入状态；false 模拟不写状态的实现。</summary>
        public bool WriteState = true;

        /// <summary>加载时是否抛异常，模拟以异常报告失败。</summary>
        public bool ThrowOnLoad;

        /// <summary>非空时挂起直到测试放行，用于模拟长任务与并行时序。</summary>
        public UniTaskCompletionSource Gate;

        /// <summary>任务权重(首行同步写入 progress,契约要求首次 await 前设置)。</summary>
        public float TaskWeight = 1f;

        /// <summary>挂起期间报告的进度值。</summary>
        public float ProgressValue;

        /// <summary>被调度执行次数（验证去重与并行启动）。</summary>
        public int LoadCount;

        /// <summary>最近一次收到的 CancellationToken（验证取消传播）。</summary>
        public CancellationToken LastToken;

        /// <summary>任务描述文字。</summary>
        public string Description = "fake";

        /// <summary>外部注入的执行序记录表（验证跨 Phase 串行）。</summary>
        public List<string> ExecutionLog;

        public async UniTask LoadAsync(LoadProgress progress, CancellationToken cancellationToken)
        {
            LoadCount++;
            LastToken = cancellationToken;
            ExecutionLog?.Add(Description);
            progress.SetWeight(TaskWeight);
            progress.SetDescription(Description);

            if (WriteState)
            {
                progress.SetState(LoadState.Loading);
                progress.SetProgress(ProgressValue);
            }

            if (Gate != null)
            {
                await Gate.Task.AttachExternalCancellation(cancellationToken);
            }

            if (ThrowOnLoad)
                throw new InvalidOperationException("boom");

            if (WriteState)
            {
                progress.SetProgress(1f);
                progress.SetState(LoadState.Completed);
            }
        }
    }
}
