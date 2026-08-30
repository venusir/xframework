using System.Collections.Generic;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XLoader;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// Loader 调度语义测试：Phase 分组、并行/串行、去重、空列表、防重入。
    /// </summary>
    class LoaderTests
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

        #region Phase 调度

        [Test]
        public void DifferentPhases_RunInOrder()
        {
            var log = new List<string>();
            var late = new FakeLoadable { Phase = 1, Description = "late", ExecutionLog = log };
            var early = new FakeLoadable { Phase = 0, Description = "early", ExecutionLog = log };
            var loader = new Loader();
            loader.AddLoadable(late);
            loader.AddLoadable(early);

            loader.LoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual(new List<string> { "early", "late" }, log, "不同 Phase 应按值从小到大串行执行");
        }

        [Test]
        public void SamePhase_StartInParallel()
        {
            var a = new FakeLoadable { Phase = 0, Gate = new UniTaskCompletionSource() };
            var b = new FakeLoadable { Phase = 0, Gate = new UniTaskCompletionSource() };
            var (loader, pump) = CreateLoader();
            loader.AddLoadable(a);
            loader.AddLoadable(b);

            var task = loader.LoadAsync();

            // 首帧轮询前两个任务都应已启动（并行启动），即使都未完成
            Assert.AreEqual(1, a.LoadCount, "同 Phase 任务应并行启动");
            Assert.AreEqual(1, b.LoadCount, "同 Phase 任务应并行启动");
            Assert.IsTrue(pump.HasPending, "任务未完成时应持续轮询");

            a.Gate.TrySetResult();
            b.Gate.TrySetResult();
            for (int i = 0; i < 8 && pump.HasPending; i++) pump.Step();

            Assert.IsFalse(pump.HasPending, "任务全部完成后应收敛");
            task.GetAwaiter().GetResult();
        }

        #endregion

        #region 注册与幂等

        [Test]
        public void AddLoadable_Duplicate_Ignored()
        {
            var fake = new FakeLoadable { Phase = 0 };
            var loader = new Loader();
            loader.AddLoadable(fake);
            loader.AddLoadable(fake);

            loader.LoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, fake.LoadCount, "重复添加同一任务应被去重");
        }

        [Test]
        public void AddLoadable_Null_Ignored()
        {
            var loader = new Loader();
            Assert.DoesNotThrow(() => loader.AddLoadable(null));
            Assert.IsFalse(loader.IsLoading);
        }

        [Test]
        public void EmptyEntries_FiresCompleted()
        {
            var loader = new Loader();
            bool completed = false;
            loader.OnLoadCompleted += () => completed = true;
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Loader\] LoadAsync: no loadable tasks found"));

            loader.LoadAsync().GetAwaiter().GetResult();

            Assert.IsTrue(completed, "空任务列表应直接触发完成事件");
            Assert.IsFalse(loader.IsLoading, "空列表不应置位 IsLoading");
        }

        [Test]
        public void LoadAsync_WhileLoading_Ignored()
        {
            var fake = new FakeLoadable { Phase = 0, Gate = new UniTaskCompletionSource() };
            var (loader, pump) = CreateLoader();
            loader.AddLoadable(fake);

            var task = loader.LoadAsync();

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Loader\] LoadAsync: already loading"));
            var second = loader.LoadAsync();
            second.GetAwaiter().GetResult();

            Assert.AreEqual(1, fake.LoadCount, "重复调用 LoadAsync 应被忽略");

            fake.Gate.TrySetResult();
            for (int i = 0; i < 8 && pump.HasPending; i++) pump.Step();
            task.GetAwaiter().GetResult();
        }

        #endregion
    }
}
