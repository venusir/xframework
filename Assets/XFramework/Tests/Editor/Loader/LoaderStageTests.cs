using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XLoader;
using XFramework.XPipeline;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// Loader 作为 <see cref="IPipelineStage"/> 的桥接测试:阶段进度转发、成功/失败/取消的终态映射。
    /// </summary>
    class LoaderStageTests
    {
        #region Private Methods

        /// <summary>创建注入手动帧泵的 Loader(EditMode 无 PlayerLoop 泵,测试用 <see cref="TestFramePump"/> 确定性驱动轮询)。</summary>
        private static (Loader loader, TestFramePump pump) CreateLoader()
        {
            var pump = new TestFramePump();
            var loader = new Loader { _framePump = pump.Next };
            return (loader, pump);
        }

        #endregion

        #region 桥接语义

        [Test]
        public void ExecuteAsync_ForwardsAggregatedProgress()
        {
            var fake = new FakeLoadable { Phase = 0, ProgressValue = 0.5f, Gate = new UniTaskCompletionSource() };
            var (loader, pump) = CreateLoader();
            loader.AddLoadable(fake);

            var pipeline = Pipeline.Create();
            var progress = new List<PipelineProgress>();
            pipeline.OnProgressUpdate += p => progress.Add(p);
            pipeline.AddStage(loader);

            var task = pipeline.RunAsync();

            // 首帧轮询:任务挂起于 0.5 → Loader 聚合 0.5 → 转发阶段上下文 → 管线级广播
            Assert.AreEqual(0.5f, progress[progress.Count - 1].OverallProgress, 0.001f, "阶段进度应转发 Loader 聚合进度");
            Assert.AreEqual(0.5f, loader._stageCtx.Progress, 0.001f, "阶段上下文进度应同步 Loader 聚合进度");
            Assert.IsTrue(pump.HasPending, "任务未完成时应持续轮询");

            fake.Gate.TrySetResult();
            for (int i = 0; i < 8 && pump.HasPending; i++) pump.Step();
            task.GetAwaiter().GetResult();

            Assert.AreEqual(1f, progress[progress.Count - 1].OverallProgress, 0.001f, "加载完成终局应广播 1");
        }

        [Test]
        public void ExecuteAsync_Success_StageCompleted()
        {
            var fake = new FakeLoadable { Phase = 0 };
            var (loader, _) = CreateLoader();
            loader.AddLoadable(fake);

            bool completed = false;
            var pipeline = Pipeline.Create();
            pipeline.OnCompleted += () => completed = true;
            pipeline.AddStage(loader);

            // 无 Gate:LoadAsync 同步完成,无帧泵挂起
            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(PipelineStageState.Completed, loader._stageCtx.State, "加载成功应置阶段 Completed");
            Assert.IsTrue(completed, "管线应触发完成事件");
        }

        [Test]
        public void ExecuteAsync_InternalFailure_StageFailed()
        {
            var fake = new FakeLoadable { Phase = 0, ThrowOnLoad = true };
            var (loader, _) = CreateLoader();
            loader.AddLoadable(fake);

            string failedReason = null;
            var pipeline = Pipeline.Create();
            pipeline.OnFailed += r => failedReason = r;
            pipeline.AddStage(loader);

            LogAssert.Expect(LogType.Error, new Regex(@"\[Loader\] Load failed:"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));
            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(PipelineStageState.Failed, loader._stageCtx.State, "加载失败应置阶段 Failed");
            StringAssert.Contains("boom", failedReason, "管线失败原因应携带任务异常消息");
        }

        [Test]
        public void ExecuteAsync_Cancellation_SettlesNormally()
        {
            var fake = new FakeLoadable { Phase = 0, Gate = new UniTaskCompletionSource() };
            var (loader, pump) = CreateLoader();
            loader.AddLoadable(fake);

            var cts = new CancellationTokenSource();
            string loaderFailedReason = null;
            string pipelineFailedReason = null;
            bool completed = false;
            loader.OnLoadFailed += r => loaderFailedReason = r;
            var pipeline = Pipeline.Create();
            pipeline.OnFailed += r => pipelineFailedReason = r;
            pipeline.OnCompleted += () => completed = true;
            pipeline.AddStage(loader);

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Loader\] Load cancelled"));
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] Pipeline cancelled"));
            var task = pipeline.RunAsync(cts.Token);
            cts.Cancel();

            // 取消后推帧:轮询退出 → Loader 取消终局正常返回 → ExecuteAsync 上抛 → 管线取消路径
            for (int i = 0; i < 8 && pump.HasPending; i++) pump.Step();
            task.GetAwaiter().GetResult();

            Assert.IsFalse(pump.HasPending, "取消后轮询应收敛");
            Assert.IsFalse(completed, "取消不应触发完成事件");
            Assert.AreEqual("Load cancelled.", loaderFailedReason, "Loader 内部取消语义保持");
            Assert.AreEqual("Pipeline cancelled.", pipelineFailedReason, "取消应经管线取消路径报告");
        }

        #endregion
    }
}
