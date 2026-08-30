using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XLoader;
using XFramework.XPipeline;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// <see cref="TaskGroupStage"/> 测试:组内并行调度、事件驱动聚合(门铃)、失败/取消传播、契约兜底。
    /// <para>全部用例经管线装配单阶段运行——EditMode 无 PlayerLoop 泵,挂起用
    /// <see cref="UniTaskCompletionSource"/> 内联续体;事件驱动下无帧泵依赖,写进度即广播。</para>
    /// </summary>
    class TaskGroupStageTests
    {
        #region 并行执行

        [Test]
        public void ParallelTasks_AllExecute()
        {
            var a = new FakeLoadable { Phase = 0, Description = "a" };
            var b = new FakeLoadable { Phase = 0, Description = "b" };
            var stage = new TaskGroupStage(new[] { a, b }, 0);

            bool completed = false;
            var pipeline = Pipeline.Create();
            pipeline.OnCompleted += () => completed = true;
            pipeline.AddStage(stage);

            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, a.LoadCount, "任务 a 应被执行");
            Assert.AreEqual(1, b.LoadCount, "任务 b 应被执行");
            Assert.IsTrue(completed, "组内全部完成应触发管线完成");
        }

        [Test]
        public void GatedTask_DoesNotBlockSibling_AndWritesProgressImmediately()
        {
            var a = new FakeLoadable { Phase = 0, Gate = new UniTaskCompletionSource(), ProgressValue = 0.5f };
            var b = new FakeLoadable { Phase = 0 };
            var stage = new TaskGroupStage(new[] { a, b }, 0);

            var progress = new List<PipelineProgress>();
            var pipeline = Pipeline.Create();
            pipeline.OnProgressUpdate += p => progress.Add(p);
            pipeline.AddStage(stage);

            var task = pipeline.RunAsync();

            // 门铃语义:任务 A 写 0.5 即同步聚合广播,无需推帧轮询
            Assert.AreEqual(0.75f, progress[progress.Count - 1].OverallProgress, 0.001f, "挂起时进度应为 A(0.5) 与已完成 B(1) 的加权");
            Assert.AreEqual(1, b.LoadCount, "任务 B 应与 A 并行执行,不被 A 挂起阻塞");
            bool foundHalf = false;
            for (int i = 0; i < progress.Count; i++)
            {
                if (progress[i].OverallProgress == 0.5f)
                {
                    foundHalf = true;
                    break;
                }
            }
            Assert.IsTrue(foundHalf, "应广播任务 A 写入的 0.5 进度");

            a.Gate.TrySetResult();
            task.GetAwaiter().GetResult();

            Assert.AreEqual(1f, progress[progress.Count - 1].OverallProgress, 0.001f, "放行后应收敛到 1");
        }

        #endregion

        #region 事件驱动聚合

        [Test]
        public void SmallSteps_AreThrottled()
        {
            var step = new StepLoadable();
            var stage = new TaskGroupStage(new[] { step }, 0);

            var progress = new List<PipelineProgress>();
            var pipeline = Pipeline.Create();
            pipeline.OnProgressUpdate += p => progress.Add(p);
            pipeline.AddStage(stage);

            pipeline.RunAsync().GetAwaiter().GetResult();

            // 0.1% 步进应被节流:广播序列仅含首帧 0 与终局 1,不得出现 0.001~0.009 的中间值
            // (首帧可能双发:进度 0 与描述 null→"Completed" 各一次,属管线事件驱动转发的既有行为)
            Assert.GreaterOrEqual(progress.Count, 3, "至少应保留首帧与终局广播");
            Assert.AreEqual(0f, progress[0].OverallProgress, 0.001f, "首帧应为 0");
            Assert.AreEqual(1f, progress[progress.Count - 1].OverallProgress, 0.001f, "完成应收敛到 1");
            for (int i = 1; i < progress.Count - 1; i++)
            {
                float v = progress[i].OverallProgress;
                Assert.IsTrue(Mathf.Approximately(v, 0f) || Mathf.Approximately(v, 1f),
                    $"小于 1% 的步进应被节流:出现非法中间值 {v}");
            }
        }

        [Test]
        public void Description_ForcesBroadcast()
        {
            var fake = new FakeLoadable { Phase = 0, Description = "fake" };
            var stage = new TaskGroupStage(new[] { fake }, 0);

            var progress = new List<PipelineProgress>();
            var pipeline = Pipeline.Create();
            pipeline.OnProgressUpdate += p => progress.Add(p);
            pipeline.AddStage(stage);

            pipeline.RunAsync().GetAwaiter().GetResult();

            bool foundDesc = false;
            for (int i = 0; i < progress.Count; i++)
            {
                if (progress[i].Description == "fake")
                {
                    foundDesc = true;
                    break;
                }
            }
            Assert.IsTrue(foundDesc, "任务描述写入应触发广播");
        }

        #endregion

        #region 失败与取消

        [Test]
        public void Failure_MarksStageFailed_AndCancelsSiblings()
        {
            var bad = new FakeLoadable { Phase = 0, ThrowOnLoad = true };
            var sibling = new FakeLoadable { Phase = 0, Gate = new UniTaskCompletionSource() };
            var stage = new TaskGroupStage(new[] { bad, sibling }, 0);

            string failedReason = null;
            var pipeline = Pipeline.Create();
            pipeline.OnFailed += r => failedReason = r;
            pipeline.AddStage(stage);

            LogAssert.Expect(LogType.Error, new Regex(@"\[Loader\] Load failed:"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));
            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(PipelineStageState.Failed, stage._stageCtx.State, "组内失败应置阶段 Failed");
            StringAssert.Contains("boom", failedReason, "管线失败原因应携带任务异常消息");
            Assert.IsTrue(sibling.LastToken.IsCancellationRequested, "失败应取消中断兄弟任务");
        }

        [Test]
        public void Cancellation_SettlesAndRaisesPipelineCancellation()
        {
            var fake = new FakeLoadable { Phase = 0, Gate = new UniTaskCompletionSource() };
            var stage = new TaskGroupStage(new[] { fake }, 0);

            var cts = new CancellationTokenSource();
            string failedReason = null;
            bool completed = false;
            var pipeline = Pipeline.Create();
            pipeline.OnFailed += r => failedReason = r;
            pipeline.OnCompleted += () => completed = true;
            pipeline.AddStage(stage);

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] Pipeline cancelled"));
            var task = pipeline.RunAsync(cts.Token);
            cts.Cancel();
            task.GetAwaiter().GetResult();

            Assert.AreEqual(1, fake.LoadCount, "任务应已启动");
            Assert.IsFalse(completed, "取消不应触发完成事件");
            Assert.AreEqual("Pipeline cancelled.", failedReason, "取消应经管线取消路径报告");
        }

        #endregion

        #region 契约兜底

        [Test]
        public void TaskWithoutStateWrite_AutoCompletes()
        {
            var fake = new FakeLoadable { Phase = 0, WriteState = false };
            var stage = new TaskGroupStage(new[] { fake }, 0);

            bool completed = false;
            var pipeline = Pipeline.Create();
            pipeline.OnCompleted += () => completed = true;
            pipeline.AddStage(stage);

            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(PipelineStageState.Completed, stage._stageCtx.State, "不写状态的实现应被契约兜底补置完成");
            Assert.IsTrue(completed);
        }

        #endregion

        /// <summary>
        /// 小步写进度探针:每次写 0.1% 增量(低于 1% 节流阈值),验证事件驱动下的节流语义。
        /// </summary>
        private sealed class StepLoadable : ILoadable
        {
            public int Phase => 0;

            public async UniTask LoadAsync(LoadProgress progress, CancellationToken cancellationToken)
            {
                progress.SetState(LoadState.Loading);

                for (int i = 1; i <= 9; i++)
                {
                    progress.SetProgress(0.001f * i);
                    await UniTask.CompletedTask;
                }

                progress.SetState(LoadState.Completed);
            }
        }
    }
}
