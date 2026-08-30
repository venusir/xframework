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
    /// Loader 外部取消测试:预取消 token 直接走取消终局、组间边界取消跳过后续组、取消后 settle 正常返回。
    /// <para>核心回归:取消绝不落入完成块(绝不触发 <see cref="ILoader.OnLoadCompleted"/>)。</para>
    /// </summary>
    class LoaderCancelTests
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

        #region 取消语义

        [Test]
        public void PreCancelledToken_TriggersCancelPath()
        {
            var fake = new FakeLoadable { Phase = 0 };
            var loader = new Loader();
            loader.AddLoadable(fake);
            string failedReason = null;
            bool completed = false;
            loader.OnLoadFailed += r => failedReason = r;
            loader.OnLoadCompleted += () => completed = true;
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Loader\] Load cancelled"));

            loader.LoadAsync(new CancellationToken(canceled: true)).GetAwaiter().GetResult();

            Assert.AreEqual(0, fake.LoadCount, "预取消时任务不应被调度");
            Assert.AreEqual("Load cancelled.", failedReason, "应走取消终局");
            Assert.IsFalse(completed, "取消不应触发完成事件");
            Assert.IsFalse(loader.IsLoading, "取消后 IsLoading 应复位");
        }

        [Test]
        public void CancelMidLoad_SkipsLaterPhases()
        {
            var gated = new FakeLoadable { Phase = 0, Gate = new UniTaskCompletionSource() };
            var late = new FakeLoadable { Phase = 1, Description = "late" };
            var (loader, pump) = CreateLoader();
            loader.AddLoadable(gated);
            loader.AddLoadable(late);
            string failedReason = null;
            bool completed = false;
            loader.OnLoadFailed += r => failedReason = r;
            loader.OnLoadCompleted += () => completed = true;
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Loader\] Load cancelled"));
            using var cts = new CancellationTokenSource();

            var task = loader.LoadAsync(cts.Token);
            Assert.AreEqual(1, gated.LoadCount, "首组任务应启动");

            cts.Cancel();
            for (int i = 0; i < 8 && pump.HasPending; i++) pump.Step();

            Assert.AreEqual(0, late.LoadCount, "取消后后续组不应启动");
            Assert.IsTrue(gated.LastToken.IsCancellationRequested, "当前组任务应收到已取消的 token");
            Assert.AreEqual("Load cancelled.", failedReason);
            Assert.IsFalse(completed, "取消不应触发完成事件");
            Assert.IsFalse(pump.HasPending, "取消后应沉降收敛");

            // 取消后 LoadAsync 应正常返回(settle 不抛),无在途任务泄漏
            task.GetAwaiter().GetResult();
            Assert.IsFalse(loader.IsLoading);
        }

        #endregion
    }
}
