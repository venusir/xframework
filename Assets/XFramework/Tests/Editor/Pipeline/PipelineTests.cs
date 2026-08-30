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
    /// 管线调度核心测试:串行执行序、加权进度、阈值节流、终局广播、守卫分支。
    /// <para>管线为事件驱动(阶段写入同步聚合),EditMode 下无 PlayerLoop 泵亦可确定性推进——挂起用
    /// <see cref="UniTaskCompletionSource"/> 内联续体同步放行。</para>
    /// </summary>
    class PipelineTests
    {
        #region Private Methods

        /// <summary>广播序列中是否存在近似值(浮点容差比较)。</summary>
        private static bool HasApprox(List<float> list, float value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (Mathf.Abs(list[i] - value) <= 0.001f) return true;
            }
            return false;
        }

        /// <summary>抽取广播序列中的全局进度值。</summary>
        private static List<float> Broadcasts(List<PipelineProgress> progress)
        {
            var values = new List<float>(progress.Count);
            for (int i = 0; i < progress.Count; i++) values.Add(progress[i].OverallProgress);
            return values;
        }

        /// <summary>创建记录进度广播的管线。</summary>
        private static (IPipeline pipeline, List<PipelineProgress> progress) CreateTrackedPipeline()
        {
            var pipeline = Pipeline.Create();
            var progress = new List<PipelineProgress>();
            pipeline.OnProgressUpdate += p => progress.Add(p);
            return (pipeline, progress);
        }

        #endregion

        #region 串行调度

        [Test]
        public void Stages_RunInOrder()
        {
            var log = new List<string>();
            var pipeline = Pipeline.Create();
            pipeline.AddStage(new FakeStage { Name = "A", ExecutionLog = log });
            pipeline.AddStage(new FakeStage { Name = "B", ExecutionLog = log });

            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(new List<string> { "A", "B" }, log, "阶段应按添加顺序串行执行");
        }

        [Test]
        public void RunWhileRunning_Ignored()
        {
            var stage = new FakeStage { Gate = new UniTaskCompletionSource() };
            var pipeline = Pipeline.Create();
            pipeline.AddStage(stage);

            var first = pipeline.RunAsync();
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] RunAsync: already running"));
            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, stage.ExecuteCount, "重入调用不应再次调度阶段");

            stage.Gate.TrySetResult();
            first.GetAwaiter().GetResult();
        }

        [Test]
        public void EmptyPipeline_FiresCompleted()
        {
            var pipeline = Pipeline.Create();
            bool completed = false;
            pipeline.OnCompleted += () => completed = true;

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] RunAsync: no stages"));
            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.IsTrue(completed, "空管线应直接触发完成事件");
        }

        #endregion

        #region 进度模型

        [Test]
        public void WeightedProgress_ExactValue()
        {
            // A(权重 1,挂起于 0.5)→ 释放完成;B(权重 3,无 Gate 同步完成)
            var a = new FakeStage { Name = "A", Weight = 1f, ProgressValue = 0.5f, Gate = new UniTaskCompletionSource() };
            var b = new FakeStage { Name = "B", Weight = 3f };
            var (pipeline, progress) = CreateTrackedPipeline();
            pipeline.AddStage(a);
            pipeline.AddStage(b);

            var task = pipeline.RunAsync();
            Assert.IsTrue(HasApprox(Broadcasts(progress), 0.5f), "A 挂起时应广播其进度 0.5");

            a.Gate.TrySetResult();
            task.GetAwaiter().GetResult();

            // 阶段切换瞬间:已完成 A(w=1) + 执行中 B(w=3, p=0) → 1/4 = 0.25(执行中阶段按权重占比)
            Assert.IsTrue(HasApprox(Broadcasts(progress), 0.25f), "加权聚合:Σ(w·p)/Σ(w) = 1/4");
            Assert.AreEqual(1f, progress[progress.Count - 1].OverallProgress, 0.001f, "完成终局必须广播 1");
        }

        [Test]
        public void ZeroWeightStage_DoesNotAffectProgress()
        {
            // 瞬时阶段(Weight 0)不占进度——StartupAsync 预置管线的基石
            var instant = new FakeStage { Name = "Instant", Weight = 0f };
            var main = new FakeStage { Name = "Main", Weight = 1f, ProgressValue = 0.5f, Gate = new UniTaskCompletionSource() };
            var (pipeline, progress) = CreateTrackedPipeline();
            pipeline.AddStage(instant);
            pipeline.AddStage(main);

            var task = pipeline.RunAsync();

            Assert.IsTrue(HasApprox(Broadcasts(progress), 0f), "Weight 0 阶段执行期间全局进度应为 0");
            Assert.IsTrue(HasApprox(Broadcasts(progress), 0.5f), "Weight 0 阶段完成后全局进度只反映主阶段");

            main.Gate.TrySetResult();
            task.GetAwaiter().GetResult();
        }

        [Test]
        public void Completed_FinalBroadcast_IsOne()
        {
            var a = new FakeStage { ProgressValue = 0.5f, Gate = new UniTaskCompletionSource() };
            var (pipeline, progress) = CreateTrackedPipeline();
            pipeline.AddStage(a);

            bool completed = false;
            pipeline.OnCompleted += () => completed = true;

            var task = pipeline.RunAsync();
            Assert.AreEqual(0.5f, progress[progress.Count - 1].OverallProgress, 0.001f, "挂起阶段应广播当前进度");

            a.Gate.TrySetResult();
            task.GetAwaiter().GetResult();

            Assert.AreEqual(1f, progress[progress.Count - 1].OverallProgress, 0.001f, "完成终局必须广播 1");
            Assert.IsTrue(completed, "完成事件必须触发");
        }

        [Test]
        public void SmallSteps_AreThrottled()
        {
            // 1% 阈值节流:挂起中 10 次 0.001 步进写进度,应几乎不产生广播
            var stage = new GatedProgressStage();
            var (pipeline, progress) = CreateTrackedPipeline();
            pipeline.AddStage(stage);

            var task = pipeline.RunAsync();

            int countBefore = progress.Count;
            for (int i = 1; i <= 10; i++) stage.Ctx.SetProgress(0.001f * i);

            Assert.LessOrEqual(progress.Count - countBefore, 2, "10 次 0.001 步进最多触发 1 次阈值广播(累计 1% 边界)");

            stage.Gate.TrySetResult();
            task.GetAwaiter().GetResult();
            Assert.AreEqual(1f, progress[progress.Count - 1].OverallProgress, 0.001f, "终局广播不受节流限制");
        }

        [Test]
        public void DescriptionChange_ForcesBroadcast()
        {
            var stage = new GatedProgressStage();
            var (pipeline, progress) = CreateTrackedPipeline();
            pipeline.AddStage(stage);

            var task = pipeline.RunAsync();
            int countBefore = progress.Count;

            stage.Ctx.SetDescription("second");

            Assert.AreEqual(countBefore + 1, progress.Count, "描述变化必须强制广播");
            Assert.AreEqual("second", progress[progress.Count - 1].Description);

            stage.Gate.TrySetResult();
            task.GetAwaiter().GetResult();
        }

        [Test]
        public void StateChange_ForcesBroadcast()
        {
            var stage = new GatedProgressStage();
            var (pipeline, progress) = CreateTrackedPipeline();
            pipeline.AddStage(stage);

            var task = pipeline.RunAsync();
            int countBefore = progress.Count;

            stage.Ctx.SetState(PipelineStageState.Pending);

            Assert.AreEqual(countBefore + 1, progress.Count, "阶段状态变化必须强制广播");
            Assert.AreEqual(PipelineStageState.Pending, stage.Ctx.State);

            stage.Gate.TrySetResult();
            task.GetAwaiter().GetResult();
        }

        #endregion

        #region 测试辅助阶段

        /// <summary>
        /// 暴露 <see cref="PipelineStageContext"/> 的挂起阶段:测试直接写进度/描述/状态,验证阈值节流与强制广播。
        /// </summary>
        private sealed class GatedProgressStage : IPipelineStage
        {
            public string Name => "gated";
            public float Weight => 1f;

            public readonly UniTaskCompletionSource Gate = new UniTaskCompletionSource();

            public PipelineStageContext Ctx;

            public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
            {
                Ctx = context;
                context.SetDescription("gated");
                context.SetProgress(0f);
                await Gate.Task.AttachExternalCancellation(cancellationToken);
            }
        }

        #endregion
    }
}
