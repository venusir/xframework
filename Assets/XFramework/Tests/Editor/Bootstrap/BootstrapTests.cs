using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XBootstrap;
using XFramework.XPipeline;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// 启动引导模块测试：登记语义、相位分组执行、同相位并行、失败与取消的<b>抛出</b>语义、
    /// 以及「按执行序逆序清理 + 清理异常隔离」。
    /// <para>用纯 C# 假阶段驱动——不依赖任何具体模块，也不依赖节点系统。</para>
    /// </summary>
    class BootstrapTests
    {
        #region Test Doubles

        private class FakeStage : IBootstrapStage
        {
            public string StageName = "stage";
            public int StagePhase;
            public bool ThrowOnExecute;
            public bool ThrowOnShutdown;
            public UniTaskCompletionSource Gate;
            /// <summary>非 null 时：阶段启动后立即取消它，模拟「运行中取消」。</summary>
            public CancellationTokenSource CancelAfterStart;
            public List<string> Log;
            public List<string> ShutdownLog;

            public int ExecuteCount;

            public int Phase => StagePhase;

            public string Name => StageName;

            public float Weight => 1f;

            public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
            {
                ExecuteCount++;
                Log?.Add(StageName);

                if (CancelAfterStart != null)
                {
                    // 同步触发取消并立即观察：让整条链路在同步段内走完取消终局，
                    // 这样测试不必 await 一个未完成的 UniTask
                    CancelAfterStart.Cancel();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (Gate != null)
                {
                    await Gate.Task;
                }

                if (ThrowOnExecute)
                {
                    throw new InvalidOperationException($"{StageName} boom");
                }

                context.SetProgress(1f);
                context.SetState(PipelineStageState.Completed);
            }

            public void Shutdown()
            {
                ShutdownLog?.Add(StageName);

                if (ThrowOnShutdown)
                {
                    throw new InvalidOperationException($"{StageName} shutdown boom");
                }
            }
        }

        /// <summary><see cref="FakeStage"/> 的派生类：用于锁定「按精确类型注销不连带派生类」。</summary>
        private sealed class DerivedFakeStage : FakeStage
        {
        }

        private static FakeStage NewStage(string name, int phase, List<string> log = null)
        {
            return new FakeStage { StageName = name, StagePhase = phase, Log = log };
        }

        #endregion

        #region Setup / Teardown

        [SetUp]
        public void SetUp()
        {
            Bootstrap.Clear();
            // Clear 不清 _isRunning / 在途取消源；会话复位是兜底（internal，经 InternalsVisibleTo 可达）
            Bootstrap.ResetForNewSession();
        }

        [TearDown]
        public void TearDown()
        {
            Bootstrap.Clear();
            // 中止用例失败时会把在途标记或取消源留下，静默吞掉后续 fixture 的 RunAsync
            Bootstrap.ResetForNewSession();
        }

        #endregion

        #region 登记

        [Test]
        public void Register_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Bootstrap.Register(null));
        }

        [Test]
        public void Register_SameInstance_WarnsAndIgnores()
        {
            var stage = NewStage("a", 0);
            Bootstrap.Register(stage);

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Bootstrap\] 阶段 FakeStage 的同一实例已登记"));
            Bootstrap.Register(stage);

            Assert.AreEqual(1, Bootstrap.Stages.Count, "同一实例重复登记应被忽略");
        }

        [Test]
        public void Register_SameTypeDifferentInstances_BothRegistered()
        {
            // 登记表是「初始化步骤列表」而非「每类型一个的容器」：
            // 参数化的阶段用同一类型登记多次是合法的
            Bootstrap.Register(NewStage("a", 0));
            Bootstrap.Register(NewStage("b", 1));

            Assert.AreEqual(2, Bootstrap.Stages.Count);
        }

        [Test]
        public void Unregister_Instance_RemovesAndReturnsTrue()
        {
            var a = NewStage("a", 0);
            var b = NewStage("b", 0);
            Bootstrap.Register(a);
            Bootstrap.Register(b);

            Assert.IsTrue(Bootstrap.Unregister(a), "已登记的实例应被移除");
            Assert.AreEqual(1, Bootstrap.Stages.Count);
            Assert.AreSame(b, Bootstrap.Stages[0], "只移除目标实例");

            Assert.IsFalse(Bootstrap.Unregister(a), "不在表中的实例返回 false");
            Assert.AreEqual(1, Bootstrap.Stages.Count, "未命中不得改变登记表");
        }

        [Test]
        public void Unregister_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Bootstrap.Unregister(null));
        }

        [Test]
        public void UnregisterByType_ExactTypeOnly_DerivedSurvives()
        {
            var baseA = NewStage("baseA", 0);
            var baseB = NewStage("baseB", 0);
            var derived = new DerivedFakeStage { StageName = "derived", StagePhase = 0 };
            Bootstrap.Register(baseA);
            Bootstrap.Register(baseB);
            Bootstrap.Register(derived);

            Assert.AreEqual(2, Bootstrap.Unregister<FakeStage>(), "同类型多实例一次全部移除");
            Assert.AreEqual(1, Bootstrap.Stages.Count);
            Assert.AreSame(derived, Bootstrap.Stages[0], "派生类不被连带移除");
        }

        [Test]
        public void UnregisterByType_NoMatch_ReturnsZeroAndKeepsTable()
        {
            var a = NewStage("a", 0);
            Bootstrap.Register(a);

            Assert.AreEqual(0, Bootstrap.Unregister<DerivedFakeStage>());
            Assert.AreEqual(1, Bootstrap.Stages.Count);
            Assert.AreSame(a, Bootstrap.Stages[0]);
        }

        [Test]
        public void Clear_EmptiesRegistry()
        {
            Bootstrap.Register(NewStage("a", 0));
            Bootstrap.Clear();

            Assert.AreEqual(0, Bootstrap.Stages.Count);
        }

        [Test]
        public void RegisterDefaults_RegistersAssetDataSave_WithExpectedPhases()
        {
            Bootstrap.RegisterDefaults();

            Assert.AreEqual(3, Bootstrap.Stages.Count);

            var phases = new List<int>();
            for (int i = 0; i < Bootstrap.Stages.Count; i++)
            {
                phases.Add(Bootstrap.Stages[i].Phase);
            }

            // 登记顺序即相位顺序：Asset(0) → Data(3) → Save(4)
            CollectionAssert.AreEqual(new[] { 0, 3, 4 }, phases);
        }

        [Test]
        public void RegisterDefaults_CalledTwice_DoesNotDuplicate()
        {
            Bootstrap.RegisterDefaults();
            Bootstrap.RegisterDefaults();

            Assert.AreEqual(3, Bootstrap.Stages.Count, "重复调用 RegisterDefaults 不应叠加（按类型跳过已存在的内置阶段）");
        }

        #endregion

        #region 执行

        [Test]
        public void RunAsync_ExecutesStagesByPhaseOrder()
        {
            var log = new List<string>();

            // 故意乱序登记，验证执行序由 Phase 决定而非登记序
            Bootstrap.Register(NewStage("p4", 4, log));
            Bootstrap.Register(NewStage("p0", 0, log));
            Bootstrap.Register(NewStage("p3", 3, log));

            Bootstrap.RunAsync().GetAwaiter().GetResult();

            CollectionAssert.AreEqual(new[] { "p0", "p3", "p4" }, log, "应按相位升序串行执行");
        }

        [Test]
        public void RunAsync_NoStages_WarnsAndReturns()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Bootstrap\] 未登记任何引导阶段"));

            // 不抛异常：零配置项目本就不需要引导流程
            Bootstrap.RunAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void RunAsync_SamePhase_RunsConcurrently()
        {
            var a = NewStage("a", 0);
            a.Gate = new UniTaskCompletionSource();
            var b = NewStage("b", 0);
            b.Gate = new UniTaskCompletionSource();

            Bootstrap.Register(a);
            Bootstrap.Register(b);

            UniTask task = Bootstrap.RunAsync();

            Assert.AreEqual(1, a.ExecuteCount, "同相位阶段应并行启动");
            Assert.AreEqual(1, b.ExecuteCount, "同相位阶段不被兄弟挂起阻塞");

            a.Gate.TrySetResult();
            b.Gate.TrySetResult();
            task.GetAwaiter().GetResult();
        }

        [Test]
        public void RunAsync_StageThrows_ThrowsInvalidOperationException()
        {
            var bad = NewStage("bad", 0);
            bad.ThrowOnExecute = true;
            Bootstrap.Register(bad);

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Parallel stage failed: bad \(\d+\.\d+s\): bad boom"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));

            var exception = Assert.Throws<InvalidOperationException>(
                () => Bootstrap.RunAsync().GetAwaiter().GetResult());

            StringAssert.Contains("启动失败", exception.Message);
        }

        [Test]
        public void RunAsync_SecondCallWhileRunning_WarnsAndReturns()
        {
            var stage = NewStage("gated", 0);
            stage.Gate = new UniTaskCompletionSource();
            Bootstrap.Register(stage);

            UniTask first = Bootstrap.RunAsync();
            Assert.AreEqual(1, stage.ExecuteCount, "首次调用应已启动阶段");

            // 第二次调用不 await：断言的是「有没有再跑一遍」，而不是它何时返回
            // （旧实现会再装配一条管线、同步把 ExecuteCount 推到 2）
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Bootstrap\] RunAsync 已在运行中，忽略本次调用。"));
            Bootstrap.RunAsync();

            Assert.AreEqual(1, stage.ExecuteCount, "重入调用不得让同一批阶段跑两遍");

            stage.Gate.TrySetResult();
            first.GetAwaiter().GetResult();
        }

        [Test]
        public void RegistryMutation_WhileRunning_WarnsButProceeds()
        {
            var stage = NewStage("gated", 0);
            stage.Gate = new UniTaskCompletionSource();
            var late = NewStage("late", 5);
            Bootstrap.Register(stage);

            UniTask first = Bootstrap.RunAsync();
            Assert.AreEqual(1, stage.ExecuteCount, "首次调用应已启动阶段");

            // 运行中登记：本轮不执行（装配期已快照），但会被后续 Shutdown 扫到
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Bootstrap\] RunAsync 运行中登记阶段 FakeStage"));
            Bootstrap.Register(late);
            Assert.AreEqual(2, Bootstrap.Stages.Count, "警告不阻止登记");

            // 运行中注销：本轮仍会执行，但拿不到 Shutdown
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Bootstrap\] RunAsync 运行中注销阶段 FakeStage"));
            Assert.IsTrue(Bootstrap.Unregister(late));
            Assert.AreEqual(1, Bootstrap.Stages.Count, "警告不阻止注销");

            stage.Gate.TrySetResult();
            first.GetAwaiter().GetResult();
        }

        [Test]
        public void RegisterDefaults_WhileRunning_WarnsButProceeds()
        {
            var gated = NewStage("gated", 0);
            gated.Gate = new UniTaskCompletionSource();
            Bootstrap.Register(gated);

            UniTask run = Bootstrap.RunAsync();
            Assert.AreEqual(1, gated.ExecuteCount, "首次调用应已启动阶段");

            // 运行中 RegisterDefaults 与 Register 同一后果：本轮不执行、但会参与后续 Shutdown
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Bootstrap\] RunAsync 运行中调用 RegisterDefaults"));
            Bootstrap.RegisterDefaults();

            Assert.AreEqual(4, Bootstrap.Stages.Count, "警告不阻止登记：默认三件应被追加");

            gated.Gate.TrySetResult();
            run.GetAwaiter().GetResult();
        }

        [Test]
        public void Clear_WhileRunning_WarnsButProceeds()
        {
            var gated = NewStage("gated", 0);
            gated.Gate = new UniTaskCompletionSource();
            Bootstrap.Register(gated);

            UniTask run = Bootstrap.RunAsync();
            Assert.AreEqual(1, gated.ExecuteCount, "首次调用应已启动阶段");

            // 运行中 Clear 比 Unregister 更重：在途阶段仍执行，但全部拿不到 Shutdown
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Bootstrap\] RunAsync 运行中调用 Clear"));
            Bootstrap.Clear();

            Assert.AreEqual(0, Bootstrap.Stages.Count, "警告不阻止清空");

            gated.Gate.TrySetResult();
            run.GetAwaiter().GetResult();
        }

        [Test]
        public void RunAsync_StageThrows_ExceptionNamesTheFailedStage()
        {
            // 断言写全「名字 + 冒号」那一段：旧文案是 Failed: {异常消息}，没有名字段，
            // 故该断言在修复前必红（FakeStage 的消息里恰好带阶段名，不影响这一点）
            var bad = NewStage("offender", 0);
            bad.ThrowOnExecute = true;
            Bootstrap.Register(bad);

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Parallel stage failed: offender \(\d+\.\d+s\): offender boom"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));

            var exception = Assert.Throws<InvalidOperationException>(
                () => Bootstrap.RunAsync().GetAwaiter().GetResult());

            StringAssert.Contains("Failed: offender: offender boom", exception.Message,
                "启动失败异常要能指名是哪个引导阶段挂的");
        }

        [Test]
        public void RunAsync_PhaseFailure_StopsSubsequentPhases()
        {
            var log = new List<string>();
            var bad = NewStage("bad", 0, log);
            bad.ThrowOnExecute = true;
            var later = NewStage("later", 5, log);

            Bootstrap.Register(bad);
            Bootstrap.Register(later);

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Parallel stage failed: bad \(\d+\.\d+s\): bad boom"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));

            Assert.Throws<InvalidOperationException>(() => Bootstrap.RunAsync().GetAwaiter().GetResult());

            CollectionAssert.DoesNotContain(log, "later", "相位失败应中断后续阶段");
        }

        [Test]
        public void RunAsync_CancelledDuringStage_ThrowsOperationCanceledException()
        {
            var cts = new CancellationTokenSource();
            var stage = NewStage("cancelling", 0);
            stage.CancelAfterStart = cts;
            Bootstrap.Register(stage);

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] Pipeline cancelled"));

            Assert.Throws<OperationCanceledException>(
                () => Bootstrap.RunAsync(cancellationToken: cts.Token).GetAwaiter().GetResult());
        }

        /// <summary>
        /// <c>Shutdown</c> 中止在途运行的三方路径：<b>直接调 <c>RunAsync</c> 且不传令牌</b>——
        /// 中止信号经 Bootstrap 内部的链接令牌生效，不依赖调用方提供令牌。
        /// <para>钉住三件事：在途运行以 <see cref="OperationCanceledException"/> 收场（且携带的令牌
        /// <b>已取消</b>——调用方以 <c>IsCancellationRequested</c> 判断，不要比对令牌身份）；
        /// 后置相位不再执行（取消在阶段边界生效）；收尾后重入守卫已释放，下一次运行不受上一轮取消影响。</para>
        /// </summary>
        [Test]
        public void Shutdown_DuringInFlightRun_CancelsRunAndSkipsLaterPhases()
        {
            var gated = NewStage("gated", 0);
            gated.Gate = new UniTaskCompletionSource();
            var later = NewStage("later", 5);
            Bootstrap.Register(gated);
            Bootstrap.Register(later);

            UniTask run = Bootstrap.RunAsync();
            Assert.AreEqual(1, gated.ExecuteCount, "首次调用应已启动阶段");

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] Pipeline cancelled"));
            Bootstrap.Shutdown();

            // 放行不响应取消的在途阶段：整条链随之同步收敛
            gated.Gate.TrySetResult();

            var exception = Assert.Throws<OperationCanceledException>(() => run.GetAwaiter().GetResult());
            Assert.IsTrue(exception.CancellationToken.IsCancellationRequested,
                "Shutdown 触发的取消要携带已取消的令牌（本次运行的链接令牌）");
            Assert.AreEqual(0, later.ExecuteCount, "被中止的在途运行不得再执行后置相位");

            // 收尾后 _isRunning 应已释放：紧接的 RunAsync 必须真的重跑（取消只作用于被中止的那一轮）
            Bootstrap.RunAsync().GetAwaiter().GetResult();
            Assert.AreEqual(2, gated.ExecuteCount, "首轮结束后 _isRunning 应已释放——再次 RunAsync 必须真的重跑阶段");
            Assert.AreEqual(1, later.ExecuteCount, "未被中止的一轮里后置相位应正常执行");
        }

        /// <summary>
        /// 会话复位 × 在途运行：旧运行收尾时<b>不得</b>清掉新运行的重入守卫。
        /// <para><see cref="Bootstrap.ResetForNewSession"/> 允许新旧两次运行并存（旧运行的续体可能还活着）；
        /// 旧实现的 <c>finally</c> 无条件清 <c>_isRunning</c>，会把新运行正在持有的守卫一起清掉——
        /// 后续 <c>RunAsync</c> 被错误放行，同一批阶段并发跑两遍。代际号修复后，旧运行只清理自己的归属。</para>
        /// </summary>
        [Test]
        public void ResetForNewSession_DuringInFlightRun_OldRunDoesNotClearNewRunsGuard()
        {
            var oldStage = NewStage("old", 0);
            oldStage.Gate = new UniTaskCompletionSource();
            Bootstrap.Register(oldStage);

            UniTask oldRun = Bootstrap.RunAsync();
            Assert.AreEqual(1, oldStage.ExecuteCount, "前提：旧运行应已启动");

            // 会话边界：清表、取消旧运行、释放 _isRunning，允许新会话开始自己的运行
            Bootstrap.ResetForNewSession();

            var newStage = NewStage("new", 0);
            newStage.Gate = new UniTaskCompletionSource();
            Bootstrap.Register(newStage);

            UniTask newRun = Bootstrap.RunAsync();
            Assert.AreEqual(1, newStage.ExecuteCount, "前提：新运行应已启动");

            // 放行旧运行：它以 OCE 收场。旧实现的 finally 会在这里把新运行的重入守卫清掉。
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] Pipeline cancelled"));
            oldStage.Gate.TrySetResult();
            Assert.Throws<OperationCanceledException>(() => oldRun.GetAwaiter().GetResult());

            // 第三次调用必须仍被重入守卫忽略；若守卫被误清，它会真的重跑 newStage
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Bootstrap\] RunAsync 已在运行中，忽略本次调用。"));
            UniTask third = Bootstrap.RunAsync();

            Assert.AreEqual(1, newStage.ExecuteCount, "旧运行收尾不得清掉新运行持有的 _isRunning");

            newStage.Gate.TrySetResult();
            third.GetAwaiter().GetResult();
            newRun.GetAwaiter().GetResult();
        }

        #endregion

        #region 清理

        [Test]
        public void Shutdown_SamePhase_RunsInReverseRegistrationOrder()
        {
            var log = new List<string>();
            var a = NewStage("a", 0, log);
            var b = NewStage("b", 0, log);
            var c = NewStage("c", 0, log);
            c.ShutdownLog = log;
            b.ShutdownLog = log;
            a.ShutdownLog = log;

            Bootstrap.Register(a);
            Bootstrap.Register(b);
            Bootstrap.Register(c);

            Bootstrap.Shutdown();

            CollectionAssert.AreEqual(new[] { "c", "b", "a" }, log,
                "同相位内并行执行本无严格先后，取登记逆序作为确定性次序");
        }

        [Test]
        public void Shutdown_OutOfOrderRegistration_RunsInReversePhaseOrder()
        {
            var log = new List<string>();
            var p4 = NewStage("p4", 4, log);
            var p0 = NewStage("p0", 0, log);
            var p3 = NewStage("p3", 3, log);
            p4.ShutdownLog = log;
            p0.ShutdownLog = log;
            p3.ShutdownLog = log;

            // 故意乱序登记：执行序由 Phase 决定而非登记序（见 RunAsync_ExecutesStagesByPhaseOrder），
            // 清理序必须是执行序的逆序，而不是登记序的逆序
            Bootstrap.Register(p4);
            Bootstrap.Register(p0);
            Bootstrap.Register(p3);

            Bootstrap.Shutdown();

            CollectionAssert.AreEqual(new[] { "p4", "p3", "p0" }, log, "清理序应为执行序的逆序（相位降序）");
        }

        [Test]
        public void Shutdown_SingleStageThrows_OthersStillRun()
        {
            var log = new List<string>();
            var a = NewStage("a", 0, log);
            a.ShutdownLog = log;
            var bad = NewStage("bad", 3, log);
            bad.ShutdownLog = log;
            bad.ThrowOnShutdown = true;
            var c = NewStage("c", 4, log);
            c.ShutdownLog = log;

            Bootstrap.Register(a);
            Bootstrap.Register(bad);
            Bootstrap.Register(c);

            LogAssert.Expect(LogType.Error, new Regex(@"\[Bootstrap\] 阶段 FakeStage 的 Shutdown 抛异常"));

            Bootstrap.Shutdown();

            // 逆序：c → bad(抛) → a；bad 的异常不得阻断 a 的清理
            CollectionAssert.AreEqual(new[] { "c", "bad", "a" }, log);
        }

        #endregion
    }
}
