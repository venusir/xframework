using System;
using NUnit.Framework;
using XFramework.XLoader;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// LoadProgress 写后通知(门铃化)测试:四个写入入口触发回调,全局级字段写入不触发,
    /// 未注入回调(独立使用场景)时写入仅落字段、零行为变化。
    /// </summary>
    class LoadProgressChangedTests
    {
        #region 写后通知触发

        [Test]
        public void SetProgress_TriggersOnChanged()
        {
            var progress = new LoadProgress();
            int count = 0;
            progress.OnChanged = p =>
            {
                count++;
                Assert.AreSame(progress, p, "回调应收到触发自身的实例引用");
            };

            progress.SetProgress(0.5f);

            Assert.AreEqual(1, count, "写入应恰好触发一次通知");
            Assert.AreEqual(0.5f, progress.Progress, 0.001f, "值应先落字段再触发通知");
        }

        [Test]
        public void SetDescription_TriggersOnChanged()
        {
            var progress = new LoadProgress();
            int count = 0;
            progress.OnChanged = p => count++;

            progress.SetDescription("loading asset");

            Assert.AreEqual(1, count);
            Assert.AreEqual("loading asset", progress.Description);
        }

        [Test]
        public void SetState_TriggersOnChanged()
        {
            var progress = new LoadProgress();
            int count = 0;
            progress.OnChanged = p => count++;

            progress.SetState(LoadState.Completed);

            Assert.AreEqual(1, count);
            Assert.AreEqual(LoadState.Completed, progress.State);
        }

        [Test]
        public void SetWeight_TriggersOnChanged_AndClamps()
        {
            var progress = new LoadProgress();
            int count = 0;
            progress.OnChanged = p => count++;

            progress.SetWeight(0f);

            Assert.AreEqual(1, count);
            Assert.AreEqual(0.01f, progress.Weight, 0.001f, "权重下限 0.01 防除零");
        }

        #endregion

        #region 触发边界

        [Test]
        public void NoCallback_WriteSucceedsSilently()
        {
            // 独立使用场景(如 AssetManager 初始化进度):未注入回调,写入仅落字段、不抛异常
            var progress = new LoadProgress();

            Assert.DoesNotThrow(() =>
            {
                progress.SetWeight(2f);
                progress.SetProgress(0.5f);
                progress.SetDescription("init");
                progress.SetState(LoadState.Loading);
            });

            Assert.AreEqual(2f, progress.Weight, 0.001f);
            Assert.AreEqual(0.5f, progress.Progress, 0.001f);
        }

        [Test]
        public void GlobalFields_DoNotTrigger()
        {
            // 全局级字段由调度者聚合时填充(非任务写入),不应触发通知,避免自通知循环
            var progress = new LoadProgress();
            int count = 0;
            progress.OnChanged = p => count++;

            progress.OverallProgress = 0.5f;
            progress.CurrentTaskName = "Task";
            progress.TotalTaskCount = 3;
            progress.CompletedCount = 1;
            progress.FailedCount = 0;

            Assert.AreEqual(0, count, "全局级字段写入不得触发写后通知");
        }

        [Test]
        public void MultipleWrites_TriggerEachTime()
        {
            var progress = new LoadProgress();
            int count = 0;
            progress.OnChanged = p => count++;

            progress.SetProgress(0.1f);
            progress.SetProgress(0.2f);
            progress.SetDescription("step");
            progress.SetState(LoadState.Completed);

            Assert.AreEqual(4, count, "每次写入都应触发一次通知");
        }

        #endregion
    }
}
