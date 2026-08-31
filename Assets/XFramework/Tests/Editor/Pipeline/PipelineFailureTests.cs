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
    }
}
