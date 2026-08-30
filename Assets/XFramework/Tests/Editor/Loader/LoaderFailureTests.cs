using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XLoader;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// Loader 失败路径测试：异常任务转 Failed、取消兄弟任务、契约兜底补状态、死循环回归。
    /// <para>死循环类用例采用「有限帧收敛断言」：驱动 N 帧后要求帧泵无挂起——若回归死循环，测试失败而非挂死。</para>
    /// </summary>
    class LoaderFailureTests
    {
        #region Private Methods

        /// <summary>创建注入手动帧泵的 Loader（EditMode 无 PlayerLoop 泵，测试用 <see cref="TestFramePump"/> 确定性驱动轮询）。</summary>
        private static (Loader loader, TestFramePump pump) CreateLoader()
        {
            var pump = new TestFramePump();
            var loader = new Loader { _framePump = pump.Next };
            return (loader, pump);
        }

        #endregion

        #region 异常任务

        [Test]
        public void ThrowingTask_FailsAndCancelsSiblings()
        {
            var throwing = new FakeLoadable { Phase = 0, ThrowOnLoad = true, Description = "bad" };
            var sibling = new FakeLoadable { Phase = 0, Gate = new UniTaskCompletionSource() };
            var (loader, pump) = CreateLoader();
            loader.AddLoadable(throwing);
            loader.AddLoadable(sibling);

            string failedReason = null;
            bool completed = false;
            loader.OnLoadFailed += reason => failedReason = reason;
            loader.OnLoadCompleted += () => completed = true;
            LogAssert.Expect(LogType.Error, new Regex(@"\[Loader\] Load failed:"));

            var task = loader.LoadAsync();

            for (int i = 0; i < 8 && pump.HasPending; i++) pump.Step();

            Assert.IsFalse(pump.HasPending, "失败后应取消并沉降全部任务，不持续轮询");
            Assert.IsNotNull(failedReason, "应触发失败事件");
            StringAssert.Contains("boom", failedReason, "失败原因应包含异常消息");
            Assert.IsTrue(sibling.LastToken.IsCancellationRequested, "同 Phase 兄弟任务应收到已取消的 token");
            Assert.IsFalse(completed, "失败不应触发完成事件");
            Assert.IsFalse(loader.IsLoading, "失败后 IsLoading 应复位");
            task.GetAwaiter().GetResult();
        }

        #endregion

        #region 契约兜底（不写状态的任务）

        [Test]
        public void TaskWithoutStateWrites_IsAutoCompleted()
        {
            var fake = new FakeLoadable { Phase = 0, WriteState = false };
            var (loader, pump) = CreateLoader();
            loader.AddLoadable(fake);
            bool completed = false;
            loader.OnLoadCompleted += () => completed = true;

            var task = loader.LoadAsync();
            for (int i = 0; i < 16 && pump.HasPending; i++) pump.Step();

            Assert.IsFalse(pump.HasPending, "不写状态的任务应由包装函数补置终态，在有限帧内收敛（防死循环回归）");
            Assert.IsTrue(completed);
            task.GetAwaiter().GetResult();
        }

        [Test]
        public void GatedTaskWithoutStateWrites_ConvergesAfterRelease()
        {
            var fake = new FakeLoadable { Phase = 0, WriteState = false, Gate = new UniTaskCompletionSource() };
            var (loader, pump) = CreateLoader();
            loader.AddLoadable(fake);
            bool completed = false;
            loader.OnLoadCompleted += () => completed = true;

            var task = loader.LoadAsync();
            Assert.IsTrue(pump.HasPending, "任务挂起时应持续轮询");

            fake.Gate.TrySetResult();
            for (int i = 0; i < 16 && pump.HasPending; i++) pump.Step();

            Assert.IsFalse(pump.HasPending, "放行后应在有限帧内收敛（防死循环回归）");
            Assert.IsTrue(completed);
            task.GetAwaiter().GetResult();
        }

        #endregion
    }
}
