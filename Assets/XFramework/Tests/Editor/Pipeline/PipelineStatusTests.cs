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
    /// 终局拉取面测试:<see cref="IPipeline.Status"/> / <see cref="IPipeline.FailureReason"/>。
    /// <para>这是纯加法用例(改前这两个成员不存在,用例根本编译不过),因此<b>无法「改前必红」</b>——
    /// 如实记在此处,不假装有红灯。回归网是既有的 PipelineFailureTests / BootstrapTests(它们锁的
    /// 抛出类型与文案不变)与 <see cref="Bootstrap"/> 改为读拉取面后的行为一致。</para>
    /// </summary>
    class PipelineStatusTests
    {
        #region 状态迁移

        [Test]
        public void Idle_ThenCompleted_ThenIdleAfterDestroy()
        {
            var pipeline = Pipeline.Create();
            Assert.AreEqual(PipelineStatus.Idle, pipeline.Status, "创建后应为 Idle");
            Assert.IsFalse(pipeline.IsRunning, "Idle 与 IsRunning 应一致");
            Assert.IsNull(pipeline.FailureReason, "Idle 不得携带失败原因");

            pipeline.AddStage(new FakeStage { Name = "A" });
            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(PipelineStatus.Completed, pipeline.Status, "跑完应保持终局值,不回落 Idle");
            Assert.IsNull(pipeline.FailureReason);

            pipeline.Destroy();
            Assert.AreEqual(PipelineStatus.Idle, pipeline.Status, "销毁应回落 Idle");
            Assert.IsNull(pipeline.FailureReason, "销毁应清掉失败原因");
        }

        [Test]
        public void EmptyPipeline_LatchesCompleted()
        {
            var pipeline = Pipeline.Create();
            bool completed = false;
            pipeline.OnCompleted += () => completed = true;

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] RunAsync: no stages"));
            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.IsTrue(completed);
            Assert.AreEqual(PipelineStatus.Completed, pipeline.Status, "空管线照常触发 OnCompleted,状态应与之一致");
        }

        [Test]
        public void Running_ReadableFromInsideStage()
        {
            var stage = new StatusProbeStage();
            var pipeline = Pipeline.Create();
            stage.Pipeline = pipeline;
            pipeline.AddStage(stage);

            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(PipelineStatus.Running, stage.Observed, "阶段执行中应读到 Running");
            Assert.AreEqual(PipelineStatus.Completed, pipeline.Status);
        }

        #endregion

        #region 终局落位先于事件

        [Test]
        public void Failed_LatchesReason_ReadableInHandler()
        {
            var pipeline = Pipeline.Create();
            pipeline.AddStage(new FakeStage { Name = "boom", ThrowOnExecute = true });

            PipelineStatus statusInHandler = PipelineStatus.Idle;
            string reasonInHandler = null;
            pipeline.OnFailed += _ =>
            {
                statusInHandler = pipeline.Status;
                reasonInHandler = pipeline.FailureReason;
            };

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));
            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(PipelineStatus.Failed, pipeline.Status);
            StringAssert.Contains("boom", pipeline.FailureReason, "失败原因应携带阶段异常消息");
            Assert.AreEqual(PipelineStatus.Failed, statusInHandler, "落位先于事件:回调里应已能读到终局状态");
            Assert.AreEqual(pipeline.FailureReason, reasonInHandler, "回调里读到的原因应与事后一致");
        }

        [Test]
        public void Failed_ReasonNamesTheFailedStage()
        {
            var pipeline = Pipeline.Create();
            pipeline.AddStage(new FakeStage { Name = "offender", ThrowOnExecute = true });

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));
            pipeline.RunAsync().GetAwaiter().GetResult();

            // 格式：Failed: {最具体的名字}: {描述}——多阶段项目要能从拉取面（Bootstrap 据此抛出
            // 的异常）直接定位到是哪个阶段失败，而不只是知道「失败发生在管线里」
            StringAssert.Contains("Failed: offender: ", pipeline.FailureReason);
        }

        [Test]
        public void Cancelled_LatchesStatus()
        {
            var pipeline = Pipeline.Create();
            pipeline.AddStage(new FakeStage { Name = "A" });

            bool cancelled = false;
            pipeline.OnCancelled += () => cancelled = true;

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] Pipeline cancelled"));
            pipeline.RunAsync(new CancellationToken(canceled: true)).GetAwaiter().GetResult();

            Assert.IsTrue(cancelled);
            Assert.AreEqual(PipelineStatus.Cancelled, pipeline.Status);
            Assert.IsNull(pipeline.FailureReason, "取消不得留下失败原因(与 OnFailed 不触发一致)");
        }

        #endregion

        /// <summary>在执行中读取管线状态的阶段:验证运行中的 <see cref="IPipeline.Status"/>。</summary>
        private sealed class StatusProbeStage : IPipelineStage
        {
            public IPipeline Pipeline;

            public string Name => "probe";

            public float Weight => 1f;

            /// <summary>执行当时读到的状态。</summary>
            public PipelineStatus Observed;

            public UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
            {
                Observed = Pipeline.Status;
                return UniTask.CompletedTask;
            }
        }
    }
}
