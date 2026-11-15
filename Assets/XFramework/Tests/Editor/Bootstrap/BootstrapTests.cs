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

            /// <summary>置真时在阶段令牌上注册一个抛异常的取消回调（R3-BST-001 的隔离用例）。</summary>
            public bool RegisterThrowingCancellationCallback;

            /// <summary>Shutdown 收尾动作（可在其中按需改登记表）。</summary>
            public Action OnShutdown;

            public List<string> Log;
            public List<string> ShutdownLog;

            public int ExecuteCount;

            /// <summary>置真时 Phase getter 抛异常，用于验证 Shutdown 入口的隔离。</summary>
            public bool ThrowOnPhaseGet;

            /// <summary>Phase getter 被读取的次数（验证 Shutdown 只快照一次）。</summary>
            public int PhaseGetCount;

            public int Phase
            {
                get
                {
                    PhaseGetCount++;
                    if (ThrowOnPhaseGet)
                        throw new InvalidOperationException($"{StageName} phase boom");
                    return StagePhase;
                }
            }

            public string Name => StageName;

            public float Weight => 1f;

            public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
            {
                ExecuteCount++;
                Log?.Add(StageName);

                if (RegisterThrowingCancellationCallback)
                {
                    cancellationToken.Register(
                        () => throw new InvalidOperationException("cancel callback boom"));
                }

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
                OnShutdown?.Invoke();

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
        public void Shutdown_CancellationCallbackThrows_StillShutsDownAllStages()
        {
            var throwing = NewStage("throwing", 0);
            throwing.RegisterThrowingCancellationCallback = true;
            // 闸住 ExecuteAsync：_runCts 保持在途，Shutdown 的 Cancel 才会真正触发阶段令牌上的回调
            throwing.Gate = new UniTaskCompletionSource();
            var normal = NewStage("normal", 1);
            normal.ShutdownLog = new List<string>();

            Bootstrap.Register(throwing);
            Bootstrap.Register(normal);
            Bootstrap.RunAsync(); // 在途；令牌仍链接到 _runCts

            LogAssert.Expect(LogType.Error, new Regex("取消回调抛出异常"));
            Assert.DoesNotThrow(() => Bootstrap.Shutdown());

            CollectionAssert.Contains(normal.ShutdownLog, "normal",
                "取消回调异常不得跳过阶段清理（R3-BST-001）");

            throwing.Gate.TrySetResult();
        }

        [Test]
        public void Shutdown_StageClearsRegistry_AllStagesStillShutDown()
        {
            var first = NewStage("first", 0);
            first.ShutdownLog = new List<string>();
            var clearing = NewStage("clearing", 1);
            clearing.ShutdownLog = new List<string>();
            clearing.OnShutdown = Bootstrap.Clear;

            Bootstrap.Register(first);
            Bootstrap.Register(clearing);
            Bootstrap.RunAsync().GetAwaiter().GetResult();

            Assert.DoesNotThrow(() => Bootstrap.Shutdown());

            CollectionAssert.Contains(first.ShutdownLog, "first",
                "阶段 Shutdown 里 Clear 登记表不得跳过其余阶段（R3-BST-003）");
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

        [Test]
        public void Register_WithTimeout_StageRunsNormallyWhenNotTriggered()
        {
            var stage = NewStage("timed", 0);
            Bootstrap.Register(stage, 5f);

            Bootstrap.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, stage.ExecuteCount, "超时未触发时阶段应正常执行");
            Assert.AreEqual(BootstrapState.Completed, Bootstrap.State);
        }

        [Test]
        public void Register_WithNonFiniteTimeout_NormalizedToDisabled()
        {
            var stage = NewStage("timed", 0);
            Bootstrap.Register(stage, float.PositiveInfinity);

            // +∞ 若未归一，TimeSpan.FromSeconds 会溢出；持续挂起则会在沉降点被取消
            Bootstrap.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, stage.ExecuteCount);
            Assert.AreEqual(BootstrapState.Completed, Bootstrap.State);
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

        /// <summary>
        /// <see cref="Bootstrap.RunAsync"/> 把管线进度原样转交给调用方的
        /// <see cref="IProgress{PipelineProgress}"/>；用 <see cref="PipelineProgress"/> 自身作持有者，
        /// 结束后即可读到最终快照。
        /// </summary>
        [Test]
        public void RunAsync_ProgressRelay_WritesFinalSnapshotToHolder()
        {
            Bootstrap.Register(NewStage("progress", 0));

            var holder = new PipelineProgress();
            Bootstrap.RunAsync(holder).GetAwaiter().GetResult();

            Assert.AreEqual(1f, holder.OverallProgress, 0.001f, "完成后全局进度应为 1");
            Assert.AreEqual("Completed", holder.Description);
            Assert.AreEqual(1, holder.TotalStageCount, "一个相位组 = 管线顶层一个阶段");
            Assert.AreEqual(1, holder.CompletedStageCount);
            Assert.AreEqual(0, holder.FailedStageCount);
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
        public void RunAsync_SecondCallWhileRunning_JoinsFirstRun()
        {
            var stage = NewStage("gated", 0);
            stage.Gate = new UniTaskCompletionSource();
            Bootstrap.Register(stage);

            UniTask first = Bootstrap.RunAsync();
            Assert.AreEqual(1, stage.ExecuteCount, "首次调用应已启动阶段");

            // 第二次调用返回第一次运行的任务：不得再装配一条管线，也不得立即返回假装已就绪
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Bootstrap\] RunAsync 已在运行中，返回第一次运行的任务"));
            UniTask second = Bootstrap.RunAsync();

            Assert.AreEqual(1, stage.ExecuteCount, "重入调用不得让同一批阶段跑两遍");
            Assert.AreEqual(UniTaskStatus.Pending, second.Status, "重入返回的任务应仍在途，而不是立即完成");

            stage.Gate.TrySetResult();
            first.GetAwaiter().GetResult();
            Assert.DoesNotThrow(() => second.GetAwaiter().GetResult(),
                "重入调用应 join 第一次运行，并随它一起成功完成");
        }

        [Test]
        public void RunAsync_SecondCallWithToken_SecondTokenDoesNotParticipate()
        {
            var stage = NewStage("gated", 0);
            stage.Gate = new UniTaskCompletionSource();
            Bootstrap.Register(stage);

            UniTask first = Bootstrap.RunAsync();
            var cts = new CancellationTokenSource();

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Bootstrap\] RunAsync 已在运行中，返回第一次运行的任务"));
            UniTask second = Bootstrap.RunAsync(cancellationToken: cts.Token);

            // 第二次调用的令牌不参与本次运行：取消它不得中止第一次运行
            cts.Cancel();
            Assert.AreEqual(BootstrapState.Running, Bootstrap.State);

            stage.Gate.TrySetResult();
            first.GetAwaiter().GetResult();
            second.GetAwaiter().GetResult();
            Assert.AreEqual(BootstrapState.Completed, Bootstrap.State,
                "第二次调用的令牌被取消不应影响第一次运行");
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

            // 第三次调用必须仍被重入守卫 join 到新运行；若守卫被误清，它会真的重跑 newStage
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Bootstrap\] RunAsync 已在运行中，返回第一次运行的任务"));
            UniTask third = Bootstrap.RunAsync();

            Assert.AreEqual(1, newStage.ExecuteCount, "旧运行收尾不得清掉新运行持有的 _isRunning");

            newStage.Gate.TrySetResult();
            third.GetAwaiter().GetResult();
            newRun.GetAwaiter().GetResult();
        }

        #endregion

        #region 就绪面（State / WaitUntilReadyAsync / LastProgress）

        [Test]
        public void State_Transitions_ThroughRunLifecycle()
        {
            Assert.AreEqual(BootstrapState.Idle, Bootstrap.State, "初始状态应为 Idle");

            var stage = NewStage("gated", 0);
            stage.Gate = new UniTaskCompletionSource();
            Bootstrap.Register(stage);

            UniTask run = Bootstrap.RunAsync();
            Assert.AreEqual(BootstrapState.Running, Bootstrap.State, "在途运行应处于 Running");

            stage.Gate.TrySetResult();
            run.GetAwaiter().GetResult();
            Assert.AreEqual(BootstrapState.Completed, Bootstrap.State, "成功收敛后应为 Completed");
        }

        [Test]
        public void State_Failed_AfterStageThrows()
        {
            var bad = NewStage("bad", 0);
            bad.ThrowOnExecute = true;
            Bootstrap.Register(bad);

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Parallel stage failed: bad \(\d+\.\d+s\): bad boom"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));

            Assert.Throws<InvalidOperationException>(
                () => Bootstrap.RunAsync().GetAwaiter().GetResult());

            Assert.AreEqual(BootstrapState.Failed, Bootstrap.State, "失败收敛后应为 Failed");
        }

        [Test]
        public void State_Cancelled_AfterCancelledRun()
        {
            var cts = new CancellationTokenSource();
            var stage = NewStage("cancelling", 0);
            stage.CancelAfterStart = cts;
            Bootstrap.Register(stage);

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] Pipeline cancelled"));

            Assert.Throws<OperationCanceledException>(
                () => Bootstrap.RunAsync(cancellationToken: cts.Token).GetAwaiter().GetResult());

            Assert.AreEqual(BootstrapState.Cancelled, Bootstrap.State, "取消收敛后应为 Cancelled");
        }

        [Test]
        public void RunAsync_NoStages_StateCompletedAndProgressFilled()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Bootstrap\] 未登记任何引导阶段"));

            Bootstrap.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(BootstrapState.Completed, Bootstrap.State, "零阶段也是一次已完成的启动");
            Assert.IsNotNull(Bootstrap.LastProgress);
            Assert.AreEqual(1f, Bootstrap.LastProgress.OverallProgress, 0.001f,
                "没有管线广播时也应把快照补成完成态");
            Assert.AreEqual("Completed", Bootstrap.LastProgress.Description);
        }

        [Test]
        public void LastProgress_ReflectsFinalSnapshot_AndResetsWithSession()
        {
            Bootstrap.Register(NewStage("progress", 0));

            Bootstrap.RunAsync().GetAwaiter().GetResult();

            Assert.IsNotNull(Bootstrap.LastProgress, "运行开始时应建立快照持有者");
            Assert.AreEqual(1f, Bootstrap.LastProgress.OverallProgress, 0.001f);
            Assert.AreEqual("Completed", Bootstrap.LastProgress.Description);
            Assert.AreEqual(1, Bootstrap.LastProgress.CompletedStageCount);

            Bootstrap.ResetForNewSession();
            Assert.IsNull(Bootstrap.LastProgress, "会话复位应清空进度快照");
        }

        [Test]
        public void WaitUntilReady_Completed_ReturnsImmediately()
        {
            Bootstrap.Register(NewStage("done", 0));
            Bootstrap.RunAsync().GetAwaiter().GetResult();

            UniTask waiter = Bootstrap.WaitUntilReadyAsync();

            Assert.AreEqual(UniTaskStatus.Succeeded, waiter.Status, "Completed 状态应同步完成");
            waiter.GetAwaiter().GetResult();
        }

        [Test]
        public void WaitUntilReady_Running_CompletesWithRun()
        {
            var stage = NewStage("gated", 0);
            stage.Gate = new UniTaskCompletionSource();
            Bootstrap.Register(stage);

            UniTask run = Bootstrap.RunAsync();
            UniTask waiter = Bootstrap.WaitUntilReadyAsync();
            Assert.AreEqual(UniTaskStatus.Pending, waiter.Status, "运行在途时等待者不应提前完成");

            stage.Gate.TrySetResult();
            run.GetAwaiter().GetResult();
            waiter.GetAwaiter().GetResult();
        }

        [Test]
        public void WaitUntilReady_Failed_RethrowsRunException()
        {
            var bad = NewStage("bad", 0);
            bad.ThrowOnExecute = true;
            Bootstrap.Register(bad);

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Parallel stage failed: bad \(\d+\.\d+s\): bad boom"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));

            var runException = Assert.Throws<InvalidOperationException>(
                () => Bootstrap.RunAsync().GetAwaiter().GetResult());

            var waitedException = Assert.Throws<InvalidOperationException>(
                () => Bootstrap.WaitUntilReadyAsync().GetAwaiter().GetResult());

            Assert.AreSame(runException, waitedException,
                "WaitUntilReadyAsync 应重抛本次运行的同一个异常（含原始堆栈）");
        }

        [Test]
        public void WaitUntilReady_Cancelled_ThrowsOperationCanceled()
        {
            var cts = new CancellationTokenSource();
            var stage = NewStage("cancelling", 0);
            stage.CancelAfterStart = cts;
            Bootstrap.Register(stage);

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] Pipeline cancelled"));
            Assert.Throws<OperationCanceledException>(
                () => Bootstrap.RunAsync(cancellationToken: cts.Token).GetAwaiter().GetResult());

            Assert.Throws<OperationCanceledException>(
                () => Bootstrap.WaitUntilReadyAsync().GetAwaiter().GetResult(),
                "Cancelled 状态应重抛本次运行的取消异常");
        }

        [Test]
        public void WaitUntilReady_Idle_WaitsForNextRun()
        {
            UniTask waiter = Bootstrap.WaitUntilReadyAsync();
            Assert.AreEqual(UniTaskStatus.Pending, waiter.Status, "Idle 时应等待下一次运行");

            var stage = NewStage("gated", 0);
            stage.Gate = new UniTaskCompletionSource();
            Bootstrap.Register(stage);

            UniTask run = Bootstrap.RunAsync();
            Assert.AreEqual(UniTaskStatus.Pending, waiter.Status, "运行开始后应转为等待本次运行完成");

            stage.Gate.TrySetResult();
            run.GetAwaiter().GetResult();
            waiter.GetAwaiter().GetResult();
        }

        [Test]
        public void WaitUntilReady_Idle_Cancellable()
        {
            var cts = new CancellationTokenSource();
            UniTask waiter = Bootstrap.WaitUntilReadyAsync(cts.Token);

            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() => waiter.GetAwaiter().GetResult());
        }

        [Test]
        public void ResetForNewSession_CancelsIdleWaitersAndResetsState()
        {
            UniTask waiter = Bootstrap.WaitUntilReadyAsync();

            Bootstrap.ResetForNewSession();

            Assert.Throws<OperationCanceledException>(() => waiter.GetAwaiter().GetResult(),
                "会话复位应取消旧会话的 Idle 等待者");
            Assert.AreEqual(BootstrapState.Idle, Bootstrap.State);
        }

        [Test]
        public void Shutdown_WhenIdle_DoesNotChangeState()
        {
            Assert.AreEqual(BootstrapState.Idle, Bootstrap.State);

            Bootstrap.Shutdown();

            Assert.AreEqual(BootstrapState.Idle, Bootstrap.State, "Shutdown 不改就绪状态");
        }

        [Test]
        public void Shutdown_WhenCompleted_DoesNotChangeState()
        {
            Bootstrap.Register(NewStage("done", 0));
            Bootstrap.RunAsync().GetAwaiter().GetResult();

            Bootstrap.Shutdown();

            Assert.AreEqual(BootstrapState.Completed, Bootstrap.State,
                "Shutdown 只清理，不把已完成的状态改回 Idle");
        }

        [Test]
        public void FailedRun_ThenNewRun_StateReturnsToRunning()
        {
            var bad = NewStage("bad", 0);
            bad.ThrowOnExecute = true;
            Bootstrap.Register(bad);

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Parallel stage failed: bad \(\d+\.\d+s\): bad boom"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));
            Assert.Throws<InvalidOperationException>(
                () => Bootstrap.RunAsync().GetAwaiter().GetResult());
            Assert.AreEqual(BootstrapState.Failed, Bootstrap.State);

            Bootstrap.Clear();
            var gated = NewStage("gated", 0);
            gated.Gate = new UniTaskCompletionSource();
            Bootstrap.Register(gated);

            UniTask run = Bootstrap.RunAsync();
            Assert.AreEqual(BootstrapState.Running, Bootstrap.State,
                "再次运行应清除上一次失败状态并回到 Running");

            gated.Gate.TrySetResult();
            run.GetAwaiter().GetResult();
            Assert.AreEqual(BootstrapState.Completed, Bootstrap.State);
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
        public void Shutdown_PhaseGetterThrows_IsolatedAndCleanedLast()
        {
            var log = new List<string>();
            var bad = NewStage("bad", 0, log);
            bad.ThrowOnPhaseGet = true;
            bad.ShutdownLog = log;
            var normal = NewStage("normal", 5, log);
            normal.ShutdownLog = log;

            Bootstrap.Register(bad);
            Bootstrap.Register(normal);

            LogAssert.Expect(LogType.Error, new Regex("读取阶段 .*Phase 抛出异常"));

            Assert.DoesNotThrow(() => Bootstrap.Shutdown());

            CollectionAssert.AreEqual(new[] { "normal", "bad" }, log,
                "Phase 不可读的阶段按 int.MinValue 兜底，降序清理中最后执行");
            Assert.AreEqual(1, bad.PhaseGetCount, "Phase 应在 Shutdown 入口只快照一次");
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
