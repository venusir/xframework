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
    /// <see cref="ParallelStage"/> 测试:组内并行调度、事件驱动聚合(门铃)、失败/取消传播、契约兜底。
    /// <para>全部用例经管线装配单阶段运行——EditMode 无 PlayerLoop 泵,挂起用
    /// <see cref="UniTaskCompletionSource"/> 内联续体;事件驱动下无帧泵依赖,写进度即广播。</para>
    /// </summary>
    class ParallelStageTests
    {
        #region 并行执行

        [Test]
        public void ParallelChildren_AllExecute()
        {
            var a = new FakeStage { Name = "a" };
            var b = new FakeStage { Name = "b" };
            var stage = new ParallelStage(new[] { a, b });

            bool completed = false;
            var pipeline = Pipeline.Create();
            pipeline.OnCompleted += () => completed = true;
            pipeline.AddStage(stage);

            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, a.ExecuteCount, "子阶段 a 应被执行");
            Assert.AreEqual(1, b.ExecuteCount, "子阶段 b 应被执行");
            Assert.IsTrue(completed, "组内全部完成应触发管线完成");
        }

        [Test]
        public void GatedChild_DoesNotBlockSibling_AndWritesProgressImmediately()
        {
            var a = new FakeStage { Name = "a", Gate = new UniTaskCompletionSource(), ProgressValue = 0.5f };
            var b = new FakeStage { Name = "b" };
            var stage = new ParallelStage(new[] { a, b });

            var progress = new List<PipelineProgress>();
            var pipeline = Pipeline.Create();
            pipeline.OnProgressUpdate += p => progress.Add(p);
            pipeline.AddStage(stage);

            var task = pipeline.RunAsync();

            // 门铃语义:子阶段 A 写 0.5 即同步聚合广播,无需推帧轮询
            Assert.AreEqual(0.75f, progress[progress.Count - 1].OverallProgress, 0.001f, "挂起时进度应为 A(0.5) 与已完成 B(1) 的加权");
            Assert.AreEqual(1, b.ExecuteCount, "子阶段 B 应与 A 并行执行,不被 A 挂起阻塞");
            bool foundIntermediate = false;
            for (int i = 0; i < progress.Count; i++)
            {
                // 子上下文预置 Executing(与管线契约一致):A 写 0.5 时 B 尚在执行中(进度 0),
                // 组内加权 = (0.5+0)/2 = 0.25——子阶段写入应触发中间聚合广播
                if (progress[i].OverallProgress == 0.25f)
                {
                    foundIntermediate = true;
                    break;
                }
            }
            Assert.IsTrue(foundIntermediate, "应广播子阶段 A 写入引发的中间聚合进度");

            a.Gate.TrySetResult();
            task.GetAwaiter().GetResult();

            Assert.AreEqual(1f, progress[progress.Count - 1].OverallProgress, 0.001f, "放行后应收敛到 1");
        }

        #endregion

        #region 事件驱动聚合

        [Test]
        public void WeightedProgress_GroupAggregation()
        {
            var a = new FakeStage { Name = "a", Weight = 1f, Gate = new UniTaskCompletionSource(), ProgressValue = 0.5f };
            var b = new FakeStage { Name = "b", Weight = 3f };
            var stage = new ParallelStage(new[] { a, b });

            var progress = new List<PipelineProgress>();
            var pipeline = Pipeline.Create();
            pipeline.OnProgressUpdate += p => progress.Add(p);
            pipeline.AddStage(stage);

            var task = pipeline.RunAsync();

            // 组内加权:(0.5×1 + 1×3) / (1+3) = 0.875
            Assert.AreEqual(0.875f, progress[progress.Count - 1].OverallProgress, 0.001f, "挂起时进度应为按子阶段权重加权聚合");

            a.Gate.TrySetResult();
            task.GetAwaiter().GetResult();
        }

        [Test]
        public void SmallSteps_AreThrottled()
        {
            var step = new StepChildStage();
            var stage = new ParallelStage(new[] { step });

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
        public void DescriptionChange_ForcesBroadcast()
        {
            var fake = new DescChildStage();
            var stage = new ParallelStage(new[] { fake });

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
            Assert.IsTrue(foundDesc, "子阶段描述写入应触发广播");
        }

        #endregion

        #region 失败与取消

        [Test]
        public void Failure_MarksStageFailed_AndCancelsSiblings()
        {
            var bad = new FakeStage { Name = "bad", ThrowOnExecute = true };
            var sibling = new FakeStage { Name = "sibling", Gate = new UniTaskCompletionSource() };
            var stage = new ParallelStage(new[] { bad, sibling });

            string failedReason = null;
            var pipeline = Pipeline.Create();
            pipeline.OnFailed += r => failedReason = r;
            pipeline.AddStage(stage);

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Parallel stage failed:"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));
            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(PipelineStageState.Failed, stage._stageCtx.State, "组内失败应置阶段 Failed");
            StringAssert.Contains("boom", failedReason, "管线失败原因应携带子阶段异常消息");
            Assert.IsTrue(sibling.LastToken.IsCancellationRequested, "失败应取消中断兄弟子阶段");
        }

        [Test]
        public void FailedChild_DiagnosisWins()
        {
            var bad = new FakeStage { Name = "bad", ThrowOnExecute = true };
            var ok = new FakeStage { Name = "ok" };
            var stage = new ParallelStage(new[] { bad, ok });

            var progress = new List<PipelineProgress>();
            var pipeline = Pipeline.Create();
            pipeline.OnProgressUpdate += p => progress.Add(p);
            pipeline.AddStage(stage);

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Parallel stage failed:"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));
            pipeline.RunAsync().GetAwaiter().GetResult();

            // 诊断优先:失败子阶段的描述与名称应曾广播,不得被已完成兄弟覆盖。
            // 注:管线失败终局广播的 Description 由 RecalculateSnapshot 生成(仅读执行中阶段描述),
            // 终局可能显示 "Completed",故断言「曾广播」而非「终局广播」。
            bool foundBoom = false;
            bool foundBad = false;
            for (int i = 0; i < progress.Count; i++)
            {
                if (progress[i].Description == "boom") foundBoom = true;
                if (progress[i].CurrentTaskName == "bad") foundBad = true;
            }
            Assert.IsTrue(foundBoom, "失败子阶段的异常描述应曾广播(诊断优先,不被兄弟覆盖)");
            Assert.IsTrue(foundBad, "失败子阶段的名称应曾广播(诊断优先)");
        }

        [Test]
        public void Cancellation_SettlesAndRaisesPipelineCancellation()
        {
            var fake = new FakeStage { Name = "fake", Gate = new UniTaskCompletionSource() };
            var stage = new ParallelStage(new[] { fake });

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

            Assert.AreEqual(1, fake.ExecuteCount, "子阶段应已启动");
            Assert.IsFalse(completed, "取消不应触发完成事件");
            Assert.AreEqual("Pipeline cancelled.", failedReason, "取消应经管线取消路径报告");
        }

        [Test]
        public void ChildThrowsOCE_GroupCancels()
        {
            var fake = new FakeStage { Name = "fake", ThrowCanceled = true };
            var stage = new ParallelStage(new[] { fake });

            string failedReason = null;
            bool completed = false;
            var pipeline = Pipeline.Create();
            pipeline.OnFailed += r => failedReason = r;
            pipeline.OnCompleted += () => completed = true;
            pipeline.AddStage(stage);

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] Pipeline cancelled"));
            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.IsFalse(completed, "子阶段自抛 OCE 应使组走取消路径,不触发完成");
            Assert.AreEqual("Pipeline cancelled.", failedReason, "取消应经管线取消路径报告");
        }

        #endregion

        #region 契约兜底

        [Test]
        public void ChildWithoutStateWrite_AutoCompletes()
        {
            var silent = new SilentChildStage();
            var stage = new ParallelStage(new[] { silent });

            bool completed = false;
            var pipeline = Pipeline.Create();
            pipeline.OnCompleted += () => completed = true;
            pipeline.AddStage(stage);

            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(PipelineStageState.Completed, stage._stageCtx.State, "不写状态的子阶段应被契约兜底补置完成");
            Assert.IsTrue(completed);
        }

        #endregion

        #region 构造

        [Test]
        public void GroupWeight_IsSumOfChildWeights()
        {
            var stage = new ParallelStage(new[] { new FakeStage { Weight = 2f }, new FakeStage { Weight = 3f } });
            Assert.AreEqual(5f, stage.Weight, 0.001f, "阶段权重应为子阶段权重之和");
        }

        [Test]
        public void EmptyChildren_Throws()
        {
            Assert.Throws<ArgumentException>(() => new ParallelStage(null), "null 子阶段列表应抛参数异常");
            Assert.Throws<ArgumentException>(() => new ParallelStage(new IPipelineStage[0]), "空子阶段列表应抛参数异常");
        }

        #endregion

        /// <summary>
        /// 描述写入探针:先置 Executing(聚合仅读执行中/失败子阶段的描述,与管线契约一致)再写描述。
        /// </summary>
        private sealed class DescChildStage : IPipelineStage
        {
            public string Name => "fake";

            public float Weight => 1f;

            public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
            {
                context.SetState(PipelineStageState.Executing);
                context.SetDescription("fake");
                context.SetState(PipelineStageState.Completed);
                await UniTask.CompletedTask;
            }
        }

        /// <summary>
        /// 小步写进度探针:每次写 0.1% 增量(低于 1% 节流阈值),验证事件驱动下的节流语义。
        /// </summary>
        private sealed class StepChildStage : IPipelineStage
        {
            public string Name => "step";

            public float Weight => 1f;

            public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
            {
                context.SetState(PipelineStageState.Executing);

                for (int i = 1; i <= 9; i++)
                {
                    context.SetProgress(0.001f * i);
                    await UniTask.CompletedTask;
                }

                context.SetState(PipelineStageState.Completed);
            }
        }

        /// <summary>不写任何状态的子阶段,验证契约兜底。</summary>
        private sealed class SilentChildStage : IPipelineStage
        {
            public string Name => "silent";

            public float Weight => 1f;

            public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
            {
                await UniTask.CompletedTask;
            }
        }
    }
}
