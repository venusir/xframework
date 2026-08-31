using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XPipeline;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// 阶段超时语义测试:超时置 Failed 并停止后续阶段、0/负值不启用、挂死阶段不阻塞管线、
    /// 竞速中外部取消走取消路径、并行组超时传播。
    /// <para>计时器经 <see cref="PipelineImpl.TimeoutTaskFactory"/> 注入(EditMode 阻塞式测试中真实延时由
    /// PlayerLoop 泵送永不触发),生产路径为真实墙钟延时。</para>
    /// </summary>
    class PipelineTimeoutTests
    {
        [Test]
        public void Timeout_NotTriggered_NormalCompletion()
        {
            var stage = new FakeStage { Name = "A" };
            var pipeline = Pipeline.Create();
            pipeline.AddStage(stage, 5f);

            // 注入永不触发的计时器:阶段先完成应走正常分支,超时不得误触发
            ((PipelineImpl)pipeline).TimeoutTaskFactory = (_, _) => new UniTaskCompletionSource().Task;

            bool completed = false;
            string failedReason = null;
            pipeline.OnCompleted += () => completed = true;
            pipeline.OnFailed += r => failedReason = r;

            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, stage.ExecuteCount);
            Assert.IsTrue(completed, "未触发超时应正常完成");
            Assert.IsNull(failedReason);
        }

        [Test]
        public void Timeout_TriggersFailure_AndStopsLaterStages()
        {
            var a = new CtxCapturingStage { Gate = new UniTaskCompletionSource() };
            var b = new FakeStage { Name = "B" };
            var pipeline = Pipeline.Create();
            pipeline.AddStage(a, 1f);
            pipeline.AddStage(b);

            var timer = new UniTaskCompletionSource();
            ((PipelineImpl)pipeline).TimeoutTaskFactory = (_, _) => timer.Task;

            bool completed = false;
            string failedReason = null;
            pipeline.OnCompleted += () => completed = true;
            pipeline.OnFailed += r => failedReason = r;

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));
            var task = pipeline.RunAsync();
            Assert.AreEqual(1, a.ExecuteCount, "超时阶段应已被调度");
            timer.TrySetResult(); // 触发超时
            task.GetAwaiter().GetResult();

            Assert.AreEqual(PipelineStageState.Failed, a.LastContext.State, "超时应置阶段 Failed");
            StringAssert.Contains("timed out", a.LastContext.Description, "失败描述应含超时信息");
            Assert.IsTrue(a.LastToken.IsCancellationRequested, "超时应取消在途阶段(token 已取消)");
            Assert.AreEqual(0, b.ExecuteCount, "超时失败后后续阶段不应执行");
            StringAssert.Contains("timed out", failedReason, "失败原因应含超时信息");
            Assert.IsFalse(completed, "超时失败不得触发完成事件");
        }

        [Test]
        public void Timeout_ZeroOrNegative_Disabled()
        {
            var a = new FakeStage { Name = "A", Gate = new UniTaskCompletionSource() };
            var b = new FakeStage { Name = "B", Gate = new UniTaskCompletionSource() };
            var pipeline = Pipeline.Create();
            pipeline.AddStage(a, 0f);
            pipeline.AddStage(b, -1f);

            // 永不完成的计时器:若超时被误启用,WhenAny 永不完成 → 本测试死锁(验证信号)
            ((PipelineImpl)pipeline).TimeoutTaskFactory = (_, _) => UniTask.Never(CancellationToken.None);

            bool completed = false;
            pipeline.OnCompleted += () => completed = true;

            var task = pipeline.RunAsync();
            a.Gate.TrySetResult();
            b.Gate.TrySetResult();
            task.GetAwaiter().GetResult();

            Assert.IsTrue(completed, "0/负值超时应不启用,正常完成");
        }

        [Test]
        public void Timeout_HungStage_DoesNotBlockRunAsync()
        {
            var hung = new HungStage();
            var pipeline = Pipeline.Create();
            pipeline.AddStage(hung, 1f);

            var timer = new UniTaskCompletionSource();
            ((PipelineImpl)pipeline).TimeoutTaskFactory = (_, _) => timer.Task;

            var progress = new List<PipelineProgress>();
            pipeline.OnProgressUpdate += p => progress.Add(p);

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));
            var task = pipeline.RunAsync();
            timer.TrySetResult();
            task.GetAwaiter().GetResult(); // 挂死阶段不得阻塞 RunAsync 返回

            int broadcastCount = progress.Count;
            // 终局后迟写:被放弃在途任务的上下文写入应被忽略(IsRunning == false)
            hung.LastContext.SetProgress(0.5f);
            hung.LastContext.SetState(PipelineStageState.Completed);
            Assert.AreEqual(broadcastCount, progress.Count, "终局后的迟写不得再触发广播");
        }

        [Test]
        public void Timeout_ExternalCancelDuringRace_Cancels()
        {
            var stage = new FakeStage { Name = "A", Gate = new UniTaskCompletionSource() };
            var pipeline = Pipeline.Create();
            pipeline.AddStage(stage, 5f);

            // 注入真实延时的取消敏感计时器:阻塞测试中延时永不触发,但外部取消经链接 CTS 同步取消计时任务
            ((PipelineImpl)pipeline).TimeoutTaskFactory =
                (seconds, token) => UniTask.Delay(TimeSpan.FromSeconds(1000), DelayType.Realtime, cancellationToken: token, cancelImmediately: true);

            var cts = new CancellationTokenSource();
            bool cancelled = false;
            string failedReason = null;
            bool completed = false;
            pipeline.OnCancelled += () => cancelled = true;
            pipeline.OnFailed += r => failedReason = r;
            pipeline.OnCompleted += () => completed = true;

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] Pipeline cancelled"));
            var task = pipeline.RunAsync(cts.Token);
            cts.Cancel(); // 竞速期间外部取消
            task.GetAwaiter().GetResult();

            Assert.IsTrue(cancelled, "竞速中外部取消应走取消终局");
            Assert.IsNull(failedReason, "取消不得触发失败事件");
            Assert.IsFalse(completed, "取消不得触发完成事件");
        }

        [Test]
        public void Timeout_ParallelStage_PropagatesToChildren()
        {
            var child = new FakeStage { Name = "child", Gate = new UniTaskCompletionSource() };
            var stage = new ParallelStage(new IPipelineStage[] { child }, "Group");
            var pipeline = Pipeline.Create();
            pipeline.AddStage(stage, 1f);

            var timer = new UniTaskCompletionSource();
            ((PipelineImpl)pipeline).TimeoutTaskFactory = (_, _) => timer.Task;

            bool completed = false;
            string failedReason = null;
            pipeline.OnCompleted += () => completed = true;
            pipeline.OnFailed += r => failedReason = r;

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));
            var task = pipeline.RunAsync();
            Assert.AreEqual(1, child.ExecuteCount, "并行组子阶段应已启动");
            timer.TrySetResult(); // 触发组级超时
            task.GetAwaiter().GetResult();

            Assert.IsTrue(child.LastToken.IsCancellationRequested, "组级超时应取消传播到子阶段");
            StringAssert.Contains("timed out", failedReason, "失败原因应含超时信息");
            Assert.IsFalse(completed, "组超时失败不得触发完成事件");
        }

        #region Private Fakes

        /// <summary>记录执行上下文与最近 token 的挂起阶段(验证超时置 Failed 与取消传播)。</summary>
        private sealed class CtxCapturingStage : IPipelineStage
        {
            public string Name { get; set; } = "capture";

            public float Weight { get; set; } = 1f;

            /// <summary>非空时挂起直到测试放行(不响应取消,模拟超时中断的在途任务)。</summary>
            public UniTaskCompletionSource Gate;

            public int ExecuteCount;

            public PipelineStageContext LastContext;

            public CancellationToken LastToken;

            public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
            {
                ExecuteCount++;
                LastContext = context;
                LastToken = cancellationToken;
                if (Gate != null)
                    await Gate.Task;
            }
        }

        /// <summary>不响应取消且永不完成的阶段(验证超时后管线不阻塞)。</summary>
        private sealed class HungStage : IPipelineStage
        {
            public string Name { get; set; } = "hung";

            public float Weight { get; set; } = 1f;

            public PipelineStageContext LastContext;

            public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
            {
                LastContext = context;
                // 不响应取消:验证超时后管线放弃在途任务而非等待其沉降
                await new UniTaskCompletionSource().Task;
            }
        }

        #endregion
    }
}
