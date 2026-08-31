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
    /// <see cref="SequenceStage"/> 测试:组内串行调度(前一完成才启动下一)、事件驱动聚合(门铃)、
    /// 失败/取消传播、契约兜底、与 <see cref="ParallelStage"/> 的双向嵌套组合。
    /// <para>全部用例经管线装配单阶段运行——EditMode 无 PlayerLoop 泵,挂起用
    /// <see cref="UniTaskCompletionSource"/> 内联续体;事件驱动下无帧泵依赖,写进度即广播。</para>
    /// </summary>
    class SequenceStageTests
    {
        #region 串行执行

        [Test]
        public void SequenceChildren_ExecuteInOrder()
        {
            var a = new FakeStage { Name = "a" };
            var b = new FakeStage { Name = "b" };
            var log = new List<string>();
            a.ExecutionLog = log;
            b.ExecutionLog = log;
            var stage = new SequenceStage(new[] { a, b });

            bool completed = false;
            var pipeline = Pipeline.Create();
            pipeline.OnCompleted += () => completed = true;
            pipeline.AddStage(stage);

            pipeline.RunAsync().GetAwaiter().GetResult();

            CollectionAssert.AreEqual(new[] { "a", "b" }, log, "子阶段应按声明顺序依次执行");
            Assert.AreEqual(1, a.ExecuteCount);
            Assert.AreEqual(1, b.ExecuteCount);
            Assert.IsTrue(completed, "组内全部完成应触发管线完成");
        }

        [Test]
        public void SecondChild_StartsOnlyAfterFirstCompletes()
        {
            var a = new FakeStage { Name = "a", Gate = new UniTaskCompletionSource() };
            var b = new FakeStage { Name = "b" };
            var stage = new SequenceStage(new[] { a, b });

            var pipeline = Pipeline.Create();
            pipeline.AddStage(stage);

            var task = pipeline.RunAsync();

            Assert.AreEqual(1, a.ExecuteCount, "子阶段 a 应已启动");
            Assert.AreEqual(0, b.ExecuteCount, "a 挂起时 b 不得启动(串行)");

            a.Gate.TrySetResult();
            task.GetAwaiter().GetResult();

            Assert.AreEqual(1, b.ExecuteCount, "a 完成后 b 才启动");
        }

        #endregion

        #region 事件驱动聚合

        [Test]
        public void SerialProgress_WeightedAggregation()
        {
            var a = new FakeStage { Name = "a", Weight = 1f, Gate = new UniTaskCompletionSource(), ProgressValue = 0.5f };
            var b = new FakeStage { Name = "b", Weight = 3f };
            var stage = new SequenceStage(new[] { a, b });

            var progress = new List<PipelineProgress>();
            var pipeline = Pipeline.Create();
            pipeline.OnProgressUpdate += p => progress.Add(p);
            pipeline.AddStage(stage);

            var task = pipeline.RunAsync();

            // 串行语义:未开始子阶段不占进度,当前子阶段进度即组进度(等权下 0.5)
            Assert.AreEqual(0.5f, progress[progress.Count - 1].OverallProgress, 0.001f, "挂起时进度应为当前子阶段 a 的进度");

            a.Gate.TrySetResult();
            task.GetAwaiter().GetResult();

            // 子段切换回落属预期(Pending 移出聚合分母):a 完成瞬间 1.0,随后 b(w=3) 启动回落 (1)/(1+3) = 0.25
            int regressIdx = -1;
            for (int i = 0; i < progress.Count; i++)
            {
                if (Mathf.Approximately(progress[i].OverallProgress, 0.25f))
                {
                    regressIdx = i;
                    break;
                }
            }
            Assert.IsTrue(regressIdx >= 0, "b 启动应广播回落进度 0.25(子段切换回落属预期)");
            bool preOne = false;
            for (int i = 0; i < regressIdx; i++)
            {
                if (Mathf.Approximately(progress[i].OverallProgress, 1f))
                {
                    preOne = true;
                    break;
                }
            }
            Assert.IsTrue(preOne, "回落前应存在当前子段完成的瞬时 1.0(已完成占比)");
            Assert.AreEqual(1f, progress[progress.Count - 1].OverallProgress, 0.001f, "终局应收敛到 1");
        }

        [Test]
        public void SmallSteps_AreThrottled()
        {
            var step = new StepChildStage();
            var stage = new SequenceStage(new[] { step });

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
            var stage = new SequenceStage(new[] { fake });

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
        public void Failure_StopsSequence_AndMarksFailed()
        {
            var bad = new FakeStage { Name = "bad", ThrowOnExecute = true };
            var b = new FakeStage { Name = "b" };
            var stage = new SequenceStage(new[] { bad, b });

            string failedReason = null;
            var pipeline = Pipeline.Create();
            pipeline.OnFailed += r => failedReason = r;
            pipeline.AddStage(stage);

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Sequence stage failed:"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));
            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(PipelineStageState.Failed, stage._stageCtx.State, "组内失败应置阶段 Failed");
            Assert.AreEqual(0, b.ExecuteCount, "失败后后续子阶段不得执行(失败即停)");
            StringAssert.Contains("boom", failedReason, "管线失败原因应携带子阶段异常消息");
        }

        [Test]
        public void FailedChild_DiagnosisWins()
        {
            var bad = new FakeStage { Name = "bad", ThrowOnExecute = true };
            var stage = new SequenceStage(new[] { bad });

            var progress = new List<PipelineProgress>();
            var pipeline = Pipeline.Create();
            pipeline.OnProgressUpdate += p => progress.Add(p);
            pipeline.AddStage(stage);

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Sequence stage failed:"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));
            pipeline.RunAsync().GetAwaiter().GetResult();

            // 诊断优先:失败子阶段的描述与名称应曾广播。
            // 注:管线失败终局广播的 Description 由 RecalculateSnapshot 生成(仅读执行中阶段描述),
            // 终局可能显示 "Completed",故断言「曾广播」而非「终局广播」。
            bool foundBoom = false;
            bool foundBad = false;
            for (int i = 0; i < progress.Count; i++)
            {
                if (progress[i].Description == "boom") foundBoom = true;
                if (progress[i].CurrentTaskName == "bad") foundBad = true;
            }
            Assert.IsTrue(foundBoom, "失败子阶段的异常描述应曾广播(诊断优先)");
            Assert.IsTrue(foundBad, "失败子阶段的名称应曾广播(诊断优先)");
        }

        [Test]
        public void Cancellation_StopsCurrentChild_AndRaisesPipelineCancellation()
        {
            var a = new FakeStage { Name = "a", Gate = new UniTaskCompletionSource() };
            var b = new FakeStage { Name = "b" };
            var stage = new SequenceStage(new[] { a, b });

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

            Assert.AreEqual(1, a.ExecuteCount, "子阶段 a 应已启动");
            Assert.IsTrue(a.LastToken.IsCancellationRequested, "取消应传播到当前子阶段");
            Assert.AreEqual(0, b.ExecuteCount, "取消后后续子阶段不得启动");
            Assert.IsTrue(cancelled, "子阶段取消沉降后应触发管线取消事件");
            Assert.IsNull(failedReason, "取消不得触发失败事件");
            Assert.IsFalse(completed, "取消不应触发完成事件");
        }

        [Test]
        public void ChildThrowsOCE_SequenceCancels()
        {
            var a = new FakeStage { Name = "a", ThrowCanceled = true };
            var stage = new SequenceStage(new[] { a });

            string failedReason = null;
            bool cancelled = false;
            bool completed = false;
            var pipeline = Pipeline.Create();
            pipeline.OnFailed += r => failedReason = r;
            pipeline.OnCancelled += () => cancelled = true;
            pipeline.OnCompleted += () => completed = true;
            pipeline.AddStage(stage);

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] Pipeline cancelled"));
            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.IsTrue(cancelled, "子阶段自抛 OCE 应使组走取消路径并触发取消事件");
            Assert.IsNull(failedReason, "取消不得触发失败事件");
            Assert.IsFalse(completed, "子阶段自抛 OCE 应使组走取消路径,不触发完成");
        }

        [Test]
        public void BoundaryCancelCheck_StopsBeforeNextChild()
        {
            // 子阶段不响应取消且正常完成 → 循环边界取消检查(而非子阶段取消路径)触发组取消
            var a = new CancelBlindChildStage { Gate = new UniTaskCompletionSource() };
            var b = new FakeStage { Name = "b" };
            var stage = new SequenceStage(new IPipelineStage[] { a, b });

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
            Assert.AreEqual(1, a.ExecuteCount, "子阶段 a 应已启动");
            cts.Cancel();
            a.Gate.TrySetResult(); // 不响应取消的子阶段正常完成
            task.GetAwaiter().GetResult();

            Assert.AreEqual(0, b.ExecuteCount, "边界取消检查应阻止下一个子阶段启动");
            Assert.IsTrue(cancelled, "边界取消应走管线取消路径");
            Assert.IsNull(failedReason, "取消不得触发失败事件");
            Assert.IsFalse(completed, "取消不应触发完成事件");
        }

        #endregion

        #region 契约兜底

        [Test]
        public void ChildWithoutStateWrite_AutoCompletes()
        {
            var silent = new SilentChildStage();
            var stage = new SequenceStage(new[] { silent });

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
            var stage = new SequenceStage(new[] { new FakeStage { Weight = 2f }, new FakeStage { Weight = 3f } });
            Assert.AreEqual(5f, stage.Weight, 0.001f, "阶段权重应为子阶段权重之和");
        }

        [Test]
        public void EmptyChildren_Throws()
        {
            Assert.Throws<ArgumentException>(() => new SequenceStage(null), "null 子阶段列表应抛参数异常");
            Assert.Throws<ArgumentException>(() => new SequenceStage(new IPipelineStage[0]), "空子阶段列表应抛参数异常");
        }

        #endregion

        #region 嵌套组合

        [Test]
        public void SequenceInsideParallel_ExecutesSerialGroup()
        {
            var a = new FakeStage { Name = "a", Gate = new UniTaskCompletionSource() };
            var b = new FakeStage { Name = "b" };
            var c = new FakeStage { Name = "c" };
            // 组内先 a 后 b(串行子段),与 c 并行
            var stage = new ParallelStage(new IPipelineStage[]
            {
                new SequenceStage(new IPipelineStage[] { a, b }, "Seq"),
                c,
            }, "P");

            var pipeline = Pipeline.Create();
            pipeline.AddStage(stage);

            var task = pipeline.RunAsync();

            Assert.AreEqual(1, a.ExecuteCount, "串行子段 a 应已启动");
            Assert.AreEqual(0, b.ExecuteCount, "a 挂起时同组串行子段 b 不得启动");
            Assert.AreEqual(1, c.ExecuteCount, "并行兄弟 c 应与串行子段并行执行");

            a.Gate.TrySetResult();
            task.GetAwaiter().GetResult();

            Assert.AreEqual(1, b.ExecuteCount, "a 完成后串行子段 b 才启动");
        }

        [Test]
        public void ParallelInsideSequence_RunsGroupThenNext()
        {
            var a = new FakeStage { Name = "a", Gate = new UniTaskCompletionSource() };
            var b = new FakeStage { Name = "b" };
            var c = new FakeStage { Name = "c" };
            // 组内先并行组(a、b 并行),组沉降后 c 才执行
            var stage = new SequenceStage(new IPipelineStage[]
            {
                new ParallelStage(new IPipelineStage[] { a, b }, "P"),
                c,
            }, "Seq");

            var pipeline = Pipeline.Create();
            pipeline.AddStage(stage);

            var task = pipeline.RunAsync();

            Assert.AreEqual(1, a.ExecuteCount, "并行组 a 应已启动");
            Assert.AreEqual(1, b.ExecuteCount, "并行组 b 应与 a 并行启动");
            Assert.AreEqual(0, c.ExecuteCount, "并行组沉降前 c 不得执行(组间串行)");

            a.Gate.TrySetResult();
            task.GetAwaiter().GetResult();

            Assert.AreEqual(1, c.ExecuteCount, "并行组沉降后 c 才执行");
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

        /// <summary>
        /// 挂起但不响应取消的子阶段(放行后正常完成):验证循环边界取消检查
        /// (子阶段正常返回但令牌已取消 → 组走取消路径,不启动后续子阶段)。
        /// </summary>
        private sealed class CancelBlindChildStage : IPipelineStage
        {
            public string Name { get; set; } = "blind";

            public float Weight { get; set; } = 1f;

            public UniTaskCompletionSource Gate;

            public int ExecuteCount;

            public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
            {
                ExecuteCount++;
                context.SetState(PipelineStageState.Executing);
                if (Gate != null)
                    await Gate.Task; // 不响应取消
                context.SetState(PipelineStageState.Completed);
            }
        }
    }
}
