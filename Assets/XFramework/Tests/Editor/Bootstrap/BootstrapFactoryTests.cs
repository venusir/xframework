using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using XFramework.XBootstrap;
using XFramework.XPipeline;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// <see cref="BootstrapStage.Create"/> 的委托语义：元数据、执行委托、异常传播与清理委托。
    /// </summary>
    class BootstrapFactoryTests
    {
        #region Setup / Teardown

        [SetUp]
        public void SetUp()
        {
            Bootstrap.Clear();
            Bootstrap.ResetForNewSession();
        }

        [TearDown]
        public void TearDown()
        {
            Bootstrap.Clear();
            Bootstrap.ResetForNewSession();
        }

        #endregion

        #region BootstrapStage.Create

        [Test]
        public void Create_NullName_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => BootstrapStage.Create(0, null, _ => UniTask.CompletedTask));
        }

        [Test]
        public void Create_NullExecute_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => BootstrapStage.Create(0, "stage", null));
        }

        [Test]
        public void Create_InvalidWeight_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => BootstrapStage.Create(0, "stage", _ => UniTask.CompletedTask, weight: -1f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => BootstrapStage.Create(0, "stage", _ => UniTask.CompletedTask, weight: float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => BootstrapStage.Create(0, "stage", _ => UniTask.CompletedTask, weight: float.PositiveInfinity));
        }

        [Test]
        public void Create_ProducesStageWithDeclaredMetadata()
        {
            var stage = BootstrapStage.Create(42, "my-stage", _ => UniTask.CompletedTask, weight: 2.5f);

            Assert.AreEqual(42, stage.Phase);
            Assert.AreEqual("my-stage", stage.Name);
            Assert.AreEqual(2.5f, stage.Weight, 0.001f);
        }

        [Test]
        public void Create_ExecuteInvokesDelegate_AndCompletesContext()
        {
            bool executed = false;
            CancellationToken observed = default;
            var stage = BootstrapStage.Create(7, "my-stage", ct =>
            {
                executed = true;
                observed = ct;
                return UniTask.CompletedTask;
            });

            var ctx = new PipelineStageContext();
            stage.ExecuteAsync(ctx, default).GetAwaiter().GetResult();

            Assert.IsTrue(executed, "ExecuteAsync 应调用委托");
            Assert.IsFalse(observed.IsCancellationRequested);
            Assert.AreEqual(PipelineStageState.Completed, ctx.State, "委托正常返回后应写完成终态");
            Assert.AreEqual(1f, ctx.Progress, 0.001f);
            Assert.AreEqual("my-stage", ctx.Description);
        }

        [Test]
        public void Create_ExecuteThrows_Propagates()
        {
            var stage = BootstrapStage.Create(0, "boom", _ => throw new InvalidOperationException("boom"));

            Assert.Throws<InvalidOperationException>(
                () => stage.ExecuteAsync(new PipelineStageContext(), default).GetAwaiter().GetResult());
        }

        [Test]
        public void Create_ShutdownInvokesDelegate()
        {
            bool shutdown = false;
            var stage = BootstrapStage.Create(0, "stage", _ => UniTask.CompletedTask, () => shutdown = true);

            stage.Shutdown();

            Assert.IsTrue(shutdown, "Shutdown 应调用调用方提供的清理委托");
        }

        [Test]
        public void Create_NullShutdown_IsSafe()
        {
            var stage = BootstrapStage.Create(0, "stage", _ => UniTask.CompletedTask);

            Assert.DoesNotThrow(() => stage.Shutdown());
        }

        [Test]
        public void Create_Registered_RunsThroughBootstrap()
        {
            bool executed = false;
            Bootstrap.Register(BootstrapStage.Create(42, "delegate",
                _ =>
                {
                    executed = true;
                    return UniTask.CompletedTask;
                }));

            Bootstrap.RunAsync().GetAwaiter().GetResult();

            Assert.IsTrue(executed, "委托阶段应能被登记表正常装配并执行");
        }

        #endregion
    }
}
