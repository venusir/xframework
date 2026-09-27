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
        public void AddStageWhileRunning_IsIgnored()
        {
            // 运行中 _contexts 已按启动时刻快照,追加阶段会使阶段列表与上下文数组错位(越界/语义未定义);
            // 守卫与 RunAsync 重入一致:打警告并忽略,被忽略的阶段不得在再次运行时执行
            var first = new FakeStage { Name = "first", Gate = new UniTaskCompletionSource() };
            var late = new FakeStage { Name = "late" };
            var pipeline = Pipeline.Create();
            pipeline.AddStage(first);

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] AddStage: already running"));
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] AddStage: already running"));
            var task = pipeline.RunAsync();
            pipeline.AddStage(late);     // 运行中追加 → 警告忽略
            pipeline.AddStage(late, 1f); // 重载同守卫,同样忽略

            Assert.AreEqual(0, late.ExecuteCount, "运行中添加的阶段不得被调度");
            first.Gate.TrySetResult();
            task.GetAwaiter().GetResult();

            pipeline.RunAsync().GetAwaiter().GetResult(); // 再次运行:被忽略的阶段不得出现
            Assert.AreEqual(0, late.ExecuteCount, "被忽略的阶段不得在再次运行时执行");
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
            // 瞬时阶段(Weight 0)不占进度——Bootstrap 启动管线中「登记与启动」两步的基石
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
        public void NaNProgress_IsSanitizedAtWriteBoundary()
        {
            // Clamp01 的两支比较对 NaN 均为 false,会把 NaN 原样放行;NaN 一旦进了加权和,全局进度变
            // NaN,而脏判定的 Abs(NaN - x) >= 0.01f 恒为 false —— ≥1% 节流从那一刻起对进度写入永久失效
            var stage = new GatedProgressStage();
            var (pipeline, progress) = CreateTrackedPipeline();
            pipeline.AddStage(stage);

            var task = pipeline.RunAsync();

            stage.Ctx.SetProgress(float.NaN);

            Assert.AreEqual(0f, stage.Ctx.Progress, 0.001f, "写入边界应把 NaN 归一为 0");

            stage.Gate.TrySetResult();
            task.GetAwaiter().GetResult();
        }

        [Test]
        public void NaNProgress_DoesNotKillThrottle()
        {
            // 写 NaN 本身不脏(快照赋值只发生在脏分支里),必须再写一次描述让脏判定成立,NaN 才落进
            // 快照——这一步正是污染进入广播的入口;此后 ≥1% 的步进若不再广播,即节流已被永久废掉
            var stage = new GatedProgressStage();
            var (pipeline, progress) = CreateTrackedPipeline();
            pipeline.AddStage(stage);

            var task = pipeline.RunAsync();

            stage.Ctx.SetProgress(float.NaN);
            stage.Ctx.SetDescription("poisoned");

            int countBefore = progress.Count;
            for (int i = 1; i <= 10; i++) stage.Ctx.SetProgress(0.05f * i);

            for (int i = 0; i < progress.Count; i++)
            {
                Assert.IsFalse(float.IsNaN(progress[i].OverallProgress), "NaN 不得进入广播快照");
            }
            Assert.GreaterOrEqual(progress.Count - countBefore, 5, "后续 ≥1% 步进应照常广播");

            stage.Gate.TrySetResult();
            task.GetAwaiter().GetResult();

            Assert.AreEqual(1f, progress[progress.Count - 1].OverallProgress, 0.001f, "终局广播必须为 1");
        }

        [Test]
        public void ReentrantWriteFromSubscriber_IsBoundedNotRecursed()
        {
            // 订阅者在广播里回写上下文(如 UI 反向驱动进度)会递归:写入 → 聚合 → 广播 → 订阅者 →
            // 写入 …。折叠后每次外部写入最多再补一次聚合,回写被限在个位数;不折叠则订户愿写多少层
            // 就递归多少层(这里给它 30 次机会,深度即 30 层栈)
            var stage = new GatedProgressStage();
            var (pipeline, progress) = CreateTrackedPipeline();
            pipeline.AddStage(stage);

            var task = pipeline.RunAsync();

            int writeBacks = 0;
            pipeline.OnProgressUpdate += _ =>
            {
                if (writeBacks >= 30) return;
                writeBacks++;
                stage.Ctx.SetDescription("w" + writeBacks);
            };

            stage.Ctx.SetDescription("trigger");

            Assert.LessOrEqual(writeBacks, 5, "重入写应被折叠为有限次,不得逐层递归");

            stage.Gate.TrySetResult();
            task.GetAwaiter().GetResult();
        }

        [Test]
        public void TerminalEvent_ThenWriteBack_DoesNotBroadcast()
        {
            // 终局块(取消终局)先重算快照广播、再触发事件,此刻 IsRunning 仍为 true —— 订阅者在事件
            // 里回写会重入整条聚合链并再广播一次,于是「OnCancelled 之后仍收到进度广播」。
            // 注:完成终局上回写不会脏(完成阶段不产出描述,且定 Completed 那次写入已把快照描述置空),
            // 故此处走取消终局——那是回写确实被判脏的路径。
            var stage = new GatedProgressStage();
            var (pipeline, progress) = CreateTrackedPipeline();
            pipeline.AddStage(stage);

            var cts = new CancellationTokenSource();
            int countAtTerminal = -1;
            pipeline.OnCancelled += () =>
            {
                countAtTerminal = progress.Count;
                stage.Ctx.SetDescription("after-terminal");
            };

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] Pipeline cancelled"));
            var task = pipeline.RunAsync(cts.Token);
            cts.Cancel();
            task.GetAwaiter().GetResult();

            Assert.Greater(countAtTerminal, 0, "取消终局应已广播过");
            Assert.AreEqual(countAtTerminal, progress.Count, "终局事件之后不得再有进度广播");
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

        [Test]
        public void RunningNoDescription_DoesNotBroadcastCompleted()
        {
            // 阶段只报进度不写描述:运行中(Executing、overall<1)广播不得出现终局文案 "Completed",
            // Description 应为空串占位(修复前:阶段启动即广播,空描述被占位为 "Completed" 泄漏到运行中)
            var stage = new ProgressOnlyGatedStage();
            var (pipeline, progress) = CreateTrackedPipeline();
            pipeline.AddStage(stage);

            var task = pipeline.RunAsync();

            Assert.Greater(progress.Count, 0, "阶段启动应产生广播");
            for (int i = 0; i < progress.Count; i++)
            {
                Assert.AreNotEqual("Completed", progress[i].Description,
                    "运行中(未完成)广播不得出现终局文案 Completed");
                Assert.AreEqual(ContextAggregation.RunningDescriptionPlaceholder, progress[i].Description,
                    "未写描述阶段的 Description 应为空串占位");
            }

            stage.Gate.TrySetResult();
            task.GetAwaiter().GetResult();

            Assert.AreEqual(1f, progress[progress.Count - 1].OverallProgress, 0.001f, "完成终局广播必须为 1");
            Assert.AreEqual("Completed", progress[progress.Count - 1].Description, "完成终局描述必须为 Completed");
        }

        #endregion

        #region 写入线程契约

        [Test]
        public void WorkerThreadWrite_LogsMainThreadError()
        {
            // 线程契约:阶段写入须与调度同一上下文(主线程);Editor 下越线程写入打 LogError
            // (复用 UniTask PlayerLoopHelper.IsMainThread;Join 阻塞主线程 → 无并发写,
            // 聚合在子线程内完成后才放行)
            var stage = new GatedProgressStage();
            var pipeline = Pipeline.Create();
            pipeline.AddStage(stage);

            LogAssert.Expect(LogType.Error,
                new Regex(@"\[Pipeline\] PipelineStageContext must be written from the Unity main thread"));
            var task = pipeline.RunAsync();

            var thread = new Thread(() => stage.Ctx.SetProgress(0.5f));
            thread.Start();
            thread.Join();

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

        /// <summary>
        /// 只报进度不写描述的挂起阶段:锁定运行中空描述占位语义(不得泄漏终局文案 "Completed")。
        /// </summary>
        private sealed class ProgressOnlyGatedStage : IPipelineStage
        {
            public string Name => "progress-only";

            public float Weight => 1f;

            public readonly UniTaskCompletionSource Gate = new UniTaskCompletionSource();

            public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
            {
                context.SetProgress(0f);
                await Gate.Task.AttachExternalCancellation(cancellationToken);
            }
        }

        #endregion
    }
}
