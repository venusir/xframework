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
    /// <see cref="LoadableStage"/> 测试:单任务适配的镜像语义(一次任务写入恰好 1 次组级聚合)、
    /// 权重影响组内聚合、契约兜底、失败/取消传播(OCE 上抛设计防误补 Completed)。
    /// <para>除直连用例(TestSink 注入子上下文)外,全部经 <see cref="ParallelStage"/> 装配——
    /// 与加载应用的真实形态一致(组内并行、事件驱动聚合)。</para>
    /// </summary>
    class LoadableStageTests
    {
        #region 契约兜底

        [Test]
        public void TaskWithoutStateWrite_AutoCompletes()
        {
            var fake = new FakeLoadable { Phase = 0, WriteState = false };
            var stage = new ParallelStage(new IPipelineStage[] { new LoadableStage(fake) });

            bool completed = false;
            var pipeline = Pipeline.Create();
            pipeline.OnCompleted += () => completed = true;
            pipeline.AddStage(stage);

            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(PipelineStageState.Completed, stage._stageCtx.State, "不写状态的实现应被契约兜底补置完成");
            Assert.IsTrue(completed);
        }

        #endregion

        #region 权重镜像

        [Test]
        public void SetWeight_MirrorsToChildContextAndAffectsGroupAggregate()
        {
            var heavy = new FakeLoadable { Phase = 0, TaskWeight = 2f, Gate = new UniTaskCompletionSource(), ProgressValue = 0.5f };
            var light = new FakeLoadable { Phase = 0 };
            var stage = new ParallelStage(new IPipelineStage[] { new LoadableStage(heavy), new LoadableStage(light) });

            var progress = new List<PipelineProgress>();
            var pipeline = Pipeline.Create();
            pipeline.OnProgressUpdate += p => progress.Add(p);
            pipeline.AddStage(stage);

            var task = pipeline.RunAsync();

            // 任务权重经镜像影响组内聚合:(0.5×2 + 1×1) / (2+1) = 2/3
            Assert.AreEqual(2f / 3f, progress[progress.Count - 1].OverallProgress, 0.001f, "任务权重应镜像到子上下文并影响组内聚合");

            heavy.Gate.TrySetResult();
            task.GetAwaiter().GetResult();
        }

        [Test]
        public void SetWeight_TriggersGroupAggregate_EvenWithoutProgress()
        {
            var fake = new FakeLoadable { Phase = 0, TaskWeight = 2f, Gate = new UniTaskCompletionSource() };
            var stage = new ParallelStage(new IPipelineStage[] { new LoadableStage(fake) });

            var progress = new List<PipelineProgress>();
            var pipeline = Pipeline.Create();
            pipeline.OnProgressUpdate += p => progress.Add(p);
            pipeline.AddStage(stage);

            var task = pipeline.RunAsync();

            // SetWeight/SetDescription 经门铃触发镜像与聚合(挂起中,进度仍为 0)
            Assert.GreaterOrEqual(progress.Count, 2, "任务写权重/描述应触发组级聚合广播");
            Assert.AreEqual(0f, progress[progress.Count - 1].OverallProgress, 0.001f, "挂起中进度应为 0");

            fake.Gate.TrySetResult();
            task.GetAwaiter().GetResult();
        }

        #endregion

        #region 失败与取消

        [Test]
        public void Failure_SetsChildFailed_AndCancelsSiblings()
        {
            var bad = new FakeLoadable { Phase = 0, ThrowOnLoad = true };
            var sibling = new FakeLoadable { Phase = 0, Gate = new UniTaskCompletionSource() };
            var stage = new ParallelStage(new IPipelineStage[] { new LoadableStage(bad), new LoadableStage(sibling) });

            string failedReason = null;
            var pipeline = Pipeline.Create();
            pipeline.OnFailed += r => failedReason = r;
            pipeline.AddStage(stage);

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Parallel stage failed:"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));
            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(PipelineStageState.Failed, stage._stageCtx.State, "组内失败应置阶段 Failed");
            StringAssert.Contains("boom", failedReason, "管线失败原因应携带任务异常消息");
            Assert.IsTrue(sibling.LastToken.IsCancellationRequested, "失败应取消中断兄弟任务");
        }

        [Test]
        public void SetStateFailed_WithoutException_FailsGroup()
        {
            var bad = new FailedLoadable();
            var stage = new ParallelStage(new IPipelineStage[] { new LoadableStage(bad) });

            bool completed = false;
            var pipeline = Pipeline.Create();
            pipeline.OnCompleted += () => completed = true;
            pipeline.AddStage(stage);

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Parallel stage failed:"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));
            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(PipelineStageState.Failed, stage._stageCtx.State, "任务主动置 Failed 应使组失败");
            Assert.IsFalse(completed, "任务失败不得触发完成事件");
        }

        [Test]
        public void Cancellation_KeepsChildState_GroupCancels()
        {
            var fake = new FakeLoadable { Phase = 0, Gate = new UniTaskCompletionSource() };
            var stage = new ParallelStage(new IPipelineStage[] { new LoadableStage(fake) });

            var cts = new CancellationTokenSource();
            string failedReason = null;
            bool cancelled = false;
            bool completed = false;
            var pipeline = Pipeline.Create();
            pipeline.OnFailed += r => failedReason = r;
            pipeline.OnCancelled += () => cancelled = true;
            pipeline.OnCompleted += () => completed = true;
            pipeline.AddStage(stage);

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] Pipeline cancelled"));
            var task = pipeline.RunAsync(cts.Token);
            cts.Cancel();
            task.GetAwaiter().GetResult();

            Assert.AreEqual(1, fake.LoadCount, "任务应已启动");
            Assert.IsTrue(cancelled, "任务取消应触发管线取消事件");
            Assert.IsNull(failedReason, "取消不得触发失败事件");
            Assert.IsFalse(completed, "取消不应触发完成事件");
        }

        [Test]
        public void Cancellation_NotMarkedCompleted_OnChildContext()
        {
            // 直连:取消任务不得被共享包装的契约兜底误补 Completed(OCE 上抛设计)
            var sink = new TestSink();
            var stage = new LoadableStage(new FakeLoadable { Phase = 0, Gate = new UniTaskCompletionSource() });
            var ctx = new PipelineStageContext { Owner = sink };
            var cts = new CancellationTokenSource();

            var task = stage.ExecuteAsync(ctx, cts.Token);
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult(), "取消后阶段应显式上抛 OCE");
            Assert.AreNotEqual(PipelineStageState.Completed, ctx.State, "取消任务不得被契约兜底误补 Completed");
        }

        #endregion

        #region 镜像语义

        [Test]
        public void ProgressAndDescription_Mirrored()
        {
            var fake = new FakeLoadable { Phase = 0, Gate = new UniTaskCompletionSource(), ProgressValue = 0.5f };
            var stage = new ParallelStage(new IPipelineStage[] { new LoadableStage(fake) });

            var progress = new List<PipelineProgress>();
            var pipeline = Pipeline.Create();
            pipeline.OnProgressUpdate += p => progress.Add(p);
            pipeline.AddStage(stage);

            var task = pipeline.RunAsync();

            bool foundHalf = false;
            bool foundDesc = false;
            for (int i = 0; i < progress.Count; i++)
            {
                if (progress[i].OverallProgress == 0.5f) foundHalf = true;
                if (progress[i].Description == "fake") foundDesc = true;
            }
            Assert.IsTrue(foundHalf, "任务进度应镜像到子上下文并参与组聚合");
            Assert.IsTrue(foundDesc, "任务描述应镜像并广播");

            fake.Gate.TrySetResult();
            task.GetAwaiter().GetResult();
        }

        [Test]
        public void DirectMirror_WritesChildContext_OneNotificationPerWrite()
        {
            // 直连:TestSink 注入子上下文,直接断言镜像后的字段与通知次数
            var sink = new TestSink();
            var stage = new LoadableStage(new FakeLoadable { Phase = 0 });
            var ctx = new PipelineStageContext { Owner = sink, Name = "X", Weight = 1f };

            stage.ExecuteAsync(ctx, default).GetAwaiter().GetResult();

            Assert.AreEqual(PipelineStageState.Completed, ctx.State, "任务完成后子上下文应镜像为 Completed");
            Assert.AreEqual(1f, ctx.Progress, 0.001f, "任务进度应镜像到子上下文");
            Assert.AreEqual("FakeLoadable", ctx.CurrentTaskName, "CurrentTaskName 应镜像任务类型名");
            // 镜像通知序 = RunTask 预置 SetState(Loading) + FakeLoadable 写入 6 次
            // (SetWeight/SetDescription/SetState(Loading)/SetProgress(0)/SetProgress(1)/SetState(Completed))
            Assert.AreEqual(7, sink.ChangedCount, "每次写入恰好触发 1 次组级聚合");
        }

        #endregion

        /// <summary>
        /// 直连接收方:统计镜像通知次数,验证「一次任务写入恰好 1 次组级聚合」。
        /// </summary>
        private sealed class TestSink : IStageContextSink
        {
            public int ChangedCount;

            public void OnStageContextChanged(PipelineStageContext context)
            {
                ChangedCount++;
            }
        }

        /// <summary>主动置 Failed 的加载任务(不抛异常,验证状态驱动失败路径)。</summary>
        private sealed class FailedLoadable : ILoadable
        {
            public int Phase => 0;

            public async UniTask LoadAsync(LoadProgress progress, CancellationToken cancellationToken)
            {
                progress.SetDescription("dead");
                progress.SetState(LoadState.Failed);
                await UniTask.CompletedTask;
            }
        }
    }
}
