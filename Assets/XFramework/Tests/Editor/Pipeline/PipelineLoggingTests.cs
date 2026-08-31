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
    /// 管线阶段日志断言:顶层阶段开始/结束耗时日志(全部 Debug.Log 级,LogAssert 按序匹配)。
    /// <para>阶段经 <see cref="PipelineImpl.RunAsync"/> 循环统一埋点,四种结局(完成/失败/取消/超时)各有耗时日志。</para>
    /// </summary>
    class PipelineLoggingTests
    {
        [Test]
        public void Stage_LogsStartAndCompleted()
        {
            var stage = new FakeStage { Name = "A" };
            var pipeline = Pipeline.Create();
            pipeline.AddStage(stage);

            LogAssert.Expect(LogType.Log, new Regex(@"\[Pipeline\] Stage 'A' start"));
            LogAssert.Expect(LogType.Log, new Regex(@"\[Pipeline\] Stage 'A' completed in \d+ms"));
            pipeline.RunAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void FailedStage_LogsFailedWithElapsed()
        {
            var stage = new FakeStage { Name = "A", ThrowOnExecute = true };
            var pipeline = Pipeline.Create();
            pipeline.AddStage(stage);

            LogAssert.Expect(LogType.Log, new Regex(@"\[Pipeline\] Stage 'A' start"));
            LogAssert.Expect(LogType.Log, new Regex(@"\[Pipeline\] Stage 'A' failed in \d+ms"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));
            pipeline.RunAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void CancelledStage_LogsCancelledWithElapsed()
        {
            var a = new FakeStage { Name = "A", Gate = new UniTaskCompletionSource() };
            var pipeline = Pipeline.Create();
            pipeline.AddStage(a);

            LogAssert.Expect(LogType.Log, new Regex(@"\[Pipeline\] Stage 'A' start"));
            LogAssert.Expect(LogType.Log, new Regex(@"\[Pipeline\] Stage 'A' cancelled in \d+ms"));
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] Pipeline cancelled"));
            var cts = new CancellationTokenSource();
            var task = pipeline.RunAsync(cts.Token);
            cts.Cancel();
            task.GetAwaiter().GetResult();
        }

        [Test]
        public void TimedOutStage_LogsTimedOutWithElapsed()
        {
            var stage = new FakeStage { Name = "A", Gate = new UniTaskCompletionSource() };
            var pipeline = Pipeline.Create();
            pipeline.AddStage(stage, 1f);

            var timer = new UniTaskCompletionSource();
            ((PipelineImpl)pipeline).TimeoutTaskFactory = (_, _) => timer.Task;

            LogAssert.Expect(LogType.Log, new Regex(@"\[Pipeline\] Stage 'A' start"));
            LogAssert.Expect(LogType.Log, new Regex(@"\[Pipeline\] Stage 'A' timed out in \d+ms"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));
            var task = pipeline.RunAsync();
            timer.TrySetResult();
            task.GetAwaiter().GetResult();
        }
    }
}
