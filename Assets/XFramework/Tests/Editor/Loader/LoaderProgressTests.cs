using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XLoader;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// Loader 进度质量测试:组内加权聚合、失败不计入进度、1% 阈值节流、描述变化强制广播、终局必广播。
    /// </summary>
    class LoaderProgressTests
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

        #region 加权聚合与失败不计入

        [Test]
        public void WeightedPhaseProgress_ExactValue()
        {
            // 权重 1:3:挂起任务(weight 1,进度 0.5)+ 已完成任务(weight 3)
            var a = new FakeLoadable { Phase = 0, TaskWeight = 1f, ProgressValue = 0.5f, Gate = new UniTaskCompletionSource() };
            var b = new FakeLoadable { Phase = 0, TaskWeight = 3f };
            var (loader, pump) = CreateLoader();
            loader.AddLoadable(a);
            loader.AddLoadable(b);
            var broadcasts = new List<float>();
            loader.OnProgressUpdate += p => broadcasts.Add(p.OverallProgress);

            var task = loader.LoadAsync();

            // 首帧:组内加权 (1×0.5 + 3×1) / (1+3) = 0.875;单组 overall 即 0.875(算术平均为 0.75)
            Assert.AreEqual(1, broadcasts.Count, "首帧应广播一次");
            Assert.AreEqual(0.875f, broadcasts[0], 0.001f, "组内应加权聚合而非算术平均");

            a.Gate.TrySetResult();
            for (int i = 0; i < 8 && pump.HasPending; i++) pump.Step();

            Assert.IsFalse(pump.HasPending, "任务全部完成后应收敛");
            Assert.AreEqual(1f, broadcasts[broadcasts.Count - 1], 0.001f, "终局广播应为 1f");
            task.GetAwaiter().GetResult();
        }

        [Test]
        public void FailedTask_NotCountedInProgress()
        {
            // 挂起任务 weight 2 进度 0.5;失败任务 weight 1——失败帧进度 (2×0.5)/2 = 0.5(失败计入则 (1+1)/3 ≈ 0.667)
            var a = new FakeLoadable { Phase = 0, TaskWeight = 2f, ProgressValue = 0.5f, Gate = new UniTaskCompletionSource() };
            var bad = new FakeLoadable { Phase = 0, TaskWeight = 1f, ThrowOnLoad = true, Description = "bad" };
            var (loader, pump) = CreateLoader();
            loader.AddLoadable(a);
            loader.AddLoadable(bad);
            var broadcasts = new List<float>();
            loader.OnProgressUpdate += p => broadcasts.Add(p.OverallProgress);
            string failedReason = null;
            loader.OnLoadFailed += r => failedReason = r;
            LogAssert.Expect(LogType.Error, new Regex(@"\[Loader\] Load failed:"));

            var task = loader.LoadAsync();

            Assert.AreEqual(0.5f, broadcasts[broadcasts.Count - 1], 0.001f, "失败任务权重不应计入进度");
            for (int i = 0; i < 8 && pump.HasPending; i++) pump.Step();

            Assert.IsFalse(pump.HasPending, "失败后应沉降收敛");
            Assert.IsNotNull(failedReason, "应触发失败事件");
            task.GetAwaiter().GetResult();
        }

        [Test]
        public void Completed_FinalBroadcast_IsOne()
        {
            var a = new FakeLoadable { Phase = 0, ProgressValue = 0.5f, Gate = new UniTaskCompletionSource() };
            var (loader, pump) = CreateLoader();
            loader.AddLoadable(a);
            var broadcasts = new List<float>();
            loader.OnProgressUpdate += p => broadcasts.Add(p.OverallProgress);
            bool completed = false;
            loader.OnLoadCompleted += () => completed = true;

            var task = loader.LoadAsync();

            Assert.AreEqual(1, broadcasts.Count, "挂起期间无实质变化,不应反复广播");
            Assert.AreEqual(0.5f, broadcasts[0], 0.001f);

            a.Gate.TrySetResult();
            for (int i = 0; i < 8 && pump.HasPending; i++) pump.Step();

            Assert.IsTrue(completed, "应触发完成事件");
            Assert.AreEqual(1f, broadcasts[broadcasts.Count - 1], 0.001f, "终局必广播 1f,不受节流限制");
            Assert.IsFalse(pump.HasPending);
            task.GetAwaiter().GetResult();
        }

        #endregion

        #region 广播节流

        [Test]
        public void SmallSteps_AreThrottled()
        {
            var fake = new GatedProgressLoadable();
            var (loader, pump) = CreateLoader();
            loader.AddLoadable(fake);
            var broadcasts = new List<float>();
            loader.OnProgressUpdate += p => broadcasts.Add(p.OverallProgress);

            var task = loader.LoadAsync();
            int initial = broadcasts.Count; // 首帧广播

            // 每帧步进 0.001(低于 1% 阈值):10 帧累计 0.01,广播数必须显著小于帧数
            for (int i = 1; i <= 10; i++)
            {
                fake.Ctx.SetProgress(0.001f * i);
                pump.Step();
            }

            Assert.Less(broadcasts.Count - initial, 10, "步进低于阈值不应每帧广播(累计 0.01,最多跨阈一次)");
            Assert.IsTrue(pump.HasPending, "任务未放行应持续轮询");

            fake.Gate.TrySetResult();
            for (int i = 0; i < 8 && pump.HasPending; i++) pump.Step();

            Assert.IsFalse(pump.HasPending, "放行后应收敛");
            task.GetAwaiter().GetResult();
        }

        [Test]
        public void DescriptionChange_ForcesBroadcast()
        {
            var fake = new GatedProgressLoadable();
            var (loader, pump) = CreateLoader();
            loader.AddLoadable(fake);
            var broadcasts = new List<string>();
            loader.OnProgressUpdate += p => broadcasts.Add(p.Description);

            var task = loader.LoadAsync();
            Assert.AreEqual(1, broadcasts.Count, "首帧应广播");

            fake.Ctx.SetDescription("second");
            pump.Step();

            Assert.AreEqual(2, broadcasts.Count, "描述变化应强制广播(状态与进度均未变)");
            Assert.AreEqual("second", broadcasts[1]);

            fake.Gate.TrySetResult();
            for (int i = 0; i < 8 && pump.HasPending; i++) pump.Step();

            Assert.IsFalse(pump.HasPending);
            task.GetAwaiter().GetResult();
        }

        #endregion

        #region 测试辅助:可驱动进度的挂起任务

        /// <summary>暴露 LoadProgress 的挂起任务:测试可直接写进度/描述,验证阈值节流与强制广播。</summary>
        private sealed class GatedProgressLoadable : ILoadable
        {
            public int Phase => 0;

            public readonly UniTaskCompletionSource Gate = new UniTaskCompletionSource();

            /// <summary>Loader 注入的进度对象(LoadAsync 首行同步赋值,LoadAsync 返回后即可用)。</summary>
            public LoadProgress Ctx;

            public async UniTask LoadAsync(LoadProgress progress, CancellationToken cancellationToken)
            {
                Ctx = progress;
                progress.SetState(LoadState.Loading);
                progress.SetDescription("gated");
                progress.SetProgress(0f);
                await Gate.Task.AttachExternalCancellation(cancellationToken);
            }
        }

        #endregion
    }
}
