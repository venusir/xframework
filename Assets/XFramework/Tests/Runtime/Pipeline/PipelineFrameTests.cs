using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XPipeline;

namespace XFramework.XPipeline.Tests
{
    /// <summary>
    /// Pipeline 的 <b>PlayMode 侧</b>覆盖：真跨帧与真墙钟。
    /// <para><b>为什么这三条只能在 PlayMode</b>：EditMode 没有 PlayerLoop 泵——① 阶段内的
    /// <c>await UniTask.Yield()</c> 不会让出到下一帧，「跨帧挂起」这个形态根本不存在；
    /// ② 超时的生产路径是 <c>UniTask.Delay(…, DelayType.Realtime)</c>，而 EditMode 用例必须注入
    /// 确定性计时器（其注释自述「真实延时由 PlayerLoop 泵送，永不触发」）——**生产的那条真实墙钟路径
    /// 只有 PlayMode 测得到**。</para>
    /// <para>本模块此前的用例全在 <c>Tests/Editor/</c>（2026-10-13 覆盖失衡盘点的处置之一）。</para>
    /// </summary>
    [TestFixture]
    public class PipelineFrameTests
    {
        #region 跨帧执行

        [Test]
        public async Task StageYieldingAcrossFrames_PipelineStaysRunning_ThenCompletes()
        {
            var stage = new FrameStage { Name = "跨帧", Frames = 3 };
            IPipeline pipeline = Pipeline.Create();
            pipeline.AddStage(stage);

            int progressUpdates = 0;
            pipeline.OnProgressUpdate += _ => progressUpdates++;

            UniTask run = pipeline.RunAsync();

            Assert.IsTrue(pipeline.IsRunning, "阶段挂起在第一帧，此刻管线应当仍在跑");

            await UniTask.Yield();
            Assert.IsTrue(pipeline.IsRunning, "跨帧期间应仍在运行——这条在 EditMode 里根本造不出来");

            await run;

            Assert.AreEqual(PipelineStatus.Completed, pipeline.Status);
            Assert.AreEqual(1, stage.ExecuteCount);
            Assert.Greater(progressUpdates, 0, "逐帧写入的进度应当被聚合广播出来");
        }

        [Test]
        public async Task ExternalCancel_DuringFrameSpanningStage_StopsThePipeline()
        {
            var hanging = new FrameStage { Name = "跨帧", Frames = 1000 };
            var later = new FrameStage { Name = "后续", Frames = 0 };
            IPipeline pipeline = Pipeline.Create();
            pipeline.AddStage(hanging);
            pipeline.AddStage(later);

            bool cancelled = false, completed = false, failed = false;
            pipeline.OnCancelled += () => cancelled = true;
            pipeline.OnCompleted += () => completed = true;
            pipeline.OnFailed += _ => failed = true;

            using var cts = new CancellationTokenSource();
            UniTask run = pipeline.RunAsync(cts.Token);

            await UniTask.Yield();
            Assert.IsTrue(pipeline.IsRunning, "前提校验：管线应当跑在挂起阶段里");

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] Pipeline cancelled"));
            cts.Cancel();
            await run;

            Assert.IsTrue(cancelled, "应当触发 OnCancelled");
            Assert.IsFalse(completed, "取消不得同时报完成");
            Assert.IsFalse(failed, "取消不得同时报失败");
            Assert.AreEqual(PipelineStatus.Cancelled, pipeline.Status);
            Assert.AreEqual(0, later.ExecuteCount, "取消后尚未开始的阶段不得执行");
        }

        [Test]
        public async Task RealClockTimeout_FailsTheHangingStage()
        {
            var hanging = new FrameStage { Name = "挂死", Gate = new UniTaskCompletionSource() };
            var later = new FrameStage { Name = "后续", Frames = 0 };
            IPipeline pipeline = Pipeline.Create();

            // 不注入 TimeoutTaskFactory：这里要跑的正是生产的那条真实墙钟路径
            pipeline.AddStage(hanging, 0.05f);
            pipeline.AddStage(later);

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed"));

            await pipeline.RunAsync();

            Assert.AreEqual(PipelineStatus.Failed, pipeline.Status);
            StringAssert.Contains("timed out after 0.05s", pipeline.FailureReason,
                "失败原因应带超时信息——它是使用方唯一的线索");
            Assert.AreEqual(0, later.ExecuteCount, "超时后尚未开始的阶段不得执行");
        }

        #endregion
    }
}
