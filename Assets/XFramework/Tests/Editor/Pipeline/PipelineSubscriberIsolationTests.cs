using System;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XPipeline;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// 订阅者异常隔离测试:事件订阅者(通常是 UI)抛出的异常不得冒泡进阶段栈。
    /// <para>为什么这是缺陷而非口味:事件在阶段写入栈内同步触发,订阅者抛出的异常会顺着
    /// <c>Broadcast → NotifyChanged → ExecuteAsync</c> 冒到 <c>StageExecution</c> 的捕获里——
    /// 于是 UI 的一个 bug 会把无辜阶段置 <see cref="PipelineStageState.Failed"/>;终局事件抛异常
    /// 的后果更重:<c>OnCompleted</c> 抛异常会把**成功报成失败**(冒到外层 catch 触发 <c>OnFailed</c>)、
    /// <c>OnCancelled</c> 抛异常会把**取消报成失败**、<c>OnFailed</c> 抛异常会让它自己触发两次并逃出
    /// <c>RunAsync</c>。同模块的 <c>StageAggregator</c> 早已为此加了 try/catch,管线侧是漏网的一半。</para>
    /// </summary>
    class PipelineSubscriberIsolationTests
    {
        #region 进度订阅者

        [Test]
        public void ThrowingProgressSubscriber_DoesNotFailTheRun()
        {
            // 只抛首次:一次就足以证明「异常不再传播」,而让每次广播都抛会把日志条数绑死在
            // 广播次数上(阶段起止、状态变化各触发一次),断言随之变脆
            var stage = new FakeStage { Name = "A", ProgressValue = 0.5f };
            var pipeline = Pipeline.Create();
            pipeline.AddStage(stage);

            int progressCount = 0;
            int failedCount = 0;
            bool completed = false;
            pipeline.OnProgressUpdate += _ =>
            {
                if (++progressCount == 1)
                    throw new InvalidOperationException("subscriber boom");
            };
            pipeline.OnFailed += _ => failedCount++;
            pipeline.OnCompleted += () => completed = true;

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] OnProgressUpdate subscriber threw"));
            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.Greater(progressCount, 1, "首次抛出后后续广播应照常送达(是隔离,不是中断)");
            Assert.IsTrue(completed, "进度订阅者抛异常不得改变运行结果");
            Assert.AreEqual(0, failedCount, "进度订阅者抛异常不得触发 OnFailed");
            Assert.AreEqual(1, stage.ExecuteCount);
        }

        #endregion

        #region 终局订阅者

        [Test]
        public void ThrowingCompletedSubscriber_DoesNotFireFailed()
        {
            var pipeline = Pipeline.Create();
            pipeline.AddStage(new FakeStage { Name = "A" });

            int failedCount = 0;
            bool completed = false;
            pipeline.OnCompleted += () =>
            {
                completed = true;
                throw new InvalidOperationException("completed boom");
            };
            pipeline.OnFailed += _ => failedCount++;

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] OnCompleted subscriber threw"));
            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.IsTrue(completed, "事件本身应已触发");
            Assert.AreEqual(0, failedCount, "完成订阅者抛异常不得把成功报成失败");
        }

        [Test]
        public void ThrowingCancelledSubscriber_KeepsCancelSemantics()
        {
            var pipeline = Pipeline.Create();
            pipeline.AddStage(new FakeStage { Name = "A" });

            int failedCount = 0;
            bool cancelled = false;
            pipeline.OnCancelled += () =>
            {
                cancelled = true;
                throw new InvalidOperationException("cancelled boom");
            };
            pipeline.OnFailed += _ => failedCount++;

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] OnCancelled subscriber threw"));
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] Pipeline cancelled"));
            pipeline.RunAsync(new CancellationToken(canceled: true)).GetAwaiter().GetResult();

            Assert.IsTrue(cancelled, "事件本身应已触发");
            Assert.AreEqual(0, failedCount, "取消订阅者抛异常不得把取消报成失败");
        }

        [Test]
        public void ThrowingCompletedSubscriber_OnEmptyPipeline_DoesNotEscape()
        {
            // 空阶段列表是唯一一条在 try 之外触发终局事件的分支,最容易漏掉隔离
            var pipeline = Pipeline.Create();

            bool completed = false;
            pipeline.OnCompleted += () =>
            {
                completed = true;
                throw new InvalidOperationException("empty boom");
            };

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] RunAsync: no stages found"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] OnCompleted subscriber threw"));
            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.IsTrue(completed, "事件本身应已触发");
        }

        [Test]
        public void ThrowingFailedSubscriber_FiresOnce_AndDoesNotEscape()
        {
            var pipeline = Pipeline.Create();
            pipeline.AddStage(new FakeStage { Name = "boom", ThrowOnExecute = true });

            int failedCount = 0;
            pipeline.OnFailed += _ =>
            {
                failedCount++;
                throw new InvalidOperationException("failed boom");
            };

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] OnFailed subscriber threw"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));
            pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, failedCount, "失败订阅者抛异常不得让它触发第二次");
        }

        #endregion
    }
}
