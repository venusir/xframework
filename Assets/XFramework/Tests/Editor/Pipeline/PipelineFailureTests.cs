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
    /// 管线失败/取消语义测试:失败即停、失败终局快照、外部取消、阶段主动取消(OCE)。
    /// </summary>
    class PipelineFailureTests
    {
        #region Private Methods

        /// <summary>创建记录进度广播的管线。</summary>
        private static (IPipeline pipeline, List<PipelineProgress> progress) CreateTrackedPipeline()
        {
            var pipeline = Pipeline.Create();
            var progress = new List<PipelineProgress>();
            pipeline.OnProgressUpdate += p => progress.Add(p);
            return (pipeline, progress);
        }

        #endregion

        #region 失败路径

        [Test]
        public void StageFailure_StopsLaterStages()
        {
            var log = new List<string>();
            var ok = new FakeStage { Name = "A", ExecutionLog = log };
            var boom = new FakeStage { Name = "B", ThrowOnExecute = true, ExecutionLog = log };
            var never = new FakeStage { Name = "C", ExecutionLog = log };
            var (pipeline, progress) = CreateTrackedPipeline();
            pipeline.AddStage(ok);
            pipeline.AddStage(boom);
            pipeline.AddStage(never);

            string failedReason = null;
            pipeline.OnFailed += r => failedReason = r;

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));
            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(new List<string> { "A", "B" }, log, "失败阶段之后的阶段不应执行");
            Assert.AreEqual(0, never.ExecuteCount, "后续阶段不得被调度");
            StringAssert.Contains("boom", failedReason, "失败原因应携带异常消息");
            Assert.AreEqual(1, progress[progress.Count - 1].FailedStageCount, "失败终局快照应记录失败阶段数");
        }

        /// <summary>
        /// 阶段抛出的异常对象要随失败日志落盘：控制台在正文后追加完整的异常详情，同一份内容进 JSONL 的
        /// `exc` 字段（含类型与抛出点堆栈）。此前只留 `ex.Message` 一句话——用户阶段代码的堆栈就此消失，
        /// 而这正是「我的 stage 为什么失败」最需要的东西。
        /// </summary>
        [Test]
        public void StageFailure_LogsTheThrownExceptionWithItsStack()
        {
            var pipeline = Pipeline.Create();
            pipeline.AddStage(new FakeStage { Name = "B", ThrowOnExecute = true });

            LogAssert.Expect(LogType.Error,
                new Regex(@"\[Pipeline\] Pipeline failed: boom\nSystem\.InvalidOperationException: boom"));

            pipeline.RunAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// 容器（串行/并行）内子阶段抛出的异常同样要传到底：容器的失败播报保持纯文本，异常对象拷进
        /// 容器自己的上下文，由管线终局那一行带出去——两处都挂会让同一个堆栈出现两遍。
        /// </summary>
        [Test]
        public void ContainerStageFailure_PropagatesChildExceptionToPipelineLog()
        {
            var pipeline = Pipeline.Create();
            pipeline.AddStage(new SequenceStage(
                new[] { new FakeStage { Name = "inner", ThrowOnExecute = true } }, "Seq"));

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Sequence stage failed:"));
            LogAssert.Expect(LogType.Error,
                new Regex(@"\[Pipeline\] Pipeline failed: .*\nSystem\.InvalidOperationException: boom"));

            pipeline.RunAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void StageFailure_TerminalSnapshot()
        {
            // 串行模型下失败阶段权重移出聚合:已完成阶段全部计入 → 终局快照 OverallProgress == 1.0
            var a = new FakeStage { Name = "A", ProgressValue = 0.5f, Gate = new UniTaskCompletionSource() };
            var boom = new FakeStage { Name = "B", ThrowOnExecute = true };
            var (pipeline, progress) = CreateTrackedPipeline();
            pipeline.AddStage(a);
            pipeline.AddStage(boom);

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));
            var task = pipeline.RunAsync();
            a.Gate.TrySetResult();
            task.GetAwaiter().GetResult();

            var last = progress[progress.Count - 1];
            Assert.AreEqual(1f, last.OverallProgress, 0.001f, "失败阶段权重移出后,已完成权重全计入");
            Assert.AreEqual(1, last.CompletedStageCount);
            Assert.AreEqual(1, last.FailedStageCount);
            Assert.AreEqual(2, last.TotalStageCount);
        }

        #endregion

        #region 取消路径

        [Test]
        public void PreCancelledToken_TriggersCancelPath()
        {
            var stage = new FakeStage();
            var (pipeline, _) = CreateTrackedPipeline();
            pipeline.AddStage(stage);

            string failedReason = null;
            bool cancelled = false;
            bool completed = false;
            pipeline.OnFailed += r => failedReason = r;
            pipeline.OnCancelled += () => cancelled = true;
            pipeline.OnCompleted += () => completed = true;

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] Pipeline cancelled"));
            pipeline.RunAsync(new CancellationToken(canceled: true)).GetAwaiter().GetResult();

            Assert.AreEqual(0, stage.ExecuteCount, "预取消:任何阶段都不应执行");
            Assert.IsTrue(cancelled, "预取消应触发取消事件");
            Assert.IsNull(failedReason, "取消不得触发失败事件");
            Assert.IsFalse(completed, "取消不应触发完成事件");
        }

        [Test]
        public void CancelMidRun_CurrentStageGetsCancelledToken()
        {
            var log = new List<string>();
            var a = new FakeStage { Name = "A", Gate = new UniTaskCompletionSource(), ExecutionLog = log };
            var b = new FakeStage { Name = "B", ExecutionLog = log };
            var (pipeline, _) = CreateTrackedPipeline();
            pipeline.AddStage(a);
            pipeline.AddStage(b);

            var cts = new CancellationTokenSource();
            string failedReason = null;
            bool cancelled = false;
            pipeline.OnFailed += r => failedReason = r;
            pipeline.OnCancelled += () => cancelled = true;

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] Pipeline cancelled"));
            var task = pipeline.RunAsync(cts.Token);
            cts.Cancel();
            task.GetAwaiter().GetResult();

            Assert.AreEqual(1, a.ExecuteCount, "当前阶段应已被调度");
            Assert.IsTrue(a.LastToken.IsCancellationRequested, "当前阶段应收到已取消的 token");
            Assert.AreEqual(0, b.ExecuteCount, "后续阶段不应执行");
            Assert.IsTrue(cancelled, "运行中取消应触发取消事件");
            Assert.IsNull(failedReason, "取消不得触发失败事件");
        }

        [Test]
        public void Cancellation_StageIgnoringCancel_StillCancels()
        {
            // 取消语义由阶段负责:不观察 token 的阶段会「正常返回」。若管线只在循环顶部检查 token,
            // 最后一个阶段这样收场就落进完成终局——调用方明明取消了,却收到 OnCompleted
            var stage = new CancelBlindStage();
            var (pipeline, _) = CreateTrackedPipeline();
            pipeline.AddStage(stage);

            var cts = new CancellationTokenSource();
            string failedReason = null;
            bool cancelled = false;
            bool completed = false;
            pipeline.OnFailed += r => failedReason = r;
            pipeline.OnCancelled += () => cancelled = true;
            pipeline.OnCompleted += () => completed = true;

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] Pipeline cancelled"));
            var task = pipeline.RunAsync(cts.Token);
            cts.Cancel();
            stage.Gate.TrySetResult();
            task.GetAwaiter().GetResult();

            Assert.IsTrue(cancelled, "阶段沉降后 token 已取消 → 应走取消终局");
            Assert.IsFalse(completed, "取消不得落入完成终局");
            Assert.IsNull(failedReason, "取消不得触发失败事件");
        }

        [Test]
        public void StageThrowsOCE_NotExternalCancel_IsCancellation()
        {
            var a = new FakeStage { ThrowCanceled = true };
            var b = new FakeStage();
            var (pipeline, _) = CreateTrackedPipeline();
            pipeline.AddStage(a);
            pipeline.AddStage(b);

            string failedReason = null;
            bool cancelled = false;
            bool completed = false;
            pipeline.OnFailed += r => failedReason = r;
            pipeline.OnCancelled += () => cancelled = true;
            pipeline.OnCompleted += () => completed = true;

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] Pipeline cancelled"));
            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(0, b.ExecuteCount, "阶段主动取消后后续阶段不应执行");
            Assert.IsTrue(cancelled, "阶段抛 OCE 统一视为管线取消");
            Assert.IsNull(failedReason, "取消不得触发失败事件(OnFailed 仅保留真实失败)");
            Assert.IsFalse(completed);
        }

        #endregion

        /// <summary>
        /// 对取消无感的挂起阶段:等测试放行后正常返回,刻意不观察 token——用于锁定「阶段沉降后
        /// token 仍已取消,则终局是取消而非完成」。
        /// </summary>
        private sealed class CancelBlindStage : IPipelineStage
        {
            public string Name => "cancel-blind";

            public float Weight => 1f;

            public readonly UniTaskCompletionSource Gate = new UniTaskCompletionSource();

            public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
            {
                context.SetDescription("cancel-blind");
                await Gate.Task; // 刻意不挂 AttachExternalCancellation:对取消无感
            }
        }
    }
}
