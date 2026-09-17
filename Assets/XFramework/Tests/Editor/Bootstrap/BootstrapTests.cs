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
    /// 以及「逆登记顺序清理 + 清理异常隔离」。
    /// <para>用纯 C# 假阶段驱动——不依赖任何具体模块，也不依赖节点系统。</para>
    /// </summary>
    class BootstrapTests
    {
        #region Test Doubles

        private sealed class FakeStage : IBootstrapStage
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
        }

        [TearDown]
        public void TearDown()
        {
            Bootstrap.Clear();
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

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Parallel stage failed:"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));

            var exception = Assert.Throws<InvalidOperationException>(
                () => Bootstrap.RunAsync().GetAwaiter().GetResult());

            StringAssert.Contains("启动失败", exception.Message);
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

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Parallel stage failed:"));
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

        #endregion

        #region 清理

        [Test]
        public void Shutdown_RunsInReverseRegistrationOrder()
        {
            var log = new List<string>();
            var a = NewStage("a", 0, log);
            var b = NewStage("b", 3, log);
            var c = NewStage("c", 4, log);
            c.ShutdownLog = log;
            b.ShutdownLog = log;
            a.ShutdownLog = log;

            Bootstrap.Register(a);
            Bootstrap.Register(b);
            Bootstrap.Register(c);

            Bootstrap.Shutdown();

            CollectionAssert.AreEqual(new[] { "c", "b", "a" }, log, "后初始化的先清理");
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
